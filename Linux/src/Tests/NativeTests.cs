using System;
using System.Linq;
using Gtk;

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
                // Gdk.Texture is immutable, so the WPF IsFrozen part of this check has no counterpart.
                var bitmap = ProductImages.Load(item[2]); Assert(bitmap != null && bitmap.GetWidth() >= 100, "bundled image loads without network: " + item[2]);
            }
            Assert(ProductImages.Resolve("eero Pro 6E", "V010001") == "eero-max-7", "known hardware code takes priority over conflicting label");
            Assert(ProductImages.Resolve("eero_pro_6e", "") == "eero-pro-6e", "API model-name formatting is normalized");
            Assert(ProductImages.Resolve("Unrecognized model", "UNKNOWN") == null && ProductImages.Resolve("eero", "") == null, "unknown or ambiguous models do not display a different product");
            Assert(ProductImages.Resolve("eero Pro 6E", "") != ProductImages.Resolve("eero Pro 6", ""), "Pro 6 and Pro 6E remain distinct");
            Assert(ProductImages.Ids.Length == 20 && ProductImages.Ids.All(id => ProductImages.Load(id) != null), "complete 20-variant image catalog is embedded");

            var onLatest = new object[] { J.Obj("os_version", "7.1.0"), J.Obj("os_version", "7.1.0") };
            Check("can_update_now alone does not report an update when eero says there is none", () => MainWindow.UpdateState(J.Obj("has_update", false, "can_update_now", true, "target_firmware", "7.1.0"), onLatest) == "current");
            Check("can_update_now without an update flag is not shown as an available update", () => MainWindow.UpdateState(J.Obj("can_update_now", true), new object[0]) == "unknown");
            Check("up_to_date status wins over can_update_now", () => MainWindow.UpdateState(J.Obj("update_status", "up_to_date", "can_update_now", true), onLatest) == "current");
            Check("eeros already on the target version are up to date", () => MainWindow.UpdateState(J.Obj("has_update", true, "target_firmware", "v7.1.0"), onLatest) == "current");
            Check("a newer target version is reported as available", () => MainWindow.UpdateState(J.Obj("has_update", true, "update_to_firmware", "7.2.0"), onLatest) == "available");
            Check("one eero behind the target still has an update", () => MainWindow.UpdateState(J.Obj("has_update", true, "target_firmware", "7.2.0"), new object[] { J.Obj("os_version", "7.2.0"), J.Obj("os_version", "7.1.0") }) == "available");
            string decimalSpeed = (2.5).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + " Gbps";
            Check("port speed codes read as Mbps/Gbps", () => MainWindow.FriendlyValue("negotiated_speed", "P1000") == "1 Gbps" && MainWindow.FriendlyValue("supported_speed", "P2500") == decimalSpeed);
            Check("port status codes read as words", () => MainWindow.FriendlyValue("port_status", "DATA_CONNECTED") == "Data connected" && MainWindow.PortState("DATA_CONNECTED") == "Connected" && MainWindow.PortState("DISCONNECTED") == "Nothing plugged in");
            Check("upper-case codes become sentences with acronyms kept", () => MainWindow.FriendlyValue("capable_protocols", "ETHERNET_CABLE_DIAGNOSTICS_PROVIDER") == "Ethernet cable diagnostics provider" && MainWindow.FriendlyValue("mode", "WAN_DHCP") == "WAN DHCP");
            Check("byte counts and signal strength are readable", () => MainWindow.FriendlyValue("upload_bytes", 1536L) == (1.5).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + " KB" && MainWindow.FriendlyValue("rssi", -58) == "Strong (-58 dBm)");
            Check("yes/no and on/off fit the field; plain text is untouched", () => MainWindow.FriendlyValue("is_guest", true) == "Yes" && MainWindow.FriendlyValue("led_on", false) == "Off" && MainWindow.FriendlyValue("name", "Office 2") == "Office 2");
            Check("field names read as plain words", () => MainWindow.Human("interface_number") == "Port number" && MainWindow.Human("wan_ip_address") == "WAN IP address" && MainWindow.Human("networkType") == "Network type");
            Check("bridge mode marks routing-only pages", () => MainWindow.RuleBadge("Home/Profiles", J.Obj("connection", J.Obj("mode", "bridged"))) == "Not in bridge mode" && MainWindow.RuleBadge("Home/Profiles", J.Obj("connection", J.Obj("mode", "nat"))) == null);
            Check("Plus badge only when eero clearly reports no plan", () => MainWindow.RuleBadge("Activity/Security activity", J.Obj("premium_status", "inactive")) == "Needs eero Plus" && MainWindow.RuleBadge("Activity/Security activity", J.Obj("premium_status", "active")) == null && MainWindow.RuleBadge("Activity/Security activity", J.Obj()) == null);
            Check("business badge only on business pages of home networks", () => MainWindow.RuleBadge("Settings/Business guest wifi", J.Obj("network_customer_type", "RESIDENTIAL")) == "Business networks only" && MainWindow.RuleBadge("Settings/Wifi name & password", J.Obj("network_customer_type", "RESIDENTIAL")) == null);
            Check("only refusals are remembered, not outages", () => FeatureAvailability.ReasonFor(403) == "Not available on your account" && FeatureAvailability.ReasonFor(404) == "Not available on this network" && FeatureAvailability.ReasonFor(500) == null && FeatureAvailability.ReasonFor(0) == null);
            FeatureAvailability.Mark("net-1", "Settings/Advanced networking/Thread", "Not available on this network"); FeatureAvailability.Reset();
            Check("learned badges persist per network", () => FeatureAvailability.Get("net-1", "Settings/Advanced networking/Thread") == "Not available on this network" && FeatureAvailability.Get("net-2", "Settings/Advanced networking/Thread") == null);
            FeatureAvailability.Clear("net-1", "Settings/Advanced networking/Thread"); FeatureAvailability.Reset();
            Check("a page that loads clears its badge", () => FeatureAvailability.Get("net-1", "Settings/Advanced networking/Thread") == null);
            var gaps = MainWindow.UngroupedMenuEntries();
            Assert(gaps.Count == 0, "grouped settings menus place every catalog page" + (gaps.Count == 0 ? "" : ": " + String.Join(", ", gaps)));
            Check("settings search ranks an exact title first", () => MainWindow.SearchCatalog("dns", null, 8).First().Path == "Settings/Advanced networking/DNS");
            Check("settings search matches every word across title and description", () => MainWindow.SearchCatalog("wifi password", null, 8).Any(e => e.Path == "Settings/Wifi name & password"));
            Check("page filter stays inside its menu", () => MainWindow.SearchCatalog("guest", "Settings", 20).All(e => e.Path.StartsWith("Settings/")));
            Check("search includes top-level pages", () => MainWindow.SearchCatalog("devices", null, 8).First().Path == "Devices");
            Check("search returns nothing for unmatched or empty queries", () => MainWindow.SearchCatalog("zzqx", null, 8).Count == 0 && MainWindow.SearchCatalog("  ", null, 8).Count == 0);
        }
        // The page fits the window's minimum layout: GTK's minimum size for the window at that page is within the bounds.
        private static bool Fits(MainWindow window, int width, int height)
        {
            int minWidth, natural, baseline, naturalBaseline, minHeight;
            window.Window.Measure(Orientation.Horizontal, -1, out minWidth, out natural, out baseline, out naturalBaseline);
            window.Window.Measure(Orientation.Vertical, width, out minHeight, out natural, out baseline, out naturalBaseline);
            return window.Window.GetWidth() > 0 && minWidth <= width && minHeight <= height;
        }
        // GTK half of NativeChecks (needs a display; see GtkChecks).
        private static void NativeUiChecks(MainWindow window)
        {
            foreach (string theme in new[] { "Light", "Dark" })
            {
                SwitchTheme(theme, window);
                foreach (string route in new[] { "Home", "Devices", "Activity", "Settings", "Settings/Advanced networking", "Settings/Advanced networking/Wireless settings", "Settings/Advanced networking/IPv6", "Home/Profiles", "Home/Profiles/Profile", "Home/eeros/Details", "Settings/Appearance", "Product gallery" })
                {
                    Isolate("minimum layout " + theme + " " + route, delegate
                    {
                        window.PreviewPage(route); Pump(40);
                        Assert(Fits(window, 1040, 720), "minimum Linux layout renders " + theme + " " + route);
                    });
                }
            }
            Isolate("live theme change", delegate
            {
                SwitchTheme("Light", window); window.PreviewPage("Home"); Pump(80);
                UiPreferences.Theme = "Dark"; Program.ApplyTheme(); Pump(250);
                NoWhite(ThemeVerification.HasWhiteCardSurface(window.Window), "existing cards recolor when the desktop theme changes while the page is open");
            });
        }
    }
}
