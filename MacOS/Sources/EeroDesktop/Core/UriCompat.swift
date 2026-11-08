import Foundation

/// A System.Uri-style view of an absolute URL, so checks translated from C# read the same:
/// lower-cased scheme and host, the scheme's default port when none is given (C# `Uri.Port`),
/// escaped `absolutePath` ("/" when empty), `query` with its "?" and `fragment` with its "#".
struct WebAddress {
    let scheme: String
    let host: String
    let port: Int
    let userInfo: String
    let absolutePath: String
    let query: String
    let fragment: String
    let absoluteUri: String
    let url: URL

    /// `Uri.TryCreate(text, UriKind.Absolute, out uri)`: nil unless the text is an absolute URI.
    init?(_ text: String?) {
        guard let text = text, let url = URL(string: text) else { return nil }
        self.init(url: url)
    }
    init?(url: URL) {
        guard let c = URLComponents(url: url, resolvingAgainstBaseURL: false), let s = c.scheme, !s.isEmpty else { return nil }
        self.url = url
        scheme = s.lowercased()
        host = (c.percentEncodedHost ?? "").lowercased()
        if let p = c.port { port = p }
        else { port = scheme == "https" ? 443 : scheme == "http" ? 80 : scheme == "ftp" ? 21 : -1 }
        var user = c.percentEncodedUser ?? ""
        if let password = c.percentEncodedPassword { user += ":" + password }
        userInfo = user
        let path = c.percentEncodedPath
        absolutePath = path.isEmpty && c.percentEncodedHost != nil ? "/" : path
        query = c.percentEncodedQuery.map { "?" + $0 } ?? ""
        fragment = c.percentEncodedFragment.map { "#" + $0 } ?? ""
        absoluteUri = url.absoluteString
    }
    /// For a `URL` the caller already holds (C# methods that take a `Uri`).
    static func of(_ url: URL) -> WebAddress? { WebAddress(url: url) }
}

extension String {
    /// C# `Uri.UnescapeDataString`: invalid escapes are left as they are.
    var unescapedData: String { removingPercentEncoding ?? self }
}
