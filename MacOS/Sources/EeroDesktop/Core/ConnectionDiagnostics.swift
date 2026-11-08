import Foundation

/// Classifies network failures (URLError and the POSIX errors underneath it) into the same categories and
/// messages as the Windows/Linux builds.
enum ConnectionDiagnostics {
    /// The error and its NSUnderlyingErrorKey chain, outermost first (C# InnerException chain).
    static func chain(_ error: Error) -> [NSError] {
        var list: [NSError] = []
        var current: NSError? = error as NSError
        while let e = current, list.count < 16 {
            list.append(e)
            current = e.userInfo[NSUnderlyingErrorKey] as? NSError
        }
        return list
    }
    private static func posixCategory(_ code: Int) -> String? {
        switch Int32(truncatingIfNeeded: code) {
        case EACCES, EPERM: return "network_access_blocked"
        case ETIMEDOUT: return "timeout"
        case ECONNREFUSED: return "connection_refused"
        case ENETUNREACH, EHOSTUNREACH, ENETDOWN, EHOSTDOWN: return "network_unreachable"
        default: return nil
        }
    }
    private static func urlCategory(_ code: Int) -> String? {
        switch URLError.Code(rawValue: code) {
        case .cannotFindHost, .dnsLookupFailed: return "dns_failure"
        case .secureConnectionFailed, .serverCertificateHasBadDate, .serverCertificateUntrusted, .serverCertificateHasUnknownRoot,
             .serverCertificateNotYetValid, .clientCertificateRejected, .clientCertificateRequired, .appTransportSecurityRequiresSecureConnection:
            return "tls_failure"
        case .timedOut: return "timeout"
        case .cannotConnectToHost: return "connection_refused"
        case .notConnectedToInternet, .internationalRoamingOff, .callIsActive: return "network_unreachable"
        case .dataNotAllowed: return "network_access_blocked"
        default: return nil
        }
    }
    static func category(_ error: Error) -> String {
        let errors = chain(error)
        // The POSIX code under a URLError is the most specific answer (like SocketException under HttpRequestException).
        for e in errors where e.domain == NSPOSIXErrorDomain { if let c = posixCategory(e.code) { return c } }
        for e in errors {
            if e.domain == NSURLErrorDomain, let c = urlCategory(e.code) { return c }
            if e.domain == "kCFErrorDomainCFNetwork" && (e.code == 2 || e.code == 1) { return "dns_failure" }
            if e.domain == NSOSStatusErrorDomain && (-9899 ... -9800).contains(e.code) { return "tls_failure" }
        }
        return error is CancellationError ? "timeout" : "connection_failure"
    }
    static func nativeCode(_ error: Error) -> Int {
        if error is CancellationError { return 0 }
        let errors = chain(error)
        if let posix = errors.first(where: { $0.domain == NSPOSIXErrorDomain }) { return posix.code }
        if let url = errors.first(where: { $0.domain == NSURLErrorDomain }) { return url.code }
        return 0
    }
    static func userMessage(_ error: Error) -> String {
        let category = category(error)
        if category == "network_access_blocked" { return "Your system blocked this app's network access. Check your firewall, content filter or network security software rules, then try again." }
        if category == "dns_failure" { return "macOS could not resolve api-user.e2ro.com. Check your DNS or VPN connection and try again." }
        if category == "tls_failure" { return "macOS could not establish a trusted TLS connection to eero. Check this Mac's date, time and certificate trust settings in Keychain Access." }
        if category == "timeout" { return "The connection to eero timed out. Try again in a moment." }
        if category == "connection_refused" { return "The connection to eero was refused. Check whether your proxy or network filter is blocking this app." }
        if category == "network_unreachable" { return "macOS reports that eero's network is unreachable. Check your internet or VPN connection." }
        let code = nativeCode(error)
        return "The connection to eero failed" + (code == 0 ? "" : " (error " + String(code) + ")") + ". Use Check connection for details, then try again."
    }
    /// `--connection-test <output>`: one unauthenticated HTTPS request; never signs in or requests a code.
    static func run(_ output: String) -> Int {
        let done = DispatchSemaphore(value: 0)
        var failure: Error?
        Task.detached {
            do {
                let api = EeroApi(SessionStore((NSTemporaryDirectory() as NSString).appendingPathComponent("unused-eero-probe.dat")))
                try await api.checkConnection()
            } catch { failure = error }
            done.signal()
        }
        done.wait()
        let json: String
        if let e = failure {
            json = J.stringify(J.obj("ok", false, "category", category(e), "native_code", nativeCode(e), "exception_type", String(describing: type(of: e)), "message", userMessage(e)))
        } else {
            json = J.stringify(J.obj("ok", true, "endpoint", EeroApi.origin, "result", "HTTPS connection succeeded; eero returned the expected unauthenticated response. No login or verification-code request was made."))
        }
        try? json.write(toFile: output, atomically: true, encoding: .utf8)
        return failure == nil ? 0 : 1
    }
}
