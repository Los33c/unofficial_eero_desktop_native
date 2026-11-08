import Foundation

/// Swift stand-ins for the .NET exception types the Windows code throws and catches.
struct AppError: Error, LocalizedError, CustomStringConvertible {
    enum Kind { case argument, invalidOperation, invalidData, crypto, unauthorized, notFound, io, cancelled, notImplemented }
    let kind: Kind
    let message: String
    var errorDescription: String? { message }
    var description: String { message }
    static func argument(_ m: String) -> AppError { AppError(kind: .argument, message: m) }
    static func invalidOperation(_ m: String) -> AppError { AppError(kind: .invalidOperation, message: m) }
    static func invalidData(_ m: String) -> AppError { AppError(kind: .invalidData, message: m) }
    static func crypto(_ m: String) -> AppError { AppError(kind: .crypto, message: m) }
    static func unauthorized(_ m: String) -> AppError { AppError(kind: .unauthorized, message: m) }
    static func notFound(_ m: String) -> AppError { AppError(kind: .notFound, message: m) }
    static func io(_ m: String) -> AppError { AppError(kind: .io, message: m) }
}

final class ApiException: Error, LocalizedError, CustomStringConvertible {
    let code: Int
    let apiError: String
    let sessionExpired: Bool
    let message: String
    init(_ code: Int, _ error: String?, _ session: Bool) {
        self.code = code; self.apiError = error ?? ""
        self.sessionExpired = session && (code == 401 || code == 419)
        self.message = ApiException.friendly(code, error)
    }
    var errorDescription: String? { message }
    var description: String { message }
    private static func friendly(_ code: Int, _ error: String?) -> String {
        let detail = Regex.isMatch(error ?? "", "^[a-zA-Z0-9_.-]{1,140}$") ? " (" + (error ?? "") + ")" : ""
        if code == 401 || code == 419 { return "Your code or session was not accepted. Try again or request a new code." + detail }
        if code == 429 { return "eero is limiting requests. Wait a few minutes before trying again." }
        if code == 402 { return "This feature requires an active eero subscription." }
        if code == 403 { return "Your account does not have permission for this action." + detail }
        if code == 404 || code == 405 { return "This feature is not available for this network or API version." }
        if code >= 500 { return "eero's service could not complete the request. Try again later." }
        return "eero rejected the request (\(code))." + detail
    }
}

/// Small regular-expression helpers mirroring System.Text.RegularExpressions.Regex.
enum Regex {
    private static var cache: [String: NSRegularExpression] = [:]
    private static let lock = NSLock()
    static func make(_ pattern: String, _ options: NSRegularExpression.Options = []) -> NSRegularExpression {
        let key = pattern + "|" + String(options.rawValue)
        lock.lock(); defer { lock.unlock() }
        if let r = cache[key] { return r }
        let r = try! NSRegularExpression(pattern: pattern, options: options)
        cache[key] = r; return r
    }
    static func isMatch(_ input: String, _ pattern: String, _ options: NSRegularExpression.Options = []) -> Bool {
        make(pattern, options).firstMatch(in: input, range: NSRange(input.startIndex..., in: input)) != nil
    }
    static func replace(_ input: String, _ pattern: String, _ template: String, _ options: NSRegularExpression.Options = []) -> String {
        make(pattern, options).stringByReplacingMatches(in: input, range: NSRange(input.startIndex..., in: input), withTemplate: template.replacingOccurrences(of: "$", with: "$"))
    }
    /// Returns capture groups of the first match (index 0 = whole match), or nil.
    static func match(_ input: String, _ pattern: String, _ options: NSRegularExpression.Options = []) -> [String]? {
        guard let m = make(pattern, options).firstMatch(in: input, range: NSRange(input.startIndex..., in: input)) else { return nil }
        return (0..<m.numberOfRanges).map { i in
            let r = m.range(at: i)
            guard r.location != NSNotFound, let range = Range(r, in: input) else { return "" }
            return String(input[range])
        }
    }
    static func matches(_ input: String, _ pattern: String, _ options: NSRegularExpression.Options = []) -> [[String]] {
        make(pattern, options).matches(in: input, range: NSRange(input.startIndex..., in: input)).map { m in
            (0..<m.numberOfRanges).map { i in
                let r = m.range(at: i)
                guard r.location != NSNotFound, let range = Range(r, in: input) else { return "" }
                return String(input[range])
            }
        }
    }
}

/// Session credential file, sealed with the per-user data key (see ProtectedData.swift).
final class SessionStore {
    let filePath: String
    private static let entropy = Data("EeroDesktop.Session.v1".utf8)
    init(_ path: String) { filePath = path }
    func save(_ token: String) throws {
        let encrypted = try ProtectedData.protect(Data(token.utf8), SessionStore.entropy)
        try FileManager.default.createDirectory(atPath: (filePath as NSString).deletingLastPathComponent, withIntermediateDirectories: true)
        try Files.writeAtomically(encrypted, filePath)
    }
    func load() -> String? {
        guard FileManager.default.fileExists(atPath: filePath) else { return nil }
        do {
            let data = try Data(contentsOf: URL(fileURLWithPath: filePath))
            let clear = try ProtectedData.unprotect(data, SessionStore.entropy)
            return String(data: clear, encoding: .utf8)
        } catch let e as AppError where e.kind == .crypto { clear(); return nil }
        catch { return nil }
    }
    func clear() { try? FileManager.default.removeItem(atPath: filePath) }
}

enum Files {
    /// Writes through a temporary file and replaces atomically; files are private to the user (0600).
    static func writeAtomically(_ data: Data, _ path: String) throws {
        let tmp = path + ".tmp"
        FileManager.default.createFile(atPath: tmp, contents: data, attributes: [.posixPermissions: 0o600])
        guard FileManager.default.fileExists(atPath: tmp) else { throw AppError.io("Could not write \(path).") }
        if FileManager.default.fileExists(atPath: path) {
            _ = try FileManager.default.replaceItemAt(URL(fileURLWithPath: path), withItemAt: URL(fileURLWithPath: tmp))
        } else { try FileManager.default.moveItem(atPath: tmp, toPath: path) }
        try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: path)
    }
    static func writeText(_ text: String, _ path: String) throws { try writeAtomically(Data(text.utf8), path) }
    static func readText(_ path: String) throws -> String { try String(contentsOfFile: path, encoding: .utf8) }
    static func exists(_ path: String) -> Bool { FileManager.default.fileExists(atPath: path) }
    static func delete(_ path: String) { try? FileManager.default.removeItem(atPath: path) }
}

final class VerifiedBrowserIdentity {
    let account: Any?
    let sessionCredential: String
    init(_ account: Any?, _ credential: String) { self.account = account; sessionCredential = credential }
}

/// HTTP transport; tests replace it with a scripted fake (the Windows HttpMessageHandler).
protocol HttpTransport: AnyObject {
    func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse)
}

/// URLSession transport: no cookie storage, no redirects, 30 second timeout (HttpClient.Timeout; others pass their own).
final class UrlSessionTransport: NSObject, HttpTransport, URLSessionTaskDelegate {
    let timeout: TimeInterval
    init(_ timeout: TimeInterval = 30) { self.timeout = timeout; super.init() }
    private lazy var session: URLSession = {
        let c = URLSessionConfiguration.ephemeral
        c.httpCookieStorage = nil; c.httpShouldSetCookies = false; c.httpCookieAcceptPolicy = .never
        c.urlCache = nil; c.requestCachePolicy = .reloadIgnoringLocalCacheData
        c.timeoutIntervalForRequest = timeout; c.timeoutIntervalForResource = timeout
        c.tlsMinimumSupportedProtocolVersion = .TLSv12
        return URLSession(configuration: c, delegate: self, delegateQueue: nil)
    }()
    func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        let (data, response) = try await session.data(for: request)
        guard let http = response as? HTTPURLResponse else { throw AppError.invalidData("eero returned an unexpected response. Please try again later.") }
        return (data, http)
    }
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest) async -> URLRequest? { nil }
    func invalidate() { session.invalidateAndCancel() }
}

/// An async mutex (SemaphoreSlim(1,1)).
actor AsyncGate {
    private var locked = false
    private var waiters: [CheckedContinuation<Void, Never>] = []
    func wait() async {
        if !locked { locked = true; return }
        await withCheckedContinuation { waiters.append($0) }
    }
    func release() {
        if waiters.isEmpty { locked = false } else { waiters.removeFirst().resume() }
    }
}

/// eero cloud API client. Members are internal (not private) so feature files can extend it, like the C# partial class.
final class EeroApi {
    static let origin = "https://api-user.e2ro.com"
    static let userAgent = "EeroDesktop/1.1.0 (macOS; unofficial)"
    let http: HttpTransport
    let gate = AsyncGate()
    let store: SessionStore
    var pending: String?
    var registering = false
    var token: String?
    var remember = false
    var signedIn: Bool { !(token ?? "").isEmpty }
    var isPending: Bool { !(pending ?? "").isEmpty }
    var isRegistering: Bool { registering }

    init(_ session: SessionStore, _ transport: HttpTransport? = nil) {
        store = session
        http = transport ?? UrlSessionTransport()
    }
    func restore() -> Bool {
        let saved = store.load()
        do { token = saved == nil ? nil : try EeroApi.validToken(saved) }
        catch { store.clear(); token = nil }
        remember = signedIn
        return signedIn
    }
    func checkConnection() async throws {
        do { _ = try await raw("GET", "account", nil, nil, false, false) }
        catch let e as ApiException { if e.code == 401 { return }; throw e }
        throw AppError.invalidData("eero returned an unexpected response to the connection check.")
    }
    static func resource(_ path: String?) throws -> URL {
        guard let path = path, !path.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { throw AppError.argument("The server did not provide a resource URL.") }
        var text: String
        if path.hasPrefix("/") { text = origin + path }
        else if path.lowercased().hasPrefix("https://") { text = path }
        else if path.lowercased().hasPrefix("http:") || path.contains("://") { throw AppError.argument("Untrusted API resource URL.") }
        else { text = origin + "/2.2/" + path.drop(while: { $0 == "/" }) }
        guard let comps = URLComponents(string: text), let url = comps.url,
              comps.scheme == "https", comps.host == "api-user.e2ro.com", comps.port == nil || comps.port == 443,
              comps.user == nil, comps.password == nil, comps.fragment == nil,
              comps.percentEncodedPath.hasPrefix("/2.2/") || comps.percentEncodedPath.hasPrefix("/2.3/") || comps.percentEncodedPath.hasPrefix("/2.4/"),
              !comps.percentEncodedPath.contains("/../"), !comps.percentEncodedPath.hasSuffix("/..")
        else { throw AppError.argument("Untrusted API resource URL.") }
        return url
    }
    static func validToken(_ t: String?) throws -> String {
        if let t = t, t.hasPrefix("eero-web-") { throw AppError.invalidData("This retired preview session requires a fresh email or phone sign-in.") }
        guard let t = t, !t.isEmpty, t.utf16.count <= 8192, !t.unicodeScalars.contains(where: { $0.value <= 32 || $0.value >= 127 || $0 == ";" || $0 == "," })
        else { throw AppError.invalidData("eero returned an invalid session credential.") }
        return t
    }
    func commit(_ t: String) throws {
        token = try EeroApi.validToken(t)
        if remember { try store.save(token!) } else { store.clear() }
    }
    struct Reply { var data: Any?; var cookie: String? }
    /// Retrofit @Field lists (for example colors[]) repeat the same key once per value.
    static func formPairs(_ body: JObject) -> [(String, String)] {
        var pairs: [(String, String)] = []
        for (k, v) in body {
            if let many = v as? [Any] { for item in many { pairs.append((k, formValue(item))) } }
            else { pairs.append((k, formValue(v))) }
        }
        return pairs
    }
    static func formValue(_ v: Any?) -> String { if let b = v as? Bool { return b ? "true" : "false" }; return J.str(v) }
    /// application/x-www-form-urlencoded exactly like .NET FormUrlEncodedContent (spaces as '+').
    static func formEncode(_ pairs: [(String, String)]) -> String {
        pairs.map { escapeForm($0.0) + "=" + escapeForm($0.1) }.joined(separator: "&")
    }
    static func escapeForm(_ s: String) -> String {
        // Uri.EscapeDataString keeps A-Z a-z 0-9 - _ . ~; FormUrlEncodedContent then turns %20 into '+'.
        var allowed = CharacterSet(charactersIn: "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~")
        allowed.insert(charactersIn: "")
        return (s.addingPercentEncoding(withAllowedCharacters: allowed) ?? "").replacingOccurrences(of: "%20", with: "+")
    }
    func raw(_ method: String, _ path: String, _ body: Any?, _ credential: String?, _ form: Bool, _ session: Bool) async throws -> Reply {
        var req = URLRequest(url: try EeroApi.resource(path))
        req.httpMethod = method
        req.setValue(EeroApi.userAgent, forHTTPHeaderField: "User-Agent")
        req.setValue("application/json", forHTTPHeaderField: "Accept")
        req.timeoutInterval = 30
        if let credential = credential, !credential.isEmpty { req.setValue("s=" + (try EeroApi.validToken(credential)), forHTTPHeaderField: "Cookie") }
        if let body = body {
            if let multipart = body as? MultipartFields {
                let (data, contentType) = multipart.content()
                req.httpBody = data; req.setValue(contentType, forHTTPHeaderField: "Content-Type")
            } else if form, let map = body as? JObject {
                req.httpBody = Data(EeroApi.formEncode(EeroApi.formPairs(map)).utf8)
                req.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
            } else {
                req.httpBody = Data(J.stringify(body).utf8)
                req.setValue("application/json; charset=utf-8", forHTTPHeaderField: "Content-Type")
            }
        }
        let (data, response) = try await http.send(req)
        let text = String(data: data, encoding: .utf8) ?? ""
        var envelope: Any? = nil
        let success = (200...299).contains(response.statusCode)
        if !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            do { envelope = try J.parse(text) }
            catch { if success { throw AppError.invalidData("eero returned an unexpected response. Please try again later.") } }
        }
        let httpCode = response.statusCode
        var code = httpCode
        if let parsed = Int(J.s(envelope, "meta", "code")) { code = parsed }
        if !success || code < 200 || code >= 300 { throw ApiException(success ? code : httpCode, J.s(envelope, "meta", "error"), session) }
        var cookie: String? = nil
        for header in EeroApi.setCookieHeaders(response) {
            for m in Regex.matches(header, "(?:^|,\\s*)s=([^; ,]+)") { cookie = try EeroApi.validToken(m[1]) }
        }
        return Reply(data: J.get(envelope, "data"), cookie: cookie)
    }
    static func setCookieHeaders(_ response: HTTPURLResponse) -> [String] {
        response.allHeaderFields.compactMap { (k, v) -> String? in
            guard let key = k as? String, key.caseInsensitiveCompare("Set-Cookie") == .orderedSame else { return nil }
            return v as? String
        }
    }
    func locked<T>(_ body: () async throws -> T) async rethrows -> T {
        await gate.wait()
        do { let r = try await body(); await gate.release(); return r }
        catch { await gate.release(); throw error }
    }
    func beginLogin(_ identifier: String) async throws {
        try await locked {
            pending = nil; registering = false; token = nil; store.clear()
            let reply = try await raw("POST", "login", J.obj("login", identifier), nil, true, false)
            pending = try EeroApi.validToken(J.s(reply.data, "user_token"))
        }
    }
    func verify(_ code: String) async throws {
        try await locked {
            if !isPending { throw AppError.invalidOperation("Request a verification code first.") }
            if registering { throw AppError.invalidOperation("Use account registration verification for this code.") }
            let reply = try await raw("POST", "login/verify", J.obj("code", code), pending, true, false)
            try commit(reply.cookie ?? J.first(reply.data, "user_token", "session_token").nullIfEmpty ?? pending!)
            pending = nil
        }
    }
    func cancelLogin() { pending = nil; registering = false }
    func importBrowserSession(_ credential: String) async throws {
        try await locked {
            let candidate = try EeroApi.validToken(credential)
            let verified = try await raw("GET", "account", nil, candidate, false, true)
            try Task.checkCancellation()
            try commit(verified.cookie ?? candidate); pending = nil; registering = false
        }
    }
    func inspectBrowserSession(_ credential: String) async throws -> VerifiedBrowserIdentity {
        try await locked {
            let candidate = try EeroApi.validToken(credential)
            let reply = try await raw("GET", "account", nil, candidate, false, true)
            return VerifiedBrowserIdentity(reply.data, reply.cookie ?? candidate)
        }
    }
    func amazonSignIn(_ accessToken: String) async throws -> Any? {
        try await locked {
            let login = try await raw("POST", "login/amazon", J.obj("auth_token", try EeroApi.validToken(accessToken), "create_account", false), nil, true, false)
            let candidate = try login.cookie ?? EeroApi.validToken(J.s(login.data, "user_token"))
            let verified = try await raw("GET", "account", nil, candidate, false, true)
            if verified.data == nil || (J.first(verified.data, "user_id", "id", "url").isEmpty && J.get(verified.data, "email") == nil) { throw AppError.invalidData("eero did not return a valid account identity.") }
            try Task.checkCancellation()
            try commit(verified.cookie ?? candidate); pending = nil; registering = false
            return verified.data
        }
    }
    func registrationConsents(_ country: String) async throws -> Any? {
        try await locked {
            try await raw("GET", "consents/account?country=" + (country.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? ""), nil, nil, false, false).data
        }
    }
    func beginRegistration(_ name: String, _ email: String, _ phone: String) async throws {
        try await locked {
            pending = nil; token = nil; registering = false; store.clear()
            let r = try await raw("POST", "register", J.obj("name", name, "email", email, "phone", phone, "send_verification", true), nil, true, false)
            pending = try EeroApi.validToken(J.s(r.data, "user_token")); registering = true
        }
    }
    func verifyRegistration(_ code: String, _ country: String, _ consents: JObject) async throws {
        try await locked {
            if !isPending || !registering { throw AppError.invalidOperation("Start account creation first.") }
            let r = try await raw("POST", "register/verify", J.obj("code", code, "country", country, "consents", consents), pending, false, false)
            try commit(r.cookie ?? J.first(r.data, "user_token", "session_token").nullIfEmpty ?? pending!); pending = nil; registering = false
        }
    }
    func resend() async throws {
        try await locked {
            if !isPending { throw AppError.invalidOperation("Request a verification code first.") }
            let r = try await raw("POST", registering ? "register/phone/resend" : "login/resend", nil, pending, false, false)
            if let updated = r.cookie ?? J.s(r.data, "user_token").nullIfEmpty { pending = try EeroApi.validToken(updated) }
        }
    }
    @discardableResult
    func send(_ method: String, _ path: String, _ body: Any? = nil, _ form: Bool = false) async throws -> Any? {
        try await locked {
            if !signedIn { throw ApiException(401, "session.missing", true) }
            var r: Reply
            do { r = try await raw(method, path, body, token, form, true) }
            catch let ex as ApiException {
                if ex.code != 401 || ex.apiError != "error.session.refresh" { throw ex }
                let fresh = try await raw("POST", "login/refresh", nil, token, false, true)
                try commit(fresh.cookie ?? J.s(fresh.data, "user_token").nullIfEmpty ?? token!)
                r = try await raw(method, path, body, token, form, true)
            }
            if let c = r.cookie { try commit(c) }
            return r.data
        }
    }
    func logout() async {
        if signedIn { _ = try? await send("POST", "logout") }
        forget()
    }
    func forget() { token = nil; pending = nil; registering = false; remember = false; store.clear() }
}
