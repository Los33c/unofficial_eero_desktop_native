import AppKit
import Foundation
import SwiftUI

// Translated from source/ProfilesSecurityFeatures.cs (Linux port: Linux/src/Features/ProfilesSecurityFeatures.cs).
extension MainWindow {
    func profilesPageV2() { nativeProfiles() }
    func profileDetailPage(_ profile: Any?) { nativeProfileDetail(profile) }
    func profilePolicyUrl(_ profile: Any?) throws -> String { try featureUrl("dns_policies/profiles/" + (J.s(profile, "url").trimEnd("/").split("/").last ?? "")) }

    func profileDevices(_ profile: Any?) {
        let current = Set(J.list(J.get(profile, "devices")).map { J.s($0, "url") })
        var seen = Set<String>(); var items: [ProfileDeviceChoice] = []
        for device in devices + J.list(J.get(profile, "devices")) {
            let url = J.s(device, "url")
            if url.isEmpty || seen.contains(url) { continue }
            seen.insert(url); items.append(ProfileDeviceChoice(url: url, label: MainWindow.device(device).name + " · " + J.s(device, "mac")))
        }
        let state = ProfileDevicesState(items, current)
        var handle: DialogHandle? = nil
        let save: () -> Void = { [self] in
            if state.working { return }
            state.working = true; handle?.canClose = false
            Task { @MainActor in
                do {
                    let selected: [Any] = state.items.filter { state.checked[$0.url] ?? false }.map { J.obj("url", $0.url) }
                    try await self.change("PUT", J.s(profile, "url"), J.obj("devices", selected), false, "Profile devices updated.")
                    state.working = false; handle?.canClose = true; handle?.close()
                } catch { state.status = MainWindow.message(error) }
                state.working = false; handle?.canClose = true
            }
        }
        handle = dialog("Profile devices", [text("Assign profile devices", 24, "#17263C", true), text("Choose the devices that belong to " + J.s(profile, "name") + ".", 13, "#64738A"),
                                            .view(ProfileDevicesView(state: state, save: save))])
        handle?.showDialog()
    }

    func securityPage(_ data: Any?) {
        sub("Protection for every device on your network. Some options need an eero Plus subscription.")
        let map = (J.get(data, "dns_policies") as? JObject) ?? (data as? JObject)
        var rows: [Row] = []
        if let map = map {
            for (key, value) in map {
                guard let on = value as? Bool, let label = MainWindow.policyLabels[key] else { continue }
                rows.append(settingToggle(label[0], label[1], on, { [self] on in
                    try await change("POST", try featureUrl("dns_policies/network"), J.obj(key, on), true, label[0] + (on ? " turned on." : " turned off."))
                }))
            }
        }
        if rows.isEmpty { rows.append(settingRow("Network protection", data == nil ? "eero hasn't returned your protection settings. Choose Refresh at the top right to try again." : "Your plan doesn't report protection settings.", nil, rowAction("Open", { [self] in try editPolicies(data, try featureUrl("dns_policies/network")) }))) }
        group("Protection for the whole network", rows: rows)
        let ad = J.get(data, "ad_block_settings")
        group("Websites & ads",
            settingRow("Block & allow websites", "Always block a website, or let one through that a filter blocks.", nil, rowActionAsync("Manage", { [self] in domainDialog(try await read(try featureUrl("dns_policies/advanced_content_filter")), nil) })),
            ad != nil ? settingToggle("Ad blocking for profiles", "Block ads on the profiles eero lists for ad blocking.", J.b(ad, "enabled"), { [self] on in
                try await change("POST", try featureUrl("dns_policies/adblock"), J.obj("enable", on, "profiles", J.list(J.get(ad, "profiles"))), false, "Ad blocking " + (on ? "turned on." : "turned off."))
            }) : nil)
        relatedPagesFor("Home/Profiles", "Home/Security & privacy/Content filters", "Activity/Security activity", "Home/Security & privacy/VPN", "Home/Security & privacy/Identity protection")
        securityApkActions()
    }

    /// Friendly names and explanations for eero's content-filter switches.
    static let policyLabels: [String: [String]] = [
        "block_malware": ["Block threats", "Stops known malware, phishing and scam websites."],
        "ad_block": ["Block ads", "Removes most ads and trackers on every device."],
        "block_illegal_content": ["Block illegal content", "Blocks sites known for illegal activity."],
        "block_pornographic_content": ["Block adult content", "Filters adult websites for everyone on the network."],
        "block_violent_content": ["Block violent content", "Filters violent and gory websites."],
        "safe_search_enabled": ["SafeSearch", "Forces safe results on Google, Bing and other search engines."],
        "youtube_restricted": ["YouTube Restricted Mode", "Hides videos YouTube marks as mature."],
        "block_messaging_content": ["Block messaging apps", "Blocks chat and messaging services."],
        "block_social_content": ["Block social media", "Blocks social networks."],
        "block_gaming_content": ["Block gaming", "Blocks online games and gaming services."],
        "block_streaming_content": ["Block streaming", "Blocks video and music streaming."],
        "block_shopping_content": ["Block shopping", "Blocks online stores."]]

    func editPolicies(_ data: Any?, _ url: String) throws {
        var map = J.get(data, "dns_policies") as? JObject
        let unified = J.get(data, "unified_content_filters", "dns_policies") as? JObject
        if let unified = unified {
            let merged = JObject()
            if let m = map { for (k, value) in m { merged[k] = value } }
            for (k, value) in unified { merged[k] = value }
            map = merged
        }
        if map == nil { map = data as? JObject }
        guard let policies = map else { throw AppError.invalidOperation("eero did not return editable content-filter settings.") }
        let allowed = ["ad_block", "block_illegal_content", "block_malware", "block_pornographic_content", "block_violent_content", "safe_search_enabled", "youtube_restricted", "block_messaging_content", "block_social_content", "block_gaming_content", "block_streaming_content", "block_shopping_content"]
        let fields = policies.filter { $0.value is Bool && (allowed.contains($0.key) || unified != nil) }.map { Field($0.key, MainWindow.human($0.key), $0.value, "bool") }
        if fields.isEmpty { throw AppError.invalidOperation("No editable content-filter settings were returned for this account.") }
        edit("Security & content filters", fields, { [self] vals in
            let changed = JObject()
            for f in fields where flag(vals, f.key) != ((f.value as? Bool) ?? false) { changed[f.key] = flag(vals, f.key) }
            if changed.count == 0 { return }
            try await change("POST", url, changed, true, "Security settings updated.")
        })
    }

    func domainDialog(_ data: Any?, _ profile: Any?) {
        let profileId: String? = profile == nil ? nil : (J.s(profile, "url").trimEnd("/").split("/").last ?? "")
        var handle: DialogHandle? = nil
        let add = localButton("Add or remove a website rule", { [self] in
            handle?.close()
            edit("Website rule", [Field("domain", "Domain, e.g. example.com"), Field("list", "Rule", "Block", "choice", "Block", "Allow"), Field("remove", "Remove this rule instead of adding it", false, "bool")], { [self] vals in
                let body = J.obj("domain", try FeatureContracts.domain(v(vals, "domain")), "is_delete", flag(vals, "remove"))
                if let id = profileId { body["profiles"] = [id] }
                let path = try featureUrl("dns_policies/" + (profileId == nil ? "network/" : "profiles/") + (v(vals, "list") == "Allow" ? "allowed" : "blocked"))
                try await change("PUT", path, body, false, "Website rule updated.")
            })
        })
        var p: [Block] = [text("Block & allow websites", 24, "#17263C", true), text(profile == nil ? "Applies to the whole network." : "Applies to " + J.s(profile, "name") + ".", 13, "#64738A"),
                          .padded([.buttons([add])], EdgeInsets(top: 0, leading: 0, bottom: 12, trailing: 0))]
        p.append(contentsOf: details(data, 0))
        handle = dialog("Website controls", p)
        handle?.showDialog()
    }

    func blockApps(_ profile: Any?) async throws {
        let baseUrl = try profilePolicyUrl(profile) + "/applications"; let available = try await read(baseUrl)
        let list = entries(available, "applications", "data")
        if list.isEmpty { info("Blocked applications", available); return }
        var fields: [Field] = []; let current = Set(J.list(J.get(profile, "premium_dns", "blocked_applications")).map { J.s($0) })
        for app in list {
            let id = J.first(app, "id", "slug", "name")
            if !id.isEmpty { fields.append(Field(id, J.first(app, "display_name", "name", "slug"), current.contains(id) || J.b(app, "is_blocked"), "bool")) }
        }
        if fields.isEmpty { info("Available app controls", available); return }
        edit("Blocked applications", fields, { [self] vals in
            let selected = fields.filter { flag(vals, $0.key) }.map { $0.key }
            try await change("PUT", baseUrl + "/blocked", J.obj("applications", selected), false, "Blocked applications updated.")
        })
    }

    func nodeDetailsPage(_ node: Any?) {
        if node == nil { page.append(text("Select an eero from Home.")); return }
        let url = J.s(node, "url"); sub("Connection details and settings for this eero.")
        let online = MainWindow.isOnline(node)
        page.append(card([.view(FeatureImageBeside(image: hardwareImage(node, 72), size: 72, spacing: 24,
                                                   content: [text(J.s(node, "model"), 22, "#17263C", true), text(J.b(node, "gateway") ? "Gateway eero" : "Mesh eero", 13, "#64738A"), statusBadge(online ? "Online" : "Offline", online)]))], 20))
        var facts: [Block] = [text("Device information", 20, "#17263C", true)]
        detail(&facts, "IP address", J.first(node, "ip_address", "ip")); detail(&facts, "Connected devices", J.s(node, "connected_clients_count"))
        detail(&facts, "eeroOS version", J.first(node, "os_version", "firmware_version")); detail(&facts, "Serial number", J.s(node, "serial"))
        detail(&facts, "MAC address", J.first(node, "mac_address", "mac"))
        let technical = quiet(localButton("All device details", { [self] in info("eero details", node) }))
        let ledUrl = resource(node, "led_action", "").nullIfEmpty, connections = resource(node, "connections", "").nullIfEmpty, serial = J.s(node, "serial")
        let brightness = Int(J.s(node, "led_brightness").trim())
        var lights: [Row] = []
        if let ledUrl = ledUrl {
            lights.append(settingToggle("Status light", "The light on top of the eero. Turn it off if it's distracting.", J.b(node, "led_on"), { [self] on in
                try await change("PUT", ledUrl, J.obj("led_on", on), true, on ? "Status light turned on." : "Status light turned off.")
            }))
            lights.append(settingRow("Light brightness", nil, brightness.map { "\($0)%" } ?? "Not reported", rowAction("Change", { [self] in
                edit("Status light brightness", [Field("brightness", "Brightness (0–100%)", J.s(node, "led_brightness"))], { [self] vals in
                    guard let n = Int(v(vals, "brightness").trim()), n >= 0, n <= 100 else { throw AppError.argument("Enter a brightness from 0 to 100.") }
                    try await change("PUT", ledUrl, J.obj("led_brightness", n), true, "Brightness updated.")
                })
            })))
        }
        if J.get(node, "nightlight") != nil { lights.append(settingRow("Nightlight", "A soft light, on a schedule if you like.", J.b(node, "nightlight", "enabled") ? "On" : "Off", rowAction("Change", { [self] in nightlightEdit(node) }))) }
        if !serial.isEmpty { lights.append(settingRow("Find this eero", "Flashes the status light so you can spot it.", nil, rowAction("Flash light", { [self] in try identifyEero(serial) }))) }
        var settings: [Block] = [groupHeading("Lights")]
        if lights.isEmpty { lights.append(infoRow("No light settings", nil, "This eero doesn't report light controls.")) }
        settings.append(rowGroup(lights, 14))
        settings.append(groupHeading("Connections & history"))
        settings.append(rowGroup([
            connections != nil ? settingRow("Ethernet ports", "See what's plugged in and control each port.", nil, rowActionAsync("Open", { [self] in portDialog(node, try await read(connections!)) })) : nil,
            settingRow("Software update history", "Past updates installed on this eero.", nil, rowActionAsync("View", { [self] in info("Software update history", try await read("/2.2/eeros/" + (url.trimEnd("/").split("/").last ?? "") + "/firmware_update/history?limit=20&offset=0")) })),
            settingRow("Data usage", "How much data went through this eero this week.", nil, rowActionAsync("View", { [self] in info("eero data usage", try await read(try apiNetworkPath("data_usage/eeros/" + (try MainWindow.nodeId(node)) + "?") + statsRange() + "&cadence=daily")) })),
            !serial.isEmpty ? settingRow("Placement rating", "Ask eero to rate where this eero is placed.", nil, rowAction("Rate", { [self] in try placementRatingEdit(serial) })) : nil
        ].compactMap { $0 }, 14))
        nodeApkActions(node, &settings, &facts)
        settings.append(groupHeading("Manage"))
        settings.append(rowGroup([
            settingRow("Restart", "Connected devices lose internet for a minute or two.", nil, rowActionAsync("Restart", { [self] in
                if !(await confirm("Restart this eero? Connected devices may lose internet access temporarily.")) { return }
                try await change("POST", resource(node, "reboot", url + "/reboot"), nil, false, "Restart requested.")
            })),
            settingRow("Remove from network", J.b(node, "gateway") ? "This is your gateway. Removing it takes the whole network offline." : "The eero stops being part of your wifi.", nil, rowAction("Remove", { [self] in
                edit("Remove eero", [Field("confirm", "Type the eero location name to confirm").help("Location: " + J.s(node, "location"))], { [self] vals in
                    if v(vals, "confirm") != J.s(node, "location") { throw AppError.argument("The location name does not match.") }
                    if !(await confirm("Remove this eero from the network? Removing the gateway can take the entire network offline.")) { return }
                    currentPage = "Home/eeros"; selectedNode = nil
                    try await change("DELETE", url, nil, false, "eero removed.")
                }, "Remove eero")
            }, true))], 14))
        // The information card is built last so rows nodeApkActions adds to it are included (WPF added them to the live panel).
        facts.append(.padded([.buttons([technical])], EdgeInsets(top: 8, leading: 0, bottom: 0, trailing: 0)))
        page.append(columns([card(facts)], settings))
    }

    func nightlightEdit(_ node: Any?) {
        let light = J.get(node, "nightlight")
        edit("Nightlight", [Field("enabled", "Enable nightlight", J.b(light, "enabled"), "bool"), Field("brightness", "Brightness (0–100)", J.s(light, "brightness_percentage")), Field("scheduled", "Use a schedule", J.b(light, "schedule", "enabled"), "bool"), Field("on", "On (HH:mm)", J.s(light, "schedule", "on").nullIfEmpty ?? "20:00"), Field("off", "Off (HH:mm)", J.s(light, "schedule", "off").nullIfEmpty ?? "07:00")], { [self] vals in
            guard let brightness = Int(v(vals, "brightness").trim()), brightness >= 0, brightness <= 100 else { throw AppError.argument("Enter a brightness from 0 to 100.") }
            _ = try FeatureContracts.schedule("Nightlight", ["MON"], v(vals, "on"), v(vals, "off"), true)
            try await change("PUT", J.s(node, "url") + "/nightlight/settings", J.obj("enabled", flag(vals, "enabled"), "brightness_percentage", brightness, "schedule", J.obj("enabled", flag(vals, "scheduled"), "on", v(vals, "on"), "off", v(vals, "off"))), false, "Nightlight updated.")
        })
    }

    /// Port actions eero offers, in plain words. Anything not listed (for example cable tests) is not offered here.
    static let portActions: [String: String] = [
        "ENABLE_PORT": "Turn port on", "DISABLE_PORT": "Turn port off", "ENABLE_DATA": "Turn data on", "DISABLE_DATA": "Turn data off",
        "ENABLE_POE": "Turn PoE power on", "DISABLE_POE": "Turn PoE power off", "RESTART_POWER": "Restart PoE power",
        "ENABLE_PORT_SECURITY": "Turn port security on", "DISABLE_PORT_SECURITY": "Turn port security off"]

    static func portState(_ status: String?) -> String {
        let s = (status ?? "").uppercased()
        if s.isEmpty { return "Not reported" }
        if s.contains("DISCONNECTED") || s.contains("NOT_CONNECTED") || s == "DOWN" || s == "NO_LINK" { return "Nothing plugged in" }
        if s.contains("DISABLED") { return "Turned off" }
        if s.contains("CONNECTED") || s == "UP" { return "Connected" }
        return friendly(status)
    }

    func portDialog(_ node: Any?, _ connections: Any?) {
        var p: [Block] = [text("Ethernet ports", 24, "#17263C", true), text("What's plugged into each port on this eero, how fast it's connected, and controls for each port.", 13, "#64738A")]
        var handle: DialogHandle? = nil
        var working = false
        let status = FeatureDialogStatus()
        let begin = { working = true; handle?.canClose = false }
        let end = { working = false; handle?.canClose = true }
        let ports = J.list(J.get(connections, "ports", "interfaces"))
        if ports.isEmpty { p.append(card([text("eero didn't report any Ethernet ports for this eero.", 14, "#64738A")], 16)) }
        for raw in ports {
            let port: Any? = raw
            let number = J.s(port, "interface_number"), name = J.s(port, "name")
            let title = !name.isEmpty && !Regex.isMatch(name, "^[0-9]+$") ? name : "Port " + (number.nullIfEmpty ?? name)
            let state = MainWindow.portState(J.s(port, "port_status"))
            let connected = state == "Connected"
            var section: [Block] = [.split(left: [text(title, 18, "#17263C", true)], right: [statusBadge(state, connected)])]
            var rows: [Block] = []
            let current = MainWindow.friendlyValue("negotiated_speed", J.get(port, "negotiated_speed")), max = MainWindow.friendlyValue("supported_speed", J.get(port, "supported_speed"))
            if !current.isEmpty || !max.isEmpty {
                rows.append(detailRow("Speed", connected && !current.isEmpty ? current + (!max.isEmpty && max != current ? " (port can do up to " + max + ")" : "") : (!max.isEmpty ? "Up to " + max : current), false))
            }
            let networkType = MainWindow.friendly(J.s(port, "network_type")); if !networkType.isEmpty && networkType != "Unknown" { rows.append(detailRow("Used for", networkType, false)) }
            let poe = J.first(port, "poe_status", "poe_state"); if !poe.isEmpty { rows.append(detailRow("PoE power", MainWindow.friendly(poe), false)) }
            let mac = J.s(port, "ethernet_address"); if !mac.isEmpty { rows.append(detailRow("Port MAC address", mac, false)) }
            section.append(.padded(rows, EdgeInsets(top: 10, leading: 0, bottom: 0, trailing: 0)))
            var actions: [ButtonSpec] = []
            for action in J.list(J.get(port, "actions")) {
                let type = J.s(action, "type"); guard let actionLabel = MainWindow.portActions[type] else { continue }
                let b = ButtonSpec(actionLabel, local: true, action: { [self] in
                    if working { return }
                    if !(await confirm(actionLabel + " on " + title + "? Anything plugged into it may lose its connection or power for a moment.")) { return }
                    begin()
                    do { try await change("POST", J.s(node, "url") + "/ports/" + J.s(port, "interface_number") + "/action", J.obj("action", type), false, actionLabel + " requested for " + title + "."); end(); handle?.close() }
                    catch { status.text = MainWindow.message(error) }
                    end()
                })
                if type.hasPrefix("DISABLE") || type == "RESTART_POWER" { danger(b) }
                actions.append(b)
            }
            if !actions.isEmpty { section.append(.padded([.buttons(actions)], EdgeInsets(top: 12, leading: 0, bottom: 0, trailing: 0))) }
            // Everything else eero reports about the port, folded away for support conversations.
            section.append(.padded([.expander(title: "More details", content: details(port, 0))], EdgeInsets(top: 6, leading: 0, bottom: 0, trailing: 0)))
            p.append(card(section, 16))
        }
        for action in J.list(J.get(connections, "node_actions")) {
            let type = J.s(action, "type"); if type != "POWER_CYCLE_ALL_PORTS" && type != "POWER_CYCLE_ALL_PORTS_AND_REBOOT" { continue }
            let b = ButtonSpec(type == "POWER_CYCLE_ALL_PORTS" ? "Restart power on every port" : "Restart power on every port and the eero", local: true, action: { [self] in
                if working { return }
                if !(await confirm("Apply this action to every Ethernet port on this eero? Devices may lose power and internet access.")) { return }
                begin()
                do { try await change("POST", J.s(node, "url") + "/action", J.obj("action", type), false, "eero port action requested."); end(); handle?.close() }
                catch { status.text = MainWindow.message(error) }
                end()
            })
            danger(b)
            p.append(.padded([.buttons([b])], EdgeInsets(top: 0, leading: 0, bottom: 8, trailing: 0)))
        }
        p.append(.view(FeatureDialogStatusText(status: status)))
        handle = dialog("Ethernet ports", p, 640)
        handle?.showDialog()
    }

    /// Everything about one device: what it is, then each setting next to the control that changes it.
    /// Used by the Devices page panel and the device dialog; close runs before any change is sent.
    func devicePanel(_ device: Any?, _ close: @escaping () -> Void) -> [Block] {
        let row = MainWindow.device(device); let mac = J.s(device, "mac")
        let url = J.s(device, "url").nullIfEmpty ?? (networkUrl ?? "") + "/devices/" + mac.escapedData
        let paused = J.b(device, "paused"), blocked = J.b(device, "blocked")
        var parts: [Block] = []
        var facts: [Block] = []
        for fact in [["Connection", row.connection], ["IP address", row.ip], ["MAC address", mac], ["Maker", J.s(device, "manufacturer")], ["Connected to", J.s(device, "source", "location")]] where !fact[1].isEmpty {
            facts.append(detailRow(fact[0], fact[1], false))
        }
        parts.append(card(facts, 14))
        var profileChoices: [(label: String, url: String)] = []
        for p in profiles where !J.s(p, "url").isEmpty {
            let label = J.s(p, "name") + " · " + (J.s(p, "url").split("/").last ?? "")
            if !profileChoices.contains(where: { $0.label == label }) { profileChoices.append((label, J.s(p, "url"))) }
        }
        let names = FeatureContracts.deviceTypes.map { (label: MainWindow.human($0), type: $0) }
        let type = J.s(device, "device_type")
        let settings: [Row?] = [
            settingToggle("Internet access", paused ? "Paused. This device can't go online." : "Pause to stop this device going online.", !paused, { [self] on in
                if !(await confirm((on ? "Resume" : "Pause") + " internet for " + row.name + "?")) { return }
                close(); try await mutate("PUT", url, J.obj("paused", !on), on ? "Internet resumed." : "Internet paused.")
            }),
            settingRow("Nickname", nil, row.name, rowAction("Change", { [self] in
                close()
                edit("Device nickname", [Field("name", "Nickname", row.name).help("Shown instead of the device's own name.")], { [self] vals in
                    try MainWindow.validateName(v(vals, "name"))
                    try await mutate("PUT", url, J.obj("nickname", v(vals, "name").trim()), "Device nickname updated.")
                })
            })),
            !profileChoices.isEmpty ? settingRow("Profile", "Profiles let you pause and filter groups of devices.", row.profile.nullIfEmpty ?? "None", rowAction("Change", { [self] in
                close()
                edit("Device profile", [Field("profile", "Profile", profileChoices.first { $0.label.hasPrefix(row.profile + " · ") }?.label ?? profileChoices[0].label, "choice", choices: profileChoices.map { $0.label })], { [self] vals in
                    let chosen = profileChoices.first { $0.label == v(vals, "profile") }?.url ?? ""
                    try await change("PUT", url, J.obj("profile", J.obj("url", chosen)), false, "Device profile updated.")
                })
            })) : nil,
            settingRow("Device type", "Sets the icon eero shows for it.", FeatureContracts.deviceTypes.contains(type) ? MainWindow.human(type) : "Not set", rowAction("Change", { [self] in
                close()
                let current = FeatureContracts.deviceTypes.contains(type) ? MainWindow.human(type) : MainWindow.human("generic")
                edit("Device type", [Field("type", "Type", current, "choice", choices: names.map { $0.label }.sorted { $0.localizedCompare($1) == .orderedAscending })], { [self] vals in
                    let chosen = names.first { $0.label == v(vals, "type") }?.type ?? ""
                    try await change("PUT", try featureUrl("devices/" + mac.escapedData), J.obj("device_type", chosen), false, "Device type updated.")
                })
            })),
            J.get(device, "secondary_wan_deny_access") is Bool ? settingToggle("Use internet backup", "Let this device use the backup connection when your internet is down.", !J.b(device, "secondary_wan_deny_access"), { [self] on in
                close(); try await change("PUT", "/2.3/networks/" + networkId + "/devices/" + mac.escapedData, J.obj("secondary_wan_deny_access", !on), false, "Backup access " + (on ? "allowed." : "turned off."))
            }) : nil,
            settingRow("Data usage", "How much data it used this week.", nil, rowActionAsync("View", { [self] in
                close(); info("Device data usage", try await read(try featureUrl("data_usage/devices/" + mac.escapedData + "?") + statsRange() + "&cadence=daily"))
            })),
            settingRow(blocked ? "Blocked" : "Block device", blocked ? "This device can't join your network." : "Stops it joining your network at all, on every eero.", nil, rowActionAsync(blocked ? "Unblock" : "Block", { [self] in
                if !(await confirm((blocked ? "Allow " : "Block ") + row.name + " on this network?" + (blocked ? "" : " If it's this Mac, the app may lose its connection."))) { return }
                close(); try await change(blocked ? "DELETE" : "POST", try featureUrl("blacklist" + (blocked ? "/" + mac.escapedData : "")), blocked ? nil : J.obj("mac", mac), true, blocked ? "Device unblocked." : "Device blocked.")
            }, !blocked)),
            settingRow("All details", "Everything eero reports about this device.", nil, rowAction("View", { [self] in close(); info("Device details", device) }))]
        parts.append(rowGroup(settings.compactMap { $0 }, 14))
        return parts
    }

    func addEeroPage() {
        sub("Grow your wifi with another eero. Plug the new eero in near an existing one first.")
        group("Add an eero",
            settingRow("Add by serial number", "The quickest way. The serial number is on the bottom of the eero.", nil, rowAction("Add eero", { [self] in
                edit("Add eero", [Field("serial", "Serial number").help("Printed on the bottom of the eero."), Field("location", "Where is it?").help("For example Kitchen or Upstairs.")], { [self] vals in
                    let serial = v(vals, "serial").trim()
                    if !Regex.isMatch(serial, "^[A-Za-z0-9-]{8,40}$") { throw AppError.argument("Enter the serial number printed on the eero.") }
                    try MainWindow.validateName(v(vals, "location"))
                    if !(await confirm("Add the eero with this serial number to the selected network?")) { return }
                    try await change("POST", "/2.2/eeros?requireSession=true", J.obj("serial", serial, "location", v(vals, "location").trim(), "network", networkUrl), true, "eero addition requested.")
                }, "Add eero")
            })))
        addGroupHeading("Other ways")
        featureMenu(currentPage, false)
        page.append(text("Replacing your gateway eero and full Bluetooth setup still need the official eero app.", 13, "#64738A"))
    }
}

/// A product image (or other fixed-size block) beside text, vertically centred (WPF two-column header grid).
struct FeatureImageBeside: View {
    let image: Block; let size: CGFloat; let spacing: CGFloat; let content: [Block]
    var body: some View {
        HStack(alignment: .center, spacing: spacing) {
            BlockView(block: image).frame(width: size, height: size)
            BlocksView(blocks: content)
        }
    }
}

/// Error text shown inside a dialog that runs its own actions (Ethernet ports).
@MainActor
final class FeatureDialogStatus: ObservableObject { @Published var text = "" }
struct FeatureDialogStatusText: View {
    @ObservedObject var status: FeatureDialogStatus
    var body: some View {
        if !status.text.isEmpty { Text(status.text).font(.system(size: 13)).foregroundColor(Tone.danger.color).fixedSize(horizontal: false, vertical: true).padding(.bottom, 8) }
    }
}

struct ProfileDeviceChoice: Identifiable { let url: String; let label: String; var id: String { url } }

@MainActor
final class ProfileDevicesState: ObservableObject {
    let items: [ProfileDeviceChoice]
    @Published var checked: [String: Bool] = [:]
    @Published var status = ""
    @Published var working = false
    init(_ items: [ProfileDeviceChoice], _ current: Set<String>) {
        self.items = items
        for i in items { checked[i.url] = current.contains(i.url) }
    }
}

struct ProfileDevicesView: View {
    @ObservedObject var state: ProfileDevicesState
    let save: () -> Void
    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ForEach(state.items) { item in
                Toggle(item.label, isOn: Binding(get: { state.checked[item.url] ?? false }, set: { state.checked[item.url] = $0 })).toggleStyle(.checkbox)
            }
            if !state.status.isEmpty { Text(state.status).font(.system(size: 13)).foregroundColor(Tone.danger.color).fixedSize(horizontal: false, vertical: true) }
            Button("Save device assignments", action: save).disabled(state.working).padding(.top, 6)
        }
    }
}
