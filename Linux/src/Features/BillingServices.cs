using System;
using System.Linq;
using System.Threading.Tasks;
using Gtk;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private void OpenBillingWindow(string url, string header = null)
        {
            if (!ServiceLinks.Allowed(url)) throw new InvalidOperationException("The billing service returned an unsupported provider address.");
            int epoch = notificationEpoch;
            var window = new ServiceBrowserWindow(this, "eero billing", url, header, null, false, BillingContracts.Return, async uri => { await Task.FromResult(0); }, true);
            // Refresh the plan once the billing window goes away.
            window.Window.OnDestroy += async delegate
            {
                if (api.SignedIn && notificationEpoch == epoch) await Run(RefreshBilling, "Checking subscription status…");
            };
            window.Present();
        }
        private async Task RefreshBilling()
        {
            if (preview) throw new InvalidOperationException("Sign in to refresh subscription benefits.");
            int epoch = notificationEpoch;
            object customer = await api.Send("GET", "/2.2/premium/customer");
            if (!api.SignedIn || notificationEpoch != epoch) return;
            featureData["billing_customer"] = customer;
            if (!String.IsNullOrEmpty(networkUrl)) await LoadNetwork();
            if (api.SignedIn && notificationEpoch == epoch) { RenderPage(); SetNotice("Subscription status refreshed from eero. Purchase and cancellation status comes from the service, including changes made in the billing window."); }
        }
        private void BillingOverview()
        {
            Sub("Your eero plan, how you pay for it and what you've bought.");
            object customer; featureData.TryGetValue("billing_customer", out customer);
            if (preview) customer = J.Obj("plan", "eero Plus", "status", "Active", "payment_channel", "eero");
            string plan = J.First(customer, "plan", "plan_name", "name"), status = J.S(customer, "status"), channel = J.S(customer, "payment_channel");
            Group("Your plan",
                SettingRow("Plan", status.Length > 0 ? Friendly(status) : null, plan.NullIfEmpty() ?? "No plan reported",
                    RowActionAsync("Manage ↗", async delegate { await OpenPaymentService("MANAGE_SUBSCRIPTION"); })),
                channel.Length > 0 ? InfoRow("Billed through", channel == "eero" ? "eero" : Friendly(channel), channel == "eero" ? "Change your payment method with Manage." : "Manage this subscription with that store or provider.") : null,
                SettingRow("Refresh status", "Bought or cancelled something? Refresh to update what this app shows.", null, RowActionAsync("Refresh", RefreshBilling)));
            Group("Purchases",
                SettingRow("Purchase history", "Receipts and past purchases. Opens eero's payment service.", null, RowActionAsync("View ↗", async delegate { await OpenPaymentService("MANAGE_PURCHASES"); })));
            RelatedPagesFor("Settings/Account settings/Subscriptions/Offers & referrals");
            TechnicalDetails(customer);
        }
        private void OrganizationBilling()
        {
            Sub("The payment account for your eero business organization. You need an organization role that allows billing.");
            Group(null,
                SettingRow("Manage payment account", "Update the payment method and details for an existing business account.", null, RowActionAsync("Manage ↗", async delegate
                {
                    object result = await Read("/2.2/organizations/self/payment_account/management_url");
                    OpenBillingWindow(J.S(result, "external_payment_account_management_url"));
                })),
                SettingRow("Set up a payment account", "Only if your organization doesn't have one yet. Opens the payment provider's sign-up form.", null, RowActionAsync("Set up ↗", async delegate
                {
                    if (preview) throw new InvalidOperationException("Sample mode does not create payment accounts.");
                    if (!await Confirm("Start payment-account setup for your eero business organization? Review the provider's requirements before submitting its form.")) return;
                    object result = await api.Send("POST", "/2.2/organizations/self/payment_account");
                    OpenBillingWindow(J.S(result, "external_payment_account_onboard_url"));
                })));
        }
    }
}
