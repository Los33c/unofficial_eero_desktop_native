import Foundation

extension Tests {
    static func serviceIntegrationChecks(_ scratch: String) async throws {
        let retiredStore = SessionStore((scratch as NSString).appendingPathComponent("retired-web-session.dat"))
        try retiredStore.save("eero-web-v1.retired-preview"); let retiredHandler = ServiceFake()
        do {
            let api = EeroApi(retiredStore, retiredHandler)
            try check(!api.restore() && !Files.exists(retiredStore.filePath), "retired Insight credentials cannot be restored as native API cookies")
            try await expect(invalidData, "retired web credentials are rejected before making native API requests") { try await api.importBrowserSession("eero-web-v1.retired-preview") }
            try check(retiredHandler.count == 0 && !api.signedIn, "rejecting a retired preview credential makes no request and leaves the app signed out")
        }
        let json = "{\"type\":\"InitialMessage\",\"content\":\"héllo { \\\"braces\\\" } 😀\"}\n{\"type\":\"MessageDelta\",\"content\":\"tail\",\"finished\":true}"
        for chunk in 1...9 {
            let parser = JsonObjectStream(); var messages: [Any?] = []; let chars = Array(json)
            var start = 0
            while start < chars.count { let part = Array(chars[start..<min(start + chunk, chars.count)]); try parser.feed(part, part.count) { messages.append($0) }; start += chunk }
            try parser.finish()
            try check(messages.count == 2 && J.s(messages[0], "content").contains("😀") && J.s(messages[1], "content") == "tail", "chat JSON framing across chunk size " + String(chunk))
        }
        try expectSync(io, "truncated chat stream is reported, not accepted as a complete reply") { let parser = JsonObjectStream(); try parser.feed(Array("{\"message\":"), 11) { _ in }; try parser.finish() }
        try expectSync(invalidData, "HTML cannot masquerade as a chat stream") { try JsonObjectStream().feed(Array("<html>"), 6) { _ in } }
        var decoder = Utf8ChunkDecoder(); let emoji = Data("😀".utf8)
        try check(try decoder.decode(emoji.prefix(2)) == "" && decoder.decode(emoji.suffix(2)) == "😀", "chat UTF-8 decoding keeps characters split across network chunks")
        try check(rejects { _ = try ConversationContracts.request("network", nil, String(repeating: "x", count: 8001)) }, "chat message length is checked before sending")
        let chat = try ConversationContracts.request("network-1", "conversation-1", "Check connectivity", "test-mcp")
        try check(J.s(chat, "context", "focused_network_id") == "network-1" && J.list(J.get(chat, "context", "client_capabilities", "opt_in_widgets")).isEmpty, "chat sends network context and no unsupported opt-in widgets")
        let grant = try ConversationContracts.request("network-1", "conversation-1", nil, nil, J.obj("approved", false, "interrupt_id", "permission-1"))
        try check(!J.b(grant, "permission_grant", "approved") && J.s(grant, "permission_grant", "interrupt_id") == "permission-1", "declining a permission sends the exact interrupt ID without granting it")
        let store = SessionStore((scratch as NSString).appendingPathComponent("service-session.dat"))
        var fake = Fake(); fake.enqueue(Step("GET", "/2.2/account", cookie: "s=stream-session", json: "{\"meta\":{\"code\":200},\"data\":{}}"))
        fake.enqueue(Step("PUT", "/2.2/conversations/messages", cookie: "s=stream-session", body: J.stringify(chat), json: json, setCookie: "s=stream-rotated; Secure; HttpOnly"))
        fake.enqueue(Step("GET", "/2.2/account", cookie: "s=stream-rotated", json: "{\"data\":{}}"))
        do {
            let api = EeroApi(store, fake)
            try await api.importBrowserSession("stream-session"); let values = Box<[Any?]>([])
            try await api.streamConversation(chat) { values.value.append($0) }; try await api.send("GET", "account")
            try check(values.value.count == 2 && fake.count == 0, "authenticated chat stream rotates its cookie and releases the API gate")
        }
        fake = Fake(); fake.enqueue(Step("GET", "/2.2/account", cookie: "s=cancel-session"))
        fake.enqueue(Step("PUT", "/2.2/conversations/messages", cookie: "s=cancel-session", json: json))
        fake.enqueue(Step("GET", "/2.2/account", cookie: "s=cancel-session"))
        do {
            let api = EeroApi(store, fake)
            try await api.importBrowserSession("cancel-session"); let seen = Box(0)
            let task = Task { try await api.streamConversation(chat) { _ in seen.value += 1; cancelCurrentTask() } }
            try await expect(cancelled, "chat cancellation aborts delivery inside a buffered response") { try await task.value }
            try await api.send("GET", "account"); try check(seen.value == 1 && fake.count == 0, "cancelled chat releases the API gate without delivering later events")
        }
        fake = Fake(); fake.enqueue(Step("GET", "/2.2/account", cookie: "s=old-session"))
        fake.enqueue(Step("POST", "/2.2/login/amazon", body: "auth_token=test-amazon&create_account=false", contentType: "application/x-www-form-urlencoded", json: "{\"data\":{\"user_token\":\"candidate-amazon\"}}"))
        fake.enqueue(Step("GET", "/2.2/account", cookie: "s=candidate-amazon", json: "{\"meta\":{\"code\":403}}", status: 403))
        fake.enqueue(Step("GET", "/2.2/account", cookie: "s=old-session"))
        do {
            let api = EeroApi(store, fake)
            try await api.importBrowserSession("old-session")
            try await expect(Tests.api, "Amazon sign-in is rejected if eero cannot verify the resulting account") { _ = try await api.amazonSignIn("test-amazon") }
            try await api.send("GET", "account")
            try check(api.signedIn && fake.count == 0, "rejected Amazon login preserves the existing verified eero session")
        }
        try check(SignInContracts.ssoReturn(URL(string: "https://app.eero.com/auth/index.html?token=sample")!) && !SignInContracts.ssoReturn(URL(string: "https://app.eero.com.evil.test/auth/index.html?token=sample")!), "provider sign-in callback is confined to eero's exact return endpoint")
        try check(try SignInContracts.providerUrl("provider-1").hasPrefix("https://sso.eero.com/"), "provider sign-in starts at the official eero broker")
        try check(BillingContracts.return(URL(string: "eero://jumpto/pluspurchasecomplete")!) && !BillingContracts.return(URL(string: "eero://jumpto/reset")!) && !BillingContracts.return(URL(string: "https://app.eero.com.evil.test/pluspurchasecomplete")!), "billing accepts only recognized completion callbacks and refreshes server status separately")
        let key = Data(repeating: 7, count: 32).base64EncodedString()
        try check(VpnContracts.canRegister(J.obj("seats_available", true)) && !VpnContracts.canRegister(J.obj("seats_available", false)), "VPN registration requires a reported available device slot")
        try check(!VpnContracts.canRegister(J.obj()) && !VpnContracts.canRegister(J.obj("seats_available", "true")), "missing or malformed VPN eligibility never enables registration")
        try check(VpnContracts.eligibilityMessage(J.obj("seats_available", false)).contains("device slots") && !VpnContracts.eligibilityMessage(J.obj("seats_available", false)).contains("does not have permission"), "full VPN device allowance is distinct from account permission denial")
        try check(!VpnContracts.canRegister(ApiException(403, "", false)) && VpnContracts.eligibilityMessage(ApiException(403, "", false)).contains("provider"), "VPN entitlement rejection explains provider availability without offering registration")
        try check(VpnContracts.eligibilityMessage(ApiException(402, "", false)).contains("eligible active benefit") && VpnContracts.eligibilityMessage(ApiException(503, "", false)).contains("Try again"), "VPN subscription and temporary service errors stay distinct")
        let profile = try VpnContracts.profile(key, "new-york-1.sgw.guardianapp.com", J.obj("server-public-key", key, "mapped-ipv4-address", "10.20.0.2", "mapped-ipv6-address", "fd00::2"))
        let configuration = try VpnContracts.configuration(profile)
        try check(configuration.contains("AllowedIPs = 0.0.0.0/0, ::/0") && configuration.contains("DNS = 1.1.1.1, 1.0.0.1") && configuration.contains(":51821"), "Guardian WireGuard config covers both address families with SDK DNS and endpoint port")
        try check(!configuration.contains("PostUp") && !configuration.contains("PreUp") && configuration.contains("10.20.0.2/32, fd00::2/128"), "generated VPN config has validated addresses and no command hooks")
        for bad in ["evil.test", "guardianapp.com.evil.test", "good.sgw.guardianapp.com\nPostUp=bad", "good.sgw.guardianapp.com:443"] {
            try expectSync(invalidData, "VPN gateway cannot redirect credentials or inject configuration") { _ = try VpnContracts.gateway(bad) }
        }
        try expectSync(invalidData, "VPN server cannot inject a broad interface prefix") { _ = try VpnContracts.profile(key, "valid.sgw.guardianapp.com", J.obj("server-public-key", key, "mapped-ipv4-address", "10.0.0.2/8")) }
        let scope = try VpnContracts.scope("account-1", "network-1")
        try check(scope != (try VpnContracts.scope("account-2", "network-1")) && scope != (try VpnContracts.scope("account-1", "network-2")) && scope.count == 24 && !scope.contains("account"), "VPN profile scopes isolate accounts and networks without exposing identifiers")
        var serviceFake = ServiceFake()
        serviceFake.enqueue { request in
            try check(request.url?.host == VpnContracts.partnerHost && request.value(forHTTPHeaderField: "Cookie") == nil, "Guardian receives no eero API cookie")
            let body = bodyOf(request)
            try check(J.s(body, "ep-grd-subscriber-identifier") == "sample-id" && J.s(body, "connect-publishable-key") == VpnContracts.publishableKey, "Guardian device list uses partner subscriber fields and publishable routing key")
            return serviceResponse(request, [J.obj("ep-grd-device-uuid", "device-1", "ep-grd-device-nickname", "Mac")])
        }
        do {
            let guardian = GuardianApi(serviceFake)
            let devices = try await guardian.devices(J.obj("guardian_id", "sample-id", "guardian_secret", "sample-secret")); try check(J.list(devices).count == 1, "Guardian device list is parsed without exposing secrets in UI details")
            try await expect(invalidData, "Guardian API refuses off-provider credential destinations") { _ = try await guardian.send("POST", "evil.test", "/api/v1/test", J.obj()) }
        }
        try check(serviceFake.count == 0, "all service test requests were exercised without any real account calls")
        serviceFake = ServiceFake()
        serviceFake.enqueue { request in
            let body = bodyOf(request)
            try check(request.url?.host == VpnContracts.serverHost && request.url?.path == "/api/v1.3/servers/hostnames-for-region" && J.s(body, "region") == "test-city", "VPN provisioning selects an online server in the requested city")
            return serviceResponse(request, [J.obj("hostname", "offline.sgw.guardianapp.com", "offline", true), J.obj("hostname", "online.sgw.guardianapp.com", "offline", false, "capacity-score", 2)])
        }
        serviceFake.enqueue { request in
            let body = bodyOf(request)
            try check(request.url?.path == "/api/v1.2/subscriber-credential/create" && J.s(body, "validation-method") == "pe-token" && J.s(body, "pe-token") == "test-pet", "VPN provisioning exchanges a device PE token for a subscriber credential")
            return serviceResponse(request, J.obj("subscriber-credential", "test-subscriber"))
        }
        serviceFake.enqueue { request in
            let body = bodyOf(request)
            try check(request.url?.host == "online.sgw.guardianapp.com" && request.url?.path == "/api/v1.4/device-credentials" && J.s(body, "transport-protocol") == "wireguard" && J.s(body, "public-key") == key, "Guardian receives the public key on the selected gateway")
            try check(J.get(body, "private_key") == nil && request.value(forHTTPHeaderField: "Cookie") == nil, "VPN private key and eero cookie are never sent to Guardian")
            return serviceResponse(request, J.obj("server-public-key", key, "mapped-ipv4-address", "10.20.0.2"))
        }
        do {
            let guardian = GuardianApi(serviceFake)
            let provisioned = try await guardian.createProfile("test-pet", "test-city", key, key)
            try check(J.s(provisioned, "gateway") == "online.sgw.guardianapp.com" && serviceFake.count == 0, "complete simulated Guardian provisioning produces a validated WireGuard profile")
        }
        let inspectedStore = SessionStore((scratch as NSString).appendingPathComponent("inspected-session.dat")); fake = Fake()
        fake.enqueue(Step("GET", "/2.2/account", cookie: "s=provider-candidate", json: "{\"data\":{\"name\":\"Sample person\"}}", setCookie: "s=provider-rotated; Secure; HttpOnly"))
        fake.enqueue(Step("GET", "/2.2/account", cookie: "s=provider-rotated", setCookie: "s=provider-final; Secure; HttpOnly"))
        do {
            let api = EeroApi(inspectedStore, fake)
            api.remember = true; let identity = try await api.inspectBrowserSession("provider-candidate")
            try check(!api.signedIn && !Files.exists(inspectedStore.filePath) && J.s(identity.account, "name") == "Sample person", "provider identity can be reviewed without signing in or saving credentials")
            try check(!J.stringify(identity).contains("provider-rotated"), "reviewed identity does not serialize its session credential")
            try await api.importBrowserSession(identity.sessionCredential); try check(inspectedStore.load() == "provider-final", "reviewed provider sign-in preserves cookie rotation through final verification")
        }
        try inspectedStore.save("preserved-session"); serviceFake = ServiceFake()
        serviceFake.enqueue { cancelCurrentTask(); return serviceResponse($0, J.obj("data", J.obj())) }
        do {
            let api = EeroApi(inspectedStore, serviceFake)
            _ = api.restore()
            let task = Task { try await api.importBrowserSession("cancelled-candidate") }
            try await expect(cancelled, "closing a sign-in window cancels candidate verification") { try await task.value }
            try check(api.signedIn && inspectedStore.load() == "preserved-session", "cancelled alternate sign-in cannot replace the saved session")
        }
        serviceFake = ServiceFake(); let started = Box(false)
        serviceFake.enqueueStream { request in
            started.value = true
            let chunks = AsyncThrowingStream<Data, Error> { _ in }   // never yields until cancelled
            return HttpStream(HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: "HTTP/1.1", headerFields: [:])!, chunks)
        }
        serviceFake.enqueue { request in try check(request.httpMethod == "GET", "normal account refresh runs while chat is awaiting its next chunk"); return serviceResponse(request, J.obj("data", J.obj())) }
        do {
            let api = EeroApi(inspectedStore, serviceFake)
            _ = api.restore()
            let streaming = Task { try await api.streamConversation(chat) { _ in } }
            var waited = 0; while !started.value && waited < 300 { try await Task.sleep(nanoseconds: 10_000_000); waited += 1 }
            let refreshed = Box(false)
            let refresh = Task { _ = try await api.send("GET", "account"); refreshed.value = true }
            waited = 0; while !refreshed.value && waited < 150 { try await Task.sleep(nanoseconds: 10_000_000); waited += 1 }
            let first = refreshed.value
            streaming.cancel(); _ = try? await streaming.value
            try await refresh.value; try check(first, "chat streaming does not lock out refresh or sign-out operations")
        }
        let probes = Box(0), stops = Box(0), confirmations = Box(0)
        try VpnTunnelLifecycle.start({}, { true }, { probes.value += 1; return probes.value == 3 }, { stops.value += 1 }, { confirmations.value += 1 }, 5, {})
        try check(probes.value == 3 && stops.value == 0 && confirmations.value == 1, "delayed VPN startup retries before confirming one fresh handshake")
        stops.value = 0; confirmations.value = 0
        var invalid = false
        do { try VpnTunnelLifecycle.start({}, { true }, { false }, { stops.value += 1 }, { confirmations.value += 1 }, 3, {}) } catch let e as AppError where e.kind == .invalidOperation { invalid = true }
        try check(invalid && stops.value == 1 && confirmations.value == 0, "VPN handshake failure removes the tunnel and cannot report connected")
        stops.value = 0; invalid = false
        do { try VpnTunnelLifecycle.start({ throw AppError.io("partial install") }, { true }, { true }, { stops.value += 1 }, {}, 1, {}) } catch let e as AppError where e.kind == .io { invalid = true }
        try check(invalid && stops.value == 1, "partially installed VPN service is removed after an installation error")
        stops.value = 0; invalid = false
        do { try VpnTunnelLifecycle.start({ throw AppError.io("no install") }, { false }, { true }, { stops.value += 1 }, {}, 1, {}) } catch let e as AppError where e.kind == .io { invalid = true }
        try check(invalid && stops.value == 0, "failed install without a service does not issue an unrelated stop")
        invalid = false
        do { try VpnTunnelLifecycle.start({}, { true }, { false }, { throw AppError.io("stop failed") }, {}, 1, {}) } catch is VpnCleanupException { invalid = true }
        try check(invalid, "failed VPN cleanup is reported explicitly instead of claiming disconnected")
        stops.value = 0; invalid = false
        do { try VpnTunnelLifecycle.start({}, { true }, { true }, { stops.value += 1 }, { throw AppError.io("cannot save success") }, 1, {}) } catch let e as AppError where e.kind == .io { invalid = true }
        try check(invalid && stops.value == 1, "VPN rolls back if its successful connection cannot be reported to the app")
    }
}
