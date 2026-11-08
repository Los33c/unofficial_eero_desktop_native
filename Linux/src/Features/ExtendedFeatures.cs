using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Gtk;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        // Runs after the editor dialog that called it has closed (WPF Dispatcher.BeginInvoke).
        private void AfterEditor(Action action) { GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT, () => { action(); return false; }); }
        private string NetworkId { get { return ExtendedContracts.Identifier(networkUrl.TrimEnd('/').Split('/').Last()); } }
        private static bool AccountOnlyPage(string route)
        {
            return route == "Settings/Notifications/Desktop notifications" || route == "Help" || route.StartsWith("Help/") || route == "Settings" || route == "Settings/Appearance" || route == "Settings/About" || route.StartsWith("Settings/Account settings") || route == "Settings/Network management" || route == "Settings/Network management/Pending transfers" || route == "Settings/User permissions/Join a network" || route == "Settings/User permissions/Incoming access invitations" || route == "Home/Add or replace eero" || route == "Home/Add or replace eero/Find nearby eeros" || route == "Home/Add or replace eero/Create a network";
        }
        private void JoinNetworkPage()
        {
            Sub("Got an invitation link from someone? Paste it here to see the network and the access you'd get before you accept.");
            var codeInput = Entry.New(); codeInput.SetPlaceholderText("Paste an invitation link or code"); codeInput.Name("Paste an invitation link or code");
            codeInput.SetMaxLength(1024); codeInput.SetMarginBottom(12); codeInput.SetHalign(Gtk.Align.Start); codeInput.SetSizeRequest(560, -1);
            page.Append(codeInput);
            var review = Button("Review invitation", async delegate
            {
                string code = ExtendedContracts.InviteCode(codeInput.GetText()); object data = await ReadInvite(code);
                InvitationReview(code, data);
            }, true);
            codeInput.OnActivate += delegate { if (!busy) review.Activate(); };
            page.Append(review);
            page.Append(Text("Nothing changes until you choose Accept in the next step.", 13, "#64738A"));
        }
        private Task<object> ReadInvite(string code) { if (preview) throw new InvalidOperationException("Sign in to review an invitation."); return api.Send("POST", "/2.2/inviteQuery", J.Obj("invite_code", code)); }
        private void InvitationReview(string code, object data)
        {
            string id = ExtendedContracts.Identifier(J.S(data, "network_id")); string state = J.S(data, "invite_status");
            var p = VBox(); p.Append(Text("Network invitation", 23, "#17263C", true)); Details(p, data, 0);
            var w = Dialog("Network invitation", p); var status = Text("", 13, "#B5333D"); p.Append(status); var actions = HBox(8); actions.SetMarginTop(8); p.Append(actions); bool working = false;
            foreach (bool decision in new[] { true, false })
            {
                bool accept = decision; var button = Gtk.Button.NewWithLabel(accept ? "Accept invitation" : "Decline invitation"); button.SetSensitive(state.Equals("pending", StringComparison.OrdinalIgnoreCase));
                if (accept) Accent(button);
                button.OnClicked += async delegate
                {
                    if (working || !await Confirm((accept ? "Accept" : "Decline") + " the invitation to " + J.S(data, "network_name") + "?")) return;
                    working = true; actions.SetSensitive(false); w.Dialog.SetCanClose(false);
                    try { await api.Send("POST", "/2.2/networks/" + id + "/invites/response", ExtendedContracts.InviteResponse(code, accept)); working = false; w.Dialog.SetCanClose(true); w.Close(); await ReloadMembership(accept ? "Network invitation accepted." : "Network invitation declined."); }
                    catch (Exception e) { status.SetText(Message(e)); }
                    finally { working = false; actions.SetSensitive(true); w.Dialog.SetCanClose(true); }
                }; actions.Append(button);
            }
            if (!state.Equals("pending", StringComparison.OrdinalIgnoreCase)) p.Append(Text("Only pending invitations can be accepted or declined.", 13, "#64738A"));
            w.ShowDialog();
        }
        private async Task ReloadMembership(string message)
        {
            selectedProfile = selectedNode = network = null; networkUrl = null; devices.Clear(); nodes.Clear(); profiles.Clear(); featureData.Clear();
            notificationEpoch++; currentPage = "Home";
            try { await LoadAccount(); ShowShell(); SetNotice(message); }
            catch (Exception e) { ShowShell(); SetNotice(message + " Refresh did not finish: " + Message(e), true); }
        }
        private void PendingTransfersPage(object data)
        {
            Sub("Networks someone wants to give to your account. If you accept, you become the owner and can manage it from the network selector.");
            var transfers = Entries(data, "transfers"); var rows = new List<Widget>();
            foreach (object raw in transfers)
            {
                object transfer = raw; string id = ExtendedContracts.Identifier(J.S(transfer, "id"));
                string from = J.First(transfer, "organization_name", "sender_name", "from"), what = J.First(transfer, "network_name", "subset_name", "name"), when = J.First(transfer, "created_at", "created");
                var actions = new List<Button>();
                foreach (bool value in new[] { true, false })
                {
                    bool accept = value; var b = RowActionAsync(accept ? "Accept" : "Decline", async delegate
                    {
                        if (preview) throw new InvalidOperationException("Preview mode is read only.");
                        if (!await Confirm((accept ? "Accept ownership of" : "Decline") + " this network transfer" + (from.Length > 0 ? " from " + from : "") + "?")) return;
                        await api.Send("POST", "/2.2/network_transfers/" + id + (accept ? "/accept" : "/deny"));
                        await ReloadMembership(accept ? "Network transfer accepted." : "Network transfer declined.");
                    });
                    if (accept) { Accent(b); CompactAction(b); }
                    actions.Add(b);
                }
                rows.Add(SettingRow((what.NullIfEmpty() ?? "A network") + (from.Length > 0 ? " from " + from : ""), when.Length > 0 ? "Sent " + when : "Waiting for your answer", null, actions.ToArray()));
            }
            if (rows.Count == 0) rows.Add(InfoRow("No pending transfers", null, "When someone sends you a network, it shows up here."));
            Group(null, rows.ToArray());
            string next = J.S(data, "next_token"); int token;
            if (Int32.TryParse(next, out token)) page.Append(Button("Next page of transfers", async delegate { featureData[currentPage] = await Read("/2.2/network_transfers/pending?token=" + token); RenderPage(); }));
        }
        private void LicensePage()
        {
            Sub("Use a software key from eero or your provider to activate a plan. You'll check the key first, then confirm before anything is activated. Automatic renewal starts off.");
            if (network == null) { Group(null, InfoRow("No network selected", null, "Choose the network to activate the key for at the top.")); return; }
            Group(null, SettingRow("License key", "Activates on " + (J.S(network, "name").NullIfEmpty() ?? "this network") + ".", null, RowAction("Enter key", delegate
            {
                Edit("License key", new[] { new Field("key", "Software key", null, "password") }, async v =>
                {
                    if (preview) throw new InvalidOperationException("Sign in to validate a key."); string key = V(v, "key").Trim();
                    object result = await api.Send("POST", "/2.2/license_keys", ExtendedContracts.License(key));
                    AfterEditor(delegate { LicenseActivation(key, result); });
                }, "Check key");
            })));
        }
        private void OutgoingTransferPage(object data)
        {
            Sub("An invitation you sent to give this network to someone else.");
            string to = J.First(data, "recipient_email_address", "recipient_phone_number", "recipient_name"), sent = J.S(data, "transfer_date");
            if (to.Length == 0 && sent.Length == 0) { Group(null, InfoRow("No transfer in progress", null, "Start one from Network management › Transfer ownership.")); return; }
            Group(null, SettingRow("Invitation to " + (to.NullIfEmpty() ?? "recipient"), (sent.Length > 0 ? "Sent " + sent + " · " : "") + "Waiting for them to accept. Ownership changes only when they do.", null,
                RowActionAsync("Resend", async delegate { if (!await Confirm("Resend the pending ownership invitation to the current recipient?")) return; await Change("POST", NetResource("transfer"), null, false, "Ownership invitation resent."); }),
                RowActionAsync("Cancel", async delegate { if (!await Confirm("Cancel the outgoing ownership invitation?")) return; await Change("DELETE", NetResource("transfer"), null, false, "Ownership invitation canceled."); }, true)));
            TechnicalDetails(data);
        }
        private void LicenseActivation(string key, object result)
        {
            var p = VBox(); p.Append(Text("Review license activation", 23, "#17263C", true)); Details(p, result, 0);
            p.Append(Text("Network: " + J.S(network, "name"), 16, "#17263C", true));
            var renewLabel = Text("Enable automatic renewal under the provider's subscription terms", 14); renewLabel.SetMarginBottom(0); renewLabel.SetMaxWidthChars(52);
            var renew = CheckButton.New(); renew.SetChild(renewLabel); renew.SetActive(false); renew.SetMarginBottom(12); p.Append(renew);
            var w = Dialog("Activate license", p); var status = Text("", 13, "#B5333D"); p.Append(status);
            var activate = Gtk.Button.NewWithLabel("Activate for this network"); Accent(activate); activate.SetHalign(Gtk.Align.Start); p.Append(activate); bool working = false;
            activate.OnClicked += async delegate
            {
                if (working || !await Confirm("Activate this subscription key for " + J.S(network, "name") + "? Automatic renewal: " + (renew.GetActive() ? "enabled" : "disabled") + ".")) return;
                working = true; activate.SetSensitive(false); w.Dialog.SetCanClose(false);
                try { await Change("POST", "/2.2/license_keys", ExtendedContracts.License(key, NetworkId, renew.GetActive()), false, "Subscription key activated."); working = false; w.Dialog.SetCanClose(true); w.Close(); }
                catch (Exception e) { status.SetText(Message(e)); } finally { working = false; activate.SetSensitive(true); w.Dialog.SetCanClose(true); }
            };
            w.ShowDialog();
        }
        private void PartnerBenefitsPage()
        {
            Sub("Apps and services included with eero Plus. You need the matching plan. Codes only appear when you ask for them.");
            Group("Your benefits",
                SettingRow("1Password", "Password manager. Get a code, then redeem it on 1Password's website.", null,
                    RowActionAsync("Status", async delegate { Info("1Password", await Read("/2.2/account/1password_status")); }),
                    RowActionAsync("Get code", async delegate { ShowPartnerCode("1Password", await Read("/2.2/partners/onepassword/coupon?test=false")); })),
                SettingRow("Malwarebytes", "Antivirus for your computers and phones.", null,
                    RowActionAsync("Get code", async delegate { ShowPartnerCode("Malwarebytes", await Read("/2.2/partners/malwarebytes/coupon?test=false")); })),
                network != null ? SettingRow("VPN (Guardian)", "See how many VPN devices your plan allows. Set up the VPN under Security & privacy.", null,
                    RowActionAsync("Check seats", async delegate { Info("VPN seats", await Read("/2.2/partners/guardian/networks/" + NetworkId + "/availability")); })) : null);
            RelatedPagesFor("Home/Security & privacy/Identity protection", "Home/Security & privacy/VPN");
        }
        private void ShowPartnerCode(string title, object result)
        {
            string code = J.S(result, "coupon"); if (code.Length == 0) throw new InvalidOperationException("No redemption code was returned for this account.");
            var p = VBox(); p.Append(Text(title + " redemption code", 23, "#17263C", true));
            var input = Entry.New(); input.SetText(code); input.SetEditable(false); input.SetMarginBottom(12); input.Name(title + " redemption code"); p.Append(input);
            var copy = Gtk.Button.NewWithLabel("Copy redemption code"); copy.SetHalign(Gtk.Align.Start); copy.OnClicked += delegate { Window.GetClipboard().SetText(code); }; p.Append(copy);
            var w = Dialog(title, p); w.Dialog.OnClosed += delegate { input.SetText(""); }; w.ShowDialog();
        }
        private void IdentityProtectionPage(object data)
        {
            Sub("Watches for your personal information in data breaches and on the dark web. Included with some eero Plus plans.");
            object enabled = J.Get(data, "enabled");
            Group("Protection",
                SettingRow("Identity protection", J.S(data, "status").Length > 0 ? Friendly(J.S(data, "status")) : "Turn it on to start monitoring.", enabled is bool ? ((bool)enabled ? "On" : "Off") : "Not reported",
                    enabled is bool && (bool)enabled ? null : RowAction("Turn on", ActivateIdentityProtection),
                    RowActionAsync("Countries", async delegate { Info("Supported countries", await Read(FeatureUrl("identity_protection/countries"))); })));
            object previous; featureData.TryGetValue("identity_scan", out previous);
            Group("Scan an email address",
                SettingRow("1. Scan", "Checks where an email address appears in known data breaches.", null, RowAction("Scan email", ScanIdentityEmail)),
                SettingRow("2. Verify the email (optional)", "Verify you own the address to see the full results. eero sends a code to it.", null, RowAction("Send code", SendIdentityVerification), RowAction("Enter code", VerifyIdentityEmail)),
                previous != null ? SettingRow("Latest result", null, null, RowAction("View", delegate { IdentityScanResult(previous); })) : null);
        }
        private void ActivateIdentityProtection()
        {
            Edit("Turn on identity protection", new[] { new Field("country", "Country", "US").Help("Two-letter code, for example US, CA or GB. Use Countries to see where it's available.") }, async v =>
            {
                string country = V(v, "country").Trim().ToUpperInvariant(); if (!Regex.IsMatch(country, "^[A-Z]{2}$")) throw new ArgumentException("Enter a two-letter country code.");
                if (!await Confirm("Turn on identity protection for this account in " + country + "?")) return;
                await Change("POST", "/2.2/account/identity_protection/signup", J.Obj("country_code", country), false, "Identity protection activation requested.");
            }, "Turn on");
        }
        private void ScanIdentityEmail()
        {
            Edit("Scan an email address", new[] { new Field("email", "Email address") }, async v =>
            {
                string email = ExtendedContracts.Email(V(v, "email")); if (preview) throw new InvalidOperationException("Sign in to run a scan.");
                if (!await Confirm("Send " + email + " to eero's identity-protection service for a scan?")) return;
                object result = await api.Send("POST", "/2.2/account/identity_protection/scan", J.Obj("email", email)); featureData["identity_scan"] = result;
                AfterEditor(delegate { IdentityScanResult(result); });
            }, "Scan");
        }
        private void SendIdentityVerification()
        {
            Edit("Verify your email", new[] { new Field("email", "Email address").Help("eero sends a code to this address.") }, async v => { if (preview) throw new InvalidOperationException("Sign in to request verification."); await api.Send("POST", "/2.2/account/identity_protection/scan/email", J.Obj("email", ExtendedContracts.Email(V(v, "email")))); SetNotice("Code sent. Choose Enter code when it arrives."); }, "Send code");
        }
        private void VerifyIdentityEmail()
        {
            Edit("Enter verification code", new[] { new Field("code", "Code from the email") }, async v =>
            {
                string code = V(v, "code").Trim(); if (!Regex.IsMatch(code, "^[A-Za-z0-9-]{4,32}$")) throw new ArgumentException("Enter the code from the verification email."); if (preview) throw new InvalidOperationException("Sign in to verify an email.");
                object result = await api.Send("POST", "/2.2/account/identity_protection/verify_email", J.Obj("code", code)); featureData["identity_scan"] = result; AfterEditor(delegate { IdentityScanResult(result); });
            }, "Verify");
        }
        private void IdentityScanResult(object result)
        {
            var p = VBox(); p.Append(Text("Digital footprint", 23, "#17263C", true)); Details(p, result, 0);
            string report = J.S(result, "mwb_full_report_url");
            if (report.Length > 0) { var b = Gtk.Button.NewWithLabel("Open detailed report at Malwarebytes"); b.SetHalign(Gtk.Align.Start); b.SetMarginTop(8); b.OnClicked += delegate { try { OpenPartnerReport(report); } catch (Exception e) { SetNotice(Message(e), true); } }; p.Append(b); }
            if (J.B(result, "email_unverified")) p.Append(Text("Verify the email (step 2 on the page) to see the full results.", 14, "#64738A"));
            Dialog("Digital footprint", p).ShowDialog();
        }
        private void OpenPartnerReport(string url)
        {
            Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u) || u.Scheme != "https" || u.Port != 443 || u.UserInfo.Length > 0 || !(u.Host == "malwarebytes.com" || u.Host.EndsWith(".malwarebytes.com", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("The service did not return a supported secure Malwarebytes report link.");
            OpenExternal(url);
        }
        private void SetupVendorsPage(object data)
        {
            Sub("Let smart-home brands set up their devices on your wifi automatically, without you typing the password.");
            var vendors = Entries(data, "vendors", "configurations"); var rows = new List<Widget>();
            foreach (object raw in vendors)
            {
                object vendor = raw; string id = J.S(vendor, "vendor_id");
                string name = J.First(vendor, "vendor_name", "name", "display_name").NullIfEmpty() ?? Friendly(id).NullIfEmpty() ?? "Device maker";
                if (id.Length == 0) { rows.Add(InfoRow(name, OnOff(J.B(vendor, "enabled")))); continue; }
                rows.Add(SettingToggle(name, "Let " + name + " devices join your wifi during setup.", J.B(vendor, "enabled"), async delegate(bool on)
                {
                    if (!await Confirm((on ? "Allow " : "Stop allowing ") + name + " to set up devices on this network?")) return;
                    await Change("PUT", FeatureUrl("simple_setup/vendors/" + Uri.EscapeDataString(id)), J.Obj("enabled", on), true, name + " setup " + (on ? "allowed." : "turned off."));
                }));
            }
            if (rows.Count == 0) rows.Add(InfoRow("No device makers listed", null, "eero didn't return any brands for automatic setup on this network."));
            Group(null, rows.ToArray());
        }
        private void IspSettingsPage()
        {
            Sub("Only needed if your internet provider gave you a PPPoE username and password, or a VLAN ID. Most connections don't use either.");
            bool pppoe = J.B(network, "pppoe_enabled"); string user = J.S(network, "pppoe_username"); string vlan = J.S(network, "vlan");
            Group(null,
                SettingRow("PPPoE login", "Signs in to your ISP with a username and password. Common with some DSL and fiber providers.", pppoe ? "On" + (user.Length > 0 ? " · " + user : "") : "Off", RowAction("Change", EditIspSettings)),
                SettingRow("VLAN tagging", "Some fiber providers require a VLAN ID on the internet connection.", vlan.Length > 0 ? "VLAN " + vlan : "Off", RowAction("Change", EditIspSettings)));
        }
        private void EditIspSettings()
        {
            Edit("ISP settings", new[] {
                new Field("enabled", "Use PPPoE login", J.B(network, "pppoe_enabled"), "bool"),
                new Field("replace", "Enter a new PPPoE username and password", !J.B(network, "pppoe_enabled"), "bool").When("enabled", "True").Help("Leave off to keep the credentials eero already has."),
                new Field("username", "PPPoE username", J.S(network, "pppoe_username")).When("replace", "True"),
                new Field("password", "PPPoE password", null, "password").When("replace", "True"),
                new Field("vlan", "ISP VLAN ID", J.S(network, "vlan")).Help("Leave blank to turn VLAN tagging off.") }, async v =>
            {
                bool enabled = Flag(v, "enabled"), replace = Flag(v, "replace"); if (enabled && !J.B(network, "pppoe_enabled") && !replace) throw new ArgumentException("Enter new credentials when enabling PPPoE for the first time.");
                object body = ExtendedContracts.Pppoe(enabled, V(v, "username"), V(v, "password"), replace, V(v, "vlan")); if (!await Confirm("Apply ISP settings? Incorrect PPPoE credentials or VLAN settings can disconnect the network.")) return;
                await Change("PUT", NetResource("settings"), body, false, "ISP settings updated.");
            }, "Apply ISP settings", "Use the settings supplied by your ISP. Wrong values can disconnect your network.");
        }
        private async Task UploadPortalAsset(object config, bool logo)
        {
            if (preview) throw new InvalidOperationException("Sign in to upload a portal asset.");
            string configId = ExtendedContracts.Identifier(J.First(config, "configuration_id", "id"));
            string fileName = logo
                ? await PickFile("Select guest portal logo", "PNG or JPEG image", new[] { "image/png", "image/jpeg" }, new[] { "*.png", "*.jpg", "*.jpeg" })
                : await PickFile("Select guest terms PDF", "PDF document", new[] { "application/pdf" }, new[] { "*.pdf" });
            if (fileName == null) return;
            long size = new FileInfo(fileName).Length; if (size > (logo ? 5 : 10) * 1024 * 1024) throw new ArgumentException("This file is too large. Logos support up to 5 MB; PDFs up to 10 MB.");
            byte[] bytes = File.ReadAllBytes(fileName); string mime = ExtendedContracts.PortalMime(fileName, bytes, logo);
            if (!await Confirm("Upload " + Path.GetFileName(fileName) + " (" + ((bytes.Length + 1023) / 1024) + " KB) as the guest portal " + (logo ? "logo" : "terms document") + "? Guests will be able to access the uploaded asset.")) return;
            string type = logo ? "CaptivePortalLogo" : "CaptivePortalToS", ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
            object upload = await api.Send("GET", FeatureUrl("file/import/upload_link?importJobType=" + type + "&fileExtension=" + ext));
            await PortalUpload.Send(upload, bytes, mime);
            await Change("POST", FeatureUrl("file/import/job"), ExtendedContracts.ImportJob(J.S(upload, "import_file_path"), logo, configId), false, "Asset uploaded and import requested. Refresh the portal after processing finishes.");
        }
        // Native file chooser (xdg portal when sandboxed). Returns a local path, or null when the user cancels.
        private async Task<string> PickFile(string title, string filterName, string[] mimeTypes, string[] patterns)
        {
            var filter = FileFilter.New(); filter.SetName(filterName);
            foreach (string m in mimeTypes) filter.AddMimeType(m);
            foreach (string p in patterns) filter.AddPattern(p);
            var filters = Gio.ListStore.New(FileFilter.GetGType()); filters.Append(filter);
            var picker = FileDialog.New(); picker.SetTitle(title); picker.SetModal(true); picker.SetFilters(filters); picker.SetDefaultFilter(filter);
            Gio.File file;
            try { file = await picker.OpenAsync(Window); }
            catch (GLib.GException) { return null; }
            if (file == null) return null;
            string path = file.GetPath();
            if (String.IsNullOrEmpty(path)) throw new InvalidOperationException("Choose a file stored on this computer.");
            return path;
        }
        private void DnsPerformancePage(object result)
        {
            Sub("Check how quickly DNS servers answer your gateway eero. Faster servers can make websites start loading sooner. Testing doesn't change your settings.");
            bool started = featureData.ContainsKey("dns_perf_action");
            Group(null,
                SettingRow("Run a test", "Compare the servers you use now with any others you want to try, for example 1.1.1.1 or 8.8.8.8.", null, RowAction("Start test", delegate
                {
                    string existing = String.Join(Environment.NewLine, J.List(J.Get(network, "dns", "custom", "ips")).Select(s => J.S(s)));
                    Edit("Test DNS servers", new[] { new Field("servers", "DNS servers to test (one per line)", existing, "multiline").Help("Leave as is to test the servers you use now."), new Field("port", "Port", "53").Help("Almost always 53.") }, async v =>
                    {
                        int port; if (!Int32.TryParse(V(v, "port"), out port)) throw new ArgumentException("Enter a numeric DNS port."); object body = ExtendedContracts.DnsPerformance(V(v, "servers"), port);
                        if (preview) throw new InvalidOperationException("Sign in to start a gateway test."); object kickoff = await api.Send("POST", FeatureUrl("dns_perf_test"), body);
                        featureData["dns_perf_action"] = ExtendedContracts.Identifier(J.S(kickoff, "action_uuid")); await LoadFeature(currentPage); RenderPage(); SetNotice("Test started. Choose Show results in a moment.");
                    }, "Start test");
                })),
                started ? SettingRow("Results", "The test takes a few seconds. Show results again if it isn't finished.", null, RowActionAsync("Show results", async delegate { await LoadFeature(currentPage); RenderPage(); })) : null);
            AddGroupHeading(page, "Results");
            if (result != null) { var body = VBox(); Details(body, result, 0); page.Append(Card(body, 18)); }
            else page.Append(Card(Text(started ? "Waiting for results. Choose Show results above." : "No test yet. Run one above to see how fast each server answers.", 14, "#64738A"), 18));
        }
    }
}
