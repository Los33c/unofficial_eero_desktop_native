import AppKit
import Foundation
import SwiftUI

extension MainWindow {
    /// Shows sign-in details masked until the person reveals them; values are listed in the order given.
    func showCredentials(_ title: String, _ values: [(String, String)]) {
        var p: [Block] = [text(title, 24, "#17263C", true), text("Reveal or copy only when you need these details. Copied values remain in the Mac clipboard.", 13, "#64738A")]
        for (label, secret) in values { p.append(.view(SecretRevealRow(label: label, secret: secret))) }
        dialog(title, p).showDialog()
    }
    /// Dictionary form (C# Dictionary<string, string>): usernames and other identifiers first, then secrets.
    func showCredentials(_ title: String, _ values: [String: String]) {
        let secretWords = ["password", "secret", "key", "dataset", "token"]
        let ordered = values.sorted { a, b in
            let sa = secretWords.contains { a.key.lowercased().contains($0) }, sb = secretWords.contains { b.key.lowercased().contains($0) }
            return sa != sb ? !sa : a.key < b.key
        }.map { ($0.key, $0.value) }
        showCredentials(title, ordered)
    }
    func guardianCredentials() async throws {
        if preview { showCredentials("Sample Guardian sign-in", [("Guardian username", "Sample account"), ("Guardian password", "sample-only-password")]); return }
        if !(await confirm("Retrieve your Guardian sign-in details from eero? Use them only in Guardian's official app or website.")) { return }
        let data = try await api.send("POST", "/2.2/partners/guardian")
        showCredentials("Guardian sign-in", try EverydayContracts.guardianLogin(data))
    }
    func subscriptionOffersPage(_ data: Any?) {
        sub("Plans you can get for this network, current offers, and links to share a plan with someone. Final prices and terms are shown in eero's payment service.")
        if network == nil { group(nil, infoRow("No network selected", nil, "Choose a network at the top to see its plans and referrals.")); return }
        let plans = MainWindow.entries(data, "plans"); var planRows: [Row] = []
        for plan in plans {
            let id = J.s(plan, "id"); if id.isEmpty { continue }
            let interval = MainWindow.friendly(J.s(plan, "interval")), price = J.first(plan, "price", "display_price", "amount")
            planRows.append(settingRow(J.s(plan, "name").nullIfEmpty ?? "eero plan", (price.isEmpty ? "Price shown at checkout" : price) + (interval.isEmpty ? "" : " · " + interval), nil,
                rowActionAsync("Offers", { [self] in
                    if preview { throw AppError.invalidOperation("Sign in to retrieve current offers.") }
                    let offers = try await api.send("POST", "/2.2/premium/in_app_payments/offers/" + networkId, try EverydayContracts.offers([id]))
                    info("Available offers", J.obj("offers", MainWindow.entries(offers, "offers").map { J.obj("title", J.s($0, "title"), "ends_at", J.s($0, "ends_at")) as Any }))
                }),
                rowActionAsync("Share link", { [self] in
                    if preview { throw AppError.invalidOperation("Sign in to create a referral link.") }
                    let referral = try await api.send("PUT", "/2.2/subscription_referrals", try EverydayContracts.referral(networkId, id))
                    try showReferralLink(J.s(referral, "referral_url"))
                }),
                rowActionAsync("Email me", { [self] in
                    if preview { throw AppError.invalidOperation("Sign in to email a referral.") }
                    if !(await confirm("Ask eero to email this plan's referral link to your account email address?")) { return }
                    try await api.send("POST", "/2.2/subscription_referral/email", try EverydayContracts.referral(networkId, id)); setNotice("Referral email requested from eero.")
                })))
        }
        if planRows.isEmpty { planRows.append(infoRow("No plans available", nil, "eero didn't return any plans for this network. Your provider may manage your plan.")) }
        group("Plans", rows: planRows)
        group("Referrals",
            settingRow("Referral status", "See who used your referral links.", nil, rowActionAsync("View", { [self] in info("Subscription referrals", try await read(try featureUrl("managed/subscription_referrals"))) })),
            settingRow("Purchases", "Receipts and past purchases. Opens eero's payment service.", nil, rowActionAsync("View ↗", { [self] in try await openPaymentService("MANAGE_PURCHASES") })))
    }
    func showReferralLink(_ value: String) throws {
        let url = try ServiceLinks.eero(value).absoluteString
        let p: [Block] = [text("Subscription referral", 24, "#17263C", true), .view(CopyableValueField(value: url, label: "Subscription referral link")),
                          .buttons([localButton("Copy link", { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(url, forType: .string) }),
                                    localButton("Open referral", { [self] in guarded { try openServiceWindow("Subscription referral", url) } })])]
        dialog("Subscription referral", p).showDialog()
    }
    func guidedDiagnosticsPage(_ data: Any?) {
        sub("Pick what's wrong. eero checks your network for that problem; results take a minute or two.")
        let labels = ["My internet is slow", "My internet is offline", "My connection keeps dropping", "My eero lights are red"]
        let helps = ["Checks your ISP speed, wifi signal and busy devices.", "Checks the modem, cables and your ISP.", "Checks signal strength and eero placement.", "Checks which eero has a problem and why."]
        let symptoms = ["internet_slow", "internet_offline", "connection_drops", "eeros_red"]
        var rows: [Row] = []
        for i in 0..<labels.count {
            let symptom = symptoms[i]
            rows.append(settingRow(labels[i], helps[i], nil, rowActionAsync("Check", { [self] in
                if preview { throw AppError.invalidOperation("Sign in to run network health checks.") }
                try await api.send("POST", try netResource("diagnostics"), try EverydayContracts.symptom(symptom))
                setNotice("Checking your network. Choose Show results in a minute.")
            })))
        }
        group("What's wrong?", rows: rows)
        let status = J.s(data, "status"), area = J.first(data, "issue_area", "issue")
        let verdict = data == nil ? "No check run yet" : status == "completed" || status == "complete" ? (area.isEmpty || area == "none" ? "No problems found" : "Problem found: " + MainWindow.friendly(area)) : !status.isEmpty ? MainWindow.friendly(status) : "Waiting for results"
        group("Results",
            settingRow("Latest result", data == nil ? "Run a check above, then show the results." : "Choose Show results again to get the newest result.", verdict,
                rowActionAsync("Show results", { [self] in featureData[currentPage] = try await read(try netResource("diagnostics")) ?? JNull.shared; renderPage() })))
        technicalDetails(data)
    }
    func deviceConnectionPage() {
        sub("Some smart-home devices can only join 2.4 GHz wifi during setup. Hide 5 GHz for a while, set up the device, then turn 5 GHz back on.")
        let flag = J.get(network, "temporary_flags", "hide_5g")
        let hidden = (flag as? Bool) ?? J.b(flag, "value")
        let until = J.first(flag, "expires_at", "expiration", "expires")
        group(nil, settingToggle("Hide 5 GHz for device setup", hidden ? "5 GHz is hidden" + (until.isEmpty ? "" : " until " + until) + ". Turn this off when your device is connected." : "Other devices may reconnect briefly. eero turns 5 GHz back on automatically after a while.", hidden, { [self] on in
            if on, !(await confirm("Hide 5 GHz on this network for now? Wireless devices may reconnect while you set up your device.")) { return }
            if on { try await change("PUT", try featureUrl("temporary_flags/hide_5g"), J.obj("value", true), true, "5 GHz hidden for device setup.") }
            else { try await change("DELETE", try featureUrl("temporary_flags/hide_5g"), nil, false, "5 GHz is back on.") }
        }))
        addGroupHeading("Still can't connect?")
        page.append(rowGroup(FeatureCatalog.entries.filter { $0.path == "Settings/Advanced networking/Wireless settings" }.map { listRow($0, false) }))
    }
    func fastRoamingPage(_ data: Any?) {
        sub("Helps phones and laptops move between eeros without dropping calls or video (802.11r).")
        if !(J.get(data, "fast_transition") is Bool) { page.append(card([text("eero didn't report a fast roaming setting for this network, so it can't be changed here.", 14, "#64738A")], 18)); return }
        group(nil, settingToggle("Fast roaming", "Turn off if an older device keeps disconnecting or can't join.", J.b(data, "fast_transition"), { [self] on in
            if !(await confirm((on ? "Turn on" : "Turn off") + " fast roaming? Some wireless devices may reconnect.")) { return }
            try await change("PUT", try featureUrl("fast_transition"), J.obj("fast_transition", on), false, "Fast roaming " + (on ? "turned on." : "turned off."))
        }))
        technicalDetails(data)
    }
    func backupDiscoveryPage(_ data: Any?) {
        sub("Find wifi networks your eero can reach and use one as a backup when your internet goes down.")
        let status = J.s(data, "status")
        group(nil, settingRow("Search for networks", "Your eero looks for nearby wifi. This takes about a minute; then choose Show results.", data == nil ? "Not searched yet" : MainWindow.friendly(status),
            rowActionAsync("Search", { [self] in
                if preview { throw AppError.invalidOperation("Sign in to scan for backup networks.") }
                try await api.send("POST", try featureUrl("backup_access_points/ssid_discovery")); setNotice("Searching for nearby wifi. Choose Show results in a minute.")
            }),
            rowActionAsync("Show results", { [self] in featureData[currentPage] = try await read(try featureUrl("backup_access_points/ssid_discovery")) ?? JNull.shared; renderPage() })))
        var rows: [Row] = []
        let rssiOf: (Any) -> Int = { Int(J.s($0, "rssi").trim()) ?? -200 }
        let found = MainWindow.entries(J.get(data, "discovered_ssids")).enumerated().sorted { a, b in
            let ra = rssiOf(a.element), rb = rssiOf(b.element); return ra != rb ? ra > rb : a.offset < b.offset
        }.map { $0.element }
        for item in found {
            let ssid = J.s(item, "ssid")
            let signal: String
            if let rssi = Int(J.s(item, "rssi").trim()) { signal = (rssi >= -60 ? "Strong signal" : rssi >= -70 ? "Good signal" : rssi >= -80 ? "Weak signal" : "Very weak signal") + " (\(rssi) dBm)" }
            else { signal = "Signal unknown" }
            rows.append(settingRow(ssid.nullIfEmpty ?? "Hidden network", signal, nil, rowAction("Use as backup", { [self] in backupEdit(nil, ssid) })))
        }
        if data != nil && rows.isEmpty { rows.append(infoRow("No networks found yet", nil, "Search, wait a minute, then choose Show results.")) }
        group("Networks found", rows: rows)
    }
    func backupPriority(_ networks: [Any]) {
        guarded {
            if networks.count < 2 { throw AppError.invalidOperation("Save at least two backup wifi networks to choose their priority.") }
            let ref = DialogRef()
            let p: [Block] = [text("Backup wifi priority", 24, "#17263C", true), text("Move the preferred backup to the top. All saved backups stay in the order.", 14, "#64738A"),
                              .view(BackupPriorityPanel(items: networks, ref: ref, save: { [self] ordered in
                                  if preview { throw AppError.invalidOperation("Preview mode is read only.") }
                                  try await change("POST", try featureUrl("backup_access_points/rearrange"), try EverydayContracts.backupOrder(ordered.map { J.s($0, "uuid") }), false, "Backup priority saved.")
                                  ref.handle?.canClose = true; ref.handle?.close()
                              }))]
            ref.handle = dialog("Backup priority", p)
            ref.handle?.showDialog()
        }
    }
    func amazonHomePage() {
        sub("Use your eero with Amazon smart-home devices and Alexa.")
        let linked = J.b(network, "amazon_account_linked")
        group("Amazon account",
            settingRow("Amazon account", linked ? "Your Amazon account is linked to this network." : "Link an Amazon account in the official eero app. New links can't be made here.", linked ? "Linked" : "Not linked",
                linked ? rowActionAsync("Unlink", { [self] in
                    if !(await confirm("Unlink the Amazon account from this network? Connected-home integrations may stop working.")) { return }
                    try await change("POST", "/2.2/amazon/networks/" + networkId + "/account_unlink", nil, false, "Amazon account unlinked.")
                }, true) : nil),
            J.get(network, "alexa_skill") is Bool ? infoRow("Alexa skill", J.b(network, "alexa_skill") ? "Enabled" : "Not enabled") : nil)
        group("Zigbee devices", settingRow("Find Zigbee devices", linked ? "Put the device in pairing mode first. It then shows up in the Amazon Alexa app." : "Needs a linked Amazon account and an eero that works as a smart-home hub.", nil,
            linked ? rowActionAsync("Find devices", { [self] in
                if !(await confirm("Start looking for nearby Zigbee devices? Put the device you want to add in pairing mode first.")) { return }
                try await change("POST", "/2.3/networks/" + networkId + "/shh/start_zigbee_discovery", nil, false, "Looking for Zigbee devices. Check the Amazon Alexa app for new devices.")
            }) : nil))
        relatedPagesFor("Settings/Connected services/Connected device setup")
    }
}

/// One credential: masked until revealed, with Reveal/Hide and Copy.
struct SecretRevealRow: View {
    let label: String; let secret: String
    @State private var shown = false
    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(label).font(.system(size: 14, weight: .semibold))
            Group {
                if shown { TextField("", text: .constant(secret)).textSelection(.enabled) }
                else { SecureField("", text: .constant(secret)).disabled(true) }
            }
            .textFieldStyle(.roundedBorder).frame(maxWidth: 520).accessibilityLabel(label)
            HStack(spacing: 8) {
                Button(shown ? "Hide" : "Reveal") { shown.toggle() }.buttonStyle(.bordered)
                Button("Copy") { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(secret, forType: .string) }.buttonStyle(.bordered)
            }
        }
        .padding(.bottom, 16)
        .onDisappear { shown = false }
    }
}

/// Reorderable list of saved backup wifi networks (highest priority first).
struct BackupPriorityPanel: View {
    let ref: DialogRef
    let save: ([Any]) async throws -> Void
    @State private var ordered: [Any]
    @State private var selected: Int? = 0
    @State private var status = ""
    @State private var working = false
    init(items: [Any], ref: DialogRef, save: @escaping ([Any]) async throws -> Void) {
        self.ref = ref; self.save = save; _ordered = State(initialValue: items)
    }
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Backup wifi (highest priority first)").font(.system(size: 13, weight: .semibold)).foregroundColor(Tone.muted.color).padding(.top, 8)
            List(selection: $selected) {
                ForEach(Array(ordered.enumerated()), id: \.offset) { i, item in
                    Text("\(i + 1). " + (J.s(item, "ssid").nullIfEmpty ?? "Backup wifi")).tag(Optional(i))
                }
            }
            .frame(height: CGFloat(min(max(ordered.count, 2), 8)) * 28 + 12)
            .accessibilityLabel("Backup wifi (highest priority first)")
            HStack(spacing: 8) {
                Button("Move up") { move(-1) }.buttonStyle(.bordered)
                Button("Move down") { move(1) }.buttonStyle(.bordered)
            }
            if !status.isEmpty { Text(status).font(.system(size: 13)).foregroundColor(Tone.danger.color).fixedSize(horizontal: false, vertical: true) }
            Button("Save priority") { commit() }.buttonStyle(.borderedProminent).disabled(working)
        }
    }
    private func move(_ offset: Int) {
        guard let from = selected else { return }
        let to = from + offset
        if from < 0 || to < 0 || to >= ordered.count { return }
        let item = ordered.remove(at: from); ordered.insert(item, at: to); selected = to
    }
    private func commit() {
        if working { return }
        working = true; status = ""
        let list = ordered
        Task { @MainActor in
            ref.handle?.canClose = false
            do { try await save(list) } catch { status = MainWindow.message(error) }
            working = false; ref.handle?.canClose = true
        }
    }
}
