import Foundation
import Security

// Self-tests (`--self-test <output>`), translated from the Windows Tests.cs harness and the platform-neutral
// checks in RuntimeTests.cs, EverydayTests.cs, ApkTests.cs, ServiceIntegrationTests.cs, AmazonNativeTests.cs,
// ExtendedTests.cs and NativeTests.cs. Each check keeps its Windows assertion and name, except where noted.
// No live requests are made: EeroApi, GuardianApi, AmazonNativeAttempt and PortalUpload use fake transports,
// ProtectedData uses an in-memory random key (never the Keychain) and FeatureAvailability a scratch file.
//
// Adapted checks (honest macOS names):
//  - "DPAPI session round trip" -> "Keychain-key AES-GCM session round trip"
//  - "persisted notification cursor is DPAPI encrypted ..." -> "... is AES-GCM encrypted ..."
//  - Socket 10013 / File Explorer blocked-network checks -> POSIX EPERM under URLError, message names the firewall
//  - Socket 11001 / AuthenticationException / TaskCanceledException -> URLError cannotFindHost / serverCertificateUntrusted / CancellationError
//  - CancellationToken checks -> Swift Task cancellation (CancellationError)
//  - StartupRegistration paths -> absolute macOS executable paths (LaunchAgent command)
//  - "VPN profile names isolate ..." -> checks the 24-character scope itself (WindowsVpn.Tunnel is Windows-only)
//  - "... produces a validated Windows profile" -> "... produces a validated WireGuard profile"
// Dropped (Windows-only or owned by UI/platform helpers):
//  - QRCoder PNG generation; WPF pixel/contrast checks (replaced by "every page renders content in Light/Dark preview" in MacTests.swift)
//  - WindowsVpn.ProtectMachine / DPAPI LocalMachine, VendorSignature.Verify (no macOS meaning)
//  - AppInstance single-instance checks (macOS LaunchServices keeps one instance per app)
// ServiceLinks, NativeBluetooth, MacVpn, UpdateState/PortState and page-render checks live in MacTests.swift.
enum Tests {
    static var results: [String] = []
    struct Failure: Error, CustomStringConvertible { let description: String }

    static func check(_ pass: Bool, _ name: String) throws {
        if !pass { throw Failure(description: "FAIL: " + name) }
        results.append("PASS: " + name)
    }
    /// C# `Throws<T>(action, name)`: passes only when `action` throws an error that `matches`.
    static func expect(_ matches: (Error) -> Bool, _ name: String, _ action: () async throws -> Void) async throws {
        do { try await action() } catch { if matches(error) { results.append("PASS: " + name); return }; throw Failure(description: "FAIL: " + name + " (" + String(describing: error) + ")") }
        throw Failure(description: "FAIL: " + name)
    }
    static func expectSync(_ matches: (Error) -> Bool, _ name: String, _ action: () throws -> Void) throws {
        do { try action() } catch { if matches(error) { results.append("PASS: " + name); return }; throw Failure(description: "FAIL: " + name + " (" + String(describing: error) + ")") }
        throw Failure(description: "FAIL: " + name)
    }
    static func kind(_ k: AppError.Kind) -> (Error) -> Bool { { ($0 as? AppError)?.kind == k } }
    static let argument = kind(.argument), invalidOperation = kind(.invalidOperation), invalidData = kind(.invalidData), io = kind(.io)
    static let api: (Error) -> Bool = { $0 is ApiException }
    static let cancelled: (Error) -> Bool = { $0 is CancellationError }
    /// True when `action` throws an argument error (C# `catch (ArgumentException)`).
    static func rejects(_ action: () throws -> Void) -> Bool {
        do { try action(); return false } catch { return (error as? AppError)?.kind == .argument }
    }

    // MARK: Fake eero transport
    final class Step {
        var method: String, path: String
        var cookie: String?, body: String?, contentType: String?, json: String?, setCookie: String?, query: String?
        var status = 200
        init(_ method: String, _ path: String, cookie: String? = nil, body: String? = nil, contentType: String? = nil, json: String? = nil, setCookie: String? = nil, query: String? = nil, status: Int = 200) {
            self.method = method; self.path = path; self.cookie = cookie; self.body = body; self.contentType = contentType; self.json = json; self.setCookie = setCookie; self.query = query; self.status = status
        }
    }
    final class Fake: HttpTransport {
        private let lock = NSLock()
        private var queue: [Step] = []
        var count: Int { lock.lock(); defer { lock.unlock() }; return queue.count }
        func enqueue(_ s: Step) { lock.lock(); queue.append(s); lock.unlock() }
        private func take() -> Step? { lock.lock(); defer { lock.unlock() }; return queue.isEmpty ? nil : queue.removeFirst() }
        func send(_ r: URLRequest) async throws -> (Data, HTTPURLResponse) {
            let next = take()
            let parts = r.url.flatMap { WebAddress(url: $0) }
            guard let s = next, let u = parts else { throw Failure(description: "Unexpected request: " + (r.httpMethod ?? "") + " " + (parts?.absolutePath ?? "")) }
            try check(r.httpMethod == s.method && u.absolutePath == s.path, "request " + s.method + " " + s.path)
            if let q = s.query { try check(u.query == q, "correct query parameters") }
            try check(u.host == "api-user.e2ro.com" && u.scheme == "https", "HTTPS trusted host")
            try check(r.value(forHTTPHeaderField: "Cookie") == s.cookie, "correct cookie scope")
            if let b = s.body { try check(r.httpBody.map { String(decoding: $0, as: UTF8.self) } == b, "request payload") }
            if let t = s.contentType { try check(Tests.mediaType(r) == t, "request content type") }
            var headers = ["Content-Type": "application/json; charset=utf-8"]
            if let c = s.setCookie { headers["Set-Cookie"] = c }
            return (Data((s.json ?? "{\"meta\":{\"code\":200},\"data\":{}}").utf8), HTTPURLResponse(url: r.url!, statusCode: s.status, httpVersion: "HTTP/1.1", headerFields: headers)!)
        }
    }
    static func mediaType(_ r: URLRequest) -> String? {
        r.value(forHTTPHeaderField: "Content-Type").map { $0.split(";")[0].trim() }
    }

    // MARK: Fake service transport (Guardian, Amazon, storage; can also stream)
    enum Reply {
        case data((URLRequest) async throws -> (Data, HTTPURLResponse))
        case stream((URLRequest) async throws -> HttpStream)
    }
    final class ServiceFake: HttpTransport, StreamingHttpTransport {
        private let lock = NSLock()
        private var queue: [Reply] = []
        var count: Int { lock.lock(); defer { lock.unlock() }; return queue.count }
        func enqueue(_ step: @escaping (URLRequest) async throws -> (Data, HTTPURLResponse)) { lock.lock(); queue.append(.data(step)); lock.unlock() }
        func enqueueStream(_ step: @escaping (URLRequest) async throws -> HttpStream) { lock.lock(); queue.append(.stream(step)); lock.unlock() }
        private func next() throws -> Reply {
            lock.lock(); defer { lock.unlock() }
            if queue.isEmpty { throw Failure(description: "Unexpected service request.") }
            return queue.removeFirst()
        }
        func send(_ r: URLRequest) async throws -> (Data, HTTPURLResponse) {
            switch try next() {
            case .data(let step): return try await step(r)
            case .stream: throw Failure(description: "Unexpected streaming step for a buffered request.")
            }
        }
        func stream(_ r: URLRequest) async throws -> HttpStream {
            switch try next() {
            case .data(let step): let (d, response) = try await step(r); return HttpStream.complete(d, response)
            case .stream(let step): return try await step(r)
            }
        }
    }
    static func response(_ r: URLRequest, _ status: Int, _ body: String = "", _ headers: [String: String] = [:]) -> (Data, HTTPURLResponse) {
        (Data(body.utf8), HTTPURLResponse(url: r.url ?? URL(string: "https://example.invalid/")!, statusCode: status, httpVersion: "HTTP/1.1", headerFields: headers)!)
    }
    static func serviceResponse(_ r: URLRequest, _ data: Any?) -> (Data, HTTPURLResponse) { response(r, 200, J.stringify(data), ["Content-Type": "application/json; charset=utf-8"]) }
    static func bodyOf(_ r: URLRequest) -> Any? { try? J.parse(String(decoding: r.httpBody ?? Data(), as: UTF8.self)) }
    /// Cancels the Task that is running the current request (closing a sign-in window in the Windows tests).
    static func cancelCurrentTask() { withUnsafeCurrentTask { $0?.cancel() } }
    final class Box<T> { var value: T; init(_ v: T) { value = v } }

    /// Runs async checks from the synchronous entry point (called from main() before any UI exists).
    static func blocking(_ body: @escaping () async throws -> Void) throws {
        let done = DispatchSemaphore(value: 0)
        let failure = Box<Error?>(nil)
        Task.detached { do { try await body() } catch { failure.value = error }; done.signal() }
        done.wait()
        if let e = failure.value { throw e }
    }

    static func run(_ output: String) -> Int {
        let outputDir = ((output as NSString).expandingTildeInPath as NSString).deletingLastPathComponent
        let scratch = ((outputDir.isEmpty ? FileManager.default.currentDirectoryPath : outputDir) as NSString).appendingPathComponent("session-tests-" + UUID().uuidString.replacingOccurrences(of: "-", with: ""))
        try? FileManager.default.createDirectory(atPath: scratch, withIntermediateDirectories: true)
        // Tests must never touch the user's Keychain or real availability notes.
        var key = [UInt8](repeating: 0, count: 32); _ = SecRandomCopyBytes(kSecRandomDefault, key.count, &key)
        let testKey = Data(key)
        ProtectedData.keyProvider = { testKey }; ProtectedData.resetCache()
        FeatureAvailability.storePath = (scratch as NSString).appendingPathComponent("availability.json"); FeatureAvailability.reset()
        var exit = 0
        do {
            try blocking { try await runAsync(scratch) }
            try featureChecks()
            try blocking { try await extendedChecks() }
            try blocking { try await runtimeChecks(scratch) }
            try everydayChecks(scratch)
            try blocking { try await serviceIntegrationChecks(scratch) }
            try blocking { try await amazonNativeChecks(scratch) }
            try blocking { try await apkChecks(scratch) }
            try nativeChecks()
            try apkCatalogChecks()
            try platformChecks()
            try pageChecks()
        } catch { results.append(String(describing: error)); exit = 1 }
        try? FileManager.default.removeItem(atPath: scratch)
        try? (results.joined(separator: "\n") + "\n").write(toFile: output, atomically: true, encoding: .utf8)
        return exit
    }

    static func featureChecks() throws {
        try check(J.stringify(try FeatureContracts.reservation("Server", "0200.0000.0001", "192.168.4.20")) == "{\"description\":\"Server\",\"mac\":\"02:00:00:00:00:01\",\"ip\":\"192.168.4.20\"}", "API reservation fields and MAC normalization")
        let forward = try FeatureContracts.forward("192.168.4.20", "8000-8010", "9000-9010", "both", "Example", true)
        try check(J.s(forward, "gateway_port") == "8000-8010" && J.s(forward, "client_port") == "9000-9010" && J.s(forward, "protocol") == "both", "API port-forward contract uses gateway_port/client_port/both")
        try check(rejects { _ = try FeatureContracts.forward("192.168.4.20", "8000-8010", "9000", "tcp", "Example", true) }, "mismatched forwarded ranges rejected")
        try check(rejects { _ = try FeatureContracts.portRange("70000") }, "out-of-range ports rejected")
        var dns = try FeatureContracts.dns(false, "", true); try check(J.s(dns, "dns", "mode") == "automatic", "API automatic DNS enum")
        dns = try FeatureContracts.dns(true, "1.1.1.1\n2606:4700:4700::1111", false); try check(J.list(J.get(dns, "dns", "custom", "ips")).count == 2, "mixed IPv4/IPv6 custom DNS payload")
        try check(rejects { _ = try FeatureContracts.dns(true, "not-an-ip", false) }, "invalid DNS server rejected")
        var dhcp = try FeatureContracts.dhcp("bridge", "", "", "", "", true); try check(J.s(dhcp, "connection", "mode") == "bridged", "API bridge mode serializes as bridged")
        dhcp = try FeatureContracts.dhcp("custom", "192.168.4.0", "255.255.255.0", "192.168.4.10", "192.168.4.200", false); try check(J.s(dhcp, "dhcp", "custom", "start_ip") == "192.168.4.10", "manual DHCP payload")
        try check(rejects { _ = try FeatureContracts.dhcp("custom", "192.168.4.0", "255.255.255.0", "192.168.5.10", "192.168.5.20", false) }, "DHCP addresses outside subnet rejected")
        try check(rejects { _ = try FeatureContracts.dhcp("custom", "192.168.4.0", "255.0.255.0", "192.168.4.10", "192.168.4.20", false) }, "noncontiguous subnet mask rejected")
        let schedule = try FeatureContracts.schedule("Bedtime", ["MON", "TUE"], "22:00", "07:00", true); try check(J.s(schedule, "start") == "22:00" && J.list(J.get(schedule, "days")).count == 2, "API scheduled-pause fields and overnight times")
        let power = try FeatureContracts.schedule("Night", ["MON"], "22:00", "07:00", true, true); try check(J.s(power, "start_time") == "22:00" && J.get(power, "start") == nil, "power schedules use distinct start_time/end_time fields")
        try check(rejects { _ = try FeatureContracts.schedule("Bedtime", [], "22:00", "07:00", true) }, "empty schedule days rejected")
        try check(rejects { _ = try FeatureContracts.schedule("Bedtime", ["MON"], "22:00", "22:00", true) }, "ambiguous zero-length schedule rejected")
        try check(try FeatureContracts.domain("Example.COM.") == "example.com", "website rule normalization")
        try check(rejects { _ = try FeatureContracts.domain("https://example.com/path") }, "website rules reject URLs and paths")
        try check(try FeatureContracts.domain("bücher.example") == "xn--bcher-kva.example", "internationalized domains use IDNA Punycode")
        let qr = FeatureContracts.wifiQr("Home;Wifi", "pass:word\\test"); try check(qr == "WIFI:T:WPA;S:Home\\;Wifi;P:pass\\:word\\\\test;;", "wifi QR payload escapes reserved characters")
        try check(Set(FeatureCatalog.entries.map { $0.path }).count == FeatureCatalog.entries.count, "feature paths are unique")
        try check(FeatureCatalog.parent("Settings/Advanced networking/DNS") == "Settings/Advanced networking", "settings breadcrumb")
        try check(FeatureCatalog.root("Home/Profiles/Profile") == "Home", "profile remains grouped under Home")
        try check(try EeroApi.resource("/2.4/networks/1/placement/wifi_coverage_scan").scheme == "https", "coverage scan API version allowed only on trusted host")
    }

    static func login() -> Step {
        Step("POST", "/2.2/login", body: "login=person%40example.com", contentType: "application/x-www-form-urlencoded", json: "{\"meta\":{\"code\":200},\"data\":{\"user_token\":\"pending-test\"}}")
    }
    static func runAsync(_ scratch: String) async throws {
        let store = SessionStore((scratch as NSString).appendingPathComponent("session.dat"))
        var fake = Fake()
        fake.enqueue(login())
        fake.enqueue(Step("POST", "/2.2/login/verify", cookie: "s=pending-test", body: "code=123456", contentType: "application/x-www-form-urlencoded", setCookie: "s=verified-test; Path=/; HttpOnly; Secure"))
        fake.enqueue(Step("GET", "/2.2/account", cookie: "s=verified-test", json: "{\"meta\":{\"code\":200},\"data\":{\"networks\":{\"data\":[{\"name\":\"Home\",\"url\":\"/2.2/networks/1\"}]}}}"))
        fake.enqueue(Step("GET", "/2.2/networks/1", cookie: "s=verified-test", json: "{\"meta\":{\"code\":401,\"error\":\"error.session.refresh\"}}", status: 401))
        fake.enqueue(Step("POST", "/2.2/login/refresh", cookie: "s=verified-test", json: "{\"meta\":{\"code\":200},\"data\":{\"user_token\":\"refreshed-test\"}}"))
        fake.enqueue(Step("GET", "/2.2/networks/1", cookie: "s=refreshed-test", json: "{\"meta\":{\"code\":200},\"data\":{\"name\":\"Home\"}}"))
        fake.enqueue(Step("PUT", "/2.2/networks/1/devices/aa", cookie: "s=refreshed-test", body: "{\"paused\":true}", contentType: "application/json"))
        fake.enqueue(Step("POST", "/2.2/networks/1/speedtest", cookie: "s=refreshed-test", json: "{\"meta\":{\"code\":202}}", status: 202))
        fake.enqueue(Step("PUT", "/2.2/eeros/1/led", cookie: "s=refreshed-test", body: "led_on=true", contentType: "application/x-www-form-urlencoded"))
        fake.enqueue(Step("POST", "/2.2/logout", cookie: "s=refreshed-test", json: "", status: 503))
        do {
            let api = EeroApi(store, fake)
            api.remember = true
            try await api.beginLogin("person@example.com"); try check(api.isPending && !api.signedIn, "pending login is not authenticated")
            try check(!Files.exists(store.filePath), "unverified token never saved")
            try await api.verify("123456"); try check(api.signedIn && !api.isPending, "verification promotes session")
            try check(store.load() == "verified-test", "Keychain-key AES-GCM session round trip")
            try check(!String(decoding: try Data(contentsOf: URL(fileURLWithPath: store.filePath)), as: UTF8.self).contains("verified-test"), "saved token not plaintext")
            let account = try await api.send("GET", "account"); try check(J.list(J.get(account, "networks")).count == 1, "network envelope parsing")
            let network = try await api.send("GET", "/2.2/networks/1"); try check(J.s(network, "name") == "Home", "expired session refresh and retry")
            try check(store.load() == "refreshed-test", "refreshed token persisted")
            try await api.send("PUT", "/2.2/networks/1/devices/aa", J.obj("paused", true))
            try check(try await api.send("POST", "/2.2/networks/1/speedtest") == nil, "accepted speed test without data")
            try await api.send("PUT", "/2.2/eeros/1/led", J.obj("led_on", true), true)
            await api.logout()
            try check(!api.signedIn && !Files.exists(store.filePath), "logout clears local credentials even on server failure")
            try check(fake.count == 0, "complete authentication and management sequence")
        }
        fake = Fake(); fake.enqueue(login())
        fake.enqueue(Step("POST", "/2.2/login/verify", cookie: "s=pending-test", body: "code=000000", json: "{\"meta\":{\"code\":401,\"error\":\"error.code.invalid\"}}", status: 401))
        fake.enqueue(Step("POST", "/2.2/login/resend", cookie: "s=pending-test"))
        fake.enqueue(Step("POST", "/2.2/login/verify", cookie: "s=pending-test", body: "code=123456"))
        fake.enqueue(Step("GET", "/2.2/account", cookie: "s=pending-test", json: "{\"meta\":{\"code\":429}}", status: 429))
        fake.enqueue(Step("GET", "/2.2/account", cookie: "s=pending-test", json: "{\"meta\":{\"code\":401,\"error\":\"error.session.expired\"}}", status: 401))
        do {
            let api = EeroApi(store, fake)
            try await expect(invalidOperation, "verification requires pending challenge") { try await api.verify("123456") }
            try await api.beginLogin("person@example.com")
            try await expect(Tests.api, "wrong code rejected") { try await api.verify("000000") }
            try check(api.isPending && !api.signedIn, "wrong code preserves challenge")
            try await api.resend(); try await api.verify("123456")
            try check(api.signedIn, "verification supports existing user cookie"); try check(!Files.exists(store.filePath), "remember off leaves no credentials on disk")
            try await expect(Tests.api, "rate limit surfaced without auto retry") { try await api.send("GET", "account") }
            do { try await api.send("GET", "account"); throw Failure(description: "Expected expiration") }
            catch let e as ApiException { try check(e.sessionExpired, "expired session flagged for sign in") }
            try check(fake.count == 0, "no unexpected retry or logout requests")
        }
        fake = Fake()
        fake.enqueue(Step("GET", "/2.2/consents/account", json: "{\"meta\":{\"code\":200},\"data\":[{\"name\":\"terms\",\"requires_consent\":true}]}", query: "?country=US"))
        fake.enqueue(Step("POST", "/2.2/register", body: "name=Test+Person&email=person%40example.com&phone=%2B12025550123&send_verification=true", contentType: "application/x-www-form-urlencoded", json: "{\"meta\":{\"code\":200},\"data\":{\"user_token\":\"register-pending\"}}"))
        fake.enqueue(Step("POST", "/2.2/register/phone/resend", cookie: "s=register-pending"))
        fake.enqueue(Step("POST", "/2.2/register/verify", cookie: "s=register-pending", body: "{\"code\":\"123456\",\"country\":\"US\",\"consents\":{\"terms\":true}}", contentType: "application/json", setCookie: "s=registered-test; Path=/; Secure; HttpOnly"))
        do {
            let api = EeroApi(store, fake)
            let agreements = try await api.registrationConsents("US"); try check(J.list(agreements).count == 1, "registration loads country-specific consent metadata without credentials")
            try await api.beginRegistration("Test Person", "person@example.com", "+12025550123"); try check(api.isRegistering && api.isPending && !api.signedIn, "registration is separate from authenticated login")
            try await expect(invalidOperation, "registration code cannot be sent to normal login verification") { try await api.verify("123456") }
            try await api.resend(); try await api.verifyRegistration("123456", "US", J.obj("terms", true))
            try check(api.signedIn && !api.isRegistering && !api.isPending && fake.count == 0, "registration completes only after code and explicit consents")
        }
        try check(WebAddress(url: try EeroApi.resource("account"))?.absolutePath == "/2.2/account", "relative resource resolution")
        try check(try EeroApi.resource("/2.3/networks/1").host == "api-user.e2ro.com", "new API version resource resolution")
        for url in ["https://evil.example/2.2/account", "http://api-user.e2ro.com/2.2/account", "//evil.example/2.2/account", "https://api-user.e2ro.com:444/2.2/account", "https://user@api-user.e2ro.com/2.2/account", "/2.2/../../outside", "https://api-user.e2ro.com/2.2/account#secret"] {
            try check(rejects { _ = try EeroApi.resource(url) }, "reject untrusted URL " + url)
        }
        try MainWindow.validateSsid("Home"); try MainWindow.validatePassword("password123")
        try check(PhoneInput.formatForDisplay("2025550123") == "+1 (202) 555-0123", "10-digit phone auto formatting")
        try check(PhoneInput.formatForDisplay("12025550123") == "+1 (202) 555-0123", "11-digit phone auto formatting")
        try check(PhoneInput.formatForDisplay("+12025550123") == "+1 (202) 555-0123", "international +1 formatting")
        try check(try PhoneInput.normalize("+1 (202) 555-0123") == "+12025550123", "formatted phone normalizes for API")
        try check(PhoneInput.formatForDisplay("20255") == "20255", "partial phone remains editable")
        try check(PhoneInput.formatForDisplay("+1 (202) 555-012") == "+1 (202) 555-012", "backspace does not restore deleted digits")
        try check(try PhoneInput.normalize("+44 (20) 7946 0958") == "+442079460958", "international country code preserved")
        try check(PhoneInput.formatForDisplay("person@example.com") == "person@example.com", "email not modified by phone formatter")
        let blocked = URLError(.cannotConnectToHost, userInfo: [NSUnderlyingErrorKey: NSError(domain: NSPOSIXErrorDomain, code: Int(EPERM))])
        try check(ConnectionDiagnostics.category(blocked) == "network_access_blocked" && ConnectionDiagnostics.nativeCode(blocked) == Int(EPERM), "network access denied by a firewall or filter identified (EPERM)")
        try check(MainWindow.message(blocked).contains("firewall"), "blocked network message explains firewall and filter rules")
        try check(ConnectionDiagnostics.category(URLError(.cannotFindHost)) == "dns_failure", "DNS error distinguished from general connectivity")
        try check(ConnectionDiagnostics.category(URLError(.serverCertificateUntrusted)) == "tls_failure", "TLS error distinguished from general connectivity")
        try check(ConnectionDiagnostics.category(CancellationError()) == "timeout" && ConnectionDiagnostics.category(URLError(.timedOut)) == "timeout", "timeout distinguished from general connectivity")
        try check(MainWindow.message(URLError(.cannotFindHost)).hasPrefix("macOS could not resolve"), "DNS failure message names macOS")
        try check(PhoneInput.preserveCaret("2025550123", 10, "+1 (202) 555-0123") == 17, "caret remains at end after phone formatting")
        try check(PhoneInput.preserveCaret("2025550123", 3, "+1 (202) 555-0123") == 9, "caret keeps following digits when editing middle")
        try check(rejects { _ = try PhoneInput.normalize("+1 20255") }, "incomplete phone rejected before login")
        try check(rejects { _ = try PhoneInput.normalize("2025550123123456789") }, "overlong phone is never silently truncated")
        try check(rejects { try MainWindow.validateSsid(String(repeating: "界", count: 12)) }, "SSID byte limit enforced")
        try check(rejects { try MainWindow.validatePassword("short") }, "wifi password length enforced")
        try store.save("restored-test")
        do { let api = EeroApi(store, Fake()); try check(api.restore() && api.remember, "session restored between launches"); api.forget() }
        try Data("corrupt-session".utf8).write(to: URL(fileURLWithPath: store.filePath)); try check(store.load() == nil, "corrupt credentials handled safely")
        results.append("All checks completed using an injected HTTP transport. No live eero requests or real account credentials were used.")
    }
}
