using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private void WifiPage()
        {
            Sub("The name and password your devices use to join your main wifi network.");
            Group("Your wifi network",
                SettingRow("Wifi name", "The network name devices see when they look for wifi.", J.S(network, "name"),
                    RowAction("Change", delegate { Edit("Wifi name", new[] { new Field("name", "Wifi name", J.S(network, "name")).Help("Up to 32 characters. Changing it disconnects every device until it rejoins.") }, async v => { ValidateSsid(V(v, "name")); if (!Confirm("Change the wifi name? Devices may need to reconnect.")) return; await Change("PUT", networkUrl, J.Obj("name", V(v, "name").Trim()), true, "Wifi name updated."); }); })),
                SettingRow("Wifi password", "Devices need the new password after you change it.", Hidden,
                    RowActionAsync("Show", async delegate { ShowPassword(await WifiPassword(network, NetResource("password"))); }),
                    RowAction("Change", delegate { Edit("Wifi password", new[] { new Field("password", "New password", null, "password").Help("8–63 characters. Use Generate a strong password below if you want a suggestion.") }, async v => { ValidatePassword(V(v, "password")); if (!Confirm("Change the wifi password? Devices will need the new password to reconnect.")) return; await Change("PUT", NetResource("password"), J.Obj("password", V(v, "password")), true, "Wifi password updated."); }); })));
            Group("Share & tools",
                SettingRow("Share with a QR code", "Phones can scan it to join without typing the password.", null,
                    RowActionAsync("Show QR code", async delegate { ShowWifiQr(J.S(network, "name"), await WifiPassword(network, NetResource("password"))); })),
                SettingRow("Copy wifi details", "Copies the name and password to the Windows clipboard.", null,
                    RowActionAsync("Copy", async delegate { string password = await WifiPassword(network, NetResource("password")); Clipboard.SetText("Wifi: " + J.S(network, "name") + Environment.NewLine + "Password: " + password); SetNotice("Wifi details copied to the Windows clipboard."); })),
                SettingRow("Generate a strong password", "Suggests a password. Nothing changes until you use Change above.", null,
                    RowActionAsync("Generate", GenerateWifiPassword)));
        }
        private async Task<string> WifiPassword(object data, string url)
        {
            object result = await Read(url);
            string password = result is string ? (string)result : J.S(result, "password");
            if (String.IsNullOrEmpty(password)) throw new InvalidOperationException("eero did not return the wifi password for this account.");
            return password;
        }
        private void ShowPassword(string password)
        {
            var p = new StackPanel(); p.Children.Add(Text("Wifi password", 24, "#17263C", true));
            var t = new TextBox { Text = password, IsReadOnly = true }; p.Children.Add(t); var w = Dialog("Wifi password", p); w.Height = 230; w.MinHeight = 220; w.Closed += delegate { t.Clear(); }; w.ShowDialog();
        }
        private void GuestPage(object data)
        {
            Sub("A separate wifi network for your guests.");
            if (data == null) { page.Children.Add(Text("Loading guest network…", 14, "#64738A")); return; }
            string guestUrl = ObjectUrl(data, NetResource("guestnetwork")); string passwordUrl = Resource(data, "password", guestUrl + "/password");
            string name = J.S(data, "name"); bool enabled = J.B(data, "enabled");
            Group(null, SettingToggle("Guest network", "Visitors get internet access without joining your main network or seeing your devices.", enabled, async delegate(bool on)
            {
                if (!on && !Confirm("Turn off guest wifi? Devices on the guest network will disconnect.")) return;
                await Change("PUT", guestUrl, J.Obj("name", name, "enabled", on), true, on ? "Guest wifi turned on." : "Guest wifi turned off.");
            }));
            Group("Guest network details",
                SettingRow("Guest wifi name", null, name.NullIfEmpty() ?? "Not set",
                    RowAction("Change", delegate { Edit("Guest wifi name", new[] { new Field("name", "Guest wifi name", name).Help("Pick a name that's different from your main wifi.") }, async v => { ValidateSsid(V(v, "name")); await Change("PUT", guestUrl, J.Obj("name", V(v, "name").Trim(), "enabled", enabled), true, "Guest wifi name updated."); }); })),
                SettingRow("Guest password", "Guests need the new password after you change it.", Hidden,
                    RowActionAsync("Show", async delegate { ShowPassword(await WifiPassword(data, passwordUrl)); }),
                    RowAction("Change", delegate { Edit("Guest password", new[] { new Field("password", "New guest password", null, "password").Help("8–63 characters.") }, async v => { ValidatePassword(V(v, "password")); await Change("PUT", passwordUrl, J.Obj("password", V(v, "password")), true, "Guest password updated."); }); })),
                SettingRow("Share guest wifi", "Show a QR code guests can scan to join.", null,
                    RowActionAsync("Show QR code", async delegate { ShowWifiQr(name, await WifiPassword(data, passwordUrl)); })));
        }
        private void NicknamePage()
        {
            Sub("How this network is labelled, and the timezone its schedules follow.");
            string nickname = J.First(network, "nickname_label", "nickname"); string zone = J.S(network, "timezone", "value").NullIfEmpty() ?? J.S(network, "timezone");
            Group(null,
                SettingRow("Nickname", "A label for this network in eero apps. It doesn't change your wifi name.", nickname.NullIfEmpty() ?? "Not set",
                    RowAction("Change", delegate { Edit("Network nickname", new[] { new Field("label", "Nickname", nickname) }, async v => { ValidateName(V(v, "label")); await Change("PUT", FeatureUrl("label?labelType=specialMarket"), J.Obj("label", V(v, "label").Trim()), true, "Network nickname updated."); }); })),
                SettingRow("Timezone", "Profile pauses, power-saving schedules and update times follow this timezone.", zone.NullIfEmpty() ?? "Not set",
                    RowAction("Change", delegate { Edit("Network timezone", new[] { new Field("timezone", "Timezone", zone).Help("Use the region/city form, for example America/New_York, America/Chicago, America/Denver, America/Los_Angeles or Europe/London.") }, async v => { string value = V(v, "timezone").Trim(); if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"^[A-Za-z_+-]+(?:/[A-Za-z0-9_+-]+){1,2}$|^UTC$")) throw new ArgumentException("Enter a timezone such as America/Chicago."); if (!Confirm("Change the network timezone? Scheduled pauses will follow the new timezone.")) return; await Change("PUT", NetResource("settings"), J.Obj("timezone", J.Obj("value", value)), false, "Timezone updated."); }); })));
        }
        private void WanPage()
        {
            Sub("How your gateway eero gets its internet connection from your internet provider (ISP). Most homes use Automatic.");
            object lease = J.Get(network, "lease"); string leaseMode = J.S(lease, "mode");
            bool isStatic = leaseMode == "static";
            string assignment = isStatic ? "Static IP" : leaseMode == "dhcp" ? "Automatic (DHCP)" : leaseMode.Length > 0 ? Friendly(leaseMode) : "Not reported by eero";
            string routerMode = J.S(network, "connection", "mode");
            Group("Internet address",
                SettingRow("Address assignment", "Automatic works for most ISPs. Choose Static IP only if your ISP gave you a fixed address.", assignment,
                    RowAction("Change", EditWanAddress)),
                isStatic ? InfoRow("Static IP address", J.S(lease, "static", "ip")) : null,
                isStatic ? InfoRow("Subnet mask", J.S(lease, "static", "mask")) : null,
                isStatic ? InfoRow("Gateway", J.S(lease, "static", "router")) : null,
                routerMode.Length > 0 ? InfoRow("Router mode", Friendly(routerMode), "Change this on the DHCP & NAT page.") : null);
            AddGroupHeading(page, "Related settings");
            var related = FeatureCatalog.Entries.Where(e => e.Path == "Settings/Advanced networking/Internet connection/ISP settings" || e.Path == "Settings/Advanced networking/DHCP & NAT").Select(e => (UIElement)ListRow(e, false)).ToList();
            page.Children.Add(RowGroup(related));
            TechnicalDetails(J.Obj("connection", J.Get(network, "connection"), "ip_settings", J.Get(network, "ip_settings"), "lease", lease, "ipv6_lease", J.Get(network, "ipv6_lease")));
        }
        private void DnsPage()
        {
            Sub("DNS turns website names into addresses. Most networks should use the automatic servers from their internet provider.");
            object dns = J.Get(network, "dns");
            bool custom = J.S(dns, "mode") == "custom"; bool caching = J.B(dns, "caching");
            var ips = J.List(J.Get(dns, "custom", "ips")).Select(x => J.S(x)).Where(s => s.Length > 0).ToList();
            string servers = String.Join(Environment.NewLine, ips);
            const string automatic = "Automatic (from your ISP)";
            Group(null,
                SettingRow("DNS servers", custom ? "Your network uses the servers you entered." : "Your internet provider chooses the servers.", custom ? "Custom · " + String.Join(", ", ips) : automatic,
                    RowAction("Change", delegate
                    {
                        Edit("DNS servers", new[] {
                            new Field("mode", "DNS servers", custom ? "Custom" : automatic, "choice", automatic, "Custom").Help("Custom lets you use a provider such as 1.1.1.1 or 8.8.8.8. Advanced Security can prevent custom DNS."),
                            new Field("servers", "Custom DNS server addresses (one per line, up to four)", servers, "multiline").When("mode", "Custom") }, async v =>
                        {
                            object body = FeatureContracts.Dns(V(v, "mode") == "Custom", V(v, "servers"), caching);
                            if (!Confirm("Apply DNS settings? eero may restart the network.")) return;
                            await Change("PUT", NetResource("settings"), body, false, "DNS servers updated.");
                        });
                    })),
                SettingToggle("Local DNS caching", "Your eero remembers recent lookups so websites start loading faster.", caching, async delegate(bool on)
                {
                    if (!Confirm((on ? "Turn on" : "Turn off") + " local DNS caching? eero may restart the network.")) return;
                    await Change("PUT", NetResource("settings"), FeatureContracts.Dns(custom, servers, on), false, "Local DNS caching " + (on ? "turned on." : "turned off."));
                }));
            RelatedPages();
        }
        private string DhcpModeLabel()
        {
            return J.S(network, "connection", "mode") == "bridged" ? "Bridge" : ApkContracts.IsPassthrough(network) ? "IP Passthrough" : J.S(network, "dhcp", "mode") == "custom" ? "Manual" : "Automatic";
        }
        private static string DhcpModeHelp(string mode)
        {
            switch (mode)
            {
                case "Manual": return "eero hands out addresses from a range you choose.";
                case "Bridge": return "Another router handles addresses; eero only provides wifi. Profiles and some security features stop working.";
                case "IP Passthrough": return "Your public IP passes to one downstream device. Routing, profiles and some eero Plus features stop working.";
                default: return "eero hands out addresses to your devices automatically. Recommended.";
            }
        }
        private void DhcpPage()
        {
            Sub("How eero gives addresses to the devices on your network.");
            string mode = DhcpModeLabel(); object range = J.Get(network, "dhcp", "custom");
            Group(null,
                SettingRow("Network mode", DhcpModeHelp(mode), mode, RowAction("Change", EditDhcp)),
                mode == "Manual" ? InfoRow("Address range", J.S(range, "start_ip") + " – " + J.S(range, "end_ip")) : null,
                mode == "Manual" ? InfoRow("Subnet", J.S(range, "subnet_ip") + " / " + J.S(range, "subnet_mask")) : null,
                J.Get(network, "nat_port_randomization") is bool && (mode == "Automatic" || mode == "Manual") ? SettingToggle("NAT port randomization", "Uses random outbound ports for extra privacy. Turn off if a game, call or VPN has trouble connecting.", J.B(network, "nat_port_randomization"), async delegate(bool on)
                {
                    if (!Confirm((on ? "Turn on" : "Turn off") + " NAT port randomization? The network may restart.")) return;
                    await Change("PUT", NetResource("settings"), J.Obj("nat_port_randomization", on), false, "NAT port randomization " + (on ? "turned on." : "turned off."));
                }) : null);
            TechnicalDetails(J.Obj("connection", J.Get(network, "connection"), "dhcp", J.Get(network, "dhcp")));
        }
        private void EditDhcp()
        {
            {
                string mode = DhcpModeLabel();
                var modes = new List<string> { "Automatic", "Manual", "Bridge" }; if (ApkContracts.PassthroughCapable(network) || ApkContracts.IsPassthrough(network)) modes.Add("IP Passthrough");
                Edit("Network mode", new[] {
                    new Field("mode", "Mode", mode, "choice", modes.ToArray()).Help("Automatic: recommended. Manual: choose the address range. Bridge: another router hands out addresses. IP Passthrough: give your public IP to one device."),
                    new Field("subnet", "Subnet network address", J.S(network, "dhcp", "custom", "subnet_ip")).When("mode", "Manual"),
                    new Field("mask", "Subnet mask", J.S(network, "dhcp", "custom", "subnet_mask")).When("mode", "Manual"),
                    new Field("first", "First address to hand out", J.S(network, "dhcp", "custom", "start_ip")).When("mode", "Manual"),
                    new Field("last", "Last address to hand out", J.S(network, "dhcp", "custom", "end_ip")).When("mode", "Manual"),
                    new Field("randomization", "NAT port randomization", J.B(network, "nat_port_randomization"), "bool").When("mode", "Automatic", "Manual") }, async v =>
                {
                    string selected = V(v, "mode") == "Bridge" ? "bridge" : V(v, "mode") == "IP Passthrough" ? "passthrough" : V(v, "mode") == "Manual" ? "custom" : "automatic";
                    object body = FeatureContracts.Dhcp(selected, V(v, "subnet"), V(v, "mask"), V(v, "first"), V(v, "last"), Flag(v, "randomization"));
                    if (!Confirm(selected == "passthrough" ? "Switch to IP Passthrough? The network will restart, your public IP passes to one downstream device, and routing, profiles and some eero Plus features stop working." : "Apply DHCP & NAT settings? The network may restart. Bridge mode disables routing, profiles and some security features.")) return;
                    await Change("PUT", NetResource("settings"), body, false, "DHCP & NAT settings updated.");
                });
            }
        }
        private void NetworkBooleanPage(string key, string title, string description)
        {
            Sub("Applies to every eero on this network. Changes take effect after eero confirms them.");
            NetworkSettingToggles(new[] { new[] { key, title, description } }, "Some devices may reconnect.");
        }
        private void WirelessPage()
        {
            Sub("Security and performance options for every eero radio on this network.");
            NetworkSettingToggles(new[] {
                new[] { "wpa3", "WPA3 security", "Stronger wifi encryption. Some older devices only support WPA2." },
                new[] { "band_steering", "Client steering", "Move capable devices to the best band and eero automatically." },
                new[] { "sqm", "Optimize for conferencing and gaming", "Smart queue management reduces lag while the connection is busy." },
                new[] { "ffs", "Amazon Frustration-Free Setup", "Let compatible Amazon devices join this network automatically." }
            }, "Devices may briefly reconnect.");
            string[] mloValues = { "disabled", "single", "multi" }, mloLabels = { "Off", "Single link", "Multi-link" };
            string mlo = J.S(network, "mlo_mode"); int mloIndex = Array.IndexOf(mloValues, mlo);
            Group("Compatibility",
                SettingRow("Legacy compatibility mode", "Helps older wifi devices that can't connect. Can lower speeds for newer devices.", null,
                    RowActionAsync("Change", async delegate
                    {
                        object value = await Read(FeatureUrl("ac_compat"));
                        Edit("Legacy compatibility mode", new[] { new Field("enabled", "Use legacy compatibility mode", J.B(value, "enabled"), "bool").Help("Turn on only if an older device can't join your wifi.") }, async v => { await Change("PUT", FeatureUrl("ac_compat"), J.Obj("enabled", Flag(v, "enabled")), true, "Compatibility setting updated."); });
                    })),
                SettingRow("Security by radio band", "Choose WPA2, WPA3 or both for the 2.4 GHz and 5 GHz bands. 6 GHz always uses WPA3.", null, RowActionAsync("Change", BandSecurity)),
                J.Get(network, "mlo_mode") != null ? SettingRow("Multi-link operation (Wi-Fi 7)", "Lets Wi-Fi 7 devices use more than one band at the same time.", mloIndex >= 0 ? mloLabels[mloIndex] : Friendly(mlo),
                    RowAction("Change", delegate { Edit("Multi-link operation", new[] { new Field("mlo_mode", "Mode", mloIndex >= 0 ? mloLabels[mloIndex] : mloLabels[0], "choice", mloLabels) }, async v => { if (!Confirm("Change multi-link operation? Connected devices may reconnect.")) return; await Change("PUT", FeatureUrl("mlo_mode"), J.Obj("mlo_mode", mloValues[Array.IndexOf(mloLabels, V(v, "mlo_mode"))]), false, "Multi-link operation updated."); }); })) : null,
                PasspointRow());
            Group("Reports",
                SettingRow("Radio settings", "The channel and power each eero radio is using.", null, RowActionAsync("View", async delegate { Info("Radio settings", await Read(FeatureUrl("radio_settings"))); })),
                SettingRow("Channel utilization", "How busy each wifi channel was over the last day.", null, RowActionAsync("View", async delegate { Info("Channel utilization", await Read(FeatureUrl("channel_utilization?") + StatsRange(1))); })));
            RelatedPages();
        }
        private void ThreadPage(object data)
        {
            Sub("Thread is a low-power network used by Matter and other smart-home devices such as sensors, locks and lights.");
            bool threadOn = J.B(network, "thread"), syncing = J.B(data, "enable_credential_syncing");
            Group(null,
                SettingToggle("Thread network", "Let Thread smart-home devices connect through your eeros.", threadOn, async delegate(bool on)
                {
                    if (!on && !Confirm("Turn off Thread? Thread smart-home devices will stop working until you turn it back on.")) return;
                    await Change("PUT", FeatureUrl("thread"), J.Obj("thread_enable", on, "enable_credential_syncing", syncing), false, on ? "Thread turned on." : "Thread turned off.");
                }),
                SettingToggle("Share Thread credentials", "Sync credentials with your phone so smart-home apps can add Thread devices.", syncing, async delegate(bool on)
                { await Change("PUT", FeatureUrl("thread"), J.Obj("thread_enable", threadOn, "enable_credential_syncing", on), false, on ? "Thread credential sharing turned on." : "Thread credential sharing turned off."); }));
            Group("Credentials",
                SettingRow("Thread credentials", "Some devices ask for these when you add them by hand.", null,
                    RowAction("View", delegate { ShowCredentials("Thread credentials", preview ? new Dictionary<string, string> { { "Thread dataset", "Sample-only-dataset" } } : EverydayContracts.ThreadCredentials(data)); })),
                SettingRow("Reset credentials", "Creates new credentials. Every Thread device must be paired again.", null,
                    RowActionAsync("Reset", async delegate
                    {
                        if (!Confirm("Reset this Thread network's credentials? Every Thread device will need to be paired again.")) return;
                        await Change("POST", FeatureUrl("thread"), null, false, "Thread credential reset requested.");
                    }, true)));
            TechnicalDetails(data);
        }
        private void DdnsPage()
        {
            Sub("Gives your network a hostname that keeps pointing at your home even when your ISP changes your public IP. Useful for reaching a camera, server or game host from outside. Requires an eligible eero subscription.");
            object ddns = J.Get(network, "dynamic_dns"); string host = J.First(ddns, "hostname", "host", "domain");
            Func<bool, Task> apply = async delegate(bool on)
            {
                if (!on && !Confirm("Turn off Dynamic DNS? Anything using your hostname will stop reaching your network.")) return;
                await Change("PUT", FeatureUrl(on ? "ddns/enable" : "ddns/disable"), null, false, on ? "Dynamic DNS turned on." : "Dynamic DNS turned off.");
            };
            Group(null,
                J.Get(ddns, "enabled") is bool
                    ? (UIElement)SettingToggle("Dynamic DNS", "Keep a hostname updated with your network's public IP.", J.B(ddns, "enabled"), apply)
                    : SettingRow("Dynamic DNS", "eero hasn't reported whether this is on.", "Unknown", RowActionAsync("Turn on", async delegate { await apply(true); }), RowActionAsync("Turn off", async delegate { await apply(false); })),
                host.Length > 0 ? SettingRow("Hostname", "Use this name instead of your public IP.", host, RowAction("Copy", delegate { Clipboard.SetText(host); SetNotice("Hostname copied."); })) : null);
        }
        private static List<object> Entries(object data, params string[] names)
        {
            var direct = J.List(data); if (direct.Count > 0) return direct;
            foreach (string name in names) { var a = J.List(J.Get(data, name)); if (a.Count > 0) return a; }
            return new List<object>();
        }
        private void RoutingPage(object data)
        {
            Sub("Give devices a fixed address, and let apps on the internet reach a device when it needs that (for example game servers or cameras).");
            if (ApkContracts.IsNotRouting(network)) { page.Children.Add(Card(Text("These settings only work when DHCP & NAT is set to Automatic or Manual. Bridge and IP Passthrough hand this job to another router."))); return; }
            if (data == null) { page.Children.Add(Text("Loading network rules…", 14, "#64738A")); return; }
            string[] kinds = { "reservations", "forwards", "pinholes" };
            string[] headings = { "Reserved addresses", "Port forwarding", "IPv6 firewall rules" };
            string[] helps = { "Always give a device the same IP address. Needed before you forward a port to it.", "Send traffic from the internet on a port to one device. Only open the ports you need.", "Allow incoming IPv6 traffic to reach a device." };
            string[] adds = { "Reserve an address", "Open a port", "Add a rule" };
            for (int k = 0; k < kinds.Length; k++)
            {
                string kind = kinds[k]; var list = J.List(J.Get(data, kind)); var rows = new List<UIElement>();
                foreach (object raw in list)
                {
                    object item = raw; string title, detail;
                    if (kind == "reservations") { title = J.First(item, "description", "mac"); detail = J.S(item, "ip") + " · " + J.S(item, "mac"); }
                    else if (kind == "forwards") { title = J.First(item, "description", "ip"); detail = "Port " + J.S(item, "gateway_port") + " → " + J.S(item, "ip") + ":" + J.S(item, "client_port") + " · " + J.S(item, "protocol").ToUpperInvariant() + (J.Get(item, "enabled") is bool && !J.B(item, "enabled") ? " · Off" : ""); }
                    else { var match = devices.FirstOrDefault(d => J.S(d, "url") == J.S(item, "device")); title = match != null ? Device(match).Name : "Device"; detail = J.S(item, "protocol").ToUpperInvariant() + " port " + J.S(item, "port"); }
                    rows.Add(SettingRow(title.NullIfEmpty() ?? "Rule", detail, null,
                        RowAction("Edit", delegate { RoutingEdit(kind, item, data); }),
                        RowActionAsync("Delete", async delegate
                        {
                            if (!Confirm(kind == "reservations" ? "Delete this reservation and its port forwards?" : "Delete this rule?")) return;
                            string url = ObjectUrl(item); if (String.IsNullOrEmpty(url)) throw new InvalidOperationException("eero did not provide an edit URL for this entry.");
                            await Change("DELETE", url + (kind == "reservations" ? "?delete_forwards=true" : ""), null, false, "Rule deleted.");
                        }, true)));
                }
                if (rows.Count == 0) rows.Add(InfoRow("None yet", null, helps[k]));
                rows.Add(SettingRow(adds[k], list.Count == 0 ? null : helps[k], null, RowAction("Add", delegate { RoutingEdit(kind, null, data); })));
                Group(headings[k], rows.ToArray());
            }
        }
        private void RoutingEdit(string kind, object item, object rules)
        {
            bool edit = item != null;
            string container = J.S(rules, kind, "url").NullIfEmpty() ?? (kind == "pinholes" ? FeatureUrl("ipv6/pinholes") : NetResource(kind));
            string url = edit ? ObjectUrl(item) : container;
            if (kind == "reservations")
            {
                Edit(edit ? "Edit reserved address" : "Reserve an address", new[] { new Field("name", "Nickname", J.S(item, "description")), new Field("mac", "Device MAC address", J.S(item, "mac")).Help("Find it on the Devices page, for example 02:00:00:00:00:01."), new Field("ip", "IP address to reserve", J.S(item, "ip")).Help("Pick an address inside your network, for example 192.168.4.50.") }, async v => { await Change(edit ? "PUT" : "POST", url, FeatureContracts.Reservation(V(v, "name"), V(v, "mac"), V(v, "ip")), false, "Reservation saved."); });
            }
            else if (kind == "forwards")
            {
                Edit(edit ? "Edit port forward" : "Open a port", new[] { new Field("description", "Nickname", J.S(item, "description")), new Field("ip", "Device IP address", J.S(item, "ip")).Help("Reserve this address first so the device keeps it."), new Field("gateway_port", "Internet port or range", J.S(item, "gateway_port")).Help("For example 443 or 8000-8010."), new Field("client_port", "Device port or range", J.S(item, "client_port")).Help("Usually the same as the internet port."), new Field("protocol", "Protocol", J.S(item, "protocol").NullIfEmpty() ?? "tcp", "choice", "tcp", "udp", "both").Help("Check the app's instructions. Choose both if unsure."), new Field("enabled", "Forward this port now", !edit || J.B(item, "enabled"), "bool") }, async v => { object body = FeatureContracts.Forward(V(v, "ip"), V(v, "gateway_port"), V(v, "client_port"), V(v, "protocol"), V(v, "description"), Flag(v, "enabled")); if (!Confirm("Save this port forward? It allows matching inbound internet traffic to the selected device.")) return; await Change(edit ? "PUT" : "POST", url, body, false, "Port forward saved."); });
            }
            else
            {
                var choices = devices.Where(d => J.S(d, "url").Length > 0).ToDictionary(d => Device(d).Name + " · " + J.S(d, "mac"), d => J.S(d, "url"));
                if (choices.Count == 0) throw new InvalidOperationException("No device URLs were returned for an IPv6 rule.");
                string chosen = choices.FirstOrDefault(p => p.Value == J.S(item, "device")).Key ?? choices.Keys.First();
                Edit(edit ? "Edit IPv6 firewall rule" : "IPv6 firewall rule", new[] { new Field("device", "Device", chosen, "choice", choices.Keys.ToArray()), new Field("protocol", "Protocol", J.S(item, "protocol").NullIfEmpty() ?? "tcp", "choice", "tcp", "udp", "both"), new Field("port", "Port or range", J.S(item, "port")) }, async v => { var body = J.Obj("device", choices[V(v, "device")], "protocol", V(v, "protocol"), "port", FeatureContracts.PortRange(V(v, "port"))); if (!Confirm("Allow this inbound IPv6 traffic to the selected device?")) return; await Change(edit ? "PUT" : "POST", url, body, false, "IPv6 firewall rule saved."); });
            }
        }
        private void PowerPage(object data)
        {
            Sub("Lower your eeros' power use. Wifi stays on, but peak speed can drop slightly while power saving is active.");
            bool saving = J.B(network, "power_saving"), scheduled = J.B(network, "power_saving_schedule_enabled");
            Group(null,
                SettingToggle("Power saving", "Save power whenever traffic is light.", saving, async delegate(bool on)
                { await Change("PUT", FeatureUrl("power_saving"), J.Obj("enable", on, "power_saving_schedule_enabled", scheduled), false, on ? "Power saving turned on." : "Power saving turned off."); }),
                SettingToggle("Use schedules", "Only save power during the times below, such as overnight.", scheduled, async delegate(bool on)
                { await Change("PUT", FeatureUrl("power_saving"), J.Obj("enable", saving, "power_saving_schedule_enabled", on), false, on ? "Power-saving schedules turned on." : "Power-saving schedules turned off."); }));
            var rows = new List<UIElement>();
            foreach (object raw in Entries(data, "schedules"))
            {
                object item = raw;
                string url = ObjectUrl(item, FeatureUrl("power_saving/schedules/" + J.First(item, "id", "schedule_id")));
                string days = String.Join(", ", J.List(J.Get(item, "days")).Select(x => Human(J.S(x).ToLowerInvariant())));
                string when = J.S(item, "start_time") + "–" + J.S(item, "end_time") + (days.Length > 0 ? " · " + days : "");
                rows.Add(SettingRow(J.S(item, "name").NullIfEmpty() ?? "Schedule", J.B(item, "enabled") ? when : when + " · Off", null,
                    RowAction("Edit", delegate { ScheduleEdit(item, url, true); }),
                    RowActionAsync("Delete", async delegate { if (!Confirm("Delete this power saving schedule?")) return; await Change("DELETE", url, null, false, "Schedule deleted."); }, true)));
            }
            if (rows.Count == 0) rows.Add(InfoRow("No schedules yet", null, "Add one to save power at set times, for example 11:00 PM to 6:00 AM."));
            rows.Add(SettingRow("Add a schedule", null, null, RowAction("Add", delegate { ScheduleEdit(null, FeatureUrl("power_saving/schedules"), true); })));
            Group("Schedules", rows.ToArray());
        }
        private void ScheduleEdit(object item, string url, bool power = false)
        {
            var days = new HashSet<string>(J.List(J.Get(item, "days")).Select(x => J.S(x)));
            var fields = new List<Field> { new Field("name", "Schedule name", J.S(item, "name")), new Field("start", "Start time (24-hour HH:mm)", J.S(item, power ? "start_time" : "start").NullIfEmpty() ?? "22:00"), new Field("end", "End time (24-hour HH:mm)", J.S(item, power ? "end_time" : "end").NullIfEmpty() ?? "07:00"), new Field("enabled", "Enabled", item == null || J.B(item, "enabled"), "bool") };
            foreach (string day in new[] { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" }) fields.Add(new Field(day, Human(day.ToLowerInvariant()), days.Contains(day), "bool"));
            Edit(item == null ? "Add schedule" : "Edit schedule", fields, async v => { var selected = new[] { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" }.Where(d => Flag(v, d)); object body = FeatureContracts.Schedule(V(v, "name"), selected, V(v, "start"), V(v, "end"), Flag(v, "enabled"), power); await Change(item == null ? "POST" : "PUT", url, body, false, "Schedule saved."); }, "Save", "Times follow the network timezone. End times earlier than start times continue into the next day.");
        }
        private void BlockedPage(object data)
        {
            Sub("Devices you've blocked can't join your network at all. Unblock one to let it connect again.");
            var rows = new List<UIElement>();
            foreach (object raw in Entries(data, "devices"))
            {
                object item = raw;
                rows.Add(SettingRow(Device(item).Name, J.S(item, "mac") + (J.S(item, "manufacturer").Length > 0 ? " · " + J.S(item, "manufacturer") : ""), null,
                    RowActionAsync("Unblock", async delegate { if (!Confirm("Allow " + Device(item).Name + " to join your network again?")) return; await Change("DELETE", FeatureUrl("blacklist/" + Uri.EscapeDataString(J.S(item, "mac"))), null, false, "Device unblocked."); })));
            }
            if (rows.Count == 0) rows.Add(InfoRow("No blocked devices", null, data == null && !preview ? "eero hasn't returned this list. Choose Refresh at the top right to try again." : "To block one, select it on the Devices page and choose Block."));
            Group(null, rows.ToArray());
        }
        private void EditWanAddress()
        {
            object lease = J.Get(network, "lease"); object stat = J.Get(lease, "static");
            const string automatic = "Automatic (DHCP)";
            Edit("Internet address", new[] {
                new Field("mode", "Address assignment", J.S(lease, "mode") == "static" ? "Static IP" : automatic, "choice", automatic, "Static IP").Help("Automatic asks your ISP for an address. Static IP uses the fixed address your ISP gave you."),
                new Field("ip", "Static IPv4 address", J.S(stat, "ip")).When("mode", "Static IP"),
                new Field("mask", "Subnet mask", J.S(stat, "mask")).When("mode", "Static IP"),
                new Field("router", "Gateway (router) address", J.S(stat, "router")).When("mode", "Static IP") }, async v =>
            {
                bool isStatic = V(v, "mode") == "Static IP"; var body = J.Obj("mode", isStatic ? "static" : "dhcp");
                if (isStatic) body["static"] = J.Obj("ip", FeatureContracts.Address(V(v, "ip"), AddressFamily.InterNetwork), "mask", FeatureContracts.Address(V(v, "mask"), AddressFamily.InterNetwork), "router", FeatureContracts.Address(V(v, "router"), AddressFamily.InterNetwork));
                if (!Confirm("Apply the internet addressing settings supplied by your ISP? Incorrect values can disconnect the network.")) return;
                await Change("PUT", NetResource("settings"), J.Obj("lease", body), false, "Internet addressing updated.");
            });
        }
    }
}
