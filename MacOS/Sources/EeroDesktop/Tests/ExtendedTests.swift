import Foundation

extension Tests {
    final class StorageFake: HttpTransport {
        var seen = false; var status = 200
        init(_ status: Int = 200) { self.status = status }
        func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
            try check(request.httpMethod == "PUT" && request.url?.host == "sample.s3.us-east-1.amazonaws.com", "portal upload uses returned HTTPS S3 PUT")
            try check(request.value(forHTTPHeaderField: "Cookie") == nil && request.value(forHTTPHeaderField: "Authorization") == nil, "eero authentication is never forwarded to asset storage")
            try check(request.value(forHTTPHeaderField: "x-amz-server-side-encryption") == "aws:kms" && request.value(forHTTPHeaderField: "x-amz-server-side-encryption-aws-kms-key-id") == "test-kms-key", "portal upload preserves server KMS encryption headers")
            try check(Tests.mediaType(request) == "image/png" && request.httpBody?.count == 8, "portal upload sends exact bytes and MIME type")
            seen = true; return response(request, status)
        }
    }
    static func extendedChecks() async throws {
        try check(try ExtendedContracts.inviteCode("https://join.eero.com/invite?invite_code=sample_code-123") == "sample_code-123", "invitation link code is parsed without visiting the link")
        try check(J.stringify(try ExtendedContracts.inviteResponse("sample_code-123", false)) == "{\"invite_code\":\"sample_code-123\",\"accept\":false}", "decline invitation uses API boolean response contract")
        try check(rejects { _ = try ExtendedContracts.inviteCode("https://join.eero.com.evil.example/?code=sample_code-123") }, "lookalike invitation hosts are rejected")
        try check(rejects { _ = try ExtendedContracts.identifier("1/../../account") }, "membership identifiers cannot alter API paths")
        let validate = try ExtendedContracts.license("TEST-LICENSE"), activate = try ExtendedContracts.license("TEST-LICENSE", "17")
        try check(J.get(validate, "network_id") == nil && J.s(validate, "software_key") == "TEST-LICENSE", "license validation does not activate for a network")
        try check(!J.b(activate, "auto_renew") && J.s(activate, "network_id") == "17", "license activation defaults to renewal disabled")
        let isp = try ExtendedContracts.pppoe(true, "user", "private-test-password", true, "100")
        try check(J.s(isp, "pppoe", "credentials", "username") == "user" && J.s(isp, "vlan") == "100", "existing-network PPPoE and VLAN contract")
        try check(J.get(try ExtendedContracts.pppoe(true, "", "", false, ""), "pppoe", "credentials") == nil, "retaining PPPoE credentials never submits empty passwords")
        try check(rejects { _ = try ExtendedContracts.pppoe(true, "", "", true, "100") }, "new PPPoE credentials must be complete")
        try check(rejects { _ = try ExtendedContracts.pppoe(false, "", "", false, "4095") }, "invalid ISP VLAN IDs are rejected")
        try check(J.list(J.get(try ExtendedContracts.dnsPerformance("1.1.1.1\n2606:4700:4700::1111", 53), "servers")).count == 2, "DNS performance accepts IPv4 and IPv6 resolver entries")
        try check(rejects { _ = try ExtendedContracts.dnsPerformance("https://resolver.example", 53) }, "DNS performance rejects URLs before making requests")
        let png = Data([137, 80, 78, 71, 13, 10, 26, 10])
        try check(try ExtendedContracts.portalMime("logo.png", png, true) == "image/png", "portal upload verifies MIME from the file signature")
        try check(rejects { _ = try ExtendedContracts.portalMime("terms.pdf", png, false) }, "renaming a non-PDF does not bypass terms file validation")
        let job = try ExtendedContracts.importJob("uploads/test/logo.png", true, "5")
        try check(J.s(job, "import_job_type") == "CaptivePortalLogo" && J.s(job, "metadata") == "{\"configuration-id\":\"5\"}", "portal import uses API job type and serialized configuration metadata")
        for url in ["http://sample.s3.amazonaws.com/x", "https://sample.s3.amazonaws.com.evil.example/x", "https://api-user.e2ro.com/2.2/account", "https://user@sample.s3.amazonaws.com/x", "https://127.0.0.1/x", "https://sample.s3.amazonaws.com:444/x"] {
            try expectSync(invalidData, "untrusted storage URL rejected: " + url) { _ = try ExtendedContracts.uploadUrl(url) }
        }
        let upload = J.obj("signed_upload_url", "https://sample.s3.us-east-1.amazonaws.com/logo?signature=fake", "kms_key_id", "test-kms-key")
        var storage = StorageFake(); try await PortalUpload.send(upload, png, "image/png", storage); try check(storage.seen, "isolated asset upload completed")
        storage = StorageFake(302)
        try await expect(invalidOperation, "redirected uploads fail before import creation") { try await PortalUpload.send(upload, png, "image/png", storage) }
    }
}
