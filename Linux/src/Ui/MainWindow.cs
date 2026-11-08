using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Gtk;

namespace EeroDesktop
{
    // The app window. Same responsibilities and page vocabulary as the Windows MainWindow,
    // built from GTK 4 + libadwaita widgets.
    public sealed partial class MainWindow
    {
        private readonly Adw.Application application;
        internal readonly Adw.ApplicationWindow Window;
        private EeroApi api;
        private Widget root;
        private Box page, navigation;
        private Label notice, accountLabel;
        private DropDown networks;
        private Widget controls;
        private bool preview, busy, loading;
        private string currentPage = "Home", networkUrl;
        private object account, network;
        private List<object> networkList = new List<object>();
        private List<object> devices = new List<object>(), nodes = new List<object>(), profiles = new List<object>();
        private DateTime lastRefresh;
        private DateTime resendAfter;
        private readonly UiTimer timer;
        private readonly Dictionary<string, string> loadErrors = new Dictionary<string, string>();
        private Entry identifierBox, codeBox;
        private CheckButton rememberBox;
        private bool formatPhone = true;
        private readonly string statusPath;
        private readonly List<Adw.Dialog> openDialogs = new List<Adw.Dialog>();
        private readonly List<Gtk.Window> ownedWindows = new List<Gtk.Window>();

        public MainWindow(Adw.Application app, bool isPreview, string diagnosticsPath = null, bool startInBackground = false, bool startAmazonNativeSignIn = false)
        {
            application = app;
            statusPath = diagnosticsPath;
            preview = isPreview; backgroundLaunch = startInBackground && !isPreview;
            Window = Adw.ApplicationWindow.New(app);
            Window.SetTitle("eero Desktop · Unofficial · v1.1");
            Window.SetIconName(Program.AppId);
            Window.SetDefaultSize(1360, 880);
            Window.SetSizeRequest(960, 640);
            api = new EeroApi(new SessionStore(System.IO.Path.Combine(DataDirectory, "session.dat")));
            timer = new UiTimer(TimeSpan.FromSeconds(60), async delegate { if (!busy && !preview && api.SignedIn && Window.IsActive && (currentPage == "Home" || currentPage == "Devices")) await Run(async delegate { await LoadNetwork(); RenderPage(); }, "Refreshing network…"); });
            Window.OnCloseRequest += delegate { return OnCloseRequested(); };
            Window.OnDestroy += delegate { timer.Stop(); api.Dispose(); };
            Adw.StyleManager.GetDefault().OnNotify += (sender, args) => { if (args.Pspec.GetName() == "dark") ThemeChanged(); };
            HookShellInput();
            ConfigureDesktopNotifications();
            ReportStatus("ready");
            if (preview) { SampleData(); ShowShell(); }
            else
            {
                ShowLogin(false);
                GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, delegate { Startup(startAmazonNativeSignIn); return false; });
            }
        }
        // Application data lives under XDG_DATA_HOME (~/.local/share/eero-desktop), like %LOCALAPPDATA% on Windows.
        internal static string DataDirectory
        {
            get
            {
                string home = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                if (String.IsNullOrEmpty(home) || !System.IO.Path.IsPathRooted(home)) home = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
                return System.IO.Path.Combine(home, "eero-desktop");
            }
        }
        private async void Startup(bool startAmazonNativeSignIn)
        {
            if (startAmazonNativeSignIn) { AmazonConsumerSignIn(); return; }
            if (api.Restore()) await Run(async delegate { await LoadAccount(); ShowShell(); ReportStatus("dashboard_loaded"); }, "Restoring your session…");
            if (!api.SignedIn && backgroundLaunch) SignInAttention();
            if (api.SignedIn) await PollNotifications();
        }
        public void Show()
        {
            if (backgroundLaunch) { application.Hold(); heldInBackground = true; return; }
            Window.Present();
        }
        private void ThemeChanged()
        {
            // libadwaita recolors every widget itself; only custom-drawn charts need a redraw.
            if (page != null && !loading) RenderPage();
        }
        private void ReportStatus(string state, Exception error = null)
        {
            if (String.IsNullOrEmpty(statusPath)) return;
            try
            {
                var apiError = error as ApiException;
                File.WriteAllText(statusPath, J.Stringify(J.Obj("state", state, "time_utc", DateTime.UtcNow.ToString("o"), "error_type", error == null ? "" : error.GetType().Name, "transport_category", error == null || apiError != null ? "" : ConnectionDiagnostics.Category(error), "native_error", error == null ? 0 : ConnectionDiagnostics.NativeCode(error), "http_code", apiError == null ? 0 : apiError.Code, "api_error", apiError == null ? "" : apiError.ApiError, "network_count", networkList.Count, "device_count", devices.Count, "node_count", nodes.Count, "profile_count", profiles.Count, "section_errors", loadErrors.Keys.ToArray())));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        private void SetNotice(string message, bool error = false)
        {
            if (notice == null) return;
            notice.SetText(message ?? "");
            notice.RemoveCssClass("c-danger"); notice.RemoveCssClass("c-muted");
            notice.AddCssClass(error ? "c-danger" : "c-muted");
        }
        public static string Message(Exception e)
        {
            if (e is HttpRequestException || e is TaskCanceledException) return ConnectionDiagnostics.UserMessage(e);
            if (e is UnauthorizedAccessException || e is System.Security.Cryptography.CryptographicException) return "Your desktop keyring could not unlock your saved session. Unlock the keyring, or sign in without Remember me.";
            return e.Message;
        }
        private async Task Run(Func<Task> action, string status)
        {
            if (busy) return;
            busy = true;
            SetNotice(status);
            if (controls != null) controls.SetSensitive(false);
            try { await action(); }
            catch (ApiException ex)
            {
                ReportStatus("error", ex);
                if (ex.SessionExpired)
                {
                    api.Forget(); timer.Stop(); ClearData(); ShowLogin(false);
                    SetNotice("Your session expired. Sign in again to continue.", true);
                }
                else SetNotice(Message(ex), true);
            }
            catch (Exception ex) { ReportStatus("error", ex); SetNotice(Message(ex), true); }
            finally { busy = false; if (controls != null) controls.SetSensitive(true); }
        }
        private void ClearData()
        { notificationEpoch++; CloseAccountWindows(); history.Clear(); account = network = selectedProfile = selectedNode = null; networkUrl = null; currentPage = "Home"; networkList.Clear(); devices.Clear(); nodes.Clear(); profiles.Clear(); loadErrors.Clear(); featureData.Clear(); bluetoothResults.Clear(); wiredSetup = new WiredNetworkSetup(); }

        private void CloseAccountWindows()
        {
            foreach (Adw.Dialog d in openDialogs.ToArray()) d.ForceClose();
            openDialogs.Clear();
            foreach (Gtk.Window w in ownedWindows.ToArray()) w.Destroy();
            ownedWindows.Clear();
        }
        internal void Own(Gtk.Window child)
        {
            child.SetTransientFor(Window); ownedWindows.Add(child);
            child.OnDestroy += delegate { ownedWindows.Remove(child); };
        }

        private void ShowLogin(bool verify)
        {
            var grid = Columns(0.38, 0.62); grid.SetColumnSpacing(0); grid.SetVexpand(true);
            root = grid; Window.SetContent(WithHeader(grid));
            var intro = VBox(); intro.Margin(48, 48, 48, 48); intro.SetValign(Gtk.Align.Center);
            var logo = Text("eero", 66); logo.AddCssClass("brand"); intro.Append(logo);
            intro.Append(Text("Desktop for Linux", 13, "#64738A"));
            intro.Append(Spacer(42));
            var wifi = Glyph("", 76, "#274CDB"); wifi.SetHalign(Gtk.Align.Start); wifi.SetMarginBottom(36); intro.Append(wifi);
            intro.Append(Text("Your wifi.\nFrom your desktop.", 30, "#17263C", true));
            intro.Append(Text("A clear view of your network, devices, and profiles.", 15, "#64738A"));
            intro.Append(Text("Independent desktop client\nNot affiliated with Amazon or eero.", 11, "#64738A"));
            var left = VBox(); left.AddCssClass("bg-rail"); left.SetVexpand(true); left.Append(intro);
            PlaceInColumn(grid, left, 0);
            var form = VBox(); form.SetValign(Gtk.Align.Center); form.Margin(36, 36, 36, 36);
            var clamp = Adw.Clamp.New(); clamp.SetMaximumSize(410); clamp.SetChild(form);
            var formScroll = ScrolledWindow.New(); formScroll.SetPolicy(PolicyType.Never, PolicyType.Automatic); formScroll.SetChild(clamp); formScroll.SetVexpand(true);
            PlaceInColumn(grid, formScroll, 1); controls = form;
            form.Append(Text(verify ? "Check your messages" : "Welcome home", 32, "#17263C", true));
            form.Append(Text(verify ? "Enter the verification code sent to your eero account. Keep this window open while you get it." : "Sign in with the email address or phone number linked to your eero account.", 15, "#64738A"));
            form.Append(Spacer(18));
            if (!verify)
            {
                form.Append(Text("Sign in using", 13, "#17263C", true));
                var method = DropDown.NewFromStrings(new[] { "Phone number", "Email address" }); method.SetSelected(formatPhone ? 0u : 1u); method.SetMarginBottom(16); method.Name("Sign in using"); form.Append(method);
                var inputLabel = Text(formatPhone ? "Phone number" : "Email address", 13, "#17263C", true); form.Append(inputLabel);
                identifierBox = Entry.New(); identifierBox.SetMaxLength(254); identifierBox.Name("Email or phone number");
                form.Append(identifierBox);
                var phoneHint = Text(formatPhone ? "10 digits automatically use +1 (US/Canada). For other countries, include + and the country code." : "Use the email address linked to your eero account.", 12, "#64738A"); phoneHint.SetMarginTop(6); form.Append(phoneHint);
                bool formatting = false;
                System.Action formatNumber = delegate
                {
                    if (!formatPhone || formatting) return;
                    string before = identifierBox.GetText();
                    string after = PhoneInput.FormatForDisplay(before);
                    if (before == after) return;
                    int caret = PhoneInput.PreserveCaret(before, identifierBox.GetPosition(), after);
                    formatting = true; identifierBox.SetText(after); identifierBox.SetPosition(caret); formatting = false;
                };
                identifierBox.OnChanged += delegate { GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT, delegate { formatNumber(); return false; }); };
                method.OnNotify += (sender, args) =>
                {
                    if (args.Pspec.GetName() != "selected") return;
                    formatPhone = method.GetSelected() == 0;
                    inputLabel.SetText(formatPhone ? "Phone number" : "Email address");
                    phoneHint.SetText(formatPhone ? "10 digits automatically use +1 (US/Canada). For other countries, include + and the country code." : "Use the email address linked to your eero account.");
                    formatNumber();
                };
                rememberBox = CheckButton.NewWithLabel("Remember me on this computer"); rememberBox.SetActive(true); rememberBox.SetMarginBottom(6);
                form.Append(rememberBox);
                var send = Button("Send verification code", async delegate
                {
                    string id = identifierBox.GetText().Trim();
                    if (formatPhone) { id = PhoneInput.Normalize(id); formatNumber(); }
                    else if (!Regex.IsMatch(id, @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) throw new ArgumentException("Enter the email address linked to your eero account.");
                    api.Remember = rememberBox.GetActive();
                    CloseAccountWindows();
                    await api.BeginLogin(id);
                    resendAfter = DateTime.UtcNow.AddSeconds(60);
                    ShowLogin(true); ReportStatus("code_requested"); SetNotice("Code requested. Check your email or text messages.");
                }, true);
                send.SetHexpand(true); send.Margin(0, 6, 0, 20); form.Append(send);
                identifierBox.OnActivate += delegate { if (!busy) send.Activate(); };
                form.Append(Text("When Remember me is on, your session is encrypted with a key kept in your desktop keyring.", 12, "#64738A"));
            }
            else
            {
                form.Append(Text("Verification code", 13, "#17263C", true));
                codeBox = Entry.New(); codeBox.SetMaxLength(12); codeBox.AddCssClass("fs-28"); codeBox.SetInputPurpose(InputPurpose.Digits); codeBox.Name("Verification code"); codeBox.SetMarginBottom(16);
                form.Append(codeBox);
                var confirm = Button("Verify and sign in", async delegate
                {
                    string code = codeBox.GetText().Trim();
                    if (!Regex.IsMatch(code, @"^[0-9]{4,10}$")) throw new ArgumentException("Enter the numeric code from your email or text message.");
                    if (api.Registering) await api.VerifyRegistration(code, registrationCountry, registrationConsents);
                    else await api.Verify(code);
                    ReportStatus("authenticated"); codeBox.SetText(""); ShowShell(); await LoadAccount(); ShowShell(); ReportStatus("dashboard_loaded");
                }, true);
                confirm.SetHexpand(true); confirm.SetMarginBottom(8); form.Append(confirm);
                codeBox.OnActivate += delegate { if (!busy) confirm.Activate(); };
                var row = WrapPanel();
                row.Add(Button("Resend code", async delegate
                {
                    int wait = (int)Math.Ceiling((resendAfter - DateTime.UtcNow).TotalSeconds);
                    if (wait > 0) throw new InvalidOperationException("You can request another code in " + wait + " seconds.");
                    await api.Resend(); resendAfter = DateTime.UtcNow.AddSeconds(60); SetNotice("Another code has been requested.");
                }));
                row.Add(LocalButton("Use a different account", delegate { api.CancelLogin(); ShowLogin(false); })); form.Append(row);
            }
            notice = Text("", 13, "#52647C"); notice.SetMarginTop(10); form.Append(notice);
            if (!verify)
            {
                var otherHead = Text("Other ways to sign in", 13, "#17263C", true); otherHead.Margin(0, 14, 0, 8); form.Append(otherHead);
                var options = ListBox.New(); options.AddCssClass("boxed-list"); options.SetSelectionMode(SelectionMode.None); options.SetMarginBottom(16);
                options.Append(LoginOption("Amazon", "Use the Amazon login linked to your eero account.", "", delegate { AmazonConsumerSignIn(); return Task.FromResult(0); }));
                options.Append(LoginOption("eero's website", "Sign in on eero.com in a secure window.", "", delegate { WebsiteSignIn(); return Task.FromResult(0); }));
                options.Append(LoginOption("Your internet provider", "If your provider gave you your eeros.", "", ProviderSignIn));
                form.Append(options);
            }
            var links = WrapPanel(0); links.SetMarginBottom(4);
            if (!verify) { var create = LocalButton("New to eero? Create an account", BeginRegistrationUi); Quiet(create); links.Add(create); }
            var check = Button("Trouble connecting? Check connection", async delegate
            {
                await api.CheckConnection();
                SetNotice("Connected to eero securely. You can request your verification code.");
                ReportStatus("connection_ok");
            }); Quiet(check); links.Add(check);
            form.Append(links);
            if (!preview && LinuxVpn.OwnActiveScopes().Length > 0) form.Append(Button("Disconnect active VPN", async delegate
            { foreach (string scope in LinuxVpn.OwnActiveScopes()) await LinuxVpn.Control("disconnect", scope); SetNotice("VPN disconnected."); }));
            form.Append(Text("Uses eero’s cloud service. An internet connection is required.", 12, "#64738A"));
        }
        // One sign-in alternative as a row: icon, name, short explanation and a chevron.
        private Adw.ActionRow LoginOption(string title, string description, string glyph, Func<Task> action)
        {
            var row = Adw.ActionRow.New(); row.SetUseMarkup(false); row.SetTitle(title); row.SetSubtitle(description); row.SetActivatable(true);
            row.AddPrefix(Glyph(glyph, 18, "#274CDB")); row.AddSuffix(Glyph("", 12));
            row.OnActivated += async delegate { await Run(action, "Working…"); };
            row.Name("Sign in with " + title);
            return row;
        }

        private async Task LoadAccount()
        {
            account = await api.Send("GET", "account");
            networkList = J.List(J.Get(account, "networks"));
            if (networkList.Count == 0) networkList = J.List(await api.Send("GET", "networks"));
            networkUrl = networkList.Count > 0 ? J.S(networkList[0], "url") : null;
            if (!String.IsNullOrEmpty(networkUrl)) await LoadNetwork();
        }
        private string Resource(object o, string key, string fallback)
        {
            object r = J.Get(o, "resources", key);
            string s = r is string ? (string)r : J.S(r, "url");
            return String.IsNullOrEmpty(s) ? fallback : s;
        }
        private async Task LoadNetwork()
        {
            if (String.IsNullOrEmpty(networkUrl)) return;
            loadErrors.Clear();
            network = await api.Send("GET", networkUrl);
            devices = await LoadList("Devices", Resource(network, "devices", networkUrl + "/devices"));
            nodes = await LoadList("eero nodes", Resource(network, "eeros", networkUrl + "/eeros"));
            profiles = await LoadList("Profiles", Resource(network, "profiles", networkUrl + "/profiles"));
            lastRefresh = DateTime.Now;
        }
        private async Task<List<object>> LoadList(string name, string url)
        {
            try { return J.List(await api.Send("GET", url)); }
            catch (ApiException ex) { if (ex.SessionExpired) throw; loadErrors[name] = Message(ex); return new List<object>(); }
            catch (HttpRequestException ex) { loadErrors[name] = Message(ex); return new List<object>(); }
            catch (TaskCanceledException ex) { loadErrors[name] = Message(ex); return new List<object>(); }
        }
        private void ShowShell() { NativeShell(); }
        public void PreviewPage(string name)
        {
            if (name == "Login") { ShowLogin(false); return; }
            if (name == "Home/Profiles/Profile") selectedProfile = J.Obj("name", "Family", "url", "/2.2/networks/preview/profiles/1", "paused", false, "devices", devices.Take(2).ToArray(), "schedule", new[] { J.Obj("name", "Bedtime", "enabled", true, "days", new[] { "MON", "TUE", "WED", "THU", "FRI" }, "start", "22:00", "end", "07:00") }, "premium_dns", J.Obj("dns_policies", J.Obj("block_malware", true, "safe_search_enabled", true)));
            if (name == "Home/eeros/Details") selectedNode = nodes.First();
            currentPage = name; featureData[name] = PreviewFeature(name); RenderPage();
        }
        private void RenderPage()
        {
            if (page == null) return;
            loading = true; page.Clear();
            UpdateNavigation();
            PageHeading();
            if (network == null && !AccountOnlyPage(currentPage))
            {
                // No network on this account yet: explain, then offer every way to get one.
                Group(null, InfoRow("No network yet", null, "Your account doesn't have an eero network the app can open. If you just set one up, choose Refresh at the top right."));
                AddGroupHeading(page, "Get started");
                page.Append(RowGroup(new[] { "Settings/User permissions/Join a network", "Settings/User permissions/Incoming access invitations", "Settings/Network management/Pending transfers", "Home/Add or replace eero/Create a network" }
                    .Select(path => FeatureCatalog.Entries.FirstOrDefault(e => e.Path == path)).Where(e => e != null).Select(e => (Widget)ListRow(e, false))));
                loading = false; return;
            }
            if (currentPage == "Home" || currentPage == "Overview") Overview();
            else if (currentPage == "Devices") DevicePage();
            else if (currentPage == "eero nodes") NodePage();
            else if (currentPage == "Profiles") ProfilesPageV2();
            else if (currentPage == "Activity") ActivityMenu();
            else if (currentPage == "Settings") FeatureMenu("Settings");
            else if (currentPage == "Help") HelpPage();
            else RenderFeature();
            // Standalone action buttons size to their label instead of spanning the page.
            foreach (Widget child in page.ChildWidgets()) if (child is Gtk.Button) child.SetHalign(Gtk.Align.Start);
            MarkDangerActions(page);
            SetNotice(preview ? "Preview mode · No network requests or changes" : "Updated " + lastRefresh.ToString("h:mm:ss tt") + " · Refreshes every minute while active");
            loading = false;
        }
        private void Sub(string text) { if (!String.Equals(text, currentPage.Split('/').Last(), StringComparison.OrdinalIgnoreCase)) page.Append(Text(text, 14, "#64738A")); page.Append(Spacer(10)); }
        private void Availability(string key)
        { if (loadErrors.ContainsKey(key)) page.Append(Card(Text("Could not load " + key.ToLowerInvariant() + ": " + loadErrors[key], 14, "#B5333D"))); }
        private void Overview() { NativeOverview(); }
        private string Speed(string direction)
        {
            string value = J.S(network, "speed", direction, "value");
            return value.Length == 0 ? "—" : value + " " + (J.S(network, "speed", direction, "units").NullIfEmpty() ?? "Mbps");
        }
        public sealed class NetworkChoice { public string Name { get; set; } public string Url { get; set; } }
        public sealed class DeviceRow
        {
            public object Raw { get; set; } public string Name { get; set; } public string Status { get; set; } public string IP { get; set; } public string Connection { get; set; } public string Profile { get; set; }
        }
        private static DeviceRow Device(object d)
        {
            return new DeviceRow { Raw = d, Name = J.First(d, "nickname", "display_name", "hostname", "mac"), Status = J.B(d, "blocked") ? "Blocked" : J.B(d, "paused") ? "Paused" : J.B(d, "connected") ? "Online" : "Offline", IP = J.First(d, "ip", "ip_address"), Connection = J.B(d, "wireless") ? (J.S(d, "frequency").NullIfEmpty() ?? "Wifi") : "Ethernet", Profile = J.S(d, "profile", "name") };
        }
        private void DevicePage() { NativeDevicePage(); }

        // A dialog presented over the main window (libadwaita sheet on narrow screens).
        internal sealed class DialogWindow
        {
            public readonly Adw.Dialog Dialog; public readonly Box Body; private readonly MainWindow owner;
            private readonly TaskCompletionSource<bool> closed = new TaskCompletionSource<bool>();
            public bool Result;
            public DialogWindow(MainWindow owner, string title, Box body, int width = 620, int height = 700)
            {
                this.owner = owner; Body = body;
                Dialog = Adw.Dialog.New(); Dialog.SetTitle(title); Dialog.SetContentWidth(width); Dialog.SetContentHeight(height);
                body.Margin(28, 12, 28, 28);
                var scroll = ScrolledWindow.New(); scroll.SetPolicy(PolicyType.Never, PolicyType.Automatic); scroll.SetChild(body); scroll.SetVexpand(true); scroll.SetPropagateNaturalHeight(true);
                var view = Adw.ToolbarView.New(); view.AddTopBar(Adw.HeaderBar.New()); view.SetContent(scroll);
                Dialog.SetChild(view);
                Dialog.OnClosed += delegate { owner.openDialogs.Remove(Dialog); closed.TrySetResult(Result); };
            }
            public void SizeToContent(int minHeight = 240, int maxHeight = 780) { Dialog.SetContentHeight(-1); Dialog.SetFollowsContentSize(false); Dialog.SetContentHeight(Math.Max(minHeight, Math.Min(maxHeight, 560))); }
            public Task<bool> ShowDialog() { owner.openDialogs.Add(Dialog); Dialog.Present(owner.Window); return closed.Task; }
            public void Close() { Dialog.ForceClose(); }
            public void Close(bool result) { Result = result; Dialog.ForceClose(); }
        }
        private DialogWindow Dialog(string title, Box body) { return new DialogWindow(this, title, body); }
        private void Detail(Box p, string name, string value)
        { p.Append(Text(name, 12, "#64738A")); p.Append(Text(String.IsNullOrEmpty(value) ? "Unavailable" : value, 15)); }
        private async Task Mutate(string method, string url, object body, string success)
        {
            if (preview) throw new InvalidOperationException("Preview mode is read only. Sign in to manage your network.");
            await api.Send(method, url, body); await LoadNetwork(); RenderPage(); SetNotice(success);
        }
        private void DeviceDialog(object d)
        {
            DeviceRow row = Device(d); var p = VBox(); p.Append(Text(row.Name, 25, "#17263C", true));
            p.Append(StatusBadge(row.Status, row.Status == "Online")); p.Append(Spacer(14));
            var w = Dialog(row.Name, p);
            foreach (Widget part in DevicePanel(d, delegate { w.Close(); })) p.Append(part);
            w.ShowDialog();
        }
        private void NodePage()
        {
            Sub("The eeros that make up your wifi. Open one to see its details, rename it or restart it."); Availability("eero nodes");
            foreach (object n in nodes) page.Append(NodeCard(n));
            if (nodes.Count == 0 && !loadErrors.ContainsKey("eero nodes")) page.Append(Card(Text("eero didn't return any eeros for this network. Choose Refresh at the top right to try again.")));
            RelatedPagesFor("Home/eeros/Placement tests", "Home/Add or replace eero");
        }
        private Widget NodeCard(object n) { return NativeNodeCard(n); }
        private async Task StartSpeedTest()
        {
            if (preview) throw new InvalidOperationException("Preview mode is read only.");
            await api.Send("POST", Resource(network, "speedtest", networkUrl + "/speedtest"));
            SetNotice("Speed test started. Results can take a few minutes. Use Refresh results on the Activity page.");
        }
        // Asks before a change; GTK dialogs are asynchronous, so callers await the answer.
        private Task<bool> Confirm(string message)
        {
            var done = new TaskCompletionSource<bool>();
            var alert = Adw.AlertDialog.New("Confirm change", message);
            alert.AddResponse("cancel", "Cancel"); alert.AddResponse("continue", "Continue");
            alert.SetCloseResponse("cancel"); alert.SetDefaultResponse("cancel");
            if (DangerWords.Any(w => message.StartsWith(w.Trim(), StringComparison.OrdinalIgnoreCase))) alert.SetResponseAppearance("continue", Adw.ResponseAppearance.Destructive);
            alert.OnResponse += (sender, args) => { done.TrySetResult(args.Response == "continue"); };
            alert.Present(Window);
            return done.Task;
        }
        internal void ShowApplicationError(string message) { Info("eero Desktop", J.Obj("message", message)); }
        private Task<string> Prompt(string title, string value)
        {
            var done = new TaskCompletionSource<string>();
            var alert = Adw.AlertDialog.New(title, null);
            var input = Entry.New(); input.SetText(value ?? ""); input.SetMaxLength(100); input.SetActivatesDefault(true); input.Name(title);
            alert.SetExtraChild(input);
            alert.AddResponse("cancel", "Cancel"); alert.AddResponse("save", "Save");
            alert.SetResponseAppearance("save", Adw.ResponseAppearance.Suggested);
            alert.SetDefaultResponse("save"); alert.SetCloseResponse("cancel");
            alert.OnResponse += (sender, args) => { done.TrySetResult(args.Response == "save" ? input.GetText() : null); };
            alert.Present(Window);
            return done.Task;
        }
        internal static void ValidateName(string s) { if (String.IsNullOrWhiteSpace(s) || s.Trim().Length > 100) throw new ArgumentException("Enter a name between 1 and 100 characters."); }
        internal static void ValidateSsid(string s) { if (String.IsNullOrWhiteSpace(s) || Encoding.UTF8.GetByteCount(s.Trim()) > 32) throw new ArgumentException("Wifi names must contain between 1 and 32 UTF-8 bytes."); }
        internal static void ValidatePassword(string s) { if (s.Length < 8 || s.Length > 63 || s.Any(c => c < 32 || c > 126)) throw new ArgumentException("Use a wifi password with 8–63 printable ASCII characters."); }
        private void SampleData()
        {
            account = J.Parse("{\"name\":\"Preview account\"}"); networkUrl = "/2.2/networks/preview";
            networkList = J.List(J.Parse("[{\"name\":\"Maple House\",\"url\":\"/2.2/networks/preview\"}]"));
            network = J.Parse("{\"name\":\"Maple House\",\"health\":{\"internet\":{\"status\":\"connected\"}},\"speed\":{\"down\":{\"value\":942,\"units\":\"Mbps\"},\"up\":{\"value\":118,\"units\":\"Mbps\"}},\"wpa3\":true,\"band_steering\":true,\"sqm\":true,\"upnp\":false,\"ipv6\":true}");
            nodes = J.List(J.Parse("[{\"location\":\"Living room\",\"model\":\"eero Pro 6E\",\"gateway\":true,\"status\":\"green\",\"os_version\":\"Preview\",\"ip_address\":\"192.168.4.1\",\"connected_clients_count\":3},{\"location\":\"Office\",\"model\":\"eero Pro 6E\",\"gateway\":false,\"status\":\"green\",\"os_version\":\"Preview\",\"ip_address\":\"192.168.4.2\",\"connected_clients_count\":2}]"));
            devices = J.List(J.Parse("[{\"nickname\":\"Office PC\",\"connected\":true,\"wireless\":false,\"ip\":\"192.168.4.20\",\"manufacturer\":\"Microsoft\",\"mac\":\"02:00:00:00:00:01\"},{\"nickname\":\"Living room TV\",\"connected\":true,\"wireless\":true,\"frequency\":\"5 GHz\",\"ip\":\"192.168.4.34\",\"mac\":\"02:00:00:00:00:02\"},{\"nickname\":\"Kitchen speaker\",\"connected\":true,\"wireless\":true,\"frequency\":\"2.4 GHz\",\"ip\":\"192.168.4.45\",\"mac\":\"02:00:00:00:00:03\"},{\"nickname\":\"Laptop\",\"connected\":true,\"wireless\":true,\"frequency\":\"6 GHz\",\"ip\":\"192.168.4.52\",\"mac\":\"02:00:00:00:00:04\"},{\"nickname\":\"Tablet\",\"connected\":false,\"wireless\":true,\"ip\":\"192.168.4.63\",\"mac\":\"02:00:00:00:00:05\"} ]"));
            profiles = J.List(J.Parse("[{\"name\":\"Family\",\"paused\":false},{\"name\":\"Kids\",\"paused\":true}]")); lastRefresh = DateTime.Now;
            var nmap = (IDictionary<string, object>)network;
            nmap["ipv6_upstream"] = true; nmap["dns"] = J.Obj("mode", "automatic", "caching", true, "custom", J.Obj("ips", new[] { "1.1.1.1", "8.8.8.8" }));
            nmap["connection"] = J.Obj("mode", "nat"); nmap["timezone"] = J.Obj("value", "America/Chicago");
            nmap["auto_setup_mode"] = "DEFAULT"; nmap["accessories"] = new[] { J.Obj("name", "Sample accessory", "model", "Sample cellular accessory", "dsn", "SAMPLEACCESSORY1", "registered", true, "joined", true, "eero_id", "1") };
            nmap["amazon_account_linked"] = true; nmap["smart_home_hub"] = true;
            nmap["lease"] = J.Obj("mode", "dhcp"); nmap["pppoe_enabled"] = false; nmap["nat_port_randomization"] = true;
            nmap["power_saving"] = false; nmap["power_saving_schedule_enabled"] = false; nmap["thread"] = true;
            nmap["dynamic_dns"] = J.Obj("enabled", false, "hostname", "sample-network.example");
            // Developer switch for checking availability badges in preview renders.
            if (Environment.GetEnvironmentVariable("EERO_DESKTOP_SAMPLE_NO_PLAN") == "1") nmap["premium_status"] = "inactive";
            for (int i = 0; i < devices.Count; i++) ((IDictionary<string, object>)devices[i])["url"] = "/2.2/networks/preview/devices/" + i;
            for (int i = 0; i < nodes.Count; i++) { var node = (IDictionary<string, object>)nodes[i]; node["url"] = "/2.2/eeros/" + (i + 1); node["serial"] = "PREVIEWNODE000" + (i + 1); node["led_on"] = true; node["led_brightness"] = 100; node["resources"] = J.Obj("led_action", "/2.2/eeros/" + (i + 1) + "/led", "connections", "/2.2/eeros/" + (i + 1) + "/connections"); }
            for (int i = 0; i < profiles.Count; i++) ((IDictionary<string, object>)profiles[i])["url"] = "/2.2/networks/preview/profiles/" + (i + 1);
        }
    }
}
