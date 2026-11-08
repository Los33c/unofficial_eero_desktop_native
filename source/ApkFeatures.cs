using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace EeroDesktop
{
    // Functions and settings for endpoints found in eero Android 26.13.0 that this client did not use before.
    public sealed partial class MainWindow
    {
        private string ApiNetworkPath(string suffix) { return "/2.2/networks/" + NetworkId + "/" + suffix.TrimStart('/'); }
        private static string NodeId(object node) { return ExtendedContracts.Identifier(J.S(node, "url").TrimEnd('/').Split('/').Last()); }
        private Dictionary<string, object> NodeChoices() { return nodes.Where(n => J.S(n, "serial").Length > 0).ToDictionary(n => J.First(n, "location", "model") + " · " + J.S(n, "serial"), n => n); }
        private Dictionary<string, object> DeviceChoices() { var d = new Dictionary<string, object>(); foreach (object x in devices) { string mac = J.S(x, "mac"); if (mac.Length == 0) continue; string key = J.First(x, "nickname", "hostname", "display_name", "manufacturer").NullIfEmpty() ?? mac; key += " · " + mac; if (!d.ContainsKey(key)) d[key] = x; } return d; }
        private Dictionary<string, object> ProfileChoices() { var d = new Dictionary<string, object>(); foreach (object p in profiles) { string url = J.S(p, "url"); if (url.Length == 0) continue; d[J.S(p, "name") + " · " + url.TrimEnd('/').Split('/').Last()] = p; } return d; }
        private void Pick(string title, Dictionary<string, object> choices, string empty, Func<object, Task> action)
        {
            if (choices.Count == 0) throw new InvalidOperationException(empty);
            // The chosen item is handled after this picker closes, so follow-up dialogs never stack on top of it.
            Edit(title, new[] { new Field("item", "Choose", choices.Keys.First(), "choice", choices.Keys.ToArray()) }, v => { object item = choices[V(v, "item")]; AfterEditor(async delegate { await Run(() => action(item), "Loading…"); }); return Task.FromResult(0); }, "Continue");
        }

        // ---- eero details -------------------------------------------------------------------------------------
        private void NodeApkActions(object node, StackPanel options, StackPanel info)
        {
            if (J.Get(node, "client_ap_disabled") is bool) Detail(info, "Client wifi", J.B(node, "client_ap_disabled") ? "Off on this eero (backhaul only)" : "On");
            if (J.S(node, "state").Length > 0) Detail(info, "State", Human(J.S(node, "state")));
            string serial = J.S(node, "serial");
            AddGroupHeading(options, "Advanced");
            options.Children.Add(RowGroup(new UIElement[] {
                J.Get(node, "nightlight") != null ? SettingRow("Preview nightlight", "Shows the nightlight at a brightness for a moment without saving it.", null, RowAction("Preview", delegate
                {
                    Edit("Preview nightlight", new[] { new Field("brightness", "Brightness (0–100%)", J.S(node, "nightlight", "brightness_percentage").NullIfEmpty() ?? "50") }, async v => { await Change("POST", "/2.2/eeros/" + NodeId(node) + "/nightlight/override", ApkContracts.NightlightPreview(V(v, "brightness")), true, "Nightlight preview sent."); }, "Preview");
                })) : null,
                SettingRow("Authorization", "Whether this eero is allowed on this network.", null, RowActionAsync("Check", async delegate { Info("Authorization status", await Read("/2.2/eeros/" + NodeId(node) + "/authorization_status?network_id=" + Uri.EscapeDataString(NetworkId))); })),
                serial.Length > 0 ? SettingRow("License", "For eeros supplied by a provider: when this eero's license ends.", null, RowActionAsync("Check", async delegate { Info("eero license", await Read("/2.2/eeros/" + Uri.EscapeDataString(RuntimeContracts.Serial(serial)) + "/license_expiration?network=" + Uri.EscapeDataString(NetworkId))); })) : null,
                serial.Length > 0 ? SettingRow("Setup settings from provider", "Settings your provider loaded onto this eero before setup.", null, RowActionAsync("View", async delegate { Info("Setup settings from provider", await Read("/2.2/eeros/" + Uri.EscapeDataString(RuntimeContracts.Serial(serial)) + "/pre_setup_config")); })) : null
            }.Where(r => r != null), 14));
        }
        private void IdentifyEero(string serial)
        {
            Edit("Find this eero", new[] { new Field("color1", "First color", "blue", "choice", ApkContracts.LedColors), new Field("color2", "Second color", "white", "choice", ApkContracts.LedColors), new Field("duration", "For how long (seconds)", "30"), new Field("each", "Seconds per color", "1") }, async v =>
            {
                object body = ApkContracts.LedCycle(new[] { V(v, "color1"), V(v, "color2") }, V(v, "duration"), V(v, "each"));
                await Change("POST", "/2.2/eeros/" + Uri.EscapeDataString(RuntimeContracts.Serial(serial)) + "/led_cycle", body, true, "The status light is flashing so you can find this eero.");
            }, "Flash light", "Flashes the status light through the chosen colors. Useful when several eeros look the same.");
        }
        private void PlacementRatingEdit(string serial)
        {
            Edit("Placement rating", new[] { new Field("rssi", "Signal strength RSSI in dBm (optional)"), new Field("mcs", "MCS index (optional)"), new Field("freq", "Frequency in MHz (optional)") }, async v =>
            {
                object body = ApkContracts.PlacementRating(V(v, "rssi"), V(v, "mcs"), V(v, "freq"));
                if (preview) throw new InvalidOperationException("Sign in to request a placement rating.");
                Info("Placement rating", await api.Send("POST", "/2.3/networks/" + NetworkId + "/placement_rating/" + Uri.EscapeDataString(RuntimeContracts.Serial(serial)), body));
            }, "Get rating", "eero rates this eero's placement. Measurements are optional; the mobile app fills them from a Bluetooth scan.");
        }

        // ---- Add or replace eero / Setup tools ----------------------------------------------------------------
        private void SetupToolsPage()
        {
            Sub("Extra tools for adding, replacing or checking eeros. Most people only need these when eero support asks.");
            Group("Before you add an eero",
                SettingRow("Check compatibility", "Make sure an eero works with this network before adding it.", null, RowAction("Check", CheckEeroCompatibility)),
                SettingRow("Use the default wifi channel", "Helps a new eero find your network during setup. Switches back automatically.", null, RowAction("Start", UseDefaultChannel)));
            Group("eeros linked to your account",
                SettingRow("Waiting to be set up", "eeros eero expects to join this network soon.", null, RowActionAsync("View", async delegate { Info("eeros waiting to be set up", await Read("/2.3/networks/" + NetworkId + "/pending_eeros")); })),
                SettingRow("Came in the box", "eeros sold together with this network's kit.", null, RowActionAsync("View", async delegate { Info("eeros packaged with this network", await Read(FeatureUrl("packaged_eeros"))); })),
                SettingRow("What each model supports", "Features available on each eero model on this network.", null, RowActionAsync("View", async delegate { Info("eero model capabilities", await Read("/2.2/eero_models/capabilities?networkId=" + Uri.EscapeDataString(NetworkId))); })));
            Group("After setup",
                SettingRow("Report a replacement", "Tell eero you swapped one eero for another so your settings carry over.", null, RowAction("Report", ReportEeroReplacement)),
                SettingRow("Placement rating", "Ask eero to rate where an eero is placed.", null, RowAction("Rate", delegate { var choices = NodeChoices(); Pick("Placement rating", choices, "No eeros with serial numbers were returned.", n => { PlacementRatingEdit(J.S(n, "serial")); return Task.FromResult(0); }); })));
        }
        private void CheckEeroCompatibility()
        {
            {
                Edit("Check eero compatibility", new[] { new Field("serial", "eero serial number").Help("Printed on the bottom of the eero."), new Field("version", "Software version (optional)") }, async v => { string q = ApkContracts.OuiQuery(V(v, "serial"), V(v, "version")); Info("Compatibility check", await Read(FeatureUrl("ouicheck?" + q))); }, "Check", "Checks whether this eero's hardware is recognized for this network before you add it.");
            }
        }
        private void UseDefaultChannel()
        {
            Edit("Use the default wifi channel", new[] { new Field("seconds", "For how long (minutes)", "60").Help("Up to 120 minutes. Switches back automatically after this time.") }, async v =>
            {
                int minutes; if (!Int32.TryParse(V(v, "seconds"), out minutes) || minutes < 1 || minutes > 120) throw new ArgumentException("Enter between 1 and 120 minutes.");
                object body = ApkContracts.DefaultChannel((minutes * 60).ToString());
                if (!Confirm("Move this network to its default wifi channel for " + minutes + " minutes? Devices may briefly reconnect.")) return;
                await Change("POST", FeatureUrl("node_setup"), body, true, "The network will use its default channel while you set up eeros.");
            }, "Start", "The mobile app does this before adding an eero so the new one can find the network.");
        }
        private void ReportEeroReplacement()
        {
            Edit("Report an eero replacement", new[] { new Field("old", "Serial of the eero you replaced"), new Field("new", "Serial of the new eero"), new Field("ok", "The replacement worked", true, "bool") }, async v =>
            {
                object body = ApkContracts.NodeSwap(V(v, "old"), V(v, "new"), Flag(v, "ok"));
                await Change("POST", "/2.2/eeros/networks/" + NetworkId + "/notify_swap", body, false, "eero replacement reported.");
            }, "Report", "Tells eero that one eero was swapped for another, so the network keeps its settings and history.");
        }

        // ---- Wifi / wireless -----------------------------------------------------------------------------------
        private async Task GenerateWifiPassword()
        {
            {
                if (preview) throw new InvalidOperationException("Sign in to generate a password.");
                object value = await api.Send("POST", "/2.2/util/generatepassword", J.Obj()); string password = J.S(value);
                if (password.Length == 0) password = J.First(value, "password", "value");
                // Generated passwords are only suggestions; the user applies one through Change.
                if (password.Length == 0) throw new InvalidOperationException("eero did not return a password.");
                var p = new StackPanel(); p.Children.Add(Text("Generated password", 23, "#17263C", true)); var box = new TextBox { Text = password, IsReadOnly = true, FontSize = 16 }; p.Children.Add(box);
                p.Children.Add(Text("Nothing has changed yet. Use Change wifi password to apply it.", 12, "#64738A"));
                var copy = new Button { Content = "Copy" }; copy.Click += delegate { Clipboard.SetText(password); }; p.Children.Add(copy); Dialog("Generated password", p).ShowDialog();
            }
        }
        // Passpoint is only shown when eero reports it for this network.
        private UIElement PasspointRow()
        {
            object passpoint = J.Get(network, "passpoint");
            if (!(passpoint is bool) && !(J.Get(passpoint, "enabled") is bool)) return null;
            bool current = passpoint is bool ? (bool)passpoint : J.B(passpoint, "enabled");
            return SettingToggle("Passpoint (Hotspot 2.0)", "Lets phones from supported carriers join automatically, like a public hotspot.", current, async delegate(bool on)
            {
                if (!Confirm((on ? "Turn on" : "Turn off") + " Passpoint? Devices may briefly reconnect.")) return;
                await Change("PUT", FeatureUrl("passpoint/enabled"), ApkContracts.Passpoint(on), false, "Passpoint " + (on ? "turned on." : "turned off."));
            });
        }

        // ---- Security & privacy --------------------------------------------------------------------------------
        private void SecurityApkActions()
        {
            Group("Advanced",
                SettingRow("Filter levels", "The preset filter levels eero offers for wifi networks and profiles.", null,
                    RowActionAsync("Networks", async delegate { Info("Filter levels for wifi networks", await Read(FeatureUrl("safe_filter_levels?safeFilterType=SubnetFilter"))); }),
                    RowActionAsync("Profiles", async delegate { Info("Filter levels for profiles", await Read(FeatureUrl("safe_filter_levels?safeFilterType=ProfileFilter"))); })),
                SettingRow("Allowed alias domains (CNAMEs)", "If a site you allowed still won't load because it uses another domain behind the scenes, add that domain here.", null,
                    RowAction("Whole network", delegate
                    {
                        Edit("Allowed alias domains", new[] { new Field("domains", "Domains, one per line", null, "multiline").Help("For example cdn.example.com. This replaces the current list.") }, async v => { object body = ApkContracts.Cnames(V(v, "domains")); await Change("PUT", FeatureUrl("dns_policies/network/allowed/cnames"), body, false, "Allowed alias domains updated."); }, "Save", "Lets these domains load through aliases that content filtering would otherwise block.");
                    }),
                    RowAction("Profiles", delegate
                    {
                        var choices = ProfileChoices(); if (choices.Count == 0) throw new InvalidOperationException("Create a profile first.");
                        var fields = new List<Field> { new Field("domains", "Domains, one per line", null, "multiline").Help("For example cdn.example.com.") }; fields.AddRange(choices.Keys.Select(k => new Field("p:" + k, "Apply to " + k, false, "bool")));
                        Edit("Allowed alias domains for profiles", fields, async v =>
                        {
                            var ids = choices.Where(c => Flag(v, "p:" + c.Key)).Select(c => J.S(c.Value, "url").TrimEnd('/').Split('/').Last());
                            object body = ApkContracts.ProfileCnames(V(v, "domains"), ids);
                            await Change("PUT", FeatureUrl("dns_policies/profiles/allowed/cnames"), body, false, "Allowed alias domains for profiles updated.");
                        }, "Save", "Applies the allowed alias domains to the profiles you switch on.");
                    })));
        }

        // ---- Activity / usage details --------------------------------------------------------------------------
        private void UsageDetailsPage()
        {
            Sub("Look closer at data usage and blocked threats for one eero, profile or device.");
            string range = StatsRange(usageDays);
            Func<string, string> profileId = url => ExtendedContracts.Identifier(url.TrimEnd('/').Split('/').Last());
            Group(null, SettingRow("Period", "Applies to everything on this page.", usageDays == 1 ? "Last day" : "Last " + usageDays + " days", RowAction("Change", delegate
            { Edit("Period", new[] { new Field("days", "Show the last", usageDays + (usageDays == 1 ? " day" : " days"), "choice", "1 day", "7 days", "30 days", "90 days") }, v => { usageDays = Int32.Parse(V(v, "days").Split(' ')[0]); RenderPage(); return Task.FromResult(0); }, "Show"); })));
            Group("Data usage",
                SettingRow("Each eero", "How much data went through each of your eeros.", null,
                    RowActionAsync("All eeros", async delegate { Info("Data usage by eero", await Read(ApiNetworkPath("data_usage/eeros/summary?") + range + "&cadence=daily")); }),
                    RowAction("Pick one", delegate { Pick("Data usage for an eero", NodeChoices(), "No eeros were returned.", async n => { Info("eero data usage", await Read(ApiNetworkPath("data_usage/eeros/" + NodeId(n) + "?") + range + "&cadence=daily")); }); })),
                SettingRow("A profile", "Data used by all the devices in one profile.", null,
                    RowAction("Pick one", delegate { Pick("Data usage for a profile", ProfileChoices(), "No profiles were returned.", async p => { Info("Profile data usage", await Read(ApiNetworkPath("data_usage/profiles/" + profileId(J.S(p, "url")) + "?") + range + "&cadence=daily")); }); })),
                SettingRow("Devices not in a profile", "Usage for devices you haven't put in a profile.", null,
                    RowActionAsync("Each device", async delegate { Info("Devices not in a profile", await Read(ApiNetworkPath("data_usage/unprofiled/devices?") + range)); }),
                    RowActionAsync("Total", async delegate { Info("Total for devices not in a profile", await Read(ApiNetworkPath("data_usage/unprofiled/summary?") + range + "&cadence=daily")); })));
            Group("Blocked threats",
                SettingRow("A device", "What eero blocked for one device.", null,
                    RowAction("Pick one", delegate { Pick("Blocked for a device", DeviceChoices(), "No devices were returned.", async d => { Info("Blocked for this device", await Read(ApiNetworkPath("insights/devices/" + Uri.EscapeDataString(FeatureContracts.Mac(J.S(d, "mac"))) + "?") + range + "&cadence=daily")); }); })),
                SettingRow("A profile", "What eero blocked for a profile, in total or per device.", null,
                    RowAction("Total", delegate { Pick("Blocked for a profile", ProfileChoices(), "No profiles were returned.", async p => { Info("Blocked for this profile", await Read(ApiNetworkPath("insights/profiles/" + profileId(J.S(p, "url")) + "?") + range + "&cadence=daily")); }); }),
                    RowAction("Per device", delegate { Pick("Blocked per device in a profile", ProfileChoices(), "No profiles were returned.", async p => { Info("Blocked per device", await Read(ApiNetworkPath("insights/profiles/" + profileId(J.S(p, "url")) + "/devices?") + range)); }); })));
        }

        // ---- Notifications / permissions / backup / guest portal / transfer -----------------------------------
        private void ChooseBackupDevices()
        {
            {
                var choices = DeviceChoices(); if (choices.Count == 0) throw new InvalidOperationException("No devices were returned.");
                var fields = choices.Select(c => new Field("d:" + c.Key, c.Key, !J.B(c.Value, "secondary_wan_deny_access"), "bool")).ToList();
                Edit("Backup internet access", fields, async v =>
                {
                    var changed = choices.Where(c => Flag(v, "d:" + c.Key) == J.B(c.Value, "secondary_wan_deny_access")).ToList();
                    if (changed.Count == 0) return;
                    if (!Confirm("Update backup internet access for " + changed.Count + " device(s)?")) return;
                    for (int i = 0; i < changed.Count; i++)
                    {
                        object body = ApkContracts.SecondaryWan(J.S(changed[i].Value, "mac"), Flag(v, "d:" + changed[i].Key));
                        if (i < changed.Count - 1) await api.Send("PUT", "/2.3/networks/" + NetworkId + "/devices/secondary_wan_config", body);
                        else await Change("PUT", "/2.3/networks/" + NetworkId + "/devices/secondary_wan_config", body, false, "Backup internet access updated.");
                    }
                }, "Save", "Devices that are switched on may use the backup connection while your main internet is down.");
            }
        }
        private void CheckTransferRecipient()
        { Edit("Check transfer recipient", new[] { new Field("contact", "Recipient email or phone") }, async v => { Info("Recipient", await Read("/2.2/transfer/has_networks?" + ApkContracts.TransferTarget(V(v, "contact")))); }, "Check", "Shows whether the recipient already has an eero account and networks before you send a transfer."); }

        // ---- Network flags -------------------------------------------------------------------------------------
        private void NetworkFlagsPage()
        {
            Sub("Hidden switches eero support uses to troubleshoot. Leave these alone unless eero support asks you to change one.");
            object flags = J.Get(network, "flags"), temporary = J.Get(network, "temporary_flags");
            Func<string, object, Task> setFlag = async delegate(string flag, object value)
            {
                string name = ApkContracts.FlagName(flag);
                if (!Confirm("Set network flag " + name + "? Unknown flags can change how the network behaves.")) return;
                await Change("PUT", FeatureUrl("flags/" + name), ApkContracts.FlagValue(J.S(value)), true, "Network flag " + name + " updated.");
            };
            var rows = new List<UIElement>();
            var map = flags as IDictionary<string, object>;
            if (map != null)
                foreach (var pair in map)
                {
                    string key = pair.Key;
                    if (pair.Value is bool) rows.Add(SettingToggle(key, null, (bool)pair.Value, async delegate(bool on) { await setFlag(key, on ? "true" : "false"); }));
                    else rows.Add(InfoRow(key, J.S(pair.Value)));
                }
            if (rows.Count == 0) rows.Add(InfoRow("No flags reported", null, "That's normal. There's nothing to do here unless eero support asks."));
            rows.Add(SettingRow("Set a flag by name", "Only with a flag name and value from eero support.", null, RowAction("Set", delegate
            {
                Edit("Set network flag", new[] { new Field("name", "Flag name").Help("Exactly as eero support gave it to you."), new Field("value", "Value").Help("Usually true or false.") }, async v => { await setFlag(V(v, "name"), V(v, "value")); }, "Set");
            })));
            Group("Network flags", rows.ToArray());
            if (temporary != null) { AddGroupHeading(page, "Temporary flags"); ObjectCards(temporary); }
        }

        // ---- Organization & community --------------------------------------------------------------------------
        private void OrganizationPage()
        {
            Sub("For networks run by a business, building or internet provider. Most home networks don't need anything here.");
            string type = J.First(network, "network_customer_type", "network_type").NullIfEmpty() ?? "Residential";
            Group("This network",
                SettingRow("Network type", "Business and apartment-building (MDU) types change which features and plans apply.", Friendly(type), RowAction("Change", delegate
                {
                    Edit("Network type", new[] { new Field("type", "Network type", type, "choice", ApkContracts.CustomerTypes) }, async v =>
                    {
                        object body = ApkContracts.CustomerType(V(v, "type"));
                        if (!Confirm("Change this network's type to " + V(v, "type") + "? Business and MDU types change which features and plans apply.")) return;
                        await Change("PUT", FeatureUrl("network_customer_type"), body, true, "Network type updated.");
                    }, "Save");
                })),
                SettingRow("Community", "The building or community this network belongs to, if any.", null, RowActionAsync("View", async delegate { Info("Community", await Read(FeatureUrl("community"))); })),
                SettingRow("Provider branding", "Branding from the internet provider that supplied your eeros.", null, RowActionAsync("View", async delegate { Info("Provider branding", await Read(FeatureUrl("cobranding"))); })));
            Group("Your organization",
                SettingRow("Managed networks", "A summary of the networks your organization manages.", null, RowActionAsync("View", async delegate { Info("Managed networks", await Read("/2.2/managed_summary")); })),
                SettingRow("Professionally managed networks", "Networks managed by an installer or provider. You can filter by name.", null, RowAction("Search", delegate { Edit("Professionally managed networks", new[] { new Field("filter", "Network name contains (optional)") }, async v => { string f = V(v, "filter").Trim(); if (f.Length > 64) throw new ArgumentException("Enter up to 64 characters."); Info("Professionally managed networks", await Read("/2.2/organizations/self/networks/pro_managed" + (f.Length > 0 ? "?network_name_filter=" + Uri.EscapeDataString(f) : ""))); }, "Show"); })),
                SettingRow("Units", "Apartments or spaces in your organization, and their unit IDs.", null, RowAction("Search", delegate { Edit("Units", new[] { new Field("search", "Search (optional)") }, async v => { string s = V(v, "search").Trim(); if (s.Length > 64) throw new ArgumentException("Enter up to 64 characters."); Info("Units", await Read("/2.2/organizations/self/subsets?limit=50" + (s.Length > 0 ? "&search=" + Uri.EscapeDataString(s) : ""))); }, "Show"); })),
                SettingRow("Assign an eero to a unit", "Link an eero to an apartment or space. Look up the unit ID with Units first.", null, RowAction("Assign", delegate
                {
                    Edit("Assign eero to unit", new[] { new Field("subset", "Unit ID").Help("Find it with Units on this page."), new Field("serial", "eero serial number").Help("Printed on the bottom of the eero."), new Field("unit", "Unit name").Help("For example 4B."), new Field("description", "Description (optional)") }, async v =>
                    {
                        string subset = ExtendedContracts.Identifier(V(v, "subset").Trim()); object body = ApkContracts.SubsetAssociation(V(v, "serial"), V(v, "unit"), V(v, "description"));
                        if (!Confirm("Assign this eero to the selected unit?")) return;
                        await Change("PUT", "/2.2/organizations/self/subsets/" + subset + "/associate", body, false, "eero assigned to the unit.");
                    }, "Assign");
                })));
            AddGroupHeading(page, "Danger zone");
            page.Children.Add(RowGroup(new UIElement[] { SettingRow("Holding state", "Pauses the network while it's being linked or transferred. Only use this if eero or your provider tells you to.", null, RowActionAsync("Put on hold", async delegate
            {
                if (!Confirm("Put this network in a holding state? eero uses this while a network is being linked or transferred, and the network may stop working normally until it is linked again.")) return;
                if (!Confirm("Are you sure? Continue only if eero or your provider told you to.")) return;
                await Change("POST", FeatureUrl("holding_state"), null, false, "Network holding state requested.");
            }, true)) }, 14));
        }

        // ---- Account -------------------------------------------------------------------------------------------
        private void TrustPage(object data)
        {
            Sub("Certificates your eero account trusts, for example for a company network. Only add certificates from a source you trust.");
            var rows = new List<UIElement>();
            foreach (object raw in Entries(data, "certificates", "trust"))
            {
                object item = raw; string serial = J.First(item, "serial", "certificate_serial"); if (serial.Length == 0) continue;
                string name = J.First(item, "name", "subject", "common_name"), expires = J.First(item, "not_after", "expires_at", "expiration");
                rows.Add(SettingRow(name.NullIfEmpty() ?? "Certificate " + serial, "Serial " + serial + (expires.Length > 0 ? " · Expires " + expires : ""), null,
                    RowAction("Replace", delegate { TrustEdit(serial); }),
                    RowActionAsync("Remove", async delegate { if (!Confirm("Remove trusted certificate " + serial + "?")) return; await Change("DELETE", "/2.2/account/trust/" + Uri.EscapeDataString(ApkContracts.Token(serial, "certificate serial")), null, false, "Certificate removed."); }, true)));
            }
            if (rows.Count == 0) rows.Add(InfoRow("No trusted certificates", null, "Most people don't need any. Add one only if your IT team or provider asks you to."));
            rows.Add(SettingRow("Add a certificate", "Paste a public certificate (PEM). Never paste a private key.", null, RowAction("Add", delegate { TrustEdit(null); })));
            Group(null, rows.ToArray());
        }
        private void TrustEdit(string oldSerial)
        {
            Edit(oldSerial == null ? "Add trusted certificate" : "Replace certificate " + oldSerial, new[] { new Field("pem", "PEM certificate", null, "multiline") }, async v =>
            {
                object body = ApkContracts.Trust(V(v, "pem"), oldSerial);
                await Change(oldSerial == null ? "POST" : "PUT", "/2.2/account/trust", body, true, oldSerial == null ? "Certificate added." : "Certificate replaced.");
            }, "Save", "Paste a public certificate only. Never paste a private key.");
        }
        private void AccountDataPage()
        {
            Sub("Information eero keeps about your account, and who is responsible for it.");
            object verified = J.Get(account, "email", "verified");
            string emailState = verified is bool ? ((bool)verified ? "Verified" : "Not verified") : "Unknown";
            Group("Email",
                SettingRow("Email verification", verified is bool && (bool)verified ? "Your email address is confirmed." : "If eero asked you to confirm your email, send a new link here.", emailState,
                    verified is bool && (bool)verified ? null : RowActionAsync("Resend email", async delegate { if (preview) throw new InvalidOperationException("Sign in first."); await api.Send("POST", "/2.2/register/email/resend", ""); SetNotice("Verification email requested. Check your inbox."); })));
            Group("Privacy",
                SettingRow("Data controller", "The company responsible for your eero data in your region.", null, RowActionAsync("View", async delegate { Info("Data controller", await Read("/2.2/data-controller/info")); })));
            Group("Technical details",
                J.Get(account, "is_external") is bool ? InfoRow("Signed in through a provider", J.B(account, "is_external") ? "Yes" : "No", "Yes means your internet provider manages this eero sign-in.") : null,
                SettingRow("Countries with text sign-in", "Where eero can send sign-in codes by text message.", null, RowActionAsync("View", async delegate { Info("Countries with text sign-in", await Read("/2.2/countries/sms")); })),
                SettingRow("App configuration", "Settings eero sends to its apps. Mostly useful for support.", null, RowActionAsync("View", async delegate { Info("App configuration", await Read("/2.2/app_configuration" + (String.IsNullOrEmpty(networkUrl) ? "" : "?networkId=" + Uri.EscapeDataString(NetworkId)))); })),
                SettingRow("Amazon account ID", "The ID that links your eero and Amazon accounts.", null, RowActionAsync("View", async delegate { Info("Amazon account ID", await Read("/2.2/directedid?" + ApkContracts.DirectedIdQuery("0", "0", "0"))); })));
        }
        private void EntitlementsPage(object data)
        {
            Sub("What this network's plan includes, and upgrades you can try.");
            Func<object, string> name = item => item is string ? Friendly((string)item) : Friendly(J.First(item, "name", "feature", "key", "slug"));
            var included = new List<UIElement>();
            foreach (object item in Entries(J.Get(data, "features"), "features"))
            {
                bool on = item is string || !(J.Get(item, "enabled") is bool) || J.B(item, "enabled");
                included.Add(InfoRow(name(item).NullIfEmpty() ?? "Feature", on ? "✓ Included" : "Not active"));
            }
            if (included.Count == 0) included.Add(InfoRow("No features reported", null, data == null ? "Choose Refresh at the top right to try again." : "This network's plan doesn't list extra features."));
            Group("Included in your plan", included.ToArray());
            var upgrades = new List<UIElement>();
            foreach (object raw in Entries(J.Get(data, "upsell_features"), "upsell_features", "features"))
            {
                object item = raw; string tier = item is string ? "" : J.First(item, "tier", "plan", "plan_tier");
                upgrades.Add(SettingRow(name(item).NullIfEmpty() ?? "Upgrade", tier.Length > 0 ? "Part of " + Friendly(tier) : "Available with an upgraded plan.", null,
                    RowAction("Start trial", delegate
                    {
                        Edit("Start a free trial", new[] { new Field("tier", "Plan to try", tier).Help(tier.Length > 0 ? "Filled in for this upgrade." : "Enter the plan name shown under Offers & referrals, for example plus.") }, async v =>
                        {
                            object body = ApkContracts.AutoTrial(V(v, "tier"));
                            if (!Confirm("Start a free trial of this plan? Read the terms first. A trial can turn into a paid subscription.")) return;
                            await Change("POST", "/2.2/premium/customer/plans", body, true, "Trial requested.");
                        }, "Start trial");
                    })));
            }
            if (upgrades.Count == 0) upgrades.Add(InfoRow("No upgrades offered", null, "eero isn't offering upgrades for this network right now."));
            Group("Upgrades you can try", upgrades.ToArray());
        }
        private void PartnerOffersPage(object data)
        {
            Sub("Products from eero's partners. Getting an offer takes three steps: sign in to the partner, enter the code they send, then order.");
            var methods = Entries(J.Get(data, "methods"), "methods", "authentication_methods").Select(m => m is string ? (string)m : J.First(m, "authentication_method", "method", "type")).Where(m => m.Length > 0).Distinct().ToArray();
            string session = J.First(J.Get(data, "offers"), "session_id", "upsell_session_id").NullIfEmpty() ?? J.First(J.Get(data, "methods"), "session_id");
            Func<string, Field> sessionField = key => session.Length > 0 ? new Field(key, "Session", session, "hidden") : new Field(key, "Session ID").Help("Shown by the partner when you start. eero didn't return one here.");
            Group("Sign in to the partner",
                SettingRow("1. Send a sign-in code", "The partner sends a code by the method you choose.", null, RowAction("Send code", delegate
                {
                    Edit("Partner sign-in", new[] { methods.Length > 0 ? new Field("method", "Send the code by", Friendly(methods[0]), "choice", methods.Select(Friendly).ToArray()) : new Field("method", "Sign-in method").Help("For example EMAIL."), sessionField("session") }, async v =>
                    {
                        string chosen = methods.FirstOrDefault(m => Friendly(m) == V(v, "method")) ?? V(v, "method");
                        await Change("POST", "/2.2/partners/upsells/authentication_methods", ApkContracts.UpsellMethod(chosen, V(v, "session")), false, "Code requested. Enter it in step 2.");
                    }, "Send code");
                })),
                SettingRow("2. Enter the code", "Type the code the partner sent you.", null, RowAction("Enter code", delegate
                {
                    Edit("Verify partner sign-in", new[] { new Field("key", "Code"), methods.Length > 0 ? new Field("method", "Sent by", Friendly(methods[0]), "choice", methods.Select(Friendly).ToArray()) : new Field("method", "Sign-in method"), sessionField("session") }, async v =>
                    {
                        string chosen = methods.FirstOrDefault(m => Friendly(m) == V(v, "method")) ?? V(v, "method");
                        await Change("PUT", "/2.2/partners/upsells/authentication", ApkContracts.UpsellAuthenticate(V(v, "key"), chosen, V(v, "session")), false, "Partner sign-in verified. Choose an offer in step 3.");
                    }, "Verify");
                })));
            var offerRows = new List<UIElement>();
            foreach (object raw in Entries(J.Get(data, "offers"), "offers"))
            {
                object offer = raw; string offerId = J.First(offer, "internal_offer_id", "offer_id", "id"); if (offerId.Length == 0) continue;
                offerRows.Add(SettingRow(J.First(offer, "title", "name", "product_name").NullIfEmpty() ?? "Partner offer", J.First(offer, "description", "partner_name", "partner").NullIfEmpty(), null, RowAction("Get offer", delegate
                {
                    Edit("Get partner offer", new[] { new Field("offers", "Offer", offerId, "hidden"), sessionField("session"), new Field("submit", "Place the order now", false, "bool").Help("Leave off to get a price quote first. The partner may charge you when you order.") }, async v =>
                    {
                        object body = ApkContracts.UpsellOrder(V(v, "offers"), V(v, "session"), Flag(v, "submit"));
                        if (Flag(v, "submit") && !Confirm("Place this partner order? The partner may charge you.")) return;
                        if (preview) throw new InvalidOperationException("Sign in to order.");
                        Info("Partner order", await api.Send("POST", "/2.2/partners/upsells/orders", body));
                    }, "Continue");
                })));
            }
            if (offerRows.Count == 0) offerRows.Add(InfoRow("No partner offers", null, "eero isn't showing partner offers for this account right now."));
            Group("3. Choose an offer", offerRows.ToArray());
            Group("Older services", SettingRow("encrypt.me VPN", "A VPN that came with older eero Plus plans. New plans use Guardian instead.", null, RowActionAsync("Create account", async delegate { if (!Confirm("Create an encrypt.me VPN account through eero? This is a legacy eero Plus partner service.")) return; if (preview) throw new InvalidOperationException("Sign in first."); Info("encrypt.me", await api.Send("POST", "/2.2/partners/encryptme", ApkContracts.EncryptMe(), true)); })));
        }

        // ---- Help / report an issue ----------------------------------------------------------------------------
        private byte[] issueScreenshot; private string issueScreenshotName;
        private void ReportIssuePage()
        {
            Sub("Tell eero support what went wrong. Your report includes the eeros and devices you pick, and an optional screenshot.");
            var attach = RowAction(issueScreenshot == null ? "Attach" : "Replace", delegate
            {
                var picker = new Microsoft.Win32.OpenFileDialog { Title = "Attach a screenshot", Filter = "PNG image|*.png", Multiselect = false };
                if (picker.ShowDialog(this) != true) return;
                if (new FileInfo(picker.FileName).Length > 8 * 1024 * 1024) throw new ArgumentException("Attach a PNG image up to 8 MB.");
                byte[] bytes = File.ReadAllBytes(picker.FileName); ApkContracts.Screenshot(new MultipartFields(), picker.FileName, bytes);
                issueScreenshot = bytes; issueScreenshotName = Path.GetFileName(picker.FileName); RenderPage();
            });
            var write = RowAction("Write report", WriteIssueReport); write.Style = (Style)FindResource("AccentButton"); CompactAction(write);
            Group("Your report",
                SettingRow("1. Add a screenshot (optional)", "A PNG image up to 8 MB showing the problem. Press Windows + Shift + S to take one.", issueScreenshot == null ? "None" : issueScreenshotName,
                    attach, issueScreenshot != null ? RowAction("Remove", delegate { issueScreenshot = null; issueScreenshotName = null; RenderPage(); }) : null),
                SettingRow("2. Describe the problem and send", "Write a short summary, say what happened, and tick the affected eeros and devices.", null, write));
            RelatedPagesFor("Settings/Troubleshooting/Guided diagnostics", "Help/Contact support");
        }
        private void WriteIssueReport()
        {
            {
                var eeroChoices = NodeChoices(); var deviceChoices = DeviceChoices();
                var fields = new List<Field> { new Field("summary", "Summary").Help("One line, for example \"Wifi drops every evening\"."), new Field("description", "What happened?", null, "multiline").Help("When it started, what you tried, and which rooms or devices are affected."), new Field("labels", "Tags (optional)").Help("Comma separated, for example slow, upstairs.") };
                fields.AddRange(eeroChoices.Keys.Select(k => new Field("e:" + k, "Affected eero: " + k, false, "bool")));
                fields.AddRange(deviceChoices.Keys.Take(40).Select(k => new Field("d:" + k, "Affected device: " + k, false, "bool")));
                Edit("Report an issue", fields, async v =>
                {
                    var body = ApkContracts.Issue(V(v, "summary"), V(v, "description"), ApkContracts.Lines(V(v, "labels")), eeroChoices.Where(c => Flag(v, "e:" + c.Key)).Select(c => J.S(c.Value, "serial")), deviceChoices.Where(c => Flag(v, "d:" + c.Key)).Select(c => J.S(c.Value, "mac")), String.IsNullOrEmpty(networkUrl) ? null : NetworkId, DateTime.UtcNow);
                    if (issueScreenshot != null) ApkContracts.Screenshot(body, issueScreenshotName, issueScreenshot);
                    if (!Confirm("Send this report to eero support?")) return;
                    if (preview) throw new InvalidOperationException("Sign in to send a report.");
                    await api.Send("POST", "/2.2/zendesk/create", body); issueScreenshot = null; issueScreenshotName = null; RenderPage(); SetNotice("Report sent to eero support.");
                }, "Send report", "Your summary, description, selected eeros and devices, and the optional screenshot are sent to eero support.");
            }
        }

        private async Task<object> LoadApkFeature(string route)
        {
            switch (route)
            {
                case "Settings/Account settings/Trusted certificates": return await api.Send("GET", "/2.2/account/trust");
                case "Settings/Account settings/Subscriptions/Entitlements & trials": return J.Obj("features", await api.Send("GET", "/2.2/entitlements/networks/" + NetworkId + "/features"), "upsell_features", await api.Send("GET", "/2.2/entitlements/networks/" + NetworkId + "/upsell_features"));
                case "Settings/Account settings/Subscriptions/Partner offers": return J.Obj("offers", await api.Send("GET", "/2.2/partners/upsells/offers" + (String.IsNullOrEmpty(networkUrl) ? "" : "?opt_network_id=" + Uri.EscapeDataString(NetworkId))), "methods", await api.Send("GET", "/2.2/partners/upsells/authentication_methods"));
            }
            return null;
        }
        private bool RenderApkFeature(object data)
        {
            switch (currentPage)
            {
                case "Home/Add or replace eero/Setup tools": SetupToolsPage(); return true;
                case "Activity/Usage details": UsageDetailsPage(); return true;
                case "Settings/Advanced networking/Network flags": NetworkFlagsPage(); return true;
                case "Settings/Network management/Organization & community": OrganizationPage(); return true;
                case "Settings/Account settings/Trusted certificates": TrustPage(data); return true;
                case "Settings/Account settings/Account data": AccountDataPage(); return true;
                case "Settings/Account settings/Subscriptions/Entitlements & trials": EntitlementsPage(data); return true;
                case "Settings/Account settings/Subscriptions/Partner offers": PartnerOffersPage(data); return true;
                case "Help/Report an issue": ReportIssuePage(); return true;
            }
            return false;
        }
        private async Task WarmChat(string networkId)
        {
            // POST 2.2/conversations/warmup prepares the assistant before the first message; failures are ignored.
            try { if (!preview && api.SignedIn && !String.IsNullOrEmpty(networkId)) await api.Send("POST", "/2.2/conversations/warmup", ConversationContracts.Request(networkId, null, null)); }
            catch (ApiException e) { if (e.SessionExpired) throw; }
            catch (System.Net.Http.HttpRequestException) { }
        }
    }
}
