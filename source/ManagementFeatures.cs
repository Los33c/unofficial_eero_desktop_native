using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private int usageDays = 7;
        private void ActivityMenu() { NativeActivity(); }
        private void InternetPage(object history)
        {
            Sub("Your connection to your internet provider and how fast it is.");
            bool online = IsOnline(network); string isp = J.First(network, "isp", "isp_name");
            Group("Connection",
                InfoRow("Status", online ? "● Online" : "○ " + (Friendly(J.S(network, "health", "internet", "status")).NullIfEmpty() ?? "Not reported"), online ? null : "Check the cable to your modem, then try Guided diagnostics under Settings › Troubleshooting."),
                isp.Length > 0 ? InfoRow("Internet provider", isp) : null,
                J.S(network, "gateway_ip").Length > 0 ? InfoRow("Public IP address", J.S(network, "gateway_ip")) : null);
            Group("Speed",
                InfoRow("Download", Speed("down")),
                InfoRow("Upload", Speed("up")),
                SettingRow("Speed test", "Measures your connection from the gateway eero. Takes about a minute; results then appear below.", null, RowActionAsync("Run test", StartSpeedTest)));
            var rows = new List<UIElement>();
            foreach (object test in Entries(history, "speed_tests", "data").Take(10))
            {
                string when = J.First(test, "date", "created", "created_at", "timestamp");
                string down = J.First(test, "down_mbps", "down"), up = J.First(test, "up_mbps", "up");
                double d, u; string speeds = (Double.TryParse(down, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? "↓ " + Math.Round(d) + " Mbps" : "↓ " + (J.S(test, "down", "value").NullIfEmpty() ?? "—")) + "   " + (Double.TryParse(up, NumberStyles.Float, CultureInfo.InvariantCulture, out u) ? "↑ " + Math.Round(u) + " Mbps" : "↑ " + (J.S(test, "up", "value").NullIfEmpty() ?? "—"));
                rows.Add(InfoRow(when.NullIfEmpty() ?? "Speed test", speeds));
            }
            if (rows.Count == 0) rows.Add(InfoRow("No earlier tests", null, "Run a speed test to start a history."));
            Group("Recent speed tests", rows.ToArray());
            RelatedPagesFor("Settings/Advanced networking/Internet connection", "Settings/Advanced networking/Internet backup", "Settings/Advanced networking/Multi-WAN");
        }
        private string StatsRange(int days = 7)
        {
            DateTime end = DateTime.UtcNow; DateTime start = end.AddDays(-days);
            return "start=" + Uri.EscapeDataString(start.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)) + "&end=" + Uri.EscapeDataString(end.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        }
        private void DataUsagePage(object data)
        {
            Sub("How much data your network used. How far back you can look depends on your eero plan.");
            Group(null,
                SettingRow("Period", null, usageDays == 1 ? "Last day" : "Last " + usageDays + " days", RowAction("Change", delegate
                {
                    Edit("Show data usage for", new[] { new Field("days", "Period", usageDays == 1 ? "1 day" : usageDays + " days", "choice", "1 day", "7 days", "30 days") }, async v => { usageDays = Int32.Parse(V(v, "days").Split(' ')[0]); featureData[currentPage] = await Read(FeatureUrl("data_usage?") + StatsRange(usageDays) + "&cadence=daily"); RenderPage(); }, "Show");
                })),
                SettingRow("Who used the most", "Usage for each device and profile in this period.", null, RowActionAsync("View", async delegate { Info("Usage by device and profile", await Read(FeatureUrl("data_usage/breakdown?") + StatsRange(usageDays))); })),
                SettingRow("Usage reports", "eero can send you a usage summary every week or month.", null, RowActionAsync("Change", async delegate { object settings = await Read(FeatureUrl("data_usage/report_settings")); Edit("Usage reports", new[] { new Field("cadence", "Send a report", J.S(settings, "cadence") == "monthly" ? "Monthly" : "Weekly", "choice", "Weekly", "Monthly") }, async v => { bool weekly = V(v, "cadence") == "Weekly"; await Change("PUT", FeatureUrl("data_usage/report_settings"), J.Obj("cadence", weekly ? "weekly" : "monthly", "notification_day", weekly ? 1 : 31), false, "Usage reports set to " + V(v, "cadence").ToLowerInvariant() + "."); }); })));
            AddSeriesCharts(page, data, true);
            RelatedPagesFor("Activity/Usage details");
        }
        // Recent changes on the network, newest first, as one plain line each.
        private void NetworkEventsPage(object data)
        {
            Sub("Recent things that happened on your network, like devices joining, updates and pauses. Newest first.");
            var events = Entries(data, "data", "events", "app_events");
            var rows = new List<UIElement>();
            foreach (object item in events.Take(50))
            {
                string what = J.First(item, "message", "title", "description", "summary").NullIfEmpty() ?? Friendly(J.First(item, "event_type", "type")).NullIfEmpty() ?? "Network event";
                string kind = Friendly(J.First(item, "event_type", "type", "category"));
                string whenKey = new[] { "created", "timestamp", "occurred_at", "time" }.FirstOrDefault(k => J.S(item, k).Length > 0) ?? "created";
                string when = FriendlyValue(whenKey, J.Get(item, whenKey));
                rows.Add(SettingRow(what, kind.Length > 0 && kind != what ? kind : null, when.NullIfEmpty(), RowAction("Details", delegate { Info("Network event", item); })));
            }
            if (rows.Count == 0) rows.Add(InfoRow(data == null ? "Events not loaded" : "Nothing new", null, data == null ? "eero hasn't returned your network's events. Choose Refresh at the top right to try again." : "Nothing has happened on your network recently."));
            Group(null, rows.ToArray());
            if (events.Count > 50) page.Children.Add(Text("Showing the 50 most recent events.", 12, "#64738A"));
        }
        private void InsightsPage(object data)
        {
            Sub("Threats, ads and websites eero blocked for you in the last 7 days. Needs an eero plan with Advanced Security.");
            Group(null,
                SettingRow("By device", "Which devices eero protected most.", null, RowActionAsync("View", async delegate { Info("Blocked by device", await Read(FeatureUrl("insights/devices?") + StatsRange())); })),
                SettingRow("By profile", "Blocking for each profile.", null, RowActionAsync("View", async delegate { Info("Blocked by profile", await Read(FeatureUrl("insights/profiles?") + StatsRange())); })));
            AddSeriesCharts(page, data, false);
            RelatedPagesFor("Home/Security & privacy", "Activity/Usage details");
        }
        private void PermissionsPage(object data)
        {
            Sub("People who can see and manage this network. Administrators can change every setting.");
            var people = new List<UIElement>();
            foreach (object raw in Entries(J.Get(data, "members"), "members"))
            {
                object member = raw; string id = J.First(member, "user_id", "id"); string role = J.S(member, "user_role");
                string name = J.First(member, "user_name", "name", "user_email", "email", "phone");
                var actions = new List<Button>();
                if (id.Length > 0 && role != "owner" && role != "admin") actions.Add(RowActionAsync("Make admin", async delegate { if (!Confirm("Invite " + name + " to become an administrator?")) return; await Change("POST", FeatureUrl("member_promotion"), J.Obj("member_id", ExtendedContracts.Identifier(id)), false, "Administrator invitation sent."); }));
                if (id.Length > 0 && role != "owner") actions.Add(RowActionAsync("Remove", async delegate { if (!Confirm("Remove administrator access for " + name + "?")) return; await Change("DELETE", FeatureUrl("admins/" + Uri.EscapeDataString(id)), null, false, "Administrator access removed."); }, true));
                people.Add(SettingRow(name.NullIfEmpty() ?? "Member", role == "owner" ? "Owner" : role == "admin" ? "Administrator" : role.Length > 0 ? Friendly(role) : "Member", null, actions.ToArray()));
            }
            if (people.Count == 0) people.Add(InfoRow(data == null ? "Members not loaded" : "Only you so far", null, data == null ? "Choose Refresh at the top right to try again." : "Invite someone to help manage this network."));
            var inviteButton = RowActionAsync("Create link", async delegate
            {
                if (preview) throw new InvalidOperationException("Preview mode is read only.");
                if (!Confirm("Create a link that grants administrator access to the person who accepts it?")) return;
                object invite = await api.Send("POST", FeatureUrl("invites"), RuntimeContracts.AdminInvite());
                string link = J.S(invite, "invite_url"); if (String.IsNullOrEmpty(link)) throw new InvalidOperationException("eero created a response without an invitation link. Refresh invitations before trying again.");
                var p = new StackPanel(); p.Children.Add(Text("Admin invitation", 23, "#17263C", true)); p.Children.Add(Text("Share this link only with the person you want to administer your network.", 13, "#64738A")); p.Children.Add(new TextBox { Text = link, IsReadOnly = true }); var copy = new Button { Content = "Copy invitation link" }; copy.Click += delegate { Clipboard.SetText(link); }; p.Children.Add(copy); Dialog("Admin invitation", p).ShowDialog(); await LoadFeature(currentPage); RenderPage();
            });
            people.Add(SettingRow("Invite an administrator", "Creates a link. Whoever opens it can manage this network, so share it carefully.", null, inviteButton));
            Group("People", people.ToArray());
            var pending = new List<UIElement>(); var invites = Entries(J.Get(data, "invites"), "invites");
            foreach (object raw in invites)
            {
                object invite = raw; string id = J.First(invite, "invite_id", "id"); if (id.Length == 0) continue;
                string created = J.First(invite, "created", "created_at");
                pending.Add(SettingRow(J.S(invite, "invite_nickname").NullIfEmpty() ?? "Administrator invitation", created.Length > 0 ? "Created " + created : "Waiting to be accepted", null,
                    RowAction("Rename", delegate { Edit("Invitation nickname", new[] { new Field("name", "Nickname", J.S(invite, "invite_nickname")).Help("Only you see this, to remember who the link is for.") }, async v => { ValidateName(V(v, "name")); await Change("PUT", FeatureUrl("invites/" + Uri.EscapeDataString(id)), J.Obj("invite_nickname", V(v, "name").Trim()), false, "Invitation nickname updated."); }); }),
                    RowActionAsync("Cancel", async delegate { if (!Confirm("Cancel this invitation? Its link will stop granting access.")) return; await Change("DELETE", FeatureUrl("invites/" + Uri.EscapeDataString(id)), null, false, "Invitation canceled."); }, true)));
            }
            if (pending.Count == 0) pending.Add(InfoRow("No pending invitations", null, "Invitations you create appear here until someone accepts them."));
            if (pending.Count > 1) pending.Add(SettingRow("Cancel all invitations", "Every pending link stops working.", null, RowActionAsync("Cancel all", async delegate { if (!Confirm("Cancel every pending administrator invitation for this network?")) return; await Change("POST", FeatureUrl("invites/cancel_pending_admin"), null, false, "Pending administrator invitations canceled."); }, true)));
            Group("Pending invitations", pending.ToArray());
            RelatedPages("Invitations to you");
        }
        // "available", "current" or "unknown". can_update_now only says eero would accept an install
        // request right now, so it never decides on its own whether an update exists.
        internal static string UpdateState(object data, IEnumerable<object> nodes)
        {
            if (data == null) return "unknown";
            string status = J.S(data, "update_status").ToLowerInvariant();
            bool? hasUpdate = J.Get(data, "has_update") is bool ? (bool?)J.B(data, "has_update") : J.Get(data, "update_available") is bool ? (bool?)J.B(data, "update_available") : null;
            if (hasUpdate == null && status.Length > 0) hasUpdate = status.Contains("available") || status == "pending" || status == "downloading";
            // Every eero already running the target version means there is nothing new, whatever the flags say.
            string target = NormalizeVersion(J.First(data, "update_to_firmware", "target_firmware"));
            var versions = (nodes ?? Enumerable.Empty<object>()).Select(n => NormalizeVersion(J.First(n, "os_version", "firmware_version"))).ToList();
            if (target.Length > 0 && versions.Count > 0 && versions.All(v => v == target)) return "current";
            if (hasUpdate == true) return "available";
            if (hasUpdate == false || status == "up_to_date" || status == "current") return "current";
            return "unknown";
        }
        private static string NormalizeVersion(string version)
        {
            string v = (version ?? "").Trim().ToLowerInvariant();
            if (v.StartsWith("v")) v = v.Substring(1);
            return v;
        }
        private void UpdatesPage(object data)
        {
            Sub("eero installs software updates automatically. You can install a ready update now or pick when updates happen.");
            string state = UpdateState(data, nodes);
            bool available = state == "available", ready = available && J.B(data, "can_update_now");
            string firmware = J.First(data, "update_to_firmware", "target_firmware");
            var installed = nodes.Select(n => J.First(n, "os_version", "firmware_version")).Where(v => v.Length > 0).Distinct().ToList();
            int hour; bool hasHour = Int32.TryParse(J.S(data, "preferred_update_hour"), out hour) && hour >= 0 && hour <= 23;
            var hours = Enumerable.Range(0, 24).Select(HourLabel).ToArray();
            var install = RowActionAsync("Install now", async delegate
            {
                if (!Confirm("Install the available eero software update? Your network will restart during installation.")) return;
                await Change("POST", NetResource("updates"), "", false, "Software update requested. Installation can take several minutes.");
            }, false); install.IsEnabled = ready;
            Group(null,
                InfoRow("Status", state == "current" ? "Up to date" : available ? "Update available" : "Not reported by eero"),
                installed.Count > 0 ? InfoRow("Installed software", String.Join(", ", installed), installed.Count > 1 ? "Your eeros are on different versions." : null) : null,
                available && firmware.Length > 0 ? InfoRow("New version", firmware) : null,
                SettingRow("Install update", ready ? "An update is ready. Your network restarts while it installs." : available ? "An update exists, but eero isn't ready to install it yet. It installs automatically at the time below." : "Nothing to install. Your eeros are on the latest software.", null, install),
                SettingRow("Automatic update time", "Updates install around this hour in your network's timezone.", hasHour ? HourLabel(hour) : "Not set",
                    RowAction("Change", delegate { Edit("Automatic update time", new[] { new Field("hour", "Update time", hasHour ? HourLabel(hour) : HourLabel(3), "choice", hours).Help("Pick a time when nobody is likely to be online.") }, async v => { int n = Array.IndexOf(hours, V(v, "hour")); if (n < 0) throw new ArgumentException("Choose an update time."); await Change("POST", NetResource("updates") + "/preferred_update_hour", J.Obj("preferred_update_hour", n), false, "Automatic update time set to " + hours[n] + "."); }); })));
            page.Children.Add(Text("The app asks eero to install its own firmware. It doesn't bypass eero's rollout schedule.", 13, "#64738A"));
        }
        private void NotificationsPage(object data)
        {
            Sub("Choose what eero tells you about. Phone alerts come from eero; Windows alerts come from this app.");
            NotificationToggles("Network alerts", data, FeatureUrl("notifications"), "eero hasn't reported which network alerts are on.");
            NotificationToggles("Account alerts", J.Get(account, "push_settings"), "/2.2/account/push_settings", null);
            Group("History",
                SettingRow("Recent notifications", "The last 50 alerts eero sent for this network.", null, RowActionAsync("View", async delegate { Info("Recent notifications", await Read(FeatureUrl("notifications_history?limit=50"))); })),
                SettingRow("Unread notifications", "Check whether anything is waiting, or clear the unread count.", null,
                    RowActionAsync("Check", async delegate { object value = await Read(ApiNetworkPath("notifications/has_unread")); bool unread = value is bool ? (bool)value : J.B(value, "has_unread"); SetNotice(unread ? "You have unread eero notifications." : "No unread eero notifications."); }),
                    RowActionAsync("Mark all read", async delegate { await Change("POST", FeatureUrl("notifications/mark_read"), null, false, "Notifications marked read."); })));
            RelatedPages("On this PC");
        }
        private static readonly Dictionary<string, string> NotificationLabels = new Dictionary<string, string> {
            { "device_new", "A new device joins" }, { "new_device", "A new device joins" }, { "network_updated", "eero software updated" }, { "network_offline", "Your network goes offline" }, { "network_online", "Your network comes back online" },
            { "eero_offline", "An eero goes offline" }, { "node_offline", "An eero goes offline" }, { "speed_test", "Speed test results" }, { "security", "Security alerts" }, { "data_usage", "Data usage reports" } };
        // One switch per alert eero reports; the whole set is saved together, as eero expects.
        private void NotificationToggles(string heading, object data, string url, string missing)
        {
            var map = data as IDictionary<string, object>;
            var flags = map == null ? new List<KeyValuePair<string, object>>() : map.Where(p => p.Value is bool).ToList();
            if (flags.Count == 0) { if (missing != null) Group(heading, InfoRow(heading, "Not reported", missing)); return; }
            var rows = new List<UIElement>();
            foreach (var flag in flags)
            {
                string key = flag.Key; string label; if (!NotificationLabels.TryGetValue(key.Replace('.', '_'), out label)) label = Friendly(key.Replace('.', '_'));
                rows.Add(SettingToggle(label, null, (bool)flag.Value, async delegate(bool on)
                {
                    var body = flags.ToDictionary(p => p.Key, p => (object)(p.Key == key ? on : (bool)p.Value));
                    await Change("PUT", url, body, false, label + (on ? " alerts turned on." : " alerts turned off."));
                }));
            }
            Group(heading, rows.ToArray());
        }
        private void EditReturnedBooleans(string title, object data, string url)
        {
            var map = data as IDictionary<string, object>;
            if (map == null) throw new InvalidOperationException("eero did not return editable settings.");
            var fields = map.Where(p => p.Value is bool).Select(p => new Field(p.Key, Human(p.Key), p.Value, "bool")).ToArray();
            if (fields.Length == 0) throw new InvalidOperationException("No editable notification flags were returned.");
            Edit(title, fields, async v => { var body = new Dictionary<string, object>(); foreach (Field f in fields) body[f.Key] = Flag(v, f.Key); await Change("PUT", url, body, false, "Notification preferences updated."); });
        }
        private void TroubleshootingPage()
        {
            Sub("Tools to find and fix network problems. If something isn't working, start with Guided diagnostics.");
            FeatureMenu(currentPage, false);
            string networkId = networkUrl.TrimEnd('/').Split('/').Last();
            Group("Checks & scans",
                SettingRow("Network diagnostics", "Run eero's full health check, then view what it found.", null,
                    RowActionAsync("Run", async delegate { if (preview) throw new InvalidOperationException("Sign in to run diagnostics."); await api.Send("POST", NetResource("diagnostics")); SetNotice("Diagnostics started. Choose View results in a minute."); }),
                    RowActionAsync("View results", async delegate { Info("Network diagnostics", await Read(NetResource("diagnostics"))); })),
                SettingRow("Connection to eero", "Confirms this PC can reach eero's cloud service securely.", null, RowActionAsync("Check", async delegate { await api.CheckConnection(); SetNotice("Secure connection to eero verified."); })),
                SettingRow("Network scan", "Lists what your eeros can see on your network.", null, RowActionAsync("View", async delegate { Info("Network scan", await Read(FeatureUrl("network_scan"))); })),
                SettingRow("Coverage scan", "Shows how strong the wifi signal is for each device.", null, RowActionAsync("View", async delegate { Info("Coverage scan", await Read("/2.4/networks/" + Uri.EscapeDataString(networkId) + "/placement/wifi_coverage_scan?allDevices=true")); })),
                SettingRow("eero upgrade report", "Shows which devices could get faster wifi from newer eero hardware.", null, RowActionAsync("View", async delegate { Info("Upgrade report", await Read("/2.4/networks/" + Uri.EscapeDataString(networkId) + "/placement/wifi_generation_scan?allDevices=true")); })));
            AddGroupHeading(page, "More help");
            var wireless = FeatureCatalog.Entries.Where(e => e.Path == "Settings/Advanced networking/Wireless settings").Select(e => (UIElement)ListRow(e, false)).ToList();
            wireless.Add(SettingRow("eero help articles", "Opens eero's support website in your browser.", null, RowActionAsync("Open ↗", async delegate { Official("https://www.eero.com/support"); await Task.FromResult(0); })));
            page.Children.Add(RowGroup(wireless));
        }
        private static string Contact(object value) { return value is string ? (string)value : J.First(value, "value", "email", "phone", "number"); }
        private void AccountPage()
        {
            Sub("Your eero account details and sign-in.");
            string auth = J.S(account, "auth", "type");
            string email = Contact(J.Get(account, "email")), phone = Contact(J.Get(account, "phone"));
            Group("Profile",
                SettingRow("Name", null, J.S(account, "name").NullIfEmpty() ?? "Not set",
                    RowAction("Change", delegate { Edit("Account name", new[] { new Field("name", "Name", J.S(account, "name")) }, async v => { ValidateName(V(v, "name")); await Change("PUT", "/2.2/account/name", J.Obj("name", V(v, "name").Trim()), true, "Account name updated."); }); })),
                SettingRow("Email", "eero sends a code to the new address. Enter it with Verify.", email.NullIfEmpty() ?? "Not set",
                    RowAction("Change", delegate { Edit("Account email", new[] { new Field("email", "New email address").Help("You'll get a verification code at this address.") }, async v => { string value = V(v, "email").Trim(); if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) throw new ArgumentException("Enter a valid email address."); await Change("PUT", "/2.2/account/email", J.Obj("email", value), true, "Email change requested. Choose Verify next to Email and enter the code."); }); }),
                    RowAction("Verify", delegate { VerifyContact("email"); })),
                SettingRow("Phone", "eero texts a code to the new number. Enter it with Verify.", phone.NullIfEmpty() ?? "Not set",
                    RowAction("Change", delegate { Edit("Account phone", new[] { new Field("phone", "New phone number").Help("Include the country code, for example +1 for the US and Canada.") }, async v => { await Change("PUT", "/2.2/account/phone", J.Obj("phone", PhoneInput.Normalize(V(v, "phone"))), true, "Phone change requested. Choose Verify next to Phone and enter the code."); }); }),
                    RowAction("Verify", delegate { VerifyContact("phone"); })),
                InfoRow("Sign-in method", auth == "eero" ? "Email or text code" : auth == "amazon" ? "Amazon" : auth == "sso" ? "Internet provider" : "Not reported by eero"),
                J.S(account, "role").Length > 0 ? InfoRow("Role", Friendly(J.S(account, "role"))) : null);
            Group("Email preferences",
                SettingToggle("eero marketing emails", "News, tips and offers from eero. Account and security emails are always sent.", J.B(account, "consents", "marketing_emails"), async delegate(bool on)
                { await Change("PUT", "/2.2/account/consents", J.Obj("marketing_emails", on), true, on ? "Marketing emails turned on." : "Marketing emails turned off."); }));
            RelatedPages("More account settings");
            AddGroupHeading(page, "Danger zone");
            Button deleteAccount = null;
            deleteAccount = RowAction("Delete account", delegate { Edit("Delete account", new[] { new Field("confirm", "Type DELETE MY EERO ACCOUNT to confirm") }, async v => { if (V(v, "confirm") != "DELETE MY EERO ACCOUNT") throw new ArgumentException("The confirmation text does not match."); if (!Confirm("Permanently delete your eero account and associated account data? This can remove your ability to manage networks.")) return; if (preview) throw new InvalidOperationException("Preview mode is read only."); await api.Send("DELETE", "/2.2/account"); api.Forget(); timer.Stop(); ClearData(); ShowLogin(false); SetNotice("Account deletion request accepted."); }, "Delete account"); }, true);
            page.Children.Add(RowGroup(new UIElement[] { SettingRow("Delete eero account", "Permanently removes your account and its data. You may lose access to your networks.", null, deleteAccount) }, 14));
        }
        private void VerifyContact(string type)
        { Edit("Verify new " + type, new[] { new Field("code", "Verification code") }, async v => { string code = V(v, "code").Trim(); if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[0-9]{4,10}$")) throw new ArgumentException("Enter the numeric verification code."); await Change("POST", "/2.2/account/" + type + "/verify", J.Obj("code", code), true, "Account " + type + " verified."); }, "Verify"); }
        private void SubscriptionPage()
        {
            Sub("Your eero plan, partner benefits and purchases.");
            object status = J.Get(network, "premium_status") ?? J.Get(account, "premium_status"), details = J.Get(network, "premium_details") ?? J.Get(account, "premium_details");
            string plan = J.First(details, "tier", "plan", "name");
            string state = status is string ? (string)status : J.First(status, "status", "state");
            Group("Your plan",
                SettingRow("Current plan", state.Length > 0 ? Friendly(state) : preview ? "Sample account. Sign in to see your plan." : "eero didn't return a plan summary.", plan.Length > 0 ? Friendly(plan) : "Not reported",
                    RowActionAsync("Details", async delegate { Info("Subscription details", await Read("/2.2/premium/customer")); })),
                SettingRow("About eero Plus", "What's included in eero's paid plans. Opens eero's website.", null, RowActionAsync("Open ↗", async delegate { Official("https://www.eero.com/eero-plus"); await Task.FromResult(0); })));
            AddGroupHeading(page, "Manage");
            FeatureMenu("Settings/Account settings/Subscriptions", false);
        }
        private void ConnectedPage(object data)
        {
            Sub("Amazon, Alexa and other services connected to this network.");
            Group("Status",
                J.Get(network, "amazon_account_linked") is bool ? InfoRow("Amazon account", J.B(network, "amazon_account_linked") ? "Linked" : "Not linked", "Linking is managed by eero. New links can't be created here.") : null,
                J.Get(network, "alexa_skill") is bool ? InfoRow("Alexa skill", J.B(network, "alexa_skill") ? "Enabled" : "Not enabled") : null,
                J.Get(network, "ffs") is bool ? InfoRow("Amazon Frustration-Free Setup", OnOff(J.B(network, "ffs")), "Change this under Wireless settings.") : null);
            if (J.Get(data, "enabled") is bool)
                Group("eero Built-in", SettingToggle("eero Built-in", "Lets devices with eero built in, such as some Amazon Echo models, extend your wifi network.", J.B(data, "enabled"), async delegate(bool on)
                { await Change("PUT", FeatureUrl("proxied_nodes"), J.Obj("enabled", on), false, on ? "eero Built-in turned on." : "eero Built-in turned off."); }));
            else
                Group("eero Built-in", SettingRow("eero Built-in", "Lets devices with eero built in, such as some Amazon Echo models, extend your wifi network.", "Not reported",
                    RowActionAsync("Change", async delegate { object settings = await Read(FeatureUrl("proxied_nodes")); Edit("eero Built-in", new[] { new Field("enabled", "Use eero Built-in devices", J.B(settings, "enabled"), "bool") }, async v => { await Change("PUT", FeatureUrl("proxied_nodes"), J.Obj("enabled", Flag(v, "enabled")), false, "eero Built-in setting updated."); }); })));
            RelatedPages("Connected service settings");
        }
        private void NetworkManagementPage()
        {
            Sub("Ownership, organization and removal for this network. To switch networks, use the network selector at the top.");
            Func<string, UIElement> link = path => FeatureCatalog.Entries.Where(e => e.Path == path).Select(e => (UIElement)ListRow(e, false)).FirstOrDefault();
            if (network == null)
            {
                page.Children.Add(RowGroup(new[] { link("Settings/User permissions/Join a network"), link("Settings/Network management/Pending transfers"), link("Home/Add or replace eero/Create a network") }.Where(r => r != null)));
                return;
            }
            Group("Ownership",
                SettingRow("Transfer ownership", "Give this network to another eero account. Nothing changes until they accept.", null,
                    RowAction("Transfer", delegate
                    {
                        Edit("Transfer ownership", new[] {
                            new Field("name", "Recipient name"),
                            new Field("method", "Send the invitation by", "Email", "choice", "Email", "Text message"),
                            new Field("email", "Recipient email").When("method", "Email"),
                            new Field("phone", "Recipient phone").When("method", "Text message").Help("Include the country code, for example +1 for the US and Canada.") }, async v =>
                        {
                            ValidateName(V(v, "name")); bool byEmail = V(v, "method") == "Email";
                            string email = byEmail ? V(v, "email").Trim() : "", phone = byEmail ? "" : PhoneInput.Normalize(V(v, "phone").Trim());
                            if (byEmail && !System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) throw new ArgumentException("Enter a valid email address.");
                            if (!Confirm("Send an ownership-transfer invitation to this recipient? Ownership changes when the recipient accepts.")) return;
                            await Change("POST", NetResource("transfer"), J.Obj("name", V(v, "name").Trim(), "email", email, "phone", phone), true, "Ownership transfer requested.");
                        }, "Send invitation");
                    }),
                    RowAction("Check recipient", CheckTransferRecipient)));
            RelatedPages("Transfers & organization");
            AddGroupHeading(page, "Set up");
            page.Children.Add(RowGroup(new[] { link("Home/Add or replace eero/Create a network") }.Where(r => r != null)));
            AddGroupHeading(page, "Danger zone");
            var delete = RowAction("Delete network", delegate { Edit("Delete network", new[] { new Field("confirm", "Type the current wifi name to confirm") }, async v => { if (V(v, "confirm") != J.S(network, "name")) throw new ArgumentException("The wifi name does not match."); if (!Confirm("Delete this network from your account? This will disrupt its management and may require setting it up again.")) return; if (preview) throw new InvalidOperationException("Preview mode is read only."); await api.Send("DELETE", networkUrl); featureData.Clear(); network = null; networkUrl = null; await LoadAccount(); currentPage = "Home"; ShowShell(); SetNotice("Network deleted."); }, "Delete network"); }, true);
            page.Children.Add(RowGroup(new UIElement[] { SettingRow("Delete this network", "Removes the network from your account. You may need to set up your eeros again.", null, delete) }, 14));
            page.Children.Add(Text("Full Bluetooth setup and replacing the gateway eero still need the official eero app.", 13, "#64738A"));
        }
        private void BackupPage(object data)
        {
            Sub("If your main internet goes down, eero can switch to a backup, such as a phone hotspot, nearby wifi or cellular backup.");
            object status = J.Get(data, "status"); var points = Entries(J.Get(data, "access_points"), "access_points");
            Group(null,
                SettingToggle("Internet backup", "Switch to a backup connection automatically while your internet is down.", J.B(status, "backup_internet_enabled"), async delegate(bool on)
                { await Change("PUT", FeatureUrl("backupinternet"), J.Obj("backup_internet_enabled", on), false, on ? "Internet backup turned on." : "Internet backup turned off."); }),
                SettingRow("Test backup connection", "Checks that your eero can reach the backup networks below.", null, RowActionAsync("Test", async delegate { if (preview) throw new InvalidOperationException("Sign in to test backup connectivity."); await api.Send("POST", FeatureUrl("backup_access_points/connectivity_check")); SetNotice("Backup connection test started. Choose Refresh in a minute to see the result."); })));
            var rows = new List<UIElement>();
            foreach (object raw in points)
            {
                object item = raw;
                rows.Add(SettingRow(J.S(item, "ssid").NullIfEmpty() ?? "Backup wifi", J.Get(item, "enabled") is bool && !J.B(item, "enabled") ? "Off" : "On", null,
                    RowAction("Edit", delegate { BackupEdit(item); }),
                    RowActionAsync("Remove", async delegate { if (!Confirm("Remove this backup wifi network?")) return; await Change("DELETE", FeatureUrl("backup_access_points/" + Uri.EscapeDataString(J.S(item, "uuid"))), null, false, "Backup wifi removed."); }, true)));
            }
            if (rows.Count == 0) rows.Add(InfoRow("No backup networks yet", null, "Add a phone hotspot or another wifi network your eero can reach."));
            rows.Add(SettingRow("Add a backup network", "Enter a wifi name and password, or search for networks nearby.", null,
                RowAction("Add", delegate { BackupEdit(null); }), RowAction("Find nearby", delegate { Navigate("Settings/Advanced networking/Internet backup/Find backup wifi"); })));
            if (points.Count > 1) rows.Add(SettingRow("Backup order", "Choose which backup network eero tries first.", null, RowAction("Change", delegate { BackupPriority(points); })));
            Group("Backup networks", rows.ToArray());
            Group("Devices & cellular",
                SettingRow("Devices allowed on backup", "Choose which devices can use the backup connection, to save hotspot or cellular data.", null, RowAction("Choose", ChooseBackupDevices)),
                SettingRow("Cellular backup usage", "How much data cellular backup has used.", null, RowActionAsync("View", async delegate { Info("Cellular backup usage", await Read(FeatureUrl("cellular_backup_usage"))); })),
                SettingRow("Cellular backup history", "When cellular backup turned on and off.", null, RowActionAsync("View", async delegate { Info("Cellular backup events", await Read(FeatureUrl("cellular_backup_events"))); })));
            TechnicalDetails(status);
        }
        private void BackupEdit(object data, string discoveredSsid = null)
        {
            Edit(data == null ? "Add backup wifi" : "Edit backup wifi", new[] { new Field("ssid", "Backup wifi name", discoveredSsid ?? J.S(data, "ssid")), new Field("password", "Backup wifi password", null, "password"), new Field("enabled", "Enabled", data == null || J.B(data, "enabled"), "bool") }, async v =>
            {
                ValidateSsid(V(v, "ssid")); if (data == null || V(v, "password").Length > 0) ValidatePassword(V(v, "password"));
                string id = data == null ? Guid.NewGuid().ToString() : J.S(data, "uuid");
                var body = J.Obj("ssid", V(v, "ssid").Trim(), "uuid", id); if (data != null) body["enabled"] = Flag(v, "enabled");
                if (V(v, "password").Length > 0) body["password"] = V(v, "password");
                await Change(data == null ? "POST" : "PUT", FeatureUrl("backup_access_points" + (data == null ? "" : "/" + Uri.EscapeDataString(id))), body, false, "Backup wifi saved.");
            }, "Save", data == null ? "Use a backup hotspot that your eero can reach." : "Leave the password blank to keep the existing password.");
        }
        private void MultiWanPage(object data)
        {
            Sub("Use more than one internet connection, such as cable plus fiber or cellular. Only on supported gateways and firmware.");
            string[] modeValues = { "MULTI_WAN_AUTOMATIC", "MULTI_WAN_STRICT", "MULTI_WAN_LEGACY" }, modeLabels = { "Automatic failover", "Strict", "Legacy" };
            int modeIndex = Array.IndexOf(modeValues, J.First(data, "wan_mode", "mode"));
            Group(null,
                SettingRow("Routing mode", "Automatic failover moves traffic to the next connection when one fails. Strict keeps traffic on the connection you choose.", modeIndex >= 0 ? modeLabels[modeIndex] : "Not reported",
                    RowAction("Change", delegate { Edit("Routing mode", new[] { new Field("mode", "Routing mode", modeIndex >= 0 ? modeLabels[modeIndex] : modeLabels[0], "choice", modeLabels).Help("Automatic failover suits most homes. Use Legacy only if eero support suggests it.") }, async v => { string mode = modeValues[Array.IndexOf(modeLabels, V(v, "mode"))]; if (!Confirm("Change the routing mode? Your internet may drop briefly.")) return; await Change("PUT", FeatureUrl("wan/enable_multi_wan"), J.Obj("wan_mode", mode), false, "Routing mode updated."); }); })),
                SettingRow("Find connections", "Look for internet connections plugged into your eeros.", null, RowActionAsync("Find", async delegate { Info("Discovered connections", await Read(FeatureUrl("wan/discover"))); })),
                SettingRow("Connection limits", "How many internet connections this gateway supports.", null, RowActionAsync("View", async delegate { Info("Connection limits", await Read(FeatureUrl("wan/limits"))); })));
            var rows = new List<UIElement>();
            foreach (object raw in Entries(data, "links"))
            {
                object link = raw; string id = J.S(link, "link_id"); if (id.Length == 0) continue;
                string type = Friendly(J.S(link, "connection_type"));
                rows.Add(SettingRow(J.First(link, "name", "link_id"), "Priority " + (J.S(link, "priority").NullIfEmpty() ?? "—") + (type.Length > 0 ? " · " + type : ""), null,
                    RowActionAsync("Details", async delegate { Info("Internet connection", await Read(FeatureUrl("wan/links/" + Uri.EscapeDataString(id)))); }),
                    RowAction("Edit", delegate { EditWanPriority(link, Entries(data, "links")); }),
                    RowActionAsync("Remove", async delegate { if (!Confirm("Remove this connection? It may be your active internet connection.")) return; await Change("DELETE", FeatureUrl("wan/links/" + Uri.EscapeDataString(id)), null, false, "Connection removed."); }, true)));
            }
            if (rows.Count == 0) rows.Add(InfoRow("No extra connections yet", null, data == null ? "eero hasn't returned connection details. Choose Refresh at the top right to try again." : "Add a second internet connection plugged into one of your eeros."));
            rows.Add(SettingRow("Add a connection", "Set up another internet connection on an eero Ethernet port.", null, RowAction("Add", AddWanLink)));
            Group("Internet connections", rows.ToArray());
        }
        private void AddWanLink()
        {
            {
                var nodeChoices = nodes.Where(n => J.S(n, "url").Length > 0).ToDictionary(n => J.First(n, "location", "model") + " · " + J.S(n, "url").Split('/').Last(), n => J.S(n, "url").Split('/').Last()); if (nodeChoices.Count == 0) throw new InvalidOperationException("No eero node identifiers were returned.");
                Edit("Add internet connection", new[] {
                    new Field("name", "Connection name").Help("For example Fiber or Cellular backup."),
                    new Field("node", "Gateway eero", nodeChoices.Keys.First(), "choice", nodeChoices.Keys.ToArray()),
                    new Field("port", "Ethernet port number").Help("The port the modem is plugged into."),
                    new Field("priority", "Priority", "1").Help("Lower numbers are used first."),
                    new Field("type", "Connection type", "DHCP", "choice", "DHCP", "Static IP", "PPPoE").Help("Most modems use DHCP. Use the settings your ISP gave you."),
                    new Field("vlan", "VLAN ID (optional)"),
                    new Field("ip", "Static IPv4 address").When("type", "Static IP"),
                    new Field("mask", "Subnet mask").When("type", "Static IP"),
                    new Field("gateway", "Gateway address").When("type", "Static IP"),
                    new Field("username", "PPPoE username").When("type", "PPPoE"),
                    new Field("password", "PPPoE password", null, "password").When("type", "PPPoE") }, async v =>
                {
                    ValidateName(V(v, "name")); int node, port, priority; if (!Int32.TryParse(nodeChoices[V(v, "node")], out node) || !Int32.TryParse(V(v, "port"), out port) || port < 0 || port > 48 || !Int32.TryParse(V(v, "priority"), out priority) || priority < 0 || priority > 100) throw new ArgumentException("Enter valid port and priority values.");
                    string connectionType = V(v, "type") == "Static IP" ? "STATIC_IP" : V(v, "type") == "PPPoE" ? "PPPOE" : "DHCP";
                    var body = J.Obj("name", V(v, "name").Trim(), "eero_id", node, "port_number", port, "priority", priority, "connection_type", connectionType);
                    if (connectionType == "STATIC_IP") body["static_ip"] = J.Obj("static_ip", FeatureContracts.Address(V(v, "ip"), System.Net.Sockets.AddressFamily.InterNetwork), "subnet_mask", FeatureContracts.Address(V(v, "mask"), System.Net.Sockets.AddressFamily.InterNetwork), "gateway_ip", FeatureContracts.Address(V(v, "gateway"), System.Net.Sockets.AddressFamily.InterNetwork));
                    if (connectionType == "PPPOE") { if (V(v, "username").Length == 0 || V(v, "password").Length == 0) throw new ArgumentException("Enter the PPPoE username and password supplied by your ISP."); body["pppoe"] = J.Obj("username", V(v, "username"), "password", V(v, "password")); }
                    if (V(v, "vlan").Length > 0) { int vlan; if (!Int32.TryParse(V(v, "vlan"), out vlan) || vlan < 1 || vlan > 4094) throw new ArgumentException("VLAN IDs must be between 1 and 4094."); body["vlan_id"] = vlan.ToString(); }
                    if (!Confirm("Add this " + V(v, "type") + " internet connection?")) return; await Change("POST", FeatureUrl("wan/links"), body, false, "Internet connection added.");
                }, "Add connection");
            }
        }
        private void EditWanPriority(object link, List<object> links)
        {
            var ordered = links.OrderBy(x => { int p; return Int32.TryParse(J.S(x, "priority"), out p) ? p : 0; }).ToList();
            int position = ordered.FindIndex(x => J.S(x, "link_id") == J.S(link, "link_id")) + 1;
            Edit("WAN name & priority", new[] { new Field("name", "Connection name", J.S(link, "name")), new Field("position", "Priority position (1 is highest)", position.ToString()) }, async v =>
            {
                ValidateName(V(v, "name")); int index; if (!Int32.TryParse(V(v, "position"), out index) || index < 1 || index > ordered.Count) throw new ArgumentException("Enter a priority position between 1 and " + ordered.Count + ".");
                var others = ordered.Where(x => J.S(x, "link_id") != J.S(link, "link_id")).ToList(); others.Insert(index - 1, link);
                int baseline; if (!Int32.TryParse(J.S(ordered.First(), "priority"), out baseline)) baseline = 0;
                var updates = new List<object>();
                for (int i = 0; i < others.Count; i++) { object item = others[i]; var entry = J.Obj("link_id", J.S(item, "link_id"), "name", J.S(item, "link_id") == J.S(link, "link_id") ? V(v, "name").Trim() : J.S(item, "name"), "priority", baseline + i); object metadata = J.Get(item, "metadata"); if (metadata != null) entry["metadata"] = metadata; updates.Add(entry); }
                if (!Confirm("Apply the WAN priorities? This can switch your active internet connection.")) return;
                await Change("PUT", FeatureUrl("wan/links"), J.Obj("links", updates), false, "WAN names and priorities updated.");
            });
        }
        private void HelpPage()
        {
            Sub("Fix a problem yourself, ask eero, or contact a person.");
            Group("Fix it yourself",
                SettingRow("Guided diagnostics", "Tell eero what's wrong and it checks your network for that problem.", null, RowAction("Start", delegate { Navigate("Settings/Troubleshooting/Guided diagnostics"); })),
                SettingRow("Troubleshooting tools", "Scans, network checks and setup help.", null, RowAction("Open", delegate { Navigate("Settings/Troubleshooting"); })),
                SettingRow("eero help articles", "Step-by-step guides on eero's website.", null, RowAction("Open ↗", delegate { OpenServiceWindow("eero support", "https://www.eero.com/support"); })));
            Group("Ask eero",
                SettingRow("Chat with eero", "eero's assistant answers questions about your network and can suggest changes for you to approve.", null, RowAction("Start chat", delegate { OpenChat(); })),
                SettingRow("Past conversations", "Read or continue earlier chats with eero.", null, RowAction("Open", delegate { OpenChat(true); })));
            Group("Contact a person",
                SettingRow("Contact support", "Phone, email or chat options for your account.", null, RowAction("Open", delegate { Navigate("Help/Contact support"); })),
                SettingRow("Report a problem", "Send eero a description of the problem, the affected eeros and devices, and a screenshot.", null, RowAction("Write report", delegate { Navigate("Help/Report an issue"); })),
                api.SignedIn ? SettingRow("Your support details", "What eero's support team sees for your account.", null, RowActionAsync("View", async delegate { Info("Your support details", await Read("/2.2/account/eero_support")); })) : null);
            Group("About this app",
                SettingRow("What works in this app", "Features that still need the official eero app, and why.", null, RowAction("Open", delegate { Navigate("Help/Service availability"); })),
                SettingRow("Version & every feature", "App version and a list of every page.", null, RowAction("Open", delegate { Navigate("Settings/About"); })));
        }
        private void AboutPage()
        {
            Sub("Information about this app, the PC it runs on, and the software it uses.");
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            string versionText = version.Major + "." + version.Minor + (version.Build > 0 ? "." + version.Build : "");
            string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName, appFolder = System.IO.Path.GetDirectoryName(exePath);
            string built = System.IO.File.Exists(exePath) ? System.IO.File.GetLastWriteTime(exePath).ToString("MMMM d, yyyy", CultureInfo.CurrentCulture) : "";
            string dataFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EeroDesktop");

            // Header: icon, name, version and credit.
            var hero = new Grid(); hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) }); hero.ColumnDefinitions.Add(new ColumnDefinition());
            hero.Children.Add(new System.Windows.Controls.Image { Source = ApplicationIcon.WindowImage, Width = 72, Height = 72, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center });
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = Text("eero Desktop", 26, "#17263C", true); name.Margin = new Thickness(0); words.Children.Add(name);
            var tagline = Text("An unofficial Windows app for managing your eero wifi.", 14, "#64738A"); tagline.Margin = new Thickness(0, 2, 0, 8); words.Children.Add(tagline);
            var chips = new WrapPanel();
            foreach (string chip in new[] { "Version " + versionText, "Made by Carlos B. (Los33)" })
            { var t = Text(chip, 12, "#274CDB", true); t.Margin = new Thickness(0); chips.Children.Add(new Border { Background = Brush("#EEF3FF"), CornerRadius = new CornerRadius(4), Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 8, 0), Child = t }); }
            words.Children.Add(chips); Grid.SetColumn(words, 1); hero.Children.Add(words);
            page.Children.Add(Card(hero, 22));

            string os = Environment.OSVersion.Version.Build >= 22000 ? "Windows 11" : "Windows 10";
            string webView = "Not installed";
            try { webView = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString(); } catch (Exception) { }
            string summary = "eero Desktop " + versionText + (built.Length > 0 ? " (built " + built + ")" : "") + Environment.NewLine + os + " build " + Environment.OSVersion.Version.Build + (Environment.Is64BitOperatingSystem ? " · 64-bit" : " · 32-bit") + Environment.NewLine + ".NET Framework " + Environment.Version + Environment.NewLine + "WebView2 " + webView;
            Group("This copy",
                InfoRow("Version", versionText + (built.Length > 0 ? " · built " + built : "")),
                InfoRow("Windows", os + " (build " + Environment.OSVersion.Version.Build + ")" + (Environment.Is64BitOperatingSystem ? " · 64-bit" : "")),
                InfoRow("Web component", "WebView2 " + webView, "Used for sign-in, billing and other eero web pages."),
                SettingRow("Copy app details", "Copies the lines above, handy when asking for help.", null, RowAction("Copy", delegate { Clipboard.SetText(summary); SetNotice("App details copied."); })));
            Group("Your data on this PC",
                SettingRow("Settings and saved sign-in", "Stored only for your Windows account. Your eero sign-in is encrypted by Windows when you choose Remember me.", null,
                    RowAction("Open folder", delegate { System.IO.Directory.CreateDirectory(dataFolder); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dataFolder) { UseShellExecute = true }); })),
                SettingRow("App location", appFolder, null, RowAction("Open folder", delegate { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(appFolder) { UseShellExecute = true }); })));
            Group("Good to know",
                InfoRow("Independent app", null, "Not made by, endorsed by or affiliated with eero or Amazon. It talks to the same eero service the official app uses."),
                InfoRow("What it can do", null, "Features depend on your eero hardware, software, account role and plan."));

            // Every feature as a clickable row, folded away so the details above stay on top.
            var all = new StackPanel { Visibility = Visibility.Collapsed };
            foreach (var group in FeatureCatalog.Entries.Where(e => e.Parent.Length > 0).GroupBy(e => FeatureCatalog.Root(e.Path)))
            {
                AddGroupHeading(all, group.Key);
                all.Children.Add(RowGroup(group.Select(e => (UIElement)ListRow(e, true))));
            }
            int featureCount = FeatureCatalog.Entries.Count(e => e.Parent.Length > 0);
            Button showAll = null;
            showAll = RowAction("Show", delegate
            {
                bool show = all.Visibility != Visibility.Visible; all.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                showAll.Content = show ? "Hide" : "Show";
            });
            Group("Help & features",
                SettingRow("What works in this app", "Features that still need the official eero app.", null, RowAction("Open", delegate { Navigate("Help/Service availability"); })),
                SettingRow("Help & support", "Diagnostics, chat with eero and contact options.", null, RowAction("Open", delegate { Navigate("Help"); })),
                SettingRow("Every page in the app", featureCount + " pages, grouped by section. Click one to open it.", null, showAll));
            page.Children.Add(all);

            string notices = System.IO.Path.Combine(appFolder, "THIRD-PARTY-NOTICES.txt"), webViewLicense = System.IO.Path.Combine(appFolder, "LICENSE-WebView2.txt");
            Func<string, Button> openFile = path => System.IO.File.Exists(path) ? RowAction("View", delegate { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }) : null;
            Group("Open-source & licenses",
                InfoRow("QRCoder", "MIT license", "Creates the wifi QR codes."),
                SettingRow("Microsoft Edge WebView2", "Shows eero's web pages inside the app.", "Microsoft license", openFile(webViewLicense)),
                InfoRow("WireGuard", "Installed separately", "The official Windows app used for the Guardian VPN tunnel."),
                SettingRow("All notices", System.IO.File.Exists(notices) ? "Licenses and image sources for everything included." : "Licenses and image sources are listed in THIRD-PARTY-NOTICES.txt in the release download.", null, openFile(notices)));
        }
        private void PlatformPage()
        {
            Sub("What you can do in this app, and what still needs the official eero app. Some features also depend on your plan, hardware or internet provider.");
            Group("Works in this app",
                InfoRow("Adding eeros", null, "Add by serial number, find nearby eeros with Bluetooth, automatic setup, and creating a new wired network."),
                InfoRow("Plans & billing", null, "Managing your plan, payment and purchases opens eero's own payment pages. Purchases made through another store stay with that store."),
                InfoRow("Signing in with Amazon", null, "Opens Amazon's sign-in page. Your Amazon login must already be linked to an eero account."),
                InfoRow("Guardian VPN", null, "Connect this PC under Security & privacy › VPN. Needs a plan that includes it, the official WireGuard app and administrator permission."),
                InfoRow("Chat & alerts", null, "Chat and support depend on your eero account. Windows alerts work while the app is running, including in the background."),
                InfoRow("Business & provider tools", null, "Guest portals, eero Insight, license keys, PPPoE and VLAN on an existing network, and accessory and phone-line details."));
            Group("Needs the official eero app",
                InfoRow("Full Bluetooth setup", null, "Setting up a brand-new eero entirely over Bluetooth."),
                InfoRow("Replacing the gateway eero", null, "Swapping the eero that connects to your modem."),
                InfoRow("First-time PPPoE or static IP setup", null, "Setting up a new network whose internet needs a login or fixed address."),
                InfoRow("Cellular & accessory activation", null, "Activating new cellular service or pairing new accessories."));
        }
    }
}
