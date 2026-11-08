import AppKit
import Foundation
import SwiftUI

// Translated from source/NetworkingFeatures.cs (Linux port: Linux/src/Features/NetworkingFeatures.cs).
extension MainWindow {
    func wifiPage() {
        sub("The name and password your devices use to join your main wifi network.")
        group("Your wifi network",
            settingRow("Wifi name", "The network name devices see when they look for wifi.", J.s(network, "name"),
                rowAction("Change", { [self] in
                    edit("Wifi name", [Field("name", "Wifi name", J.s(network, "name")).help("Up to 32 characters. Changing it disconnects every device until it rejoins.")], { [self] vals in
                        try MainWindow.validateSsid(v(vals, "name"))
                        if !(await confirm("Change the wifi name? Devices may need to reconnect.")) { return }
                        try await change("PUT", networkUrl ?? "", J.obj("name", v(vals, "name").trim()), true, "Wifi name updated.")
                    })
                })),
            settingRow("Wifi password", "Devices need the new password after you change it.", MainWindow.hidden,
                rowActionAsync("Show", { [self] in showPassword(try await wifiPassword(network, try netResource("password"))) }),
                rowAction("Change", { [self] in
                    edit("Wifi password", [Field("password", "New password", nil, "password").help("8–63 characters. Use Generate a strong password below if you want a suggestion.")], { [self] vals in
                        try MainWindow.validatePassword(v(vals, "password"))
                        if !(await confirm("Change the wifi password? Devices will need the new password to reconnect.")) { return }
                        try await change("PUT", try netResource("password"), J.obj("password", v(vals, "password")), true, "Wifi password updated.")
                    })
                })))
        group("Share & tools",
            settingRow("Share with a QR code", "Phones can scan it to join without typing the password.", nil,
                rowActionAsync("Show QR code", { [self] in showWifiQr(J.s(network, "name"), try await wifiPassword(network, try netResource("password"))) })),
            settingRow("Copy wifi details", "Copies the name and password to the clipboard.", nil,
                rowActionAsync("Copy", { [self] in
                    let password = try await wifiPassword(network, try netResource("password"))
                    networkingCopy("Wifi: " + J.s(network, "name") + "\n" + "Password: " + password)
                    setNotice("Wifi details copied to the clipboard.")
                })),
            settingRow("Generate a strong password", "Suggests a password. Nothing changes until you use Change above.", nil,
                rowActionAsync("Generate", { [self] in try await generateWifiPassword() })))
    }

    func wifiPassword(_ data: Any?, _ url: String) async throws -> String {
        let result = try await read(url)
        let password = (result as? String) ?? J.s(result, "password")
        if password.isEmpty { throw AppError.invalidOperation("eero did not return the wifi password for this account.") }
        return password
    }

    func showPassword(_ password: String) {
        let secret = NetworkingSecretText(password)
        let handle = dialog("Wifi password", [text("Wifi password", 24, "#17263C", true), .view(NetworkingSecretField(secret: secret, label: "Wifi password"))], 620, 230)
        handle.onClosed = { secret.value = "" }
        handle.showDialog()
    }

    func guestPage(_ data: Any?) {
        sub("A separate wifi network for your guests.")
        if data == nil { page.append(text("Loading guest network…", 14, "#64738A")); return }
        let guestUrl = MainWindow.objectUrl(data, (try? netResource("guestnetwork")) ?? "") ?? ""
        let passwordUrl = resource(data, "password", guestUrl + "/password")
        let name = J.s(data, "name"), enabled = J.b(data, "enabled")
        group(nil, settingToggle("Guest network", "Visitors get internet access without joining your main network or seeing your devices.", enabled, { [self] on in
            if !on, !(await confirm("Turn off guest wifi? Devices on the guest network will disconnect.")) { return }
            try await change("PUT", guestUrl, J.obj("name", name, "enabled", on), true, on ? "Guest wifi turned on." : "Guest wifi turned off.")
        }))
        group("Guest network details",
            settingRow("Guest wifi name", nil, name.nullIfEmpty ?? "Not set",
                rowAction("Change", { [self] in
                    edit("Guest wifi name", [Field("name", "Guest wifi name", name).help("Pick a name that's different from your main wifi.")], { [self] vals in
                        try MainWindow.validateSsid(v(vals, "name"))
                        try await change("PUT", guestUrl, J.obj("name", v(vals, "name").trim(), "enabled", enabled), true, "Guest wifi name updated.")
                    })
                })),
            settingRow("Guest password", "Guests need the new password after you change it.", MainWindow.hidden,
                rowActionAsync("Show", { [self] in showPassword(try await wifiPassword(data, passwordUrl)) }),
                rowAction("Change", { [self] in
                    edit("Guest password", [Field("password", "New guest password", nil, "password").help("8–63 characters.")], { [self] vals in
                        try MainWindow.validatePassword(v(vals, "password"))
                        try await change("PUT", passwordUrl, J.obj("password", v(vals, "password")), true, "Guest password updated.")
                    })
                })),
            settingRow("Share guest wifi", "Show a QR code guests can scan to join.", nil,
                rowActionAsync("Show QR code", { [self] in showWifiQr(name, try await wifiPassword(data, passwordUrl)) })))
    }

    func nicknamePage() {
        sub("How this network is labelled, and the timezone its schedules follow.")
        let nickname = J.first(network, "nickname_label", "nickname")
        let zone = J.s(network, "timezone", "value").nullIfEmpty ?? J.s(network, "timezone")
        group(nil,
            settingRow("Nickname", "A label for this network in eero apps. It doesn't change your wifi name.", nickname.nullIfEmpty ?? "Not set",
                rowAction("Change", { [self] in
                    edit("Network nickname", [Field("label", "Nickname", nickname)], { [self] vals in
                        try MainWindow.validateName(v(vals, "label"))
                        try await change("PUT", try featureUrl("label?labelType=specialMarket"), J.obj("label", v(vals, "label").trim()), true, "Network nickname updated.")
                    })
                })),
            settingRow("Timezone", "Profile pauses, power-saving schedules and update times follow this timezone.", zone.nullIfEmpty ?? "Not set",
                rowAction("Change", { [self] in
                    edit("Network timezone", [Field("timezone", "Timezone", zone).help("Use the region/city form, for example America/New_York, America/Chicago, America/Denver, America/Los_Angeles or Europe/London.")], { [self] vals in
                        let value = v(vals, "timezone").trim()
                        if !Regex.isMatch(value, "^[A-Za-z_+-]+(?:/[A-Za-z0-9_+-]+){1,2}$|^UTC$") { throw AppError.argument("Enter a timezone such as America/Chicago.") }
                        if !(await confirm("Change the network timezone? Scheduled pauses will follow the new timezone.")) { return }
                        try await change("PUT", try netResource("settings"), J.obj("timezone", J.obj("value", value)), false, "Timezone updated.")
                    })
                })))
    }

    func wanPage() {
        sub("How your gateway eero gets its internet connection from your internet provider (ISP). Most homes use Automatic.")
        let lease = J.get(network, "lease"); let leaseMode = J.s(lease, "mode")
        let isStatic = leaseMode == "static"
        let assignment = isStatic ? "Static IP" : leaseMode == "dhcp" ? "Automatic (DHCP)" : !leaseMode.isEmpty ? MainWindow.friendly(leaseMode) : "Not reported by eero"
        let routerMode = J.s(network, "connection", "mode")
        group("Internet address",
            settingRow("Address assignment", "Automatic works for most ISPs. Choose Static IP only if your ISP gave you a fixed address.", assignment,
                rowAction("Change", { [self] in editWanAddress() })),
            isStatic ? infoRow("Static IP address", J.s(lease, "static", "ip")) : nil,
            isStatic ? infoRow("Subnet mask", J.s(lease, "static", "mask")) : nil,
            isStatic ? infoRow("Gateway", J.s(lease, "static", "router")) : nil,
            !routerMode.isEmpty ? infoRow("Router mode", MainWindow.friendly(routerMode), "Change this on the DHCP & NAT page.") : nil)
        addGroupHeading("Related settings")
        let related = FeatureCatalog.entries.filter { $0.path == "Settings/Advanced networking/Internet connection/ISP settings" || $0.path == "Settings/Advanced networking/DHCP & NAT" }.map { listRow($0, false) }
        page.append(rowGroup(related))
        technicalDetails(J.obj("connection", J.get(network, "connection"), "ip_settings", J.get(network, "ip_settings"), "lease", lease, "ipv6_lease", J.get(network, "ipv6_lease")))
    }

    func dnsPage() {
        sub("DNS turns website names into addresses. Most networks should use the automatic servers from their internet provider.")
        let dns = J.get(network, "dns")
        let custom = J.s(dns, "mode") == "custom", caching = J.b(dns, "caching")
        let ips = J.list(J.get(dns, "custom", "ips")).map { J.s($0) }.filter { !$0.isEmpty }
        let servers = ips.joined(separator: "\n")
        let automatic = "Automatic (from your ISP)"
        group(nil,
            settingRow("DNS servers", custom ? "Your network uses the servers you entered." : "Your internet provider chooses the servers.", custom ? "Custom · " + ips.joined(separator: ", ") : automatic,
                rowAction("Change", { [self] in
                    edit("DNS servers", [
                        Field("mode", "DNS servers", custom ? "Custom" : automatic, "choice", automatic, "Custom").help("Custom lets you use a provider such as 1.1.1.1 or 8.8.8.8. Advanced Security can prevent custom DNS."),
                        Field("servers", "Custom DNS server addresses (one per line, up to four)", servers, "multiline").when("mode", "Custom")], { [self] vals in
                        let body = try FeatureContracts.dns(v(vals, "mode") == "Custom", v(vals, "servers"), caching)
                        if !(await confirm("Apply DNS settings? eero may restart the network.")) { return }
                        try await change("PUT", try netResource("settings"), body, false, "DNS servers updated.")
                    })
                })),
            settingToggle("Local DNS caching", "Your eero remembers recent lookups so websites start loading faster.", caching, { [self] on in
                if !(await confirm((on ? "Turn on" : "Turn off") + " local DNS caching? eero may restart the network.")) { return }
                try await change("PUT", try netResource("settings"), try FeatureContracts.dns(custom, servers, on), false, "Local DNS caching " + (on ? "turned on." : "turned off."))
            }))
        relatedPages()
    }

    func dhcpModeLabel() -> String {
        J.s(network, "connection", "mode") == "bridged" ? "Bridge" : ApkContracts.isPassthrough(network) ? "IP Passthrough" : J.s(network, "dhcp", "mode") == "custom" ? "Manual" : "Automatic"
    }
    static func dhcpModeHelp(_ mode: String) -> String {
        switch mode {
        case "Manual": return "eero hands out addresses from a range you choose."
        case "Bridge": return "Another router handles addresses; eero only provides wifi. Profiles and some security features stop working."
        case "IP Passthrough": return "Your public IP passes to one downstream device. Routing, profiles and some eero Plus features stop working."
        default: return "eero hands out addresses to your devices automatically. Recommended."
        }
    }

    func dhcpPage() {
        sub("How eero gives addresses to the devices on your network.")
        let mode = dhcpModeLabel(); let range = J.get(network, "dhcp", "custom")
        group(nil,
            settingRow("Network mode", MainWindow.dhcpModeHelp(mode), mode, rowAction("Change", { [self] in editDhcp() })),
            mode == "Manual" ? infoRow("Address range", J.s(range, "start_ip") + " – " + J.s(range, "end_ip")) : nil,
            mode == "Manual" ? infoRow("Subnet", J.s(range, "subnet_ip") + " / " + J.s(range, "subnet_mask")) : nil,
            J.get(network, "nat_port_randomization") is Bool && (mode == "Automatic" || mode == "Manual") ? settingToggle("NAT port randomization", "Uses random outbound ports for extra privacy. Turn off if a game, call or VPN has trouble connecting.", J.b(network, "nat_port_randomization"), { [self] on in
                if !(await confirm((on ? "Turn on" : "Turn off") + " NAT port randomization? The network may restart.")) { return }
                try await change("PUT", try netResource("settings"), J.obj("nat_port_randomization", on), false, "NAT port randomization " + (on ? "turned on." : "turned off."))
            }) : nil)
        technicalDetails(J.obj("connection", J.get(network, "connection"), "dhcp", J.get(network, "dhcp")))
    }

    func editDhcp() {
        let mode = dhcpModeLabel()
        var modes = ["Automatic", "Manual", "Bridge"]
        if ApkContracts.passthroughCapable(network) || ApkContracts.isPassthrough(network) { modes.append("IP Passthrough") }
        edit("Network mode", [
            Field("mode", "Mode", mode, "choice", choices: modes).help("Automatic: recommended. Manual: choose the address range. Bridge: another router hands out addresses. IP Passthrough: give your public IP to one device."),
            Field("subnet", "Subnet network address", J.s(network, "dhcp", "custom", "subnet_ip")).when("mode", "Manual"),
            Field("mask", "Subnet mask", J.s(network, "dhcp", "custom", "subnet_mask")).when("mode", "Manual"),
            Field("first", "First address to hand out", J.s(network, "dhcp", "custom", "start_ip")).when("mode", "Manual"),
            Field("last", "Last address to hand out", J.s(network, "dhcp", "custom", "end_ip")).when("mode", "Manual"),
            Field("randomization", "NAT port randomization", J.b(network, "nat_port_randomization"), "bool").when("mode", "Automatic", "Manual")], { [self] vals in
            let chosen = v(vals, "mode")
            let selected = chosen == "Bridge" ? "bridge" : chosen == "IP Passthrough" ? "passthrough" : chosen == "Manual" ? "custom" : "automatic"
            let body = try FeatureContracts.dhcp(selected, v(vals, "subnet"), v(vals, "mask"), v(vals, "first"), v(vals, "last"), flag(vals, "randomization"))
            if !(await confirm(selected == "passthrough" ? "Switch to IP Passthrough? The network will restart, your public IP passes to one downstream device, and routing, profiles and some eero Plus features stop working." : "Apply DHCP & NAT settings? The network may restart. Bridge mode disables routing, profiles and some security features.")) { return }
            try await change("PUT", try netResource("settings"), body, false, "DHCP & NAT settings updated.")
        })
    }

    func networkBooleanPage(_ key: String, _ title: String, _ description: String) {
        sub("Applies to every eero on this network. Changes take effect after eero confirms them.")
        networkSettingToggles([[key, title, description]], "Some devices may reconnect.")
    }

    func wirelessPage() {
        sub("Security and performance options for every eero radio on this network.")
        networkSettingToggles([
            ["wpa3", "WPA3 security", "Stronger wifi encryption. Some older devices only support WPA2."],
            ["band_steering", "Client steering", "Move capable devices to the best band and eero automatically."],
            ["sqm", "Optimize for conferencing and gaming", "Smart queue management reduces lag while the connection is busy."],
            ["ffs", "Amazon Frustration-Free Setup", "Let compatible Amazon devices join this network automatically."]
        ], "Devices may briefly reconnect.")
        let mloValues = ["disabled", "single", "multi"], mloLabels = ["Off", "Single link", "Multi-link"]
        let mlo = J.s(network, "mlo_mode"); let mloIndex = mloValues.firstIndex(of: mlo)
        group("Compatibility",
            settingRow("Legacy compatibility mode", "Helps older wifi devices that can't connect. Can lower speeds for newer devices.", nil,
                rowActionAsync("Change", { [self] in
                    let value = try await read(try featureUrl("ac_compat"))
                    edit("Legacy compatibility mode", [Field("enabled", "Use legacy compatibility mode", J.b(value, "enabled"), "bool").help("Turn on only if an older device can't join your wifi.")], { [self] vals in
                        try await change("PUT", try featureUrl("ac_compat"), J.obj("enabled", flag(vals, "enabled")), true, "Compatibility setting updated.")
                    })
                })),
            settingRow("Security by radio band", "Choose WPA2, WPA3 or both for the 2.4 GHz and 5 GHz bands. 6 GHz always uses WPA3.", nil, rowActionAsync("Change", { [self] in try await bandSecurity() })),
            J.get(network, "mlo_mode") != nil ? settingRow("Multi-link operation (Wi-Fi 7)", "Lets Wi-Fi 7 devices use more than one band at the same time.", mloIndex.map { mloLabels[$0] } ?? MainWindow.friendly(mlo),
                rowAction("Change", { [self] in
                    edit("Multi-link operation", [Field("mlo_mode", "Mode", mloIndex.map { mloLabels[$0] } ?? mloLabels[0], "choice", choices: mloLabels)], { [self] vals in
                        if !(await confirm("Change multi-link operation? Connected devices may reconnect.")) { return }
                        guard let index = mloLabels.firstIndex(of: v(vals, "mlo_mode")) else { throw AppError.argument("Choose a multi-link mode.") }
                        try await change("PUT", try featureUrl("mlo_mode"), J.obj("mlo_mode", mloValues[index]), false, "Multi-link operation updated.")
                    })
                })) : nil,
            passpointRow())
        group("Reports",
            settingRow("Radio settings", "The channel and power each eero radio is using.", nil, rowActionAsync("View", { [self] in info("Radio settings", try await read(try featureUrl("radio_settings"))) })),
            settingRow("Channel utilization", "How busy each wifi channel was over the last day.", nil, rowActionAsync("View", { [self] in info("Channel utilization", try await read(try featureUrl("channel_utilization?") + statsRange(1))) })))
        relatedPages()
    }

    func threadPage(_ data: Any?) {
        sub("Thread is a low-power network used by Matter and other smart-home devices such as sensors, locks and lights.")
        let threadOn = J.b(network, "thread"), syncing = J.b(data, "enable_credential_syncing")
        group(nil,
            settingToggle("Thread network", "Let Thread smart-home devices connect through your eeros.", threadOn, { [self] on in
                if !on, !(await confirm("Turn off Thread? Thread smart-home devices will stop working until you turn it back on.")) { return }
                try await change("PUT", try featureUrl("thread"), J.obj("thread_enable", on, "enable_credential_syncing", syncing), false, on ? "Thread turned on." : "Thread turned off.")
            }),
            settingToggle("Share Thread credentials", "Sync credentials with your phone so smart-home apps can add Thread devices.", syncing, { [self] on in
                try await change("PUT", try featureUrl("thread"), J.obj("thread_enable", threadOn, "enable_credential_syncing", on), false, on ? "Thread credential sharing turned on." : "Thread credential sharing turned off.")
            }))
        group("Credentials",
            settingRow("Thread credentials", "Some devices ask for these when you add them by hand.", nil,
                rowAction("View", { [self] in
                    if preview { showCredentials("Thread credentials", ["Thread dataset": "Sample-only-dataset"]) }
                    else { showCredentials("Thread credentials", try EverydayContracts.threadCredentials(data)) }
                })),
            settingRow("Reset credentials", "Creates new credentials. Every Thread device must be paired again.", nil,
                rowActionAsync("Reset", { [self] in
                    if !(await confirm("Reset this Thread network's credentials? Every Thread device will need to be paired again.")) { return }
                    try await change("POST", try featureUrl("thread"), nil, false, "Thread credential reset requested.")
                }, true)))
        technicalDetails(data)
    }

    func ddnsPage() {
        sub("Gives your network a hostname that keeps pointing at your home even when your ISP changes your public IP. Useful for reaching a camera, server or game host from outside. Requires an eligible eero subscription.")
        let ddns = J.get(network, "dynamic_dns"); let host = J.first(ddns, "hostname", "host", "domain")
        let apply: (Bool) async throws -> Void = { [self] on in
            if !on, !(await confirm("Turn off Dynamic DNS? Anything using your hostname will stop reaching your network.")) { return }
            try await change("PUT", try featureUrl(on ? "ddns/enable" : "ddns/disable"), nil, false, on ? "Dynamic DNS turned on." : "Dynamic DNS turned off.")
        }
        group(nil,
            J.get(ddns, "enabled") is Bool
                ? settingToggle("Dynamic DNS", "Keep a hostname updated with your network's public IP.", J.b(ddns, "enabled"), apply)
                : settingRow("Dynamic DNS", "eero hasn't reported whether this is on.", "Unknown", rowActionAsync("Turn on", { try await apply(true) }), rowActionAsync("Turn off", { try await apply(false) })),
            !host.isEmpty ? settingRow("Hostname", "Use this name instead of your public IP.", host, rowAction("Copy", { [self] in networkingCopy(host); setNotice("Hostname copied.") })) : nil)
    }

    /// C# static `Entries`: the response itself when it is a list, otherwise the first non-empty named list.
    static func entries(_ data: Any?, _ names: String...) -> [Any] { entriesList(data, names) }
    func entries(_ data: Any?, _ names: String...) -> [Any] { MainWindow.entriesList(data, names) }
    static func entriesList(_ data: Any?, _ names: [String]) -> [Any] {
        let direct = J.list(data); if !direct.isEmpty { return direct }
        for name in names { let a = J.list(J.get(data, name)); if !a.isEmpty { return a } }
        return []
    }

    func routingPage(_ data: Any?) {
        sub("Give devices a fixed address, and let apps on the internet reach a device when it needs that (for example game servers or cameras).")
        if ApkContracts.isNotRouting(network) { page.append(card([text("These settings only work when DHCP & NAT is set to Automatic or Manual. Bridge and IP Passthrough hand this job to another router.")])); return }
        if data == nil { page.append(text("Loading network rules…", 14, "#64738A")); return }
        let kinds = ["reservations", "forwards", "pinholes"]
        let headings = ["Reserved addresses", "Port forwarding", "IPv6 firewall rules"]
        let helps = ["Always give a device the same IP address. Needed before you forward a port to it.", "Send traffic from the internet on a port to one device. Only open the ports you need.", "Allow incoming IPv6 traffic to reach a device."]
        let adds = ["Reserve an address", "Open a port", "Add a rule"]
        for k in 0..<kinds.count {
            let kind = kinds[k]; let list = J.list(J.get(data, kind)); var rows: [Row] = []
            for raw in list {
                let item: Any? = raw; var title: String, detail: String
                if kind == "reservations" { title = J.first(item, "description", "mac"); detail = J.s(item, "ip") + " · " + J.s(item, "mac") }
                else if kind == "forwards" { title = J.first(item, "description", "ip"); detail = "Port " + J.s(item, "gateway_port") + " → " + J.s(item, "ip") + ":" + J.s(item, "client_port") + " · " + J.s(item, "protocol").uppercased() + (J.get(item, "enabled") is Bool && !J.b(item, "enabled") ? " · Off" : "") }
                else { let match = devices.first { J.s($0, "url") == J.s(item, "device") }; title = match != nil ? MainWindow.device(match).name : "Device"; detail = J.s(item, "protocol").uppercased() + " port " + J.s(item, "port") }
                rows.append(settingRow(title.nullIfEmpty ?? "Rule", detail, nil,
                    rowAction("Edit", { [self] in try routingEdit(kind, item, data) }),
                    rowActionAsync("Delete", { [self] in
                        if !(await confirm(kind == "reservations" ? "Delete this reservation and its port forwards?" : "Delete this rule?")) { return }
                        guard let url = MainWindow.objectUrl(item), !url.isEmpty else { throw AppError.invalidOperation("eero did not provide an edit URL for this entry.") }
                        try await change("DELETE", url + (kind == "reservations" ? "?delete_forwards=true" : ""), nil, false, "Rule deleted.")
                    }, true)))
            }
            if rows.isEmpty { rows.append(infoRow("None yet", nil, helps[k])) }
            rows.append(settingRow(adds[k], list.isEmpty ? nil : helps[k], nil, rowAction("Add", { [self] in try routingEdit(kind, nil, data) })))
            group(headings[k], rows: rows)
        }
    }

    func routingEdit(_ kind: String, _ item: Any?, _ rules: Any?) throws {
        let edit = item != nil
        var container = J.s(rules, kind, "url")
        if container.isEmpty { container = kind == "pinholes" ? try featureUrl("ipv6/pinholes") : try netResource(kind) }
        let url = edit ? (MainWindow.objectUrl(item) ?? "") : container
        if kind == "reservations" {
            self.edit(edit ? "Edit reserved address" : "Reserve an address", [Field("name", "Nickname", J.s(item, "description")), Field("mac", "Device MAC address", J.s(item, "mac")).help("Find it on the Devices page, for example 02:00:00:00:00:01."), Field("ip", "IP address to reserve", J.s(item, "ip")).help("Pick an address inside your network, for example 192.168.4.50.")], { [self] vals in
                try await change(edit ? "PUT" : "POST", url, try FeatureContracts.reservation(v(vals, "name"), v(vals, "mac"), v(vals, "ip")), false, "Reservation saved.")
            })
        } else if kind == "forwards" {
            self.edit(edit ? "Edit port forward" : "Open a port", [Field("description", "Nickname", J.s(item, "description")), Field("ip", "Device IP address", J.s(item, "ip")).help("Reserve this address first so the device keeps it."), Field("gateway_port", "Internet port or range", J.s(item, "gateway_port")).help("For example 443 or 8000-8010."), Field("client_port", "Device port or range", J.s(item, "client_port")).help("Usually the same as the internet port."), Field("protocol", "Protocol", J.s(item, "protocol").nullIfEmpty ?? "tcp", "choice", "tcp", "udp", "both").help("Check the app's instructions. Choose both if unsure."), Field("enabled", "Forward this port now", !edit || J.b(item, "enabled"), "bool")], { [self] vals in
                let body = try FeatureContracts.forward(v(vals, "ip"), v(vals, "gateway_port"), v(vals, "client_port"), v(vals, "protocol"), v(vals, "description"), flag(vals, "enabled"))
                if !(await confirm("Save this port forward? It allows matching inbound internet traffic to the selected device.")) { return }
                try await change(edit ? "PUT" : "POST", url, body, false, "Port forward saved.")
            })
        } else {
            var choices: [(label: String, url: String)] = []
            for d in devices where !J.s(d, "url").isEmpty {
                let label = MainWindow.device(d).name + " · " + J.s(d, "mac")
                if !choices.contains(where: { $0.label == label }) { choices.append((label, J.s(d, "url"))) }
            }
            if choices.isEmpty { throw AppError.invalidOperation("No device URLs were returned for an IPv6 rule.") }
            let chosen = choices.first { $0.url == J.s(item, "device") }?.label ?? choices[0].label
            self.edit(edit ? "Edit IPv6 firewall rule" : "IPv6 firewall rule", [Field("device", "Device", chosen, "choice", choices: choices.map { $0.label }), Field("protocol", "Protocol", J.s(item, "protocol").nullIfEmpty ?? "tcp", "choice", "tcp", "udp", "both"), Field("port", "Port or range", J.s(item, "port"))], { [self] vals in
                let device = choices.first { $0.label == v(vals, "device") }?.url ?? ""
                let body = J.obj("device", device, "protocol", v(vals, "protocol"), "port", try FeatureContracts.portRange(v(vals, "port")))
                if !(await confirm("Allow this inbound IPv6 traffic to the selected device?")) { return }
                try await change(edit ? "PUT" : "POST", url, body, false, "IPv6 firewall rule saved.")
            })
        }
    }

    func powerPage(_ data: Any?) {
        sub("Lower your eeros' power use. Wifi stays on, but peak speed can drop slightly while power saving is active.")
        let saving = J.b(network, "power_saving"), scheduled = J.b(network, "power_saving_schedule_enabled")
        group(nil,
            settingToggle("Power saving", "Save power whenever traffic is light.", saving, { [self] on in
                try await change("PUT", try featureUrl("power_saving"), J.obj("enable", on, "power_saving_schedule_enabled", scheduled), false, on ? "Power saving turned on." : "Power saving turned off.")
            }),
            settingToggle("Use schedules", "Only save power during the times below, such as overnight.", scheduled, { [self] on in
                try await change("PUT", try featureUrl("power_saving"), J.obj("enable", saving, "power_saving_schedule_enabled", on), false, on ? "Power-saving schedules turned on." : "Power-saving schedules turned off.")
            }))
        var rows: [Row] = []
        for raw in entries(data, "schedules") {
            let item: Any? = raw
            let url = MainWindow.objectUrl(item, (try? featureUrl("power_saving/schedules/" + J.first(item, "id", "schedule_id"))) ?? "") ?? ""
            let days = J.list(J.get(item, "days")).map { MainWindow.human(J.s($0).lowercased()) }.joined(separator: ", ")
            let when = J.s(item, "start_time") + "–" + J.s(item, "end_time") + (!days.isEmpty ? " · " + days : "")
            rows.append(settingRow(J.s(item, "name").nullIfEmpty ?? "Schedule", J.b(item, "enabled") ? when : when + " · Off", nil,
                rowAction("Edit", { [self] in scheduleEdit(item, url, true) }),
                rowActionAsync("Delete", { [self] in
                    if !(await confirm("Delete this power saving schedule?")) { return }
                    try await change("DELETE", url, nil, false, "Schedule deleted.")
                }, true)))
        }
        if rows.isEmpty { rows.append(infoRow("No schedules yet", nil, "Add one to save power at set times, for example 11:00 PM to 6:00 AM.")) }
        rows.append(settingRow("Add a schedule", nil, nil, rowAction("Add", { [self] in scheduleEdit(nil, try featureUrl("power_saving/schedules"), true) })))
        group("Schedules", rows: rows)
    }

    func scheduleEdit(_ item: Any?, _ url: String, _ power: Bool = false) {
        let week = ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"]
        let days = Set(J.list(J.get(item, "days")).map { J.s($0) })
        var fields: [Field] = [Field("name", "Schedule name", J.s(item, "name")),
                               Field("start", "Start time (24-hour HH:mm)", J.s(item, power ? "start_time" : "start").nullIfEmpty ?? "22:00"),
                               Field("end", "End time (24-hour HH:mm)", J.s(item, power ? "end_time" : "end").nullIfEmpty ?? "07:00"),
                               Field("enabled", "Enabled", item == nil || J.b(item, "enabled"), "bool")]
        for day in week { fields.append(Field(day, MainWindow.human(day.lowercased()), days.contains(day), "bool")) }
        edit(item == nil ? "Add schedule" : "Edit schedule", fields, { [self] vals in
            let selected = week.filter { flag(vals, $0) }
            let body = try FeatureContracts.schedule(v(vals, "name"), selected, v(vals, "start"), v(vals, "end"), flag(vals, "enabled"), power)
            try await change(item == nil ? "POST" : "PUT", url, body, false, "Schedule saved.")
        }, "Save", "Times follow the network timezone. End times earlier than start times continue into the next day.")
    }

    func blockedPage(_ data: Any?) {
        sub("Devices you've blocked can't join your network at all. Unblock one to let it connect again.")
        var rows: [Row] = []
        for raw in entries(data, "devices") {
            let item: Any? = raw
            rows.append(settingRow(MainWindow.device(item).name, J.s(item, "mac") + (!J.s(item, "manufacturer").isEmpty ? " · " + J.s(item, "manufacturer") : ""), nil,
                rowActionAsync("Unblock", { [self] in
                    if !(await confirm("Allow " + MainWindow.device(item).name + " to join your network again?")) { return }
                    try await change("DELETE", try featureUrl("blacklist/" + J.s(item, "mac").escapedData), nil, false, "Device unblocked.")
                })))
        }
        if rows.isEmpty { rows.append(infoRow("No blocked devices", nil, data == nil && !preview ? "eero hasn't returned this list. Choose Refresh at the top right to try again." : "To block one, select it on the Devices page and choose Block.")) }
        group(nil, rows: rows)
    }

    func editWanAddress() {
        let lease = J.get(network, "lease"); let stat = J.get(lease, "static")
        let automatic = "Automatic (DHCP)"
        edit("Internet address", [
            Field("mode", "Address assignment", J.s(lease, "mode") == "static" ? "Static IP" : automatic, "choice", automatic, "Static IP").help("Automatic asks your ISP for an address. Static IP uses the fixed address your ISP gave you."),
            Field("ip", "Static IPv4 address", J.s(stat, "ip")).when("mode", "Static IP"),
            Field("mask", "Subnet mask", J.s(stat, "mask")).when("mode", "Static IP"),
            Field("router", "Gateway (router) address", J.s(stat, "router")).when("mode", "Static IP")], { [self] vals in
            let isStatic = v(vals, "mode") == "Static IP"; let body = J.obj("mode", isStatic ? "static" : "dhcp")
            if isStatic { body["static"] = J.obj("ip", try FeatureContracts.address(v(vals, "ip"), AddressFamily.interNetwork), "mask", try FeatureContracts.address(v(vals, "mask"), AddressFamily.interNetwork), "router", try FeatureContracts.address(v(vals, "router"), AddressFamily.interNetwork)) }
            if !(await confirm("Apply the internet addressing settings supplied by your ISP? Incorrect values can disconnect the network.")) { return }
            try await change("PUT", try netResource("settings"), J.obj("lease", body), false, "Internet addressing updated.")
        })
    }

    fileprivate func networkingCopy(_ s: String) {
        NSPasteboard.general.clearContents(); NSPasteboard.general.setString(s, forType: .string)
    }
}

/// The revealed password in the Show password dialog; cleared when the dialog closes.
@MainActor
final class NetworkingSecretText: ObservableObject {
    @Published var value: String
    init(_ value: String) { self.value = value }
}

struct NetworkingSecretField: View {
    @ObservedObject var secret: NetworkingSecretText
    let label: String
    var body: some View {
        Text(secret.value).font(.system(size: 16, design: .monospaced)).textSelection(.enabled)
            .frame(maxWidth: .infinity, alignment: .leading).padding(8)
            .overlay(RoundedRectangle(cornerRadius: 5).strokeBorder(Color(nsColor: .separatorColor)))
            .accessibilityLabel(label)
    }
}
