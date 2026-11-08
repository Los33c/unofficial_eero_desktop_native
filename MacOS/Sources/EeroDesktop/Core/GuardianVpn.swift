import CryptoKit
import Foundation

enum VpnContracts {
    static let partnerHost = "wifi-connect-api.guardianapp.com"
    static let serverHost = "connect-api.guardianapp.com"
    /// Publishable routing identifier used by eero's Guardian integration; not an account credential.
    static let publishableKey = "pk_2B2CE7masv5UM6JYgPTVUJ3JF"

    static func eligibilityMessage(_ availability: Any?) -> String {
        if let error = availability as? ApiException {
            if error.code == 402 { return "Guardian VPN requires an eligible active benefit. Some internet providers do not include Guardian with their service." }
            if error.code == 403 { return "Guardian VPN is not enabled for this network, or your account does not have permission to use it. Your internet provider may not offer this benefit." }
            if error.code == 404 || error.code == 405 { return "Guardian VPN availability is not provided for this network or service version." }
            return error.message
        }
        guard let seats = J.get(availability, "seats_available") as? Bool else { return "Guardian VPN eligibility has not been confirmed. Check eligibility before setting up WireGuard or registering a device." }
        return seats ? "Guardian reports a VPN device slot is available for this network." : "No Guardian VPN device slots are currently available. If your service includes Guardian, manage registered devices or check benefit activation."
    }
    static func canRegister(_ availability: Any?) -> Bool {
        !(availability is ApiException) && J.get(availability, "seats_available") is Bool && J.b(availability, "seats_available")
    }
    static func scope(_ account: String?, _ network: String?) throws -> String {
        guard let account = account, !account.isEmpty, let network = network, !network.isEmpty else { throw AppError.argument("Sign in and select a network first.") }
        let digest = SHA256.hash(data: Data((account + "\n" + network).utf8))
        return String(digest.map { String(format: "%02x", $0) }.joined().prefix(24))
    }
    static func gateway(_ host: String?) throws -> String {
        guard let host = host, Regex.isMatch(host, "^[a-z0-9][a-z0-9-]{0,62}\\.sgw\\.guardianapp\\.com\\z", .caseInsensitive) else { throw AppError.invalidData("Guardian returned an unsupported VPN server.") }
        return host.lowercased()
    }
    /// Convert.FromBase64String ignores white space; anything else that is not Base64 is rejected.
    static func key(_ value: String?) throws -> String {
        let compact = (value ?? "").filter { !" \t\r\n".contains($0) }
        guard let bytes = Data(base64Encoded: compact) else { throw AppError.invalidData("A VPN key is invalid.") }
        if bytes.count != 32 || bytes.allSatisfy({ $0 == 0 }) { throw AppError.invalidData("A VPN key is invalid.") }
        return bytes.base64EncodedString()
    }
    private static func address(_ value: String?, _ family: AddressFamily) throws -> String {
        let parts = (value ?? "").split("/")
        guard parts.count <= 2, let address = IPAddress.tryParse(parts[0]), address.addressFamily == family, !IPAddress.isLoopback(address), address != IPAddress.any, address != IPAddress.ipv6Any
        else { throw AppError.invalidData("Guardian returned an invalid tunnel address.") }
        let prefix = family == .interNetwork ? 32 : 128
        if parts.count == 2 && parts[1] != String(prefix) { throw AppError.invalidData("Guardian returned an unsupported tunnel prefix.") }
        return address.toString() + "/" + String(prefix)
    }
    static func profile(_ privateKey: String?, _ gateway: String?, _ response: Any?) throws -> JObject {
        let ipv6 = J.s(response, "mapped-ipv6-address")
        return J.obj("private_key", try key(privateKey), "server_key", try key(J.s(response, "server-public-key")), "ipv4", try address(J.s(response, "mapped-ipv4-address"), .interNetwork),
                     "ipv6", ipv6.isEmpty ? "" : (try address(ipv6, .interNetworkV6)), "gateway", try VpnContracts.gateway(gateway))
    }
    static func configuration(_ profile: Any?) throws -> String {
        let ipv4 = try address(J.s(profile, "ipv4"), .interNetwork)
        var ipv6 = J.s(profile, "ipv6")
        if !ipv6.isEmpty { ipv6 = ", " + (try address(ipv6, .interNetworkV6)) }
        return "[Interface]\nPrivateKey = " + (try key(J.s(profile, "private_key"))) + "\nAddress = " + ipv4 + ipv6 + "\nDNS = 1.1.1.1, 1.0.0.1\n\n[Peer]\nPublicKey = " + (try key(J.s(profile, "server_key")))
            + "\nAllowedIPs = 0.0.0.0/0, ::/0\nEndpoint = " + (try gateway(J.s(profile, "gateway"))) + ":51821\nPersistentKeepalive = 25\n"
    }
    static func subscriber(_ credentials: Any?) throws -> JObject {
        let id = J.s(credentials, "guardian_id"), secret = J.s(credentials, "guardian_secret")
        if id.isEmpty || secret.isEmpty { throw AppError.invalidData("eero did not return Guardian account credentials.") }
        return J.obj("ep-grd-subscriber-identifier", id, "ep-grd-subscriber-secret", secret)
    }
}

/// Guardian Connect API client. Uses its own transport, so the eero session cookie is never sent to Guardian.
final class GuardianApi {
    let http: HttpTransport
    init(_ handler: HttpTransport? = nil) { http = handler ?? UrlSessionTransport(35) }
    func dispose() {}
    func send(_ method: String, _ host: String, _ path: String, _ body: Any? = nil) async throws -> Any? {
        if host != VpnContracts.partnerHost && host != VpnContracts.serverHost { _ = try VpnContracts.gateway(host) }
        if !path.hasPrefix("/api/v1") || path.contains("..") || path.unicodeScalars.contains(where: { $0.value <= 32 }) || path.contains("#") || path.contains("\\") { throw AppError.argument("Invalid Guardian API path.") }
        guard let uri = WebAddress("https://" + host + path), uri.host == host, uri.port == 443, uri.userInfo.isEmpty else { throw AppError.argument("Invalid Guardian API host.") }
        var request = URLRequest(url: uri.url)
        request.httpMethod = method
        request.timeoutInterval = 35
        request.setValue(EeroApi.userAgent, forHTTPHeaderField: "User-Agent")
        if host == VpnContracts.partnerHost { request.setValue(VpnContracts.publishableKey, forHTTPHeaderField: "GRD-Connect-Publishable-Key") }
        if var payload = body {
            if host == VpnContracts.partnerHost, let values = payload as? JObject {
                let copy = JObject(); for (k, v) in values { copy[k] = v }
                copy["connect-publishable-key"] = VpnContracts.publishableKey; payload = copy
            }
            request.httpBody = Data(J.stringify(payload).utf8)
            request.setValue("application/json; charset=utf-8", forHTTPHeaderField: "Content-Type")
        }
        let (data, response) = try await http.send(request)
        if !(200...299).contains(response.statusCode) { throw AppError.invalidOperation("Guardian could not complete the request (HTTP " + String(response.statusCode) + "). Check your benefit eligibility and try again.") }
        if data.count > 4 * 1024 * 1024 { throw AppError.invalidData("Guardian returned an oversized response.") }
        let text = String(decoding: data, as: UTF8.self)
        return text.trim().isEmpty ? J.obj() : try J.parse(text)
    }
    func regions() async throws -> Any? { try await send("GET", VpnContracts.serverHost, "/api/v1.3/servers/all-server-regions/city") }
    func devices(_ credentials: Any?) async throws -> Any? { try await send("POST", VpnContracts.partnerHost, "/api/v1.2/partners/subscriber/devices/list", try VpnContracts.subscriber(credentials)) }
    func removeDevice(_ credentials: Any?, _ uuid: String?) async throws -> Any? {
        let body = try VpnContracts.subscriber(credentials)
        body["ep-grd-device-uuid"] = try ExtendedContracts.identifier(uuid)
        return try await send("POST", VpnContracts.partnerHost, "/api/v1.2/partners/subscriber/device/delete", body)
    }
    func createProfile(_ pet: String?, _ region: String?, _ privateKey: String?, _ publicKey: String?) async throws -> Any? {
        guard let pet = pet, !pet.isEmpty else { throw AppError.invalidOperation("Register this Mac first.") }
        let rows = try await send("POST", VpnContracts.serverHost, "/api/v1.3/servers/hostnames-for-region", J.obj("region", region, "region-precision", "city", "paid", true, "feature-environment", 0, "beta-capable", false))
        func score(_ r: Any) -> Int { FeatureContracts.parseInt32(J.s(r, "capacity-score")) ?? Int(Int32.max) }
        let online = J.list(rows).filter { !J.b($0, "offline") }.enumerated().sorted { a, b in
            let x = score(a.element), y = score(b.element); return x != y ? x < y : a.offset < b.offset
        }
        guard let host = online.first?.element else { throw AppError.invalidOperation("Guardian did not return an online server for that region.") }
        let gateway = try VpnContracts.gateway(J.s(host, "hostname"))
        let credential = try await send("POST", VpnContracts.serverHost, "/api/v1.2/subscriber-credential/create", J.obj("validation-method", "pe-token", "pe-token", pet))
        let subscriber = J.s(credential, "subscriber-credential")
        if subscriber.isEmpty { throw AppError.invalidData("Guardian did not return a subscriber credential.") }
        let device = try await send("POST", gateway, "/api/v1.4/device-credentials", J.obj("subscriber-credential", subscriber, "transport-protocol", "wireguard", "public-key", try VpnContracts.key(publicKey)))
        return try VpnContracts.profile(privateKey, gateway, device)
    }
}
