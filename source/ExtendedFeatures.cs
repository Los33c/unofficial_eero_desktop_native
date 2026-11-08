using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private void AfterEditor(Action action) { Dispatcher.BeginInvoke(action); }
        private string NetworkId { get { return ExtendedContracts.Identifier(networkUrl.TrimEnd('/').Split('/').Last()); } }
        private static bool AccountOnlyPage(string route)
        {
            return route == "Settings/Notifications/Windows notifications" || route == "Help" || route.StartsWith("Help/") || route == "Settings" || route == "Settings/Appearance" || route == "Settings/About" || route.StartsWith("Settings/Account settings") || route == "Settings/Network management" || route == "Settings/Network management/Pending transfers" || route == "Settings/User permissions/Join a network" || route == "Settings/User permissions/Incoming access invitations" || route == "Home/Add or replace eero" || route == "Home/Add or replace eero/Find nearby eeros" || route == "Home/Add or replace eero/Create a network";
        }
        private void JoinNetworkPage()
        {
            Sub("Got an invitation link from someone? Paste it here to see the network and the access you'd get before you accept.");
            TextBox codeBox; TextBlock hint;
            var field = SearchField("Paste an invitation link or code", out codeBox, out hint); field.Margin = new Thickness(0, 0, 0, 12); field.MaxWidth = 560; field.HorizontalAlignment = HorizontalAlignment.Left; codeBox.MaxLength = 1024;
            page.Children.Add(field);
            var review = Button("Review invitation", async delegate
            {
                string code = ExtendedContracts.InviteCode(codeBox.Text); object data = await ReadInvite(code);
                InvitationReview(code, data);
            }, true);
            codeBox.KeyDown += delegate(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.Enter && !busy) review.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); };
            page.Children.Add(review);
            page.Children.Add(Text("Nothing changes until you choose Accept in the next step.", 13, "#64738A"));
        }
        private Task<object> ReadInvite(string code) { if (preview) throw new InvalidOperationException("Sign in to review an invitation."); return api.Send("POST", "/2.2/inviteQuery", J.Obj("invite_code", code)); }
        private void InvitationReview(string code, object data)
        {
            string id = ExtendedContracts.Identifier(J.S(data, "network_id")); string state = J.S(data, "invite_status");
            var p = new StackPanel(); p.Children.Add(Text("Network invitation", 23, "#17263C", true)); Details(p, data, 0);
            var w = Dialog("Network invitation", p); var status = Text("", 13, "#B5333D"); p.Children.Add(status); var actions = new WrapPanel(); p.Children.Add(actions); bool working = false;
            foreach (bool decision in new[] { true, false })
            {
                bool accept = decision; var button = new Button { Content = accept ? "Accept invitation" : "Decline invitation", IsEnabled = state.Equals("pending", StringComparison.OrdinalIgnoreCase) };
                button.Click += async delegate
                {
                    if (working || !Confirm((accept ? "Accept" : "Decline") + " the invitation to " + J.S(data, "network_name") + "?")) return;
                    working = true; actions.IsEnabled = false;
                    try { await api.Send("POST", "/2.2/networks/" + id + "/invites/response", ExtendedContracts.InviteResponse(code, accept)); working = false; w.Close(); await ReloadMembership(accept ? "Network invitation accepted." : "Network invitation declined."); }
                    catch (Exception e) { status.Text = Message(e); }
                    finally { working = false; actions.IsEnabled = true; }
                }; actions.Children.Add(button);
            }
            if (!state.Equals("pending", StringComparison.OrdinalIgnoreCase)) p.Children.Add(Text("Only pending invitations can be accepted or declined.", 13, "#64738A"));
            w.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { e.Cancel = working; }; w.ShowDialog();
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
            var transfers = Entries(data, "transfers"); var rows = new List<UIElement>();
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
                        if (!Confirm((accept ? "Accept ownership of" : "Decline") + " this network transfer" + (from.Length > 0 ? " from " + from : "") + "?")) return;
                        await api.Send("POST", "/2.2/network_transfers/" + id + (accept ? "/accept" : "/deny"));
                        await ReloadMembership(accept ? "Network transfer accepted." : "Network transfer declined.");
                    });
                    if (accept) { b.Style = (Style)FindResource("AccentButton"); CompactAction(b); }
                    actions.Add(b);
                }
                rows.Add(SettingRow((what.NullIfEmpty() ?? "A network") + (from.Length > 0 ? " from " + from : ""), when.Length > 0 ? "Sent " + when : "Waiting for your answer", null, actions.ToArray()));
            }
            if (rows.Count == 0) rows.Add(InfoRow("No pending transfers", null, "When someone sends you a network, it shows up here."));
            Group(null, rows.ToArray());
            string next = J.S(data, "next_token"); int token;
            if (Int32.TryParse(next, out token)) page.Children.Add(Button("Next page of transfers", async delegate { featureData[currentPage] = await Read("/2.2/network_transfers/pending?token=" + token); RenderPage(); }));
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
                RowActionAsync("Resend", async delegate { if (!Confirm("Resend the pending ownership invitation to the current recipient?")) return; await Change("POST", NetResource("transfer"), null, false, "Ownership invitation resent."); }),
                RowActionAsync("Cancel", async delegate { if (!Confirm("Cancel the outgoing ownership invitation?")) return; await Change("DELETE", NetResource("transfer"), null, false, "Ownership invitation canceled."); }, true)));
            TechnicalDetails(data);
        }
        private void LicenseActivation(string key, object result)
        {
            var p = new StackPanel(); p.Children.Add(Text("Review license activation", 23, "#17263C", true)); Details(p, result, 0);
            p.Children.Add(Text("Network: " + J.S(network, "name"), 16, "#17263C", true));
            var renew = new CheckBox { Content = new TextBlock { Text = "Enable automatic renewal under the provider's subscription terms", TextWrapping = TextWrapping.Wrap, MaxWidth = 410 }, IsChecked = false }; p.Children.Add(renew);
            var w = Dialog("Activate license", p); var status = Text("", 13, "#B5333D"); p.Children.Add(status); var activate = new Button { Content = "Activate for this network" }; p.Children.Add(activate); bool working = false;
            activate.Click += async delegate
            {
                if (working || !Confirm("Activate this subscription key for " + J.S(network, "name") + "? Automatic renewal: " + (renew.IsChecked == true ? "enabled" : "disabled") + ".")) return;
                working = true; activate.IsEnabled = false;
                try { await Change("POST", "/2.2/license_keys", ExtendedContracts.License(key, NetworkId, renew.IsChecked == true), false, "Subscription key activated."); working = false; w.Close(); }
                catch (Exception e) { status.Text = Message(e); } finally { working = false; activate.IsEnabled = true; }
            };
            w.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { e.Cancel = working; }; w.ShowDialog();
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
            var p = new StackPanel(); p.Children.Add(Text(title + " redemption code", 23, "#17263C", true)); var input = new TextBox { Text = code, IsReadOnly = true }; p.Children.Add(input);
            var copy = new Button { Content = "Copy redemption code" }; copy.Click += delegate { Clipboard.SetText(code); }; p.Children.Add(copy); var w = Dialog(title, p); w.Closed += delegate { input.Clear(); }; w.ShowDialog();
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
            {
                Edit("Turn on identity protection", new[] { new Field("country", "Country", "US").Help("Two-letter code, for example US, CA or GB. Use Countries to see where it's available.") }, async v =>
                {
                    string country = V(v, "country").Trim().ToUpperInvariant(); if (!Regex.IsMatch(country, "^[A-Z]{2}$")) throw new ArgumentException("Enter a two-letter country code.");
                    if (!Confirm("Turn on identity protection for this account in " + country + "?")) return;
                    await Change("POST", "/2.2/account/identity_protection/signup", J.Obj("country_code", country), false, "Identity protection activation requested.");
                }, "Turn on");
            }
        }
        private void ScanIdentityEmail()
        {
            Edit("Scan an email address", new[] { new Field("email", "Email address") }, async v =>
            {
                string email = ExtendedContracts.Email(V(v, "email")); if (preview) throw new InvalidOperationException("Sign in to run a scan.");
                if (!Confirm("Send " + email + " to eero's identity-protection service for a scan?")) return;
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
            var p = new StackPanel(); p.Children.Add(Text("Digital footprint", 23, "#17263C", true)); Details(p, result, 0);
            string report = J.S(result, "mwb_full_report_url"); if (report.Length > 0) { var b = new Button { Content = "Open detailed report at Malwarebytes" }; b.Click += delegate { OpenPartnerReport(report); }; p.Children.Add(b); }
            if (J.B(result, "email_unverified")) p.Children.Add(Text("Verify the email (step 2 on the page) to see the full results.", 14, "#64738A")); Dialog("Digital footprint", p).ShowDialog();
        }
        private void OpenPartnerReport(string url)
        {
            Uri u; if (!Uri.TryCreate(url, UriKind.Absolute, out u) || u.Scheme != "https" || u.Port != 443 || u.UserInfo.Length > 0 || !(u.Host == "malwarebytes.com" || u.Host.EndsWith(".malwarebytes.com", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("The service did not return a supported secure Malwarebytes report link.");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        private void SetupVendorsPage(object data)
        {
            Sub("Let smart-home brands set up their devices on your wifi automatically, without you typing the password.");
            var vendors = Entries(data, "vendors", "configurations"); var rows = new List<UIElement>();
            foreach (object raw in vendors)
            {
                object vendor = raw; string id = J.S(vendor, "vendor_id");
                string name = J.First(vendor, "vendor_name", "name", "display_name").NullIfEmpty() ?? Friendly(id).NullIfEmpty() ?? "Device maker";
                if (id.Length == 0) { rows.Add(InfoRow(name, OnOff(J.B(vendor, "enabled")))); continue; }
                rows.Add(SettingToggle(name, "Let " + name + " devices join your wifi during setup.", J.B(vendor, "enabled"), async delegate(bool on)
                {
                    if (!Confirm((on ? "Allow " : "Stop allowing ") + name + " to set up devices on this network?")) return;
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
            {
                Edit("ISP settings", new[] {
                    new Field("enabled", "Use PPPoE login", J.B(network, "pppoe_enabled"), "bool"),
                    new Field("replace", "Enter a new PPPoE username and password", !J.B(network, "pppoe_enabled"), "bool").When("enabled", "True").Help("Leave off to keep the credentials eero already has."),
                    new Field("username", "PPPoE username", J.S(network, "pppoe_username")).When("replace", "True"),
                    new Field("password", "PPPoE password", null, "password").When("replace", "True"),
                    new Field("vlan", "ISP VLAN ID", J.S(network, "vlan")).Help("Leave blank to turn VLAN tagging off.") }, async v =>
                {
                    bool enabled = Flag(v, "enabled"), replace = Flag(v, "replace"); if (enabled && !J.B(network, "pppoe_enabled") && !replace) throw new ArgumentException("Enter new credentials when enabling PPPoE for the first time.");
                    object body = ExtendedContracts.Pppoe(enabled, V(v, "username"), V(v, "password"), replace, V(v, "vlan")); if (!Confirm("Apply ISP settings? Incorrect PPPoE credentials or VLAN settings can disconnect the network.")) return;
                    await Change("PUT", NetResource("settings"), body, false, "ISP settings updated.");
                }, "Apply ISP settings", "Use the settings supplied by your ISP. Wrong values can disconnect your network.");
            }
        }
        private async Task UploadPortalAsset(object config, bool logo)
        {
            if (preview) throw new InvalidOperationException("Sign in to upload a portal asset.");
            string configId = ExtendedContracts.Identifier(J.First(config, "configuration_id", "id"));
            var picker = new Microsoft.Win32.OpenFileDialog { Title = logo ? "Select guest portal logo" : "Select guest terms PDF", Filter = logo ? "PNG or JPEG image|*.png;*.jpg;*.jpeg" : "PDF document|*.pdf", Multiselect = false };
            if (picker.ShowDialog(this) != true) return;
            long size = new FileInfo(picker.FileName).Length; if (size > (logo ? 5 : 10) * 1024 * 1024) throw new ArgumentException("This file is too large. Logos support up to 5 MB; PDFs up to 10 MB.");
            byte[] bytes = File.ReadAllBytes(picker.FileName); string mime = ExtendedContracts.PortalMime(picker.FileName, bytes, logo);
            if (!Confirm("Upload " + Path.GetFileName(picker.FileName) + " (" + ((bytes.Length + 1023) / 1024) + " KB) as the guest portal " + (logo ? "logo" : "terms document") + "? Guests will be able to access the uploaded asset.")) return;
            string type = logo ? "CaptivePortalLogo" : "CaptivePortalToS", ext = Path.GetExtension(picker.FileName).TrimStart('.').ToLowerInvariant();
            object upload = await api.Send("GET", FeatureUrl("file/import/upload_link?importJobType=" + type + "&fileExtension=" + ext));
            await PortalUpload.Send(upload, bytes, mime);
            await Change("POST", FeatureUrl("file/import/job"), ExtendedContracts.ImportJob(J.S(upload, "import_file_path"), logo, configId), false, "Asset uploaded and import requested. Refresh the portal after processing finishes.");
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
            if (result != null) { var body = new StackPanel(); Details(body, result, 0); page.Children.Add(Card(body, 18)); }
            else page.Children.Add(Card(Text(started ? "Waiting for results. Choose Show results above." : "No test yet. Run one above to see how fast each server answers.", 14, "#64738A"), 18));
        }
    }
    public static class PortalUpload
    {
        public static async Task Send(object upload, byte[] data, string mime, HttpMessageHandler handler = null)
        {
            Uri url = ExtendedContracts.UploadUrl(J.S(upload, "signed_upload_url")); string key = J.S(upload, "kms_key_id");
            if (key.Length == 0 || key.Length > 2048 || key.Any(Char.IsControl)) throw new InvalidDataException("The upload encryption key is missing or invalid.");
            // A separate client never forwards the eero session cookie to storage.
            using (var http = new HttpClient(handler ?? new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }))
            using (var request = new HttpRequestMessage(HttpMethod.Put, url))
            {
                http.Timeout = TimeSpan.FromSeconds(120); request.Headers.Add("x-amz-server-side-encryption", "aws:kms"); request.Headers.Add("x-amz-server-side-encryption-aws-kms-key-id", key);
                request.Content = new ByteArrayContent(data); request.Content.Headers.ContentType = new MediaTypeHeaderValue(mime);
                using (var response = await http.SendAsync(request)) if (!response.IsSuccessStatusCode) throw new InvalidOperationException("The storage service rejected the upload (" + (int)response.StatusCode + "). No import was requested.");
            }
        }
    }
}
