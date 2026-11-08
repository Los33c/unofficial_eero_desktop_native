import Foundation

// Translated from source/SignInServices.cs (MainWindow part; SignInContracts and SignInService live in Core)
// plus WebsiteSignIn from source/ServiceBrowser.cs.
extension MainWindow {
    /// The login screen's Remember me switch, when the login view publishes it; Windows uses `rememberBox == null || IsChecked`.
    var rememberExternalSignIn: Bool { (state["rememberSignIn"] as? Bool) ?? true }

    func completeExternalSignIn(_ action: () async throws -> Void) async throws {
        if busy { throw AppError.invalidOperation("Another sign-in operation is in progress. Wait for it to finish.") }
        busy = true
        defer { busy = false }
        try await action()
    }

    func providerSignIn() async throws {
        let data = try await SignInService().providers()
        var choices: [String: String] = [:]
        for provider in J.list(data) { let name = J.s(provider, "name"), id = J.s(provider, "idp"); if !name.isEmpty && !id.isEmpty { choices[name] = id } }
        if choices.isEmpty { throw AppError.invalidOperation("eero did not return any sign-in providers.") }
        let names = choices.keys.sorted { $0.localizedCompare($1) == .orderedAscending }
        edit("Sign in through your internet provider", [Field("provider", "Provider", names[0], "choice", choices: names)], { [self] values in
            api.remember = rememberExternalSignIn
            var browser: ServiceBrowserWindow? = nil
            let callback: (URL) async throws -> Void = { [self] uri in
                try await completeExternalSignIn({
                    let query = try SignInContracts.parameters(uri.query ?? "")
                    guard let candidate = query["token"], !candidate.isEmpty else { throw AppError.invalidOperation("The provider did not return an eero sign-in token.") }
                    let identity = try await self.api.inspectBrowserSession(candidate); let verified = identity.account
                    if browser?.lifetimeCancelled == true { throw CancellationError() }
                    if !(await self.confirm("Continue as " + J.first(verified, "name", "email", "phone") + "?\n" + J.first(verified, "email", "phone"))) { throw AppError.invalidOperation("Sign-in was cancelled.") }
                    if browser?.lifetimeCancelled == true { throw CancellationError() }
                    try await self.api.importBrowserSession(identity.sessionCredential); self.clearData(); try await self.loadAccount(); self.showShell(); self.setNotice("Provider sign-in verified with eero.")
                })
            }
            let url = try SignInContracts.providerUrl(choices[v(values, "provider")] ?? "")
            browser = ServiceBrowserWindow.open(self, "Sign in with " + v(values, "provider"), url, nil, nil, false, SignInContracts.ssoReturn, callback, true)
        }, "Continue", "Choose the provider associated with your eero account. Credentials are entered only on its sign-in page.")
    }

    func websiteSignIn() {
        api.remember = rememberExternalSignIn
        var w: ServiceBrowserWindow? = nil
        w = ServiceBrowserWindow.open(self, "Sign in on eero's website", "https://account.eero.com/", nil, { [self] token in
            try await completeExternalSignIn({
                if w?.lifetimeCancelled == true { throw CancellationError() }
                try await self.api.importBrowserSession(token); self.clearData(); try await self.loadAccount(); self.showShell(); self.setNotice("Website sign-in verified with eero.")
            })
        })
    }
}
