import Foundation

// Translated from source/RegistrationFeatures.cs (Linux port: Linux/src/Features/RegistrationFeatures.cs).
extension MainWindow {
    /// Country chosen when account registration started (sent with the verification code).
    var registrationCountry: String {
        get { stored("registrationCountry", "") }
        set { state["registrationCountry"] = newValue }
    }
    /// Consent choices made on the registration form (sent with the verification code).
    var registrationConsents: JObject {
        get { stored("registrationConsents", JObject()) }
        set { state["registrationConsents"] = newValue }
    }

    func beginRegistrationUi() {
        edit("Create an eero account", [Field("country", "Two-letter country code", "US")], { [self] values in
            let country = v(values, "country").trim().uppercased()
            if !Regex.isMatch(country, "^[A-Z]{2}$") { throw AppError.argument("Enter a two-letter country code, such as US or CA.") }
            let consents = try await api.registrationConsents(country)
            queueRegistration(country, J.list(consents))
        }, "Continue", "Use this only if you do not already have an eero account. Phone/SMS verification is required.")
    }

    /// Opens the next form once the country dialog has closed (Windows: Dispatcher.BeginInvoke).
    func queueRegistration(_ country: String, _ consents: [Any]) {
        Task { @MainActor in self.registrationForm(country, consents) }
    }

    func registrationForm(_ country: String, _ agreements: [Any]) {
        var fields: [Field] = [Field("name", "Your name"), Field("email", "Email address"), Field("phone", "Phone number with country code"), Field("remember", "Remember me on this Mac", true, "bool")]
        fields.append(Field("legal_link", "Review eero terms & privacy", "https://www.eero.com/legal", "link"))
        for agreement in agreements {
            let key = J.s(agreement, "name"); if key.isEmpty { continue }
            fields.append(Field("consent:" + key, J.s(agreement, "localized_consent").nullIfEmpty ?? MainWindow.human(key), false, "bool"))
        }
        if agreements.isEmpty { fields.append(Field("legal", "I have reviewed and agree to eero's terms and privacy policy", false, "bool")) }
        edit("Account details and consent", fields, { [self] values in
            try MainWindow.validateName(v(values, "name")); let email = v(values, "email").trim()
            if !Regex.isMatch(email, "^[^\\s@]+@[^\\s@]+\\.[^\\s@]+$") { throw AppError.argument("Enter a valid email address.") }
            let choices = JObject()
            for agreement in agreements {
                let key = J.s(agreement, "name"); if key.isEmpty { continue }
                let consent = flag(values, "consent:" + key)
                if J.b(agreement, "requires_consent") && !consent { throw AppError.argument("A required consent has not been accepted. Review the agreement before continuing.") }
                choices[key] = consent
            }
            if agreements.isEmpty && !flag(values, "legal") { throw AppError.argument("Review and accept eero's terms and privacy policy before creating an account.") }
            if !(await confirm("Create a new eero account with these details and request a verification code?")) { return }
            api.remember = flag(values, "remember")
            try await api.beginRegistration(v(values, "name").trim(), email, try PhoneInput.normalize(v(values, "phone")))
            registrationCountry = country; registrationConsents = choices; resendAfter = Date().addingTimeInterval(60)
            showLogin(true); setNotice("Account creation started. Enter the verification code sent to your phone.")
        }, "Create account", "The displayed consent text comes from eero. Required consents must be explicitly accepted; optional consents start unchecked. Review eero's terms at https://www.eero.com/legal before continuing.")
    }
}
