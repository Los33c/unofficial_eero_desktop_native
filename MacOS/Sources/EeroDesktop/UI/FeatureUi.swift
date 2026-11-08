import AppKit
import CoreImage
import Foundation
import SwiftUI

extension MainWindow {
    func featureUrl(_ suffix: String) throws -> String {
        guard let url = networkUrl, !url.isEmpty else { throw AppError.invalidOperation("Select a network first.") }
        return url.trimEnd("/") + "/" + suffix.trimStart("/")
    }
    func netResource(_ name: String) throws -> String { resource(network, name, try featureUrl(name)) }
    static func objectUrl(_ data: Any?, _ fallback: String? = nil) -> String? { J.s(data, "url").nullIfEmpty ?? fallback }
    func navigate(_ route: String) { navigateTo(route, true) }
    func navigateTo(_ route: String, _ record: Bool) {
        if busy { return }
        if record { recordHistory(route) }
        currentPage = route; renderPage()
        if route.contains("/") {
            Task { @MainActor in
                await self.run({ try await self.loadFeature(route); if self.currentPage == route { self.renderPage() } }, "Loading " + (route.split("/").last ?? route).lowercased() + "…")
            }
        }
    }
    func featureMenu(_ parent: String, _ explanation: Bool = true) { nativeFeatureMenu(parent, explanation) }

    func loadFeature(_ route: String) async throws {
        if preview { featureData[route] = previewFeature(route) ?? JNull.shared; return }
        if !api.signedIn { return }
        var data: Any? = nil
        do {
            switch route {
            case "Help/Contact support": data = try await api.send("GET", "/2.2/account/eero_support")
            case "Settings/Account settings/Subscriptions/Billing & status": data = try await api.send("GET", "/2.2/premium/customer"); featureData["billing_customer"] = data ?? JNull.shared
            case "Settings/Troubleshooting/Guided diagnostics": data = try await api.send("GET", try netResource("diagnostics"))
            case "Settings/Advanced networking/Wireless settings/Fast roaming": data = try await api.send("GET", try featureUrl("fast_transition"))
            case "Settings/Advanced networking/Internet backup/Find backup wifi": data = try await api.send("GET", try featureUrl("backup_access_points/ssid_discovery"))
            case "Settings/Account settings/Subscriptions/Offers & referrals": if !(networkUrl ?? "").isEmpty { data = try await api.send("GET", "/2.4/premium/plans/" + networkId) }
            case "Settings/User permissions/Incoming access invitations": data = try await api.send("GET", "/2.2/incoming_member_promotions")
            case "Home/Add or replace eero/Automatic setup": data = try await api.send("GET", try featureUrl("auto_discovered_eeros"))
            case "Settings/Connected services/Accessories & cellular": data = try await api.send("GET", try featureUrl("accessories/setup/status"))
            case "Home/Profiles/Profile": if selectedProfile != nil { data = try await api.send("GET", J.s(selectedProfile, "url")); selectedProfile = data }
            case "Home/eeros/Details": if selectedNode != nil { data = try await api.send("GET", J.s(selectedNode, "url")); selectedNode = data }
            case "Settings/Network management/Pending transfers": data = try await api.send("GET", "/2.2/network_transfers/pending")
            case "Settings/Network management/Outgoing transfer": data = try await api.send("GET", try netResource("transfer"))
            case "Settings/Connected services/Connected device setup": data = try await api.send("GET", try featureUrl("simple_setup/vendors"))
            case "Home/Security & privacy/Identity protection": data = try await api.send("GET", "/2.2/account/identity_protection")
            case "Settings/Advanced networking/DNS/DNS performance": if let action = featureData["dns_perf_action"] { data = try await api.send("GET", try featureUrl("dns_perf_test/" + (try ExtendedContracts.identifier(J.str(action))))) }
            case "Devices/Blocked devices": data = try await api.send("GET", try featureUrl("blacklist"))
            case "Home/Internet": data = try await api.send("GET", try netResource("speedtest"))
            case "Home/Security & privacy": data = try await api.send("GET", try featureUrl("dns_policies/network"))
            case "Settings/Guest wifi network": data = try await api.send("GET", try netResource("guestnetwork"))
            case "Settings/Advanced networking/Reservations & port forwarding": data = try await api.send("GET", try netResource("routing"))
            case "Settings/Advanced networking/Thread": data = try await api.send("GET", try netResource("thread"))
            case "Settings/User permissions": data = J.obj("members", try await api.send("GET", try featureUrl("members")), "invites", try await api.send("GET", try featureUrl("invites")))
            case "Settings/Software updates": data = try await api.send("GET", try netResource("updates"))
            case "Settings/Notifications": data = try await api.send("GET", try featureUrl("notifications"))
            case "Settings/Account settings": account = try await api.send("GET", "account"); data = account
            case "Settings/Advanced networking/Power saving": data = try await api.send("GET", try featureUrl("power_saving/schedules"))
            case "Settings/Advanced networking/Internet backup": data = J.obj("status", try await api.send("GET", try featureUrl("backupinternet")), "access_points", try await api.send("GET", try featureUrl("backup_access_points")))
            case "Settings/Advanced networking/Multi-WAN": data = try await api.send("GET", try featureUrl("wan/links"))
            case "Activity/Data usage": data = try await api.send("GET", try featureUrl("data_usage?") + statsRange(usageDays) + "&cadence=daily")
            case "Activity/Security activity": data = try await api.send("GET", try featureUrl("insights?") + statsRange() + "&cadence=daily")
            case "Activity/Network events": data = try await api.send("GET", try featureUrl("app_events?page_size=50"))
            case "Settings/Connected services": data = try await api.send("GET", try featureUrl("proxied_nodes"))
            case "Settings/Additional wifi networks", "Home/Security & privacy/Content filters": data = try await api.send("GET", try featureUrl("subnets_config"))
            case "Settings/Business guest wifi": data = try await api.send("GET", try featureUrl("captive_portal/configurations"))
            case "Settings/Advanced networking/Multiple static IPs": data = try await api.send("GET", "/2.3/networks/" + ((networkUrl ?? "").trimEnd("/").split("/").last ?? "") + "/multistaticip")
            default: break
            }
            if data == nil { data = try await loadApkFeature(route) }
            featureData[route] = data ?? JNull.shared
            FeatureAvailability.clear(availabilityKey, route)
        } catch let e as ApiException {
            if e.sessionExpired { throw e }
            featureData[route] = e
            FeatureAvailability.mark(availabilityKey, route, FeatureAvailability.reasonFor(e.code))
        }
    }

    func renderFeature() {
        var data = featureData[currentPage]
        if data is JNull { data = nil }
        if let error = data as? ApiException {
            // A page eero refused or couldn't load: say so plainly and offer a retry.
            let retry = accent(rowActionAsync("Try again", { [unowned self] in try await self.loadFeature(self.currentPage); self.renderPage() }))
            let denied = error.code == 401 || error.code == 403, missing = error.code == 404
            group(nil, settingRow(denied ? "Your account can't open this" : missing ? "Not available for this network" : "This page couldn't load",
                (denied ? "Only the network owner or an administrator can use this, or it needs a plan you don't have." : missing ? "Your eero hardware, software or plan may not support this feature." : "Check your internet connection, then try again.") + " eero said: " + error.message
                + (FeatureAvailability.reasonFor(error.code) != nil ? " Menus will now mark this option so you know before opening it." : ""), nil, retry))
            featureMenu(currentPage, false)
            return
        }
        if renderApkFeature(data) { return }
        switch currentPage {
        case "Help/Contact support": supportContactsPage(data)
        case "Settings/Account settings/Subscriptions/Billing & status": billingOverview()
        case "Settings/Account settings/Subscriptions/Business billing": organizationBilling()
        case "Home/Chat": chatPage(false)
        case "Help/Support history": chatPage(true)
        case "Settings/Troubleshooting/Guided diagnostics": guidedDiagnosticsPage(data)
        case "Settings/Troubleshooting/Connect a device": deviceConnectionPage()
        case "Settings/Advanced networking/Wireless settings/Fast roaming": fastRoamingPage(data)
        case "Settings/Advanced networking/Internet backup/Find backup wifi": backupDiscoveryPage(data)
        case "Settings/Account settings/Subscriptions/Offers & referrals": subscriptionOffersPage(data)
        case "Settings/Connected services/Amazon connected home": amazonHomePage()
        case "Settings/Notifications/Desktop notifications": desktopNotificationsPage()
        case "Settings/User permissions/Incoming access invitations": incomingAccessPage(data)
        case "Home/Add or replace eero/Find nearby eeros": bluetoothSetupPage()
        case "Home/Add or replace eero/Automatic setup": automaticSetupPage(data)
        case "Home/Add or replace eero/Create a network": createNetworkPage()
        case "Home/eeros/Placement tests": placementPage()
        case "Home/Security & privacy/VPN": vpnPage()
        case "Settings/Connected services/Accessories & cellular": accessoriesPage(data)
        case "Settings/Business guest wifi/eero Insight": businessInsightsPage()
        case "Settings/Account settings/Subscriptions/Manage subscriptions & purchases": subscriptionWebPage()
        case "Home/Internet": internetPage(data)
        case "Home/eeros": nodePage()
        case "Home/Profiles": profilesPageV2()
        case "Home/Security & privacy": securityPage(data)
        case "Home/Add or replace eero": addEeroPage()
        case "Devices/Blocked devices": blockedPage(data)
        case "Activity/Data usage": dataUsagePage(data)
        case "Activity/Security activity": insightsPage(data)
        case "Activity/Network events": networkEventsPage(data)
        case "Settings/Wifi name & password": wifiPage()
        case "Settings/Guest wifi network": guestPage(data)
        case "Settings/Additional wifi networks": additionalWifiPage(data)
        case "Settings/Business guest wifi": businessPortalPage(data)
        case "Home/Security & privacy/Content filters": subnetFiltersPage(data)
        case "Settings/Nickname & location": nicknamePage()
        case "Settings/Advanced networking": featureMenu(currentPage)
        case "Settings/Advanced networking/Internet connection": wanPage()
        case "Settings/Advanced networking/DHCP & NAT": dhcpPage()
        case "Settings/Advanced networking/DNS": dnsPage()
        case "Settings/Advanced networking/Reservations & port forwarding": routingPage(data)
        case "Settings/Advanced networking/IPv6": networkBooleanPage("ipv6_upstream", "IPv6", "IPv6 connectivity from your internet provider.")
        case "Settings/Advanced networking/UPnP": networkBooleanPage("upnp", "UPnP", "Allow devices to request port mappings automatically.")
        case "Settings/Advanced networking/Wireless settings": wirelessPage()
        case "Settings/Advanced networking/Thread": threadPage(data)
        case "Settings/Advanced networking/Dynamic DNS": ddnsPage()
        case "Settings/Advanced networking/Power saving": powerPage(data)
        case "Settings/Advanced networking/Internet backup": backupPage(data)
        case "Settings/Advanced networking/Multi-WAN": multiWanPage(data)
        case "Settings/Advanced networking/Multiple static IPs": multiStaticPage(data)
        case "Settings/User permissions": permissionsPage(data)
        case "Settings/Software updates": updatesPage(data)
        case "Settings/Appearance": appearancePage()
        case "Settings/Notifications": notificationsPage(data)
        case "Settings/Troubleshooting": troubleshootingPage()
        case "Settings/Account settings": accountPage()
        case "Settings/Account settings/Subscriptions": subscriptionPage()
        case "Settings/Connected services": connectedPage(data)
        case "Settings/Network management": networkManagementPage()
        case "Settings/About": aboutPage()
        case "Help/Service availability": platformPage()
        case "Home/Profiles/Profile": profileDetailPage(selectedProfile)
        case "Home/eeros/Details": nodeDetailsPage(selectedNode)
        case "Settings/User permissions/Join a network": joinNetworkPage()
        case "Settings/Network management/Pending transfers": pendingTransfersPage(data)
        case "Settings/Network management/Outgoing transfer": outgoingTransferPage(data)
        case "Settings/Account settings/Subscriptions/Redeem a license key": licensePage()
        case "Settings/Account settings/Subscriptions/Partner benefits": partnerBenefitsPage()
        case "Home/Security & privacy/Identity protection": identityProtectionPage(data)
        case "Settings/Connected services/Connected device setup": setupVendorsPage(data)
        case "Settings/Advanced networking/Internet connection/ISP settings": ispSettingsPage()
        case "Theme preview": themePreviewPage()
        case "Product gallery": productGalleryPage()
        case "Settings/Advanced networking/DNS/DNS performance": dnsPerformancePage(data)
        default: featureMenu(currentPage)
        }
    }

    static func human(_ input: String) -> String {
        if let label = fieldLabels[input] { return label }
        var key = input
        if key.hasPrefix("is_") && key.count > 3 { key = String(key.dropFirst(3)) }
        if key.hasSuffix("_bytes") && key.count > 6 { key = String(key.dropLast(6)) }
        // Sentence case with known acronyms kept, e.g. "wan_ip_address" -> "WAN IP address".
        let spaced = Regex.replace(key.replacingOccurrences(of: "_", with: " ").replacingOccurrences(of: ".", with: " ").replacingOccurrences(of: "-", with: " "), "([a-z])([A-Z])", "$1 $2").trim()
        var words = spaced.splitRemovingEmpty(" ").map { acronymWord($0.lowercased()) ?? $0.lowercased() }
        if words.isEmpty { return key }
        words[0] = words[0].prefix(1).uppercased() + words[0].dropFirst()
        return words.joined(separator: " ")
    }
    /// Plain-language names for fields eero returns; anything not listed is turned into sentence case.
    static let fieldLabels: [String: String] = [
        "ip": "IP address", "mac": "MAC address", "ssid": "Wifi name", "up_mbps": "Upload", "down_mbps": "Download", "ipv6_upstream": "IPv6", "led_on": "Status light", "sqm": "Optimize for conferencing and gaming",
        "dsn": "Serial number", "ips": "Servers", "created_at": "Created", "updated_at": "Updated", "sub_header": "Subheading", "access_expiration_in_minutes": "Session length (minutes)", "can_update_now": "Update ready to install", "update_status": "Status", "target_firmware": "Latest software", "mode": "Mode", "rssi": "Signal", "signal": "Signal", "fast_transition": "Fast roaming", "pppoe_enabled": "PPPoE login", "pppoe_username": "PPPoE username", "vlan": "VLAN ID", "ipv6_lease": "IPv6 lease", "ip_settings": "IP settings",
        "interfaces": "Ports", "interface_number": "Port number", "negotiated_speed": "Current speed", "supported_speed": "Fastest supported speed", "port_status": "Status", "ethernet_address": "MAC address", "capable_protocols": "Supports",
        "os_version": "Software version", "firmware_version": "Software version", "connected_clients_count": "Connected devices", "ip_address": "IP address", "mac_address": "MAC address", "last_seen": "Last seen", "last_active": "Last active",
        "started_at": "Started", "completed_at": "Finished", "finished_at": "Finished", "start": "Start", "end": "End", "created": "Created", "timestamp": "Time", "frequency": "Band", "channel_width": "Channel width", "tx_power": "Transmit power",
        "nickname": "Nickname", "hostname": "Device name", "display_name": "Name", "manufacturer": "Maker", "connection_type": "Connection", "wireless": "Wifi", "gateway": "Gateway eero", "heartbeat_ok": "Checking in with eero",
        "download": "Downloaded", "upload": "Uploaded", "blocked": "Blocked", "paused": "Paused", "serial": "Serial number", "model": "Model", "location": "Location", "status": "Status"]

    func objectCards(_ value: Any?, _ title: String? = nil) {
        if value == nil || value is JNull { page.append(card([text("eero hasn't returned this information for your network. Choose Refresh at the top right to try again; if it stays empty, this feature may not apply to your network or plan.", 14, "#64738A")], 18)); return }
        var p: [Block] = []; if let t = title { p.append(text(t, 20, "#17263C", true)) }
        p.append(contentsOf: details(value, 0)); page.append(card(p))
    }
    /// Fields that are internal plumbing or secrets; they are never shown to people.
    static let hiddenDetailKey = "(^resources$|^url$|^links$|^_links$|^actions$|^position$|^metadata$|^node_actions$|token|password|secret|credential|certificate|master_key|private_key|psk|operational_dataset|software_key|invite_code|signed_upload_url|kms_key_id|mwb_full_report_url|^coupon$)"
    static func simple(_ x: Any?) -> Bool { x == nil || x is JNull || x is String || x is Bool || x is Int || x is Decimal || x is Double || x is Int64 }
    static func itemTitle(_ item: Any?) -> (String?, String?) {
        if simple(item) { return (nil, nil) }
        for key in ["nickname", "display_name", "name", "title", "location", "ssid", "model", "version", "os_version", "date", "created", "timestamp", "type"] {
            let value = friendlyValue(key, J.get(item, key))
            if !value.isEmpty && !value.hasPrefix("/2.") { return (key == "name" && Regex.isMatch(value, "^[0-9]+$") ? "Port " + value : value, key) }
        }
        return (nil, nil)
    }
    /// Renders any eero response as labelled rows: friendly field names and values, lists as titled cards.
    func details(_ value: Any?, _ depth: Int, _ skipKey: String? = nil) -> [Block] {
        var p: [Block] = []
        if depth > 5 { return p }
        if let map = value as? JObject {
            for (key, item) in map {
                if Regex.isMatch(key, MainWindow.hiddenDetailKey, .caseInsensitive) || MainWindow.internalKey(key) || key == skipKey { continue }
                if item is JObject || item is [Any] {
                    let list = J.list(item)
                    if !(item is JObject) && list.allSatisfy(MainWindow.simple) {
                        let joined = list.prefix(40).map { MainWindow.friendlyValue(key, $0) }.filter { !$0.isEmpty && !$0.hasPrefix("/2.") }.joined(separator: ", ")
                        if !joined.isEmpty { p.append(detailRow(MainWindow.human(key), joined, false)) }
                        continue
                    }
                    let child = details(item, depth + 1)
                    if child.isEmpty { continue }
                    p.append(.padded([text(MainWindow.human(key), 14, "#17263C", true)], EdgeInsets(top: 14, leading: 0, bottom: 0, trailing: 0)))
                    p.append(.indent(child, 12))
                } else {
                    let display = MainWindow.friendlyValue(key, item)
                    if !display.isEmpty && display != "Unknown" && !display.hasPrefix("/2.") && !display.hasPrefix("http") { p.append(detailRow(MainWindow.human(key), display, item is Bool && !MainWindow.yesNoKey(key))) }
                }
            }
            return p
        }
        let items = J.list(value)
        if !items.isEmpty && items.allSatisfy(MainWindow.simple) { p.append(text(items.prefix(60).map { MainWindow.friendlyValue("", $0) }.filter { !$0.isEmpty && !$0.hasPrefix("/2.") }.joined(separator: ", "), 13)) }
        else if !items.isEmpty {
            for (index, item) in items.prefix(60).enumerated() {
                let (title, titleKey) = MainWindow.itemTitle(item)
                var c: [Block] = [text(title ?? "Item \(index + 1)", 13, "#17263C", true)]
                c.append(contentsOf: details(item, depth + 1, titleKey))
                p.append(card(c, 12))
            }
            if items.count > 60 { p.append(text("Showing the first 60 of \(items.count).", 12, "#64738A")) }
        }
        else if let v = value, !(v is JNull), !(v is [Any]), !(v is String) { p.append(text(MainWindow.friendlyValue("", v), 13)) }
        else if let s = value as? String { p.append(text(MainWindow.friendly(s), 13)) }
        else { p.append(text("Nothing to show yet.", 13, "#64738A")) }
        return p
    }
    func read(_ path: String) async throws -> Any? {
        if preview { throw AppError.invalidOperation("Sign in to load this information.") }
        return try await api.send("GET", path)
    }
    func change(_ method: String, _ path: String, _ body: Any?, _ form: Bool, _ message: String) async throws {
        if preview { throw AppError.invalidOperation("Preview mode is read only. Sign in to manage your network.") }
        try await api.send(method, path, body, form)
        do {
            if !(networkUrl ?? "").isEmpty { try await loadNetwork() }
            if currentPage == "Home/Profiles/Profile" && selectedProfile != nil { selectedProfile = try await api.send("GET", J.s(selectedProfile, "url")) }
            else if currentPage == "Home/eeros/Details" && selectedNode != nil { selectedNode = try await api.send("GET", J.s(selectedNode, "url")) }
            else { try await loadFeature(currentPage) }
            renderPage(); setNotice(message)
        } catch { setNotice(message + " Refresh did not finish: " + MainWindow.message(error), true) }
    }
    static func v(_ values: [String: Any], _ key: String) -> String { J.str(values[key]) }
    static func flag(_ values: [String: Any], _ key: String) -> Bool { v(values, key).caseInsensitiveCompare("True") == .orderedSame }
    func v(_ values: [String: Any], _ key: String) -> String { MainWindow.v(values, key) }
    func flag(_ values: [String: Any], _ key: String) -> Bool { MainWindow.flag(values, key) }

    /// A form dialog: each Field becomes a native control; save runs before the dialog closes.
    func edit(_ title: String, _ fields: [Field], _ save: @escaping ([String: Any]) async throws -> Void, _ submit: String = "Save", _ description: String? = nil) {
        Dialogs.form(self, EditForm(title: title, description: description, fields: fields, submit: submit, save: save))
    }
    func info(_ title: String, _ data: Any?) {
        var body = details(data, 0)
        if body.isEmpty { body.append(text("eero didn't return any details for this.", 14, "#64738A")) }
        dialog(title, [text(title, 23, "#17263C", true), card(body, 16)]).showDialog()
    }
    /// Navigation links look like links, so they are not mistaken for actions.
    func section(_ title: String, _ route: String) -> ButtonSpec {
        let b = localButton(title + "  ›", { [unowned self] in self.navigate(route) }); b.kind = .quiet; b.help = "Open " + title; return b
    }
    func actionRow(_ buttons: ButtonSpec?...) { page.append(.buttons(buttons.compactMap { $0 })) }
    func official(_ url: String) throws {
        if url == "about:blank" || !ServiceLinks.allowed(url) { throw AppError.invalidOperation("The service returned a link outside the supported official domains.") }
        openExternal(url)
    }
    func openExternal(_ url: String) { if let u = URL(string: url) { NSWorkspace.shared.open(u) } }
    func showWifiQr(_ name: String, _ password: String?) {
        guard let image = QrCode.image(FeatureContracts.wifiQr(name, password), scale: 8) else { setNotice("The QR code could not be created.", true); return }
        dialog("Wifi QR code", [text("Join " + name, 23, "#17263C", true), .padded([.image(image, width: 320, height: 320, tooltip: nil)], EdgeInsets(top: 12, leading: 0, bottom: 12, trailing: 0)),
                                text("This QR code contains your wifi credentials. Anyone who scans it can join the network.", 12, "#64738A")]).showDialog()
    }

    func previewFeature(_ path: String) -> Any? {
        if path == "Help/Contact support" { return J.obj("contacts", [J.obj("type", "CHAT", "title", "Sample support chat", "content", "https://www.eero.com/support"), J.obj("type", "MAIL", "title", "Sample support email", "content", "support@example.com")]) }
        if path.hasSuffix("Offers & referrals") { return J.obj("plans", [J.obj("id", "sample-product", "name", "Sample eero Plus plan", "currency", "USD", "price", 999)]) }
        if path.hasSuffix("Guided diagnostics") { return J.obj("status", "completed", "issue_area", "none", "issues", [Any]()) }
        if path.hasSuffix("Fast roaming") { return J.obj("fast_transition", true) }
        if path.hasSuffix("Find backup wifi") { return J.obj("status", "SUCCESS", "discovered_ssids", [J.obj("ssid", "Sample phone hotspot", "rssi", -53)]) }
        if path.hasSuffix("Incoming access invitations") { return J.obj("promotions", [J.obj("invite_id", "sample-invite", "network_id", "sample", "network_name", "Sample family network", "invite_role", "admin")]) }
        if path.hasSuffix("Automatic setup") { return J.obj("eeros", [J.obj("serial", "SAMPLE-SERIAL", "model", "Sample eero", "ineligible_for_zts", J.obj("reason", "Preview example"))]) }
        if path.hasSuffix("Accessories & cellular") { return [J.obj("model", "Sample accessory", "status", "connected")] }
        if path.hasSuffix("Guest wifi network") { return J.obj("name", "Maple Guest", "enabled", true) }
        if path.hasSuffix("Reservations & port forwarding") { return J.obj("reservations", J.obj("data", [J.obj("description", "Office PC", "ip", "192.168.4.20", "mac", "02:00:00:00:00:01")]), "forwards", J.obj("data", [Any]()), "pinholes", J.obj("data", [Any]())) }
        if path.hasSuffix("Security & privacy") { return J.obj("dns_policies", J.obj("block_malware", true, "ad_block", true)) }
        if path.hasSuffix("Software updates") { return J.obj("target_firmware", "Preview", "update_status", "up_to_date", "can_update_now", false) }
        if path.hasSuffix("Notifications") { return J.obj("device.new", true, "network.updated", true) }
        if path.hasSuffix("Pending transfers") { return J.obj("transfers", [J.obj("id", 17, "organization_name", "Sample provider", "subset_name", "Maple community", "created_at", "2026-10-22")]) }
        if path.hasSuffix("Outgoing transfer") { return J.obj("recipient_email_address", "recipient@example.com", "transfer_date", "2026-10-22") }
        if path.hasSuffix("Trusted certificates") { return J.obj("data", [J.obj("serial", "SAMPLE01", "subject", "CN=Sample device", "expires", "2027-10-22")]) }
        if path.hasSuffix("Entitlements & trials") { return J.obj("features", [J.obj("feature", "advanced_security", "enabled", true)], "upsell_features", [J.obj("feature", "sample_upgrade", "tier", "plus")]) }
        if path.hasSuffix("Partner offers") { return J.obj("offers", [J.obj("internal_offer_id", "sample-offer", "name", "Sample partner offer")], "methods", [J.obj("authentication_method", "EMAIL")]) }
        if path.hasSuffix("Identity protection") { return J.obj("enabled", false, "status", "Sample benefit status") }
        if path.hasSuffix("Connected device setup") { return J.obj("vendors", [J.obj("vendor_id", "sample", "name", "Sample connected-device vendor", "enabled", true)]) }
        if path.hasSuffix("Business guest wifi") { return J.obj("configurations", [J.obj("configuration_id", 5, "header", "Welcome to Maple House", "sub_header", "Guest wifi", "color_theme", "Dark", "capture_email", false, "capture_name", false, "access_expiration_in_minutes", 240)]) }
        if path == "Activity/Network events" {
            return J.obj("data", [J.obj("created", "2026-11-07T14:12:00Z", "event_type", "DEVICE_CONNECTED", "message", "Laptop joined the network"),
                                  J.obj("created", "2026-11-07T09:40:00Z", "event_type", "NETWORK_UPDATED", "message", "eero software was updated"),
                                  J.obj("created", "2026-11-06T22:00:00Z", "event_type", "PROFILE_PAUSED", "message", "Kids was paused by a schedule")])
        }
        if path == "Activity/Security activity" {
            func series(_ type: String, _ sum: Int, _ values: [Int]) -> JObject { J.obj("type", type, "sum", sum, "values", values.enumerated().map { J.obj("time", "2026-10-\(16 + $0.offset)", "value", $0.element) }) }
            return J.obj("series", [series("threats", 12, [1, 3, 0, 4, 2, 1, 1]), series("ads", 1840, [210, 260, 240, 330, 270, 290, 240])])
        }
        if path == "Activity/Data usage" {
            let values: [Int] = [1200000000, 2100000000, 1600000000, 2800000000, 1800000000, 2200000000, 1700000000]
            return J.obj("series", [J.obj("type", "download", "sum", 13400000000, "values", values.enumerated().map { J.obj("time", "2026-10-\(16 + $0.offset)", "value", $0.element) })])
        }
        return nil
    }
}

/// One field of an `edit` form (C# `Field`).
final class Field {
    let key: String, label: String, type: String
    var value: Any?
    let choices: [String]
    var whenKey: String?, helpText: String?
    var whenValues: [String] = []
    init(_ key: String, _ label: String, _ value: Any? = nil, _ type: String = "text", _ choices: String...) {
        self.key = key; self.label = label; self.value = value; self.type = type; self.choices = choices
    }
    init(_ key: String, _ label: String, _ value: Any?, _ type: String, choices: [String]) {
        self.key = key; self.label = label; self.value = value; self.type = type; self.choices = choices
    }
    /// Show this field only while another field has one of these values ("True"/"False" for switches).
    @discardableResult func when(_ key: String, _ values: String...) -> Field { whenKey = key; whenValues = values; return self }
    @discardableResult func help(_ text: String) -> Field { helpText = text; return self }
}

/// Wifi QR codes with Core Image (error correction level Q, like the Windows QRCoder call).
enum QrCode {
    static func image(_ text: String, scale: CGFloat) -> NSImage? {
        guard let filter = CIFilter(name: "CIQRCodeGenerator") else { return nil }
        filter.setValue(Data(text.utf8), forKey: "inputMessage")
        filter.setValue("Q", forKey: "inputCorrectionLevel")
        guard let output = filter.outputImage?.transformed(by: CGAffineTransform(scaleX: scale, y: scale)) else { return nil }
        let rep = NSCIImageRep(ciImage: output)
        let image = NSImage(size: rep.size); image.addRepresentation(rep)
        return image
    }
}
