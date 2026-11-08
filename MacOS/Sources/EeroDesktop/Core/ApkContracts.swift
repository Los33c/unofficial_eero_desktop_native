import Foundation

/// Multipart request body (Retrofit @Multipart). Text parts keep their order; one optional file part.
/// Serialized like .NET MultipartFormDataContent (quoted boundary, text/plain; charset=utf-8 parts).
final class MultipartFields {
    var fields: [(key: String, value: String)] = []
    var fileField: String?, fileName: String?, fileType: String?
    var file: Data?
    init() {}
    @discardableResult func add(_ name: String, _ value: String?) -> MultipartFields { fields.append((key: name, value: value ?? "")); return self }
    func value(_ name: String) -> String? { fields.first { $0.key == name }?.value }
    func content() -> (Data, String) {
        let boundary = UUID().uuidString.lowercased()
        var body = Data()
        func line(_ s: String) { body.append(Data((s + "\r\n").utf8)) }
        for p in fields {
            line("--" + boundary)
            line("Content-Type: text/plain; charset=utf-8")
            line("Content-Disposition: form-data; name=\"" + p.key + "\"")
            line("")
            body.append(Data(p.value.utf8)); line("")
        }
        if let file = file {
            line("--" + boundary)
            line("Content-Type: " + (fileType ?? "application/octet-stream"))
            line("Content-Disposition: form-data; name=\"" + (fileField ?? "") + "\"; filename=\"" + (fileName ?? "") + "\"")
            line("")
            body.append(file); line("")
        }
        line("--" + boundary + "--")
        return (body, "multipart/form-data; boundary=\"" + boundary + "\"")
    }
}

/// Request contracts for endpoints present in eero Android 26.13.0 that were not yet used by this client.
/// Field names come from the app's Retrofit @Field/@Body/@Part declarations and Gson @SerializedName values.
enum ApkContracts {
    static let ledColors: [String] = ["blue", "green", "white", "off"]
    static let customerTypes: [String] = ["Residential", "Business", "MDU"]
    static let safeFilterTypes: [String] = ["SubnetFilter", "ProfileFilter"]

    private static func range(_ value: String?, _ min: Int, _ max: Int, _ label: String) throws -> Int {
        guard let n = FeatureContracts.parseInt32((value ?? "").trim()), n >= min, n <= max else { throw AppError.argument("Enter " + label + " from " + String(min) + " to " + String(max) + ".") }
        return n
    }
    static func token(_ value: String?, _ label: String, _ max: Int = 128) throws -> String {
        let s = (value ?? "").trim()
        if !Regex.isMatch(s, "^[A-Za-z0-9_.:-]{1," + String(max) + "}$") { throw AppError.argument("Enter a valid " + label + ".") }
        return s
    }
    static func lines(_ text: String?) -> [String] { (text ?? "").splitRemovingEmpty("\n\r,").map { $0.trim() }.filter { !$0.isEmpty } }
    private static func distinct(_ items: [String]) -> [String] { var out: [String] = []; for i in items where !out.contains(i) { out.append(i) }; return out }

    // POST 2.2/eeros/{serial}/led_cycle (form): colors[], duration, time_per_color
    static func ledCycle(_ colors: [String]?, _ duration: String?, _ timePerColor: String?) throws -> JObject {
        let list = (colors ?? []).map { $0.trim().lowercased() }.filter { !$0.isEmpty }
        if list.isEmpty || list.count > 8 || list.contains(where: { !ledColors.contains($0) }) { throw AppError.argument("Choose one to eight colors: blue, green, white or off.") }
        return J.obj("colors[]", list, "duration", try range(duration, 1, 600, "a duration in seconds"), "time_per_color", try range(timePerColor, 1, 60, "seconds per color"))
    }
    // POST 2.2/eeros/{id}/nightlight/override (form): brightness_percentage
    static func nightlightPreview(_ brightness: String?) throws -> JObject { J.obj("brightness_percentage", try range(brightness, 0, 100, "a brightness")) }
    // PUT 2.2/networks/{id}/passpoint/enabled (JSON PasspointStatus)
    static func passpoint(_ enabled: Bool) -> JObject { J.obj("enabled", enabled) }
    // POST 2.2/networks/{id}/node_setup (form): default_channel_duration. The Android app uses 3600 seconds.
    static func defaultChannel(_ seconds: String?) throws -> JObject { J.obj("default_channel_duration", try range(seconds, 60, 7200, "a duration in seconds")) }
    // POST 2.2/eeros/networks/{networkId}/notify_swap (JSON NofifyNodeSwapRequest)
    static func nodeSwap(_ oldSerial: String?, _ newSerial: String?, _ succeeded: Bool) throws -> JObject {
        let a = try RuntimeContracts.serial(oldSerial), b = try RuntimeContracts.serial(newSerial)
        if a.caseInsensitiveCompare(b) == .orderedSame { throw AppError.argument("The replacement eero must have a different serial number.") }
        return J.obj("old_eero_serial", a, "new_eero_serial", b, "succeeded", succeeded)
    }
    // POST /2.3/networks/{id}/placement_rating/{serial} (JSON BluetoothPlacementTestRequest: rssi, mcs, freq; all optional)
    static func placementRating(_ rssi: String?, _ mcs: String?, _ freq: String?) throws -> JObject {
        let body = JObject()
        if let rssi = rssi, !rssi.trim().isEmpty {
            guard let r = Double(rssi.trim()), r.isFinite, r >= -120, r <= 0 else { throw AppError.argument("Enter RSSI in dBm from -120 to 0.") }
            body["rssi"] = r
        }
        if let mcs = mcs, !mcs.trim().isEmpty { body["mcs"] = try range(mcs, 0, 15, "an MCS index") }
        if let freq = freq, !freq.trim().isEmpty { body["freq"] = try range(freq, 2400, 7200, "a frequency in MHz") }
        return body
    }
    // PUT /2.3/networks/{id}/devices/secondary_wan_config (JSON BackupInternetDeviceAccess)
    static func secondaryWan(_ mac: String?, _ allow: Bool) throws -> JObject { J.obj("mac", try FeatureContracts.mac(mac), "secondary_wan_deny_access", !allow) }
    // PUT 2.2/networks/{id}/business_guest_network (JSON EnableCaptivePortalOnBoarding)
    static func businessGuest(_ otherNetworks: Bool) -> JObject { J.obj("enable_for_other_networks", otherNetworks) }
    // PUT dns_policies/network/allowed/cnames (JSON AllowCnamesForNetworkRequest)
    static func cnames(_ domains: String?) throws -> JObject {
        let list = distinct(try lines(domains).map { try FeatureContracts.domain($0) })
        if list.count > 100 { throw AppError.argument("Enter no more than 100 domains.") }
        return J.obj("domains", list)
    }
    // PUT dns_policies/profiles/allowed/cnames (JSON AllowCnamesForProfilesRequest)
    static func profileCnames(_ domains: String?, _ profileIds: [String]?) throws -> JObject {
        let body = try cnames(domains)
        let ids = distinct(try (profileIds ?? []).map { try ExtendedContracts.identifier($0) })
        if ids.isEmpty { throw AppError.argument("Choose at least one profile.") }
        body["profiles"] = ids; return body
    }
    // PUT 2.2/networks/{id}/flags/{name} (form): value
    static func flagName(_ name: String?) throws -> String {
        let s = (name ?? "").trim()
        if !Regex.isMatch(s, "^[a-z0-9_]{1,64}$") { throw AppError.argument("Enter a flag name with lowercase letters, digits and underscores.") }
        return s
    }
    static func flagValue(_ value: String?) throws -> JObject {
        let s = (value ?? "").trim(); if s.isEmpty || s.utf16.count > 512 { throw AppError.argument("Enter a flag value of 1–512 characters.") }
        return J.obj("value", s)
    }
    // PUT 2.2/networks/{id}/network_customer_type (form): network_customer_type = NetworkType.name()
    static func customerType(_ type: String?) throws -> JObject {
        guard let type = type, customerTypes.contains(type) else { throw AppError.argument("Choose Residential, Business or MDU.") }
        return J.obj("network_customer_type", type)
    }
    static func safeFilterType(_ type: String?) throws -> String {
        guard let type = type, safeFilterTypes.contains(type) else { throw AppError.argument("Choose SubnetFilter or ProfileFilter.") }
        return type
    }
    // 2.2/account/trust (form): certificate [, serial]
    static func certificate(_ pem: String?) throws -> String {
        let s = (pem ?? "").replacingOccurrences(of: "\r\n", with: "\n").trim()
        if s.utf16.count > 16384 || !Regex.isMatch(s, "^-----BEGIN CERTIFICATE-----\n[A-Za-z0-9+/=\n]+\n-----END CERTIFICATE-----$") { throw AppError.argument("Paste one PEM certificate that starts with -----BEGIN CERTIFICATE-----.") }
        return s
    }
    static func trust(_ pem: String?, _ oldSerial: String? = nil) throws -> JObject {
        let body = J.obj("certificate", try certificate(pem))
        if let oldSerial = oldSerial { body["serial"] = try token(oldSerial, "certificate serial") }
        return body
    }
    // GET 2.2/transfer/has_networks?email=…|phone=…
    static func transferTarget(_ contact: String?) throws -> String {
        let s = (contact ?? "").trim()
        if s.contains("@") { return "email=" + (try ExtendedContracts.email(s)).escapedData }
        return "phone=" + (try PhoneInput.normalize(s)).escapedData
    }
    // POST 2.2/premium/customer/plans (form): autotrial_tier
    static func autoTrial(_ tier: String?) throws -> JObject { J.obj("autotrial_tier", try token(tier, "trial tier", 40)) }
    // POST 2.2/partners/encryptme (form): test=false selects the production partner environment.
    static func encryptMe() -> JObject { J.obj("test", false) }
    // Partner upsell flow (JSON bodies)
    static func upsellMethod(_ method: String?, _ sessionId: String?) throws -> JObject {
        J.obj("authentication_method", try token(method, "authentication method", 64), "authentication_session_id", try token(sessionId, "session ID", 256))
    }
    static func upsellAuthenticate(_ key: String?, _ method: String?, _ sessionId: String?) throws -> JObject {
        let k = (key ?? "").trim()
        if k.isEmpty || k.utf16.count > 256 || k.unicodeScalars.contains(where: { $0.value < 32 }) { throw AppError.argument("Enter the verification code or key you received.") }
        return J.obj("authentication_key", k, "authentication_method", try token(method, "authentication method", 64), "session_id", try token(sessionId, "session ID", 256))
    }
    static func upsellOrder(_ offerIds: String?, _ sessionId: String?, _ submit: Bool) throws -> JObject {
        let ids = distinct(try lines(offerIds).map { try token($0, "offer ID", 128) })
        if ids.isEmpty { throw AppError.argument("Enter at least one offer ID.") }
        return J.obj("internal_offer_ids", ids, "authentication_session_id", try token(sessionId, "session ID", 256), "should_submit", submit)
    }
    // PUT 2.2/organizations/self/subsets/{subsetId}/associate (JSON AssociateEeroToSubsetRequest)
    static func subsetAssociation(_ serial: String?, _ unitIdentifier: String?, _ unitDescription: String?) throws -> JObject {
        let id = (unitIdentifier ?? "").trim(), text = (unitDescription ?? "").trim()
        if id.isEmpty || id.utf16.count > 64 || text.utf16.count > 200 { throw AppError.argument("Enter a unit identifier (up to 64 characters) and an optional description.") }
        return J.obj("eero_serial", try RuntimeContracts.serial(serial), "unit_identifier", id, "unit_description", text)
    }
    // GET 2.2/directedid?d=&i=&p=  (the Android app requests 0,0,0)
    static func directedIdQuery(_ d: String?, _ i: String?, _ p: String?) throws -> String {
        "d=" + String(try range(d, 0, 99, "a directed-ID type")) + "&i=" + String(try range(i, 0, 99, "an identifier type")) + "&p=" + String(try range(p, 0, 99, "a partner"))
    }
    // GET 2.2/networks/{id}/ouicheck?serial=&version=
    static func ouiQuery(_ serial: String?, _ version: String?) throws -> String {
        var q = "serial=" + (try RuntimeContracts.serial(serial)).escapedData
        if let version = version, !version.trim().isEmpty { q += "&version=" + (try token(version, "firmware version", 64)).escapedData }
        return q
    }
    // POST 2.2/zendesk/create (multipart CreateReportIssue)
    // Labels are optional; the Android app loads its label list at runtime, so none are hard-coded here.
    static func issue(_ summary: String?, _ description: String?, _ labels: [String]?, _ eeros: [String]?, _ devices: [String]?, _ networkId: String?, _ when: Date) throws -> MultipartFields {
        let s = (summary ?? "").trim(), d = (description ?? "").trim()
        if s.utf16.count < 3 || s.utf16.count > 200 { throw AppError.argument("Enter a summary of 3–200 characters.") }
        if d.utf16.count < 10 || d.utf16.count > 8000 { throw AppError.argument("Describe the issue in 10–8,000 characters.") }
        let body = MultipartFields()
        for (n, e) in distinct(eeros ?? []).enumerated() { body.add("affected_eeros[" + String(n) + "]", try RuntimeContracts.serial(e)) }
        for (n, m) in distinct(devices ?? []).enumerated() { body.add("affected_devices[" + String(n) + "]", try FeatureContracts.mac(m)) }
        for (n, l) in distinct(labels ?? []).enumerated() { body.add("labels[" + String(n) + "]", try token(l, "label", 64)) }
        body.add("issue_timestamp", Dates.utcStamp(when))
        body.add("description", d).add("summary", s).add("network_id", networkId == nil ? "" : (try ExtendedContracts.identifier(networkId)))
        return body
    }
    static func screenshot(_ body: MultipartFields, _ name: String?, _ png: Data?) throws {
        guard let png = png, png.count >= 8, png.count <= 8 * 1024 * 1024 else { throw AppError.argument("Attach a PNG image up to 8 MB.") }
        let b = [UInt8](png.prefix(4))
        if b[0] != 137 || b[1] != 80 || b[2] != 78 || b[3] != 71 { throw AppError.argument("Attach a PNG image up to 8 MB.") }
        body.file = png; body.fileField = "reportscreen"; body.fileType = "image/png"
        body.fileName = Regex.replace((name ?? "screenshot.png").split("/").last ?? "", "[^A-Za-z0-9_.-]", "_")
    }
    static func isPassthrough(_ network: Any?) -> Bool { J.s(network, "connection", "mode") == "ipPassthrough" }
    static func isNotRouting(_ network: Any?) -> Bool { let mode = J.s(network, "connection", "mode"); return mode == "bridged" || mode == "ipPassthrough" }
    static func passthroughCapable(_ network: Any?) -> Bool { J.b(network, "capabilities", "ip_passthrough", "capable") }
}
