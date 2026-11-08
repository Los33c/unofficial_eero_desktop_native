using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gtk;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private void ShowCredentials(string title, Dictionary<string, string> values)
        {
            var p = VBox(); p.Append(Text(title, 24, "#17263C", true));
            p.Append(Text("Reveal or copy only when you need these details. Copied values remain in your desktop clipboard.", 13, "#64738A"));
            var fields = new List<Entry>(); var secrets = new List<PasswordEntry>();
            foreach (var entry in values)
            {
                string secret = entry.Value; var label = Text(entry.Key, 14, "#17263C", true); label.SetMarginBottom(4); p.Append(label);
                var masked = PasswordEntry.New(); masked.SetText(secret); masked.SetEditable(false); masked.SetCanFocus(false); masked.SetCanTarget(false); masked.SetMarginBottom(8); masked.Name(entry.Key); secrets.Add(masked); p.Append(masked);
                var shown = Entry.New(); shown.SetEditable(false); shown.SetVisible(false); shown.SetMarginBottom(8); shown.Name(entry.Key); fields.Add(shown); p.Append(shown);
                var actions = HBox(8); actions.SetMarginBottom(16); var reveal = Gtk.Button.NewWithLabel("Reveal"); actions.Append(reveal);
                reveal.OnClicked += delegate { bool show = !shown.GetVisible(); shown.SetText(show ? secret : ""); shown.SetVisible(show); masked.SetVisible(!show); reveal.SetLabel(show ? "Hide" : "Reveal"); };
                actions.Append(LocalButton("Copy", delegate { Window.GetClipboard().SetText(secret); })); p.Append(actions);
            }
            var w = Dialog(title, p); w.Dialog.OnClosed += delegate { foreach (var field in fields) field.SetText(""); foreach (var secret in secrets) secret.SetText(""); values.Clear(); }; w.ShowDialog();
        }
        private async Task GuardianCredentials()
        {
            if (preview) { ShowCredentials("Sample Guardian sign-in", new Dictionary<string, string> { { "Guardian username", "Sample account" }, { "Guardian password", "sample-only-password" } }); return; }
            if (!await Confirm("Retrieve your Guardian sign-in details from eero? Use them only in Guardian's official app or website.")) return;
            object data = await api.Send("POST", "/2.2/partners/guardian");
            ShowCredentials("Guardian sign-in", EverydayContracts.GuardianLogin(data));
        }
        private void SubscriptionOffersPage(object data)
        {
            Sub("Plans you can get for this network, current offers, and links to share a plan with someone. Final prices and terms are shown in eero's payment service.");
            if (network == null) { Group(null, InfoRow("No network selected", null, "Choose a network at the top to see its plans and referrals.")); return; }
            var plans = Entries(data, "plans"); var planRows = new List<Widget>();
            foreach (object raw in plans)
            {
                object plan = raw; string id = J.S(plan, "id"); if (id.Length == 0) continue;
                string interval = Friendly(J.S(plan, "interval")), price = J.First(plan, "price", "display_price", "amount");
                planRows.Add(SettingRow(J.S(plan, "name").NullIfEmpty() ?? "eero plan", (price.Length > 0 ? price : "Price shown at checkout") + (interval.Length > 0 ? " · " + interval : ""), null,
                    RowActionAsync("Offers", async delegate
                    {
                        if (preview) throw new InvalidOperationException("Sign in to retrieve current offers.");
                        object offers = await api.Send("POST", "/2.2/premium/in_app_payments/offers/" + NetworkId, EverydayContracts.Offers(new[] { id }));
                        Info("Available offers", J.Obj("offers", Entries(offers, "offers").Select(o => J.Obj("title", J.S(o, "title"), "ends_at", J.S(o, "ends_at"))).ToArray()));
                    }),
                    RowActionAsync("Share link", async delegate
                    {
                        if (preview) throw new InvalidOperationException("Sign in to create a referral link.");
                        object referral = await api.Send("PUT", "/2.2/subscription_referrals", EverydayContracts.Referral(NetworkId, id));
                        ShowReferralLink(J.S(referral, "referral_url"));
                    }),
                    RowActionAsync("Email me", async delegate
                    {
                        if (preview) throw new InvalidOperationException("Sign in to email a referral.");
                        if (!await Confirm("Ask eero to email this plan's referral link to your account email address?")) return;
                        await api.Send("POST", "/2.2/subscription_referral/email", EverydayContracts.Referral(NetworkId, id)); SetNotice("Referral email requested from eero.");
                    })));
            }
            if (planRows.Count == 0) planRows.Add(InfoRow("No plans available", null, "eero didn't return any plans for this network. Your provider may manage your plan."));
            Group("Plans", planRows.ToArray());
            Group("Referrals",
                SettingRow("Referral status", "See who used your referral links.", null, RowActionAsync("View", async delegate { Info("Subscription referrals", await Read(FeatureUrl("managed/subscription_referrals"))); })),
                SettingRow("Purchases", "Receipts and past purchases. Opens eero's payment service.", null, RowActionAsync("View ↗", async delegate { await OpenPaymentService("MANAGE_PURCHASES"); })));
        }
        private void ShowReferralLink(string value)
        {
            string url = ServiceLinks.Eero(value).AbsoluteUri;
            var p = VBox(); p.Append(Text("Subscription referral", 24, "#17263C", true));
            var box = Entry.New(); box.SetText(url); box.SetEditable(false); box.SetMarginBottom(12); box.Name("Subscription referral link"); p.Append(box);
            var actions = HBox(8);
            actions.Append(LocalButton("Copy link", delegate { Window.GetClipboard().SetText(url); }));
            actions.Append(LocalButton("Open referral", delegate { Guarded(delegate { OpenServiceWindow("Subscription referral", url); }); }));
            p.Append(actions);
            Dialog("Subscription referral", p).ShowDialog();
        }
        private void GuidedDiagnosticsPage(object data)
        {
            Sub("Pick what's wrong. eero checks your network for that problem; results take a minute or two.");
            string[] labels = { "My internet is slow", "My internet is offline", "My connection keeps dropping", "My eero lights are red" };
            string[] helps = { "Checks your ISP speed, wifi signal and busy devices.", "Checks the modem, cables and your ISP.", "Checks signal strength and eero placement.", "Checks which eero has a problem and why." };
            string[] symptoms = { "internet_slow", "internet_offline", "connection_drops", "eeros_red" };
            var rows = new List<Widget>();
            for (int i = 0; i < labels.Length; i++)
            {
                string symptom = symptoms[i];
                rows.Add(SettingRow(labels[i], helps[i], null, RowActionAsync("Check", async delegate
                {
                    if (preview) throw new InvalidOperationException("Sign in to run network health checks.");
                    await api.Send("POST", NetResource("diagnostics"), EverydayContracts.Symptom(symptom));
                    SetNotice("Checking your network. Choose Show results in a minute.");
                })));
            }
            Group("What's wrong?", rows.ToArray());
            string status = J.S(data, "status"), area = J.First(data, "issue_area", "issue");
            string verdict = data == null ? "No check run yet" : status == "completed" || status == "complete" ? (area.Length == 0 || area == "none" ? "No problems found" : "Problem found: " + Friendly(area)) : status.Length > 0 ? Friendly(status) : "Waiting for results";
            Group("Results",
                SettingRow("Latest result", data == null ? "Run a check above, then show the results." : "Choose Show results again to get the newest result.", verdict,
                    RowActionAsync("Show results", async delegate { featureData[currentPage] = await Read(NetResource("diagnostics")); RenderPage(); })));
            TechnicalDetails(data);
        }
        private void DeviceConnectionPage()
        {
            Sub("Some smart-home devices can only join 2.4 GHz wifi during setup. Hide 5 GHz for a while, set up the device, then turn 5 GHz back on.");
            object flag = J.Get(network, "temporary_flags", "hide_5g");
            bool hidden = flag is bool ? (bool)flag : J.B(flag, "value");
            string until = J.First(flag, "expires_at", "expiration", "expires");
            Group(null, SettingToggle("Hide 5 GHz for device setup", hidden ? "5 GHz is hidden" + (until.Length > 0 ? " until " + until : "") + ". Turn this off when your device is connected." : "Other devices may reconnect briefly. eero turns 5 GHz back on automatically after a while.", hidden, async delegate(bool on)
            {
                if (on && !await Confirm("Hide 5 GHz on this network for now? Wireless devices may reconnect while you set up your device.")) return;
                if (on) await Change("PUT", FeatureUrl("temporary_flags/hide_5g"), J.Obj("value", true), true, "5 GHz hidden for device setup.");
                else await Change("DELETE", FeatureUrl("temporary_flags/hide_5g"), null, false, "5 GHz is back on.");
            }));
            AddGroupHeading(page, "Still can't connect?");
            page.Append(RowGroup(FeatureCatalog.Entries.Where(e => e.Path == "Settings/Advanced networking/Wireless settings").Select(e => ListRow(e, false))));
        }
        private void FastRoamingPage(object data)
        {
            Sub("Helps phones and laptops move between eeros without dropping calls or video (802.11r).");
            if (!(J.Get(data, "fast_transition") is bool)) { page.Append(Card(Text("eero didn't report a fast roaming setting for this network, so it can't be changed here.", 14, "#64738A"), 18)); return; }
            Group(null, SettingToggle("Fast roaming", "Turn off if an older device keeps disconnecting or can't join.", J.B(data, "fast_transition"), async delegate(bool on)
            {
                if (!await Confirm((on ? "Turn on" : "Turn off") + " fast roaming? Some wireless devices may reconnect.")) return;
                await Change("PUT", FeatureUrl("fast_transition"), J.Obj("fast_transition", on), false, "Fast roaming " + (on ? "turned on." : "turned off."));
            }));
            TechnicalDetails(data);
        }
        private void BackupDiscoveryPage(object data)
        {
            Sub("Find wifi networks your eero can reach and use one as a backup when your internet goes down.");
            string status = J.S(data, "status");
            Group(null, SettingRow("Search for networks", "Your eero looks for nearby wifi. This takes about a minute; then choose Show results.", data == null ? "Not searched yet" : Friendly(status),
                RowActionAsync("Search", async delegate { if (preview) throw new InvalidOperationException("Sign in to scan for backup networks."); await api.Send("POST", FeatureUrl("backup_access_points/ssid_discovery")); SetNotice("Searching for nearby wifi. Choose Show results in a minute."); }),
                RowActionAsync("Show results", async delegate { featureData[currentPage] = await Read(FeatureUrl("backup_access_points/ssid_discovery")); RenderPage(); })));
            var rows = new List<Widget>();
            foreach (object found in Entries(J.Get(data, "discovered_ssids")).OrderByDescending(f => { int r; return Int32.TryParse(J.S(f, "rssi"), out r) ? r : -200; }))
            {
                string ssid = J.S(found, "ssid"); int rssi; string signal = Int32.TryParse(J.S(found, "rssi"), out rssi) ? (rssi >= -60 ? "Strong signal" : rssi >= -70 ? "Good signal" : rssi >= -80 ? "Weak signal" : "Very weak signal") + " (" + rssi + " dBm)" : "Signal unknown";
                rows.Add(SettingRow(ssid.NullIfEmpty() ?? "Hidden network", signal, null, RowAction("Use as backup", delegate { BackupEdit(null, ssid); })));
            }
            if (data != null && rows.Count == 0) rows.Add(InfoRow("No networks found yet", null, "Search, wait a minute, then choose Show results."));
            Group("Networks found", rows.ToArray());
        }
        private void BackupPriority(List<object> networks)
        {
            Guarded(delegate
            {
                if (networks.Count < 2) throw new InvalidOperationException("Save at least two backup wifi networks to choose their priority.");
                var p = VBox(); p.Append(Text("Backup wifi priority", 24, "#17263C", true)); p.Append(Text("Move the preferred backup to the top. All saved backups stay in the order.", 14, "#64738A"));
                var ordered = networks.ToList();
                var heading = Text("Backup wifi (highest priority first)", 13, "#64738A", true); heading.Margin(4, 8, 0, 6); p.Append(heading);
                var list = ListBox.New(); list.AddCssClass("boxed-list"); list.SetSelectionMode(SelectionMode.Single); list.SetMarginBottom(12); list.Name("Backup wifi (highest priority first)"); p.Append(list);
                System.Action<int> fill = selected =>
                {
                    list.RemoveAll();
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        var row = Adw.ActionRow.New(); row.SetUseMarkup(false); row.SetTitle((i + 1) + ". " + (J.S(ordered[i], "ssid").NullIfEmpty() ?? "Backup wifi"));
                        list.Append(row);
                    }
                    var chosen = list.GetRowAtIndex(selected); if (chosen != null) list.SelectRow(chosen);
                };
                fill(0);
                Action<int> move = offset => { var row = list.GetSelectedRow(); int from = row == null ? -1 : row.GetIndex(), to = from + offset; if (from < 0 || to < 0 || to >= ordered.Count) return; object item = ordered[from]; ordered.RemoveAt(from); ordered.Insert(to, item); fill(to); };
                var buttons = HBox(8); buttons.SetMarginBottom(8); buttons.Append(LocalButton("Move up", delegate { move(-1); })); buttons.Append(LocalButton("Move down", delegate { move(1); })); p.Append(buttons);
                var status = Text("", 13, "#B5333D"); p.Append(status); var w = Dialog("Backup priority", p); bool working = false;
                var save = Gtk.Button.NewWithLabel("Save priority"); Accent(save); save.SetHalign(Gtk.Align.Start); p.Append(save);
                save.OnClicked += async delegate
                {
                    if (working) return; working = true; save.SetSensitive(false); w.Dialog.SetCanClose(false);
                    try { if (preview) throw new InvalidOperationException("Preview mode is read only."); await Change("POST", FeatureUrl("backup_access_points/rearrange"), EverydayContracts.BackupOrder(ordered.Select(n => J.S(n, "uuid"))), false, "Backup priority saved."); working = false; w.Dialog.SetCanClose(true); w.Close(); }
                    catch (Exception e) { status.SetText(Message(e)); }
                    finally { working = false; save.SetSensitive(true); w.Dialog.SetCanClose(true); }
                };
                w.ShowDialog();
            });
        }
        private void AmazonHomePage()
        {
            Sub("Use your eero with Amazon smart-home devices and Alexa.");
            bool linked = J.B(network, "amazon_account_linked");
            Group("Amazon account",
                SettingRow("Amazon account", linked ? "Your Amazon account is linked to this network." : "Link an Amazon account in the official eero app. New links can't be made here.", linked ? "Linked" : "Not linked",
                    linked ? RowActionAsync("Unlink", async delegate { if (!await Confirm("Unlink the Amazon account from this network? Connected-home integrations may stop working.")) return; await Change("POST", "/2.2/amazon/networks/" + NetworkId + "/account_unlink", null, false, "Amazon account unlinked."); }, true) : null),
                J.Get(network, "alexa_skill") is bool ? InfoRow("Alexa skill", J.B(network, "alexa_skill") ? "Enabled" : "Not enabled") : null);
            Group("Zigbee devices", SettingRow("Find Zigbee devices", linked ? "Put the device in pairing mode first. It then shows up in the Amazon Alexa app." : "Needs a linked Amazon account and an eero that works as a smart-home hub.", null,
                linked ? RowActionAsync("Find devices", async delegate { if (!await Confirm("Start looking for nearby Zigbee devices? Put the device you want to add in pairing mode first.")) return; await Change("POST", "/2.3/networks/" + NetworkId + "/shh/start_zigbee_discovery", null, false, "Looking for Zigbee devices. Check the Amazon Alexa app for new devices."); }) : null));
            RelatedPagesFor("Settings/Connected services/Connected device setup");
        }
    }
}
