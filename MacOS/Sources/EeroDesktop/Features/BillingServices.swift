import Foundation

// Translated from BillingServices.cs (BillingContracts itself lives in Core).
extension MainWindow {
    /// `header` is the raw "Name: value\r\n" block from ServiceLinks.tokenHeader, as on Windows.
    func openBillingWindow(_ url: String, _ header: String? = nil) throws {
        if !ServiceLinks.allowed(url) { throw AppError.invalidOperation("The billing service returned an unsupported provider address.") }
        let epoch = notificationEpoch
        let window = ServiceBrowserWindow.open(self, "eero billing", url, header, nil, false, BillingContracts.return, { _ in }, true)
        // Refresh the plan once the billing window goes away.
        window.onClose = { [weak self] in
            guard let self = self else { return }
            Task { @MainActor in
                if self.api.signedIn && self.notificationEpoch == epoch { await self.run({ try await self.refreshBilling() }, "Checking subscription status…") }
            }
        }
    }
    /// Splits "Name: value\r\n" header lines into fields (nil when there are none).
    static func headerFields(_ header: String?) -> [String: String]? {
        guard let header = header, !header.isEmpty else { return nil }
        var fields: [String: String] = [:]
        for line in header.components(separatedBy: "\r\n") where !line.isEmpty {
            guard let colon = line.firstIndex(of: ":") else { continue }
            fields[String(line[..<colon]).trim()] = String(line[line.index(after: colon)...]).trim()
        }
        return fields.isEmpty ? nil : fields
    }
    func refreshBilling() async throws {
        if preview { throw AppError.invalidOperation("Sign in to refresh subscription benefits.") }
        let epoch = notificationEpoch
        let customer = try await api.send("GET", "/2.2/premium/customer")
        if !api.signedIn || notificationEpoch != epoch { return }
        featureData["billing_customer"] = customer ?? JNull.shared
        if !(networkUrl ?? "").isEmpty { try await loadNetwork() }
        if api.signedIn && notificationEpoch == epoch { renderPage(); setNotice("Subscription status refreshed from eero. Purchase and cancellation status comes from the service, including changes made in the billing window.") }
    }
    func billingOverview() {
        sub("Your eero plan, how you pay for it and what you've bought.")
        var customer: Any? = featureData["billing_customer"]
        if customer is JNull || customer is Error { customer = nil }
        if preview { customer = J.obj("plan", "eero Plus", "status", "Active", "payment_channel", "eero") }
        let plan = J.first(customer, "plan", "plan_name", "name"), status = J.s(customer, "status"), channel = J.s(customer, "payment_channel")
        group("Your plan",
              settingRow("Plan", !status.isEmpty ? MainWindow.friendly(status) : nil, plan.nullIfEmpty ?? "No plan reported",
                         rowActionAsync("Manage ↗", { [unowned self] in try await self.openPaymentService("MANAGE_SUBSCRIPTION") })),
              !channel.isEmpty ? infoRow("Billed through", channel == "eero" ? "eero" : MainWindow.friendly(channel), channel == "eero" ? "Change your payment method with Manage." : "Manage this subscription with that store or provider.") : nil,
              settingRow("Refresh status", "Bought or cancelled something? Refresh to update what this app shows.", nil, rowActionAsync("Refresh", { [unowned self] in try await self.refreshBilling() })))
        group("Purchases",
              settingRow("Purchase history", "Receipts and past purchases. Opens eero's payment service.", nil, rowActionAsync("View ↗", { [unowned self] in try await self.openPaymentService("MANAGE_PURCHASES") })))
        relatedPagesFor("Settings/Account settings/Subscriptions/Offers & referrals")
        technicalDetails(customer)
    }
    func organizationBilling() {
        sub("The payment account for your eero business organization. You need an organization role that allows billing.")
        group(nil,
              settingRow("Manage payment account", "Update the payment method and details for an existing business account.", nil, rowActionAsync("Manage ↗", { [unowned self] in
                  let result = try await self.read("/2.2/organizations/self/payment_account/management_url")
                  try self.openBillingWindow(J.s(result, "external_payment_account_management_url"))
              })),
              settingRow("Set up a payment account", "Only if your organization doesn't have one yet. Opens the payment provider's sign-up form.", nil, rowActionAsync("Set up ↗", { [unowned self] in
                  if self.preview { throw AppError.invalidOperation("Sample mode does not create payment accounts.") }
                  if !(await self.confirm("Start payment-account setup for your eero business organization? Review the provider's requirements before submitting its form.")) { return }
                  let result = try await self.api.send("POST", "/2.2/organizations/self/payment_account")
                  try self.openBillingWindow(J.s(result, "external_payment_account_onboard_url"))
              })))
    }
}
