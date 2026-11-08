import Foundation

enum PortalUpload {
    static func send(_ upload: Any?, _ data: Data, _ mime: String, _ handler: HttpTransport? = nil) async throws {
        let url = try ExtendedContracts.uploadUrl(J.s(upload, "signed_upload_url")); let key = J.s(upload, "kms_key_id")
        if key.isEmpty || key.utf16.count > 2048 || key.unicodeScalars.contains(where: { $0.properties.generalCategory == .control }) { throw AppError.invalidData("The upload encryption key is missing or invalid.") }
        // A separate transport never forwards the eero session cookie to storage.
        let http = handler ?? UrlSessionTransport(120)
        var request = URLRequest(url: url)
        request.httpMethod = "PUT"; request.timeoutInterval = 120
        request.setValue("aws:kms", forHTTPHeaderField: "x-amz-server-side-encryption")
        request.setValue(key, forHTTPHeaderField: "x-amz-server-side-encryption-aws-kms-key-id")
        request.httpBody = data; request.setValue(mime, forHTTPHeaderField: "Content-Type")
        let (_, response) = try await http.send(request)
        if !(200...299).contains(response.statusCode) { throw AppError.invalidOperation("The storage service rejected the upload (" + String(response.statusCode) + "). No import was requested.") }
    }
}
