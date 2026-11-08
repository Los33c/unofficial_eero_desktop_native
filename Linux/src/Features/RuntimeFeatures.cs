using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gtk;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private void IncomingAccessPage(object data)
        {
            Sub("Networks that invited you to help manage them.");
            var invites = Entries(data, "promotions"); var rows = new List<Widget>();
            foreach (object raw in invites)
            {
                object invite = raw; string id = J.S(invite, "invite_id"), net = J.S(invite, "network_id"), role = J.S(invite, "invite_role");
                string name = J.S(invite, "network_name").NullIfEmpty() ?? "A network";
                var actions = new List<Button>();
                if (id.Length > 0 && net.Length > 0)
                    foreach (bool decision in new[] { true, false })
                    {
                        bool accept = decision;
                        var b = RowActionAsync(accept ? "Accept" : "Decline", async delegate
                        {
                            if (!await Confirm((accept ? "Accept" : "Decline") + " this access invitation for " + name + "?")) return;
                            await Change("POST", "/2.2/networks/" + ExtendedContracts.Identifier(net) + "/invites/response", RuntimeContracts.InvitationResponse(id, accept), false, accept ? "Access invitation accepted." : "Access invitation declined.");
                        });
                        if (accept) Accent(b);
                        if (accept) CompactAction(b);
                        actions.Add(b);
                    }
                rows.Add(SettingRow(name, role == "admin" ? "Administrator: can change every setting on this network." : "Access: " + Friendly(role), null, actions.ToArray()));
            }
            if (rows.Count == 0) rows.Add(InfoRow("No invitations", null, "When someone invites you to manage their network, it shows up here."));
            Group(null, rows.ToArray());
        }
        private void AutomaticSetupPage(object data)
        {
            Sub("New eeros you plug in can join this network by themselves. eero finds them through its cloud service.");
            string current = J.S(network, "auto_setup_mode");
            string[] modes = { "DEFAULT", "ZTS", "LTS", "OFF" }, labels = { "Provider default", "ZTS policy", "LTS policy", "Off" };
            int index = Array.IndexOf(modes, current);
            Group(null, SettingRow("Automatic setup", "Provider default suits most homes. ZTS and LTS are eero's automatic setup policies; use one only if your provider asks. Off means you add every eero yourself.", index >= 0 ? labels[index] : "Not reported", RowAction("Change", delegate
            {
                Edit("Automatic setup", new[] { new Field("mode", "Mode", index >= 0 ? labels[index] : labels[0], "choice", labels).Help("If you're unsure, choose Provider default.") }, async v =>
                {
                    string chosen = modes[Array.IndexOf(labels, V(v, "mode"))];
                    if (!await Confirm("Change automatic setup to " + V(v, "mode") + "?")) return;
                    await Change("PUT", FeatureUrl("auto_setup_mode"), RuntimeContracts.AutoSetup(chosen), false, "Automatic setup set to " + V(v, "mode") + ".");
                });
            })));
            var rows = new List<Widget>();
            foreach (object raw in Entries(data, "eeros"))
            {
                object eero = raw; string serial = J.S(eero, "serial");
                string reason = J.First(J.Get(eero, "ineligible_for_zts"), "reason", "message");
                rows.Add(SettingRow(J.First(eero, "model", "serial").NullIfEmpty() ?? "eero", "Serial " + serial + (reason.Length > 0 ? " · Can't join automatically: " + reason : ""), null,
                    serial.Length > 0 ? RowAction("Add", delegate { AddDiscoveredEero(serial); }) : null));
            }
            if (rows.Count == 0) rows.Add(InfoRow("No new eeros found", null, "Plug in the new eero and wait a few minutes. You can also search with Bluetooth under Add or replace eero."));
            Group("eeros waiting to join", rows.ToArray());
        }
        private void AddDiscoveredEero(string serial)
        {
            Edit("Add discovered eero", new[] { new Field("serial", "Serial number", serial), new Field("location", "Location") }, async v =>
            {
                string validated = RuntimeContracts.Serial(V(v, "serial")); ValidateName(V(v, "location"));
                if (!await Confirm("Add eero " + validated + " to the selected network?")) return;
                await Change("POST", "/2.2/eeros?requireSession=true", J.Obj("serial", validated, "location", V(v, "location").Trim(), "network", networkUrl), true, "eero addition requested.");
            }, "Add eero");
        }
        private void AccessoriesPage(object status)
        {
            Sub("Add-on hardware such as cellular backup and phone adapters connected to your eeros.");
            var accessories = J.List(J.Get(network, "accessories"));
            foreach (object raw in accessories)
            {
                object accessory = raw; string serial = J.First(accessory, "dsn", "serial"), eeroId = J.S(accessory, "eero_id");
                var host = nodes.FirstOrDefault(n => J.S(n, "url").TrimEnd('/').Split('/').Last() == eeroId);
                string state = J.B(accessory, "joined") ? "Connected" : J.B(accessory, "registered") ? "Registered, not connected" : "Not set up";
                var header = HBox(20);
                var photo = HardwareImage(accessory, 64); photo.SetSizeRequest(64, 64); photo.SetHalign(Gtk.Align.Start); photo.SetValign(Gtk.Align.Center); header.Append(photo);
                var words = VBox(); words.SetValign(Gtk.Align.Center); words.SetHexpand(true); var title = Text(J.First(accessory, "name", "model"), 18, "#17263C", true); title.SetMarginBottom(0); words.Append(title);
                words.Append(Text(J.S(accessory, "model") + (host != null ? " · Connected to " + J.First(host, "location", "model") : ""), 12, "#64738A")); header.Append(words);
                Group(null, header,
                    InfoRow("Status", state),
                    serial.Length > 0 ? SettingRow("Serial number", null, serial, RowActionAsync("Details", async delegate { Info("Accessory", await Read(FeatureUrl("accessories/" + Uri.EscapeDataString(RuntimeContracts.Serial(serial))))); })) : null,
                    eeroId.Length > 0 ? SettingRow("Phone lines", "Phone numbers linked to this accessory, if it's a phone adapter.", null, RowActionAsync("View", async delegate { Info("Phone lines", await Read(FeatureUrl("sip_config_associations/" + ExtendedContracts.Identifier(eeroId)))); })) : null,
                    serial.Length > 0 ? SettingRow("Remove accessory", "Its cellular backup or phone service may stop working.", null, RowAction("Remove", delegate { Edit("Remove accessory", new[] { new Field("serial", "Type the serial number to confirm").Help("Serial: " + serial) }, async v =>
                    {
                        if (V(v, "serial").Trim() != serial) throw new ArgumentException("The accessory serial number does not match.");
                        if (!await Confirm("Remove this accessory? Its cellular backup or phone service may stop working.")) return;
                        await Change("DELETE", FeatureUrl("accessories/" + Uri.EscapeDataString(RuntimeContracts.Serial(serial))), null, false, "Accessory removed.");
                    }, "Remove accessory"); }, true)) : null);
            }
            if (accessories.Count == 0) Group(null, InfoRow("No accessories", null, "No add-on hardware is registered on this network."));
            object cellular = J.Get(network, "cellular_backup"); if (cellular != null) { AddGroupHeading(page, "Cellular backup"); ObjectCards(cellular); }
            TechnicalDetails(status);
            page.Append(Text("Activating new cellular service or pairing new accessories still happens through your provider's setup.", 13, "#64738A"));
        }
        private void PlacementPage()
        {
            Sub("Check whether each eero is in a good spot. Run a test, wait a minute, then view the results.");
            var rows = new List<Widget>();
            foreach (object raw in nodes)
            {
                object node = raw; string serial = J.S(node, "serial"); if (serial.Length == 0) continue;
                string url = "/2.3/networks/" + NetworkId + "/placement_test/" + Uri.EscapeDataString(RuntimeContracts.Serial(serial));
                rows.Add(SettingRow(J.First(node, "location", "model"), (J.B(node, "gateway") ? "Gateway · " : "") + J.S(node, "model"), null,
                    RowActionAsync("Run test", async delegate
                    {
                        if (!await Confirm("Run a placement test for " + J.First(node, "location", "model") + "?")) return;
                        await Change("POST", url, null, false, "Placement test started. Choose View results in a minute.");
                    }),
                    RowActionAsync("View results", async delegate { Info("Placement results", await Read(url)); })));
            }
            if (rows.Count == 0) rows.Add(InfoRow("No eeros to test", null, "eero didn't return serial numbers for your eeros."));
            Group(null, rows.ToArray());
        }
        private void VpnPage()
        {
            Sub("A VPN from Guardian that protects this computer on any network, such as public wifi. It must be included with your eero plan.");
            object availability; featureData.TryGetValue("guardian_availability", out availability);
            Group("Eligibility", SettingRow("VPN included with your plan?", VpnContracts.EligibilityMessage(availability), null,
                RowActionAsync("Check", async delegate { object result = await LoadGuardianAvailability(); RenderPage(); SetNotice(VpnContracts.EligibilityMessage(result), result is ApiException); })));
            VpnControls();
            Group("Guardian account & help",
                SettingRow("Guardian sign-in details", "Your login for Guardian's own app or website.", null, RowActionAsync("Show", GuardianCredentials)),
                SettingRow("Guardian account", "Activate or manage your Guardian account on their website.", null, RowAction("Open ↗", delegate { OpenServiceWindow("Guardian VPN", "https://guardianapp.com/login?connect-api-env=wifi-company-prod"); })),
                SettingRow("Guardian's own apps", "Prefer Guardian's own app on your other devices? Get it from Guardian's website.", null, RowAction("Open ↗", delegate { Official("https://guardianapp.com/"); })),
                SettingRow("Setup instructions", "eero's step-by-step guide to the VPN.", null, RowAction("Open ↗", delegate { OpenServiceWindow("Guardian setup", "https://eero.com/support/articles/how-do-i-set-up-vpn-powered-by-guardian"); })));
        }
        private void SubscriptionWebPage()
        {
            Sub("These open eero's payment service in a separate window, signed in for you. Check prices and terms there before you buy or cancel anything.");
            Group("Bought through eero",
                SettingRow("Subscription", "Change, renew or cancel your eero plan and payment method.", null, RowActionAsync("Manage ↗", async delegate { await OpenPaymentService("MANAGE_SUBSCRIPTION"); })),
                SettingRow("Purchases", "Receipts and things you've bought.", null, RowActionAsync("View ↗", async delegate { await OpenPaymentService("MANAGE_PURCHASES"); })),
                SettingRow("Add-ons", "Extras you can add to your plan.", null, RowActionAsync("Browse ↗", async delegate { await OpenPaymentService("MOBILE_ADDON_PAYMENT"); })));
            Group("Bought somewhere else",
                SettingRow("Google Play subscription", "Opens Google Play in your browser. Sign in with the Google account that made the purchase.", null, RowAction("Open ↗", delegate
                {
                    OpenExternal("https://play.google.com/store/account/subscriptions");
                })));
        }
        private void BusinessInsightsPage()
        {
            Sub("eero's website for businesses to manage their networks, guest portals and staff access. You need a business account with permission to use it.");
            Group(null, SettingRow("eero Insight", "Opens in its own window, already signed in to this network.", null, RowActionAsync("Open ↗", async delegate
            {
                if (preview) throw new InvalidOperationException("Sign in to open eero Insight.");
                object temporary = await api.Send("GET", "/2.2/login/temporary");
                OpenServiceWindow("eero Insight", "https://insight.eero.com/login/mobile?next=/networks/" + NetworkId, ServiceLinks.TokenHeader(J.S(temporary, "temporary_token")));
            })));
            RelatedPagesFor("Settings/Business guest wifi");
        }
    }
}
