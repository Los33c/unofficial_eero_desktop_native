import Foundation

// Checks that need the macOS platform layer or the feature pages (added after the core suite):
// ServiceLinks (secure browser allow-list), Bluetooth service filter, VPN helper and profile names,
// update/port state wording, and a render of every page of the preview network in light and dark.
extension Tests {
    /// C# `catch (InvalidDataException)` — the action must fail with AppError.invalidData.
    static func rejectsData(_ action: () throws -> Void) -> Bool {
        do { try action(); return false } catch { return (error as? AppError)?.kind == .invalidData }
    }
    static func failsWithAppError(_ action: () throws -> Void) -> Bool {
        do { try action(); return false } catch { return error is AppError }
    }
    static func platformChecks() throws {
        for url in ["http://account.eero.com", "https://eero.com.attacker.example/", "https://user@account.eero.com/", "https://account.eero.com:444/", "file:///C:/test.html", "javascript:alert(1)", "https://127.0.0.1/"] {
            try check(!ServiceLinks.allowed(url), "service browser rejects " + url)
        }
        try check(ServiceLinks.allowed("https://account.eero.com/") && ServiceLinks.allowed("https://guardianapp.com/login?connect-api-env=wifi-company-prod"), "verified eero and Guardian services allowed")
        try check(try ServiceLinks.tokenHeader("test-temporary-token") == "x-temp-user-token: test-temporary-token\r\n", "API temporary service header")
        try check(rejectsData { _ = try ServiceLinks.tokenHeader("token\r\nCookie: s=private") }, "temporary token cannot inject another header")
        try check(rejectsData { _ = try ServiceLinks.eero("https://guardianapp.com/") }, "temporary eero session target excludes partner hosts")
        try check(ServiceLinks.keepTemporaryHeader("https://account.eero.com/start?x=1", "https://account.eero.com/start?x=1"), "temporary header allowed only for initial target")
        try check(!ServiceLinks.keepTemporaryHeader("https://outside.example/start", "https://outside.example/start"), "temporary header cannot be initialized for an unrelated host")
        for redirected in ["https://stripe.com/start?x=1", "https://account.eero.com/asset.js", "https://api-user.e2ro.com/2.2/account", "http://account.eero.com/start?x=1", "https://account.eero.com/start?x=2"] {
            try check(!ServiceLinks.keepTemporaryHeader("https://account.eero.com/start?x=1", redirected), "temporary header stripped from redirected or unrelated resource")
        }
        try check(ServiceLinks.providerNavigation("https://support.example-provider.com/chat") && !ServiceLinks.providerNavigation("http://support.example-provider.com/") && !ServiceLinks.providerNavigation("https://user@support.example-provider.com/"), "provider support links stay on HTTPS without embedded credentials")
        try check(NativeBluetooth.eeroService == UUID(uuidString: "d99a3d3f-c1ef-4533-809d-82de05d509ca"), "BLE scanner filters the API eero setup service")
        let scope = try VpnContracts.scope("account-1", "network-1")
        let name = try MacVpn.tunnel(scope)
        try check(scope != (try VpnContracts.scope("account-2", "network-1")) && scope != (try VpnContracts.scope("account-1", "network-2")) && name.count <= 15 && Regex.isMatch(name, "^[a-z0-9-]+$"),
                  "VPN profile names isolate accounts and networks without exposing identifiers")
        try check(failsWithAppError { _ = try MacVpn.tunnel("../../etc") }, "VPN profile names reject anything but a validated scope")
        try check(MacVpn.elevatedControl("../invalid") == 2, "VPN helper rejects invalid request IDs before reading files or changing the system")
        try check(rejectsData { _ = try VpnContracts.gateway("valid.sgw.guardianapp.com\n") }, "VPN gateway with a trailing line break is rejected")
    }

    static func pageChecks() throws { try MainActor.assumeIsolated { try pageChecksOnMain() } }
    @MainActor static func pageChecksOnMain() throws {
        let onLatest: [Any] = [J.obj("os_version", "7.1.0"), J.obj("os_version", "7.1.0")]
        try check(MainWindow.updateState(J.obj("has_update", false, "can_update_now", true, "target_firmware", "7.1.0"), onLatest) == "current", "can_update_now alone does not report an update when eero says there is none")
        try check(MainWindow.updateState(J.obj("can_update_now", true), []) == "unknown", "can_update_now without an update flag is not shown as an available update")
        try check(MainWindow.updateState(J.obj("update_status", "up_to_date", "can_update_now", true), onLatest) == "current", "up_to_date status wins over can_update_now")
        try check(MainWindow.updateState(J.obj("has_update", true, "target_firmware", "v7.1.0"), onLatest) == "current", "eeros already on the target version are up to date")
        try check(MainWindow.updateState(J.obj("has_update", true, "update_to_firmware", "7.2.0"), onLatest) == "available", "a newer target version is reported as available")
        try check(MainWindow.updateState(J.obj("has_update", true, "target_firmware", "7.2.0"), [J.obj("os_version", "7.2.0"), J.obj("os_version", "7.1.0")]) == "available", "one eero behind the target still has an update")
        try check(MainWindow.friendlyValue("port_status", "DATA_CONNECTED") == "Data connected" && MainWindow.portState("DATA_CONNECTED") == "Connected" && MainWindow.portState("DISCONNECTED") == "Nothing plugged in", "port status codes read as words")

        // Every page of the sample network builds without crashing, in both themes, and shows content.
        let saved = UiPreferences.theme
        defer { UiPreferences.theme = saved }
        let routes = ["Home", "Devices", "Activity", "Settings", "Help", "Theme preview", "Product gallery"] + FeatureCatalog.entries.map { $0.path }
            + ["Home/Profiles/Profile", "Home/eeros/Details"]
        for theme in ["Light", "Dark"] {
            UiPreferences.theme = theme
            let model = MainWindow(preview: true)
            var empty: [String] = []
            for route in routes {
                model.previewPage(route)
                if model.pageBlocks.count < 2 { empty.append(route) }
            }
            try check(empty.isEmpty && model.screen == .shell, "every page renders content in " + theme + " preview" + (empty.isEmpty ? "" : " (empty: " + empty.joined(separator: ", ") + ")"))
        }
        try check(MainWindow.ungroupedMenuEntries().isEmpty, "every Settings and Advanced networking page has a menu group")
    }
}
