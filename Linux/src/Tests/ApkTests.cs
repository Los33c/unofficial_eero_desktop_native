using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace EeroDesktop
{
    internal static partial class Tests
    {
        private static bool Rejects(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }
        // Contracts for the endpoints added from the eero Android 26.13.0 audit.
        private static async Task ApkChecks(string scratch)
        {
            // IP Passthrough (new connection mode in 26.13.0): same DHCP request as bridge, mode ipPassthrough, no dhcp block.
            object pass = FeatureContracts.Dhcp("passthrough", "", "", "", "", false);
            Assert(J.S(pass, "connection", "mode") == "ipPassthrough" && J.Get(pass, "dhcp") == null, "IP Passthrough serializes as ipPassthrough without DHCP settings");
            Assert(J.S(FeatureContracts.Dhcp("automatic", "", "", "", "", false), "connection", "mode") == "nat", "automatic DHCP still uses NAT");
            object passNet = J.Obj("connection", J.Obj("mode", "ipPassthrough"), "capabilities", J.Obj("ip_passthrough", J.Obj("capable", true)));
            Assert(ApkContracts.IsPassthrough(passNet) && ApkContracts.IsNotRouting(passNet) && ApkContracts.PassthroughCapable(passNet), "passthrough networks are detected and treated as non-routing");
            Assert(!ApkContracts.IsNotRouting(J.Obj("connection", J.Obj("mode", "nat"))), "NAT networks keep routing features");

            // Form bodies, including Retrofit list fields.
            Assert(String.Join("&", EeroApi.FormPairs(ApkContracts.LedCycle(new[] { "blue", "white" }, "30", "1")).Select(p => p.Key + "=" + p.Value)) == "colors[]=blue&colors[]=white&duration=30&time_per_color=1", "LED cycle repeats colors[] per color");
            Assert(Rejects(() => ApkContracts.LedCycle(new[] { "purple" }, "30", "1")) && Rejects(() => ApkContracts.LedCycle(new[] { "blue" }, "0", "1")), "LED cycle rejects unknown colors and durations");
            Assert(J.Stringify(ApkContracts.NightlightPreview("40")) == "{\"brightness_percentage\":40}" && Rejects(() => ApkContracts.NightlightPreview("101")), "nightlight preview brightness contract");
            Assert(J.Stringify(ApkContracts.Passpoint(true)) == "{\"enabled\":true}", "Passpoint body");
            Assert(J.Stringify(ApkContracts.DefaultChannel("3600")) == "{\"default_channel_duration\":3600}" && Rejects(() => ApkContracts.DefaultChannel("10")), "default-channel setup duration contract");
            Assert(J.Stringify(ApkContracts.NodeSwap("GGC1UC0000000001", "GGC1UC0000000002", true)) == "{\"old_eero_serial\":\"GGC1UC0000000001\",\"new_eero_serial\":\"GGC1UC0000000002\",\"succeeded\":true}", "eero swap notification body");
            Assert(Rejects(() => ApkContracts.NodeSwap("GGC1UC0000000001", "ggc1uc0000000001", true)), "eero swap needs two different serials");
            Assert(J.Stringify(ApkContracts.PlacementRating("", "", "")) == "{}" && J.Stringify(ApkContracts.PlacementRating("-61.5", "7", "5180")) == "{\"rssi\":-61.5,\"mcs\":7,\"freq\":5180}", "placement rating measurements are optional and typed");
            Assert(J.Stringify(ApkContracts.SecondaryWan("02-00-00-00-00-0A", false)) == "{\"mac\":\"02:00:00:00:00:0a\",\"secondary_wan_deny_access\":true}", "device backup access body normalizes MAC");
            Assert(J.Stringify(ApkContracts.BusinessGuest(true)) == "{\"enable_for_other_networks\":true}", "business guest network body");
            Assert(J.Stringify(ApkContracts.Cnames("Example.com\ncdn.example.com.\nexample.com")) == "{\"domains\":[\"example.com\",\"cdn.example.com\"]}", "CNAME allow list is normalized and de-duplicated");
            Assert(Rejects(() => ApkContracts.Cnames("https://example.com/path")) && Rejects(() => ApkContracts.ProfileCnames("example.com", new string[0])), "CNAME lists reject URLs and require a profile");
            Assert(J.Stringify(ApkContracts.ProfileCnames("example.com", new[] { "12", "12", "34" })) == "{\"domains\":[\"example.com\"],\"profiles\":[\"12\",\"34\"]}", "profile CNAME body");
            Assert(ApkContracts.FlagName("custom_whitelist_blacklist") == "custom_whitelist_blacklist" && Rejects(() => ApkContracts.FlagName("../account")), "network flag names cannot change the API path");
            Assert(J.Stringify(ApkContracts.CustomerType("MDU")) == "{\"network_customer_type\":\"MDU\"}" && Rejects(() => ApkContracts.CustomerType("Enterprise")), "network type uses the app's enum names");
            const string pem = "-----BEGIN CERTIFICATE-----\nTUlJQlNhbXBsZQ==\n-----END CERTIFICATE-----";
            Assert(J.S(ApkContracts.Trust(pem + "\n"), "certificate") == pem && J.S(ApkContracts.Trust(pem, "AB:CD"), "serial") == "AB:CD", "trusted certificate add and replace bodies");
            Assert(Rejects(() => ApkContracts.Certificate("-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----")), "private keys are never accepted as certificates");
            Assert(ApkContracts.TransferTarget("Person@Example.com").StartsWith("email=") && ApkContracts.TransferTarget("4255550100") == "phone=%2B14255550100", "transfer recipient check query");
            Assert(J.Stringify(ApkContracts.UpsellOrder("offer-1\noffer-2", "session-1", false)) == "{\"internal_offer_ids\":[\"offer-1\",\"offer-2\"],\"authentication_session_id\":\"session-1\",\"should_submit\":false}", "partner order body");
            Assert(J.Stringify(ApkContracts.SubsetAssociation("GGC1UC0000000001", "4B", "")) == "{\"eero_serial\":\"GGC1UC0000000001\",\"unit_identifier\":\"4B\",\"unit_description\":\"\"}", "organization unit association body");
            Assert(ApkContracts.DirectedIdQuery("0", "0", "0") == "d=0&i=0&p=0" && ApkContracts.OuiQuery("GGC1UC0000000001", "v7.1.1") == "serial=GGC1UC0000000001&version=v7.1.1", "directed-ID and OUI query strings");

            // Issue report multipart fields.
            var issue = ApkContracts.Issue("Slow wifi", "Wifi upstairs drops every evening.", new[] { "wifi" }, new[] { "GGC1UC0000000001" }, new[] { "020000000001" }, "17", new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc));
            Assert(issue.Value("affected_eeros[0]") == "GGC1UC0000000001" && issue.Value("affected_devices[0]") == "02:00:00:00:00:01" && issue.Value("labels[0]") == "wifi" && issue.Value("issue_timestamp") == "2026-11-07T12:00:00Z" && issue.Value("network_id") == "17" && issue.Value("summary") == "Slow wifi", "issue report uses the app's multipart field names");
            Assert(Rejects(() => ApkContracts.Issue("x", "too short", null, null, null, null, DateTime.UtcNow)), "issue reports need a summary and description");
            Assert(Rejects(() => ApkContracts.Screenshot(issue, "a.png", new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })), "only PNG screenshots are attached");

            // Wire format through the authenticated client.
            var store = new SessionStore(System.IO.Path.Combine(scratch, "apk-session.dat"));
            var fake = new Fake(); fake.Steps.Enqueue(Login());
            fake.Steps.Enqueue(new Step { Method = "POST", Path = "/2.2/login/verify", Cookie = "s=pending-test", SetCookie = "s=apk-test; Path=/; HttpOnly; Secure" });
            fake.Steps.Enqueue(new Step { Method = "POST", Path = "/2.2/eeros/GGC1UC0000000001/led_cycle", Cookie = "s=apk-test", Body = "colors%5B%5D=blue&colors%5B%5D=off&duration=20&time_per_color=2", ContentType = "application/x-www-form-urlencoded" });
            fake.Steps.Enqueue(new Step { Method = "PUT", Path = "/2.2/networks/17/passpoint/enabled", Cookie = "s=apk-test", Body = "{\"enabled\":false}", ContentType = "application/json" });
            fake.Steps.Enqueue(new Step { Method = "PUT", Path = "/2.2/networks/17/settings", Cookie = "s=apk-test", Body = "{\"connection\":{\"mode\":\"ipPassthrough\"},\"nat_port_randomization\":false}", ContentType = "application/json" });
            fake.Steps.Enqueue(new Step { Method = "GET", Path = "/2.2/networks/17/safe_filter_levels", Query = "?safeFilterType=SubnetFilter", Cookie = "s=apk-test" });
            fake.Steps.Enqueue(new Step { Method = "POST", Path = "/2.2/zendesk/create", Cookie = "s=apk-test", ContentType = "multipart/form-data" });
            fake.Steps.Enqueue(new Step { Method = "POST", Path = "/2.2/register/email/resend", Cookie = "s=apk-test", Body = "\"\"", ContentType = "application/json" });
            using (var api = new EeroApi(store, fake))
            {
                await api.BeginLogin("person@example.com"); await api.Verify("123456");
                await api.Send("POST", "/2.2/eeros/GGC1UC0000000001/led_cycle", ApkContracts.LedCycle(new[] { "blue", "off" }, "20", "2"), true);
                await api.Send("PUT", "/2.2/networks/17/passpoint/enabled", ApkContracts.Passpoint(false));
                await api.Send("PUT", "/2.2/networks/17/settings", FeatureContracts.Dhcp("passthrough", "", "", "", "", false));
                await api.Send("GET", "/2.2/networks/17/safe_filter_levels?safeFilterType=" + ApkContracts.SafeFilterType("SubnetFilter"));
                await api.Send("POST", "/2.2/zendesk/create", issue);
                await api.Send("POST", "/2.2/register/email/resend", "");
                Assert(fake.Steps.Count == 0, "26.13.0 endpoint wire formats");
            }
            // Moved here from ApkUiChecks: it needs no display, so it also runs headless.
            Assert(FeatureCatalog.Entries.Count(e => e.Path == "Help/Report an issue" || e.Path == "Activity/Usage details" || e.Path == "Home/Add or replace eero/Setup tools") == 3, "new feature pages are listed in the catalog");
        }
        // GTK half of the 26.13.0 checks (needs a display; see GtkChecks). "Renders" means the page builds
        // without an exception and fits the 1040x720 minimum layout, as the WPF check arranged it at 1040 wide.
        private static void ApkUiChecks(MainWindow window)
        {
            foreach (string route in new[] { "Home/Add or replace eero/Setup tools", "Activity/Usage details", "Settings/Advanced networking/Network flags", "Settings/Network management/Organization & community", "Settings/Account settings/Trusted certificates", "Settings/Account settings/Account data", "Settings/Account settings/Subscriptions/Entitlements & trials", "Settings/Account settings/Subscriptions/Partner offers", "Help/Report an issue", "Settings/Advanced networking/DHCP & NAT", "Settings/Advanced networking/Wireless settings", "Settings/Wifi name & password", "Home/Security & privacy", "Settings/Notifications", "Settings/User permissions", "Settings/Advanced networking/Internet backup", "Settings/Business guest wifi", "Settings/Network management", "Settings/Account settings", "Home/eeros/Details", "Activity", "Help" })
            {
                Isolate("26.13.0 page " + route, delegate
                {
                    window.PreviewPage(route); Pump(40);
                    Assert(Fits(window, 1040, 720), "26.13.0 feature page renders: " + route);
                });
            }
        }
    }
}
