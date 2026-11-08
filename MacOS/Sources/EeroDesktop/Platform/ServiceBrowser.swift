import AppKit
import Foundation
import SwiftUI
import WebKit

/// Allow-list and helpers for the private service browser (Windows: ServiceLinks in ServiceBrowser.cs).
enum ServiceLinks {
    static let domains = ["eero.com", "e2ro.com", "amazon.com", "alexa.com", "amazon.co.uk", "amazon.de", "amazon.fr", "amazon.it", "amazon.es", "amazon.ca", "amazon.com.au", "amazon.co.jp", "amazon.in", "amazon.com.mx", "amazon.com.br", "guardianapp.com", "1password.com", "malwarebytes.com", "stripe.com", "paypal.com", "eero.zendesk.com"]

    /// Parses an absolute https URL on the standard port without user info (C# `Uri.TryCreate` + the shared checks).
    /// Hosts are lower-cased and limited to plain DNS characters so look-alike encodings never pass.
    static func secure(_ value: String?) -> URLComponents? {
        guard let value = value, !value.isEmpty, !value.unicodeScalars.contains(where: { $0.value <= 32 || $0.value == 127 }),
              let c = URLComponents(string: value), c.scheme?.lowercased() == "https",
              c.port == nil || c.port == 443, c.user == nil, c.password == nil,
              let host = c.host?.lowercased(), !host.isEmpty, Regex.isMatch(host, "^[a-z0-9.-]+$"), !host.hasPrefix("."), !host.hasSuffix(".")
        else { return nil }
        var normalized = c; normalized.host = host; normalized.scheme = "https"
        return normalized
    }
    static func onDomain(_ host: String, _ domain: String) -> Bool { host == domain || host.hasSuffix("." + domain) }

    static func eero(_ value: String) throws -> URL {
        guard let c = secure(value), let host = c.host, onDomain(host, "eero.com"), let url = c.url
        else { throw AppError.invalidData("The service did not return a secure eero web address.") }
        return url
    }
    static func tokenHeader(_ token: String?) throws -> String {
        guard let token = token, !token.isEmpty, token.utf16.count <= 8192, !token.unicodeScalars.contains(where: { $0.value < 33 || $0.value > 126 })
        else { throw AppError.invalidData("The web service did not return a valid temporary sign-in token.") }
        return "x-temp-user-token: " + token + "\r\n"
    }
    /// Same text as C# `Uri.AbsoluteUri` for the comparisons below (lower-case host, "/" for an empty path, no default port).
    static func absolute(_ c: URLComponents) -> String {
        var n = c; n.port = nil
        if n.percentEncodedPath.isEmpty { n.percentEncodedPath = "/" }
        return n.string ?? ""
    }
    static func keepTemporaryHeader(_ initial: String, _ request: String) -> Bool {
        guard (try? eero(initial)) != nil, let target = secure(initial), let resource = secure(request) else { return false }
        return absolute(target) == absolute(resource)
    }
    static func allowed(_ value: String) -> Bool {
        if value == "about:blank" { return true }
        guard let host = secure(value)?.host else { return false }
        return domains.contains { onDomain(host, $0) }
    }
    /// Private (non-persistent) WebKit sessions keep nothing on disk; this folder is reserved for browser data like Windows.
    static var profileDirectory: String { (AppPaths.dataDirectory as NSString).appendingPathComponent("WebServices") }
    static func providerNavigation(_ value: String) -> Bool { secure(value) != nil }
    /// Parses "Name: value\r\n" header text (the Windows header string) into request headers.
    static func headerFields(_ text: String?) -> [String: String]? {
        guard let text = text, !text.isEmpty else { return nil }
        var fields: [String: String] = [:]
        for line in text.components(separatedBy: "\r\n") where !line.isEmpty {
            guard let colon = line.firstIndex(of: ":") else { continue }
            fields[String(line[..<colon]).trim()] = String(line[line.index(after: colon)...]).trim()
        }
        return fields.isEmpty ? nil : fields
    }
    /// WebKit ships with macOS, so there is no runtime to install (Windows checks the WebView2 Runtime).
    static var webKitVersion: String? {
        let bundle = Bundle(for: WKWebView.self)
        let short = bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String
        let build = bundle.object(forInfoDictionaryKey: "CFBundleVersion") as? String
        if let s = short, let b = build, s != b { return s + " (" + b + ")" }
        return short ?? build
    }
    static func checkRuntime(_ output: String) -> Int {
        guard let version = webKitVersion, !version.isEmpty else {
            try? J.stringify(J.obj("ok", false, "error", "WebKitUnavailable")).write(toFile: output, atomically: true, encoding: .utf8); return 1
        }
        let os = ProcessInfo.processInfo.operatingSystemVersion
        let json = J.stringify(J.obj("ok", true, "webkit_version", version, "macos_version", "\(os.majorVersion).\(os.minorVersion).\(os.patchVersion)", "private_data_store", true))
        do { try json.write(toFile: output, atomically: true, encoding: .utf8); return 0 } catch { return 1 }
    }
}

@MainActor
final class ServiceBrowserState: ObservableObject {
    @Published var address = ""
    @Published var notice = "Opening secure service…"
    @Published var importEnabled = false
    @Published var importing = false
}

struct WebViewHost: NSViewRepresentable {
    let webView: WKWebView
    func makeNSView(context: Context) -> WKWebView { webView }
    func updateNSView(_ nsView: WKWebView, context: Context) {}
}

struct ServiceBrowserView: View {
    @ObservedObject var state: ServiceBrowserState
    let webView: WKWebView
    let showImport: Bool
    let onImport: () -> Void
    var body: some View {
        VStack(spacing: 0) {
            VStack(alignment: .leading, spacing: 8) {
                Text(state.address).font(.system(size: 17, weight: .semibold)).lineLimit(1).truncationMode(.middle).textSelection(.enabled)
                if showImport {
                    Text("Sign in yourself on eero's website. Use this sign-in imports only a verified eero API session from this private app browser.")
                        .font(.system(size: 13)).fixedSize(horizontal: false, vertical: true)
                    Button("Use this sign-in", action: onImport).disabled(!state.importEnabled || state.importing)
                }
            }.padding(18).frame(maxWidth: .infinity, alignment: .leading)
            Divider()
            WebViewHost(webView: webView).frame(maxWidth: .infinity, maxHeight: .infinity)
            Divider()
            Text(state.notice).font(.system(size: 13)).fixedSize(horizontal: false, vertical: true)
                .padding(EdgeInsets(top: 10, leading: 18, bottom: 16, trailing: 18)).frame(maxWidth: .infinity, alignment: .leading)
                .accessibilityAddTraits(.updatesFrequently)
        }
        .frame(minWidth: 850, minHeight: 650)
    }
}

/// A private WebKit window for eero and partner websites (Windows: ServiceBrowserWindow on WebView2).
/// Uses a non-persistent website data store, so cookies and storage disappear when the window closes.
@MainActor
final class ServiceBrowserWindow: NSObject, WKNavigationDelegate, WKUIDelegate, NSWindowDelegate {
    private static var live: [ServiceBrowserWindow] = []
    static let sampleHtml = "<html><body style='background:#101a2b;color:#e5ecf8;font:18px -apple-system,Helvetica;padding:40px'><h1>Mac service browser</h1><p>WebKit is running. This sample uses no account or network request.</p></body></html>"

    let window: NSWindow
    let webView: WKWebView
    let state = ServiceBrowserState()
    weak var owner: MainWindow?
    private let target: String?
    private let headers: [String: String]?
    private let sample: Bool
    private let importSession: ((String) async throws -> Void)?
    private let providerNavigation: Bool
    private let callbackMatch: ((URL) -> Bool)?
    private let callback: ((URL) async throws -> Void)?
    private var completing = false
    private var work: [Task<Void, Never>] = []
    private(set) var closed = false
    /// Runs once when the window closes (Windows: `Closed` event).
    var onClose: (() -> Void)?
    /// True once the window has closed (Windows: LifetimeToken). Running callbacks are not cancelled, because sign-in
    /// callbacks call clearData(), which closes this window before they load the account; check this flag instead.
    var lifetimeCancelled: Bool { closed }

    private init(_ owner: MainWindow, _ title: String, _ url: String?, _ headers: [String: String]?, _ importSession: ((String) async throws -> Void)?, _ preview: Bool,
                 _ callbackMatch: ((URL) -> Bool)?, _ callback: ((URL) async throws -> Void)?, _ providerNavigation: Bool) {
        self.owner = owner; target = url; self.headers = headers; self.importSession = importSession; sample = preview
        self.callbackMatch = callbackMatch; self.callback = callback; self.providerNavigation = providerNavigation
        let configuration = WKWebViewConfiguration()
        configuration.websiteDataStore = .nonPersistent()
        configuration.preferences.javaScriptCanOpenWindowsAutomatically = false
        webView = WKWebView(frame: .zero, configuration: configuration)
        webView.allowsBackForwardNavigationGestures = true
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1080, height: 800), styleMask: [.titled, .closable, .resizable, .miniaturizable], backing: .buffered, defer: false)
        super.init()
        window.title = title + " · eero Desktop"
        window.minSize = NSSize(width: 850, height: 650)
        window.isReleasedWhenClosed = false
        state.address = preview ? "Sample service window" : (url.flatMap { URLComponents(string: $0)?.host } ?? "")
        webView.navigationDelegate = self; webView.uiDelegate = self
        window.contentView = NSHostingView(rootView: ServiceBrowserView(state: state, webView: webView, showImport: importSession != nil, onImport: { [weak self] in self?.importClick() }))
        window.delegate = self
    }

    /// Same parameters as the Windows constructor: `headers` is the extra-header text ("Name: value" lines, e.g.
    /// ServiceLinks.tokenHeader), `importSession` receives the verified-domain eero `s` cookie when the person chooses
    /// Use this sign-in. Pass "" as the URL for the local preview sample.
    @discardableResult
    static func open(_ owner: MainWindow, _ title: String, _ url: String, _ headers: String? = nil, _ importSession: ((String) async throws -> Void)? = nil, _ preview: Bool = false,
                     _ callbackMatch: ((URL) -> Bool)? = nil, _ callback: ((URL) async throws -> Void)? = nil, _ providerNavigation: Bool = false) -> ServiceBrowserWindow {
        let w = ServiceBrowserWindow(owner, title, url, ServiceLinks.headerFields(headers), importSession, preview, callbackMatch, callback, providerNavigation)
        live.append(w)
        AuxiliaryWindows.adopt(w.window)
        if let frame = owner.window?.frame {
            w.window.setFrameOrigin(NSPoint(x: frame.midX - w.window.frame.width / 2, y: max(0, frame.midY - w.window.frame.height / 2)))
        } else { w.window.center() }
        w.window.makeKeyAndOrderFront(nil)
        w.initialize()
        return w
    }

    func close() { if !closed { window.close() } }
    func activate() { window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true) }

    private func initialize() {
        do {
            if sample { webView.loadHTMLString(ServiceBrowserWindow.sampleHtml, baseURL: nil); return }
            guard let target = target, !target.isEmpty, let url = URL(string: target) else { throw AppError.invalidData("The service did not return a valid web address.") }
            var request = URLRequest(url: url)
            if let headers = headers {
                _ = try ServiceLinks.eero(target)
                for (name, value) in headers { request.setValue(value, forHTTPHeaderField: name) }
            }
            webView.load(request)
        } catch { state.notice = "The secure browser could not start: " + MainWindow.message(error) }
    }

    private func spawn(_ body: @escaping @MainActor () async -> Void) {
        let t = Task { @MainActor in await body() }
        work.append(t)
    }

    // MARK: Navigation filtering (Windows: NavigationStarting / WebResourceRequested)
    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping @MainActor @Sendable (WKNavigationActionPolicy) -> Void) {
        guard let url = navigationAction.request.url else { decisionHandler(.cancel); return }
        let text = url.absoluteString
        let mainFrame = navigationAction.targetFrame?.isMainFrame ?? true
        if !mainFrame {
            // Embedded frames (3-D Secure, captcha, payment fields) may load any secure page, like WebView2 frames.
            let scheme = url.scheme?.lowercased() ?? ""
            decisionHandler(scheme == "https" || text == "about:blank" || text == "about:srcdoc" ? .allow : .cancel); return
        }
        if sample && text == "about:blank" { decisionHandler(.allow); return }
        if let match = callbackMatch, match(url) {
            decisionHandler(.cancel)
            if completing || closed { return }
            completing = true
            spawn { [weak self] in
                guard let self = self else { return }
                defer { self.completing = false }
                do {
                    self.state.notice = "Verifying the service response…"
                    if let callback = self.callback { try await callback(url) }
                    if !self.closed { self.close() }
                } catch { if !self.closed { self.state.notice = MainWindow.message(error) } }
            }
            return
        }
        if !(ServiceLinks.allowed(text) || providerNavigation && ServiceLinks.providerNavigation(text)) {
            decisionHandler(.cancel); state.notice = "This destination is outside the supported provider websites."; return
        }
        // The temporary eero token header is sent only to the exact page it was issued for.
        if navigationAction.request.value(forHTTPHeaderField: "x-temp-user-token") != nil && !ServiceLinks.keepTemporaryHeader(target ?? "", text) {
            decisionHandler(.cancel); webView.load(URLRequest(url: url)); return
        }
        if url.scheme?.lowercased() == "https", let host = url.host { state.address = host + (url.path.isEmpty ? "/" : url.path) }
        decisionHandler(.allow)
    }
    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration, for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
        let url = navigationAction.request.url
        let text = url?.absoluteString ?? ""
        if ServiceLinks.allowed(text) || providerNavigation && ServiceLinks.providerNavigation(text) || (url != nil && callbackMatch?(url!) == true) { webView.load(URLRequest(url: url!)) }
        else { state.notice = "The provider requested a destination outside this service window." }
        return nil
    }
    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        if closed { return }
        state.notice = sample ? "Local sample loaded. No account or network request." : "Secure provider service. Complete any sign-in or purchase yourself."
        state.importEnabled = true
    }
    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) { failed(error) }
    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) { failed(error) }
    private func failed(_ error: Error) {
        let ns = error as NSError
        // Cancelled loads (blocked destinations, redirects re-issued without the token) are not failures.
        if closed || ns.code == NSURLErrorCancelled || (ns.domain == WKErrorDomain && ns.code == 102) || ns.domain == "WebKitErrorDomain" && ns.code == 102 { return }
        state.notice = "The provider page did not finish loading (" + ns.localizedDescription + ")."
        state.importEnabled = false
    }
    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) { if !closed { state.notice = "The provider page stopped unexpectedly. Close this window and try again." } }

    // MARK: JavaScript dialogs (payment and sign-in pages use them)
    func webView(_ webView: WKWebView, runJavaScriptAlertPanelWithMessage message: String, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping @MainActor @Sendable () -> Void) {
        let alert = NSAlert(); alert.messageText = frame.request.url?.host ?? "Provider"; alert.informativeText = message; alert.addButton(withTitle: "OK")
        alert.beginSheetModal(for: window) { _ in completionHandler() }
    }
    func webView(_ webView: WKWebView, runJavaScriptConfirmPanelWithMessage message: String, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping @MainActor @Sendable (Bool) -> Void) {
        let alert = NSAlert(); alert.messageText = frame.request.url?.host ?? "Provider"; alert.informativeText = message
        alert.addButton(withTitle: "OK"); alert.addButton(withTitle: "Cancel")
        alert.beginSheetModal(for: window) { completionHandler($0 == .alertFirstButtonReturn) }
    }

    // MARK: Session import (Windows: ImportClick)
    private func importClick() {
        if state.importing { return }
        state.importing = true; state.importEnabled = false
        let store = webView.configuration.websiteDataStore.httpCookieStore
        spawn { [weak self] in
            guard let self = self else { return }
            defer { self.state.importing = false; if !self.closed { self.state.importEnabled = true } }
            do {
                let cookies: [HTTPCookie] = await withCheckedContinuation { c in store.getAllCookies { c.resume(returning: $0) } }
                let session = cookies.first { c in
                    let domain = c.domain.trimStart(".").lowercased()
                    return c.name == "s" && c.isSecure && (domain == "api-user.e2ro.com" || domain == "e2ro.com")
                }
                guard let value = session?.value else {
                    self.state.notice = "This website has not provided a reusable eero API session. Finish signing in, or use email or phone verification in eero Desktop."; return
                }
                try Task.checkCancellation()
                if let handler = self.importSession { try await handler(value) }
                if !self.closed { self.close() }
            } catch { if !self.closed { self.state.notice = MainWindow.message(error) } }
        }
    }

    func windowWillClose(_ notification: Notification) {
        if closed { return }
        closed = true
        work.removeAll()
        webView.stopLoading(); webView.navigationDelegate = nil; webView.uiDelegate = nil
        let handler = onClose; onClose = nil
        ServiceBrowserWindow.live.removeAll { $0 === self }
        handler?()
    }
}

extension MainWindow {
    func openServiceWindow(_ title: String, _ url: String, _ headers: String? = nil) throws {
        if !ServiceLinks.allowed(url) { throw AppError.invalidOperation("The service returned an unsupported provider website.") }
        ServiceBrowserWindow.open(self, title, url, headers)
    }
    func openPaymentService(_ targetPage: String) async throws {
        if preview { throw AppError.invalidOperation("Sign in to manage a subscription.") }
        let session = try await api.send("GET", "/2.2/login/temporary?client_type=WEB_PAYMENTS&target_page=" + targetPage.escapedData)
        let url = J.s(session, "target_url"); _ = try ServiceLinks.eero(url)
        try openBillingWindow(url, try ServiceLinks.tokenHeader(J.s(session, "temporary_token")))
    }
}
