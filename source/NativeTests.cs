using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace EeroDesktop
{
    internal static partial class Tests
    {
        private static void NativeChecks()
        {
            var cases = new[] {
                new[] { "eero Pro 6E", "S010001", "eero-pro-6e" }, new[] { "eero Max 7", "V010001", "eero-max-7" },
                new[] { "eero", "A010001", "eero-gen1" }, new[] { "eero", "J010011", "eero-gen2" },
                new[] { "eero Pro", "B010011", "eero-pro" }, new[] { "eero Beacon", "D010001", "eero-beacon" },
                new[] { "eero Pro 6", "K010011", "eero-pro-6" }, new[] { "eero 6", "N010001", "eero-6" },
                new[] { "eero 6 Extender", "Q010011", "eero-6-extender" }, new[] { "eero 6+", "R010001", "eero-6-plus" },
                new[] { "eero PoE 6", "T010001", "eero-poe-6" }, new[] { "eero PoE Gateway", "U010001", "eero-poe-gateway" },
                new[] { "eero 7", "PA10001", "eero-7" }, new[] { "eero Pro 7", "ME10001", "eero-pro-7" },
                new[] { "eero Outdoor 7", "SN10001", "eero-outdoor-7" }, new[] { "eero PoE 7", "NV01", "eero-poe-7" },
                new[] { "eero Signal", "RG4U", "eero-signal-4g" }, new[] { "eero Signal", "RG5W", "eero-signal-5g" },
                new[] { "eero 6+ Extender", "", "eero-6-plus-extender" }, new[] { "eero Telephone Adapter", "FH01", "eero-telephone-adapter" }
            };
            foreach (var item in cases)
            {
                Assert(ProductImages.Resolve(item[0], item[1]) == item[2], "hardware photograph matches " + item[0] + " " + item[1]);
                var bitmap = ProductImages.Load(item[2]); Assert(bitmap != null && bitmap.PixelWidth >= 100 && bitmap.IsFrozen, "bundled image loads without network: " + item[2]);
            }
            Assert(ProductImages.Resolve("eero Pro 6E", "V010001") == "eero-max-7", "known hardware code takes priority over conflicting label");
            Assert(ProductImages.Resolve("eero_pro_6e", "") == "eero-pro-6e", "API model-name formatting is normalized");
            Assert(ProductImages.Resolve("Unrecognized model", "UNKNOWN") == null && ProductImages.Resolve("eero", "") == null, "unknown or ambiguous models do not display a different product");
            Assert(ProductImages.Resolve("eero Pro 6E", "") != ProductImages.Resolve("eero Pro 6", ""), "Pro 6 and Pro 6E remain distinct");
            Assert(ProductImages.Ids.Length == 20 && ProductImages.Ids.All(id => ProductImages.Load(id) != null), "complete 20-variant image catalog is embedded");

            var onLatest = new object[] { J.Obj("os_version", "7.1.0"), J.Obj("os_version", "7.1.0") };
            Assert(MainWindow.UpdateState(J.Obj("has_update", false, "can_update_now", true, "target_firmware", "7.1.0"), onLatest) == "current", "can_update_now alone does not report an update when eero says there is none");
            Assert(MainWindow.UpdateState(J.Obj("can_update_now", true), new object[0]) == "unknown", "can_update_now without an update flag is not shown as an available update");
            Assert(MainWindow.UpdateState(J.Obj("update_status", "up_to_date", "can_update_now", true), onLatest) == "current", "up_to_date status wins over can_update_now");
            Assert(MainWindow.UpdateState(J.Obj("has_update", true, "target_firmware", "v7.1.0"), onLatest) == "current", "eeros already on the target version are up to date");
            Assert(MainWindow.UpdateState(J.Obj("has_update", true, "update_to_firmware", "7.2.0"), onLatest) == "available", "a newer target version is reported as available");
            Assert(MainWindow.UpdateState(J.Obj("has_update", true, "target_firmware", "7.2.0"), new object[] { J.Obj("os_version", "7.2.0"), J.Obj("os_version", "7.1.0") }) == "available", "one eero behind the target still has an update");
            string decimalSpeed = (2.5).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + " Gbps";
            Assert(MainWindow.FriendlyValue("negotiated_speed", "P1000") == "1 Gbps" && MainWindow.FriendlyValue("supported_speed", "P2500") == decimalSpeed, "port speed codes read as Mbps/Gbps");
            Assert(MainWindow.FriendlyValue("port_status", "DATA_CONNECTED") == "Data connected" && MainWindow.PortState("DATA_CONNECTED") == "Connected" && MainWindow.PortState("DISCONNECTED") == "Nothing plugged in", "port status codes read as words");
            Assert(MainWindow.FriendlyValue("capable_protocols", "ETHERNET_CABLE_DIAGNOSTICS_PROVIDER") == "Ethernet cable diagnostics provider" && MainWindow.FriendlyValue("mode", "WAN_DHCP") == "WAN DHCP", "upper-case codes become sentences with acronyms kept");
            Assert(MainWindow.FriendlyValue("upload_bytes", 1536L) == (1.5).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + " KB" && MainWindow.FriendlyValue("rssi", -58) == "Strong (-58 dBm)", "byte counts and signal strength are readable");
            Assert(MainWindow.FriendlyValue("is_guest", true) == "Yes" && MainWindow.FriendlyValue("led_on", false) == "Off" && MainWindow.FriendlyValue("name", "Office 2") == "Office 2", "yes/no and on/off fit the field; plain text is untouched");
            Assert(MainWindow.Human("interface_number") == "Port number" && MainWindow.Human("wan_ip_address") == "WAN IP address" && MainWindow.Human("networkType") == "Network type", "field names read as plain words");
            Assert(MainWindow.RuleBadge("Home/Profiles", J.Obj("connection", J.Obj("mode", "bridged"))) == "Not in bridge mode" && MainWindow.RuleBadge("Home/Profiles", J.Obj("connection", J.Obj("mode", "nat"))) == null, "bridge mode marks routing-only pages");
            Assert(MainWindow.RuleBadge("Activity/Security activity", J.Obj("premium_status", "inactive")) == "Needs eero Plus" && MainWindow.RuleBadge("Activity/Security activity", J.Obj("premium_status", "active")) == null && MainWindow.RuleBadge("Activity/Security activity", J.Obj()) == null, "Plus badge only when eero clearly reports no plan");
            Assert(MainWindow.RuleBadge("Settings/Business guest wifi", J.Obj("network_customer_type", "RESIDENTIAL")) == "Business networks only" && MainWindow.RuleBadge("Settings/Wifi name & password", J.Obj("network_customer_type", "RESIDENTIAL")) == null, "business badge only on business pages of home networks");
            Assert(FeatureAvailability.ReasonFor(403) == "Not available on your account" && FeatureAvailability.ReasonFor(404) == "Not available on this network" && FeatureAvailability.ReasonFor(500) == null && FeatureAvailability.ReasonFor(0) == null, "only refusals are remembered, not outages");
            FeatureAvailability.Mark("net-1", "Settings/Advanced networking/Thread", "Not available on this network"); FeatureAvailability.Reset();
            Assert(FeatureAvailability.Get("net-1", "Settings/Advanced networking/Thread") == "Not available on this network" && FeatureAvailability.Get("net-2", "Settings/Advanced networking/Thread") == null, "learned badges persist per network");
            FeatureAvailability.Clear("net-1", "Settings/Advanced networking/Thread"); FeatureAvailability.Reset();
            Assert(FeatureAvailability.Get("net-1", "Settings/Advanced networking/Thread") == null, "a page that loads clears its badge");
            var gaps = MainWindow.UngroupedMenuEntries();
            Assert(gaps.Count == 0, "grouped settings menus place every catalog page" + (gaps.Count == 0 ? "" : ": " + String.Join(", ", gaps)));
            Assert(MainWindow.SearchCatalog("dns", null, 8).First().Path == "Settings/Advanced networking/DNS", "settings search ranks an exact title first");
            Assert(MainWindow.SearchCatalog("wifi password", null, 8).Any(e => e.Path == "Settings/Wifi name & password"), "settings search matches every word across title and description");
            Assert(MainWindow.SearchCatalog("guest", "Settings", 20).All(e => e.Path.StartsWith("Settings/")), "page filter stays inside its menu");
            Assert(MainWindow.SearchCatalog("devices", null, 8).First().Path == "Devices", "search includes top-level pages");
            Assert(MainWindow.SearchCatalog("zzqx", null, 8).Count == 0 && MainWindow.SearchCatalog("  ", null, 8).Count == 0, "search returns nothing for unmatched or empty queries");
            foreach (string theme in new[] { "Light", "Dark" })
            {
                UiPreferences.Theme = theme; Program.ApplyTheme(); var window = new MainWindow(true);
                foreach (string route in new[] { "Home", "Devices", "Activity", "Settings", "Settings/Advanced networking", "Settings/Advanced networking/Wireless settings", "Settings/Advanced networking/IPv6", "Home/Profiles", "Home/Profiles/Profile", "Home/eeros/Details", "Settings/Appearance", "Product gallery" })
                {
                    window.PreviewPage(route); var root = (FrameworkElement)window.Content;
                    root.Measure(new Size(1040, 720)); root.Arrange(new Rect(0, 0, 1040, 720)); root.UpdateLayout();
                    Assert(root.ActualWidth == 1040 && root.ActualHeight == 720, "minimum Windows layout renders " + theme + " " + route);
                }
                window.Close();
            }
            UiPreferences.Theme = "Light"; Program.ApplyTheme(); var liveWindow = new MainWindow(true); liveWindow.PreviewPage("Home");
            UiPreferences.Theme = "Dark"; Program.ApplyTheme(); var liveRoot = (FrameworkElement)liveWindow.Content;
            liveRoot.Measure(new Size(1360, 880)); liveRoot.Arrange(new Rect(0, 0, 1360, 880)); liveRoot.UpdateLayout();
            Assert(!ThemeVerification.HasWhiteCardSurface(liveRoot), "existing cards recolor when Windows theme changes without rebuilding the page");
            liveWindow.Close();
        }
    }
}
