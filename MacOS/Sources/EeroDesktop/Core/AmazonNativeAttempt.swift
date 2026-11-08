import CryptoKit
import Foundation
import Security

/// One consumer Amazon sign-in (authorization code + PKCE, as the eero Android app does).
/// Protocol constants, not SDK code. See the local APK verification notes.
final class AmazonNativeAttempt {
    static let tokenEndpoint = "https://api.amazon.com/auth/token"
    private static let deviceType = "A2W01JEFAJFTXI"
    private let http: HttpTransport
    private let verifier: String, state: String, client: String
    private let lock = NSLock()
    private var consumed = 0

    init(_ handler: HttpTransport? = nil) {
        verifier = AmazonNativeAttempt.random(); state = AmazonNativeAttempt.random()
        let serial = UUID().uuidString.replacingOccurrences(of: "-", with: "").uppercased()
        client = Data((serial + "#" + AmazonNativeAttempt.deviceType).utf8).map { String(format: "%02x", $0) }.joined()
        http = handler ?? UrlSessionTransport()
    }
    func dispose() {}
    private func consume() -> Int { lock.lock(); defer { lock.unlock() }; let used = consumed; consumed = 1; return used }
    private static func base64Url(_ bytes: Data) -> String {
        bytes.base64EncodedString().trimEnd("=").replacingOccurrences(of: "+", with: "-").replacingOccurrences(of: "/", with: "_")
    }
    private static func random() -> String {
        var bytes = [UInt8](repeating: 0, count: 32)
        if SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes) != errSecSuccess { bytes = (0..<32).map { _ in UInt8.random(in: 0...255) } }
        return base64Url(Data(bytes))
    }
    static func challenge(_ value: String) -> String { base64Url(Data(SHA256.hash(data: Data(value.utf8)))) }
    var signInUrl: String {
        let fields: [(String, String)] = [
            ("openid.assoc_handle", "amzn_eero_mobile_android_us"), ("pageId", "eero_mobile_accounts"), ("language", "en_US"),
            ("openid.ns", "http://specs.openid.net/auth/2.0"), ("openid.mode", "checkid_setup"),
            ("openid.claimed_id", "http://specs.openid.net/auth/2.0/identifier_select"), ("openid.identity", "http://specs.openid.net/auth/2.0/identifier_select"),
            ("openid.ns.pape", "http://specs.openid.net/extensions/pape/1.0"), ("openid.pape.max_auth_age", "0"),
            ("openid.ns.oa2", "http://www.amazon.com/ap/ext/oauth/2"), ("openid.oa2.response_type", "code"),
            ("openid.oa2.scope", "device_auth_access"), ("openid.oa2.client_id", "device:" + client),
            ("openid.oa2.code_challenge", AmazonNativeAttempt.challenge(verifier)), ("openid.oa2.code_challenge_method", "S256"),
            ("openid.return_to", "https://www.amazon.com/ap/maplanding?state=" + state), ("accountStatusPolicy", "P1")
        ]
        return "https://www.amazon.com/ap/signin?" + fields.map { $0.0.escapedData + "=" + $0.1.escapedData }.joined(separator: "&")
    }
    static func returnAddress(_ uri: URL?) -> Bool {
        guard let uri = uri, let u = WebAddress.of(uri) else { return false }
        return u.scheme == "https" && u.host == "www.amazon.com" && u.port == 443 && u.userInfo.isEmpty && u.absolutePath == "/ap/maplanding" && u.fragment.isEmpty
    }
    static func navigation(_ value: String?) -> Bool {
        guard let uri = WebAddress(value), uri.scheme == "https", uri.port == 443, uri.userInfo.isEmpty else { return false }
        return ["amazon.com", "amazon.co.uk", "amazon.ca", "amazon.de", "amazon.fr", "amazon.it", "amazon.es", "amazon.co.jp", "amazon.com.au", "amazon.in", "amazon.com.mx", "amazon.com.br"]
            .contains { host in uri.host == host || uri.host.hasSuffix("." + host) }
    }
    func exchangeBody(_ returned: URL) throws -> JObject {
        guard AmazonNativeAttempt.returnAddress(returned), let parts = WebAddress.of(returned) else { throw AppError.invalidData("Amazon returned to an unexpected address.") }
        let query = try SignInContracts.parameters(parts.query)
        guard let received = query["state"], received == state else { throw AppError.invalidData("The Amazon response did not match this sign-in attempt. Close this window and try again.") }
        if query["error"] != nil || query["openid.error"] != nil { throw AppError.invalidOperation("Amazon sign-in was cancelled or rejected.") }
        guard let code = query["openid.oa2.authorization_code"], !code.isEmpty, code.utf16.count <= 8192, !code.unicodeScalars.contains(where: { $0.value <= 32 || $0.value >= 127 })
        else { throw AppError.invalidData("Amazon did not return a valid authorization code.") }
        if let scope = query["openid.oa2.scope"], scope != "device_auth_access" { throw AppError.invalidData("Amazon returned an unexpected sign-in scope.") }
        return J.obj("app_name", "EeroDesktop", "app_version", "1.1.0", "source_token_type", "authorization_code", "source_token", code,
                     "requested_token_type", "access_token", "code_verifier", verifier, "code_algorithm", "SHA-256", "client_id", client, "client_domain", "DeviceLegacy")
    }
    /// Cancel the calling Task to abandon the exchange (C# CancellationToken).
    func exchange(_ returned: URL) async throws -> String {
        let body = try exchangeBody(returned)
        let used = consume()
        if used != 0 { throw AppError.invalidOperation("This sign-in attempt has already been used. Close this window and start again.") }
        var request = URLRequest(url: URL(string: AmazonNativeAttempt.tokenEndpoint)!)
        request.httpMethod = "POST"; request.timeoutInterval = 30
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.setValue(EeroApi.userAgent, forHTTPHeaderField: "User-Agent")
        request.setValue("api.amazon.com", forHTTPHeaderField: "x-amzn-identity-auth-domain")
        request.httpBody = Data(J.stringify(body).utf8)
        request.setValue("application/json; charset=utf-8", forHTTPHeaderField: "Content-Type")
        let (data, response) = try await http.send(request)
        if !(200...299).contains(response.statusCode) {
            var category = ""
            if data.count < 65536, let parsed = try? J.parse(String(decoding: data, as: UTF8.self)) {
                let name = J.s(parsed, "error").lowercased()
                if ["invalid_request", "invalid_grant", "invalid_client", "invalid_token", "unauthorized_client", "access_denied", "invalid_scope"].contains(name) { category = "." + name }
            }
            throw ApiException(response.statusCode, "amazon.token.exchange" + category, false)
        }
        if data.count > 65536 { throw AppError.invalidData("Amazon returned an oversized sign-in response.") }
        let result: Any?
        do { result = try J.parse(String(decoding: data, as: UTF8.self)) } catch { throw AppError.invalidData("Amazon returned an unexpected sign-in response.") }
        let token = J.s(result, "access_token")
        if J.s(result, "token_type").caseInsensitiveCompare("bearer") != .orderedSame || token.isEmpty || token.utf16.count > 8192
            || token.unicodeScalars.contains(where: { $0.value <= 32 || $0.value >= 127 || $0 == ";" || $0 == "," }) { throw AppError.invalidData("Amazon did not return a valid access token.") }
        try Task.checkCancellation()
        return token
    }
}
