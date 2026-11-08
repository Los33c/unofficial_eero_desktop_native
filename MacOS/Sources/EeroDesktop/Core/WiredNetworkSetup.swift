import Foundation

/// Creates a network for a wired gateway eero and claims the eero into it. A failed claim can be retried
/// against the same network; an uncertain creation is never repeated automatically.
final class WiredNetworkSetup {
    private(set) var creationUncertain = false
    private(set) var createdUrl: String?
    private var serial: String?, location: String?
    init() {}
    func create(_ api: EeroApi, _ body: Any?, _ serialNumber: String?, _ nodeLocation: String) async throws {
        if createdUrl != nil || creationUncertain { throw AppError.invalidOperation("A creation request was already sent. Review the network in your account before starting another setup.") }
        serial = try RuntimeContracts.serial(serialNumber); try MainWindow.validateName(nodeLocation); location = nodeLocation.trim()
        creationUncertain = true
        let created = try await api.send("POST", "/2.3/networks", body)
        let resource = try EeroApi.resource(J.s(created, "url"))
        guard let parts = WebAddress(url: resource), Regex.isMatch(parts.absolutePath, "^/2\\.[23]/networks/[A-Za-z0-9_-]+$"), parts.query.isEmpty
        else { throw AppError.invalidOperation("The server did not return a valid created network. Refresh your account before continuing.") }
        createdUrl = parts.absolutePath; creationUncertain = false
    }
    func claim(_ api: EeroApi) async throws -> String {
        guard let destination = createdUrl else { throw AppError.invalidOperation("Create a network first.") }
        try await api.send("POST", "/2.2/eeros?requireSession=true", J.obj("serial", serial, "location", location, "network", destination), true)
        createdUrl = nil; serial = nil; location = nil
        return destination
    }
}
