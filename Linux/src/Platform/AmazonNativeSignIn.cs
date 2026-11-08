using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Gtk;

namespace EeroDesktop
{
    // Amazon consumer sign-in in a private WebKitGTK window (same flow as the Windows WebView2 window).
    internal sealed class AmazonNativeWindow
    {
        public readonly Adw.Window Window;
        private readonly AmazonNativeAttempt attempt = new AmazonNativeAttempt();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly WebKit.WebView browser;
        private readonly Label notice, address;
        private readonly Func<string, CancellationToken, Task> complete;
        private readonly Action<string, object> report;
        private bool completing, closed, failed;
        internal AmazonNativeWindow(MainWindow owner, Func<string, CancellationToken, Task> complete, Action<string, object> report)
        {
            this.complete = complete; this.report = report;
            Window = Adw.Window.New();
            Window.SetTitle("Sign in with Amazon · eero Desktop"); Window.SetDefaultSize(1080, 820); Window.SetSizeRequest(850, 650);
            Window.SetIconName(Program.AppId);
            if (Program.App != null) Window.SetApplication(Program.App);
            if (owner != null) owner.Own(Window);
            var root = MainWindow.VBox(); root.Margin(16, 8, 16, 16);
            var top = MainWindow.VBox(); top.Append(MainWindow.Text("Sign in with Amazon", 24, "#17263C", true));
            var help = MainWindow.Text("Sign in with the Amazon account linked to your eero account. This window closes after eero verifies your sign-in.", 14, "#64738A"); help.Margin(0, 8, 0, 8); top.Append(help);
            address = MainWindow.Text("www.amazon.com", 13, "#64738A"); address.SetMarginBottom(12); top.Append(address); root.Append(top);
            notice = MainWindow.Text("Opening Amazon’s secure sign-in…", 13, "#52647C"); notice.Margin(0, 12, 0, 8);
            try { browser = ServiceLinks.PrivateView(); }
            catch (Exception error) { browser = null; notice.SetText(MainWindow.Message(error) + ServiceLinks.MissingRuntime); report("amazon_browser_error", J.Obj("error_type", error.GetType().Name)); }
            if (browser != null) root.Append(browser); else { var filler = MainWindow.VBox(); filler.SetVexpand(true); root.Append(filler); }
            var bottom = MainWindow.VBox(); bottom.Append(notice);
            var cancel = Button.NewWithLabel("Cancel"); cancel.SetHalign(Gtk.Align.Start); cancel.OnClicked += delegate { Close(); }; bottom.Append(cancel);
            root.Append(bottom);
            var view = Adw.ToolbarView.New(); view.AddTopBar(Adw.HeaderBar.New()); view.SetContent(root);
            Window.SetContent(view);
            Window.OnDestroy += delegate { closed = true; lifetime.Cancel(); attempt.Dispose(); };
            if (browser != null) GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT, delegate { Initialize(); return false; });
        }
        public bool Closed { get { return closed; } }
        public void Present() { Window.Present(); }
        public void Close() { if (!closed) Window.Close(); }
        private void Initialize()
        {
            if (closed) return;
            try
            {
                browser.OnDecidePolicy += (sender, args) =>
                {
                    if (closed) { args.Decision.Ignore(); return true; }
                    if (args.DecisionType == WebKit.PolicyDecisionType.Response)
                    {
                        var response = args.Decision as WebKit.ResponsePolicyDecision;
                        if (response != null && !response.IsMimeTypeSupported()) { args.Decision.Ignore(); return true; }
                        return false;
                    }
                    var navigation = args.Decision as WebKit.NavigationPolicyDecision;
                    if (navigation == null) return false;
                    string value = navigation.GetNavigationAction().GetRequest().GetUri() ?? "";
                    if (args.DecisionType == WebKit.PolicyDecisionType.NewWindowAction)
                    {
                        args.Decision.Ignore(); if (AmazonNativeAttempt.Navigation(value)) browser.LoadUri(value);
                        return true;
                    }
                    Uri uri;
                    if (Uri.TryCreate(value, UriKind.Absolute, out uri) && AmazonNativeAttempt.ReturnAddress(uri))
                    {
                        // The return address carries the one-time authorization code: it is never loaded.
                        args.Decision.Ignore(); Finish(uri);
                        return true;
                    }
                    if (!AmazonNativeAttempt.Navigation(value)) { args.Decision.Ignore(); notice.SetText("Amazon requested an unsupported destination."); return true; }
                    args.Decision.Use();
                    return true;
                };
                browser.OnCreate += (sender, args) => null;
                browser.OnLoadChanged += (sender, args) =>
                {
                    if (closed) return;
                    if (args.LoadEvent == WebKit.LoadEvent.Started) failed = false;
                    if (args.LoadEvent == WebKit.LoadEvent.Started || args.LoadEvent == WebKit.LoadEvent.Redirected || args.LoadEvent == WebKit.LoadEvent.Committed)
                    {
                        string current = browser.GetUri() ?? ""; Uri uri;
                        if (!AmazonNativeAttempt.Navigation(current)) { browser.StopLoading(); if (!completing) notice.SetText("Amazon requested an unsupported destination."); return; }
                        if (Uri.TryCreate(current, UriKind.Absolute, out uri)) address.SetText(uri.Host);
                    }
                    if (args.LoadEvent == WebKit.LoadEvent.Finished && !failed && !completing)
                    { notice.SetText("Complete Amazon sign-in and any verification on the page above. The window closes only after eero verifies your account."); report("amazon_page_loaded", null); }
                };
                browser.OnLoadFailed += (sender, args) =>
                {
                    if (closed || completing || ServiceLinks.Interrupted(args.Error)) return true;
                    failed = true;
                    string status = args.Error == null ? "unknown error" : args.Error.Message;
                    notice.SetText("The Amazon page could not load (" + status + ").");
                    report("amazon_page_error", J.Obj("browser_error", args.Error == null ? "Unknown" : "Code" + args.Error.Code));
                    return false;
                };
                report("amazon_browser_ready", null); browser.LoadUri(attempt.SignInUrl);
            }
            catch (Exception error) { if (!closed) notice.SetText(MainWindow.Message(error)); report("amazon_browser_error", J.Obj("error_type", error.GetType().Name)); }
        }
        private async void Finish(Uri uri)
        {
            if (completing || closed) return; completing = true;
            string phase = "amazon_token_exchange";
            try
            {
                report("amazon_code_returned", null); notice.SetText("Finishing Amazon sign-in…");
                string token = await attempt.Exchange(uri, lifetime.Token); report("amazon_token_received", null);
                phase = "eero_account_verification";
                notice.SetText("Verifying your eero account…"); await complete(token, lifetime.Token); token = null;
                if (!closed) Window.Close();
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                var failure = error as ApiException; string code = failure == null ? "" : failure.ApiError;
                if (!closed) notice.SetText(phase == "eero_account_verification" && code == "error.login.unknown"
                    ? "Amazon sign-in succeeded, but eero does not recognize this Amazon account for an existing eero login. Use your eero email or phone number. Amazon login can be managed in eero’s Account settings; no account was created or changed here."
                    : (phase == "amazon_token_exchange" ? "Amazon could not finish sign-in: " : "eero could not verify this Amazon sign-in: ") + MainWindow.Message(error) + " Close this window to start a fresh sign-in.");
                report("amazon_signin_error", J.Obj("phase", phase, "error_type", error.GetType().Name, "http_code", failure == null ? 0 : failure.Code, "api_error", Regex.IsMatch(code ?? "", "^[a-zA-Z0-9_.-]{1,140}$") ? code : ""));
            }
        }
    }

    public sealed partial class MainWindow
    {
        private AmazonNativeWindow amazonNativeWindow;
        private void AmazonConsumerSignIn()
        {
            if (amazonNativeWindow != null && !amazonNativeWindow.Closed) { amazonNativeWindow.Present(); return; }
            var window = new AmazonNativeWindow(this, async delegate(string token, System.Threading.CancellationToken cancellation)
            {
                await CompleteExternalSignIn(async delegate
                {
                    bool remember = api.Remember, success = false;
                    try
                    {
                        api.Remember = rememberBox == null || rememberBox.GetActive();
                        await api.AmazonSignIn(token, cancellation); success = true; ReportStatus("amazon_authenticated");
                        ClearData(); await LoadAccount(); ShowShell(); SetNotice("Signed in with Amazon."); ReportStatus("dashboard_loaded");
                    }
                    finally { if (!success) api.Remember = remember; }
                });
            }, delegate(string state, object details)
            {
                if (String.IsNullOrEmpty(statusPath)) return;
                try
                {
                    string json = J.Stringify(J.Obj("state", state, "time_utc", DateTime.UtcNow.ToString("o"), "details", details));
                    File.WriteAllText(statusPath + ".amazon-native.json", json);
                    string history = statusPath + ".amazon-native.history.jsonl";
                    if (!File.Exists(history) || new FileInfo(history).Length < 65536) File.AppendAllText(history, json + Environment.NewLine);
                }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            });
            amazonNativeWindow = window;
            window.Window.OnDestroy += delegate { if (amazonNativeWindow == window) amazonNativeWindow = null; };
            window.Present();
        }
    }
}
