using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Gtk;

namespace EeroDesktop
{
    // Allow-list and runtime checks for the in-app service browser (same rules as the Windows WebView2 browser).
    public static class ServiceLinks
    {
        public static Uri Eero(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length > 0 || !(uri.Host == "eero.com" || uri.Host.EndsWith(".eero.com", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("The service did not return a secure eero web address.");
            return uri;
        }
        public static string TokenHeader(string token)
        {
            if (String.IsNullOrEmpty(token) || token.Length > 8192 || token.Any(c => c < 33 || c > 126)) throw new InvalidDataException("The web service did not return a valid temporary sign-in token.");
            return "x-temp-user-token: " + token + "\r\n";
        }
        public static bool KeepTemporaryHeader(string initial, string request)
        {
            Uri target, resource;
            try { target = Eero(initial); } catch (InvalidDataException) { return false; }
            return Uri.TryCreate(request, UriKind.Absolute, out resource) && resource.Scheme == "https" && resource.Port == 443 && resource.UserInfo.Length == 0 && target.AbsoluteUri == resource.AbsoluteUri;
        }
        public static bool Allowed(string value)
        {
            if (value == "about:blank") return true;
            Uri u; if (!Uri.TryCreate(value, UriKind.Absolute, out u) || u.Scheme != "https" || u.Port != 443 || u.UserInfo.Length > 0) return false;
            string[] domains = { "eero.com", "e2ro.com", "amazon.com", "alexa.com", "amazon.co.uk", "amazon.de", "amazon.fr", "amazon.it", "amazon.es", "amazon.ca", "amazon.com.au", "amazon.co.jp", "amazon.in", "amazon.com.mx", "amazon.com.br", "guardianapp.com", "1password.com", "malwarebytes.com", "stripe.com", "paypal.com", "eero.zendesk.com" };
            return domains.Any(d => u.Host == d || u.Host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
        }
        // Kept for parity with Windows. Service windows use ephemeral WebKit sessions, so nothing is written here.
        public static string ProfileDirectory { get { return Path.Combine(MainWindow.DataDirectory, "WebServices"); } }
        public static bool ProviderNavigation(string value)
        { Uri uri; return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.Port == 443 && uri.UserInfo.Length == 0; }
        public static string RuntimeVersion()
        { return WebKit.Functions.GetMajorVersion() + "." + WebKit.Functions.GetMinorVersion() + "." + WebKit.Functions.GetMicroVersion(); }
        public static int CheckRuntime(string output)
        {
            try
            {
                string version = RuntimeVersion();
                // Creating a private session proves the network process side of WebKitGTK is usable.
                using (var session = WebKit.NetworkSession.NewEphemeral()) if (!session.GetIsEphemeral()) throw new InvalidOperationException("WebKitGTK did not create a private session.");
                File.WriteAllText(output, J.Stringify(J.Obj("ok", true, "webkitgtk_runtime", version, "webkitgtk_api", "6.0")));
                return 0;
            }
            catch (Exception e) { File.WriteAllText(output, J.Stringify(J.Obj("ok", false, "error", e.GetType().Name))); return 1; }
        }
        internal const string MissingRuntime = " Install the WebKitGTK 6.0 runtime from your distribution (for example libwebkitgtk-6.0-4 or webkitgtk6.0) if it is missing.";

        // A private (ephemeral) WebKit session: no cookies, cache or credentials survive the window.
        internal static WebKit.WebView PrivateView()
        {
            var session = WebKit.NetworkSession.NewEphemeral();
            session.SetPersistentCredentialStorageEnabled(false);
            session.SetTlsErrorsPolicy(WebKit.TLSErrorsPolicy.Fail);
            session.OnDownloadStarted += (sender, args) => { args.Download.Cancel(); };
            var view = WebKit.WebView.NewWithProperties(new[] { new GObject.ConstructArgument("network-session", new GObject.Value(session)) });
            var settings = view.GetSettings();
            settings.SetEnableDeveloperExtras(false);
            settings.SetJavascriptCanOpenWindowsAutomatically(false);
            view.SetHexpand(true); view.SetVexpand(true);
            // Websites may not use the camera, microphone, location or notifications from these windows.
            view.OnPermissionRequest += (sender, args) => { args.Request.Deny(); return true; };
            view.OnRunFileChooser += (sender, args) => { args.Request.Cancel(); return true; };
            return view;
        }
        // Policy cancellations end a load too; those are not page errors.
        internal static bool Interrupted(GLib.Error error)
        {
            if (error == null) return false;
            return error.Matches(WebKit.Functions.PolicyErrorQuark(), 102) || error.Matches(WebKit.Functions.NetworkErrorQuark(), 302);
        }
    }

    // Reads cookies from a WebKit session. Gir.Core 0.8 does not bind the async start of
    // webkit_cookie_manager_get_cookies, so this calls WebKitGTK and libsoup directly.
    internal static class WebKitCookies
    {
        internal sealed class Cookie { public string Name, Value, Domain; public bool Secure; }
        private const string WebKitLib = "libwebkitgtk-6.0.so.4", SoupLib = "libsoup-3.0.so.0", GLibLib = "libglib-2.0.so.0";
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ReadyCallback(IntPtr source, IntPtr result, IntPtr data);
        [DllImport(WebKitLib)] private static extern void webkit_cookie_manager_get_cookies(IntPtr manager, [MarshalAs(UnmanagedType.LPUTF8Str)] string uri, IntPtr cancellable, ReadyCallback callback, IntPtr data);
        [DllImport(WebKitLib)] private static extern IntPtr webkit_cookie_manager_get_cookies_finish(IntPtr manager, IntPtr result, out IntPtr error);
        [DllImport(SoupLib)] private static extern IntPtr soup_cookie_get_name(IntPtr cookie);
        [DllImport(SoupLib)] private static extern IntPtr soup_cookie_get_value(IntPtr cookie);
        [DllImport(SoupLib)] private static extern IntPtr soup_cookie_get_domain(IntPtr cookie);
        [DllImport(SoupLib)] private static extern int soup_cookie_get_secure(IntPtr cookie);
        [DllImport(SoupLib)] private static extern void soup_cookie_free(IntPtr cookie);
        [DllImport(GLibLib)] private static extern void g_list_free(IntPtr list);
        [DllImport(GLibLib)] private static extern void g_error_free(IntPtr error);
        private static readonly ReadyCallback Ready = OnReady;
        private sealed class Pending { public WebKit.CookieManager Manager; public TaskCompletionSource<List<Cookie>> Done; }

        public static Task<List<Cookie>> Get(WebKit.WebView view, string uri)
        {
            var manager = view.GetNetworkSession().GetCookieManager();
            var pending = new Pending { Manager = manager, Done = new TaskCompletionSource<List<Cookie>>() };
            GCHandle handle = GCHandle.Alloc(pending);
            try { webkit_cookie_manager_get_cookies(manager.Handle.DangerousGetHandle(), uri, IntPtr.Zero, Ready, GCHandle.ToIntPtr(handle)); }
            catch { handle.Free(); throw; }
            return pending.Done.Task;
        }
        private static void OnReady(IntPtr source, IntPtr result, IntPtr data)
        {
            GCHandle handle = GCHandle.FromIntPtr(data);
            var pending = (Pending)handle.Target; handle.Free();
            try
            {
                IntPtr error;
                IntPtr list = webkit_cookie_manager_get_cookies_finish(pending.Manager.Handle.DangerousGetHandle(), result, out error);
                if (error != IntPtr.Zero) { g_error_free(error); pending.Done.TrySetException(new InvalidOperationException("The secure browser could not read its session.")); return; }
                var cookies = new List<Cookie>();
                for (IntPtr node = list; node != IntPtr.Zero; node = Marshal.ReadIntPtr(node, IntPtr.Size))
                {
                    IntPtr cookie = Marshal.ReadIntPtr(node);
                    if (cookie == IntPtr.Zero) continue;
                    cookies.Add(new Cookie { Name = Marshal.PtrToStringUTF8(soup_cookie_get_name(cookie)) ?? "", Value = Marshal.PtrToStringUTF8(soup_cookie_get_value(cookie)) ?? "", Domain = Marshal.PtrToStringUTF8(soup_cookie_get_domain(cookie)) ?? "", Secure = soup_cookie_get_secure(cookie) != 0 });
                    soup_cookie_free(cookie);
                }
                if (list != IntPtr.Zero) g_list_free(list);
                pending.Done.TrySetResult(cookies);
            }
            catch (Exception e) { pending.Done.TrySetException(e); }
        }
    }

    // The secure service window: a private WebKitGTK view limited to the supported provider websites.
    public sealed class ServiceBrowserWindow
    {
        public readonly Adw.Window Window;
        private readonly WebKit.WebView browser;
        private readonly Label address, notice;
        private readonly string target, headers;
        private readonly bool sample;
        private readonly bool providerNavigation;
        private readonly Func<Uri, bool> callbackMatch;
        private readonly Func<Uri, Task> callback;
        private bool completing, closed, failed, temporaryHeader;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        public CancellationToken LifetimeToken { get { return lifetime.Token; } }
        private readonly Func<string, Task> importSession;
        private readonly Button import;
        private const string SampleHtml = "<html><body style='background:#101a2b;color:#e5ecf8;font:18px sans-serif;padding:40px'><h1>Linux service browser</h1><p>WebKitGTK is running. This sample uses no account or network request.</p></body></html>";
        public ServiceBrowserWindow(MainWindow owner, string title, string url, string extraHeaders = null, Func<string, Task> import = null, bool preview = false, Func<Uri, bool> callbackMatch = null, Func<Uri, Task> callback = null, bool providerNavigation = false)
        {
            target = url; headers = extraHeaders; importSession = import; sample = preview;
            this.callbackMatch = callbackMatch; this.callback = callback; this.providerNavigation = providerNavigation;
            Window = Adw.Window.New();
            Window.SetTitle(title + " · eero Desktop"); Window.SetDefaultSize(1080, 800); Window.SetSizeRequest(850, 650);
            Window.SetIconName(Program.AppId);
            if (Program.App != null) Window.SetApplication(Program.App);
            if (owner != null) owner.Own(Window);
            var root = MainWindow.VBox();
            var top = MainWindow.VBox(); top.Margin(18, 12, 18, 12);
            address = MainWindow.Text(preview ? "Sample service window" : new Uri(url).Host, 17, "#17263C", true); top.Append(address);
            if (import != null)
            {
                var help = MainWindow.Text("Sign in yourself on eero's website. Use this sign-in imports only a verified eero API session from this private app browser.", 14, "#64738A"); help.Margin(0, 8, 0, 8); top.Append(help);
                this.import = Button.NewWithLabel("Use this sign-in"); this.import.AddCssClass("suggested-action"); this.import.SetSensitive(false); this.import.SetHalign(Gtk.Align.Start);
                this.import.OnClicked += async delegate { await ImportClick(); }; top.Append(this.import);
            }
            root.Append(top);
            notice = MainWindow.Text("Opening secure service…", 13, "#52647C"); notice.Margin(18, 10, 18, 16);
            try { browser = ServiceLinks.PrivateView(); }
            catch (Exception e) { browser = null; notice.SetText("The secure browser could not start: " + MainWindow.Message(e) + ServiceLinks.MissingRuntime); }
            if (browser != null) root.Append(browser); else { var filler = MainWindow.VBox(); filler.SetVexpand(true); root.Append(filler); }
            root.Append(notice);
            var view = Adw.ToolbarView.New(); view.AddTopBar(Adw.HeaderBar.New()); view.SetContent(root);
            Window.SetContent(view);
            Window.OnDestroy += delegate { closed = true; lifetime.Cancel(); };
            if (browser != null) GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT, delegate { Initialize(); return false; });
        }
        public void Present() { Window.Present(); }
        public void Show() { Window.Present(); }
        public void Close() { if (!closed) Window.Close(); }
        private bool Permitted(string uri) { return ServiceLinks.Allowed(uri) || providerNavigation && ServiceLinks.ProviderNavigation(uri); }
        private void Initialize()
        {
            if (closed) return;
            try
            {
                browser.OnDecidePolicy += (sender, args) => Decide(args.Decision, args.DecisionType);
                browser.OnCreate += (sender, args) => null;
                browser.OnLoadChanged += (sender, args) =>
                {
                    if (closed) return;
                    if (args.LoadEvent == WebKit.LoadEvent.Started) failed = false;
                    if (args.LoadEvent == WebKit.LoadEvent.Started || args.LoadEvent == WebKit.LoadEvent.Redirected || args.LoadEvent == WebKit.LoadEvent.Committed)
                    {
                        // Second line of defence for the top-level page: WebKitGTK cannot tell frame and page
                        // navigations apart in its policy signal, so a page that ends up outside the list is stopped here.
                        string current = browser.GetUri() ?? "";
                        if (!(sample && current == "about:blank") && !Permitted(current))
                        {
                            browser.StopLoading(); notice.SetText("This destination is outside the supported provider websites.");
                            if (args.LoadEvent == WebKit.LoadEvent.Committed) browser.LoadHtml("<html><body></body></html>", "about:blank");
                            return;
                        }
                        Uri u; if (Uri.TryCreate(current, UriKind.Absolute, out u) && u.Scheme == "https") address.SetText(u.Host + u.AbsolutePath);
                    }
                    if (args.LoadEvent == WebKit.LoadEvent.Finished)
                    {
                        temporaryHeader = false;
                        if (!failed) notice.SetText(sample ? "Local sample loaded. No account or network request." : "Secure provider service. Complete any sign-in or purchase yourself.");
                        if (import != null) import.SetSensitive(!failed);
                        // Developer switch: save the local sample window as PNG (screenshots for the docs).
                        string render = Environment.GetEnvironmentVariable("EERO_DESKTOP_BROWSER_RENDER");
                        if (sample && !String.IsNullOrEmpty(render) && Path.IsPathRooted(render) && render.EndsWith(".png", StringComparison.Ordinal))
                            GLib.Functions.TimeoutAdd(GLib.Constants.PRIORITY_DEFAULT, 1500, delegate { try { Rendering.Save(Window, render); } catch (Exception e) { Console.Error.WriteLine("Render failed: " + e.Message); } if (Program.App != null) Program.App.Quit(); return false; });
                    }
                };
                browser.OnLoadFailed += (sender, args) =>
                {
                    if (closed || ServiceLinks.Interrupted(args.Error)) return true;
                    failed = true; notice.SetText("The provider page did not finish loading (" + (args.Error == null ? "unknown error" : args.Error.Message) + ").");
                    if (import != null) import.SetSensitive(false);
                    return false;
                };
                browser.OnLoadFailedWithTlsErrors += (sender, args) => { failed = true; notice.SetText("The provider page did not finish loading (the site's security certificate was not trusted)."); return false; };
                if (sample) browser.LoadHtml(SampleHtml, null);
                else if (headers == null) browser.LoadUri(target);
                else
                {
                    ServiceLinks.Eero(target);
                    var request = WebKit.URIRequest.New(target);
                    var requestHeaders = request.GetHttpHeaders();
                    foreach (string line in headers.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int colon = line.IndexOf(':');
                        if (colon <= 0 || line.Any(c => c < 32 || c > 126)) throw new InvalidDataException("The web service did not return a valid temporary sign-in token.");
                        requestHeaders.Append(line.Substring(0, colon).Trim(), line.Substring(colon + 1).Trim());
                    }
                    temporaryHeader = true;
                    browser.LoadRequest(request);
                }
            }
            catch (Exception e) { notice.SetText("The secure browser could not start: " + MainWindow.Message(e) + ServiceLinks.MissingRuntime); }
        }
        private bool Decide(WebKit.PolicyDecision decision, WebKit.PolicyDecisionType type)
        {
            if (closed) { decision.Ignore(); return true; }
            if (type == WebKit.PolicyDecisionType.Response)
            {
                var response = decision as WebKit.ResponsePolicyDecision;
                if (response != null && !response.IsMimeTypeSupported()) { decision.Ignore(); notice.SetText("This window does not download files."); return true; }
                return false;
            }
            var navigation = decision as WebKit.NavigationPolicyDecision;
            if (navigation == null) return false;
            var action = navigation.GetNavigationAction();
            string uri = action.GetRequest().GetUri() ?? "";
            if (sample && uri == "about:blank") { decision.Use(); return true; }
            Uri destination;
            bool callbackTarget = callbackMatch != null && Uri.TryCreate(uri, UriKind.Absolute, out destination) && callbackMatch(destination);
            if (type == WebKit.PolicyDecisionType.NewWindowAction)
            {
                // Popups open in this window, like the Windows browser.
                decision.Ignore();
                if (Permitted(uri) || callbackTarget) browser.LoadUri(uri); else notice.SetText("The provider requested a destination outside this service window.");
                return true;
            }
            if (callbackTarget)
            {
                decision.Ignore(); Uri.TryCreate(uri, UriKind.Absolute, out destination); Complete(destination);
                return true;
            }
            if (!Permitted(uri))
            {
                // Pages may embed frames from other sites (as WebView2 allows); a top-level page is checked again in load-changed.
                bool frameLike = action.GetNavigationType() == WebKit.NavigationType.Other && !action.IsUserGesture() && !action.IsRedirect() && !String.IsNullOrEmpty(browser.GetUri()) && browser.GetUri() != "about:blank";
                Uri parsed;
                if (frameLike && Uri.TryCreate(uri, UriKind.Absolute, out parsed) && parsed.Scheme == "https" && parsed.UserInfo.Length == 0) { decision.Use(); return true; }
                decision.Ignore(); notice.SetText("This destination is outside the supported provider websites.");
                return true;
            }
            if (temporaryHeader && action.IsRedirect() && !ServiceLinks.KeepTemporaryHeader(target, uri))
            {
                // The temporary eero token header is only for the first eero page. WebKitGTK cannot remove a header
                // from a redirect, so the redirect is restarted as a fresh request without it.
                temporaryHeader = false; decision.Ignore(); browser.LoadUri(uri);
                return true;
            }
            decision.Use();
            return true;
        }
        private async void Complete(Uri destination)
        {
            if (completing) return; completing = true;
            try { notice.SetText("Verifying the service response…"); await callback(destination); if (!closed) Window.Close(); }
            catch (Exception error) { if (!closed) notice.SetText(MainWindow.Message(error)); }
            finally { completing = false; }
        }
        private async Task ImportClick()
        {
            import.SetSensitive(false);
            try
            {
                var cookies = await WebKitCookies.Get(browser, EeroApi.Origin);
                var session = cookies.FirstOrDefault(c => c.Name == "s" && c.Secure && (c.Domain.TrimStart('.') == "api-user.e2ro.com" || c.Domain.TrimStart('.') == "e2ro.com"));
                if (session == null) { notice.SetText("This website has not provided a reusable eero API session. Finish signing in, or use email/phone verification in the app."); return; }
                await importSession(session.Value); if (!closed) Window.Close();
            }
            catch (Exception e) { if (!closed) notice.SetText(MainWindow.Message(e)); }
            finally { if (!closed) import.SetSensitive(true); }
        }
    }

    public sealed partial class MainWindow
    {
        private void OpenServiceWindow(string title, string url, string headers = null)
        {
            if (!ServiceLinks.Allowed(url)) throw new InvalidOperationException("The service returned an unsupported provider website.");
            var w = new ServiceBrowserWindow(this, title, url, headers); w.Present();
        }
        private async Task OpenPaymentService(string targetPage)
        {
            if (preview) throw new InvalidOperationException("Sign in to manage a subscription.");
            object session = await api.Send("GET", "/2.2/login/temporary?client_type=WEB_PAYMENTS&target_page=" + Uri.EscapeDataString(targetPage));
            string url = J.S(session, "target_url"); ServiceLinks.Eero(url);
            OpenBillingWindow(url, ServiceLinks.TokenHeader(J.S(session, "temporary_token")));
        }
        private void WebsiteSignIn()
        {
            api.Remember = rememberBox == null || rememberBox.GetActive();
            ServiceBrowserWindow w = null;
            w = new ServiceBrowserWindow(this, "Sign in on eero's website", "https://account.eero.com/", null, async token =>
            {
                await CompleteExternalSignIn(async delegate { await api.ImportBrowserSession(token, w.LifetimeToken); ClearData(); await LoadAccount(); ShowShell(); SetNotice("Website sign-in verified with eero."); });
            });
            w.Present();
        }
    }
}
