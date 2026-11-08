import Foundation

extension Tests {
    /// The platform-neutral part of NativeTests.cs: hardware names, friendly values, badges, learned availability,
    /// menu grouping and search. The MainWindow statics are main-actor isolated; `run` is called on the main thread.
    static func nativeChecks() throws {
        try MainActor.assumeIsolated { try nativeChecksOnMain() }
    }
    @MainActor static func nativeChecksOnMain() throws {
        let cases: [[String]] = [
            ["eero Pro 6E", "S010001", "eero-pro-6e"], ["eero Max 7", "V010001", "eero-max-7"],
            ["eero", "A010001", "eero-gen1"], ["eero", "J010011", "eero-gen2"],
            ["eero Pro", "B010011", "eero-pro"], ["eero Beacon", "D010001", "eero-beacon"],
            ["eero Pro 6", "K010011", "eero-pro-6"], ["eero 6", "N010001", "eero-6"],
            ["eero 6 Extender", "Q010011", "eero-6-extender"], ["eero 6+", "R010001", "eero-6-plus"],
            ["eero PoE 6", "T010001", "eero-poe-6"], ["eero PoE Gateway", "U010001", "eero-poe-gateway"],
            ["eero 7", "PA10001", "eero-7"], ["eero Pro 7", "ME10001", "eero-pro-7"],
            ["eero Outdoor 7", "SN10001", "eero-outdoor-7"], ["eero PoE 7", "NV01", "eero-poe-7"],
            ["eero Signal", "RG4U", "eero-signal-4g"], ["eero Signal", "RG5W", "eero-signal-5g"],
            ["eero 6+ Extender", "", "eero-6-plus-extender"], ["eero Telephone Adapter", "FH01", "eero-telephone-adapter"]
        ]
        for item in cases { try check(ProductImages.resolve(item[0], item[1]) == item[2], "hardware photograph matches " + item[0] + " " + item[1]) }
        try check(ProductImages.resolve("eero Pro 6E", "V010001") == "eero-max-7", "known hardware code takes priority over conflicting label")
        try check(ProductImages.resolve("eero_pro_6e", "") == "eero-pro-6e", "API model-name formatting is normalized")
        try check(ProductImages.resolve("Unrecognized model", "UNKNOWN") == nil && ProductImages.resolve("eero", "") == nil, "unknown or ambiguous models do not display a different product")
        try check(ProductImages.resolve("eero Pro 6E", "") != ProductImages.resolve("eero Pro 6", ""), "Pro 6 and Pro 6E remain distinct")

        let decimalSpeed = Num.format(2.5, 1) + " Gbps"
        try check(MainWindow.friendlyValue("negotiated_speed", "P1000") == "1 Gbps" && MainWindow.friendlyValue("supported_speed", "P2500") == decimalSpeed, "port speed codes read as Mbps/Gbps")
        try check(MainWindow.friendlyValue("port_status", "DATA_CONNECTED") == "Data connected", "port status codes read as words")
        try check(MainWindow.friendlyValue("capable_protocols", "ETHERNET_CABLE_DIAGNOSTICS_PROVIDER") == "Ethernet cable diagnostics provider" && MainWindow.friendlyValue("mode", "WAN_DHCP") == "WAN DHCP", "upper-case codes become sentences with acronyms kept")
        try check(MainWindow.friendlyValue("upload_bytes", 1536) == Num.format(1.5, 1) + " KB" && MainWindow.friendlyValue("rssi", -58) == "Strong (-58 dBm)", "byte counts and signal strength are readable")
        try check(MainWindow.friendlyValue("is_guest", true) == "Yes" && MainWindow.friendlyValue("led_on", false) == "Off" && MainWindow.friendlyValue("name", "Office 2") == "Office 2", "yes/no and on/off fit the field; plain text is untouched")
        try check(MainWindow.human("interface_number") == "Port number" && MainWindow.human("wan_ip_address") == "WAN IP address" && MainWindow.human("networkType") == "Network type", "field names read as plain words")
        try check(MainWindow.ruleBadge("Home/Profiles", J.obj("connection", J.obj("mode", "bridged"))) == "Not in bridge mode" && MainWindow.ruleBadge("Home/Profiles", J.obj("connection", J.obj("mode", "nat"))) == nil, "bridge mode marks routing-only pages")
        try check(MainWindow.ruleBadge("Activity/Security activity", J.obj("premium_status", "inactive")) == "Needs eero Plus" && MainWindow.ruleBadge("Activity/Security activity", J.obj("premium_status", "active")) == nil && MainWindow.ruleBadge("Activity/Security activity", J.obj()) == nil, "Plus badge only when eero clearly reports no plan")
        try check(MainWindow.ruleBadge("Settings/Business guest wifi", J.obj("network_customer_type", "RESIDENTIAL")) == "Business networks only" && MainWindow.ruleBadge("Settings/Wifi name & password", J.obj("network_customer_type", "RESIDENTIAL")) == nil, "business badge only on business pages of home networks")
        try check(FeatureAvailability.reasonFor(403) == "Not available on your account" && FeatureAvailability.reasonFor(404) == "Not available on this network" && FeatureAvailability.reasonFor(500) == nil && FeatureAvailability.reasonFor(0) == nil, "only refusals are remembered, not outages")
        FeatureAvailability.mark("net-1", "Settings/Advanced networking/Thread", "Not available on this network"); FeatureAvailability.reset()
        try check(FeatureAvailability.get("net-1", "Settings/Advanced networking/Thread") == "Not available on this network" && FeatureAvailability.get("net-2", "Settings/Advanced networking/Thread") == nil, "learned badges persist per network")
        FeatureAvailability.clear("net-1", "Settings/Advanced networking/Thread"); FeatureAvailability.reset()
        try check(FeatureAvailability.get("net-1", "Settings/Advanced networking/Thread") == nil, "a page that loads clears its badge")
        let gaps = MainWindow.ungroupedMenuEntries()
        try check(gaps.isEmpty, "grouped settings menus place every catalog page" + (gaps.isEmpty ? "" : ": " + gaps.joined(separator: ", ")))
        try check(MainWindow.searchCatalog("dns", nil, 8).first?.path == "Settings/Advanced networking/DNS", "settings search ranks an exact title first")
        try check(MainWindow.searchCatalog("wifi password", nil, 8).contains { $0.path == "Settings/Wifi name & password" }, "settings search matches every word across title and description")
        try check(MainWindow.searchCatalog("guest", "Settings", 20).allSatisfy { $0.path.hasPrefix("Settings/") }, "page filter stays inside its menu")
        try check(MainWindow.searchCatalog("devices", nil, 8).first?.path == "Devices", "search includes top-level pages")
        try check(MainWindow.searchCatalog("zzqx", nil, 8).isEmpty && MainWindow.searchCatalog("  ", nil, 8).isEmpty, "search returns nothing for unmatched or empty queries")
    }
}
