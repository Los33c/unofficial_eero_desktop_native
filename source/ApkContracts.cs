using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace EeroDesktop
{
    // Multipart request body (Retrofit @Multipart). Text parts keep their order; one optional file part.
    public sealed class MultipartFields
    {
        public readonly List<KeyValuePair<string, string>> Fields = new List<KeyValuePair<string, string>>();
        public string FileField, FileName, FileType; public byte[] File;
        public MultipartFields Add(string name, string value) { Fields.Add(new KeyValuePair<string, string>(name, value ?? "")); return this; }
        public string Value(string name) { var hit = Fields.FirstOrDefault(p => p.Key == name); return hit.Key == null ? null : hit.Value; }
        internal HttpContent Content()
        {
            var content = new MultipartFormDataContent();
            foreach (var p in Fields)
            {
                var part = new StringContent(p.Value, Encoding.UTF8, "text/plain");
                part.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"" + p.Key + "\"" };
                content.Add(part);
            }
            if (File != null)
            {
                var bytes = new ByteArrayContent(File); bytes.Headers.ContentType = new MediaTypeHeaderValue(FileType);
                bytes.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"" + FileField + "\"", FileName = "\"" + FileName + "\"" };
                content.Add(bytes);
            }
            return content;
        }
    }

    // Request contracts for endpoints present in eero Android 26.13.0 that were not yet used by this client.
    // Field names come from the app's Retrofit @Field/@Body/@Part declarations and Gson @SerializedName values.
    public static class ApkContracts
    {
        public static readonly string[] LedColors = { "blue", "green", "white", "off" };
        public static readonly string[] CustomerTypes = { "Residential", "Business", "MDU" };
        public static readonly string[] SafeFilterTypes = { "SubnetFilter", "ProfileFilter" };

        private static int Range(string value, int min, int max, string label)
        {
            int n; if (!Int32.TryParse((value ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < min || n > max) throw new ArgumentException("Enter " + label + " from " + min + " to " + max + ".");
            return n;
        }
        public static string Token(string value, string label, int max = 128)
        {
            string s = (value ?? "").Trim();
            if (!Regex.IsMatch(s, "^[A-Za-z0-9_.:-]{1," + max + "}$")) throw new ArgumentException("Enter a valid " + label + ".");
            return s;
        }
        public static List<string> Lines(string text) { return (text ?? "").Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList(); }

        // POST 2.2/eeros/{serial}/led_cycle (form): colors[], duration, time_per_color
        public static Dictionary<string, object> LedCycle(IEnumerable<string> colors, string duration, string timePerColor)
        {
            var list = (colors ?? new string[0]).Select(c => (c ?? "").Trim().ToLowerInvariant()).Where(c => c.Length > 0).ToList();
            if (list.Count == 0 || list.Count > 8 || list.Any(c => !LedColors.Contains(c))) throw new ArgumentException("Choose one to eight colors: blue, green, white or off.");
            return J.Obj("colors[]", list, "duration", Range(duration, 1, 600, "a duration in seconds"), "time_per_color", Range(timePerColor, 1, 60, "seconds per color"));
        }
        // POST 2.2/eeros/{id}/nightlight/override (form): brightness_percentage
        public static Dictionary<string, object> NightlightPreview(string brightness) { return J.Obj("brightness_percentage", Range(brightness, 0, 100, "a brightness")); }
        // PUT 2.2/networks/{id}/passpoint/enabled (JSON PasspointStatus)
        public static Dictionary<string, object> Passpoint(bool enabled) { return J.Obj("enabled", enabled); }
        // POST 2.2/networks/{id}/node_setup (form): default_channel_duration. The Android app uses 3600 seconds.
        public static Dictionary<string, object> DefaultChannel(string seconds) { return J.Obj("default_channel_duration", Range(seconds, 60, 7200, "a duration in seconds")); }
        // POST 2.2/eeros/networks/{networkId}/notify_swap (JSON NofifyNodeSwapRequest)
        public static Dictionary<string, object> NodeSwap(string oldSerial, string newSerial, bool succeeded)
        {
            string a = RuntimeContracts.Serial(oldSerial), b = RuntimeContracts.Serial(newSerial);
            if (String.Equals(a, b, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The replacement eero must have a different serial number.");
            return J.Obj("old_eero_serial", a, "new_eero_serial", b, "succeeded", succeeded);
        }
        // POST /2.3/networks/{id}/placement_rating/{serial} (JSON BluetoothPlacementTestRequest: rssi, mcs, freq; all optional)
        public static Dictionary<string, object> PlacementRating(string rssi, string mcs, string freq)
        {
            var body = new Dictionary<string, object>();
            if (!String.IsNullOrWhiteSpace(rssi)) { double r; if (!Double.TryParse(rssi.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out r) || r < -120 || r > 0) throw new ArgumentException("Enter RSSI in dBm from -120 to 0."); body["rssi"] = r; }
            if (!String.IsNullOrWhiteSpace(mcs)) body["mcs"] = Range(mcs, 0, 15, "an MCS index");
            if (!String.IsNullOrWhiteSpace(freq)) body["freq"] = Range(freq, 2400, 7200, "a frequency in MHz");
            return body;
        }
        // PUT /2.3/networks/{id}/devices/secondary_wan_config (JSON BackupInternetDeviceAccess)
        public static Dictionary<string, object> SecondaryWan(string mac, bool allow) { return J.Obj("mac", FeatureContracts.Mac(mac), "secondary_wan_deny_access", !allow); }
        // PUT 2.2/networks/{id}/business_guest_network (JSON EnableCaptivePortalOnBoarding)
        public static Dictionary<string, object> BusinessGuest(bool otherNetworks) { return J.Obj("enable_for_other_networks", otherNetworks); }
        // PUT dns_policies/network/allowed/cnames (JSON AllowCnamesForNetworkRequest)
        public static Dictionary<string, object> Cnames(string domains)
        {
            var list = Lines(domains).Select(FeatureContracts.Domain).Distinct().ToList();
            if (list.Count > 100) throw new ArgumentException("Enter no more than 100 domains.");
            return J.Obj("domains", list);
        }
        // PUT dns_policies/profiles/allowed/cnames (JSON AllowCnamesForProfilesRequest)
        public static Dictionary<string, object> ProfileCnames(string domains, IEnumerable<string> profileIds)
        {
            var body = Cnames(domains); var ids = (profileIds ?? new string[0]).Select(ExtendedContracts.Identifier).Distinct().ToList();
            if (ids.Count == 0) throw new ArgumentException("Choose at least one profile.");
            body["profiles"] = ids; return body;
        }
        // PUT 2.2/networks/{id}/flags/{name} (form): value
        public static string FlagName(string name)
        {
            string s = (name ?? "").Trim();
            if (!Regex.IsMatch(s, "^[a-z0-9_]{1,64}$")) throw new ArgumentException("Enter a flag name with lowercase letters, digits and underscores.");
            return s;
        }
        public static Dictionary<string, object> FlagValue(string value)
        {
            string s = (value ?? "").Trim(); if (s.Length == 0 || s.Length > 512) throw new ArgumentException("Enter a flag value of 1–512 characters.");
            return J.Obj("value", s);
        }
        // PUT 2.2/networks/{id}/network_customer_type (form): network_customer_type = NetworkType.name()
        public static Dictionary<string, object> CustomerType(string type)
        {
            if (!CustomerTypes.Contains(type)) throw new ArgumentException("Choose Residential, Business or MDU.");
            return J.Obj("network_customer_type", type);
        }
        public static string SafeFilterType(string type) { if (!SafeFilterTypes.Contains(type)) throw new ArgumentException("Choose SubnetFilter or ProfileFilter."); return type; }
        // 2.2/account/trust (form): certificate [, serial]
        public static string Certificate(string pem)
        {
            string s = (pem ?? "").Replace("\r\n", "\n").Trim();
            if (s.Length > 16384 || !Regex.IsMatch(s, "^-----BEGIN CERTIFICATE-----\n[A-Za-z0-9+/=\n]+\n-----END CERTIFICATE-----$")) throw new ArgumentException("Paste one PEM certificate that starts with -----BEGIN CERTIFICATE-----.");
            return s;
        }
        public static Dictionary<string, object> Trust(string pem, string oldSerial = null)
        {
            var body = J.Obj("certificate", Certificate(pem));
            if (oldSerial != null) body["serial"] = Token(oldSerial, "certificate serial");
            return body;
        }
        // GET 2.2/transfer/has_networks?email=…|phone=…
        public static string TransferTarget(string contact)
        {
            string s = (contact ?? "").Trim();
            if (s.Contains("@")) return "email=" + Uri.EscapeDataString(ExtendedContracts.Email(s));
            return "phone=" + Uri.EscapeDataString(PhoneInput.Normalize(s));
        }
        // POST 2.2/premium/customer/plans (form): autotrial_tier
        public static Dictionary<string, object> AutoTrial(string tier) { return J.Obj("autotrial_tier", Token(tier, "trial tier", 40)); }
        // POST 2.2/partners/encryptme (form): test=false selects the production partner environment.
        public static Dictionary<string, object> EncryptMe() { return J.Obj("test", false); }
        // Partner upsell flow (JSON bodies)
        public static Dictionary<string, object> UpsellMethod(string method, string sessionId) { return J.Obj("authentication_method", Token(method, "authentication method", 64), "authentication_session_id", Token(sessionId, "session ID", 256)); }
        public static Dictionary<string, object> UpsellAuthenticate(string key, string method, string sessionId)
        {
            string k = (key ?? "").Trim(); if (k.Length == 0 || k.Length > 256 || k.Any(c => c < 32)) throw new ArgumentException("Enter the verification code or key you received.");
            return J.Obj("authentication_key", k, "authentication_method", Token(method, "authentication method", 64), "session_id", Token(sessionId, "session ID", 256));
        }
        public static Dictionary<string, object> UpsellOrder(string offerIds, string sessionId, bool submit)
        {
            var ids = Lines(offerIds).Select(id => Token(id, "offer ID", 128)).Distinct().ToList();
            if (ids.Count == 0) throw new ArgumentException("Enter at least one offer ID.");
            return J.Obj("internal_offer_ids", ids, "authentication_session_id", Token(sessionId, "session ID", 256), "should_submit", submit);
        }
        // PUT 2.2/organizations/self/subsets/{subsetId}/associate (JSON AssociateEeroToSubsetRequest)
        public static Dictionary<string, object> SubsetAssociation(string serial, string unitIdentifier, string unitDescription)
        {
            string id = (unitIdentifier ?? "").Trim(), text = (unitDescription ?? "").Trim();
            if (id.Length == 0 || id.Length > 64 || text.Length > 200) throw new ArgumentException("Enter a unit identifier (up to 64 characters) and an optional description.");
            return J.Obj("eero_serial", RuntimeContracts.Serial(serial), "unit_identifier", id, "unit_description", text);
        }
        // GET 2.2/directedid?d=&i=&p=  (the Android app requests 0,0,0)
        public static string DirectedIdQuery(string d, string i, string p) { return "d=" + Range(d, 0, 99, "a directed-ID type") + "&i=" + Range(i, 0, 99, "an identifier type") + "&p=" + Range(p, 0, 99, "a partner"); }
        // GET 2.2/networks/{id}/ouicheck?serial=&version=
        public static string OuiQuery(string serial, string version)
        {
            string q = "serial=" + Uri.EscapeDataString(RuntimeContracts.Serial(serial));
            if (!String.IsNullOrWhiteSpace(version)) q += "&version=" + Uri.EscapeDataString(Token(version, "firmware version", 64));
            return q;
        }
        // POST 2.2/zendesk/create (multipart CreateReportIssue)
        // Labels are optional; the Android app loads its label list at runtime, so none are hard-coded here.
        public static MultipartFields Issue(string summary, string description, IEnumerable<string> labels, IEnumerable<string> eeros, IEnumerable<string> devices, string networkId, DateTime when)
        {
            string s = (summary ?? "").Trim(), d = (description ?? "").Trim();
            if (s.Length < 3 || s.Length > 200) throw new ArgumentException("Enter a summary of 3–200 characters.");
            if (d.Length < 10 || d.Length > 8000) throw new ArgumentException("Describe the issue in 10–8,000 characters.");
            var body = new MultipartFields();
            int n = 0; foreach (string e in (eeros ?? new string[0]).Distinct()) body.Add("affected_eeros[" + n++ + "]", RuntimeContracts.Serial(e));
            n = 0; foreach (string m in (devices ?? new string[0]).Distinct()) body.Add("affected_devices[" + n++ + "]", FeatureContracts.Mac(m));
            n = 0; foreach (string l in (labels ?? new string[0]).Distinct()) body.Add("labels[" + n++ + "]", Token(l, "label", 64));
            body.Add("issue_timestamp", when.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            body.Add("description", d).Add("summary", s).Add("network_id", networkId == null ? "" : ExtendedContracts.Identifier(networkId));
            return body;
        }
        public static void Screenshot(MultipartFields body, string name, byte[] png)
        {
            if (png == null || png.Length < 8 || png.Length > 8 * 1024 * 1024 || png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71) throw new ArgumentException("Attach a PNG image up to 8 MB.");
            body.File = png; body.FileField = "reportscreen"; body.FileType = "image/png"; body.FileName = Regex.Replace(System.IO.Path.GetFileName(name ?? "screenshot.png"), "[^A-Za-z0-9_.-]", "_");
        }
        public static bool IsPassthrough(object network) { return J.S(network, "connection", "mode") == "ipPassthrough"; }
        public static bool IsNotRouting(object network) { string mode = J.S(network, "connection", "mode"); return mode == "bridged" || mode == "ipPassthrough"; }
        public static bool PassthroughCapable(object network) { return J.B(network, "capabilities", "ip_passthrough", "capable"); }
    }
}
