using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Gtk;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private void ProfilesPageV2() { NativeProfiles(); }
        private void ProfileDetailPage(object profile) { NativeProfileDetail(profile); }
        private string ProfilePolicyUrl(object profile) { return FeatureUrl("dns_policies/profiles/" + J.S(profile, "url").TrimEnd('/').Split('/').Last()); }
        private void ProfileDevices(object profile)
        {
            var p = VBox(); p.Append(Text("Assign profile devices", 24, "#17263C", true)); p.Append(Text("Choose the devices that belong to " + J.S(profile, "name") + ".", 13, "#64738A"));
            var current = new HashSet<string>(J.List(J.Get(profile, "devices")).Select(d => J.S(d, "url")));
            var all = devices.Concat(J.List(J.Get(profile, "devices"))).Where(d => J.S(d, "url").Length > 0).GroupBy(d => J.S(d, "url")).Select(g => g.First()).ToList();
            var checks = new List<KeyValuePair<CheckButton, string>>();
            foreach (object device in all) { var check = CheckButton.NewWithLabel(Device(device).Name + " · " + J.S(device, "mac")); check.SetActive(current.Contains(J.S(device, "url"))); checks.Add(new KeyValuePair<CheckButton, string>(check, J.S(device, "url"))); p.Append(check); }
            var status = Text("", 13, "#B5333D"); p.Append(status); var save = Gtk.Button.NewWithLabel("Save device assignments"); save.SetHalign(Gtk.Align.Start); p.Append(save); var w = Dialog("Profile devices", p); bool working = false;
            save.OnClicked += async delegate
            {
                if (working) return; working = true; save.SetSensitive(false); w.Dialog.SetCanClose(false);
                try { var selected = checks.Where(c => c.Key.GetActive()).Select(c => J.Obj("url", c.Value)).ToArray(); await Change("PUT", J.S(profile, "url"), J.Obj("devices", selected), false, "Profile devices updated."); working = false; w.Close(); }
                catch (Exception e) { status.SetText(Message(e)); }
                finally { working = false; save.SetSensitive(true); w.Dialog.SetCanClose(true); }
            };
            w.ShowDialog();
        }
        private void SecurityPage(object data)
        {
            Sub("Protection for every device on your network. Some options need an eero Plus subscription.");
            var map = J.Get(data, "dns_policies") as IDictionary<string, object> ?? data as IDictionary<string, object>;
            var rows = new List<Widget>();
            if (map != null)
                foreach (var pair in map.Where(p => p.Value is bool && PolicyLabels.ContainsKey(p.Key)))
                {
                    string key = pair.Key; string[] label = PolicyLabels[key];
                    rows.Add(SettingToggle(label[0], label[1], (bool)pair.Value, async delegate(bool on)
                    { await Change("POST", FeatureUrl("dns_policies/network"), J.Obj(key, on), true, label[0] + (on ? " turned on." : " turned off.")); }));
                }
            if (rows.Count == 0) rows.Add(SettingRow("Network protection", data == null ? "eero hasn't returned your protection settings. Choose Refresh at the top right to try again." : "Your plan doesn't report protection settings.", null, RowAction("Open", delegate { EditPolicies(data, FeatureUrl("dns_policies/network")); })));
            Group("Protection for the whole network", rows.ToArray());
            object ad = J.Get(data, "ad_block_settings");
            Group("Websites & ads",
                SettingRow("Block & allow websites", "Always block a website, or let one through that a filter blocks.", null, RowActionAsync("Manage", async delegate { DomainDialog(await Read(FeatureUrl("dns_policies/advanced_content_filter")), null); })),
                ad != null ? SettingToggle("Ad blocking for profiles", "Block ads on the profiles eero lists for ad blocking.", J.B(ad, "enabled"), async delegate(bool on)
                { await Change("POST", FeatureUrl("dns_policies/adblock"), J.Obj("enable", on, "profiles", J.List(J.Get(ad, "profiles")).ToArray()), false, "Ad blocking " + (on ? "turned on." : "turned off.")); }) : null);
            RelatedPagesFor("Home/Profiles", "Home/Security & privacy/Content filters", "Activity/Security activity", "Home/Security & privacy/VPN", "Home/Security & privacy/Identity protection");
            SecurityApkActions();
        }
        // Friendly names and explanations for eero's content-filter switches.
        private static readonly Dictionary<string, string[]> PolicyLabels = new Dictionary<string, string[]> {
            { "block_malware", new[] { "Block threats", "Stops known malware, phishing and scam websites." } },
            { "ad_block", new[] { "Block ads", "Removes most ads and trackers on every device." } },
            { "block_illegal_content", new[] { "Block illegal content", "Blocks sites known for illegal activity." } },
            { "block_pornographic_content", new[] { "Block adult content", "Filters adult websites for everyone on the network." } },
            { "block_violent_content", new[] { "Block violent content", "Filters violent and gory websites." } },
            { "safe_search_enabled", new[] { "SafeSearch", "Forces safe results on Google, Bing and other search engines." } },
            { "youtube_restricted", new[] { "YouTube Restricted Mode", "Hides videos YouTube marks as mature." } },
            { "block_messaging_content", new[] { "Block messaging apps", "Blocks chat and messaging services." } },
            { "block_social_content", new[] { "Block social media", "Blocks social networks." } },
            { "block_gaming_content", new[] { "Block gaming", "Blocks online games and gaming services." } },
            { "block_streaming_content", new[] { "Block streaming", "Blocks video and music streaming." } },
            { "block_shopping_content", new[] { "Block shopping", "Blocks online stores." } } };
        private void EditPolicies(object data, string url)
        {
            var map = J.Get(data, "dns_policies") as IDictionary<string, object>;
            var unified = J.Get(data, "unified_content_filters", "dns_policies") as IDictionary<string, object>;
            if (unified != null) { if (map == null) map = new Dictionary<string, object>(); else map = new Dictionary<string, object>(map); foreach (var entry in unified) map[entry.Key] = entry.Value; }
            if (map == null) map = data as IDictionary<string, object>;
            if (map == null) throw new InvalidOperationException("eero did not return editable content-filter settings.");
            string[] allowed = { "ad_block", "block_illegal_content", "block_malware", "block_pornographic_content", "block_violent_content", "safe_search_enabled", "youtube_restricted", "block_messaging_content", "block_social_content", "block_gaming_content", "block_streaming_content", "block_shopping_content" };
            var fields = map.Where(p => p.Value is bool && (allowed.Contains(p.Key) || unified != null)).Select(p => new Field(p.Key, Human(p.Key), p.Value, "bool")).ToArray();
            if (fields.Length == 0) throw new InvalidOperationException("No editable content-filter settings were returned for this account.");
            Edit("Security & content filters", fields, async v => { var changed = new Dictionary<string, object>(); foreach (Field f in fields) if (Flag(v, f.Key) != (bool)f.Value) changed[f.Key] = Flag(v, f.Key); if (changed.Count == 0) return; await Change("POST", url, changed, true, "Security settings updated."); });
        }
        private void DomainDialog(object data, object profile)
        {
            string profileId = profile == null ? null : J.S(profile, "url").TrimEnd('/').Split('/').Last();
            var p = VBox(); p.Append(Text("Block & allow websites", 24, "#17263C", true)); p.Append(Text(profile == null ? "Applies to the whole network." : "Applies to " + J.S(profile, "name") + ".", 13, "#64738A"));
            var add = Gtk.Button.NewWithLabel("Add or remove a website rule"); add.SetHalign(Gtk.Align.Start); add.SetMarginBottom(12); p.Append(add);
            Details(p, data, 0);
            var w = Dialog("Website controls", p);
            add.OnClicked += delegate
            {
                w.Close();
                Edit("Website rule", new[] { new Field("domain", "Domain, e.g. example.com"), new Field("list", "Rule", "Block", "choice", "Block", "Allow"), new Field("remove", "Remove this rule instead of adding it", false, "bool") }, async v =>
                {
                    var body = J.Obj("domain", FeatureContracts.Domain(V(v, "domain")), "is_delete", Flag(v, "remove"));
                    if (profileId != null) body["profiles"] = new[] { profileId };
                    string path = FeatureUrl("dns_policies/" + (profileId == null ? "network/" : "profiles/") + (V(v, "list") == "Allow" ? "allowed" : "blocked"));
                    await Change("PUT", path, body, false, "Website rule updated.");
                });
            };
            w.ShowDialog();
        }
        private async Task BlockApps(object profile)
        {
            string baseUrl = ProfilePolicyUrl(profile) + "/applications"; object available = await Read(baseUrl);
            var list = Entries(available, "applications", "data");
            if (list.Count == 0) { Info("Blocked applications", available); return; }
            var fields = new List<Field>(); var current = new HashSet<string>(J.List(J.Get(profile, "premium_dns", "blocked_applications")).Select(x => J.S(x)));
            foreach (object app in list)
            {
                string id = J.First(app, "id", "slug", "name"); if (id.Length > 0) fields.Add(new Field(id, J.First(app, "display_name", "name", "slug"), current.Contains(id) || J.B(app, "is_blocked"), "bool"));
            }
            if (fields.Count == 0) { Info("Available app controls", available); return; }
            Edit("Blocked applications", fields, async v => { var selected = fields.Where(f => Flag(v, f.Key)).Select(f => f.Key).ToArray(); await Change("PUT", baseUrl + "/blocked", J.Obj("applications", selected), false, "Blocked applications updated."); });
        }
        private void NodeDetailsPage(object node)
        {
            if (node == null) { page.Append(Text("Select an eero from Home.")); return; }
            string url = J.S(node, "url"); Sub("Connection details and settings for this eero.");
            var identity = HBox(24); var photo = HardwareImage(node, 72); photo.SetSizeRequest(72, 72); photo.SetValign(Gtk.Align.Center); identity.Append(photo);
            var model = VBox(); model.SetValign(Gtk.Align.Center); model.SetHexpand(true); model.Append(Text(J.S(node, "model"), 22, "#17263C", true)); model.Append(Text(J.B(node, "gateway") ? "Gateway eero" : "Mesh eero", 13, "#64738A")); model.Append(StatusBadge(IsOnline(node) ? "Online" : "Offline", IsOnline(node))); identity.Append(model); page.Append(Card(identity, 20));
            var columns = Columns(); var information = Column(columns, 0); var settings = Column(columns, 1); page.Append(columns);
            var info = VBox(); info.Append(Text("Device information", 20, "#17263C", true));
            Detail(info, "IP address", J.First(node, "ip_address", "ip")); Detail(info, "Connected devices", J.S(node, "connected_clients_count"));
            Detail(info, "eeroOS version", J.First(node, "os_version", "firmware_version")); Detail(info, "Serial number", J.S(node, "serial"));
            Detail(info, "MAC address", J.First(node, "mac_address", "mac"));
            var technical = LocalButton("All device details", delegate { Info("eero details", node); }); Quiet(technical); technical.SetHalign(Gtk.Align.Start); technical.SetMarginTop(8); info.Append(technical); information.Append(Card(info));
            string ledUrl = Resource(node, "led_action", null), connections = Resource(node, "connections", null), serial = J.S(node, "serial");
            int brightness; bool hasBrightness = Int32.TryParse(J.S(node, "led_brightness"), out brightness);
            var lights = new List<Widget>();
            if (ledUrl != null)
            {
                lights.Add(SettingToggle("Status light", "The light on top of the eero. Turn it off if it's distracting.", J.B(node, "led_on"), async delegate(bool on) { await Change("PUT", ledUrl, J.Obj("led_on", on), true, on ? "Status light turned on." : "Status light turned off."); }));
                lights.Add(SettingRow("Light brightness", null, hasBrightness ? brightness + "%" : "Not reported", RowAction("Change", delegate { Edit("Status light brightness", new[] { new Field("brightness", "Brightness (0–100%)", J.S(node, "led_brightness")) }, async v => { int n; if (!Int32.TryParse(V(v, "brightness"), out n) || n < 0 || n > 100) throw new ArgumentException("Enter a brightness from 0 to 100."); await Change("PUT", ledUrl, J.Obj("led_brightness", n), true, "Brightness updated."); }); })));
            }
            if (J.Get(node, "nightlight") != null) lights.Add(SettingRow("Nightlight", "A soft light, on a schedule if you like.", J.B(node, "nightlight", "enabled") ? "On" : "Off", RowAction("Change", delegate { NightlightEdit(node); })));
            if (serial.Length > 0) lights.Add(SettingRow("Find this eero", "Flashes the status light so you can spot it.", null, RowAction("Flash light", delegate { IdentifyEero(serial); })));
            AddGroupHeading(settings, "Lights");
            if (lights.Count == 0) lights.Add(InfoRow("No light settings", null, "This eero doesn't report light controls."));
            settings.Append(RowGroup(lights, 14));
            AddGroupHeading(settings, "Connections & history");
            settings.Append(RowGroup(new Widget[] {
                connections != null ? SettingRow("Ethernet ports", "See what's plugged in and control each port.", null, RowActionAsync("Open", async delegate { PortDialog(node, await Read(connections)); })) : null,
                SettingRow("Software update history", "Past updates installed on this eero.", null, RowActionAsync("View", async delegate { Info("Software update history", await Read("/2.2/eeros/" + url.TrimEnd('/').Split('/').Last() + "/firmware_update/history?limit=20&offset=0")); })),
                SettingRow("Data usage", "How much data went through this eero this week.", null, RowActionAsync("View", async delegate { Info("eero data usage", await Read(ApiNetworkPath("data_usage/eeros/" + NodeId(node) + "?") + StatsRange() + "&cadence=daily")); })),
                serial.Length > 0 ? SettingRow("Placement rating", "Ask eero to rate where this eero is placed.", null, RowAction("Rate", delegate { PlacementRatingEdit(serial); })) : null }.Where(r => r != null), 14));
            NodeApkActions(node, settings, info);
            AddGroupHeading(settings, "Manage");
            settings.Append(RowGroup(new Widget[] {
                SettingRow("Restart", "Connected devices lose internet for a minute or two.", null, RowActionAsync("Restart", async delegate { if (!await Confirm("Restart this eero? Connected devices may lose internet access temporarily.")) return; await Change("POST", Resource(node, "reboot", url + "/reboot"), null, false, "Restart requested."); })),
                SettingRow("Remove from network", J.B(node, "gateway") ? "This is your gateway. Removing it takes the whole network offline." : "The eero stops being part of your wifi.", null, RowAction("Remove", delegate { Edit("Remove eero", new[] { new Field("confirm", "Type the eero location name to confirm").Help("Location: " + J.S(node, "location")) }, async v => { if (V(v, "confirm") != J.S(node, "location")) throw new ArgumentException("The location name does not match."); if (!await Confirm("Remove this eero from the network? Removing the gateway can take the entire network offline.")) return; currentPage = "Home/eeros"; selectedNode = null; await Change("DELETE", url, null, false, "eero removed."); }, "Remove eero"); }, true)) }, 14));
        }
        private void NightlightEdit(object node)
        {
            object light = J.Get(node, "nightlight");
            Edit("Nightlight", new[] { new Field("enabled", "Enable nightlight", J.B(light, "enabled"), "bool"), new Field("brightness", "Brightness (0–100)", J.S(light, "brightness_percentage")), new Field("scheduled", "Use a schedule", J.B(light, "schedule", "enabled"), "bool"), new Field("on", "On (HH:mm)", J.S(light, "schedule", "on").NullIfEmpty() ?? "20:00"), new Field("off", "Off (HH:mm)", J.S(light, "schedule", "off").NullIfEmpty() ?? "07:00") }, async v =>
            {
                int brightness; if (!Int32.TryParse(V(v, "brightness"), out brightness) || brightness < 0 || brightness > 100) throw new ArgumentException("Enter a brightness from 0 to 100.");
                FeatureContracts.Schedule("Nightlight", new[] { "MON" }, V(v, "on"), V(v, "off"), true);
                await Change("PUT", J.S(node, "url") + "/nightlight/settings", J.Obj("enabled", Flag(v, "enabled"), "brightness_percentage", brightness, "schedule", J.Obj("enabled", Flag(v, "scheduled"), "on", V(v, "on"), "off", V(v, "off"))), false, "Nightlight updated.");
            });
        }
        // Port actions eero offers, in plain words. Anything not listed (for example cable tests) is not offered here.
        private static readonly Dictionary<string, string> PortActions = new Dictionary<string, string> {
            { "ENABLE_PORT", "Turn port on" }, { "DISABLE_PORT", "Turn port off" }, { "ENABLE_DATA", "Turn data on" }, { "DISABLE_DATA", "Turn data off" },
            { "ENABLE_POE", "Turn PoE power on" }, { "DISABLE_POE", "Turn PoE power off" }, { "RESTART_POWER", "Restart PoE power" },
            { "ENABLE_PORT_SECURITY", "Turn port security on" }, { "DISABLE_PORT_SECURITY", "Turn port security off" } };
        internal static string PortState(string status)
        {
            string s = (status ?? "").ToUpperInvariant();
            if (s.Length == 0) return "Not reported";
            if (s.Contains("DISCONNECTED") || s.Contains("NOT_CONNECTED") || s == "DOWN" || s == "NO_LINK") return "Nothing plugged in";
            if (s.Contains("DISABLED")) return "Turned off";
            if (s.Contains("CONNECTED") || s == "UP") return "Connected";
            return Friendly(status);
        }
        private void PortDialog(object node, object connections)
        {
            var p = VBox(); p.Append(Text("Ethernet ports", 24, "#17263C", true));
            p.Append(Text("What's plugged into each port on this eero, how fast it's connected, and controls for each port.", 13, "#64738A"));
            var w = Dialog("Ethernet ports", p); w.Dialog.SetContentWidth(640); bool working = false; var status = Text("", 13, "#B5333D");
            System.Action begin = delegate { working = true; w.Dialog.SetCanClose(false); };
            System.Action end = delegate { working = false; w.Dialog.SetCanClose(true); };
            var ports = J.List(J.Get(connections, "ports", "interfaces"));
            if (ports.Count == 0) p.Append(Card(Text("eero didn't report any Ethernet ports for this eero.", 14, "#64738A"), 16));
            foreach (object raw in ports)
            {
                object port = raw; var section = VBox();
                string number = J.S(port, "interface_number"), name = J.S(port, "name");
                string title = name.Length > 0 && !Regex.IsMatch(name, "^[0-9]+$") ? name : "Port " + (number.NullIfEmpty() ?? name);
                string state = PortState(J.S(port, "port_status"));
                bool connected = state == "Connected";
                var label = Text(title, 18, "#17263C", true); label.SetMarginBottom(0); label.SetValign(Gtk.Align.Center);
                section.Append(Split(label, StatusBadge(state, connected)));
                var rows = VBox(); rows.SetMarginTop(10);
                string current = FriendlyValue("negotiated_speed", J.Get(port, "negotiated_speed")), max = FriendlyValue("supported_speed", J.Get(port, "supported_speed"));
                if (current.Length > 0 || max.Length > 0)
                    rows.Append(DetailRow("Speed", connected && current.Length > 0 ? current + (max.Length > 0 && max != current ? " (port can do up to " + max + ")" : "") : (max.Length > 0 ? "Up to " + max : current), false));
                string networkType = Friendly(J.S(port, "network_type")); if (!String.IsNullOrEmpty(networkType) && networkType != "Unknown") rows.Append(DetailRow("Used for", networkType, false));
                string poe = J.First(port, "poe_status", "poe_state"); if (poe.Length > 0) rows.Append(DetailRow("PoE power", Friendly(poe), false));
                string mac = J.S(port, "ethernet_address"); if (mac.Length > 0) rows.Append(DetailRow("Port MAC address", mac, false));
                section.Append(rows);
                var actions = WrapPanel(); actions.SetMarginTop(12); int actionCount = 0;
                foreach (object action in J.List(J.Get(port, "actions")))
                {
                    string type = J.S(action, "type"); string actionLabel; if (!PortActions.TryGetValue(type, out actionLabel)) continue;
                    var b = Gtk.Button.NewWithLabel(actionLabel); if (type.StartsWith("DISABLE") || type == "RESTART_POWER") Danger(b); actions.Add(b); actionCount++;
                    b.OnClicked += async delegate
                    {
                        if (working) return;
                        if (!await Confirm(actionLabel + " on " + title + "? Anything plugged into it may lose its connection or power for a moment.")) return;
                        begin(); b.SetSensitive(false);
                        try { await Change("POST", J.S(node, "url") + "/ports/" + J.S(port, "interface_number") + "/action", J.Obj("action", type), false, actionLabel + " requested for " + title + "."); end(); w.Close(); }
                        catch (Exception e) { status.SetText(Message(e)); }
                        finally { end(); b.SetSensitive(true); }
                    };
                }
                if (actionCount > 0) section.Append(actions);
                // Everything else eero reports about the port, folded away for support conversations.
                var more = VBox(); more.SetVisible(false); more.SetMarginTop(8); Details(more, port, 0);
                var toggle = Gtk.Button.NewWithLabel("More details"); Quiet(toggle); toggle.SetMarginTop(6); toggle.SetHalign(Gtk.Align.Start);
                toggle.OnClicked += delegate { bool show = !more.GetVisible(); more.SetVisible(show); toggle.SetLabel(show ? "Fewer details" : "More details"); };
                section.Append(toggle); section.Append(more);
                p.Append(Card(section, 16));
            }
            foreach (object action in J.List(J.Get(connections, "node_actions")))
            {
                string type = J.S(action, "type"); if (type != "POWER_CYCLE_ALL_PORTS" && type != "POWER_CYCLE_ALL_PORTS_AND_REBOOT") continue;
                var b = Gtk.Button.NewWithLabel(type == "POWER_CYCLE_ALL_PORTS" ? "Restart power on every port" : "Restart power on every port and the eero"); Danger(b); b.SetHalign(Gtk.Align.Start); b.SetMarginBottom(8); p.Append(b);
                b.OnClicked += async delegate
                {
                    if (working) return;
                    if (!await Confirm("Apply this action to every Ethernet port on this eero? Devices may lose power and internet access.")) return;
                    begin(); b.SetSensitive(false);
                    try { await Change("POST", J.S(node, "url") + "/action", J.Obj("action", type), false, "eero port action requested."); end(); w.Close(); }
                    catch (Exception e) { status.SetText(Message(e)); }
                    finally { end(); b.SetSensitive(true); }
                };
            }
            p.Append(status); w.ShowDialog();
        }
        // Everything about one device: what it is, then each setting next to the control that changes it.
        // Used by the Devices page panel and the device dialog; close runs before any change is sent.
        private List<Widget> DevicePanel(object device, System.Action close)
        {
            DeviceRow row = Device(device); string mac = J.S(device, "mac");
            string url = J.S(device, "url").NullIfEmpty() ?? networkUrl + "/devices/" + Uri.EscapeDataString(mac);
            bool paused = J.B(device, "paused"), blocked = J.B(device, "blocked");
            var parts = new List<Widget>();
            var facts = VBox();
            foreach (var fact in new[] { new[] { "Connection", row.Connection }, new[] { "IP address", row.IP }, new[] { "MAC address", mac }, new[] { "Maker", J.S(device, "manufacturer") }, new[] { "Connected to", J.S(device, "source", "location") } })
                if (!String.IsNullOrEmpty(fact[1])) facts.Append(DetailRow(fact[0], fact[1], false));
            parts.Add(Card(facts, 14));
            var profileChoices = profiles.Where(p => J.S(p, "url").Length > 0).ToDictionary(p => J.S(p, "name") + " · " + J.S(p, "url").Split('/').Last(), p => J.S(p, "url"));
            var names = FeatureContracts.DeviceTypes.ToDictionary(t => Human(t), t => t);
            string type = J.S(device, "device_type");
            var settings = new List<Widget> {
                SettingToggle("Internet access", paused ? "Paused. This device can't go online." : "Pause to stop this device going online.", !paused, async delegate(bool on)
                { if (!await Confirm((on ? "Resume" : "Pause") + " internet for " + row.Name + "?")) return; close(); await Mutate("PUT", url, J.Obj("paused", !on), on ? "Internet resumed." : "Internet paused."); }),
                SettingRow("Nickname", null, row.Name, RowAction("Change", delegate { close(); Edit("Device nickname", new[] { new Field("name", "Nickname", row.Name).Help("Shown instead of the device's own name.") }, async v => { ValidateName(V(v, "name")); await Mutate("PUT", url, J.Obj("nickname", V(v, "name").Trim()), "Device nickname updated."); }); })),
                profileChoices.Count > 0 ? SettingRow("Profile", "Profiles let you pause and filter groups of devices.", row.Profile.NullIfEmpty() ?? "None", RowAction("Change", delegate
                { close(); Edit("Device profile", new[] { new Field("profile", "Profile", profileChoices.Keys.FirstOrDefault(k => k.StartsWith(row.Profile + " · ")) ?? profileChoices.Keys.First(), "choice", profileChoices.Keys.ToArray()) }, async v => { await Change("PUT", url, J.Obj("profile", J.Obj("url", profileChoices[V(v, "profile")])), false, "Device profile updated."); }); })) : null,
                SettingRow("Device type", "Sets the icon eero shows for it.", FeatureContracts.DeviceTypes.Contains(type) ? Human(type) : "Not set", RowAction("Change", delegate
                { close(); string current = FeatureContracts.DeviceTypes.Contains(type) ? Human(type) : Human("generic"); Edit("Device type", new[] { new Field("type", "Type", current, "choice", names.Keys.OrderBy(n => n).ToArray()) }, async v => { await Change("PUT", FeatureUrl("devices/" + Uri.EscapeDataString(mac)), J.Obj("device_type", names[V(v, "type")]), false, "Device type updated."); }); })),
                J.Get(device, "secondary_wan_deny_access") is bool ? SettingToggle("Use internet backup", "Let this device use the backup connection when your internet is down.", !J.B(device, "secondary_wan_deny_access"), async delegate(bool on)
                { close(); await Change("PUT", "/2.3/networks/" + NetworkId + "/devices/" + Uri.EscapeDataString(mac), J.Obj("secondary_wan_deny_access", !on), false, "Backup access " + (on ? "allowed." : "turned off.")); }) : null,
                SettingRow("Data usage", "How much data it used this week.", null, RowActionAsync("View", async delegate { close(); Info("Device data usage", await Read(FeatureUrl("data_usage/devices/" + Uri.EscapeDataString(mac) + "?") + StatsRange() + "&cadence=daily")); })),
                SettingRow(blocked ? "Blocked" : "Block device", blocked ? "This device can't join your network." : "Stops it joining your network at all, on every eero.", null, RowActionAsync(blocked ? "Unblock" : "Block", async delegate
                {
                    if (!await Confirm((blocked ? "Allow " : "Block ") + row.Name + " on this network?" + (blocked ? "" : " If it's this computer, the app may lose its connection."))) return;
                    close(); await Change(blocked ? "DELETE" : "POST", FeatureUrl("blacklist" + (blocked ? "/" + Uri.EscapeDataString(mac) : "")), blocked ? null : J.Obj("mac", mac), true, blocked ? "Device unblocked." : "Device blocked.");
                }, !blocked)),
                SettingRow("All details", "Everything eero reports about this device.", null, RowAction("View", delegate { close(); Info("Device details", device); })) };
            parts.Add(RowGroup(settings.Where(s => s != null), 14));
            return parts;
        }
        private void AddEeroPage()
        {
            Sub("Grow your wifi with another eero. Plug the new eero in near an existing one first.");
            Group("Add an eero",
                SettingRow("Add by serial number", "The quickest way. The serial number is on the bottom of the eero.", null, RowAction("Add eero", delegate
                {
                    Edit("Add eero", new[] { new Field("serial", "Serial number").Help("Printed on the bottom of the eero."), new Field("location", "Where is it?").Help("For example Kitchen or Upstairs.") }, async v =>
                    {
                        string serial = V(v, "serial").Trim(); if (!System.Text.RegularExpressions.Regex.IsMatch(serial, "^[A-Za-z0-9-]{8,40}$")) throw new ArgumentException("Enter the serial number printed on the eero."); ValidateName(V(v, "location"));
                        if (!await Confirm("Add the eero with this serial number to the selected network?")) return;
                        await Change("POST", "/2.2/eeros?requireSession=true", J.Obj("serial", serial, "location", V(v, "location").Trim(), "network", networkUrl), true, "eero addition requested.");
                    }, "Add eero");
                })));
            AddGroupHeading(page, "Other ways");
            FeatureMenu(currentPage, false);
            page.Append(Text("Replacing your gateway eero and full Bluetooth setup still need the official eero app.", 13, "#64738A"));
        }
    }
}
