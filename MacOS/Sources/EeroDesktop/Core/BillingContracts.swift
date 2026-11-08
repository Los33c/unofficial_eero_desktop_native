import Foundation

enum BillingContracts {
    static func `return`(_ uri: URL) -> Bool {
        guard let u = WebAddress.of(uri), u.userInfo.isEmpty else { return false }
        let pages = ["pluspurchasecomplete", "businesspurchasecomplete", "datapackpurchasecomplete"]
        if u.scheme == "eero" && u.host == "jumpto" { return pages.contains(u.absolutePath.trim("/")) }
        if u.scheme == "https" && u.host == "app.eero.com" && u.port == 443 {
            return pages.contains { p in u.absolutePath == "/" + p || u.absolutePath == "/" + p + "/index.html" || u.absolutePath == "/jumpto/" + p }
        }
        return false
    }
}
