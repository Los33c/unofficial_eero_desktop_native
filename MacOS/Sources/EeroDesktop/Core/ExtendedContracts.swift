import Foundation

enum ExtendedContracts {
    static func inviteCode(_ input: String?) throws -> String {
        var code = (input ?? "").trim()
        if let url = WebAddress(code) {
            if url.scheme != "https" || url.port != 443 || !url.userInfo.isEmpty || !(url.host == "eero.com" || url.host.hasSuffix(".eero.com")) { throw AppError.argument("Use the eero invitation link or its invitation code.") }
            let query = url.query.trimStart("?").split("&").map { p -> [String] in
                guard let i = p.firstIndex(of: "=") else { return [p] }
                return [String(p[..<i]), String(p[p.index(after: i)...])]
            }.filter { $0.count == 2 }
            if let pair = query.first(where: { ["invite_code", "inviteCode", "code"].contains($0[0].unescapedData) }) { code = pair[1].unescapedData }
            else { code = url.absolutePath.trim("/").split("/").last ?? "" }
        }
        if !Regex.isMatch(code, "^[A-Za-z0-9_~.+/=-]{4,2048}$") { throw AppError.argument("Enter a valid invitation code or eero invitation link.") }
        return code
    }
    static func identifier(_ input: String?) throws -> String {
        guard let input = input, Regex.isMatch(input, "^[A-Za-z0-9_-]{1,128}$") else { throw AppError.argument("The service returned an invalid identifier.") }
        return input
    }
    static func inviteResponse(_ code: String?, _ accept: Bool) throws -> JObject { J.obj("invite_code", try inviteCode(code), "accept", accept) }
    static func license(_ key: String?, _ networkId: String? = nil, _ autoRenew: Bool = false) throws -> JObject {
        let k = (key ?? "").trim()
        if k.utf16.count < 4 || k.utf16.count > 512 || k.unicodeScalars.contains(where: { $0.properties.generalCategory == .control }) { throw AppError.argument("Enter the software license key supplied with your subscription.") }
        guard let networkId = networkId else { return J.obj("software_key", k) }
        return J.obj("software_key", k, "network_id", try identifier(networkId), "auto_renew", autoRenew)
    }
    static func pppoe(_ enabled: Bool, _ username: String?, _ password: String?, _ replaceCredentials: Bool, _ vlan: String?) throws -> JObject {
        let pppoe = J.obj("enabled", enabled)
        if enabled && replaceCredentials {
            guard let username = username, !username.trim().isEmpty, let password = password, !password.isEmpty else { throw AppError.argument("Enter both the PPPoE username and password provided by your ISP.") }
            pppoe["credentials"] = J.obj("username", username.trim(), "password", password)
        }
        let body = J.obj("pppoe", pppoe)
        if let vlan = vlan, !vlan.trim().isEmpty {
            guard let tag = FeatureContracts.parseInt32(vlan), tag >= 0, tag <= 4094 else { throw AppError.argument("VLAN ID must be between 0 and 4094.") }
            body["vlan"] = String(tag)
        } else { body["vlan"] = "" }
        return body
    }
    static func uploadUrl(_ input: String?) throws -> URL {
        guard let u = WebAddress(input), u.scheme == "https", u.port == 443, u.userInfo.isEmpty, u.fragment.isEmpty,
              Regex.isMatch(u.host, "^(?:[a-z0-9][a-z0-9.-]*\\.)?s3(?:[.-][a-z0-9-]+)?\\.amazonaws\\.com$", .caseInsensitive)
        else { throw AppError.invalidData("eero did not return a supported secure S3 upload destination.") }
        return u.url
    }
    static func portalMime(_ path: String?, _ bytes: Data?, _ logo: Bool) throws -> String {
        guard let bytes = bytes, bytes.count >= 8, bytes.count <= (logo ? 5 : 10) * 1024 * 1024 else { throw AppError.argument("Select a logo up to 5 MB or a PDF up to 10 MB.") }
        let name = (path ?? "").split("/").last ?? ""
        let dot = name.lastIndexOf(".")
        let ext = dot < 0 ? "" : name.substring(dot).lowercased()
        let b = [UInt8](bytes)
        if logo && ext == ".png" && Array(b.prefix(8)) == [137, 80, 78, 71, 13, 10, 26, 10] { return "image/png" }
        if logo && [".jpg", ".jpeg"].contains(ext) && b[0] == 255 && b[1] == 216 && b[2] == 255 { return "image/jpeg" }
        if !logo && ext == ".pdf" && Array(b.prefix(5)) == [37, 80, 68, 70, 45] { return "application/pdf" }
        throw AppError.argument(logo ? "Select a PNG or JPEG image." : "Select a PDF document.")
    }
    static func importJob(_ importPath: String?, _ logo: Bool, _ configurationId: String?) throws -> JObject {
        guard let importPath = importPath, !importPath.trim().isEmpty, importPath.utf16.count <= 4096 else { throw AppError.invalidData("eero did not return an import path.") }
        return J.obj("import_file_path", importPath, "import_job_type", logo ? "CaptivePortalLogo" : "CaptivePortalToS", "metadata", J.stringify(J.obj("configuration-id", try identifier(configurationId))))
    }
    static func email(_ value: String?) throws -> String {
        let v = (value ?? "").trim()
        if v.utf16.count > 254 || !Regex.isMatch(v, "^[^\\s@]+@[^\\s@]+\\.[^\\s@]+$") { throw AppError.argument("Enter a valid email address.") }
        return v
    }
    static func dnsPerformance(_ text: String?, _ port: Int) throws -> JObject {
        var servers: [String] = []
        for s in (text ?? "").splitRemovingEmpty("\r\n,;").map({ $0.trim() }) where !servers.contains(s) { servers.append(s) }
        if servers.isEmpty || servers.count > 8 || port < 1 || port > 65535 { throw AppError.argument("Enter one to eight DNS server IP addresses and a port between 1 and 65535.") }
        var entries: [Any] = []
        for server in servers {
            guard let ip = IPAddress.tryParse(server) else { throw AppError.argument("Enter DNS server IP addresses rather than hostnames or URLs.") }
            entries.append(J.obj("ip", ip.toString(), "port", port))
        }
        return J.obj("servers", entries)
    }
}
