import Foundation

/// The service streams concatenated JSON objects, not Server-Sent Events.
final class JsonObjectStream {
    private var buffer = String.UnicodeScalarView()
    private var length = 0
    private var depth = 0
    private var quoted = false, escaped = false
    init() {}
    func feed(_ chars: [Character], _ count: Int, _ receive: (Any?) throws -> Void) throws {
        for character in chars.prefix(count) {
            for c in character.unicodeScalars {
                if length == 0 && c.properties.isWhitespace { continue }
                if length == 0 && c != "{" { throw AppError.invalidData("The chat service returned an unsupported stream.") }
                buffer.append(c); length += c.utf16.count
                if length > 1024 * 1024 { throw AppError.invalidData("A chat response exceeded the size limit.") }
                if quoted { if escaped { escaped = false } else if c == "\\" { escaped = true } else if c == "\"" { quoted = false } }
                else if c == "\"" { quoted = true }
                else if c == "{" || c == "[" { depth += 1 }
                else if c == "}" || c == "]" { depth -= 1 }
                if depth == 0 {
                    let value = try J.parse(String(buffer))
                    buffer = String.UnicodeScalarView(); length = 0
                    try receive(value)
                }
            }
        }
    }
    /// C# EndOfStreamException (an IOException).
    func finish() throws { if length != 0 { throw AppError.io("The chat response was interrupted. Reload history before sending again.") } }
}

enum ConversationContracts {
    static func request(_ networkId: String?, _ conversationId: String?, _ message: String?, _ mcpToken: String? = nil, _ permission: Any? = nil, _ uiEvent: Any? = nil) throws -> JObject {
        if (networkId ?? "").trim().isEmpty { throw AppError.argument("Select a network before opening chat.") }
        if let m = message, m.trim().isEmpty || m.utf16.count > 8000 { throw AppError.argument("Enter a message of 1–8,000 characters.") }
        let body = J.obj("context", J.obj("focused_network_id", networkId, "client_capabilities", J.obj("opt_in_widgets", [String]())))
        if let c = conversationId, !c.isEmpty { body["conversation_id"] = c }
        if let m = message { body["message"] = m }
        if let t = mcpToken, !t.isEmpty { body["mcp_token"] = t }
        if let p = permission, !(p is JNull) { body["permission_grant"] = p }
        if let u = uiEvent, !(u is JNull) { body["ui_event"] = u }
        return body
    }
    static func error(_ error: Error) -> String {
        if let api = error as? ApiException, api.apiError == "error.conversations.not_available" { return "eero has not enabled chat for this account or network." }
        if let api = error as? ApiException, api.apiError == "error.conversations.user_blocked" { return "eero has restricted chat access for this account." }
        return MainWindow.message(error)
    }
}

/// A response whose body arrives in chunks (HttpCompletionOption.ResponseHeadersRead).
final class HttpStream {
    let response: HTTPURLResponse
    let chunks: AsyncThrowingStream<Data, Error>
    private let cancelAction: () -> Void
    init(_ response: HTTPURLResponse, _ chunks: AsyncThrowingStream<Data, Error>, _ cancel: @escaping () -> Void = {}) {
        self.response = response; self.chunks = chunks; cancelAction = cancel
    }
    func cancel() { cancelAction() }
    /// A body that is already complete (transports that do not stream).
    static func complete(_ data: Data, _ response: HTTPURLResponse) -> HttpStream {
        HttpStream(response, AsyncThrowingStream { c in if !data.isEmpty { c.yield(data) }; c.finish() })
    }
}

/// Transports that can return the response before its body is complete. Others fall back to `send`.
protocol StreamingHttpTransport: AnyObject {
    func stream(_ request: URLRequest) async throws -> HttpStream
}

extension UrlSessionTransport: StreamingHttpTransport {
    func stream(_ request: URLRequest) async throws -> HttpStream { try await UrlSessionStreaming.shared.stream(request) }
}

/// URLSession data-delegate streaming: same cookie, cache, TLS and redirect policy as UrlSessionTransport;
/// 30 seconds without data times out, a whole reply may take up to 10 minutes.
final class UrlSessionStreaming: NSObject, URLSessionDataDelegate {
    static let shared = UrlSessionStreaming()
    private final class State {
        var head: CheckedContinuation<HTTPURLResponse, Error>?
        let body: AsyncThrowingStream<Data, Error>.Continuation
        init(_ body: AsyncThrowingStream<Data, Error>.Continuation) { self.body = body }
    }
    private let lock = NSLock()
    private var states: [Int: State] = [:]
    private lazy var session: URLSession = {
        let c = URLSessionConfiguration.ephemeral
        c.httpCookieStorage = nil; c.httpShouldSetCookies = false; c.httpCookieAcceptPolicy = .never
        c.urlCache = nil; c.requestCachePolicy = .reloadIgnoringLocalCacheData
        c.timeoutIntervalForRequest = 30; c.timeoutIntervalForResource = 600
        c.tlsMinimumSupportedProtocolVersion = .TLSv12
        return URLSession(configuration: c, delegate: self, delegateQueue: nil)
    }()
    func stream(_ request: URLRequest) async throws -> HttpStream {
        var continuation: AsyncThrowingStream<Data, Error>.Continuation!
        let chunks = AsyncThrowingStream<Data, Error> { continuation = $0 }
        let task = session.dataTask(with: request)
        continuation.onTermination = { _ in task.cancel() }
        let state = State(continuation)
        let response: HTTPURLResponse = try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (head: CheckedContinuation<HTTPURLResponse, Error>) in
                lock.lock(); state.head = head; states[task.taskIdentifier] = state; lock.unlock()
                task.resume()
            }
        } onCancel: { task.cancel() }
        return HttpStream(response, chunks, { task.cancel() })
    }
    private func state(_ task: URLSessionTask) -> State? { lock.lock(); defer { lock.unlock() }; return states[task.taskIdentifier] }
    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive response: URLResponse, completionHandler: @escaping (URLSession.ResponseDisposition) -> Void) {
        guard let s = state(dataTask) else { completionHandler(.cancel); return }
        lock.lock(); let head = s.head; s.head = nil; lock.unlock()
        if let http = response as? HTTPURLResponse { head?.resume(returning: http); completionHandler(.allow) }
        else { head?.resume(throwing: AppError.invalidData("eero returned an unexpected response. Please try again later.")); completionHandler(.cancel) }
    }
    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive data: Data) { state(dataTask)?.body.yield(data) }
    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        lock.lock(); let s = states.removeValue(forKey: task.taskIdentifier); let head = s?.head; s?.head = nil; lock.unlock()
        if let head = head { head.resume(throwing: error ?? AppError.invalidData("eero returned an unexpected response. Please try again later.")) }
        if let error = error { s?.body.finish(throwing: error) } else { s?.body.finish() }
    }
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
        completionHandler(nil)
    }
}

/// Decodes UTF-8 that arrives in arbitrary chunks; invalid bytes are rejected (UTF8Encoding(false, true)).
struct Utf8ChunkDecoder {
    private var pending = Data()
    mutating func decode(_ chunk: Data) throws -> String {
        pending.append(chunk)
        let bytes = [UInt8](pending)
        // Keep an incomplete trailing sequence for the next chunk.
        var cut = bytes.count
        var i = bytes.count - 1, back = 0
        while i >= 0 && back < 4 && (bytes[i] & 0xC0) == 0x80 { i -= 1; back += 1 }
        if i >= 0 {
            let lead = bytes[i]
            let need = lead >= 0xF0 ? 4 : lead >= 0xE0 ? 3 : lead >= 0xC0 ? 2 : 1
            if bytes.count - i < need { cut = i }
        }
        guard let text = String(data: Data(bytes[0..<cut]), encoding: .utf8) else { throw AppError.invalidData("The chat service returned an unsupported stream.") }
        pending = Data(bytes[cut...])
        return text
    }
    func finish() throws { if !pending.isEmpty { throw AppError.invalidData("The chat service returned an unsupported stream.") } }
}

extension EeroApi {
    /// PUT /2.2/conversations/messages and deliver each streamed JSON object to `receive`.
    /// The API gate is released once the stream starts, so refresh, switching and logout keep working while a reply is read.
    /// Cancel the calling Task to stop (C# CancellationToken); `receive` runs on the calling task, not the main actor.
    func streamConversation(_ body: Any?, _ receive: (Any?) throws -> Void) async throws {
        var opened: HttpStream? = nil
        await gate.wait()
        do {
            if !signedIn { throw ApiException(401, "session.missing", true) }
            // Retry only an explicit session-refresh rejection before a stream starts.
            for attempt in 0..<2 {
                var request = URLRequest(url: try EeroApi.resource("/2.2/conversations/messages"))
                request.httpMethod = "PUT"
                request.setValue(EeroApi.userAgent, forHTTPHeaderField: "User-Agent")
                request.setValue("application/json", forHTTPHeaderField: "Accept")
                request.setValue("s=" + (try EeroApi.validToken(token)), forHTTPHeaderField: "Cookie")
                request.httpBody = Data(J.stringify(body).utf8)
                request.setValue("application/json; charset=utf-8", forHTTPHeaderField: "Content-Type")
                let response: HttpStream
                if let streaming = http as? StreamingHttpTransport { response = try await streaming.stream(request) }
                else { let (data, r) = try await http.send(request); response = HttpStream.complete(data, r) }
                if !(200...299).contains(response.response.statusCode) {
                    var text = Data()
                    do { for try await chunk in response.chunks { text.append(chunk); if text.count > 65536 { break } } } catch {}
                    response.cancel()
                    let envelope = try? J.parse(String(decoding: text, as: UTF8.self))
                    let failure = ApiException(response.response.statusCode, J.s(envelope, "meta", "error"), true)
                    if !(attempt == 0 && failure.code == 401 && failure.apiError == "error.session.refresh") { throw failure }
                    let fresh = try await raw("POST", "login/refresh", nil, token, false, true)
                    try commit(fresh.cookie ?? J.s(fresh.data, "user_token").nullIfEmpty ?? token!)
                } else {
                    opened = response
                    for value in EeroApi.setCookieHeaders(response.response) {
                        if let m = Regex.match(value, "(?:^|,\\s*)s=([^; ,]+)") {
                            do { try commit(m[1]) } catch { response.cancel(); throw error }
                        }
                    }
                    break
                }
            }
        } catch { await gate.release(); throw error }
        await gate.release()
        guard let stream = opened else { return }
        defer { stream.cancel() }
        let parser = JsonObjectStream()
        var decoder = Utf8ChunkDecoder()
        var total = 0
        for try await chunk in stream.chunks {
            try Task.checkCancellation()
            total += chunk.count
            if total > 8 * 1024 * 1024 { throw AppError.invalidData("The chat response exceeded the size limit.") }
            let text = try decoder.decode(chunk)
            try parser.feed(Array(text), text.count) { value in try Task.checkCancellation(); try receive(value) }
        }
        try Task.checkCancellation()
        try decoder.finish()
        try parser.finish()
    }
}
