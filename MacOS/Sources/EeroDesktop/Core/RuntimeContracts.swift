import Foundation

enum RuntimeContracts {
    static func serial(_ value: String?) throws -> String {
        let serial = (value ?? "").trim()
        if !Regex.isMatch(serial, "^[A-Za-z0-9-]{8,40}$") { throw AppError.argument("Enter the serial number printed on the eero or accessory.") }
        return serial
    }
    static func adminInvite() -> JObject { J.obj("invite_role", "admin") }
    static func invitationResponse(_ inviteId: String?, _ accept: Bool) throws -> JObject { J.obj("invite_id", try ExtendedContracts.identifier(inviteId), "accept", accept) }
    static func autoSetup(_ mode: String?) throws -> JObject {
        guard let mode = mode, ["OFF", "ZTS", "LTS", "DEFAULT"].contains(mode) else { throw AppError.argument("Choose a supported automatic setup mode.") }
        return J.obj("mode", mode)
    }
    /// `consents` is the user's explicit decisions: a JObject (keeps its order) or a `[String: Any]`.
    static func createNetwork(_ name: String, _ password: String, _ timezone: String?, _ consents: Any?) throws -> JObject {
        try MainWindow.validateSsid(name); try MainWindow.validatePassword(password)
        if !Regex.isMatch(timezone ?? "", "^[A-Za-z_+-]+(?:/[A-Za-z_0-9+-]+){0,3}$") { throw AppError.argument("Enter an IANA timezone such as America/Chicago or Europe/London.") }
        return J.obj("name", name.trim(), "password", password, "timezone", J.obj("value", timezone), "settings", J.obj("consents", consents, "enabled_simple_setup_vendors", [Int]()))
    }
}
