import AppKit
import Foundation
import SwiftUI

/// The app's window model: same responsibilities and page vocabulary as the Windows `MainWindow`.
/// Page functions append `Block`s to `page`; `renderPage()` publishes them to SwiftUI.
@MainActor
final class MainWindow: ObservableObject {
    enum Screen: Equatable { case login(verify: Bool), shell }

    // Published state the SwiftUI views observe.
    @Published var screen: Screen = .login(verify: false)
    @Published var pageBlocks: [Block] = []
    @Published var noticeText = ""
    @Published var noticeIsError = false
    @Published var busy = false
    @Published var renderToken = 0
    @Published var navigationSelection: String? = "Home"

    var api: EeroApi
    var page: [Block] = []
    var preview: Bool
    var loading = false
    var currentPage = "Home"
    var networkUrl: String?
    var account: Any?
    var network: Any?
    var networkList: [Any] = []
    var devices: [Any] = [], nodes: [Any] = [], profiles: [Any] = []
    var lastRefresh = Date()
    var resendAfter = Date.distantPast
    var loadErrors: [String: String] = [:]
    var loadErrorOrder: [String] = []
    var formatPhone = true
    let statusPath: String?
    var timer: Timer?
    var backgroundLaunch: Bool
    var featureData: [String: Any] = [:]
    var selectedProfile: Any?
    var selectedNode: Any?
    var history: [String] = []
    weak var window: NSWindow?
    // Desktop notifications / background mode (Desktop.swift).
    var notificationTimer: Timer?
    var pollingNotifications = false
    var notificationEpoch = 0
    var notificationJournal: NotificationJournal?
    var exiting = false
    /// Per-page state owned by feature files lives here, keyed by name (extensions cannot add stored properties).
    var state: [String: Any] = [:]

    init(preview isPreview: Bool, statusPath diagnosticsPath: String? = nil, background startInBackground: Bool = false) {
        statusPath = diagnosticsPath
        preview = isPreview; backgroundLaunch = startInBackground && !isPreview
        api = EeroApi(SessionStore(AppPaths.data("session.dat")))
        reportStatus("ready")
        if preview { sampleData(); showShell() }
        else { showLogin(false) }
    }

    /// Called once the window exists (restores the saved session).
    func startup(amazonNativeSignIn: Bool) {
        configureDesktopNotifications()
        if preview { return }
        Task { @MainActor in
            if amazonNativeSignIn { amazonConsumerSignIn(); return }
            if api.restore() { await run({ try await self.loadAccount(); self.showShell(); self.reportStatus("dashboard_loaded") }, "Restoring your session…") }
            if !api.signedIn && backgroundLaunch { signInAttention() }
            if api.signedIn { await pollNotifications() }
        }
    }

    func reportStatus(_ state: String, _ error: Error? = nil) {
        guard let statusPath = statusPath, !statusPath.isEmpty else { return }
        let apiError = error as? ApiException
        let json = J.stringify(J.obj("state", state, "time_utc", Dates.roundTrip(Date()), "error_type", error == nil ? "" : String(describing: type(of: error!)),
                                     "transport_category", error == nil || apiError != nil ? "" : ConnectionDiagnostics.category(error!), "native_error", error == nil ? 0 : ConnectionDiagnostics.nativeCode(error!),
                                     "http_code", apiError?.code ?? 0, "api_error", apiError?.apiError ?? "", "network_count", networkList.count, "device_count", devices.count,
                                     "node_count", nodes.count, "profile_count", profiles.count, "section_errors", loadErrorOrder))
        try? json.write(toFile: statusPath, atomically: true, encoding: .utf8)
    }

    // MARK: Notice, errors and the busy runner
    func setNotice(_ message: String, _ error: Bool = false) { noticeText = message; noticeIsError = error }
    nonisolated static func message(_ e: Error) -> String {
        if let u = e as? URLError { return ConnectionDiagnostics.userMessage(u) }
        if e is CancellationError { return ConnectionDiagnostics.userMessage(URLError(.timedOut)) }
        if let a = e as? AppError {
            if a.kind == .crypto || a.kind == .unauthorized { return "macOS could not unlock your saved session in the Keychain. Try signing in without Remember me." }
            return a.message
        }
        if let a = e as? ApiException { return a.message }
        let ns = e as NSError
        if ns.domain == NSURLErrorDomain { return ConnectionDiagnostics.userMessage(URLError(URLError.Code(rawValue: ns.code))) }
        return e.localizedDescription
    }
    func run(_ action: @escaping () async throws -> Void, _ status: String) async {
        if busy { return }
        busy = true
        setNotice(status)
        defer { busy = false }
        do { try await action() }
        catch let ex as ApiException {
            reportStatus("error", ex)
            if ex.sessionExpired {
                api.forget(); stopTimer(); clearData(); showLogin(false)
                setNotice("Your session expired. Sign in again to continue.", true)
            } else { setNotice(MainWindow.message(ex), true) }
        } catch {
            reportStatus("error", error); setNotice(MainWindow.message(error), true)
        }
    }
    /// Runs a page button: `run` buttons get the busy state and error notice, local ones run directly.
    func perform(_ spec: ButtonSpec) {
        if spec.local {
            if busy { return }
            Task { @MainActor in
                do { try await spec.action() } catch { self.setNotice(MainWindow.message(error), true) }
            }
        } else {
            Task { @MainActor in await self.run(spec.action, "Working…") }
        }
    }
    func runToggle(_ title: String, _ wanted: Bool, _ apply: @escaping (Bool) async throws -> Void) {
        if busy { return }
        Task { @MainActor in
            await self.run({ try await apply(wanted) }, "Saving " + title.lowercased() + "…")
            self.renderToken += 1
        }
    }

    func clearData() {
        notificationEpoch += 1; closeAccountWindows(); history.removeAll(); account = nil; network = nil; selectedProfile = nil; selectedNode = nil
        networkUrl = nil; currentPage = "Home"; networkList.removeAll(); devices.removeAll(); nodes.removeAll(); profiles.removeAll(); loadErrors.removeAll(); loadErrorOrder.removeAll()
        featureData.removeAll(); clearPlatformState()
    }
    func closeAccountWindows() { Dialogs.closeAll(); AuxiliaryWindows.closeAll() }
    /// Account-scoped feature state that ClearData resets on Windows (Bluetooth results, wired setup).
    func clearPlatformState() { state.removeValue(forKey: "bluetoothResults"); state.removeValue(forKey: "wiredSetup") }
    /// Storage for feature-file fields: `var usageDays: Int { get { stored("usageDays", 7) } set { state["usageDays"] = newValue } }`.
    func stored<T>(_ key: String, _ initial: @autoclosure () -> T) -> T {
        if let v = state[key] as? T { return v }
        let v = initial(); state[key] = v; return v
    }

    // MARK: Login
    func showLogin(_ verify: Bool) {
        stopTimer()
        screen = .login(verify: verify)
    }
    func sendCode(_ identifier: String, _ remember: Bool) async throws {
        var id = identifier.trim()
        if formatPhone { id = try PhoneInput.normalize(id) }
        else if !Regex.isMatch(id, "^[^\\s@]+@[^\\s@]+\\.[^\\s@]+$") { throw AppError.argument("Enter the email address linked to your eero account.") }
        api.remember = remember
        closeAccountWindows()
        try await api.beginLogin(id)
        resendAfter = Date().addingTimeInterval(60)
        showLogin(true); reportStatus("code_requested"); setNotice("Code requested. Check your email or text messages.")
    }
    func verifyCode(_ input: String) async throws {
        let code = input.trim()
        if !Regex.isMatch(code, "^[0-9]{4,10}$") { throw AppError.argument("Enter the numeric code from your email or text message.") }
        if api.isRegistering { try await api.verifyRegistration(code, registrationCountry, registrationConsents) }
        else { try await api.verify(code) }
        reportStatus("authenticated"); showShell(); try await loadAccount(); showShell(); reportStatus("dashboard_loaded")
    }
    func resendCode() async throws {
        let wait = Int(ceil(resendAfter.timeIntervalSinceNow))
        if wait > 0 { throw AppError.invalidOperation("You can request another code in \(wait) seconds.") }
        try await api.resend(); resendAfter = Date().addingTimeInterval(60); setNotice("Another code has been requested.")
    }
    func checkConnection() async throws {
        try await api.checkConnection()
        setNotice("Connected to eero securely. You can request your verification code.")
        reportStatus("connection_ok")
    }

    // MARK: Loading
    func loadAccount() async throws {
        account = try await api.send("GET", "account")
        networkList = J.list(J.get(account, "networks"))
        if networkList.isEmpty { networkList = J.list(try await api.send("GET", "networks")) }
        networkUrl = networkList.isEmpty ? nil : J.s(networkList[0], "url")
        if !(networkUrl ?? "").isEmpty { try await loadNetwork() }
    }
    func resource(_ o: Any?, _ key: String, _ fallback: String) -> String {
        let r = J.get(o, "resources", key)
        let s = (r as? String) ?? J.s(r, "url")
        return s.isEmpty ? fallback : s
    }
    func loadNetwork() async throws {
        guard let url = networkUrl, !url.isEmpty else { return }
        loadErrors.removeAll(); loadErrorOrder.removeAll()
        network = try await api.send("GET", url)
        devices = try await loadList("Devices", resource(network, "devices", url + "/devices"))
        nodes = try await loadList("eero nodes", resource(network, "eeros", url + "/eeros"))
        profiles = try await loadList("Profiles", resource(network, "profiles", url + "/profiles"))
        lastRefresh = Date()
    }
    func loadList(_ name: String, _ url: String) async throws -> [Any] {
        do { return J.list(try await api.send("GET", url)) }
        catch let ex as ApiException { if ex.sessionExpired { throw ex }; setLoadError(name, MainWindow.message(ex)); return [] }
        catch let ex as AppError where ex.kind == .argument || ex.kind == .invalidData { throw ex }
        catch { setLoadError(name, MainWindow.message(error)); return [] }
    }
    func setLoadError(_ name: String, _ message: String) { if loadErrors[name] == nil { loadErrorOrder.append(name) }; loadErrors[name] = message }
    func showShell() { screen = .shell; renderPage(); if !preview { startTimer() } }
    func previewPage(_ name: String) {
        if name == "Login" { showLogin(false); return }
        if name == "Home/Profiles/Profile" { selectedProfile = J.obj("name", "Family", "url", "/2.2/networks/preview/profiles/1", "paused", false, "devices", Array(devices.prefix(2)), "schedule", [J.obj("name", "Bedtime", "enabled", true, "days", ["MON", "TUE", "WED", "THU", "FRI"], "start", "22:00", "end", "07:00")], "premium_dns", J.obj("dns_policies", J.obj("block_malware", true, "safe_search_enabled", true))) }
        if name == "Home/eeros/Details" { selectedNode = nodes.first }
        currentPage = name; featureData[name] = previewFeature(name) ?? JNull.shared; renderPage()
    }

    // MARK: Rendering
    func renderPage() {
        guard screen == .shell else { return }
        loading = true; page = []
        updateNavigation()
        pageHeading()
        if network == nil && !accountOnlyPage(currentPage) {
            // No network on this account yet: explain, then offer every way to get one.
            group(nil, infoRow("No network yet", nil, "Your account doesn't have an eero network the app can open. If you just set one up, choose Refresh at the top right."))
            addGroupHeading("Get started")
            page.append(rowGroup(["Settings/User permissions/Join a network", "Settings/User permissions/Incoming access invitations", "Settings/Network management/Pending transfers", "Home/Add or replace eero/Create a network"]
                .compactMap { path in FeatureCatalog.entries.first { $0.path == path } }.map { listRow($0, false) }))
            publishPage(); return
        }
        if currentPage == "Home" || currentPage == "Overview" { overview() }
        else if currentPage == "Devices" { devicePage() }
        else if currentPage == "eero nodes" { nodePage() }
        else if currentPage == "Profiles" { profilesPageV2() }
        else if currentPage == "Activity" { activityMenu() }
        else if currentPage == "Settings" { featureMenu("Settings") }
        else if currentPage == "Help" { helpPage() }
        else { renderFeature() }
        markDangerActions()
        setNotice(preview ? "Preview mode · No network requests or changes" : "Updated " + Dates.format(lastRefresh, "h:mm:ss a") + " · Refreshes every minute while active")
        publishPage()
    }
    private func publishPage() { pageBlocks = page; renderToken += 1; loading = false }
    func sub(_ text: String) { if text.caseInsensitiveCompare(currentPage.split("/").last ?? "") != .orderedSame { page.append(self.text(text, 14, "#64738A")) }; page.append(.spacer(10)) }
    func availability(_ key: String) { if let e = loadErrors[key] { page.append(card([text("Could not load " + key.lowercased() + ": " + e, 14, "#B5333D")])) } }
    func overview() { nativeOverview() }
    func speed(_ direction: String) -> String {
        let value = J.s(network, "speed", direction, "value")
        return value.isEmpty ? "—" : value + " " + (J.s(network, "speed", direction, "units").nullIfEmpty ?? "Mbps")
    }
    struct NetworkChoice: Hashable { let name: String; let url: String }
    final class DeviceRow: Identifiable {
        let id = UUID()
        let raw: Any?; let name: String; let status: String; let ip: String; let connection: String; let profile: String
        init(raw: Any?, name: String, status: String, ip: String, connection: String, profile: String) { self.raw = raw; self.name = name; self.status = status; self.ip = ip; self.connection = connection; self.profile = profile }
    }
    static func device(_ d: Any?) -> DeviceRow {
        DeviceRow(raw: d, name: J.first(d, "nickname", "display_name", "hostname", "mac"),
                  status: J.b(d, "blocked") ? "Blocked" : J.b(d, "paused") ? "Paused" : J.b(d, "connected") ? "Online" : "Offline",
                  ip: J.first(d, "ip", "ip_address"), connection: J.b(d, "wireless") ? (J.s(d, "frequency").nullIfEmpty ?? "Wifi") : "Ethernet", profile: J.s(d, "profile", "name"))
    }
    func devicePage() { nativeDevicePage() }
    func detail(_ p: inout [Block], _ name: String, _ value: String?) { p.append(text(name, 12, "#64738A")); p.append(text((value ?? "").isEmpty ? "Unavailable" : value!, 15)) }
    func mutate(_ method: String, _ url: String, _ body: Any?, _ success: String) async throws {
        if preview { throw AppError.invalidOperation("Preview mode is read only. Sign in to manage your network.") }
        try await api.send(method, url, body); try await loadNetwork(); renderPage(); setNotice(success)
    }
    func deviceDialog(_ d: Any?) {
        let row = MainWindow.device(d)
        var p: [Block] = [text(row.name, 25, "#17263C", true), statusBadge(row.status, row.status == "Online"), .spacer(14)]
        var handle: DialogHandle? = nil
        p.append(contentsOf: devicePanel(d, { handle?.close() }))
        handle = dialog(row.name, p)
        handle?.showDialog()
    }
    func nodePage() {
        sub("The eeros that make up your wifi. Open one to see its details, rename it or restart it."); availability("eero nodes")
        for n in nodes { page.append(nodeCard(n)) }
        if nodes.isEmpty && loadErrors["eero nodes"] == nil { page.append(card([text("eero didn't return any eeros for this network. Choose Refresh at the top right to try again.")])) }
        relatedPagesFor("Home/eeros/Placement tests", "Home/Add or replace eero")
    }
    func nodeCard(_ n: Any?) -> Block { nativeNodeCard(n) }
    func startSpeedTest() async throws {
        if preview { throw AppError.invalidOperation("Preview mode is read only.") }
        try await api.send("POST", resource(network, "speedtest", (networkUrl ?? "") + "/speedtest"))
        setNotice("Speed test started. Results can take a few minutes. Use Refresh results on the Activity page.")
    }
    func showApplicationError(_ message: String) { info("eero Desktop", J.obj("message", message)) }
    nonisolated static func validateName(_ s: String) throws { if s.trim().isEmpty || s.trim().count > 100 { throw AppError.argument("Enter a name between 1 and 100 characters.") } }
    nonisolated static func validateSsid(_ s: String) throws { if s.trim().isEmpty || s.trim().utf8.count > 32 { throw AppError.argument("Wifi names must contain between 1 and 32 UTF-8 bytes.") } }
    nonisolated static func validatePassword(_ s: String) throws {
        if s.count < 8 || s.count > 63 || s.unicodeScalars.contains(where: { $0.value < 32 || $0.value > 126 }) { throw AppError.argument("Use a wifi password with 8–63 printable ASCII characters.") }
    }

    // MARK: Timers
    func startTimer() {
        guard timer == nil, !preview else { return }
        timer = Timer.scheduledTimer(withTimeInterval: 60, repeats: true) { [weak self] _ in
            Task { @MainActor in
                guard let self = self else { return }
                if !self.busy && !self.preview && self.api.signedIn && NSApp.isActive && (self.currentPage == "Home" || self.currentPage == "Devices") {
                    await self.run({ try await self.loadNetwork(); self.renderPage() }, "Refreshing network…")
                }
            }
        }
    }
    func stopTimer() { timer?.invalidate(); timer = nil }

    // MARK: Sample data (preview mode)
    func sampleData() {
        account = try? J.parse("{\"name\":\"Preview account\"}"); networkUrl = "/2.2/networks/preview"
        networkList = J.list(try? J.parse("[{\"name\":\"Maple House\",\"url\":\"/2.2/networks/preview\"}]"))
        network = try? J.parse("{\"name\":\"Maple House\",\"health\":{\"internet\":{\"status\":\"connected\"}},\"speed\":{\"down\":{\"value\":942,\"units\":\"Mbps\"},\"up\":{\"value\":118,\"units\":\"Mbps\"}},\"wpa3\":true,\"band_steering\":true,\"sqm\":true,\"upnp\":false,\"ipv6\":true}")
        nodes = J.list(try? J.parse("[{\"location\":\"Living room\",\"model\":\"eero Pro 6E\",\"gateway\":true,\"status\":\"green\",\"os_version\":\"Preview\",\"ip_address\":\"192.168.4.1\",\"connected_clients_count\":3},{\"location\":\"Office\",\"model\":\"eero Pro 6E\",\"gateway\":false,\"status\":\"green\",\"os_version\":\"Preview\",\"ip_address\":\"192.168.4.2\",\"connected_clients_count\":2}]"))
        devices = J.list(try? J.parse("[{\"nickname\":\"Office PC\",\"connected\":true,\"wireless\":false,\"ip\":\"192.168.4.20\",\"manufacturer\":\"Microsoft\",\"mac\":\"02:00:00:00:00:01\"},{\"nickname\":\"Living room TV\",\"connected\":true,\"wireless\":true,\"frequency\":\"5 GHz\",\"ip\":\"192.168.4.34\",\"mac\":\"02:00:00:00:00:02\"},{\"nickname\":\"Kitchen speaker\",\"connected\":true,\"wireless\":true,\"frequency\":\"2.4 GHz\",\"ip\":\"192.168.4.45\",\"mac\":\"02:00:00:00:00:03\"},{\"nickname\":\"Laptop\",\"connected\":true,\"wireless\":true,\"frequency\":\"6 GHz\",\"ip\":\"192.168.4.52\",\"mac\":\"02:00:00:00:00:04\"},{\"nickname\":\"Tablet\",\"connected\":false,\"wireless\":true,\"ip\":\"192.168.4.63\",\"mac\":\"02:00:00:00:00:05\"} ]"))
        profiles = J.list(try? J.parse("[{\"name\":\"Family\",\"paused\":false},{\"name\":\"Kids\",\"paused\":true}]")); lastRefresh = Date()
        let nmap = network as! JObject
        nmap["ipv6_upstream"] = true; nmap["dns"] = J.obj("mode", "automatic", "caching", true, "custom", J.obj("ips", ["1.1.1.1", "8.8.8.8"]))
        nmap["connection"] = J.obj("mode", "nat"); nmap["timezone"] = J.obj("value", "America/Chicago")
        nmap["auto_setup_mode"] = "DEFAULT"; nmap["accessories"] = [J.obj("name", "Sample accessory", "model", "Sample cellular accessory", "dsn", "SAMPLEACCESSORY1", "registered", true, "joined", true, "eero_id", "1")]
        nmap["amazon_account_linked"] = true; nmap["smart_home_hub"] = true
        nmap["lease"] = J.obj("mode", "dhcp"); nmap["pppoe_enabled"] = false; nmap["nat_port_randomization"] = true
        nmap["power_saving"] = false; nmap["power_saving_schedule_enabled"] = false; nmap["thread"] = true
        nmap["dynamic_dns"] = J.obj("enabled", false, "hostname", "sample-network.example")
        // Developer switch for checking availability badges in preview renders.
        if ProcessInfo.processInfo.environment["EERO_DESKTOP_SAMPLE_NO_PLAN"] == "1" { nmap["premium_status"] = "inactive" }
        for (i, d) in devices.enumerated() { (d as! JObject)["url"] = "/2.2/networks/preview/devices/\(i)" }
        for (i, n) in nodes.enumerated() {
            let node = n as! JObject
            node["url"] = "/2.2/eeros/\(i + 1)"; node["serial"] = "PREVIEWNODE000\(i + 1)"; node["led_on"] = true; node["led_brightness"] = 100
            node["resources"] = J.obj("led_action", "/2.2/eeros/\(i + 1)/led", "connections", "/2.2/eeros/\(i + 1)/connections")
        }
        for (i, p) in profiles.enumerated() { (p as! JObject)["url"] = "/2.2/networks/preview/profiles/\(i + 1)" }
    }
}
