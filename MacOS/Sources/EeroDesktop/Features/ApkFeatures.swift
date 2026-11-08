import AppKit
import Foundation
import SwiftUI

// Functions and settings for endpoints found in eero Android 26.13.0 that this client did not use before.
extension MainWindow {
    func apiNetworkPath(_ suffix: String) -> String { "/2.2/networks/" + networkId + "/" + suffix.trimStart("/") }
    static func nodeId(_ node: Any?) throws -> String { try ExtendedContracts.identifier(J.s(node, "url").trimEnd("/").split("/").last ?? "") }
    func nodeChoices() -> JObject {
        let d = JObject()
        for n in nodes where !J.s(n, "serial").isEmpty { d[J.first(n, "location", "model") + " · " + J.s(n, "serial")] = n }
        return d
    }
    func deviceChoices() -> JObject {
        let d = JObject()
        for x in devices {
            let mac = J.s(x, "mac"); if mac.isEmpty { continue }
            var key = J.first(x, "nickname", "hostname", "display_name", "manufacturer").nullIfEmpty ?? mac; key += " · " + mac
            if !d.containsKey(key) { d[key] = x }
        }
        return d
    }
    func profileChoices() -> JObject {
        let d = JObject()
        for p in profiles {
            let url = J.s(p, "url"); if url.isEmpty { continue }
            d[J.s(p, "name") + " · " + (url.trimEnd("/").split("/").last ?? "")] = p
        }
        return d
    }
    /// Synchronous actions that can refuse (empty choices etc.) show the same error the Windows app shows.
    func guarded(_ action: () throws -> Void) {
        do { try action() }
        catch { showApplicationError("The app could not complete that action. " + MainWindow.message(error)) }
    }
    func pick(_ title: String, _ choices: JObject, _ empty: String, _ action: @escaping (Any?) async throws -> Void) {
        guarded {
            if choices.count == 0 { throw AppError.invalidOperation(empty) }
            // The chosen item is handled after this picker closes, so follow-up dialogs never stack on top of it.
            edit(title, [Field("item", "Choose", choices.keys.first, "choice", choices: choices.keys)], { [self] values in
                let item = choices[v(values, "item")]
                afterEditor { [self] in await run({ try await action(item) }, "Loading…") }
            }, "Continue")
        }
    }

    // MARK: eero details
    func nodeApkActions(_ node: Any?, _ options: inout [Block], _ infoPanel: inout [Block]) {
        if J.get(node, "client_ap_disabled") is Bool { detail(&infoPanel, "Client wifi", J.b(node, "client_ap_disabled") ? "Off on this eero (backhaul only)" : "On") }
        if !J.s(node, "state").isEmpty { detail(&infoPanel, "State", MainWindow.human(J.s(node, "state"))) }
        let serial = J.s(node, "serial")
        options.append(groupHeading("Advanced"))
        let rows: [Row?] = [
            J.get(node, "nightlight") != nil ? settingRow("Preview nightlight", "Shows the nightlight at a brightness for a moment without saving it.", nil, rowAction("Preview", { [self] in
                edit("Preview nightlight", [Field("brightness", "Brightness (0–100%)", J.s(node, "nightlight", "brightness_percentage").nullIfEmpty ?? "50")], { [self] values in
                    try await change("POST", "/2.2/eeros/" + (try MainWindow.nodeId(node)) + "/nightlight/override", try ApkContracts.nightlightPreview(v(values, "brightness")), true, "Nightlight preview sent.")
                }, "Preview")
            })) : nil,
            settingRow("Authorization", "Whether this eero is allowed on this network.", nil, rowActionAsync("Check", { [self] in info("Authorization status", try await read("/2.2/eeros/" + (try MainWindow.nodeId(node)) + "/authorization_status?network_id=" + networkId.escapedData)) })),
            !serial.isEmpty ? settingRow("License", "For eeros supplied by a provider: when this eero's license ends.", nil, rowActionAsync("Check", { [self] in info("eero license", try await read("/2.2/eeros/" + (try RuntimeContracts.serial(serial)).escapedData + "/license_expiration?network=" + networkId.escapedData)) })) : nil,
            !serial.isEmpty ? settingRow("Setup settings from provider", "Settings your provider loaded onto this eero before setup.", nil, rowActionAsync("View", { [self] in info("Setup settings from provider", try await read("/2.2/eeros/" + (try RuntimeContracts.serial(serial)).escapedData + "/pre_setup_config")) })) : nil
        ]
        options.append(rowGroup(rows.compactMap { $0 }, 14))
    }
    func identifyEero(_ serial: String) {
        edit("Find this eero", [Field("color1", "First color", "blue", "choice", choices: ApkContracts.ledColors), Field("color2", "Second color", "white", "choice", choices: ApkContracts.ledColors), Field("duration", "For how long (seconds)", "30"), Field("each", "Seconds per color", "1")], { [self] values in
            let body = try ApkContracts.ledCycle([v(values, "color1"), v(values, "color2")], v(values, "duration"), v(values, "each"))
            try await change("POST", "/2.2/eeros/" + (try RuntimeContracts.serial(serial)).escapedData + "/led_cycle", body, true, "The status light is flashing so you can find this eero.")
        }, "Flash light", "Flashes the status light through the chosen colors. Useful when several eeros look the same.")
    }
    func placementRatingEdit(_ serial: String) {
        edit("Placement rating", [Field("rssi", "Signal strength RSSI in dBm (optional)"), Field("mcs", "MCS index (optional)"), Field("freq", "Frequency in MHz (optional)")], { [self] values in
            let body = try ApkContracts.placementRating(v(values, "rssi"), v(values, "mcs"), v(values, "freq"))
            if preview { throw AppError.invalidOperation("Sign in to request a placement rating.") }
            info("Placement rating", try await api.send("POST", "/2.3/networks/" + networkId + "/placement_rating/" + (try RuntimeContracts.serial(serial)).escapedData, body))
        }, "Get rating", "eero rates this eero's placement. Measurements are optional; the mobile app fills them from a Bluetooth scan.")
    }

    // MARK: Add or replace eero / Setup tools
    func setupToolsPage() {
        sub("Extra tools for adding, replacing or checking eeros. Most people only need these when eero support asks.")
        group("Before you add an eero",
            settingRow("Check compatibility", "Make sure an eero works with this network before adding it.", nil, rowAction("Check", { [self] in checkEeroCompatibility() })),
            settingRow("Use the default wifi channel", "Helps a new eero find your network during setup. Switches back automatically.", nil, rowAction("Start", { [self] in useDefaultChannel() })))
        group("eeros linked to your account",
            settingRow("Waiting to be set up", "eeros eero expects to join this network soon.", nil, rowActionAsync("View", { [self] in info("eeros waiting to be set up", try await read("/2.3/networks/" + networkId + "/pending_eeros")) })),
            settingRow("Came in the box", "eeros sold together with this network's kit.", nil, rowActionAsync("View", { [self] in info("eeros packaged with this network", try await read(try featureUrl("packaged_eeros"))) })),
            settingRow("What each model supports", "Features available on each eero model on this network.", nil, rowActionAsync("View", { [self] in info("eero model capabilities", try await read("/2.2/eero_models/capabilities?networkId=" + networkId.escapedData)) })))
        group("After setup",
            settingRow("Report a replacement", "Tell eero you swapped one eero for another so your settings carry over.", nil, rowAction("Report", { [self] in reportEeroReplacement() })),
            settingRow("Placement rating", "Ask eero to rate where an eero is placed.", nil, rowAction("Rate", { [self] in
                let choices = nodeChoices()
                pick("Placement rating", choices, "No eeros with serial numbers were returned.", { [self] n in placementRatingEdit(J.s(n, "serial")) })
            })))
    }
    func checkEeroCompatibility() {
        edit("Check eero compatibility", [Field("serial", "eero serial number").help("Printed on the bottom of the eero."), Field("version", "Software version (optional)")], { [self] values in
            let q = try ApkContracts.ouiQuery(v(values, "serial"), v(values, "version"))
            info("Compatibility check", try await read(try featureUrl("ouicheck?" + q)))
        }, "Check", "Checks whether this eero's hardware is recognized for this network before you add it.")
    }
    func useDefaultChannel() {
        edit("Use the default wifi channel", [Field("seconds", "For how long (minutes)", "60").help("Up to 120 minutes. Switches back automatically after this time.")], { [self] values in
            guard let minutes = Num.parseInt(v(values, "seconds")), minutes >= 1, minutes <= 120 else { throw AppError.argument("Enter between 1 and 120 minutes.") }
            let body = try ApkContracts.defaultChannel(String(minutes * 60))
            if !(await confirm("Move this network to its default wifi channel for \(minutes) minutes? Devices may briefly reconnect.")) { return }
            try await change("POST", try featureUrl("node_setup"), body, true, "The network will use its default channel while you set up eeros.")
        }, "Start", "The mobile app does this before adding an eero so the new one can find the network.")
    }
    func reportEeroReplacement() {
        edit("Report an eero replacement", [Field("old", "Serial of the eero you replaced"), Field("new", "Serial of the new eero"), Field("ok", "The replacement worked", true, "bool")], { [self] values in
            let body = try ApkContracts.nodeSwap(v(values, "old"), v(values, "new"), flag(values, "ok"))
            try await change("POST", "/2.2/eeros/networks/" + networkId + "/notify_swap", body, false, "eero replacement reported.")
        }, "Report", "Tells eero that one eero was swapped for another, so the network keeps its settings and history.")
    }

    // MARK: Wifi / wireless
    func generateWifiPassword() async throws {
        if preview { throw AppError.invalidOperation("Sign in to generate a password.") }
        let value = try await api.send("POST", "/2.2/util/generatepassword", J.obj()); var password = J.s(value)
        if value is JObject { password = "" }
        if password.isEmpty { password = J.first(value, "password", "value") }
        // Generated passwords are only suggestions; the user applies one through Change.
        if password.isEmpty { throw AppError.invalidOperation("eero did not return a password.") }
        let p: [Block] = [text("Generated password", 23, "#17263C", true), .view(CopyableValueField(value: password, label: "Generated password", size: 16)),
                          text("Nothing has changed yet. Use Change wifi password to apply it.", 12, "#64738A"),
                          .buttons([localButton("Copy", { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(password, forType: .string) })])]
        dialog("Generated password", p).showDialog()
    }
    /// Passpoint is only shown when eero reports it for this network.
    func passpointRow() -> Row? {
        let passpoint = J.get(network, "passpoint")
        if !(passpoint is Bool) && !(J.get(passpoint, "enabled") is Bool) { return nil }
        let current = (passpoint as? Bool) ?? J.b(passpoint, "enabled")
        return settingToggle("Passpoint (Hotspot 2.0)", "Lets phones from supported carriers join automatically, like a public hotspot.", current, { [self] on in
            if !(await confirm((on ? "Turn on" : "Turn off") + " Passpoint? Devices may briefly reconnect.")) { return }
            try await change("PUT", try featureUrl("passpoint/enabled"), try ApkContracts.passpoint(on), false, "Passpoint " + (on ? "turned on." : "turned off."))
        })
    }

    // MARK: Security & privacy
    func securityApkActions() {
        group("Advanced",
            settingRow("Filter levels", "The preset filter levels eero offers for wifi networks and profiles.", nil,
                rowActionAsync("Networks", { [self] in info("Filter levels for wifi networks", try await read(try featureUrl("safe_filter_levels?safeFilterType=SubnetFilter"))) }),
                rowActionAsync("Profiles", { [self] in info("Filter levels for profiles", try await read(try featureUrl("safe_filter_levels?safeFilterType=ProfileFilter"))) })),
            settingRow("Allowed alias domains (CNAMEs)", "If a site you allowed still won't load because it uses another domain behind the scenes, add that domain here.", nil,
                rowAction("Whole network", { [self] in
                    edit("Allowed alias domains", [Field("domains", "Domains, one per line", nil, "multiline").help("For example cdn.example.com. This replaces the current list.")], { [self] values in
                        let body = try ApkContracts.cnames(v(values, "domains"))
                        try await change("PUT", try featureUrl("dns_policies/network/allowed/cnames"), body, false, "Allowed alias domains updated.")
                    }, "Save", "Lets these domains load through aliases that content filtering would otherwise block.")
                }),
                rowAction("Profiles", { [self] in
                    guarded {
                        let choices = profileChoices(); if choices.count == 0 { throw AppError.invalidOperation("Create a profile first.") }
                        var fields = [Field("domains", "Domains, one per line", nil, "multiline").help("For example cdn.example.com.")]
                        fields.append(contentsOf: choices.keys.map { Field("p:" + $0, "Apply to " + $0, false, "bool") })
                        edit("Allowed alias domains for profiles", fields, { [self] values in
                            let ids = choices.filter { flag(values, "p:" + $0.key) }.map { J.s($0.value, "url").trimEnd("/").split("/").last ?? "" }
                            let body = try ApkContracts.profileCnames(v(values, "domains"), ids)
                            try await change("PUT", try featureUrl("dns_policies/profiles/allowed/cnames"), body, false, "Allowed alias domains for profiles updated.")
                        }, "Save", "Applies the allowed alias domains to the profiles you switch on.")
                    }
                })))
    }

    // MARK: Activity / usage details
    func usageDetailsPage() {
        sub("Look closer at data usage and blocked threats for one eero, profile or device.")
        let range = statsRange(usageDays)
        let profileId: (String) throws -> String = { url in try ExtendedContracts.identifier(url.trimEnd("/").split("/").last ?? "") }
        group(nil, settingRow("Period", "Applies to everything on this page.", usageDays == 1 ? "Last day" : "Last \(usageDays) days", rowAction("Change", { [self] in
            edit("Period", [Field("days", "Show the last", "\(usageDays)" + (usageDays == 1 ? " day" : " days"), "choice", "1 day", "7 days", "30 days", "90 days")], { [self] values in
                usageDays = Int(v(values, "days").split(" ")[0]) ?? 7; renderPage()
            }, "Show")
        })))
        group("Data usage",
            settingRow("Each eero", "How much data went through each of your eeros.", nil,
                rowActionAsync("All eeros", { [self] in info("Data usage by eero", try await read(apiNetworkPath("data_usage/eeros/summary?") + range + "&cadence=daily")) }),
                rowAction("Pick one", { [self] in pick("Data usage for an eero", nodeChoices(), "No eeros were returned.", { [self] n in info("eero data usage", try await read(apiNetworkPath("data_usage/eeros/" + (try MainWindow.nodeId(n)) + "?") + range + "&cadence=daily")) }) })),
            settingRow("A profile", "Data used by all the devices in one profile.", nil,
                rowAction("Pick one", { [self] in pick("Data usage for a profile", profileChoices(), "No profiles were returned.", { [self] p in info("Profile data usage", try await read(apiNetworkPath("data_usage/profiles/" + (try profileId(J.s(p, "url"))) + "?") + range + "&cadence=daily")) }) })),
            settingRow("Devices not in a profile", "Usage for devices you haven't put in a profile.", nil,
                rowActionAsync("Each device", { [self] in info("Devices not in a profile", try await read(apiNetworkPath("data_usage/unprofiled/devices?") + range)) }),
                rowActionAsync("Total", { [self] in info("Total for devices not in a profile", try await read(apiNetworkPath("data_usage/unprofiled/summary?") + range + "&cadence=daily")) })))
        group("Blocked threats",
            settingRow("A device", "What eero blocked for one device.", nil,
                rowAction("Pick one", { [self] in pick("Blocked for a device", deviceChoices(), "No devices were returned.", { [self] d in info("Blocked for this device", try await read(apiNetworkPath("insights/devices/" + (try FeatureContracts.mac(J.s(d, "mac"))).escapedData + "?") + range + "&cadence=daily")) }) })),
            settingRow("A profile", "What eero blocked for a profile, in total or per device.", nil,
                rowAction("Total", { [self] in pick("Blocked for a profile", profileChoices(), "No profiles were returned.", { [self] p in info("Blocked for this profile", try await read(apiNetworkPath("insights/profiles/" + (try profileId(J.s(p, "url"))) + "?") + range + "&cadence=daily")) }) }),
                rowAction("Per device", { [self] in pick("Blocked per device in a profile", profileChoices(), "No profiles were returned.", { [self] p in info("Blocked per device", try await read(apiNetworkPath("insights/profiles/" + (try profileId(J.s(p, "url"))) + "/devices?") + range)) }) })))
    }

    // MARK: Notifications / permissions / backup / guest portal / transfer
    func chooseBackupDevices() {
        guarded {
            let choices = deviceChoices(); if choices.count == 0 { throw AppError.invalidOperation("No devices were returned.") }
            let fields = choices.map { Field("d:" + $0.key, $0.key, !J.b($0.value, "secondary_wan_deny_access"), "bool") }
            edit("Backup internet access", fields, { [self] values in
                let changed = choices.filter { flag(values, "d:" + $0.key) == J.b($0.value, "secondary_wan_deny_access") }
                if changed.isEmpty { return }
                if !(await confirm("Update backup internet access for \(changed.count) device(s)?")) { return }
                for (i, c) in changed.enumerated() {
                    let body = try ApkContracts.secondaryWan(J.s(c.value, "mac"), flag(values, "d:" + c.key))
                    if i < changed.count - 1 { try await api.send("PUT", "/2.3/networks/" + networkId + "/devices/secondary_wan_config", body) }
                    else { try await change("PUT", "/2.3/networks/" + networkId + "/devices/secondary_wan_config", body, false, "Backup internet access updated.") }
                }
            }, "Save", "Devices that are switched on may use the backup connection while your main internet is down.")
        }
    }
    func checkTransferRecipient() {
        edit("Check transfer recipient", [Field("contact", "Recipient email or phone")], { [self] values in
            info("Recipient", try await read("/2.2/transfer/has_networks?" + (try ApkContracts.transferTarget(v(values, "contact")))))
        }, "Check", "Shows whether the recipient already has an eero account and networks before you send a transfer.")
    }

    // MARK: Network flags
    func networkFlagsPage() {
        sub("Hidden switches eero support uses to troubleshoot. Leave these alone unless eero support asks you to change one.")
        let flags = J.get(network, "flags"), temporary = J.get(network, "temporary_flags")
        let setFlag: (String, Any?) async throws -> Void = { [self] flag, value in
            let name = try ApkContracts.flagName(flag)
            if !(await confirm("Set network flag " + name + "? Unknown flags can change how the network behaves.")) { return }
            try await change("PUT", try featureUrl("flags/" + name), try ApkContracts.flagValue(J.s(value)), true, "Network flag " + name + " updated.")
        }
        var rows: [Row] = []
        if let map = flags as? JObject {
            for (key, value) in map {
                if let on = value as? Bool { rows.append(settingToggle(key, nil, on, { wanted in try await setFlag(key, wanted ? "true" : "false") })) }
                else { rows.append(infoRow(key, J.s(value))) }
            }
        }
        if rows.isEmpty { rows.append(infoRow("No flags reported", nil, "That's normal. There's nothing to do here unless eero support asks.")) }
        rows.append(settingRow("Set a flag by name", "Only with a flag name and value from eero support.", nil, rowAction("Set", { [self] in
            edit("Set network flag", [Field("name", "Flag name").help("Exactly as eero support gave it to you."), Field("value", "Value").help("Usually true or false.")], { [self] values in
                try await setFlag(v(values, "name"), v(values, "value"))
            }, "Set")
        })))
        group("Network flags", rows: rows)
        if temporary != nil { addGroupHeading("Temporary flags"); objectCards(temporary) }
    }

    // MARK: Organization & community
    func organizationPage() {
        sub("For networks run by a business, building or internet provider. Most home networks don't need anything here.")
        let type = J.first(network, "network_customer_type", "network_type").nullIfEmpty ?? "Residential"
        group("This network",
            settingRow("Network type", "Business and apartment-building (MDU) types change which features and plans apply.", MainWindow.friendly(type), rowAction("Change", { [self] in
                edit("Network type", [Field("type", "Network type", type, "choice", choices: ApkContracts.customerTypes)], { [self] values in
                    let body = try ApkContracts.customerType(v(values, "type"))
                    if !(await confirm("Change this network's type to " + v(values, "type") + "? Business and MDU types change which features and plans apply.")) { return }
                    try await change("PUT", try featureUrl("network_customer_type"), body, true, "Network type updated.")
                }, "Save")
            })),
            settingRow("Community", "The building or community this network belongs to, if any.", nil, rowActionAsync("View", { [self] in info("Community", try await read(try featureUrl("community"))) })),
            settingRow("Provider branding", "Branding from the internet provider that supplied your eeros.", nil, rowActionAsync("View", { [self] in info("Provider branding", try await read(try featureUrl("cobranding"))) })))
        group("Your organization",
            settingRow("Managed networks", "A summary of the networks your organization manages.", nil, rowActionAsync("View", { [self] in info("Managed networks", try await read("/2.2/managed_summary")) })),
            settingRow("Professionally managed networks", "Networks managed by an installer or provider. You can filter by name.", nil, rowAction("Search", { [self] in
                edit("Professionally managed networks", [Field("filter", "Network name contains (optional)")], { [self] values in
                    let f = v(values, "filter").trim(); if f.count > 64 { throw AppError.argument("Enter up to 64 characters.") }
                    info("Professionally managed networks", try await read("/2.2/organizations/self/networks/pro_managed" + (f.isEmpty ? "" : "?network_name_filter=" + f.escapedData)))
                }, "Show")
            })),
            settingRow("Units", "Apartments or spaces in your organization, and their unit IDs.", nil, rowAction("Search", { [self] in
                edit("Units", [Field("search", "Search (optional)")], { [self] values in
                    let s = v(values, "search").trim(); if s.count > 64 { throw AppError.argument("Enter up to 64 characters.") }
                    info("Units", try await read("/2.2/organizations/self/subsets?limit=50" + (s.isEmpty ? "" : "&search=" + s.escapedData)))
                }, "Show")
            })),
            settingRow("Assign an eero to a unit", "Link an eero to an apartment or space. Look up the unit ID with Units first.", nil, rowAction("Assign", { [self] in
                edit("Assign eero to unit", [Field("subset", "Unit ID").help("Find it with Units on this page."), Field("serial", "eero serial number").help("Printed on the bottom of the eero."), Field("unit", "Unit name").help("For example 4B."), Field("description", "Description (optional)")], { [self] values in
                    let subset = try ExtendedContracts.identifier(v(values, "subset").trim())
                    let body = try ApkContracts.subsetAssociation(v(values, "serial"), v(values, "unit"), v(values, "description"))
                    if !(await confirm("Assign this eero to the selected unit?")) { return }
                    try await change("PUT", "/2.2/organizations/self/subsets/" + subset + "/associate", body, false, "eero assigned to the unit.")
                }, "Assign")
            })))
        addGroupHeading("Danger zone")
        page.append(rowGroup([settingRow("Holding state", "Pauses the network while it's being linked or transferred. Only use this if eero or your provider tells you to.", nil, rowActionAsync("Put on hold", { [self] in
            if !(await confirm("Put this network in a holding state? eero uses this while a network is being linked or transferred, and the network may stop working normally until it is linked again.")) { return }
            if !(await confirm("Are you sure? Continue only if eero or your provider told you to.")) { return }
            try await change("POST", try featureUrl("holding_state"), nil, false, "Network holding state requested.")
        }, true))], 14))
    }

    // MARK: Account
    func trustPage(_ data: Any?) {
        sub("Certificates your eero account trusts, for example for a company network. Only add certificates from a source you trust.")
        var rows: [Row] = []
        for item in MainWindow.entries(data, "certificates", "trust") {
            let serial = J.first(item, "serial", "certificate_serial"); if serial.isEmpty { continue }
            let name = J.first(item, "name", "subject", "common_name"), expires = J.first(item, "not_after", "expires_at", "expiration")
            rows.append(settingRow(name.nullIfEmpty ?? "Certificate " + serial, "Serial " + serial + (expires.isEmpty ? "" : " · Expires " + expires), nil,
                rowAction("Replace", { [self] in trustEdit(serial) }),
                rowActionAsync("Remove", { [self] in
                    if !(await confirm("Remove trusted certificate " + serial + "?")) { return }
                    try await change("DELETE", "/2.2/account/trust/" + (try ApkContracts.token(serial, "certificate serial")).escapedData, nil, false, "Certificate removed.")
                }, true)))
        }
        if rows.isEmpty { rows.append(infoRow("No trusted certificates", nil, "Most people don't need any. Add one only if your IT team or provider asks you to.")) }
        rows.append(settingRow("Add a certificate", "Paste a public certificate (PEM). Never paste a private key.", nil, rowAction("Add", { [self] in trustEdit(nil) })))
        group(nil, rows: rows)
    }
    func trustEdit(_ oldSerial: String?) {
        edit(oldSerial == nil ? "Add trusted certificate" : "Replace certificate " + oldSerial!, [Field("pem", "PEM certificate", nil, "multiline")], { [self] values in
            let body = try ApkContracts.trust(v(values, "pem"), oldSerial)
            try await change(oldSerial == nil ? "POST" : "PUT", "/2.2/account/trust", body, true, oldSerial == nil ? "Certificate added." : "Certificate replaced.")
        }, "Save", "Paste a public certificate only. Never paste a private key.")
    }
    func accountDataPage() {
        sub("Information eero keeps about your account, and who is responsible for it.")
        let verified = J.get(account, "email", "verified")
        let isVerified = (verified as? Bool) ?? false
        let emailState = verified is Bool ? (isVerified ? "Verified" : "Not verified") : "Unknown"
        group("Email",
            settingRow("Email verification", isVerified ? "Your email address is confirmed." : "If eero asked you to confirm your email, send a new link here.", emailState,
                isVerified ? nil : rowActionAsync("Resend email", { [self] in
                    if preview { throw AppError.invalidOperation("Sign in first.") }
                    try await api.send("POST", "/2.2/register/email/resend", ""); setNotice("Verification email requested. Check your inbox.")
                })))
        group("Privacy",
            settingRow("Data controller", "The company responsible for your eero data in your region.", nil, rowActionAsync("View", { [self] in info("Data controller", try await read("/2.2/data-controller/info")) })))
        group("Technical details",
            J.get(account, "is_external") is Bool ? infoRow("Signed in through a provider", J.b(account, "is_external") ? "Yes" : "No", "Yes means your internet provider manages this eero sign-in.") : nil,
            settingRow("Countries with text sign-in", "Where eero can send sign-in codes by text message.", nil, rowActionAsync("View", { [self] in info("Countries with text sign-in", try await read("/2.2/countries/sms")) })),
            settingRow("App configuration", "Settings eero sends to its apps. Mostly useful for support.", nil, rowActionAsync("View", { [self] in info("App configuration", try await read("/2.2/app_configuration" + ((networkUrl ?? "").isEmpty ? "" : "?networkId=" + networkId.escapedData))) })),
            settingRow("Amazon account ID", "The ID that links your eero and Amazon accounts.", nil, rowActionAsync("View", { [self] in info("Amazon account ID", try await read("/2.2/directedid?" + (try ApkContracts.directedIdQuery("0", "0", "0")))) })))
    }
    func entitlementsPage(_ data: Any?) {
        sub("What this network's plan includes, and upgrades you can try.")
        let name: (Any?) -> String = { item in (item as? String).map { MainWindow.friendly($0) } ?? MainWindow.friendly(J.first(item, "name", "feature", "key", "slug")) }
        var included: [Row] = []
        for item in MainWindow.entries(J.get(data, "features"), "features") {
            let on = item is String || !(J.get(item, "enabled") is Bool) || J.b(item, "enabled")
            included.append(infoRow(name(item).nullIfEmpty ?? "Feature", on ? "✓ Included" : "Not active"))
        }
        if included.isEmpty { included.append(infoRow("No features reported", nil, data == nil ? "Choose Refresh at the top right to try again." : "This network's plan doesn't list extra features.")) }
        group("Included in your plan", rows: included)
        var upgrades: [Row] = []
        for item in MainWindow.entries(J.get(data, "upsell_features"), "upsell_features", "features") {
            let tier = item is String ? "" : J.first(item, "tier", "plan", "plan_tier")
            upgrades.append(settingRow(name(item).nullIfEmpty ?? "Upgrade", tier.isEmpty ? "Available with an upgraded plan." : "Part of " + MainWindow.friendly(tier), nil,
                rowAction("Start trial", { [self] in
                    edit("Start a free trial", [Field("tier", "Plan to try", tier).help(tier.isEmpty ? "Enter the plan name shown under Offers & referrals, for example plus." : "Filled in for this upgrade.")], { [self] values in
                        let body = try ApkContracts.autoTrial(v(values, "tier"))
                        if !(await confirm("Start a free trial of this plan? Read the terms first. A trial can turn into a paid subscription.")) { return }
                        try await change("POST", "/2.2/premium/customer/plans", body, true, "Trial requested.")
                    }, "Start trial")
                })))
        }
        if upgrades.isEmpty { upgrades.append(infoRow("No upgrades offered", nil, "eero isn't offering upgrades for this network right now.")) }
        group("Upgrades you can try", rows: upgrades)
    }
    func partnerOffersPage(_ data: Any?) {
        sub("Products from eero's partners. Getting an offer takes three steps: sign in to the partner, enter the code they send, then order.")
        var methods: [String] = []
        for m in MainWindow.entries(J.get(data, "methods"), "methods", "authentication_methods") {
            let value = (m as? String) ?? J.first(m, "authentication_method", "method", "type")
            if !value.isEmpty && !methods.contains(value) { methods.append(value) }
        }
        let session = J.first(J.get(data, "offers"), "session_id", "upsell_session_id").nullIfEmpty ?? J.first(J.get(data, "methods"), "session_id")
        let sessionField: (String) -> Field = { key in session.isEmpty ? Field(key, "Session ID").help("Shown by the partner when you start. eero didn't return one here.") : Field(key, "Session", session, "hidden") }
        let friendlyMethods = methods.map { MainWindow.friendly($0) }
        let chosenMethod: (String) -> String = { shown in methods.first { MainWindow.friendly($0) == shown } ?? shown }
        group("Sign in to the partner",
            settingRow("1. Send a sign-in code", "The partner sends a code by the method you choose.", nil, rowAction("Send code", { [self] in
                edit("Partner sign-in", [methods.isEmpty ? Field("method", "Sign-in method").help("For example EMAIL.") : Field("method", "Send the code by", friendlyMethods[0], "choice", choices: friendlyMethods), sessionField("session")], { [self] values in
                    let chosen = chosenMethod(v(values, "method"))
                    try await change("POST", "/2.2/partners/upsells/authentication_methods", try ApkContracts.upsellMethod(chosen, v(values, "session")), false, "Code requested. Enter it in step 2.")
                }, "Send code")
            })),
            settingRow("2. Enter the code", "Type the code the partner sent you.", nil, rowAction("Enter code", { [self] in
                edit("Verify partner sign-in", [Field("key", "Code"), methods.isEmpty ? Field("method", "Sign-in method") : Field("method", "Sent by", friendlyMethods[0], "choice", choices: friendlyMethods), sessionField("session")], { [self] values in
                    let chosen = chosenMethod(v(values, "method"))
                    try await change("PUT", "/2.2/partners/upsells/authentication", try ApkContracts.upsellAuthenticate(v(values, "key"), chosen, v(values, "session")), false, "Partner sign-in verified. Choose an offer in step 3.")
                }, "Verify")
            })))
        var offerRows: [Row] = []
        for offer in MainWindow.entries(J.get(data, "offers"), "offers") {
            let offerId = J.first(offer, "internal_offer_id", "offer_id", "id"); if offerId.isEmpty { continue }
            offerRows.append(settingRow(J.first(offer, "title", "name", "product_name").nullIfEmpty ?? "Partner offer", J.first(offer, "description", "partner_name", "partner").nullIfEmpty, nil, rowAction("Get offer", { [self] in
                edit("Get partner offer", [Field("offers", "Offer", offerId, "hidden"), sessionField("session"), Field("submit", "Place the order now", false, "bool").help("Leave off to get a price quote first. The partner may charge you when you order.")], { [self] values in
                    let body = try ApkContracts.upsellOrder(v(values, "offers"), v(values, "session"), flag(values, "submit"))
                    if flag(values, "submit"), !(await confirm("Place this partner order? The partner may charge you.")) { return }
                    if preview { throw AppError.invalidOperation("Sign in to order.") }
                    info("Partner order", try await api.send("POST", "/2.2/partners/upsells/orders", body))
                }, "Continue")
            })))
        }
        if offerRows.isEmpty { offerRows.append(infoRow("No partner offers", nil, "eero isn't showing partner offers for this account right now.")) }
        group("3. Choose an offer", rows: offerRows)
        group("Older services", settingRow("encrypt.me VPN", "A VPN that came with older eero Plus plans. New plans use Guardian instead.", nil, rowActionAsync("Create account", { [self] in
            if !(await confirm("Create an encrypt.me VPN account through eero? This is a legacy eero Plus partner service.")) { return }
            if preview { throw AppError.invalidOperation("Sign in first.") }
            info("encrypt.me", try await api.send("POST", "/2.2/partners/encryptme", try ApkContracts.encryptMe(), true))
        })))
    }

    // MARK: Help / report an issue
    var issueScreenshot: Data? { get { state["issueScreenshot"] as? Data } set { state["issueScreenshot"] = newValue } }
    var issueScreenshotName: String? { get { state["issueScreenshotName"] as? String } set { state["issueScreenshotName"] = newValue } }
    func reportIssuePage() {
        sub("Tell eero support what went wrong. Your report includes the eeros and devices you pick, and an optional screenshot.")
        let take = rowActionAsync("Take screenshot", { [self] in
            guard let path = try await captureScreenshot() else { return }
            defer { try? FileManager.default.removeItem(atPath: path) }
            try attachIssueScreenshot(path, "Screenshot.png")
        })
        let attach = rowActionAsync(issueScreenshot == nil ? "Attach" : "Replace", { [self] in
            guard let path = try await pickFile("Attach a screenshot", "PNG image", ["image/png"], ["*.png"]) else { return }
            try attachIssueScreenshot(path)
        })
        let write = rowAction("Write report", { [self] in writeIssueReport() }); accent(write); compactAction(write)
        group("Your report",
            settingRow("1. Add a screenshot (optional)", "A PNG image up to 8 MB showing the problem. Choose Take screenshot, or press Shift-Command-4 and attach the picture.", issueScreenshot == nil ? "None" : issueScreenshotName,
                take, attach, issueScreenshot != nil ? rowAction("Remove", { [self] in issueScreenshot = nil; issueScreenshotName = nil; renderPage() }) : nil),
            settingRow("2. Describe the problem and send", "Write a short summary, say what happened, and tick the affected eeros and devices.", nil, write))
        relatedPagesFor("Settings/Troubleshooting/Guided diagnostics", "Help/Contact support")
    }
    func attachIssueScreenshot(_ path: String, _ shownName: String? = nil) throws {
        let size = ((try? FileManager.default.attributesOfItem(atPath: path))?[.size] as? NSNumber)?.int64Value ?? 0
        if size > 8 * 1024 * 1024 { throw AppError.argument("Attach a PNG image up to 8 MB.") }
        let bytes: Data
        do { bytes = try Data(contentsOf: URL(fileURLWithPath: path)) } catch { throw AppError.io("The file could not be read.") }
        let name = shownName ?? (path as NSString).lastPathComponent
        try ApkContracts.screenshot(MultipartFields(), name, bytes)
        issueScreenshot = bytes; issueScreenshotName = name; renderPage()
    }
    /// macOS interactive capture: the person picks an area or window (Space toggles), Escape cancels. Returns a PNG path or nil.
    func captureScreenshot() async throws -> String? {
        let path = (NSTemporaryDirectory() as NSString).appendingPathComponent("eero-report-" + UUID().uuidString + ".png")
        let tool = "/usr/sbin/screencapture"
        if !FileManager.default.isExecutableFile(atPath: tool) { throw AppError.invalidOperation("Screenshots aren't available on this Mac. Choose Attach to add an image instead.") }
        let process = Process()
        process.executableURL = URL(fileURLWithPath: tool)
        process.arguments = ["-i", "-t", "png", path]
        do {
            try await withCheckedThrowingContinuation { (c: CheckedContinuation<Void, Error>) in
                process.terminationHandler = { _ in c.resume() }
                do { try process.run() } catch { process.terminationHandler = nil; c.resume(throwing: error) }
            }
        } catch { throw AppError.invalidOperation("Screenshots aren't available on this Mac. Choose Attach to add an image instead.") }
        NSApp.activate(ignoringOtherApps: true)
        return FileManager.default.fileExists(atPath: path) ? path : nil
    }
    func writeIssueReport() {
        let eeroChoices = nodeChoices(), deviceChoices = deviceChoices()
        var fields = [Field("summary", "Summary").help("One line, for example \"Wifi drops every evening\"."), Field("description", "What happened?", nil, "multiline").help("When it started, what you tried, and which rooms or devices are affected."), Field("labels", "Tags (optional)").help("Comma separated, for example slow, upstairs.")]
        fields.append(contentsOf: eeroChoices.keys.map { Field("e:" + $0, "Affected eero: " + $0, false, "bool") })
        fields.append(contentsOf: deviceChoices.keys.prefix(40).map { Field("d:" + $0, "Affected device: " + $0, false, "bool") })
        edit("Report an issue", fields, { [self] values in
            let body = try ApkContracts.issue(v(values, "summary"), v(values, "description"), ApkContracts.lines(v(values, "labels")),
                                              eeroChoices.filter { flag(values, "e:" + $0.key) }.map { J.s($0.value, "serial") },
                                              deviceChoices.filter { flag(values, "d:" + $0.key) }.map { J.s($0.value, "mac") },
                                              (networkUrl ?? "").isEmpty ? nil : networkId, Date())
            if let shot = issueScreenshot { try ApkContracts.screenshot(body, issueScreenshotName ?? "Screenshot.png", shot) }
            if !(await confirm("Send this report to eero support?")) { return }
            if preview { throw AppError.invalidOperation("Sign in to send a report.") }
            try await api.send("POST", "/2.2/zendesk/create", body); issueScreenshot = nil; issueScreenshotName = nil; renderPage(); setNotice("Report sent to eero support.")
        }, "Send report", "Your summary, description, selected eeros and devices, and the optional screenshot are sent to eero support.")
    }

    func loadApkFeature(_ route: String) async throws -> Any? {
        switch route {
        case "Settings/Account settings/Trusted certificates": return try await api.send("GET", "/2.2/account/trust")
        case "Settings/Account settings/Subscriptions/Entitlements & trials":
            return J.obj("features", try await api.send("GET", "/2.2/entitlements/networks/" + networkId + "/features"), "upsell_features", try await api.send("GET", "/2.2/entitlements/networks/" + networkId + "/upsell_features"))
        case "Settings/Account settings/Subscriptions/Partner offers":
            return J.obj("offers", try await api.send("GET", "/2.2/partners/upsells/offers" + ((networkUrl ?? "").isEmpty ? "" : "?opt_network_id=" + networkId.escapedData)), "methods", try await api.send("GET", "/2.2/partners/upsells/authentication_methods"))
        default: return nil
        }
    }
    func renderApkFeature(_ data: Any?) -> Bool {
        switch currentPage {
        case "Home/Add or replace eero/Setup tools": setupToolsPage(); return true
        case "Activity/Usage details": usageDetailsPage(); return true
        case "Settings/Advanced networking/Network flags": networkFlagsPage(); return true
        case "Settings/Network management/Organization & community": organizationPage(); return true
        case "Settings/Account settings/Trusted certificates": trustPage(data); return true
        case "Settings/Account settings/Account data": accountDataPage(); return true
        case "Settings/Account settings/Subscriptions/Entitlements & trials": entitlementsPage(data); return true
        case "Settings/Account settings/Subscriptions/Partner offers": partnerOffersPage(data); return true
        case "Help/Report an issue": reportIssuePage(); return true
        default: return false
        }
    }
    func warmChat(_ networkId: String?) async throws {
        // POST 2.2/conversations/warmup prepares the assistant before the first message; failures are ignored.
        do {
            if !preview && api.signedIn, let id = networkId, !id.isEmpty { try await api.send("POST", "/2.2/conversations/warmup", try ConversationContracts.request(id, nil, nil)) }
        }
        catch let e as ApiException { if e.sessionExpired { throw e } }
        catch is URLError {}
    }
}
