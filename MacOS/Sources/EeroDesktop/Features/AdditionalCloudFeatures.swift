import Foundation
import SwiftUI

extension MainWindow {
    func additionalWifiPage(_ data: Any?) {
        sub("Extra wifi networks with their own name and password, for example to keep smart-home devices or kids' devices separate. How many you can add depends on your eero hardware and plan.")
        let subnets = MainWindow.entries(data, "subnets", "configurations"); var rows: [Row] = []
        for subnet in subnets {
            let type = J.s(subnet, "subnet_type")
            let rate = Int(J.s(subnet, "rate_limit_pct").trim())
            let limited = rate != nil && rate! < 100
            let summary = (MainWindow.friendly(J.s(subnet, "subnet_kind")).nullIfEmpty ?? "Wifi network") + " · " + (J.get(subnet, "enabled") is Bool && !J.b(subnet, "enabled") ? "Off" : "On")
                + (J.b(subnet, "open_network") ? " · No password" : "") + (J.get(subnet, "wan_access") is Bool && !J.b(subnet, "wan_access") ? " · No internet" : "") + (limited ? " · \(rate!)% bandwidth" : "")
            var actions = [rowAction("Edit", { [self] in subnetEdit(subnet) }), rowActionAsync("Content filters", { [self] in try await editSubnetFilters(subnet) })]
            if type == "A" || type == "B" {
                actions.append(rowActionAsync("Delete", { [self] in
                    if !(await confirm("Delete this additional wifi network? Connected devices will disconnect.")) { return }
                    try await change("DELETE", try featureUrl("subnets_config/" + type), nil, false, "Additional wifi network deleted.")
                }, true))
            }
            rows.append(settingRow(J.s(subnet, "name").nullIfEmpty ?? "Additional network", summary, nil, actions: actions))
        }
        if rows.isEmpty { rows.append(infoRow("No additional networks yet", nil, "Add one to keep smart-home, kids' or work devices on their own wifi.")) }
        let used = Set(subnets.map { J.s($0, "subnet_type") })
        let free = ["A", "B"].first { !used.contains($0) }
        let add = rowAction("Add", { [self] in subnetEdit(nil, free) }); add.enabled = free != nil
        rows.append(settingRow("Add a wifi network", free != nil ? "Choose a name, password and what it's for." : "Both additional network slots are in use. Delete one to add another.", nil, add))
        group(nil, rows: rows)
    }
    func subnetEdit(_ subnet: Any?, _ slot: String? = "A") {
        var fields = [Field("name", "Wifi name", J.s(subnet, "name")),
                      Field("password", subnet == nil ? "Password" : "New password", nil, "password").help(subnet == nil ? "8–63 characters." : "Leave blank to keep the current password.").when("open_network", "False"),
                      Field("enabled", "Turn this network on", subnet == nil || J.b(subnet, "enabled"), "bool"),
                      Field("open_network", "No password (open network)", J.b(subnet, "open_network"), "bool").help("Anyone nearby could join. Use only for a captive portal or guest kiosk."),
                      Field("wan_access", "Allow internet access", subnet == nil || J.b(subnet, "wan_access"), "bool").help("Turn off for local-only devices such as some cameras."),
                      Field("rate", "Bandwidth limit (percent of your connection)", J.s(subnet, "rate_limit_pct").nullIfEmpty ?? "100").help("100 means no limit.")]
        if subnet == nil {
            fields.append(Field("type", "Slot", slot ?? "A", "hidden"))
            fields.append(Field("kind", "What is it for?", "IoT", "choice", "IoT", "Kids", "Business", "Guest").help("IoT is for smart-home devices like plugs, cameras and speakers."))
        }
        edit(subnet == nil ? "Add additional wifi" : "Edit additional wifi", fields, { [self] values in
            try MainWindow.validateSsid(v(values, "name"))
            guard let rate = Num.parseInt(v(values, "rate")), rate >= 1, rate <= 100 else { throw AppError.argument("Enter a bandwidth percentage between 1 and 100.") }
            let password = v(values, "password").nullIfEmpty ?? J.s(subnet, "password")
            if !flag(values, "open_network") { try MainWindow.validatePassword(password) }
            let body = J.obj("name", v(values, "name").trim(), "enabled", flag(values, "enabled"), "open_network", flag(values, "open_network"), "wan_access", flag(values, "wan_access"), "rate_limit_pct", rate)
            if subnet != nil {
                for key in ["network_id", "subnet_id", "subnet_type", "subnet_kind", "dedicated_subnet"] { if let existing = J.get(subnet, key) { body[key] = existing } }
            } else {
                body["network_id"] = (networkUrl ?? "").trimEnd("/").split("/").last ?? ""; body["subnet_type"] = v(values, "type"); body["subnet_kind"] = v(values, "kind")
            }
            if !flag(values, "open_network") { body["password"] = password }
            if !(await confirm("Save this wifi network? Devices using it may disconnect.")) { return }
            try await change("PUT", try featureUrl("subnets_config"), body, false, "Wifi subnet saved.")
        })
    }
    func subnetFiltersPage(_ data: Any?) {
        sub("Separate content filters for each extra wifi network, for example stricter filters on a kids' network. Your main network uses Security & privacy.")
        var rows: [Row] = []
        for subnet in MainWindow.entries(data, "subnets", "configurations") {
            rows.append(settingRow(J.first(subnet, "name", "subnet_type").nullIfEmpty ?? "Wifi network", MainWindow.friendly(J.s(subnet, "subnet_kind")).nullIfEmpty, nil, rowActionAsync("Edit filters", { [self] in try await editSubnetFilters(subnet) })))
        }
        if rows.isEmpty { rows.append(infoRow("No extra wifi networks", nil, data == nil ? "eero hasn't returned your wifi networks. Choose Refresh at the top right to try again." : "Add an additional wifi network first, then set its filters here.")) }
        group(nil, rows: rows)
        relatedPagesFor("Settings/Additional wifi networks", "Home/Security & privacy")
    }
    func editSubnetFilters(_ subnet: Any?) async throws {
        let id = J.s(subnet, "subnet_id").nullIfEmpty ?? J.s(subnet, "subnet_type"); if id.isEmpty { throw AppError.invalidOperation("eero did not provide a subnet identifier.") }
        let data = try await read(try featureUrl("subnets_config/" + id.escapedData + "/dns_policies/content_filters"))
        let map = JObject()
        for candidate in [J.get(data, "dns_policies"), J.get(data, "unified_content_filters", "dns_policies"), J.get(data, "premium_dns", "dns_policies"), J.get(data, "premium_dns", "unified_content_filters", "dns_policies")] {
            if let values = candidate as? JObject { for (key, value) in values where value is Bool { map[key] = value } }
        }
        if map.count == 0 { throw AppError.invalidOperation("No editable content filters were returned for this subnet.") }
        edit("Wifi content filters", map.map { Field($0.key, MainWindow.human($0.key), $0.value, "bool") }, { [self] values in
            let filters = JObject(); for (key, _) in map { filters[key] = flag(values, key) }
            try await change("PUT", try featureUrl("subnets_config/dns_policies/content_filters"), J.obj("subnets", [id], "content_filters", filters), false, "Subnet content filters updated.")
        })
    }
    func bandSecurity() async throws {
        let path = try featureUrl("wpa3_per_band"); let data = try await read(path)
        edit("Security by radio band", [Field("band_2_4_ghz", "2.4 GHz security", J.s(data, "band_2_4_ghz"), "choice", "WPA2", "WPA2_WPA3", "WPA3"), Field("band_5_ghz", "5 GHz security", J.s(data, "band_5_ghz"), "choice", "WPA2", "WPA2_WPA3", "WPA3")], { [self] values in
            if !(await confirm("Change wifi authentication modes? Some devices may need to reconnect or may not support the new mode.")) { return }
            try await change("PUT", path, J.obj("band_2_4_ghz", v(values, "band_2_4_ghz"), "band_5_ghz", v(values, "band_5_ghz")), false, "Per-band wifi security updated.")
        }, "Save", "6 GHz uses WPA3 as required by that band; this endpoint edits the 2.4 and 5 GHz modes.")
    }
    func multiStaticPage(_ data: Any?) {
        sub("Use a block of extra public IP addresses from your internet provider. This only works if your ISP gives you more than one static IP.")
        let lan = J.get(data, "multistaticip_settings"), nat = J.get(data, "multistaticip_settings_nat_portfwd")
        let enabled = J.b(data, "enabled"); let type = J.s(data, "type").nullIfEmpty ?? "PublicLAN"
        let lanLabel = "Public addresses on devices", natLabel = "Port forwarding (NAT)"
        group(nil,
            settingRow("Multiple static IPs", "Public addresses on devices gives each device its own public IP. Port forwarding keeps devices private and forwards the extra IPs.", data == nil ? "Not reported" : enabled ? "On · " + MainWindow.friendly(type) : "Off", rowAction("Change", { [self] in
                edit("Multiple static IPs", [
                    Field("enabled", "Use multiple static IPs", J.b(data, "enabled"), "bool"),
                    Field("type", "How to use them", type == "NATWithPortFwd" ? natLabel : lanLabel, "choice", lanLabel, natLabel).when("enabled", "True"),
                    Field("subnet", "Public subnet address", J.s(lan, "subnet_ip")).when("type", lanLabel),
                    Field("mask", "Public subnet mask", J.s(lan, "subnet_mask")).when("type", lanLabel),
                    Field("router", "ISP router address", J.s(lan, "router_ip")).when("type", lanLabel),
                    Field("start", "First public IP", J.s(nat, "subnet_ip_start")).when("type", natLabel),
                    Field("end", "Last public IP", J.s(nat, "subnet_ip_end")).when("type", natLabel)], { [self] values in
                    let chosen = v(values, "type") == natLabel ? "NATWithPortFwd" : "PublicLAN"
                    let body = J.obj("enabled", flag(values, "enabled"), "type", chosen)
                    if flag(values, "enabled") {
                        if chosen == "PublicLAN" {
                            body["multistaticip_settings"] = J.obj("subnet_ip", try FeatureContracts.address(v(values, "subnet"), AddressFamily.interNetwork), "subnet_mask", try FeatureContracts.address(v(values, "mask"), AddressFamily.interNetwork), "router_ip", try FeatureContracts.address(v(values, "router"), AddressFamily.interNetwork))
                        } else {
                            body["multistaticip_settings_nat_portfwd"] = J.obj("subnet_ip_start", try FeatureContracts.address(v(values, "start"), AddressFamily.interNetwork), "subnet_ip_end", try FeatureContracts.address(v(values, "end"), AddressFamily.interNetwork))
                        }
                    }
                    if !(await confirm("Apply multiple-static-IP settings supplied by your ISP? Incorrect values can disrupt internet access.")) { return }
                    try await change("PUT", "/2.3/networks/" + ((networkUrl ?? "").trimEnd("/").split("/").last ?? "") + "/multistaticip", body, false, "Multiple static IP settings updated.")
                })
            })))
        if enabled && type == "PublicLAN" {
            group("Current addresses", infoRow("Public subnet", J.s(lan, "subnet_ip") + " / " + J.s(lan, "subnet_mask")), infoRow("ISP router", J.s(lan, "router_ip")))
        } else if enabled {
            group("Current addresses", infoRow("Public IP range", J.s(nat, "subnet_ip_start") + " – " + J.s(nat, "subnet_ip_end")))
        }
    }
    func businessPortalPage(_ data: Any?) {
        sub("A branded sign-in page guests see before they can use your guest wifi. Useful for shops, cafés and offices.")
        let configs = MainWindow.entries(data, "configurations")
        for config in configs {
            let configId: () throws -> Int = {
                guard let id = Int(J.first(config, "configuration_id", "id").trim()) else { throw AppError.invalidOperation("eero didn't return an ID for this portal.") }
                return id
            }
            let captureEmail = J.b(config, "capture_email"), captureName = J.b(config, "capture_name")
            let session: String
            if let minutes = Int(J.s(config, "access_expiration_in_minutes").trim()) {
                session = minutes % 1440 == 0 ? "\(minutes / 1440)" + (minutes == 1440 ? " day" : " days") : minutes % 60 == 0 ? "\(minutes / 60)" + (minutes == 60 ? " hour" : " hours") : "\(minutes) minutes"
            } else { session = "Not set" }
            group(J.s(config, "header").nullIfEmpty ?? "Guest sign-in portal",
                settingRow("Heading & theme", "The text and colors guests see.", (J.s(config, "header").nullIfEmpty ?? "No heading") + " · " + (J.s(config, "color_theme").nullIfEmpty ?? "Light"), rowAction("Change", { [self] in portalEdit(config) })),
                settingRow("Session length", "How long guests stay signed in before seeing the portal again.", session, rowAction("Change", { [self] in portalEdit(config) })),
                settingToggle("Ask for visitor email", "Guests enter an email address before getting online.", captureEmail, { [self] on in
                    try await change("PUT", try featureUrl("captive_portal/configuration"), J.obj("id", try configId(), "capture_email", on, "capture_name", captureName), false, "Visitor email " + (on ? "required." : "no longer requested."))
                }),
                settingToggle("Ask for visitor name", "Guests enter their name before getting online.", captureName, { [self] on in
                    try await change("PUT", try featureUrl("captive_portal/configuration"), J.obj("id", try configId(), "capture_email", captureEmail, "capture_name", on), false, "Visitor name " + (on ? "required." : "no longer requested."))
                }),
                settingRow("Logo", "A PNG or JPEG image shown on the portal.", J.s(config, "business_logo_url").isEmpty ? "None" : "Added", rowActionAsync("Upload", { [self] in try await uploadPortalAsset(config, true) })),
                settingRow("Terms of service", "A PDF guests can read before they accept.", J.s(config, "business_terms_of_service_url").isEmpty ? "None" : "Added", rowActionAsync("Upload", { [self] in try await uploadPortalAsset(config, false) })),
                settingRow("Use for guest wifi", "Show this portal when someone joins your guest network.", nil, rowActionAsync("Use", { [self] in
                    let id = try configId()
                    if !(await confirm("Use this sign-in portal for guest wifi?")) { return }
                    try await change("PUT", try featureUrl("captive_portal/subnet_id/GUEST"), J.obj("captive_portal_configuration_id", id), false, "Guest portal selected.")
                })))
        }
        if data != nil && configs.isEmpty { group(nil, infoRow("No guest portals yet", nil, "Guest portals are created for business networks through eero. Once one exists, you can edit it here.")) }
        group("Other networks", settingRow("Use on my other networks", "Turn the business guest portal on for every network in your account.", nil, rowAction("Change", { [self] in
            edit("Business guest network", [Field("other", "Use the business guest portal on my other networks", false, "bool")], { [self] values in
                try await change("PUT", try featureUrl("business_guest_network"), try ApkContracts.businessGuest(flag(values, "other")), false, "Business guest network updated.")
            })
        })))
        relatedPages()
    }
    func portalEdit(_ config: Any?) {
        edit("Guest portal branding", [Field("header", "Heading", J.s(config, "header")), Field("sub_header", "Subheading", J.s(config, "sub_header")), Field("color_theme", "Theme", J.s(config, "color_theme").nullIfEmpty ?? "Light", "choice", "Light", "Dark"),
                                       Field("minutes", "Session length in minutes", J.s(config, "access_expiration_in_minutes")).help("For example 60 for one hour or 1440 for one day."),
                                       Field("logo", "Hosted business logo URL (optional)", J.s(config, "business_logo_url")), Field("terms", "Hosted business terms URL (optional)", J.s(config, "business_terms_of_service_url"))], { [self] values in
            guard let id = Int(J.first(config, "configuration_id", "id").trim()), let minutes = Num.parseInt(v(values, "minutes")), minutes >= 1, minutes <= 525600 else {
                throw AppError.argument("Enter a session duration between 1 and 525600 minutes.")
            }
            for key in ["logo", "terms"] where !v(values, key).isEmpty {
                guard let uri = URLComponents(string: v(values, key)), uri.scheme == "https", !(uri.host ?? "").isEmpty, uri.user == nil, uri.password == nil else { throw AppError.argument("Use HTTPS URLs for hosted assets and terms.") }
            }
            let body = J.obj("id", id, "header", v(values, "header"), "sub_header", v(values, "sub_header"), "color_theme", v(values, "color_theme"), "access_expiration_in_minutes", minutes, "business_logo_url", v(values, "logo"), "business_terms_of_service_url", v(values, "terms"))
            try await change("PUT", try featureUrl("captive_portal/configuration"), body, false, "Guest portal branding updated.")
        })
    }
}
