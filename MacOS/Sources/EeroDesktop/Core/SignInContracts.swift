import Foundation

enum SignInContracts {
    static func ssoReturn(_ uri: URL) -> Bool {
        guard let u = WebAddress.of(uri), u.userInfo.isEmpty else { return false }
        return (u.scheme == "https" && u.host == "app.eero.com" && u.port == 443 && u.absolutePath == "/auth/index.html") || (u.scheme == "eero" && u.host == "jumpto" && u.absolutePath == "/auth")
    }
    static func parameters(_ query: String) throws -> [String: String] {
        var result: [String: String] = [:]
        for item in query.trimStart("?#").split("&") {
            if item.isEmpty { continue }
            let pair: [String]
            if let i = item.firstIndex(of: "=") { pair = [String(item[..<i]), String(item[item.index(after: i)...])] } else { pair = [item] }
            let key = pair[0].replacingOccurrences(of: "+", with: " ").unescapedData
            if result[key] != nil { throw AppError.invalidData("The sign-in response contains duplicate parameters.") }
            result[key] = pair.count == 2 ? pair[1].replacingOccurrences(of: "+", with: " ").unescapedData : ""
        }
        return result
    }
    static func providerUrl(_ id: String?) throws -> String {
        guard let id = id, Regex.isMatch(id, "^[a-zA-Z0-9_-]{1,128}$") else { throw AppError.argument("The provider identifier is invalid.") }
        return "https://sso.eero.com/?idp=" + id.escapedData + "&redirect=" + "https://app.eero.com/auth/index.html".escapedData + "&clientID=28971b56-0016-4416-bef8-689caac23011"
    }
}

final class SignInService {
    private let http: HttpTransport
    init(_ handler: HttpTransport? = nil) { http = handler ?? UrlSessionTransport() }
    func dispose() {}
    func providers() async throws -> Any? {
        var request = URLRequest(url: URL(string: "https://sso.eero.com/api/v1/providers?assetType=mdpi")!)
        request.httpMethod = "GET"; request.setValue(EeroApi.userAgent, forHTTPHeaderField: "User-Agent")
        let (data, response) = try await http.send(request)
        if !(200...299).contains(response.statusCode) { throw AppError.invalidOperation("Response status code does not indicate success: " + String(response.statusCode) + ".") }
        if data.count > 1024 * 1024 { throw AppError.invalidData("The provider list exceeded its size limit.") }
        return try J.parse(String(decoding: data, as: UTF8.self))
    }
}
