import Foundation

extension Tests {
    /// Contracts for the endpoints added from the eero Android 26.13.0 audit.
    static func apkChecks(_ scratch: String) async throws {
        // IP Passthrough (new connection mode in 26.13.0): same DHCP request as bridge, mode ipPassthrough, no dhcp block.
        let pass = try FeatureContracts.dhcp("passthrough", "", "", "", "", false)
        try check(J.s(pass, "connection", "mode") == "ipPassthrough" && J.get(pass, "dhcp") == nil, "IP Passthrough serializes as ipPassthrough without DHCP settings")
        try check(J.s(try FeatureContracts.dhcp("automatic", "", "", "", "", false), "connection", "mode") == "nat", "automatic DHCP still uses NAT")
        let passNet = J.obj("connection", J.obj("mode", "ipPassthrough"), "capabilities", J.obj("ip_passthrough", J.obj("capable", true)))
        try check(ApkContracts.isPassthrough(passNet) && ApkContracts.isNotRouting(passNet) && ApkContracts.passthroughCapable(passNet), "passthrough networks are detected and treated as non-routing")
        try check(!ApkContracts.isNotRouting(J.obj("connection", J.obj("mode", "nat"))), "NAT networks keep routing features")

        // Form bodies, including Retrofit list fields.
        try check(EeroApi.formPairs(try ApkContracts.ledCycle(["blue", "white"], "30", "1")).map { $0.0 + "=" + $0.1 }.joined(separator: "&") == "colors[]=blue&colors[]=white&duration=30&time_per_color=1", "LED cycle repeats colors[] per color")
        try check(rejects { _ = try ApkContracts.ledCycle(["purple"], "30", "1") } && rejects { _ = try ApkContracts.ledCycle(["blue"], "0", "1") }, "LED cycle rejects unknown colors and durations")
        try check(J.stringify(try ApkContracts.nightlightPreview("40")) == "{\"brightness_percentage\":40}" && rejects { _ = try ApkContracts.nightlightPreview("101") }, "nightlight preview brightness contract")
        try check(J.stringify(ApkContracts.passpoint(true)) == "{\"enabled\":true}", "Passpoint body")
        try check(J.stringify(try ApkContracts.defaultChannel("3600")) == "{\"default_channel_duration\":3600}" && rejects { _ = try ApkContracts.defaultChannel("10") }, "default-channel setup duration contract")
        try check(J.stringify(try ApkContracts.nodeSwap("GGC1UC0000000001", "GGC1UC0000000002", true)) == "{\"old_eero_serial\":\"GGC1UC0000000001\",\"new_eero_serial\":\"GGC1UC0000000002\",\"succeeded\":true}", "eero swap notification body")
        try check(rejects { _ = try ApkContracts.nodeSwap("GGC1UC0000000001", "ggc1uc0000000001", true) }, "eero swap needs two different serials")
        try check(J.stringify(try ApkContracts.placementRating("", "", "")) == "{}" && J.stringify(try ApkContracts.placementRating("-61.5", "7", "5180")) == "{\"rssi\":-61.5,\"mcs\":7,\"freq\":5180}", "placement rating measurements are optional and typed")
        try check(J.stringify(try ApkContracts.secondaryWan("02-00-00-00-00-0A", false)) == "{\"mac\":\"02:00:00:00:00:0a\",\"secondary_wan_deny_access\":true}", "device backup access body normalizes MAC")
        try check(J.stringify(ApkContracts.businessGuest(true)) == "{\"enable_for_other_networks\":true}", "business guest network body")
        try check(J.stringify(try ApkContracts.cnames("Example.com\ncdn.example.com.\nexample.com")) == "{\"domains\":[\"example.com\",\"cdn.example.com\"]}", "CNAME allow list is normalized and de-duplicated")
        try check(rejects { _ = try ApkContracts.cnames("https://example.com/path") } && rejects { _ = try ApkContracts.profileCnames("example.com", []) }, "CNAME lists reject URLs and require a profile")
        try check(J.stringify(try ApkContracts.profileCnames("example.com", ["12", "12", "34"])) == "{\"domains\":[\"example.com\"],\"profiles\":[\"12\",\"34\"]}", "profile CNAME body")
        try check(try ApkContracts.flagName("custom_whitelist_blacklist") == "custom_whitelist_blacklist" && rejects { _ = try ApkContracts.flagName("../account") }, "network flag names cannot change the API path")
        try check(J.stringify(try ApkContracts.customerType("MDU")) == "{\"network_customer_type\":\"MDU\"}" && rejects { _ = try ApkContracts.customerType("Enterprise") }, "network type uses the app's enum names")
        let pem = "-----BEGIN CERTIFICATE-----\nTUlJQlNhbXBsZQ==\n-----END CERTIFICATE-----"
        try check(J.s(try ApkContracts.trust(pem + "\n"), "certificate") == pem && J.s(try ApkContracts.trust(pem, "AB:CD"), "serial") == "AB:CD", "trusted certificate add and replace bodies")
        try check(rejects { _ = try ApkContracts.certificate("-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----") }, "private keys are never accepted as certificates")
        try check(try ApkContracts.transferTarget("Person@Example.com").hasPrefix("email=") && ApkContracts.transferTarget("4255550100") == "phone=%2B14255550100", "transfer recipient check query")
        try check(J.stringify(try ApkContracts.upsellOrder("offer-1\noffer-2", "session-1", false)) == "{\"internal_offer_ids\":[\"offer-1\",\"offer-2\"],\"authentication_session_id\":\"session-1\",\"should_submit\":false}", "partner order body")
        try check(J.stringify(try ApkContracts.subsetAssociation("GGC1UC0000000001", "4B", "")) == "{\"eero_serial\":\"GGC1UC0000000001\",\"unit_identifier\":\"4B\",\"unit_description\":\"\"}", "organization unit association body")
        try check(try ApkContracts.directedIdQuery("0", "0", "0") == "d=0&i=0&p=0" && ApkContracts.ouiQuery("GGC1UC0000000001", "v7.1.1") == "serial=GGC1UC0000000001&version=v7.1.1", "directed-ID and OUI query strings")

        // Issue report multipart fields.
        let when = ISO8601DateFormatter().date(from: "2026-11-07T12:00:00Z")!
        let issue = try ApkContracts.issue("Slow wifi", "Wifi upstairs drops every evening.", ["wifi"], ["GGC1UC0000000001"], ["020000000001"], "17", when)
        try check(issue.value("affected_eeros[0]") == "GGC1UC0000000001" && issue.value("affected_devices[0]") == "02:00:00:00:00:01" && issue.value("labels[0]") == "wifi" && issue.value("issue_timestamp") == "2026-11-07T12:00:00Z" && issue.value("network_id") == "17" && issue.value("summary") == "Slow wifi", "issue report uses the app's multipart field names")
        try check(rejects { _ = try ApkContracts.issue("x", "too short", nil, nil, nil, nil, Date()) }, "issue reports need a summary and description")
        try check(rejects { try ApkContracts.screenshot(issue, "a.png", Data([1, 2, 3, 4, 5, 6, 7, 8])) }, "only PNG screenshots are attached")
        let (multipart, multipartType) = issue.content()
        let multipartText = String(decoding: multipart, as: UTF8.self)
        try check(multipartType.hasPrefix("multipart/form-data; boundary=") && multipartText.contains("Content-Disposition: form-data; name=\"summary\"\r\n\r\nSlow wifi\r\n") && multipartText.hasSuffix("--\r\n"), "multipart body uses form-data parts like .NET MultipartFormDataContent")

        // Wire format through the authenticated client.
        let store = SessionStore((scratch as NSString).appendingPathComponent("apk-session.dat"))
        let fake = Fake(); fake.enqueue(login())
        fake.enqueue(Step("POST", "/2.2/login/verify", cookie: "s=pending-test", setCookie: "s=apk-test; Path=/; HttpOnly; Secure"))
        fake.enqueue(Step("POST", "/2.2/eeros/GGC1UC0000000001/led_cycle", cookie: "s=apk-test", body: "colors%5B%5D=blue&colors%5B%5D=off&duration=20&time_per_color=2", contentType: "application/x-www-form-urlencoded"))
        fake.enqueue(Step("PUT", "/2.2/networks/17/passpoint/enabled", cookie: "s=apk-test", body: "{\"enabled\":false}", contentType: "application/json"))
        fake.enqueue(Step("PUT", "/2.2/networks/17/settings", cookie: "s=apk-test", body: "{\"connection\":{\"mode\":\"ipPassthrough\"},\"nat_port_randomization\":false}", contentType: "application/json"))
        fake.enqueue(Step("GET", "/2.2/networks/17/safe_filter_levels", cookie: "s=apk-test", query: "?safeFilterType=SubnetFilter"))
        fake.enqueue(Step("POST", "/2.2/zendesk/create", cookie: "s=apk-test", contentType: "multipart/form-data"))
        fake.enqueue(Step("POST", "/2.2/register/email/resend", cookie: "s=apk-test", body: "\"\"", contentType: "application/json"))
        let api = EeroApi(store, fake)
        try await api.beginLogin("person@example.com"); try await api.verify("123456")
        try await api.send("POST", "/2.2/eeros/GGC1UC0000000001/led_cycle", try ApkContracts.ledCycle(["blue", "off"], "20", "2"), true)
        try await api.send("PUT", "/2.2/networks/17/passpoint/enabled", ApkContracts.passpoint(false))
        try await api.send("PUT", "/2.2/networks/17/settings", try FeatureContracts.dhcp("passthrough", "", "", "", "", false))
        try await api.send("GET", "/2.2/networks/17/safe_filter_levels?safeFilterType=" + (try ApkContracts.safeFilterType("SubnetFilter")))
        try await api.send("POST", "/2.2/zendesk/create", issue)
        try await api.send("POST", "/2.2/register/email/resend", "")
        try check(fake.count == 0, "26.13.0 endpoint wire formats")
    }
    static func apkCatalogChecks() throws {
        try check(FeatureCatalog.entries.filter { $0.path == "Help/Report an issue" || $0.path == "Activity/Usage details" || $0.path == "Home/Add or replace eero/Setup tools" }.count == 3, "new feature pages are listed in the catalog")
    }
}
