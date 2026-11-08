import AppKit
import Foundation
import SwiftUI
import UniformTypeIdentifiers

extension MainWindow {
    /// Runs after the editor dialog that called it has closed (WPF Dispatcher.BeginInvoke).
    func afterEditor(_ action: @escaping () async -> Void) { Task { @MainActor in await action() } }
    var networkId: String { (try? ExtendedContracts.identifier((networkUrl ?? "").trimEnd("/").split("/").last ?? "")) ?? "" }
    func accountOnlyPage(_ route: String) -> Bool { MainWindow.accountOnlyPage(route) }
    static func accountOnlyPage(_ route: String) -> Bool {
        return route == "Settings/Notifications/Desktop notifications" || route == "Help" || route.hasPrefix("Help/") || route == "Settings" || route == "Settings/Appearance" || route == "Settings/About" || route.hasPrefix("Settings/Account settings") || route == "Settings/Network management" || route == "Settings/Network management/Pending transfers" || route == "Settings/User permissions/Join a network" || route == "Settings/User permissions/Incoming access invitations" || route == "Home/Add or replace eero" || route == "Home/Add or replace eero/Find nearby eeros" || route == "Home/Add or replace eero/Create a network"
    }
    func joinNetworkPage() {
        sub("Got an invitation link from someone? Paste it here to see the network and the access you'd get before you accept.")
        page.append(.view(InviteCodeEntry()))
        page.append(text("Nothing changes until you choose Accept in the next step.", 13, "#64738A"))
    }
    func readInvite(_ code: String) async throws -> Any? {
        if preview { throw AppError.invalidOperation("Sign in to review an invitation.") }
        return try await api.send("POST", "/2.2/inviteQuery", J.obj("invite_code", code))
    }
    func invitationReview(_ code: String, _ data: Any?) {
        let state = J.s(data, "invite_status")
        let pending = state.caseInsensitiveCompare("pending") == .orderedSame
        let ref = DialogRef()
        var p: [Block] = [text("Network invitation", 23, "#17263C", true)]
        p.append(contentsOf: details(data, 0))
        var buttons: [DialogActionButton] = []
        for accept in [true, false] {
            buttons.append(DialogActionButton(accept ? "Accept invitation" : "Decline invitation", accept, pending, { [self] in
                if !(await confirm((accept ? "Accept" : "Decline") + " the invitation to " + J.s(data, "network_name") + "?")) { return }
                let id = try ExtendedContracts.identifier(J.s(data, "network_id"))
                try await api.send("POST", "/2.2/networks/" + id + "/invites/response", try ExtendedContracts.inviteResponse(code, accept))
                ref.handle?.canClose = true; ref.handle?.close()
                await reloadMembership(accept ? "Network invitation accepted." : "Network invitation declined.")
            }))
        }
        p.append(.view(DialogActionsView(buttons: buttons, ref: ref)))
        if !pending { p.append(text("Only pending invitations can be accepted or declined.", 13, "#64738A")) }
        ref.handle = dialog("Network invitation", p)
        ref.handle?.showDialog()
    }
    func reloadMembership(_ message: String) async {
        selectedProfile = nil; selectedNode = nil; network = nil; networkUrl = nil; devices.removeAll(); nodes.removeAll(); profiles.removeAll(); featureData.removeAll()
        notificationEpoch += 1; currentPage = "Home"
        do { try await loadAccount(); showShell(); setNotice(message) }
        catch { showShell(); setNotice(message + " Refresh did not finish: " + MainWindow.message(error), true) }
    }
    func pendingTransfersPage(_ data: Any?) {
        sub("Networks someone wants to give to your account. If you accept, you become the owner and can manage it from the network selector.")
        let transfers = MainWindow.entries(data, "transfers"); var rows: [Row] = []
        for transfer in transfers {
            let from = J.first(transfer, "organization_name", "sender_name", "from"), what = J.first(transfer, "network_name", "subset_name", "name"), when = J.first(transfer, "created_at", "created")
            var actions: [ButtonSpec] = []
            for accept in [true, false] {
                let b = rowActionAsync(accept ? "Accept" : "Decline", { [self] in
                    if preview { throw AppError.invalidOperation("Preview mode is read only.") }
                    if !(await confirm((accept ? "Accept ownership of" : "Decline") + " this network transfer" + (from.isEmpty ? "" : " from " + from) + "?")) { return }
                    let id = try ExtendedContracts.identifier(J.s(transfer, "id"))
                    try await api.send("POST", "/2.2/network_transfers/" + id + (accept ? "/accept" : "/deny"))
                    await reloadMembership(accept ? "Network transfer accepted." : "Network transfer declined.")
                })
                if accept { accent(b); compactAction(b) }
                actions.append(b)
            }
            rows.append(settingRow((what.nullIfEmpty ?? "A network") + (from.isEmpty ? "" : " from " + from), when.isEmpty ? "Waiting for your answer" : "Sent " + when, nil, actions: actions))
        }
        if rows.isEmpty { rows.append(infoRow("No pending transfers", nil, "When someone sends you a network, it shows up here.")) }
        group(nil, rows: rows)
        let next = J.s(data, "next_token")
        if let token = Int(next) {
            page.append(.buttons([button("Next page of transfers", { [self] in featureData[currentPage] = try await read("/2.2/network_transfers/pending?token=\(token)") ?? JNull.shared; renderPage() })]))
        }
    }
    func licensePage() {
        sub("Use a software key from eero or your provider to activate a plan. You'll check the key first, then confirm before anything is activated. Automatic renewal starts off.")
        if network == nil { group(nil, infoRow("No network selected", nil, "Choose the network to activate the key for at the top.")); return }
        group(nil, settingRow("License key", "Activates on " + (J.s(network, "name").nullIfEmpty ?? "this network") + ".", nil, rowAction("Enter key", { [self] in
            edit("License key", [Field("key", "Software key", nil, "password")], { [self] values in
                if preview { throw AppError.invalidOperation("Sign in to validate a key.") }
                let key = v(values, "key").trim()
                let result = try await api.send("POST", "/2.2/license_keys", try ExtendedContracts.license(key))
                afterEditor { [self] in licenseActivation(key, result) }
            }, "Check key")
        })))
    }
    func outgoingTransferPage(_ data: Any?) {
        sub("An invitation you sent to give this network to someone else.")
        let to = J.first(data, "recipient_email_address", "recipient_phone_number", "recipient_name"), sent = J.s(data, "transfer_date")
        if to.isEmpty && sent.isEmpty { group(nil, infoRow("No transfer in progress", nil, "Start one from Network management › Transfer ownership.")); return }
        group(nil, settingRow("Invitation to " + (to.nullIfEmpty ?? "recipient"), (sent.isEmpty ? "" : "Sent " + sent + " · ") + "Waiting for them to accept. Ownership changes only when they do.", nil,
            rowActionAsync("Resend", { [self] in if !(await confirm("Resend the pending ownership invitation to the current recipient?")) { return }; try await change("POST", try netResource("transfer"), nil, false, "Ownership invitation resent.") }),
            rowActionAsync("Cancel", { [self] in if !(await confirm("Cancel the outgoing ownership invitation?")) { return }; try await change("DELETE", try netResource("transfer"), nil, false, "Ownership invitation canceled.") }, true)))
        technicalDetails(data)
    }
    func licenseActivation(_ key: String, _ result: Any?) {
        let ref = DialogRef(), renew = FlagBox(false)
        var p: [Block] = [text("Review license activation", 23, "#17263C", true)]
        p.append(contentsOf: details(result, 0))
        p.append(text("Network: " + J.s(network, "name"), 16, "#17263C", true))
        p.append(.view(FlagBoxToggle(box: renew, label: "Enable automatic renewal under the provider's subscription terms")))
        p.append(.view(DialogActionsView(buttons: [DialogActionButton("Activate for this network", true, true, { [self] in
            if !(await confirm("Activate this subscription key for " + J.s(network, "name") + "? Automatic renewal: " + (renew.value ? "enabled" : "disabled") + ".")) { return }
            try await change("POST", "/2.2/license_keys", try ExtendedContracts.license(key, networkId, renew.value), false, "Subscription key activated.")
            ref.handle?.canClose = true; ref.handle?.close()
        })], ref: ref)))
        ref.handle = dialog("Activate license", p)
        ref.handle?.showDialog()
    }
    func partnerBenefitsPage() {
        sub("Apps and services included with eero Plus. You need the matching plan. Codes only appear when you ask for them.")
        group("Your benefits",
            settingRow("1Password", "Password manager. Get a code, then redeem it on 1Password's website.", nil,
                rowActionAsync("Status", { [self] in info("1Password", try await read("/2.2/account/1password_status")) }),
                rowActionAsync("Get code", { [self] in try showPartnerCode("1Password", try await read("/2.2/partners/onepassword/coupon?test=false")) })),
            settingRow("Malwarebytes", "Antivirus for your computers and phones.", nil,
                rowActionAsync("Get code", { [self] in try showPartnerCode("Malwarebytes", try await read("/2.2/partners/malwarebytes/coupon?test=false")) })),
            network != nil ? settingRow("VPN (Guardian)", "See how many VPN devices your plan allows. Set up the VPN under Security & privacy.", nil,
                rowActionAsync("Check seats", { [self] in info("VPN seats", try await read("/2.2/partners/guardian/networks/" + networkId + "/availability")) })) : nil)
        relatedPagesFor("Home/Security & privacy/Identity protection", "Home/Security & privacy/VPN")
    }
    func showPartnerCode(_ title: String, _ result: Any?) throws {
        let code = J.s(result, "coupon"); if code.isEmpty { throw AppError.invalidOperation("No redemption code was returned for this account.") }
        let p: [Block] = [text(title + " redemption code", 23, "#17263C", true), .view(CopyableValueField(value: code, label: title + " redemption code")),
                          .buttons([localButton("Copy redemption code", { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(code, forType: .string) })])]
        dialog(title, p).showDialog()
    }
    func identityProtectionPage(_ data: Any?) {
        sub("Watches for your personal information in data breaches and on the dark web. Included with some eero Plus plans.")
        let enabled = J.get(data, "enabled")
        let on = (enabled as? Bool) ?? false
        group("Protection",
            settingRow("Identity protection", J.s(data, "status").isEmpty ? "Turn it on to start monitoring." : MainWindow.friendly(J.s(data, "status")), enabled is Bool ? (on ? "On" : "Off") : "Not reported",
                on ? nil : rowAction("Turn on", { [self] in activateIdentityProtection() }),
                rowActionAsync("Countries", { [self] in info("Supported countries", try await read(try featureUrl("identity_protection/countries"))) })))
        let previous = featureData["identity_scan"].flatMap { $0 is JNull ? nil : $0 }
        group("Scan an email address",
            settingRow("1. Scan", "Checks where an email address appears in known data breaches.", nil, rowAction("Scan email", { [self] in scanIdentityEmail() })),
            settingRow("2. Verify the email (optional)", "Verify you own the address to see the full results. eero sends a code to it.", nil, rowAction("Send code", { [self] in sendIdentityVerification() }), rowAction("Enter code", { [self] in verifyIdentityEmail() })),
            previous != nil ? settingRow("Latest result", nil, nil, rowAction("View", { [self] in identityScanResult(previous) })) : nil)
    }
    func activateIdentityProtection() {
        edit("Turn on identity protection", [Field("country", "Country", "US").help("Two-letter code, for example US, CA or GB. Use Countries to see where it's available.")], { [self] values in
            let country = v(values, "country").trim().uppercased(); if !Regex.isMatch(country, "^[A-Z]{2}$") { throw AppError.argument("Enter a two-letter country code.") }
            if !(await confirm("Turn on identity protection for this account in " + country + "?")) { return }
            try await change("POST", "/2.2/account/identity_protection/signup", J.obj("country_code", country), false, "Identity protection activation requested.")
        }, "Turn on")
    }
    func scanIdentityEmail() {
        edit("Scan an email address", [Field("email", "Email address")], { [self] values in
            let email = try ExtendedContracts.email(v(values, "email")); if preview { throw AppError.invalidOperation("Sign in to run a scan.") }
            if !(await confirm("Send " + email + " to eero's identity-protection service for a scan?")) { return }
            let result = try await api.send("POST", "/2.2/account/identity_protection/scan", J.obj("email", email)); featureData["identity_scan"] = result
            afterEditor { [self] in identityScanResult(result) }
        }, "Scan")
    }
    func sendIdentityVerification() {
        edit("Verify your email", [Field("email", "Email address").help("eero sends a code to this address.")], { [self] values in
            if preview { throw AppError.invalidOperation("Sign in to request verification.") }
            try await api.send("POST", "/2.2/account/identity_protection/scan/email", J.obj("email", try ExtendedContracts.email(v(values, "email"))))
            setNotice("Code sent. Choose Enter code when it arrives.")
        }, "Send code")
    }
    func verifyIdentityEmail() {
        edit("Enter verification code", [Field("code", "Code from the email")], { [self] values in
            let code = v(values, "code").trim(); if !Regex.isMatch(code, "^[A-Za-z0-9-]{4,32}$") { throw AppError.argument("Enter the code from the verification email.") }
            if preview { throw AppError.invalidOperation("Sign in to verify an email.") }
            let result = try await api.send("POST", "/2.2/account/identity_protection/verify_email", J.obj("code", code)); featureData["identity_scan"] = result
            afterEditor { [self] in identityScanResult(result) }
        }, "Verify")
    }
    func identityScanResult(_ result: Any?) {
        var p: [Block] = [text("Digital footprint", 23, "#17263C", true)]
        p.append(contentsOf: details(result, 0))
        let report = J.s(result, "mwb_full_report_url")
        if !report.isEmpty { p.append(.padded([.buttons([localButton("Open detailed report at Malwarebytes", { [self] in try openPartnerReport(report) })])], EdgeInsets(top: 8, leading: 0, bottom: 0, trailing: 0))) }
        if J.b(result, "email_unverified") { p.append(text("Verify the email (step 2 on the page) to see the full results.", 14, "#64738A")) }
        dialog("Digital footprint", p).showDialog()
    }
    func openPartnerReport(_ url: String) throws {
        guard let u = URLComponents(string: url), let scheme = u.scheme, scheme == "https", (u.port ?? 443) == 443, u.user == nil, u.password == nil,
              let host = u.host?.lowercased(), host == "malwarebytes.com" || host.hasSuffix(".malwarebytes.com") else {
            throw AppError.invalidOperation("The service did not return a supported secure Malwarebytes report link.")
        }
        openExternal(url)
    }
    func setupVendorsPage(_ data: Any?) {
        sub("Let smart-home brands set up their devices on your wifi automatically, without you typing the password.")
        let vendors = MainWindow.entries(data, "vendors", "configurations"); var rows: [Row] = []
        for vendor in vendors {
            let id = J.s(vendor, "vendor_id")
            let name = J.first(vendor, "vendor_name", "name", "display_name").nullIfEmpty ?? MainWindow.friendly(id).nullIfEmpty ?? "Device maker"
            if id.isEmpty { rows.append(infoRow(name, MainWindow.onOff(J.b(vendor, "enabled")))); continue }
            rows.append(settingToggle(name, "Let " + name + " devices join your wifi during setup.", J.b(vendor, "enabled"), { [self] on in
                if !(await confirm((on ? "Allow " : "Stop allowing ") + name + " to set up devices on this network?")) { return }
                try await change("PUT", try featureUrl("simple_setup/vendors/" + id.escapedData), J.obj("enabled", on), true, name + " setup " + (on ? "allowed." : "turned off."))
            }))
        }
        if rows.isEmpty { rows.append(infoRow("No device makers listed", nil, "eero didn't return any brands for automatic setup on this network.")) }
        group(nil, rows: rows)
    }
    func ispSettingsPage() {
        sub("Only needed if your internet provider gave you a PPPoE username and password, or a VLAN ID. Most connections don't use either.")
        let pppoe = J.b(network, "pppoe_enabled"), user = J.s(network, "pppoe_username"), vlan = J.s(network, "vlan")
        group(nil,
            settingRow("PPPoE login", "Signs in to your ISP with a username and password. Common with some DSL and fiber providers.", pppoe ? "On" + (user.isEmpty ? "" : " · " + user) : "Off", rowAction("Change", { [self] in editIspSettings() })),
            settingRow("VLAN tagging", "Some fiber providers require a VLAN ID on the internet connection.", vlan.isEmpty ? "Off" : "VLAN " + vlan, rowAction("Change", { [self] in editIspSettings() })))
    }
    func editIspSettings() {
        edit("ISP settings", [
            Field("enabled", "Use PPPoE login", J.b(network, "pppoe_enabled"), "bool"),
            Field("replace", "Enter a new PPPoE username and password", !J.b(network, "pppoe_enabled"), "bool").when("enabled", "True").help("Leave off to keep the credentials eero already has."),
            Field("username", "PPPoE username", J.s(network, "pppoe_username")).when("replace", "True"),
            Field("password", "PPPoE password", nil, "password").when("replace", "True"),
            Field("vlan", "ISP VLAN ID", J.s(network, "vlan")).help("Leave blank to turn VLAN tagging off.")], { [self] values in
            let enabled = flag(values, "enabled"), replace = flag(values, "replace")
            if enabled && !J.b(network, "pppoe_enabled") && !replace { throw AppError.argument("Enter new credentials when enabling PPPoE for the first time.") }
            let body = try ExtendedContracts.pppoe(enabled, v(values, "username"), v(values, "password"), replace, v(values, "vlan"))
            if !(await confirm("Apply ISP settings? Incorrect PPPoE credentials or VLAN settings can disconnect the network.")) { return }
            try await change("PUT", try netResource("settings"), body, false, "ISP settings updated.")
        }, "Apply ISP settings", "Use the settings supplied by your ISP. Wrong values can disconnect your network.")
    }
    func uploadPortalAsset(_ config: Any?, _ logo: Bool) async throws {
        if preview { throw AppError.invalidOperation("Sign in to upload a portal asset.") }
        let configId = try ExtendedContracts.identifier(J.first(config, "configuration_id", "id"))
        let picked = logo
            ? try await pickFile("Select guest portal logo", "PNG or JPEG image", ["image/png", "image/jpeg"], ["*.png", "*.jpg", "*.jpeg"])
            : try await pickFile("Select guest terms PDF", "PDF document", ["application/pdf"], ["*.pdf"])
        guard let fileName = picked else { return }
        let size = ((try? FileManager.default.attributesOfItem(atPath: fileName))?[.size] as? NSNumber)?.int64Value ?? 0
        if size > Int64((logo ? 5 : 10) * 1024 * 1024) { throw AppError.argument("This file is too large. Logos support up to 5 MB; PDFs up to 10 MB.") }
        let bytes: Data
        do { bytes = try Data(contentsOf: URL(fileURLWithPath: fileName)) } catch { throw AppError.io("The file could not be read.") }
        let mime = try ExtendedContracts.portalMime(fileName, bytes, logo)
        if !(await confirm("Upload " + (fileName as NSString).lastPathComponent + " (\((bytes.count + 1023) / 1024) KB) as the guest portal " + (logo ? "logo" : "terms document") + "? Guests will be able to access the uploaded asset.")) { return }
        let type = logo ? "CaptivePortalLogo" : "CaptivePortalToS", ext = (fileName as NSString).pathExtension.trimStart(".").lowercased()
        let upload = try await api.send("GET", try featureUrl("file/import/upload_link?importJobType=" + type + "&fileExtension=" + ext))
        try await PortalUpload.send(upload, bytes, mime)
        try await change("POST", try featureUrl("file/import/job"), try ExtendedContracts.importJob(J.s(upload, "import_file_path"), logo, configId), false, "Asset uploaded and import requested. Refresh the portal after processing finishes.")
    }
    /// Native file chooser (NSOpenPanel as a sheet). Returns a local path, or nil when the user cancels.
    func pickFile(_ title: String, _ filterName: String, _ mimeTypes: [String], _ patterns: [String]) async throws -> String? {
        let panel = NSOpenPanel()
        panel.title = title; panel.message = title + " (" + filterName + ")"
        panel.canChooseFiles = true; panel.canChooseDirectories = false; panel.allowsMultipleSelection = false; panel.resolvesAliases = true
        var types: [UTType] = []
        for m in mimeTypes { if let t = UTType(mimeType: m), !types.contains(t) { types.append(t) } }
        for p in patterns { if let t = UTType(filenameExtension: p.trimStart("*").trimStart(".")), !types.contains(t) { types.append(t) } }
        if !types.isEmpty { panel.allowedContentTypes = types }
        let response: NSApplication.ModalResponse
        if let host = Dialogs.host() { response = await panel.beginSheetModal(for: host) } else { response = panel.runModal() }
        guard response == .OK, let url = panel.url else { return nil }
        if !url.isFileURL || url.path.isEmpty { throw AppError.invalidOperation("Choose a file stored on this Mac.") }
        return url.path
    }
    func dnsPerformancePage(_ result: Any?) {
        sub("Check how quickly DNS servers answer your gateway eero. Faster servers can make websites start loading sooner. Testing doesn't change your settings.")
        let started = featureData["dns_perf_action"] != nil
        group(nil,
            settingRow("Run a test", "Compare the servers you use now with any others you want to try, for example 1.1.1.1 or 8.8.8.8.", nil, rowAction("Start test", { [self] in
                let existing = J.list(J.get(network, "dns", "custom", "ips")).map { J.s($0) }.joined(separator: "\n")
                edit("Test DNS servers", [Field("servers", "DNS servers to test (one per line)", existing, "multiline").help("Leave as is to test the servers you use now."), Field("port", "Port", "53").help("Almost always 53.")], { [self] values in
                    guard let port = Num.parseInt(v(values, "port")) else { throw AppError.argument("Enter a numeric DNS port.") }
                    let body = try ExtendedContracts.dnsPerformance(v(values, "servers"), port)
                    if preview { throw AppError.invalidOperation("Sign in to start a gateway test.") }
                    let kickoff = try await api.send("POST", try featureUrl("dns_perf_test"), body)
                    featureData["dns_perf_action"] = try ExtendedContracts.identifier(J.s(kickoff, "action_uuid")); try await loadFeature(currentPage); renderPage(); setNotice("Test started. Choose Show results in a moment.")
                }, "Start test")
            })),
            started ? settingRow("Results", "The test takes a few seconds. Show results again if it isn't finished.", nil, rowActionAsync("Show results", { [self] in try await loadFeature(currentPage); renderPage() })) : nil)
        addGroupHeading("Results")
        if result != nil { page.append(card(details(result, 0), 18)) }
        else { page.append(card([text(started ? "Waiting for results. Choose Show results above." : "No test yet. Run one above to see how fast each server answers.", 14, "#64738A")], 18)) }
    }
}

// MARK: - Dialog building blocks shared by the feature pages in this port

/// Holds a dialog handle so content built before the dialog exists can close it.
@MainActor
final class DialogRef { var handle: DialogHandle? }

/// A Bool shared between a dialog control and the action that reads it.
final class FlagBox: ObservableObject {
    @Published var value: Bool
    init(_ value: Bool) { self.value = value }
}

struct FlagBoxToggle: View {
    @ObservedObject var box: FlagBox
    let label: String
    var body: some View {
        Toggle(isOn: $box.value) { Text(label).font(.system(size: 14)).fixedSize(horizontal: false, vertical: true) }
            .toggleStyle(.checkbox).padding(.bottom, 12).accessibilityLabel(label)
    }
}

struct DialogActionButton {
    let title: String; let accent: Bool; let enabled: Bool
    let run: () async throws -> Void
    init(_ title: String, _ accent: Bool, _ enabled: Bool, _ run: @escaping () async throws -> Void) { self.title = title; self.accent = accent; self.enabled = enabled; self.run = run }
}

/// Dialog buttons that run one action at a time, keep the dialog open while working and show failures inline.
struct DialogActionsView: View {
    let buttons: [DialogActionButton]
    let ref: DialogRef
    @State private var status = ""
    @State private var working = false
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            if !status.isEmpty { Text(status).font(.system(size: 13)).foregroundColor(Tone.danger.color).fixedSize(horizontal: false, vertical: true) }
            HStack(spacing: 8) {
                ForEach(Array(buttons.enumerated()), id: \.offset) { _, b in
                    if b.accent { Button(b.title) { press(b) }.buttonStyle(.borderedProminent).disabled(!b.enabled || working) }
                    else { Button(b.title) { press(b) }.buttonStyle(.bordered).disabled(!b.enabled || working) }
                }
            }
        }.padding(.top, 8)
    }
    private func press(_ b: DialogActionButton) {
        if working { return }
        working = true; status = ""
        Task { @MainActor in
            ref.handle?.canClose = false
            do { try await b.run() } catch { status = MainWindow.message(error) }
            working = false; ref.handle?.canClose = true
        }
    }
}

/// A read-only, selectable value (generated password, redemption code, link).
struct CopyableValueField: View {
    let value: String
    let label: String
    var size: CGFloat = 14
    var body: some View {
        TextField("", text: .constant(value)).textFieldStyle(.roundedBorder).font(.system(size: size))
            .disabled(false).textSelection(.enabled).frame(maxWidth: 520, alignment: .leading).padding(.bottom, 12).accessibilityLabel(label)
    }
}

/// Invitation code input with its Review button (Join a network).
struct InviteCodeEntry: View {
    @EnvironmentObject var model: MainWindow
    @State private var code = ""
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            TextField("Paste an invitation link or code", text: Binding(get: { code }, set: { code = String($0.prefix(1024)) }))
                .textFieldStyle(.roundedBorder).frame(width: 560).accessibilityLabel("Paste an invitation link or code")
                .onSubmit { if !model.busy { review() } }
            Button("Review invitation") { review() }.buttonStyle(.borderedProminent).disabled(model.busy)
        }.padding(.bottom, 8)
    }
    private func review() {
        let input = code
        Task { @MainActor in
            await model.run({
                let checked = try ExtendedContracts.inviteCode(input)
                let data = try await model.readInvite(checked)
                model.invitationReview(checked, data)
            }, "Working…")
        }
    }
}
