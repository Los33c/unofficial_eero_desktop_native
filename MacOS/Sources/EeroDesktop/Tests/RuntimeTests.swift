import Foundation

extension Tests {
    static func runtimeChecks(_ scratch: String) async throws {
        try check(J.stringify(RuntimeContracts.adminInvite()) == "{\"invite_role\":\"admin\"}", "admin invitation uses serialized API invite_role field")
        try check(J.stringify(try RuntimeContracts.invitationResponse("invite-7", false)) == "{\"invite_id\":\"invite-7\",\"accept\":false}", "member-promotion response uses invite_id and boolean acceptance")
        try check(rejects { _ = try RuntimeContracts.invitationResponse("../account", true) }, "incoming invitation IDs cannot change a resource path")
        for mode in ["OFF", "ZTS", "LTS", "DEFAULT"] { try check(J.s(try RuntimeContracts.autoSetup(mode), "mode") == mode, "API automatic setup mode " + mode) }
        try check(rejects { _ = try RuntimeContracts.autoSetup("enabled") }, "unrecognized automatic setup modes rejected")
        var ble = BluetoothEero.advertisement(0, "3.0.0-1-AA==", -60)
        try check(ble.inNetwork == true && ble.ethernet == nil, "BLE v3 reads membership without inventing an Ethernet flag")
        ble = BluetoothEero.advertisement(0, "4.0.0-Model-1", -47)
        try check(ble.inNetwork == nil && ble.ethernet == true && ble.signal == -47, "BLE v4 reports Ethernet without inventing network membership")
        ble = BluetoothEero.advertisement(0, "eero Setup", -70); try check(ble.version == nil && ble.inNetwork == nil && ble.ethernet == nil, "legacy setup advertisement remains unknown until read")
        ble = BluetoothEero.advertisement(0, "malformed", -70); try check(ble.version == nil, "malformed BLE name does not crash discovery")
        let create = try RuntimeContracts.createNetwork("Test wifi", "sample-password", "America/Chicago", J.obj("test_terms", true, "optional", false))
        try check(J.s(create, "timezone", "value") == "America/Chicago" && J.b(create, "settings", "consents", "test_terms") && !J.b(create, "settings", "consents", "optional"), "network creation preserves timezone and explicit consent decisions")
        try check(rejects { _ = try RuntimeContracts.createNetwork("Test", "short", "America/Chicago", JObject()) }, "invalid wifi password rejected before creating a network")

        let store = SessionStore((scratch as NSString).appendingPathComponent("runtime-session.dat")); try store.save("existing-test")
        var fake = Fake()
        fake.enqueue(Step("GET", "/2.2/account", cookie: "s=unverified-browser-test", json: "{\"meta\":{\"code\":401,\"error\":\"error.session.invalid\"}}", status: 401))
        fake.enqueue(Step("GET", "/2.2/account", cookie: "s=verified-browser-test", setCookie: "s=rotated-browser-test; Path=/; Secure; HttpOnly"))
        do {
            let api = EeroApi(store, fake)
            _ = api.restore(); try await expect(Tests.api, "website sign-in is checked against account API") { try await api.importBrowserSession("unverified-browser-test") }
            try check(api.signedIn && store.load() == "existing-test", "rejected browser session preserves the existing saved sign-in")
            try await api.importBrowserSession("verified-browser-test"); try check(store.load() == "rotated-browser-test", "verified browser session honors server cookie rotation")
            try check(fake.count == 0, "browser session verification completed without extra requests")
        }
        fake = Fake(); try store.save("setup-test")
        fake.enqueue(Step("POST", "/2.3/networks", cookie: "s=setup-test", body: J.stringify(create), json: "{\"meta\":{\"code\":200},\"data\":{\"url\":\"/2.2/networks/new-7\"}}"))
        fake.enqueue(Step("POST", "/2.2/eeros", cookie: "s=setup-test", body: "serial=TESTSERIAL123&location=Office&network=%2F2.2%2Fnetworks%2Fnew-7", contentType: "application/x-www-form-urlencoded", json: "{\"meta\":{\"code\":409,\"error\":\"setup.not_ready\"}}", query: "?requireSession=true", status: 409))
        fake.enqueue(Step("POST", "/2.2/eeros", cookie: "s=setup-test", body: "serial=TESTSERIAL123&location=Office&network=%2F2.2%2Fnetworks%2Fnew-7", contentType: "application/x-www-form-urlencoded", query: "?requireSession=true"))
        do {
            let api = EeroApi(store, fake)
            _ = api.restore(); let setup = WiredNetworkSetup(); try await setup.create(api, create, "TESTSERIAL123", "Office")
            try await expect(Tests.api, "failed gateway addition remains recoverable") { _ = try await setup.claim(api) }
            try check(setup.createdUrl == "/2.2/networks/new-7" && !setup.creationUncertain, "failed claim preserves the created network")
            try await expect(invalidOperation, "claim failure cannot issue a second network creation") { try await setup.create(api, create, "TESTSERIAL123", "Office") }
            try check(try await setup.claim(api) == "/2.2/networks/new-7" && setup.createdUrl == nil, "retry claims the same network then clears completed setup state")
            try check(fake.count == 0, "wired setup sent exactly one creation and two explicit claims")
        }
        fake = Fake(); fake.enqueue(Step("POST", "/2.3/networks", cookie: "s=setup-test", json: "{\"meta\":{\"code\":503}}", status: 503))
        do {
            let api = EeroApi(store, fake)
            _ = api.restore(); let setup = WiredNetworkSetup()
            try await expect(Tests.api, "ambiguous network creation failure surfaced") { try await setup.create(api, create, "TESTSERIAL123", "Office") }
            try check(setup.creationUncertain, "uncertain creation remains blocked for account review")
            try await expect(invalidOperation, "ambiguous create is never silently repeated") { try await setup.create(api, create, "TESTSERIAL123", "Office") }
        }
    }
}
