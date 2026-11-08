import Foundation

extension Tests {
    static func amazonTestReturn(_ attempt: AmazonNativeAttempt, _ code: String = "test-code") throws -> URL {
        let fields = try SignInContracts.parameters(WebAddress(attempt.signInUrl)!.query)
        return URL(string: fields["openid.return_to"]! + "&openid.oa2.authorization_code=" + code.escapedData + "&openid.oa2.scope=device_auth_access")!
    }
    static func amazonNativeChecks(_ scratch: String) async throws {
        try check(AmazonNativeAttempt.challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk") == "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", "consumer Amazon PKCE matches the published RFC 7636 vector")
        do {
            let attempt = AmazonNativeAttempt(ServiceFake()), other = AmazonNativeAttempt(ServiceFake())
            let fields = try SignInContracts.parameters(WebAddress(attempt.signInUrl)!.query)
            let otherFields = try SignInContracts.parameters(WebAddress(other.signInUrl)!.query)
            try check(WebAddress(attempt.signInUrl)!.host == "www.amazon.com" && fields["openid.oa2.response_type"] == "code" && fields["openid.oa2.scope"] == "device_auth_access", "consumer Amazon uses the APK's authorization-code scope on Amazon's own portal")
            try check(!attempt.signInUrl.contains("insight") && !attempt.signInUrl.contains("client_secret") && fields["client_id"] == nil, "consumer Amazon does not require an Insight account or developer OAuth client")
            try check(fields["openid.oa2.client_id"] != otherFields["openid.oa2.client_id"] && fields["openid.oa2.code_challenge"] != otherFields["openid.oa2.code_challenge"] && fields["openid.return_to"] != otherFields["openid.return_to"], "each sign-in has an independent virtual device, verifier and return nonce")
            let body = try attempt.exchangeBody(try amazonTestReturn(attempt))
            try check(fields["openid.oa2.code_challenge"] == AmazonNativeAttempt.challenge(J.s(body, "code_verifier")) && fields["openid.oa2.client_id"] == "device:" + J.s(body, "client_id"), "consumer token exchange is bound to the original device client and PKCE challenge")
            try check(J.s(body, "client_domain") == "DeviceLegacy" && J.s(body, "code_algorithm") == "SHA-256" && J.s(body, "requested_token_type") == "access_token" && J.get(body, "client_secret") == nil, "consumer exchange uses the APK's token protocol without a client secret or device registration")
            let good = try amazonTestReturn(attempt).absoluteString
            for value in [good.replacingOccurrences(of: "www.amazon.com", with: "www.amazon.com.evil.test"), good.replacingOccurrences(of: "https://", with: "http://"), good.replacingOccurrences(of: "/ap/maplanding", with: "/ap/other"), good + "&state=duplicate", good + "#fragment", good.replacingOccurrences(of: "device_auth_access", with: "profile"), good.replacingOccurrences(of: "test-code", with: "bad%0D%0Acode"), try amazonTestReturn(other).absoluteString] {
                try expectSync(invalidData, "consumer callback rejects address, nonce, duplicate-parameter, scope and code injection") { _ = try attempt.exchangeBody(URL(string: value)!) }
            }
            for value in ["http://www.amazon.com/", "https://www.amazon.com.evil.test", "https://user@www.amazon.com/", "https://www.amazon.com:8443/", "file:///C:/example", "https://insight.eero.com/"] {
                try check(!AmazonNativeAttempt.navigation(value), "consumer sign-in navigation rejects insecure, impersonated and unrelated origins")
            }
        }
        var handler = ServiceFake()
        handler.enqueue { request in
            try check(request.httpMethod == "POST" && request.url?.absoluteString == AmazonNativeAttempt.tokenEndpoint, "consumer authorization code is exchanged only at Amazon's HTTPS token endpoint")
            try check(request.value(forHTTPHeaderField: "Cookie") == nil && request.value(forHTTPHeaderField: "Authorization") == nil && request.value(forHTTPHeaderField: "x-amzn-identity-auth-domain") == "api.amazon.com", "Amazon token exchange receives no eero session, browser cookies or preexisting credentials")
            let body = bodyOf(request)
            try check(J.s(body, "source_token") == "test-code" && J.s(body, "source_token_type") == "authorization_code" && J.s(body, "app_name") == "EeroDesktop", "desktop identifies itself and exchanges only this attempt's returned code")
            return serviceResponse(request, J.obj("token_type", "bearer", "access_token", "test-amazon-access", "expires_in", 3600))
        }
        do {
            let exchange = AmazonNativeAttempt(handler)
            try check(try await exchange.exchange(try amazonTestReturn(exchange)) == "test-amazon-access", "consumer exchange accepts a valid bearer response")
            try await expect(invalidOperation, "a consumer authorization code cannot be exchanged twice") { _ = try await exchange.exchange(try amazonTestReturn(exchange)) }
            try check(handler.count == 0, "single-use protection causes no repeated token request")
        }
        for result in [J.obj("token_type", "mac", "access_token", "test"), J.obj("token_type", "bearer"), J.obj("token_type", "bearer", "access_token", "bad\r\nCookie:bad")] {
            handler = ServiceFake(); handler.enqueue { serviceResponse($0, result) }
            let exchange = AmazonNativeAttempt(handler)
            try await expect(invalidData, "malformed Amazon token responses cannot become eero credentials") { _ = try await exchange.exchange(try amazonTestReturn(exchange)) }
        }
        handler = ServiceFake(); handler.enqueue { response($0, 302) }
        do {
            let exchange = AmazonNativeAttempt(handler)
            try await expect(Tests.api, "Amazon exchange redirects are rejected instead of forwarding the authorization code") { _ = try await exchange.exchange(try amazonTestReturn(exchange)) }
        }
        handler = ServiceFake(); handler.enqueue { response($0, 401, "{\"error\":\"invalid_grant\",\"error_description\":\"test-sensitive-phrase\"}") }
        do {
            let exchange = AmazonNativeAttempt(handler)
            do { _ = try await exchange.exchange(try amazonTestReturn(exchange)); throw Failure(description: "Expected rejected Amazon exchange.") }
            catch let error as ApiException { try check(error.apiError == "amazon.token.exchange.invalid_grant" && !error.message.contains("test-sensitive-phrase"), "Amazon errors retain only a known diagnostic category without echoing service response details") }
        }
        let store = SessionStore((scratch as NSString).appendingPathComponent("amazon-native-session.dat")); try store.save("previous-session")
        var fake = Fake()
        fake.enqueue(Step("POST", "/2.2/login/amazon", body: "auth_token=test-amazon-access&create_account=false", json: "{\"data\":{\"user_token\":\"candidate-session\"}}"))
        fake.enqueue(Step("GET", "/2.2/account", cookie: "s=candidate-session", json: "{\"data\":{}}"))
        do {
            let api = EeroApi(store, fake)
            _ = api.restore(); try await expect(invalidData, "Amazon login refuses a success-shaped response without an actual eero account identity") { _ = try await api.amazonSignIn("test-amazon-access") }
            try check(api.signedIn && store.load() == "previous-session", "an incomplete Amazon account response preserves the previous verified session")
        }
        fake = Fake()
        fake.enqueue(Step("POST", "/2.2/login/amazon", body: "auth_token=test-amazon-access&create_account=false", json: "{\"data\":{\"user_token\":\"candidate-session\"}}"))
        fake.enqueue(Step("GET", "/2.2/account", cookie: "s=candidate-session", json: "{\"data\":{\"user_id\":\"test-user\"}}", setCookie: "s=verified-session; Secure; HttpOnly"))
        do {
            let api = EeroApi(store, fake)
            api.remember = true; let account = try await api.amazonSignIn("test-amazon-access")
            try check(api.signedIn && store.load() == "verified-session" && J.s(account, "user_id") == "test-user" && fake.count == 0, "Amazon login saves only the rotated eero session after verifying an existing account")
        }
        handler = ServiceFake()
        handler.enqueue { serviceResponse($0, J.obj("data", J.obj("user_token", "candidate-session"))) }
        handler.enqueue { cancelCurrentTask(); return serviceResponse($0, J.obj("data", J.obj("user_id", "test-user"))) }
        do {
            let api = EeroApi(store, handler)
            _ = api.restore()
            let task = Task { _ = try await api.amazonSignIn("test-amazon-access") }
            try await expect(cancelled, "closing consumer Amazon sign-in cancels verification before committing its candidate session") { try await task.value }
            try check(api.signedIn && store.load() == "verified-session", "cancelled consumer Amazon login preserves the remembered session")
        }
    }
}
