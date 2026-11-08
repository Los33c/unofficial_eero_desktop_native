using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private string VpnScope { get { return VpnContracts.Scope(preview ? "preview-account" : J.First(account, "log_id", "url", "id", "email"), NetworkId); } }
        private SessionStore VpnStore { get { return new SessionStore(Path.Combine(WindowsVpn.UserDirectory, VpnScope + ".dat")); } }
        private object VpnState()
        {
            if (preview) return J.Obj("nickname", "Windows PC", "region_label", "New York", "pet", "sample");
            string value = VpnStore.Load(); return String.IsNullOrEmpty(value) ? J.Obj() : J.Parse(value);
        }
        private void VpnControls()
        {
            object state = VpnState(); string scope = VpnScope;
            bool registered = J.S(state, "pet").Length > 0, installed = !preview && WindowsVpn.Installed, located = J.S(state, "region_label").Length > 0;
            string status = preview ? "Disconnected · sample" : WindowsVpn.Status(scope);
            var install = RowActionAsync("Install", async delegate
            {
                if (preview) throw new InvalidOperationException("Sample mode does not install software.");
                object availability = await LoadGuardianAvailability();
                if (availability is ApiException || !(J.Get(availability, "seats_available") is bool)) throw new InvalidOperationException(VpnContracts.EligibilityMessage(availability));
                if (!Confirm("Download and run the digitally signed official WireGuard installer? Windows will ask for administrator permission. After installation, return here and choose Connect.")) return;
                await WindowsVpn.InstallComponent(); SetNotice("Windows VPN component installed. Choose a location and connect."); RenderPage();
            });
            Group("This PC",
                SettingRow("VPN connection", "Connect and Disconnect ask for Windows administrator permission. Closing this app leaves the VPN running.", status,
                    RowActionAsync("Connect", ConnectGuardian), RowActionAsync("Disconnect", DisconnectGuardian), RowAction("Refresh", RenderPage)),
                SettingRow("VPN location", "Where your traffic appears to come from.", J.S(state, "region_label").NullIfEmpty() ?? "Not chosen", RowActionAsync("Change", ChooseGuardianRegion)),
                J.S(state, "verified_utc").Length > 0 ? InfoRow("Last verified", J.S(state, "verified_utc"), "The last time the VPN tunnel confirmed it was working.") : null);
            Group("Set up in order",
                SettingRow("1. Register this PC", "Uses one of your plan's VPN device slots.", registered ? "✓ " + (J.S(state, "nickname").NullIfEmpty() ?? "Registered") : "Not registered", RowActionAsync(registered ? "Register again" : "Register", RegisterGuardian)),
                SettingRow("2. Install the Windows VPN component", "The official WireGuard app that creates the VPN tunnel. Needs administrator permission.", installed ? "✓ Installed" : preview ? "Sample" : "Not installed", install),
                SettingRow("3. Choose a location, then Connect", "Pick a location above, then choose Connect.", located ? "✓ Done" : "Not done", RowActionAsync("Choose", ChooseGuardianRegion)));
            Group("Devices", SettingRow("Your VPN devices", "See and remove devices that use your plan's VPN slots.", null, RowActionAsync("Manage", GuardianDevices)));
        }
        private async Task<object> LoadGuardianAvailability()
        {
            if (preview) throw new InvalidOperationException("Sign in to check Guardian VPN eligibility.");
            string scope = VpnScope; object result;
            featureData.Remove("guardian_availability");
            try { result = await api.Send("GET", "/2.2/partners/guardian/networks/" + NetworkId + "/availability"); }
            catch (ApiException error) { if (error.SessionExpired) throw; result = error; }
            if (scope == VpnScope) featureData["guardian_availability"] = result;
            return result;
        }
        private async Task RegisterGuardian()
        {
            object availability = await LoadGuardianAvailability();
            if (!VpnContracts.CanRegister(availability)) throw new InvalidOperationException(VpnContracts.EligibilityMessage(availability));
            Edit("Register this Windows VPN device", new[] { new Field("nickname", "Device name", Environment.MachineName), new Field("terms_link", "Read Guardian terms", "https://guardianapp.com/terms-and-conditions/", "link"), new Field("agree", "I agree to Guardian's terms and privacy policy", false, "bool") }, async values =>
            {
                if (preview) throw new InvalidOperationException("Sample mode does not register VPN devices.");
                if (!Flag(values, "agree")) throw new ArgumentException("Review and accept Guardian's terms before registering.");
                string nickname = V(values, "nickname").Trim(); ValidateName(nickname);
                object state = VpnState();
                if (J.S(state, "pet").Length > 0) throw new InvalidOperationException("This network already has a saved Windows registration. Choose a location or use Manage VPN devices.");
                if (!Confirm("Use an available Guardian VPN device slot for " + nickname + " on this network?")) return;
                object result = await api.Send("POST", "/2.2/partners/guardian/networks/" + NetworkId + "/pet", J.Obj("nickname", nickname));
                string pet = J.S(result, "guardian_pet"); if (pet.Length == 0) throw new InvalidDataException("eero did not return a VPN device token. Refresh Manage VPN devices before registering again.");
                try { VpnStore.Save(J.Stringify(J.Obj("pet", pet, "uuid", J.S(result, "uuid"), "nickname", nickname, "expiration", J.S(result, "expiration")))); }
                catch (Exception) { throw new IOException("eero registered the device, but Windows could not save it. Check Manage VPN devices and remove the registration before trying again."); }
                SetNotice("Windows device registered. Install the VPN component, then choose a VPN location."); RenderPage();
            }, "Register device", "Registration uses one of your subscription's VPN device slots. No connection is started here.");
        }
        private async Task ChooseGuardianRegion()
        {
            if (preview) throw new InvalidOperationException("Sign in to choose a Guardian VPN location.");
            object regions; using (var guardian = new GuardianApi()) regions = await guardian.Regions();
            var choices = new Dictionary<string, string>();
            foreach (object region in J.List(regions))
            {
                string name = J.S(region, "name"), label = J.First(region, "name-pretty", "name"); if (name.Length == 0) continue;
                string key = label + " · " + J.S(region, "country-iso-code"); if (choices.ContainsKey(key)) key += " · " + name; choices[key] = name;
            }
            if (choices.Count == 0) throw new InvalidOperationException("Guardian did not return any VPN locations.");
            Edit("VPN location", new[] { new Field("region", "Location", choices.Keys.OrderBy(x => x).First(), "choice", choices.Keys.OrderBy(x => x).ToArray()) }, async values =>
            {
                if (WindowsVpn.Running(VpnScope)) throw new InvalidOperationException("Disconnect VPN before changing its location.");
                if (!WindowsVpn.Installed) throw new InvalidOperationException("Install the Windows VPN component before choosing a location.");
                var state = VpnState() as Dictionary<string, object>; string pet = J.S(state, "pet"); if (pet.Length == 0) throw new InvalidOperationException("Register this Windows device first.");
                string selected = V(values, "region"); string[] keys = WindowsVpn.KeyPair();
                try { using (var guardian = new GuardianApi()) state["profile"] = await guardian.CreateProfile(pet, choices[selected], keys[0], keys[1]); }
                finally { keys[0] = keys[1] = null; }
                state["region"] = choices[selected]; state["region_label"] = selected; state.Remove("verified_utc"); VpnStore.Save(J.Stringify(state)); RenderPage(); SetNotice("VPN location saved. Choose Connect VPN to establish the Windows tunnel.");
            }, "Use location");
        }
        private async Task ConnectGuardian()
        {
            if (preview) throw new InvalidOperationException("Sample mode does not connect a VPN.");
            var state = VpnState() as Dictionary<string, object>; object profile = J.Get(state, "profile");
            if (profile == null) throw new InvalidOperationException("Register this Windows device and choose a location first.");
            if (!Confirm("Connect this PC to Guardian through " + J.S(state, "region_label") + "? Windows will request administrator permission to start the tunnel. Full-tunnel protection can block local network access while connected.")) return;
            object result = await WindowsVpn.Control("connect", VpnScope, profile);
            state["verified_utc"] = J.S(result, "verified_utc"); VpnStore.Save(J.Stringify(state)); RenderPage(); SetNotice(J.S(result, "message"));
        }
        private async Task DisconnectGuardian()
        {
            if (preview) throw new InvalidOperationException("Sample mode does not change a VPN.");
            if (!WindowsVpn.Running(VpnScope)) { SetNotice("This network's Windows VPN is already disconnected."); return; }
            object result = await WindowsVpn.Control("disconnect", VpnScope); RenderPage(); SetNotice(J.S(result, "message"));
        }
        private async Task GuardianDevices()
        {
            if (preview) { Info("VPN devices", J.Obj("device", "Sample Windows PC", "status", "Registered")); return; }
            object credentials = await api.Send("POST", "/2.2/partners/guardian"); object devices;
            using (var guardian = new GuardianApi()) devices = await guardian.Devices(credentials);
            var panel = new StackPanel { Margin = new System.Windows.Thickness(24) };
            panel.Children.Add(Text("Devices using your Guardian subscription", 23, "#17263C", true));
            var window = new System.Windows.Window { Title = "VPN devices · eero Desktop", Width = 720, Height = 690, Owner = this, WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            window.SourceInitialized += delegate { Program.ThemeNativeWindow(window); };
            foreach (object raw in J.List(devices))
            {
                object device = raw; string id = J.S(device, "ep-grd-device-uuid"); if (id.Length == 0) continue;
                var item = new StackPanel(); string name = J.S(device, "ep-grd-device-nickname").NullIfEmpty() ?? "VPN device";
                item.Children.Add(Text(name, 19, "#17263C", true)); item.Children.Add(Text("Activated: " + J.S(device, "ep-grd-device-created-at"), 13, "#64738A"));
                item.Children.Add(Button("Remove this VPN device", async delegate
                {
                    if (!Confirm("Remove " + name + " from your Guardian subscription? Its VPN access will stop.")) return;
                    object state = VpnState(); bool local = J.S(state, "uuid") == id;
                    if (local && WindowsVpn.Running(VpnScope)) throw new InvalidOperationException("Disconnect this Windows VPN before removing its registration.");
                    using (var guardian = new GuardianApi()) await guardian.RemoveDevice(credentials, id);
                    if (local) VpnStore.Clear(); window.Close(); SetNotice("VPN device removed."); RenderPage();
                })); panel.Children.Add(Card(item));
            }
            if (panel.Children.Count == 1) panel.Children.Add(Text("No devices were returned.", 14));
            window.Closed += delegate { credentials = null; }; window.Show();
        }
    }
}
