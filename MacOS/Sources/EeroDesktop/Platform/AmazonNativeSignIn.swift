import AppKit
import Foundation
import SwiftUI
import WebKit

@MainActor
final class AmazonSignInState: ObservableObject {
    @Published var address = "www.amazon.com"
    @Published var notice = "Opening Amazon’s secure sign-in…"
}

struct AmazonSignInView: View {
    @ObservedObject var state: AmazonSignInState
    let webView: WKWebView
    let cancel: () -> Void
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text("Sign in with Amazon").font(.system(size: 24, weight: .semibold))
            Text("Sign in with the Amazon account linked to your eero account. This window closes after eero verifies your sign-in.")
                .font(.system(size: 13)).fixedSize(horizontal: false, vertical: true).padding(.vertical, 8)
            Text(state.address).font(.system(size: 13)).foregroundColor(.secondary).padding(.bottom, 12)
            WebViewHost(webView: webView).frame(maxWidth: .infinity, maxHeight: .infinity)
                .overlay(RoundedRectangle(cornerRadius: 4).strokeBorder(Color(nsColor: .separatorColor)))
            Text(state.notice).font(.system(size: 13)).fixedSize(horizontal: false, vertical: true).padding(.top, 12).padding(.bottom, 8)
                .accessibilityAddTraits(.updatesFrequently)
            Button("Cancel", action: cancel).keyboardShortcut(.cancelAction)
        }
        .padding(16)
        .frame(minWidth: 850, minHeight: 650)
    }
}

/// Amazon "Login with Amazon" PKCE flow in a private WebKit window (Windows: AmazonNativeWindow).
/// The protocol logic lives in AmazonNativeAttempt (Core); this window only drives the page.
@MainActor
final class AmazonNativeWindow: NSObject, WKNavigationDelegate, WKUIDelegate, NSWindowDelegate {
    static weak var current: AmazonNativeWindow?
    private static var live: [AmazonNativeWindow] = []
    private let attempt = AmazonNativeAttempt()
    let window: NSWindow
    let webView: WKWebView
    let state = AmazonSignInState()
    private let complete: (String) async throws -> Void
    private let report: (String, Any?) -> Void
    private var completing = false
    private(set) var closed = false

    init(_ complete: @escaping (String) async throws -> Void, _ report: @escaping (String, Any?) -> Void) {
        self.complete = complete; self.report = report
        let configuration = WKWebViewConfiguration()
        // A fresh private data store per attempt (Windows: a unique in-private profile).
        configuration.websiteDataStore = .nonPersistent()
        configuration.preferences.javaScriptCanOpenWindowsAutomatically = false
        webView = WKWebView(frame: .zero, configuration: configuration)
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1080, height: 820), styleMask: [.titled, .closable, .resizable, .miniaturizable], backing: .buffered, defer: false)
        super.init()
        window.title = "Sign in with Amazon · eero Desktop"
        window.minSize = NSSize(width: 850, height: 650)
        window.isReleasedWhenClosed = false
        webView.navigationDelegate = self; webView.uiDelegate = self
        window.contentView = NSHostingView(rootView: AmazonSignInView(state: state, webView: webView, cancel: { [weak self] in self?.close() }))
        window.delegate = self
    }

    func show(_ owner: NSWindow?) {
        AmazonNativeWindow.live.append(self); AmazonNativeWindow.current = self
        AuxiliaryWindows.adopt(window)
        if let frame = owner?.frame { window.setFrameOrigin(NSPoint(x: frame.midX - window.frame.width / 2, y: max(0, frame.midY - window.frame.height / 2))) } else { window.center() }
        window.makeKeyAndOrderFront(nil)
        initialize()
    }
    func activate() { window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true) }
    func close() { if !closed { window.close() } }

    private func initialize() {
        report("amazon_browser_ready", nil)
        guard let url = URL(string: attempt.signInUrl) else {
            state.notice = "Amazon sign-in could not start."; report("amazon_browser_error", J.obj("error_type", "UriFormatException")); return
        }
        webView.load(URLRequest(url: url))
    }

    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping @MainActor @Sendable (WKNavigationActionPolicy) -> Void) {
        guard let uri = navigationAction.request.url else { decisionHandler(.cancel); return }
        if !(navigationAction.targetFrame?.isMainFrame ?? true) {
            decisionHandler(uri.scheme?.lowercased() == "https" || uri.absoluteString == "about:blank" || uri.absoluteString == "about:srcdoc" ? .allow : .cancel); return
        }
        if AmazonNativeAttempt.returnAddress(uri) {
            decisionHandler(.cancel)
            if completing || closed { return }
            completing = true
            Task { @MainActor [weak self] in await self?.finish(uri) }
            return
        }
        if !AmazonNativeAttempt.navigation(uri.absoluteString) { decisionHandler(.cancel); state.notice = "Amazon requested an unsupported destination."; return }
        state.address = uri.host ?? ""
        decisionHandler(.allow)
    }

    private func finish(_ uri: URL) async {
        var phase = "amazon_token_exchange"
        do {
            report("amazon_code_returned", nil); state.notice = "Finishing Amazon sign-in…"
            var token: String? = try await attempt.exchange(uri)
            if closed { token = nil; return }
            report("amazon_token_received", nil)
            phase = "eero_account_verification"
            state.notice = "Verifying your eero account…"
            try await complete(token!); token = nil
            if !closed { close() }
        } catch is CancellationError {
        } catch {
            let failure = error as? ApiException
            let code = failure?.apiError ?? ""
            if !closed {
                state.notice = phase == "eero_account_verification" && code == "error.login.unknown"
                    ? "Amazon sign-in succeeded, but eero does not recognize this Amazon account for an existing eero login. Use your eero email or phone number. Amazon login can be managed in eero’s Account settings; no account was created or changed here."
                    : (phase == "amazon_token_exchange" ? "Amazon could not finish sign-in: " : "eero could not verify this Amazon sign-in: ") + MainWindow.message(error) + " Close this window to start a fresh sign-in."
            }
            report("amazon_signin_error", J.obj("phase", phase, "error_type", String(describing: type(of: error)), "http_code", failure?.code ?? 0, "api_error", Regex.isMatch(code, "^[a-zA-Z0-9_.-]{1,140}$") ? code : ""))
        }
        // Windows resets this flag only by closing; a failed attempt cannot be reused (the code was consumed).
    }

    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration, for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? {
        if let url = navigationAction.request.url, AmazonNativeAttempt.navigation(url.absoluteString) { webView.load(URLRequest(url: url)) }
        return nil
    }
    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        if completing || closed { return }
        state.notice = "Complete Amazon sign-in and any verification on the page above. The window closes only after eero verifies your account."
        report("amazon_page_loaded", nil)
    }
    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) { failed(error) }
    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) { failed(error) }
    private func failed(_ error: Error) {
        let ns = error as NSError
        if completing || closed || ns.code == NSURLErrorCancelled || ns.code == 102 && (ns.domain == WKErrorDomain || ns.domain == "WebKitErrorDomain") { return }
        state.notice = "The Amazon page could not load (" + ns.localizedDescription + ")."
        report("amazon_page_error", J.obj("browser_error", ns.domain + " " + String(ns.code)))
    }
    func webView(_ webView: WKWebView, runJavaScriptAlertPanelWithMessage message: String, initiatedByFrame frame: WKFrameInfo, completionHandler: @escaping @MainActor @Sendable () -> Void) {
        let alert = NSAlert(); alert.messageText = "Amazon"; alert.informativeText = message; alert.addButton(withTitle: "OK")
        alert.beginSheetModal(for: window) { _ in completionHandler() }
    }

    func windowWillClose(_ notification: Notification) {
        if closed { return }
        closed = true
        webView.stopLoading(); webView.navigationDelegate = nil; webView.uiDelegate = nil
        AmazonNativeWindow.live.removeAll { $0 === self }
    }
}

extension MainWindow {
    func amazonConsumerSignIn() {
        if let existing = AmazonNativeWindow.current, !existing.closed { existing.activate(); return }
        let window = AmazonNativeWindow({ [unowned self] token in
            try await self.completeExternalSignIn {
                let remember = self.api.remember
                var success = false
                defer { if !success { self.api.remember = remember } }
                self.api.remember = self.rememberExternalSignIn
                _ = try await self.api.amazonSignIn(token); success = true; self.reportStatus("amazon_authenticated")
                // clearData() closes this sign-in window; finish loading outside the window's lifetime.
                try await Task { @MainActor in
                    self.clearData(); try await self.loadAccount(); self.showShell(); self.setNotice("Signed in with Amazon."); self.reportStatus("dashboard_loaded")
                }.value
            }
        }, { [weak self] state, details in
            guard let statusPath = self?.statusPath, !statusPath.isEmpty else { return }
            let json = J.stringify(J.obj("state", state, "time_utc", Dates.roundTrip(Date()), "details", details))
            try? json.write(toFile: statusPath + ".amazon-native.json", atomically: true, encoding: .utf8)
            let history = statusPath + ".amazon-native.history.jsonl"
            let size = (try? FileManager.default.attributesOfItem(atPath: history)[.size] as? Int) ?? 0
            if size < 65536 {
                if let handle = FileHandle(forWritingAtPath: history) { handle.seekToEndOfFile(); handle.write(Data((json + "\n").utf8)); handle.closeFile() }
                else { try? (json + "\n").write(toFile: history, atomically: true, encoding: .utf8) }
            }
        })
        window.show(self.window)
    }
}
