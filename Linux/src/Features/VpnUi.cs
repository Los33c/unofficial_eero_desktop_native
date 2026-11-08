using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gtk;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private string VpnScope { get { return VpnContracts.Scope(preview ? "preview-account" : J.First(account, "log_id", "url", "id", "email"), NetworkId); } }
        private SessionStore VpnStore { get { LinuxVpn.PrivateUserDirectory(); return new SessionStore(System.IO.Path.Combine(LinuxVpn.UserDirectory, VpnScope + ".dat")); } }
        private object VpnState()
        {
            if (preview) return J.Obj("nickname", "This computer", "region_label", "New York", "pet", "sample");
            string value = VpnStore.Load(); return String.IsNullOrEmpty(value) ? J.Obj() : J.Parse(value);
        }
        private void VpnControls()
        {
            object state = VpnState(); string scope = VpnScope;
            bool registered = J.S(state, "pet").Length > 0, installed = !preview && LinuxVpn.Installed, located = J.S(state, "region_label").Length > 0;
            string status = preview ? "Disconnected · sample" : LinuxVpn.Status(scope);
            string backend = preview ? null : LinuxVpn.Backend;
            var install = RowAction(installed ? "Details" : "How to install", delegate
            {
                Info("WireGuard on Linux", J.Obj("status", preview ? "Sample" : installed ? "Installed" : "Not installed", "connection_manager", backend ?? "Not found", "what_to_do", preview ? "Sample mode does not check installed software." : LinuxVpn.MissingComponents));
            });
            Group("This computer",
                SettingRow("VPN connection", "Connect and Disconnect may ask for administrator permission. Closing this app leaves the VPN running.", status,
                    RowActionAsync("Connect", ConnectGuardian), RowActionAsync("Disconnect", DisconnectGuardian), RowAction("Refresh", RenderPage)),
                SettingRow("VPN location", "Where your traffic appears to come from.", J.S(state, "region_label").NullIfEmpty() ?? "Not chosen", RowActionAsync("Change", ChooseGuardianRegion)),
                J.S(state, "verified_utc").Length > 0 ? InfoRow("Last verified", J.S(state, "verified_utc"), "The last time the VPN tunnel confirmed it was working.") : null);
            Group("Set up in order",
                SettingRow("1. Register this computer", "Uses one of your plan's VPN device slots.", registered ? "✓ " + (J.S(state, "nickname").NullIfEmpty() ?? "Registered") : "Not registered", RowActionAsync(registered ? "Register again" : "Register", RegisterGuardian)),
                SettingRow("2. Install WireGuard", "WireGuard tools create the VPN tunnel, through NetworkManager or wg-quick. Install them with your distribution's package manager.", installed ? "✓ Installed" : preview ? "Sample" : "Not installed", install),
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
            Edit("Register this VPN device", new[] { new Field("nickname", "Device name", Environment.MachineName), new Field("terms_link", "Read Guardian terms", "https://guardianapp.com/terms-and-conditions/", "link"), new Field("agree", "I agree to Guardian's terms and privacy policy", false, "bool") }, async values =>
            {
                if (preview) throw new InvalidOperationException("Sample mode does not register VPN devices.");
                if (!Flag(values, "agree")) throw new ArgumentException("Review and accept Guardian's terms before registering.");
                string nickname = V(values, "nickname").Trim(); ValidateName(nickname);
                object state = VpnState();
                if (J.S(state, "pet").Length > 0) throw new InvalidOperationException("This network already has a saved registration on this computer. Choose a location or use Manage VPN devices.");
                if (!await Confirm("Use an available Guardian VPN device slot for " + nickname + " on this network?")) return;
                object result = await api.Send("POST", "/2.2/partners/guardian/networks/" + NetworkId + "/pet", J.Obj("nickname", nickname));
                string pet = J.S(result, "guardian_pet"); if (pet.Length == 0) throw new InvalidDataException("eero did not return a VPN device token. Refresh Manage VPN devices before registering again.");
                try { VpnStore.Save(J.Stringify(J.Obj("pet", pet, "uuid", J.S(result, "uuid"), "nickname", nickname, "expiration", J.S(result, "expiration")))); }
                catch (Exception) { throw new IOException("eero registered the device, but this computer could not save it. Check Manage VPN devices and remove the registration before trying again."); }
                SetNotice("Device registered. Install WireGuard if needed, then choose a VPN location."); RenderPage();
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
                if (LinuxVpn.Running(VpnScope)) throw new InvalidOperationException("Disconnect VPN before changing its location.");
                if (!LinuxVpn.Installed) throw new InvalidOperationException("Install WireGuard before choosing a location. " + LinuxVpn.MissingComponents);
                var state = VpnState() as Dictionary<string, object>; string pet = J.S(state, "pet"); if (pet.Length == 0) throw new InvalidOperationException("Register this device first.");
                string selected = V(values, "region"); string[] keys = await Task.Run(() => LinuxVpn.KeyPair());
                try { using (var guardian = new GuardianApi()) state["profile"] = await guardian.CreateProfile(pet, choices[selected], keys[0], keys[1]); }
                finally { keys[0] = keys[1] = null; }
                state["region"] = choices[selected]; state["region_label"] = selected; state.Remove("verified_utc"); VpnStore.Save(J.Stringify(state)); RenderPage(); SetNotice("VPN location saved. Choose Connect VPN to establish the tunnel.");
            }, "Use location");
        }
        private async Task ConnectGuardian()
        {
            if (preview) throw new InvalidOperationException("Sample mode does not connect a VPN.");
            var state = VpnState() as Dictionary<string, object>; object profile = J.Get(state, "profile");
            if (profile == null) throw new InvalidOperationException("Register this device and choose a location first.");
            if (!await Confirm("Connect this computer to Guardian through " + J.S(state, "region_label") + "? Your system may ask for administrator permission to start the tunnel. Full-tunnel protection can block local network access while connected.")) return;
            SetNotice("Connecting and verifying the WireGuard handshake…");
            object result = await LinuxVpn.Control("connect", VpnScope, profile);
            state["verified_utc"] = J.S(result, "verified_utc"); VpnStore.Save(J.Stringify(state)); RenderPage(); SetNotice(J.S(result, "message"));
        }
        private async Task DisconnectGuardian()
        {
            if (preview) throw new InvalidOperationException("Sample mode does not change a VPN.");
            if (!LinuxVpn.Running(VpnScope)) { SetNotice("This network's VPN is already disconnected."); return; }
            object result = await LinuxVpn.Control("disconnect", VpnScope); RenderPage(); SetNotice(J.S(result, "message"));
        }
        private async Task GuardianDevices()
        {
            if (preview) { Info("VPN devices", J.Obj("device", "Sample computer", "status", "Registered")); return; }
            object credentials = await api.Send("POST", "/2.2/partners/guardian"); object devices;
            using (var guardian = new GuardianApi()) devices = await guardian.Devices(credentials);
            var panel = VBox();
            panel.Append(Text("Devices using your Guardian subscription", 23, "#17263C", true));
            var window = Dialog("VPN devices", panel); int shown = 0;
            foreach (object raw in J.List(devices))
            {
                object device = raw; string id = J.S(device, "ep-grd-device-uuid"); if (id.Length == 0) continue;
                var item = VBox(); string name = J.S(device, "ep-grd-device-nickname").NullIfEmpty() ?? "VPN device";
                item.Append(Text(name, 19, "#17263C", true)); item.Append(Text("Activated: " + J.S(device, "ep-grd-device-created-at"), 13, "#64738A"));
                var remove = Button("Remove this VPN device", async delegate
                {
                    if (!await Confirm("Remove " + name + " from your Guardian subscription? Its VPN access will stop.")) return;
                    object state = VpnState(); bool local = J.S(state, "uuid") == id;
                    if (local && LinuxVpn.Running(VpnScope)) throw new InvalidOperationException("Disconnect this computer's VPN before removing its registration.");
                    using (var guardian = new GuardianApi()) await guardian.RemoveDevice(credentials, id);
                    if (local) VpnStore.Clear(); window.Close(); SetNotice("VPN device removed."); RenderPage();
                });
                remove.SetHalign(Gtk.Align.Start); remove.SetMarginTop(8); item.Append(remove);
                panel.Append(Card(item)); shown++;
            }
            if (shown == 0) panel.Append(Text("No devices were returned.", 14));
            window.Dialog.OnClosed += delegate { credentials = null; }; window.ShowDialog();
        }
    }
}
