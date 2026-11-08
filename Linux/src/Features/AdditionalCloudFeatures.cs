using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Gtk;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private void AdditionalWifiPage(object data)
        {
            Sub("Extra wifi networks with their own name and password, for example to keep smart-home devices or kids' devices separate. How many you can add depends on your eero hardware and plan.");
            var subnets = Entries(data, "subnets", "configurations"); var rows = new List<Widget>();
            foreach (object raw in subnets)
            {
                object subnet = raw; string type = J.S(subnet, "subnet_type");
                int rate; bool limited = Int32.TryParse(J.S(subnet, "rate_limit_pct"), out rate) && rate < 100;
                string summary = (Friendly(J.S(subnet, "subnet_kind")).NullIfEmpty() ?? "Wifi network") + " · " + (J.Get(subnet, "enabled") is bool && !J.B(subnet, "enabled") ? "Off" : "On") + (J.B(subnet, "open_network") ? " · No password" : "") + (J.Get(subnet, "wan_access") is bool && !J.B(subnet, "wan_access") ? " · No internet" : "") + (limited ? " · " + rate + "% bandwidth" : "");
                var actions = new List<Button> { RowAction("Edit", delegate { SubnetEdit(subnet); }), RowActionAsync("Content filters", async delegate { await EditSubnetFilters(subnet); }) };
                if (type == "A" || type == "B") actions.Add(RowActionAsync("Delete", async delegate { if (!await Confirm("Delete this additional wifi network? Connected devices will disconnect.")) return; await Change("DELETE", FeatureUrl("subnets_config/" + type), null, false, "Additional wifi network deleted."); }, true));
                rows.Add(SettingRow(J.S(subnet, "name").NullIfEmpty() ?? "Additional network", summary, null, actions.ToArray()));
            }
            if (rows.Count == 0) rows.Add(InfoRow("No additional networks yet", null, "Add one to keep smart-home, kids' or work devices on their own wifi."));
            var used = new HashSet<string>(subnets.Select(s => J.S(s, "subnet_type")));
            string free = new[] { "A", "B" }.FirstOrDefault(s => !used.Contains(s));
            var add = RowAction("Add", delegate { SubnetEdit(null, free); }); add.SetSensitive(free != null);
            rows.Add(SettingRow("Add a wifi network", free != null ? "Choose a name, password and what it's for." : "Both additional network slots are in use. Delete one to add another.", null, add));
            Group(null, rows.ToArray());
        }
        private void SubnetEdit(object subnet, string slot = "A")
        {
            var fields = new List<Field> { new Field("name", "Wifi name", J.S(subnet, "name")), new Field("password", subnet == null ? "Password" : "New password", null, "password").Help(subnet == null ? "8–63 characters." : "Leave blank to keep the current password.").When("open_network", "False"), new Field("enabled", "Turn this network on", subnet == null || J.B(subnet, "enabled"), "bool"), new Field("open_network", "No password (open network)", J.B(subnet, "open_network"), "bool").Help("Anyone nearby could join. Use only for a captive portal or guest kiosk."), new Field("wan_access", "Allow internet access", subnet == null || J.B(subnet, "wan_access"), "bool").Help("Turn off for local-only devices such as some cameras."), new Field("rate", "Bandwidth limit (percent of your connection)", J.S(subnet, "rate_limit_pct").NullIfEmpty() ?? "100").Help("100 means no limit.") };
            if (subnet == null) { fields.Add(new Field("type", "Slot", slot ?? "A", "hidden")); fields.Add(new Field("kind", "What is it for?", "IoT", "choice", "IoT", "Kids", "Business", "Guest").Help("IoT is for smart-home devices like plugs, cameras and speakers.")); }
            Edit(subnet == null ? "Add additional wifi" : "Edit additional wifi", fields, async v =>
            {
                ValidateSsid(V(v, "name")); int rate; if (!Int32.TryParse(V(v, "rate"), out rate) || rate < 1 || rate > 100) throw new ArgumentException("Enter a bandwidth percentage between 1 and 100.");
                string password = V(v, "password").NullIfEmpty() ?? J.S(subnet, "password");
                if (!Flag(v, "open_network")) ValidatePassword(password);
                var body = J.Obj("name", V(v, "name").Trim(), "enabled", Flag(v, "enabled"), "open_network", Flag(v, "open_network"), "wan_access", Flag(v, "wan_access"), "rate_limit_pct", rate);
                if (subnet != null) foreach (string key in new[] { "network_id", "subnet_id", "subnet_type", "subnet_kind", "dedicated_subnet" }) { object existing = J.Get(subnet, key); if (existing != null) body[key] = existing; }
                else { body["network_id"] = networkUrl.TrimEnd('/').Split('/').Last(); body["subnet_type"] = V(v, "type"); body["subnet_kind"] = V(v, "kind"); }
                if (!Flag(v, "open_network")) body["password"] = password;
                if (!await Confirm("Save this wifi network? Devices using it may disconnect.")) return;
                await Change("PUT", FeatureUrl("subnets_config"), body, false, "Wifi subnet saved.");
            });
        }
        private void SubnetFiltersPage(object data)
        {
            Sub("Separate content filters for each extra wifi network, for example stricter filters on a kids' network. Your main network uses Security & privacy.");
            var rows = new List<Widget>();
            foreach (object raw in Entries(data, "subnets", "configurations"))
            {
                object subnet = raw;
                rows.Add(SettingRow(J.First(subnet, "name", "subnet_type").NullIfEmpty() ?? "Wifi network", Friendly(J.S(subnet, "subnet_kind")).NullIfEmpty(), null, RowActionAsync("Edit filters", async delegate { await EditSubnetFilters(subnet); })));
            }
            if (rows.Count == 0) rows.Add(InfoRow("No extra wifi networks", null, data == null ? "eero hasn't returned your wifi networks. Choose Refresh at the top right to try again." : "Add an additional wifi network first, then set its filters here."));
            Group(null, rows.ToArray());
            RelatedPagesFor("Settings/Additional wifi networks", "Home/Security & privacy");
        }
        private async Task EditSubnetFilters(object subnet)
        {
            string id = J.S(subnet, "subnet_id").NullIfEmpty() ?? J.S(subnet, "subnet_type"); if (id.Length == 0) throw new InvalidOperationException("eero did not provide a subnet identifier.");
            object data = await Read(FeatureUrl("subnets_config/" + Uri.EscapeDataString(id) + "/dns_policies/content_filters"));
            var map = new Dictionary<string, object>();
            foreach (object candidate in new[] { J.Get(data, "dns_policies"), J.Get(data, "unified_content_filters", "dns_policies"), J.Get(data, "premium_dns", "dns_policies"), J.Get(data, "premium_dns", "unified_content_filters", "dns_policies") })
            { var values = candidate as IDictionary<string, object>; if (values != null) foreach (var p in values) if (p.Value is bool) map[p.Key] = p.Value; }
            if (map.Count == 0) throw new InvalidOperationException("No editable content filters were returned for this subnet.");
            Edit("Wifi content filters", map.Select(p => new Field(p.Key, Human(p.Key), p.Value, "bool")), async v => { var filters = map.ToDictionary(p => p.Key, p => (object)Flag(v, p.Key)); await Change("PUT", FeatureUrl("subnets_config/dns_policies/content_filters"), J.Obj("subnets", new[] { id }, "content_filters", filters), false, "Subnet content filters updated."); });
        }
        private async Task BandSecurity()
        {
            string path = FeatureUrl("wpa3_per_band"); object data = await Read(path);
            Edit("Security by radio band", new[] { new Field("band_2_4_ghz", "2.4 GHz security", J.S(data, "band_2_4_ghz"), "choice", "WPA2", "WPA2_WPA3", "WPA3"), new Field("band_5_ghz", "5 GHz security", J.S(data, "band_5_ghz"), "choice", "WPA2", "WPA2_WPA3", "WPA3") }, async v => { if (!await Confirm("Change wifi authentication modes? Some devices may need to reconnect or may not support the new mode.")) return; await Change("PUT", path, J.Obj("band_2_4_ghz", V(v, "band_2_4_ghz"), "band_5_ghz", V(v, "band_5_ghz")), false, "Per-band wifi security updated."); }, "Save", "6 GHz uses WPA3 as required by that band; this endpoint edits the 2.4 and 5 GHz modes.");
        }
        private void MultiStaticPage(object data)
        {
            Sub("Use a block of extra public IP addresses from your internet provider. This only works if your ISP gives you more than one static IP.");
            object lan = J.Get(data, "multistaticip_settings"), nat = J.Get(data, "multistaticip_settings_nat_portfwd");
            bool enabled = J.B(data, "enabled"); string type = J.S(data, "type").NullIfEmpty() ?? "PublicLAN";
            const string lanLabel = "Public addresses on devices", natLabel = "Port forwarding (NAT)";
            Group(null,
                SettingRow("Multiple static IPs", "Public addresses on devices gives each device its own public IP. Port forwarding keeps devices private and forwards the extra IPs.", data == null ? "Not reported" : enabled ? "On · " + Friendly(type) : "Off", RowAction("Change", delegate
            {
                Edit("Multiple static IPs", new[] {
                    new Field("enabled", "Use multiple static IPs", J.B(data, "enabled"), "bool"),
                    new Field("type", "How to use them", type == "NATWithPortFwd" ? natLabel : lanLabel, "choice", lanLabel, natLabel).When("enabled", "True"),
                    new Field("subnet", "Public subnet address", J.S(lan, "subnet_ip")).When("type", lanLabel),
                    new Field("mask", "Public subnet mask", J.S(lan, "subnet_mask")).When("type", lanLabel),
                    new Field("router", "ISP router address", J.S(lan, "router_ip")).When("type", lanLabel),
                    new Field("start", "First public IP", J.S(nat, "subnet_ip_start")).When("type", natLabel),
                    new Field("end", "Last public IP", J.S(nat, "subnet_ip_end")).When("type", natLabel) }, async v =>
                {
                    string chosen = V(v, "type") == natLabel ? "NATWithPortFwd" : "PublicLAN";
                    var body = J.Obj("enabled", Flag(v, "enabled"), "type", chosen);
                    if (Flag(v, "enabled"))
                    {
                        if (chosen == "PublicLAN") body["multistaticip_settings"] = J.Obj("subnet_ip", FeatureContracts.Address(V(v, "subnet"), AddressFamily.InterNetwork), "subnet_mask", FeatureContracts.Address(V(v, "mask"), AddressFamily.InterNetwork), "router_ip", FeatureContracts.Address(V(v, "router"), AddressFamily.InterNetwork));
                        else body["multistaticip_settings_nat_portfwd"] = J.Obj("subnet_ip_start", FeatureContracts.Address(V(v, "start"), AddressFamily.InterNetwork), "subnet_ip_end", FeatureContracts.Address(V(v, "end"), AddressFamily.InterNetwork));
                    }
                    if (!await Confirm("Apply multiple-static-IP settings supplied by your ISP? Incorrect values can disrupt internet access.")) return;
                    await Change("PUT", "/2.3/networks/" + networkUrl.TrimEnd('/').Split('/').Last() + "/multistaticip", body, false, "Multiple static IP settings updated.");
                });
            })));
            if (enabled && type == "PublicLAN")
                Group("Current addresses", InfoRow("Public subnet", J.S(lan, "subnet_ip") + " / " + J.S(lan, "subnet_mask")), InfoRow("ISP router", J.S(lan, "router_ip")));
            else if (enabled)
                Group("Current addresses", InfoRow("Public IP range", J.S(nat, "subnet_ip_start") + " – " + J.S(nat, "subnet_ip_end")));
        }
        private void BusinessPortalPage(object data)
        {
            Sub("A branded sign-in page guests see before they can use your guest wifi. Useful for shops, cafés and offices.");
            var configs = Entries(data, "configurations");
            foreach (object raw in configs)
            {
                object config = raw;
                Func<int> configId = delegate { int id; if (!Int32.TryParse(J.First(config, "configuration_id", "id"), out id)) throw new InvalidOperationException("eero didn't return an ID for this portal."); return id; };
                bool captureEmail = J.B(config, "capture_email"), captureName = J.B(config, "capture_name");
                int minutes; string session = Int32.TryParse(J.S(config, "access_expiration_in_minutes"), out minutes) ? (minutes % 1440 == 0 ? minutes / 1440 + (minutes == 1440 ? " day" : " days") : minutes % 60 == 0 ? minutes / 60 + (minutes == 60 ? " hour" : " hours") : minutes + " minutes") : "Not set";
                Group(J.S(config, "header").NullIfEmpty() ?? "Guest sign-in portal",
                    SettingRow("Heading & theme", "The text and colors guests see.", (J.S(config, "header").NullIfEmpty() ?? "No heading") + " · " + (J.S(config, "color_theme").NullIfEmpty() ?? "Light"), RowAction("Change", delegate { PortalEdit(config); })),
                    SettingRow("Session length", "How long guests stay signed in before seeing the portal again.", session, RowAction("Change", delegate { PortalEdit(config); })),
                    SettingToggle("Ask for visitor email", "Guests enter an email address before getting online.", captureEmail, async delegate(bool on)
                    { await Change("PUT", FeatureUrl("captive_portal/configuration"), J.Obj("id", configId(), "capture_email", on, "capture_name", captureName), false, "Visitor email " + (on ? "required." : "no longer requested.")); }),
                    SettingToggle("Ask for visitor name", "Guests enter their name before getting online.", captureName, async delegate(bool on)
                    { await Change("PUT", FeatureUrl("captive_portal/configuration"), J.Obj("id", configId(), "capture_email", captureEmail, "capture_name", on), false, "Visitor name " + (on ? "required." : "no longer requested.")); }),
                    SettingRow("Logo", "A PNG or JPEG image shown on the portal.", J.S(config, "business_logo_url").Length > 0 ? "Added" : "None", RowActionAsync("Upload", async delegate { await UploadPortalAsset(config, true); })),
                    SettingRow("Terms of service", "A PDF guests can read before they accept.", J.S(config, "business_terms_of_service_url").Length > 0 ? "Added" : "None", RowActionAsync("Upload", async delegate { await UploadPortalAsset(config, false); })),
                    SettingRow("Use for guest wifi", "Show this portal when someone joins your guest network.", null, RowActionAsync("Use", async delegate { int id = configId(); if (!await Confirm("Use this sign-in portal for guest wifi?")) return; await Change("PUT", FeatureUrl("captive_portal/subnet_id/GUEST"), J.Obj("captive_portal_configuration_id", id), false, "Guest portal selected."); })));
            }
            if (data != null && configs.Count == 0) Group(null, InfoRow("No guest portals yet", null, "Guest portals are created for business networks through eero. Once one exists, you can edit it here."));
            Group("Other networks", SettingRow("Use on my other networks", "Turn the business guest portal on for every network in your account.", null, RowAction("Change", delegate { Edit("Business guest network", new[] { new Field("other", "Use the business guest portal on my other networks", false, "bool") }, async v => { await Change("PUT", FeatureUrl("business_guest_network"), ApkContracts.BusinessGuest(Flag(v, "other")), false, "Business guest network updated."); }); })));
            RelatedPages();
        }
        private void PortalEdit(object config)
        {
            Edit("Guest portal branding", new[] { new Field("header", "Heading", J.S(config, "header")), new Field("sub_header", "Subheading", J.S(config, "sub_header")), new Field("color_theme", "Theme", J.S(config, "color_theme").NullIfEmpty() ?? "Light", "choice", "Light", "Dark"), new Field("minutes", "Session length in minutes", J.S(config, "access_expiration_in_minutes")).Help("For example 60 for one hour or 1440 for one day."), new Field("logo", "Hosted business logo URL (optional)", J.S(config, "business_logo_url")), new Field("terms", "Hosted business terms URL (optional)", J.S(config, "business_terms_of_service_url")) }, async v =>
            {
                int id, minutes; if (!Int32.TryParse(J.First(config, "configuration_id", "id"), out id) || !Int32.TryParse(V(v, "minutes"), out minutes) || minutes < 1 || minutes > 525600) throw new ArgumentException("Enter a session duration between 1 and 525600 minutes.");
                foreach (string key in new[] { "logo", "terms" }) if (V(v, key).Length > 0) { Uri uri; if (!Uri.TryCreate(V(v, key), UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0) throw new ArgumentException("Use HTTPS URLs for hosted assets and terms."); }
                var body = J.Obj("id", id, "header", V(v, "header"), "sub_header", V(v, "sub_header"), "color_theme", V(v, "color_theme"), "access_expiration_in_minutes", minutes, "business_logo_url", V(v, "logo"), "business_terms_of_service_url", V(v, "terms"));
                await Change("PUT", FeatureUrl("captive_portal/configuration"), body, false, "Guest portal branding updated.");
            });
        }
    }
}
