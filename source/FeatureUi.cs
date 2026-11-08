using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private readonly Dictionary<string, object> featureData = new Dictionary<string, object>();
        private object selectedProfile, selectedNode;
        private string FeatureUrl(string suffix) { if (String.IsNullOrEmpty(networkUrl)) throw new InvalidOperationException("Select a network first."); return networkUrl.TrimEnd('/') + "/" + suffix.TrimStart('/'); }
        private string NetResource(string name) { return Resource(network, name, FeatureUrl(name)); }
        private static string ObjectUrl(object data, string fallback = null) { return J.S(data, "url").NullIfEmpty() ?? fallback; }
        private void Navigate(string route) { NavigateTo(route, true); }
        private async void NavigateTo(string route, bool record)
        {
            if (busy) return;
            if (record) RecordHistory(route);
            CloseRailSearch(false);
            currentPage = route; RenderPage();
            if (route.Contains("/")) await Run(async delegate { await LoadFeature(route); if (currentPage == route) RenderPage(); }, "Loading " + route.Split('/').Last().ToLowerInvariant() + "…");
        }
        private void FeatureMenu(string parent, bool explanation = true) { NativeFeatureMenu(parent, explanation); }
        private async Task LoadFeature(string route)
        {
            if (preview) { featureData[route] = PreviewFeature(route); return; }
            if (!api.SignedIn) return;
            object data = null;
            try
            {
                switch (route)
                {
                    case "Help/Contact support": data = await api.Send("GET", "/2.2/account/eero_support"); break;
                    case "Settings/Account settings/Subscriptions/Billing & status": data = await api.Send("GET", "/2.2/premium/customer"); featureData["billing_customer"] = data; break;
                    case "Settings/Troubleshooting/Guided diagnostics": data = await api.Send("GET", NetResource("diagnostics")); break;
                    case "Settings/Advanced networking/Wireless settings/Fast roaming": data = await api.Send("GET", FeatureUrl("fast_transition")); break;
                    case "Settings/Advanced networking/Internet backup/Find backup wifi": data = await api.Send("GET", FeatureUrl("backup_access_points/ssid_discovery")); break;
                    case "Settings/Account settings/Subscriptions/Offers & referrals": if (!String.IsNullOrEmpty(networkUrl)) data = await api.Send("GET", "/2.4/premium/plans/" + NetworkId); break;
                    case "Settings/User permissions/Incoming access invitations": data = await api.Send("GET", "/2.2/incoming_member_promotions"); break;
                    case "Home/Add or replace eero/Automatic setup": data = await api.Send("GET", FeatureUrl("auto_discovered_eeros")); break;
                    case "Settings/Connected services/Accessories & cellular": data = await api.Send("GET", FeatureUrl("accessories/setup/status")); break;
                    case "Home/Profiles/Profile": if (selectedProfile != null) selectedProfile = data = await api.Send("GET", J.S(selectedProfile, "url")); break;
                    case "Home/eeros/Details": if (selectedNode != null) selectedNode = data = await api.Send("GET", J.S(selectedNode, "url")); break;
                    case "Settings/Network management/Pending transfers": data = await api.Send("GET", "/2.2/network_transfers/pending"); break;
                    case "Settings/Network management/Outgoing transfer": data = await api.Send("GET", NetResource("transfer")); break;
                    case "Settings/Connected services/Connected device setup": data = await api.Send("GET", FeatureUrl("simple_setup/vendors")); break;
                    case "Home/Security & privacy/Identity protection": data = await api.Send("GET", "/2.2/account/identity_protection"); break;
                    case "Settings/Advanced networking/DNS/DNS performance": object action; if (featureData.TryGetValue("dns_perf_action", out action)) data = await api.Send("GET", FeatureUrl("dns_perf_test/" + ExtendedContracts.Identifier(J.S(action)))); break;
                    case "Devices/Blocked devices": data = await api.Send("GET", FeatureUrl("blacklist")); break;
                    case "Home/Internet": data = await api.Send("GET", NetResource("speedtest")); break;
                    case "Home/Security & privacy": data = await api.Send("GET", FeatureUrl("dns_policies/network")); break;
                    case "Settings/Guest wifi network": data = await api.Send("GET", NetResource("guestnetwork")); break;
                    case "Settings/Advanced networking/Reservations & port forwarding": data = await api.Send("GET", NetResource("routing")); break;
                    case "Settings/Advanced networking/Thread": data = await api.Send("GET", NetResource("thread")); break;
                    case "Settings/User permissions": data = J.Obj("members", await api.Send("GET", FeatureUrl("members")), "invites", await api.Send("GET", FeatureUrl("invites"))); break;
                    case "Settings/Software updates": data = await api.Send("GET", NetResource("updates")); break;
                    case "Settings/Notifications": data = await api.Send("GET", FeatureUrl("notifications")); break;
                    case "Settings/Account settings": account = await api.Send("GET", "account"); data = account; break;
                    case "Settings/Advanced networking/Power saving": data = await api.Send("GET", FeatureUrl("power_saving/schedules")); break;
                    case "Settings/Advanced networking/Internet backup": data = J.Obj("status", await api.Send("GET", FeatureUrl("backupinternet")), "access_points", await api.Send("GET", FeatureUrl("backup_access_points"))); break;
                    case "Settings/Advanced networking/Multi-WAN": data = await api.Send("GET", FeatureUrl("wan/links")); break;
                    case "Activity/Data usage": data = await api.Send("GET", FeatureUrl("data_usage?") + StatsRange(usageDays) + "&cadence=daily"); break;
                    case "Activity/Security activity": data = await api.Send("GET", FeatureUrl("insights?") + StatsRange() + "&cadence=daily"); break;
                    case "Activity/Network events": data = await api.Send("GET", FeatureUrl("app_events?page_size=50")); break;
                    case "Settings/Connected services": data = await api.Send("GET", FeatureUrl("proxied_nodes")); break;
                    case "Settings/Additional wifi networks": case "Home/Security & privacy/Content filters": data = await api.Send("GET", FeatureUrl("subnets_config")); break;
                    case "Settings/Business guest wifi": data = await api.Send("GET", FeatureUrl("captive_portal/configurations")); break;
                    case "Settings/Advanced networking/Multiple static IPs": data = await api.Send("GET", "/2.3/networks/" + networkUrl.TrimEnd('/').Split('/').Last() + "/multistaticip"); break;
                }
                if (data == null) data = await LoadApkFeature(route);
                featureData[route] = data;
                FeatureAvailability.Clear(AvailabilityKey, route);
            }
            catch (ApiException e)
            {
                if (e.SessionExpired) throw;
                featureData[route] = e;
                FeatureAvailability.Mark(AvailabilityKey, route, FeatureAvailability.ReasonFor(e.Code));
            }
        }
        private void RenderFeature()
        {
            object data; featureData.TryGetValue(currentPage, out data);
            var error = data as ApiException;
            if (error != null)
            {
                // A page eero refused or couldn't load: say so plainly and offer a retry.
                var retry = RowActionAsync("Try again", async delegate { await LoadFeature(currentPage); RenderPage(); }); retry.Style = (Style)FindResource("AccentButton"); CompactAction(retry);
                bool denied = error.Code == 401 || error.Code == 403, missing = error.Code == 404;
                Group(null, SettingRow(denied ? "Your account can't open this" : missing ? "Not available for this network" : "This page couldn't load",
                    (denied ? "Only the network owner or an administrator can use this, or it needs a plan you don't have." : missing ? "Your eero hardware, software or plan may not support this feature." : "Check your internet connection, then try again.") + " eero said: " + error.Message
                    + (FeatureAvailability.ReasonFor(error.Code) != null ? " Menus will now mark this option so you know before opening it." : ""), null, retry));
                FeatureMenu(currentPage, false);
                return;
            }
            if (RenderApkFeature(data)) return;
            switch (currentPage)
            {
                case "Help/Contact support": SupportContactsPage(data); break;
                case "Settings/Account settings/Subscriptions/Billing & status": BillingOverview(); break;
                case "Settings/Account settings/Subscriptions/Business billing": OrganizationBilling(); break;
                case "Home/Chat": ChatPage(false); break;
                case "Help/Support history": ChatPage(true); break;
                case "Settings/Troubleshooting/Guided diagnostics": GuidedDiagnosticsPage(data); break;
                case "Settings/Troubleshooting/Connect a device": DeviceConnectionPage(); break;
                case "Settings/Advanced networking/Wireless settings/Fast roaming": FastRoamingPage(data); break;
                case "Settings/Advanced networking/Internet backup/Find backup wifi": BackupDiscoveryPage(data); break;
                case "Settings/Account settings/Subscriptions/Offers & referrals": SubscriptionOffersPage(data); break;
                case "Settings/Connected services/Amazon connected home": AmazonHomePage(); break;
                case "Settings/Notifications/Windows notifications": WindowsNotificationsPage(); break;
                case "Settings/User permissions/Incoming access invitations": IncomingAccessPage(data); break;
                case "Home/Add or replace eero/Find nearby eeros": BluetoothSetupPage(); break;
                case "Home/Add or replace eero/Automatic setup": AutomaticSetupPage(data); break;
                case "Home/Add or replace eero/Create a network": CreateNetworkPage(); break;
                case "Home/eeros/Placement tests": PlacementPage(); break;
                case "Home/Security & privacy/VPN": VpnPage(); break;
                case "Settings/Connected services/Accessories & cellular": AccessoriesPage(data); break;
                case "Settings/Business guest wifi/eero Insight": BusinessInsightsPage(); break;
                case "Settings/Account settings/Subscriptions/Manage subscriptions & purchases": SubscriptionWebPage(); break;
                case "Home/Internet": InternetPage(data); break;
                case "Home/eeros": NodePage(); break;
                case "Home/Profiles": ProfilesPageV2(); break;
                case "Home/Security & privacy": SecurityPage(data); break;
                case "Home/Add or replace eero": AddEeroPage(); break;
                case "Devices/Blocked devices": BlockedPage(data); break;
                case "Activity/Data usage": DataUsagePage(data); break;
                case "Activity/Security activity": InsightsPage(data); break;
                case "Activity/Network events": NetworkEventsPage(data); break;
                case "Settings/Wifi name & password": WifiPage(); break;
                case "Settings/Guest wifi network": GuestPage(data); break;
                case "Settings/Additional wifi networks": AdditionalWifiPage(data); break;
                case "Settings/Business guest wifi": BusinessPortalPage(data); break;
                case "Home/Security & privacy/Content filters": SubnetFiltersPage(data); break;
                case "Settings/Nickname & location": NicknamePage(); break;
                case "Settings/Advanced networking": FeatureMenu(currentPage); break;
                case "Settings/Advanced networking/Internet connection": WanPage(); break;
                case "Settings/Advanced networking/DHCP & NAT": DhcpPage(); break;
                case "Settings/Advanced networking/DNS": DnsPage(); break;
                case "Settings/Advanced networking/Reservations & port forwarding": RoutingPage(data); break;
                case "Settings/Advanced networking/IPv6": NetworkBooleanPage("ipv6_upstream", "IPv6", "IPv6 connectivity from your internet provider."); break;
                case "Settings/Advanced networking/UPnP": NetworkBooleanPage("upnp", "UPnP", "Allow devices to request port mappings automatically."); break;
                case "Settings/Advanced networking/Wireless settings": WirelessPage(); break;
                case "Settings/Advanced networking/Thread": ThreadPage(data); break;
                case "Settings/Advanced networking/Dynamic DNS": DdnsPage(); break;
                case "Settings/Advanced networking/Power saving": PowerPage(data); break;
                case "Settings/Advanced networking/Internet backup": BackupPage(data); break;
                case "Settings/Advanced networking/Multi-WAN": MultiWanPage(data); break;
                case "Settings/Advanced networking/Multiple static IPs": MultiStaticPage(data); break;
                case "Settings/User permissions": PermissionsPage(data); break;
                case "Settings/Software updates": UpdatesPage(data); break;
                case "Settings/Appearance": AppearancePage(); break;
                case "Settings/Notifications": NotificationsPage(data); break;
                case "Settings/Troubleshooting": TroubleshootingPage(); break;
                case "Settings/Account settings": AccountPage(); break;
                case "Settings/Account settings/Subscriptions": SubscriptionPage(); break;
                case "Settings/Connected services": ConnectedPage(data); break;
                case "Settings/Network management": NetworkManagementPage(); break;
                case "Settings/About": AboutPage(); break;
                case "Help/Service availability": PlatformPage(); break;
                case "Home/Profiles/Profile": ProfileDetailPage(selectedProfile); break;
                case "Home/eeros/Details": NodeDetailsPage(selectedNode); break;
                case "Settings/User permissions/Join a network": JoinNetworkPage(); break;
                case "Settings/Network management/Pending transfers": PendingTransfersPage(data); break;
                case "Settings/Network management/Outgoing transfer": OutgoingTransferPage(data); break;
                case "Settings/Account settings/Subscriptions/Redeem a license key": LicensePage(); break;
                case "Settings/Account settings/Subscriptions/Partner benefits": PartnerBenefitsPage(); break;
                case "Home/Security & privacy/Identity protection": IdentityProtectionPage(data); break;
                case "Settings/Connected services/Connected device setup": SetupVendorsPage(data); break;
                case "Settings/Advanced networking/Internet connection/ISP settings": IspSettingsPage(); break;
                case "Theme preview": ThemePreviewPage(); break;
                case "Product gallery": ProductGalleryPage(); break;
                case "Settings/Advanced networking/DNS/DNS performance": DnsPerformancePage(data); break;
                default: FeatureMenu(currentPage); break;
            }
        }
        internal static string Human(string key)
        {
            string label;
            if (FieldLabels.TryGetValue(key, out label)) return label;
            if (key.StartsWith("is_") && key.Length > 3) key = key.Substring(3);
            if (key.EndsWith("_bytes") && key.Length > 6) key = key.Substring(0, key.Length - 6);
            // Sentence case with known acronyms kept, e.g. "wan_ip_address" -> "WAN IP address".
            string spaced = Regex.Replace(key.Replace('_', ' ').Replace('.', ' ').Replace('-', ' '), "([a-z])([A-Z])", "$1 $2").Trim();
            var words = spaced.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(w => AcronymWord(w.ToLowerInvariant()) ?? w.ToLowerInvariant()).ToArray();
            if (words.Length == 0) return key;
            words[0] = Char.ToUpperInvariant(words[0][0]) + words[0].Substring(1);
            return String.Join(" ", words);
        }
        // Plain-language names for fields eero returns; anything not listed is turned into sentence case.
        private static readonly Dictionary<string, string> FieldLabels = new Dictionary<string, string> {
            { "ip", "IP address" }, { "mac", "MAC address" }, { "ssid", "Wifi name" }, { "up_mbps", "Upload" }, { "down_mbps", "Download" }, { "ipv6_upstream", "IPv6" }, { "led_on", "Status light" }, { "sqm", "Optimize for conferencing and gaming" },
            { "dsn", "Serial number" }, { "ips", "Servers" }, { "created_at", "Created" }, { "updated_at", "Updated" }, { "sub_header", "Subheading" }, { "access_expiration_in_minutes", "Session length (minutes)" }, { "can_update_now", "Update ready to install" }, { "update_status", "Status" }, { "target_firmware", "Latest software" }, { "mode", "Mode" }, { "rssi", "Signal" }, { "signal", "Signal" }, { "fast_transition", "Fast roaming" }, { "pppoe_enabled", "PPPoE login" }, { "pppoe_username", "PPPoE username" }, { "vlan", "VLAN ID" }, { "ipv6_lease", "IPv6 lease" }, { "ip_settings", "IP settings" },
            { "interfaces", "Ports" }, { "interface_number", "Port number" }, { "negotiated_speed", "Current speed" }, { "supported_speed", "Fastest supported speed" }, { "port_status", "Status" }, { "ethernet_address", "MAC address" }, { "capable_protocols", "Supports" },
            { "os_version", "Software version" }, { "firmware_version", "Software version" }, { "connected_clients_count", "Connected devices" }, { "ip_address", "IP address" }, { "mac_address", "MAC address" }, { "last_seen", "Last seen" }, { "last_active", "Last active" },
            { "started_at", "Started" }, { "completed_at", "Finished" }, { "finished_at", "Finished" }, { "start", "Start" }, { "end", "End" }, { "created", "Created" }, { "timestamp", "Time" }, { "frequency", "Band" }, { "channel_width", "Channel width" }, { "tx_power", "Transmit power" },
            { "nickname", "Nickname" }, { "hostname", "Device name" }, { "display_name", "Name" }, { "manufacturer", "Maker" }, { "connection_type", "Connection" }, { "wireless", "Wifi" }, { "gateway", "Gateway eero" }, { "heartbeat_ok", "Checking in with eero" },
            { "download", "Downloaded" }, { "upload", "Uploaded" }, { "blocked", "Blocked" }, { "paused", "Paused" }, { "serial", "Serial number" }, { "model", "Model" }, { "location", "Location" }, { "status", "Status" } };
        private void ObjectCards(object value, string title = null)
        {
            if (value == null) { page.Children.Add(Card(Text("eero hasn't returned this information for your network. Choose Refresh at the top right to try again; if it stays empty, this feature may not apply to your network or plan.", 14, "#64738A"), 18)); return; }
            var p = new StackPanel(); if (title != null) p.Children.Add(Text(title, 20, "#17263C", true));
            Details(p, value, 0); page.Children.Add(Card(p));
        }
        // Fields that are internal plumbing or secrets; they are never shown to people.
        private static readonly Regex HiddenDetailKey = new Regex("(^resources$|^url$|^links$|^_links$|^actions$|^position$|^metadata$|^node_actions$|token|password|secret|credential|certificate|master_key|private_key|psk|operational_dataset|software_key|invite_code|signed_upload_url|kms_key_id|mwb_full_report_url|^coupon$)", RegexOptions.IgnoreCase);
        private static bool Simple(object x) { return x == null || x is string || x is ValueType; }
        private static string ItemTitle(object item, out string titleKey)
        {
            titleKey = null;
            if (Simple(item)) return null;
            foreach (string key in new[] { "nickname", "display_name", "name", "title", "location", "ssid", "model", "version", "os_version", "date", "created", "timestamp", "type" })
            {
                string value = FriendlyValue(key, J.Get(item, key));
                if (value.Length > 0 && !value.StartsWith("/2.")) { titleKey = key; return key == "name" && Regex.IsMatch(value, "^[0-9]+$") ? "Port " + value : value; }
            }
            return null;
        }
        // Renders any eero response as labelled rows: friendly field names and values, lists as titled cards.
        private void Details(StackPanel p, object value, int depth, string skipKey = null)
        {
            if (depth > 5) return;
            var map = value as IDictionary<string, object>;
            if (map != null)
            {
                foreach (var item in map)
                {
                    if (HiddenDetailKey.IsMatch(item.Key) || InternalKey(item.Key) || item.Key == skipKey) continue;
                    var a = item.Value as IEnumerable;
                    if (item.Value is IDictionary<string, object> || (a != null && !(item.Value is string)))
                    {
                        var list = J.List(item.Value);
                        if (!(item.Value is IDictionary<string, object>) && list.All(Simple))
                        {
                            string joined = String.Join(", ", list.Take(40).Select(x => FriendlyValue(item.Key, x)).Where(s => s.Length > 0 && !s.StartsWith("/2.")));
                            if (joined.Length > 0) p.Children.Add(DetailRow(Human(item.Key), joined, false));
                            continue;
                        }
                        var child = new StackPanel { Margin = new Thickness(12, 0, 0, 8) }; Details(child, item.Value, depth + 1);
                        if (child.Children.Count == 0) continue;
                        var section = Text(Human(item.Key), 14, "#17263C", true); section.Margin = new Thickness(0, 14, 0, 4); p.Children.Add(section); p.Children.Add(child);
                    }
                    else
                    {
                        string display = FriendlyValue(item.Key, item.Value);
                        if (display.Length > 0 && display != "Unknown" && !display.StartsWith("/2.") && !display.StartsWith("http")) p.Children.Add(DetailRow(Human(item.Key), display, item.Value is bool && !YesNoKey(item.Key)));
                    }
                }
                return;
            }
            var items = J.List(value);
            if (items.Count > 0 && items.All(Simple)) p.Children.Add(Text(String.Join(", ", items.Take(60).Select(x => FriendlyValue("", x)).Where(s => s.Length > 0 && !s.StartsWith("/2."))), 13));
            else if (items.Count > 0)
            {
                int number = 0;
                foreach (object item in items.Take(60))
                {
                    number++; var card = new StackPanel(); string titleKey;
                    var head = Text(ItemTitle(item, out titleKey) ?? "Item " + number, 13, "#17263C", true); head.Margin = new Thickness(0, 0, 0, 4); card.Children.Add(head);
                    Details(card, item, depth + 1, titleKey);
                    p.Children.Add(new Border { BorderBrush = Brush("#E5EAF3"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 6), Margin = new Thickness(0, 0, 0, 8), Child = card });
                }
                if (items.Count > 60) p.Children.Add(Text("Showing the first 60 of " + items.Count + ".", 12, "#64738A"));
            }
            else if (value != null && !(value is IEnumerable)) p.Children.Add(Text(FriendlyValue("", value), 13));
            else if (value is string) p.Children.Add(Text(Friendly((string)value), 13));
            else p.Children.Add(Text("Nothing to show yet.", 13, "#64738A"));
        }
        private async Task<object> Read(string path)
        { if (preview) throw new InvalidOperationException("Sign in to load this information."); return await api.Send("GET", path); }
        private async Task Change(string method, string path, object body, bool form, string message)
        {
            if (preview) throw new InvalidOperationException("Preview mode is read only. Sign in to manage your network.");
            await api.Send(method, path, body, form);
            try
            {
                if (!String.IsNullOrEmpty(networkUrl)) await LoadNetwork();
                if (currentPage == "Home/Profiles/Profile" && selectedProfile != null) selectedProfile = await api.Send("GET", J.S(selectedProfile, "url"));
                else if (currentPage == "Home/eeros/Details" && selectedNode != null) selectedNode = await api.Send("GET", J.S(selectedNode, "url"));
                else await LoadFeature(currentPage);
                RenderPage(); SetNotice(message);
            }
            catch (Exception e) { SetNotice(message + " Refresh did not finish: " + Message(e), true); }
        }
        private sealed class Field
        {
            public string Key, Label, Type; public object Value; public string[] Choices;
            public string WhenKey, HelpText; public string[] WhenValues;
            public Field(string key, string label, object value = null, string type = "text", params string[] choices) { Key = key; Label = label; Value = value; Type = type; Choices = choices; }
            // Show this field only while another field has one of these values ("True"/"False" for switches).
            public Field When(string key, params string[] values) { WhenKey = key; WhenValues = values; return this; }
            public Field Help(string text) { HelpText = text; return this; }
        }
        private void Edit(string title, IEnumerable<Field> fields, Func<Dictionary<string, object>, Task> save, string submit = "Save", string description = null)
        {
            var body = new StackPanel(); body.Children.Add(Text(title, 24, "#17263C", true));
            if (description != null) body.Children.Add(Text(description, 13, "#64738A"));
            var readers = new Dictionary<string, Func<object>>(); var secrets = new List<PasswordBox>();
            var holders = new List<KeyValuePair<Field, StackPanel>>(); Action refresh = null;
            foreach (Field field in fields)
            {
                Field item = field;
                var holder = new StackPanel(); body.Children.Add(holder); holders.Add(new KeyValuePair<Field, StackPanel>(item, holder));
                if (item.Type == "link") { holder.Children.Add(LocalButton(item.Label, delegate { Official(J.S(item.Value)); })); continue; }
                if (item.Type == "hidden") { holder.Visibility = Visibility.Collapsed; readers[item.Key] = () => item.Value; continue; }
                if (item.Type == "bool")
                {
                    var row = new Grid { Margin = new Thickness(0, 6, 0, String.IsNullOrEmpty(item.HelpText) ? 14 : 2), Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
                    row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var label = Text(item.Label, 14); label.Margin = new Thickness(0, 0, 16, 0); label.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(label);
                    var c = ToggleSwitch(item.Value is bool && (bool)item.Value, item.Label); Grid.SetColumn(c, 1); row.Children.Add(c);
                    label.MouseLeftButtonUp += delegate { if (c.IsEnabled) c.IsChecked = c.IsChecked != true; };
                    c.Checked += delegate { if (refresh != null) refresh(); }; c.Unchecked += delegate { if (refresh != null) refresh(); };
                    holder.Children.Add(row); readers[item.Key] = () => c.IsChecked == true;
                    if (!String.IsNullOrEmpty(item.HelpText)) { var help = Text(item.HelpText, 12, "#64738A"); help.Margin = new Thickness(0, 0, 60, 14); holder.Children.Add(help); }
                    continue;
                }
                holder.Children.Add(Text(item.Label, 13, "#17263C", true));
                if (!String.IsNullOrEmpty(item.HelpText)) { var help = Text(item.HelpText, 12, "#64738A"); help.Margin = new Thickness(0, -4, 0, 2); holder.Children.Add(help); }
                if (item.Type == "choice")
                {
                    var c = new ComboBox { ItemsSource = item.Choices, SelectedItem = J.S(item.Value) }; if (c.SelectedIndex < 0) c.SelectedIndex = 0; holder.Children.Add(c); readers[item.Key] = () => J.S(c.SelectedItem);
                    c.SelectionChanged += delegate { if (refresh != null) refresh(); };
                }
                else if (item.Type == "password")
                { var c = new PasswordBox { MaxLength = 512 }; holder.Children.Add(c); secrets.Add(c); readers[item.Key] = () => c.Password; }
                else
                {
                    var c = new TextBox { Text = J.S(item.Value), MaxLength = item.Type == "multiline" || item.Type == "long" ? 4096 : 254, AcceptsReturn = item.Type == "multiline", MinHeight = item.Type == "multiline" ? 80 : 0 }; holder.Children.Add(c); readers[item.Key] = () => c.Text;
                }
            }
            refresh = delegate
            {
                // A field whose controlling field is hidden is hidden too, so nested conditions work in order.
                var hiddenKeys = new HashSet<string>();
                foreach (var pair in holders)
                {
                    Field f = pair.Key; if (f.WhenKey == null || !readers.ContainsKey(f.WhenKey)) continue;
                    string current = Convert.ToString(readers[f.WhenKey](), CultureInfo.InvariantCulture);
                    bool show = !hiddenKeys.Contains(f.WhenKey) && f.WhenValues.Any(w => String.Equals(w, current, StringComparison.OrdinalIgnoreCase));
                    pair.Value.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                    if (!show) hiddenKeys.Add(f.Key);
                }
            };
            refresh();
            var status = Text("", 13, "#B5333D"); body.Children.Add(status);
            var actions = new WrapPanel(); body.Children.Add(actions);
            var window = Dialog(title, body); bool working = false;
            window.SizeToContent = SizeToContent.Height; window.MinHeight = 240; window.MaxHeight = 780;
            var commit = new Button { Content = submit, Style = (Style)FindResource("AccentButton"), IsDefault = !fields.Any(f => f.Type == "multiline") }; actions.Children.Add(commit);
            var cancel = new Button { Content = "Cancel", IsCancel = true }; actions.Children.Add(cancel);
            commit.Click += async delegate
            {
                if (working) return;
                working = true; commit.IsEnabled = cancel.IsEnabled = false; status.Text = "Saving…";
                try { var values = readers.ToDictionary(p => p.Key, p => p.Value()); await save(values); working = false; window.Close(); }
                catch (Exception e)
                {
                    status.Text = Message(e);
                    var auth = e as ApiException;
                    if (auth != null && auth.SessionExpired) { working = false; window.Close(); api.Forget(); ClearData(); ShowLogin(false); SetNotice("Your session expired. Sign in again.", true); }
                }
                finally { working = false; commit.IsEnabled = cancel.IsEnabled = true; }
            };
            window.Closing += delegate(object s, System.ComponentModel.CancelEventArgs e) { if (working) e.Cancel = true; };
            // Commit closes after the request has completed, so allow its own close.
            window.Closed += delegate { foreach (PasswordBox secret in secrets) secret.Clear(); };
            cancel.Click += delegate { if (!working) window.Close(); };
            window.ShowDialog();
        }
        private static string V(IDictionary<string, object> values, string key) { object v; return values.TryGetValue(key, out v) ? J.S(v) : ""; }
        private static bool Flag(IDictionary<string, object> values, string key) { return V(values, key).Equals("True", StringComparison.OrdinalIgnoreCase); }
        private void Info(string title, object data)
        {
            var p = new StackPanel(); p.Children.Add(Text(title, 23, "#17263C", true));
            var body = new StackPanel(); Details(body, data, 0);
            if (body.Children.Count == 0) body.Children.Add(Text("eero didn't return any details for this.", 14, "#64738A"));
            p.Children.Add(Card(body, 16));
            var w = Dialog(title, p); w.ShowDialog();
        }
        // Navigation links look like links, so they are not mistaken for actions.
        private Button Section(string title, string route)
        {
            var b = LocalButton(title + "  ›", delegate { Navigate(route); });
            b.Style = (Style)FindResource("QuietButton"); b.ToolTip = "Open " + title;
            return b;
        }
        private void ActionRow(params UIElement[] buttons) { var row = new WrapPanel(); foreach (UIElement b in buttons) row.Children.Add(b); page.Children.Add(row); }
        private void Official(string url)
        {
            if (url == "about:blank" || !ServiceLinks.Allowed(url))
                throw new InvalidOperationException("The service returned a link outside the supported official domains.");
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        private void ShowWifiQr(string name, string password)
        {
            using (var generator = new QRCodeGenerator()) using (var code = generator.CreateQrCode(FeatureContracts.WifiQr(name, password), QRCodeGenerator.ECCLevel.Q)) using (var png = new PngByteQRCode(code))
            {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.StreamSource = new MemoryStream(png.GetGraphic(8)); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.EndInit(); bitmap.Freeze();
                var p = new StackPanel(); p.Children.Add(Text("Join " + name, 23, "#17263C", true)); p.Children.Add(new Image { Source = bitmap, Width = 320, Height = 320, Margin = new Thickness(0, 12, 0, 12) }); p.Children.Add(Text("This QR code contains your wifi credentials. Anyone who scans it can join the network.", 12, "#64738A")); Dialog("Wifi QR code", p).ShowDialog();
            }
        }
        private object PreviewFeature(string path)
        {
            if (path == "Help/Contact support") return J.Obj("contacts", new[] { J.Obj("type", "CHAT", "title", "Sample support chat", "content", "https://www.eero.com/support"), J.Obj("type", "MAIL", "title", "Sample support email", "content", "support@example.com") });
            if (path.EndsWith("Offers & referrals")) return J.Obj("plans", new[] { J.Obj("id", "sample-product", "name", "Sample eero Plus plan", "currency", "USD", "price", 999) });
            if (path.EndsWith("Guided diagnostics")) return J.Obj("status", "completed", "issue_area", "none", "issues", new object[0]);
            if (path.EndsWith("Fast roaming")) return J.Obj("fast_transition", true);
            if (path.EndsWith("Find backup wifi")) return J.Obj("status", "SUCCESS", "discovered_ssids", new[] { J.Obj("ssid", "Sample phone hotspot", "rssi", -53) });
            if (path.EndsWith("Incoming access invitations")) return J.Obj("promotions", new[] { J.Obj("invite_id", "sample-invite", "network_id", "sample", "network_name", "Sample family network", "invite_role", "admin") });
            if (path.EndsWith("Automatic setup")) return J.Obj("eeros", new[] { J.Obj("serial", "SAMPLE-SERIAL", "model", "Sample eero", "ineligible_for_zts", J.Obj("reason", "Preview example")) });
            if (path.EndsWith("Accessories & cellular")) return new[] { J.Obj("model", "Sample accessory", "status", "connected") };
            if (path.EndsWith("Guest wifi network")) return J.Obj("name", "Maple Guest", "enabled", true);
            if (path.EndsWith("Reservations & port forwarding")) return J.Obj("reservations", J.Obj("data", new[] { J.Obj("description", "Office PC", "ip", "192.168.4.20", "mac", "02:00:00:00:00:01") }), "forwards", J.Obj("data", new object[0]), "pinholes", J.Obj("data", new object[0]));
            if (path.EndsWith("Security & privacy")) return J.Obj("dns_policies", J.Obj("block_malware", true, "ad_block", true));
            if (path.EndsWith("Software updates")) return J.Obj("target_firmware", "Preview", "update_status", "up_to_date", "can_update_now", false);
            if (path.EndsWith("Notifications")) return J.Obj("device.new", true, "network.updated", true);
            if (path.EndsWith("Pending transfers")) return J.Obj("transfers", new[] { J.Obj("id", 17, "organization_name", "Sample provider", "subset_name", "Maple community", "created_at", "2026-10-22") });
            if (path.EndsWith("Outgoing transfer")) return J.Obj("recipient_email_address", "recipient@example.com", "transfer_date", "2026-10-22");
            if (path.EndsWith("Trusted certificates")) return J.Obj("data", new[] { J.Obj("serial", "SAMPLE01", "subject", "CN=Sample device", "expires", "2027-10-22") });
            if (path.EndsWith("Entitlements & trials")) return J.Obj("features", new[] { J.Obj("feature", "advanced_security", "enabled", true) }, "upsell_features", new[] { J.Obj("feature", "sample_upgrade", "tier", "plus") });
            if (path.EndsWith("Partner offers")) return J.Obj("offers", new[] { J.Obj("internal_offer_id", "sample-offer", "name", "Sample partner offer") }, "methods", new[] { J.Obj("authentication_method", "EMAIL") });
            if (path.EndsWith("Identity protection")) return J.Obj("enabled", false, "status", "Sample benefit status");
            if (path.EndsWith("Connected device setup")) return J.Obj("vendors", new[] { J.Obj("vendor_id", "sample", "name", "Sample connected-device vendor", "enabled", true) });
            if (path.EndsWith("Business guest wifi")) return J.Obj("configurations", new[] { J.Obj("configuration_id", 5, "header", "Welcome to Maple House", "sub_header", "Guest wifi", "color_theme", "Dark", "capture_email", false, "capture_name", false, "access_expiration_in_minutes", 240) });
            if (path == "Activity/Network events") return J.Obj("data", new[] {
                J.Obj("created", "2026-11-07T14:12:00Z", "event_type", "DEVICE_CONNECTED", "message", "Laptop joined the network"),
                J.Obj("created", "2026-11-07T09:40:00Z", "event_type", "NETWORK_UPDATED", "message", "eero software was updated"),
                J.Obj("created", "2026-11-06T22:00:00Z", "event_type", "PROFILE_PAUSED", "message", "Kids was paused by a schedule") });
            if (path == "Activity/Security activity") return J.Obj("series", new[] {
                J.Obj("type", "threats", "sum", 12, "values", new[] { J.Obj("time", "2026-10-16", "value", 1), J.Obj("time", "2026-10-17", "value", 3), J.Obj("time", "2026-10-18", "value", 0), J.Obj("time", "2026-10-19", "value", 4), J.Obj("time", "2026-10-20", "value", 2), J.Obj("time", "2026-10-21", "value", 1), J.Obj("time", "2026-10-22", "value", 1) }),
                J.Obj("type", "ads", "sum", 1840, "values", new[] { J.Obj("time", "2026-10-16", "value", 210), J.Obj("time", "2026-10-17", "value", 260), J.Obj("time", "2026-10-18", "value", 240), J.Obj("time", "2026-10-19", "value", 330), J.Obj("time", "2026-10-20", "value", 270), J.Obj("time", "2026-10-21", "value", 290), J.Obj("time", "2026-10-22", "value", 240) }) });
            if (path == "Activity/Data usage") return J.Obj("series", new[] { J.Obj("type", "download", "sum", 13400000000L, "values", new[] { J.Obj("time", "2026-10-16", "value", 1200000000L), J.Obj("time", "2026-10-17", "value", 2100000000L), J.Obj("time", "2026-10-18", "value", 1600000000L), J.Obj("time", "2026-10-19", "value", 2800000000L), J.Obj("time", "2026-10-20", "value", 1800000000L), J.Obj("time", "2026-10-21", "value", 2200000000L), J.Obj("time", "2026-10-22", "value", 1700000000L) }) });
            return null;
        }
    }
}
