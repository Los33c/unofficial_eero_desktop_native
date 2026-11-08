using System;
using System.IO;
using System.Linq;
using System.Reflection;

[assembly: AssemblyTitle("eero Desktop (Unofficial) - Carlos B. (Los33)")]
[assembly: AssemblyCompany("Carlos B.")]
[assembly: AssemblyProduct("eero Desktop (Unofficial)")]
[assembly: AssemblyDescription("Independent eero network manager for Linux. Made by: Carlos B. (Los33).")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyInformationalVersion("1.1.0")]

namespace EeroDesktop
{
    public static partial class Program
    {
        public const string AppId = "io.github.los33.EeroDesktop";
        internal static Adw.Application App;

        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test") return Tests.Run(args.Length > 1 ? args[1] : "test-results.txt");
            if (args.Length > 1 && args[0] == "--connection-test") return ConnectionDiagnostics.Run(args[1]);
            if (args.Length > 1 && args[0] == "--browser-runtime-test") { InitializeLibraries(); return ServiceLinks.CheckRuntime(args[1]); }
            if (args.Length > 1 && args[0] == "--bluetooth-test") return NativeBluetooth.Check(args[1]);
            if (args.Length == 2 && args[0] == "--vpn-control") return LinuxVpn.ElevatedControl(args[1]);
            if (args.Contains("--version")) { Console.WriteLine("eero Desktop (Unofficial) 1.1.0 for Linux"); return 0; }
            if (args.Contains("--help") || args.Contains("-h")) { Console.WriteLine(Usage); return 0; }

            InitializeLibraries();
            bool isolated = args.Contains("--amazon-native-signin") || args.Contains("--preview") || args.Contains("--theme-preview") || args.Contains("--browser-preview") || args.Contains("--chat-preview") || (args.Length > 0 && (args[0] == "--render" || args[0] == "--render-chat"));
            int themeIndex = Array.IndexOf(args, "--theme");
            if (themeIndex >= 0 && themeIndex + 1 < args.Length && new[] { "Light", "Dark", "System" }.Contains(args[themeIndex + 1])) UiPreferences.Theme = args[themeIndex + 1];
            bool background = args.Contains("--background");

            App = Adw.Application.New(isolated ? null : AppId, isolated ? Gio.ApplicationFlags.NonUnique : Gio.ApplicationFlags.DefaultFlags);
            if (!isolated)
            {
                // One window per desktop session: a second launch raises the running app.
                try { App.Register(null); } catch (Exception) { }
                if (App.GetIsRemote())
                {
                    if (!background) App.Activate();
                    return 0;
                }
                if (background && !UiPreferences.StartAtLogin) return 0;
            }

            MainWindow window = null;
            // Like the Windows DispatcherUnhandledException handler: report, keep running.
            GLib.UnhandledException.SetHandler(e =>
            {
                Console.Error.WriteLine(e);
                if (window != null) window.ShowApplicationError("The app could not complete that action. " + MainWindow.Message(e));
            });
            string renderPath = args.Length > 1 && (args[0] == "--render" || args[0] == "--render-chat") ? args[1] : null;
            App.OnStartup += delegate { Css.Load(); ApplyTheme(); ApplicationIcon.Install(); };
            App.OnActivate += delegate
            {
                if (window != null) { window.RestoreWindow(); return; }
                if (args.Contains("--chat-preview") || (args.Length > 1 && args[0] == "--render-chat"))
                {
                    var chat = MainWindow.ChatSample(App);
                    if (renderPath != null) Rendering.SaveWhenReady(chat, renderPath, 960, 780, delegate { App.Quit(); });
                    return;
                }
                if (args.Contains("--browser-preview")) { new ServiceBrowserWindow(null, "Service browser sample", null, null, null, true).Present(); return; }
                string statusPath = null;
                int statusIndex = Array.IndexOf(args, "--status-file");
                if (statusIndex >= 0 && statusIndex + 1 < args.Length) statusPath = args[statusIndex + 1];
                bool render = renderPath != null;
                window = new MainWindow(App, args.Contains("--preview") || args.Contains("--theme-preview") || render, statusPath, background, args.Contains("--amazon-native-signin"));
                if (args.Contains("--theme-preview")) window.PreviewPage("Theme preview");
                if (render)
                {
                    if (args.Length > 2 && !args[2].StartsWith("--")) window.PreviewPage(args[2]);
                    int renderWidth = 1260, renderHeight = 820;
                    int sizeIndex = Array.IndexOf(args, "--render-size");
                    if (sizeIndex >= 0 && sizeIndex + 1 < args.Length)
                    {
                        string[] dimensions = args[sizeIndex + 1].Split('x');
                        if (dimensions.Length != 2 || !Int32.TryParse(dimensions[0], out renderWidth) || !Int32.TryParse(dimensions[1], out renderHeight) || renderWidth < 360 || renderWidth > 3200 || renderHeight < 480 || renderHeight > 2400)
                            throw new ArgumentException("Render size must be between 360x480 and 3200x2400.");
                    }
                    Rendering.SaveWhenReady(window.Window, renderPath, renderWidth, renderHeight, delegate { App.Quit(); });
                    return;
                }
                window.Show();
            };
            App.OnShutdown += delegate { if (window != null) window.PrepareForShutdown(); };
            // GLib parses argv itself; our options were handled above.
            int status = App.RunWithSynchronizationContext(new[] { "eero-desktop" });
            return renderPath != null && !File.Exists(renderPath) ? 1 : status;
        }

        private const string Usage = "eero Desktop (Unofficial) for Linux\n\n  eero-desktop                 Open the app\n  --background                 Start hidden (used by start at login)\n  --preview                    Sample network, no account or network requests\n  --theme Light|Dark|System    Override the appearance\n  --render <file.png> [page] [--render-size WxH]   Save a sample page as PNG\n  --self-test <results.txt>    Run the built-in self-tests\n  --connection-test <out.json> Check the HTTPS connection to eero\n  --browser-runtime-test <out.json>  Check WebKitGTK\n  --bluetooth-test <out.json>  Check BlueZ Bluetooth access\n  --version";

        internal static void InitializeLibraries()
        {
            Adw.Module.Initialize();
            try { WebKit.Module.Initialize(); } catch (Exception) { }
        }

        public static void ApplyTheme()
        {
            var manager = Adw.StyleManager.GetDefault();
            manager.SetColorScheme(UiPreferences.Theme == "Dark" ? Adw.ColorScheme.ForceDark : UiPreferences.Theme == "Light" ? Adw.ColorScheme.ForceLight : Adw.ColorScheme.Default);
        }
    }
}
