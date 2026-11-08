// Linux-specific parts of the self-test harness: the GTK rendering run, the GApplication
// single-instance checks (Windows: AppInstance named mutex) and the opt-in Secret Service checks.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace EeroDesktop
{
    internal static partial class Tests
    {
        private static GLib.MainContext mainContext;
        // Runs the GTK main context for a while so layout, styling and frame clocks settle.
        private static void Pump(int milliseconds)
        {
            if (mainContext == null) mainContext = GLib.MainContext.Default();
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < milliseconds) { if (!mainContext.Iteration(false)) Thread.Sleep(1); }
        }
        private static bool PumpUntil(Func<bool> done, int timeoutMilliseconds)
        {
            if (mainContext == null) mainContext = GLib.MainContext.Default();
            var clock = Stopwatch.StartNew();
            while (!done() && clock.ElapsedMilliseconds < timeoutMilliseconds) { if (!mainContext.Iteration(false)) Thread.Sleep(1); }
            return done();
        }
        // Theme changes re-render the current page from a GObject notify handler, where an exception
        // would end the process; park the window on the first page that renders before switching.
        private static void SwitchTheme(string theme, MainWindow window)
        {
            if (window != null)
            {
                Exception last = null; bool parked = false;
                foreach (string page in new[] { "Settings/Appearance", "Home", "Devices", "Settings" })
                {
                    try { window.PreviewPage(page); parked = true; break; }
                    catch (Exception e) { last = e; }
                }
                if (!parked) throw new InvalidOperationException("No page renders, so the theme cannot be switched safely.", last);
            }
            UiPreferences.Theme = theme; Program.ApplyTheme(); Pump(150);
        }
        private static void Check(string name, Func<bool> condition) { Isolate(name, delegate { Assert(condition(), name); }); }
        private static void NoWhite(bool found, string name)
        {
            if (found) results.Add("note: white surface found: " + ThemeVerification.LastOffender);
            Assert(!found, name);
        }

        // gtk_init_check reports a missing display instead of exiting like gtk_init (which Gtk.Module.Initialize calls).
        [System.Runtime.InteropServices.DllImport("libgtk-4.so.1")]
        private static extern bool gtk_init_check(); // gboolean is a 4-byte int, the default bool marshalling
        // WPF could lay out windows without a display. GTK needs a Wayland or X11 display, so the
        // rendering checks run only when one is available, inside a real Adw.Application.
        private static void GtkChecks()
        {
            string wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"), x11 = Environment.GetEnvironmentVariable("DISPLAY");
            const string skipped = "GTK rendering checks (UiChecks, ThemeChecks, NativeChecks layout, ApkUiChecks); run under a desktop session, WSLg, `weston --backend=headless` or `xvfb-run` to include them";
            if (String.IsNullOrEmpty(wayland) && String.IsNullOrEmpty(x11)) { results.Add("UI MODE: headless (no WAYLAND_DISPLAY or DISPLAY)."); Skip(skipped); return; }
            if (!gtk_init_check()) { results.Add("UI MODE: headless (GTK could not open the display " + (String.IsNullOrEmpty(wayland) ? x11 : wayland) + ")."); Skip(skipped); return; }
            Program.InitializeLibraries(); // GTK is already initialized, so this cannot exit the process
            results.Add("UI MODE: display (GDK opened " + Gdk.Display.GetDefault().GetName() + "). GTK rendering checks ran in an Adw.Application.");
            var app = Adw.Application.New(null, Gio.ApplicationFlags.NonUnique);
            Program.App = app;
            app.OnStartup += delegate { Css.Load(); Program.ApplyTheme(); };
            app.OnActivate += delegate
            {
                app.Hold();
                MainWindow window = null;
                try
                {
                    Isolate("preview window", delegate
                    {
                        window = new MainWindow(app, true);
                        window.Window.SetDefaultSize(1260, 820); window.Window.Present();
                        if (!PumpUntil(() => window.Window.GetMapped() && window.Window.GetWidth() > 0, 15000)) throw new TimeoutException("The preview window was never mapped by the compositor.");
                        Pump(300);
                    });
                    if (window != null)
                    {
                        Isolate("UiChecks", () => UiChecks(app, window));
                        Isolate("ThemeChecks", () => ThemeChecks(app, window));
                        Isolate("NativeChecks layout", () => NativeUiChecks(window));
                        Isolate("ApkUiChecks", () => ApkUiChecks(window));
                    }
                }
                finally
                {
                    if (window != null) { window.PrepareForShutdown(); window.Window.Destroy(); }
                    UiPreferences.Theme = "System"; Program.ApplyTheme();
                    app.Release(); app.Quit();
                }
            };
            app.RunWithSynchronizationContext(new[] { "eero-desktop-self-test" });
        }

        // Linux counterpart of the Windows AppInstance checks: Program registers one GApplication per
        // desktop session (D-Bus name ownership), and a second launch activates the first instead of
        // starting another window or notification monitor. The peer is a separate process, like a second launch.
        private const int PeerRemote = 10, PeerPrimary = 20;
        private static void InstanceChecks()
        {
            string bus = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"), runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (String.IsNullOrEmpty(bus) && (String.IsNullOrEmpty(runtime) || !File.Exists(Path.Combine(runtime, "bus")))) { Skip("GApplication single-instance checks (no D-Bus session bus; run under dbus-run-session to include them)"); return; }
            string id = "io.github.los33.EeroDesktop.SelfTest.t" + Guid.NewGuid().ToString("N");
            bool signaled = false;
            var owner = Gio.Application.New(id, Gio.ApplicationFlags.DefaultFlags);
            owner.OnActivate += delegate { signaled = true; };
            try
            {
                owner.Register(null);
                Assert(owner.GetIsRegistered() && !owner.GetIsRemote(), "first app instance owns the local session window");
                using (Process peer = StartPeer(id))
                {
                    // The owner answers the peer's D-Bus calls from this thread's main context.
                    PumpUntil(() => peer.HasExited && signaled, 20000);
                    if (!peer.HasExited) peer.Kill();
                    else if (peer.ExitCode != PeerRemote) results.Add("note: instance peer exited with " + peer.ExitCode);
                    Assert(peer.HasExited && peer.ExitCode == PeerRemote && signaled, "second app instance wakes the existing owner without starting another monitor");
                }
            }
            finally { owner.Dispose(); }
            // GApplication gives up its bus name when the owning process ends (disposing the wrapper does not
            // finalize it), so "closing" is an owner process that exits; the next launch must become the owner.
            string released = "io.github.los33.EeroDesktop.SelfTest.t" + Guid.NewGuid().ToString("N");
            int[] exits = new int[2];
            for (int i = 0; i < 2; i++)
                using (Process launch = StartPeer(released))
                {
                    PumpUntil(() => launch.HasExited, 20000);
                    if (!launch.HasExited) { launch.Kill(); exits[i] = -1; } else exits[i] = launch.ExitCode;
                }
            if (exits[0] != PeerPrimary || exits[1] != PeerPrimary) results.Add("note: instance owners exited with " + exits[0] + " and " + exits[1]);
            Assert(exits[0] == PeerPrimary && exits[1] == PeerPrimary, "closing the owner releases the app instance lock");
        }
        private static Process StartPeer(string id)
        {
            string exe = Environment.ProcessPath;
            var start = new ProcessStartInfo(exe) { UseShellExecute = false };
            if (Path.GetFileNameWithoutExtension(exe) == "dotnet") start.ArgumentList.Add(System.IO.Path.Combine(AppContext.BaseDirectory, "eero-desktop.dll"));
            start.ArgumentList.Add("--self-test"); start.ArgumentList.Add(Path.Combine(Path.GetTempPath(), "eero-instance-peer.txt"));
            start.Environment["EERO_TEST_INSTANCE_PEER"] = id;
            return Process.Start(start);
        }
        private static int InstancePeer(string id)
        {
            if (!Regex.IsMatch(id, "^io\\.github\\.los33\\.EeroDesktop\\.SelfTest\\.t[0-9a-f]{32}$")) return 2;
            Gio.Module.Initialize();
            var app = Gio.Application.New(id, Gio.ApplicationFlags.DefaultFlags);
            if (!app.Register(null)) return 3;
            if (!app.GetIsRemote()) return PeerPrimary;
            app.Run(new[] { "eero-desktop" }); // remote: forwards activate to the owner and flushes the bus
            return PeerRemote;
        }

        // Opt-in: the real libsecret path. Needs an unlocked Secret Service (GNOME Keyring, KWallet...).
        // It reuses (or creates) the app's real data key; it never deletes or replaces an existing key.
        private static void KeyringChecks(string scratch, Func<byte[]> realKeyring, Func<byte[]> testProvider)
        {
            if (Environment.GetEnvironmentVariable("EERO_TEST_KEYRING") != "1") { Skip("Secret Service keyring checks (set EERO_TEST_KEYRING=1 with an unlocked keyring to include them)"); return; }
            try
            {
                byte[] first = KeyringKey.LoadOrCreate(), second = KeyringKey.LoadOrCreate();
                Assert(first.Length == 32 && first.SequenceEqual(second), "Secret Service keyring keeps one persistent 256-bit data key");
                ProtectedData.KeyProvider = realKeyring; ProtectedData.ResetCache();
                var store = new SessionStore(Path.Combine(scratch, "keyring-session.dat")); store.Save("keyring-test");
                Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(store.FilePath)).Contains("keyring-test"), "session sealed with the real keyring key is not plaintext");
                ProtectedData.ResetCache();
                Assert(store.Load() == "keyring-test", "session sealed with the real keyring key opens after the key is fetched again");
                byte[] sealedWithTestKey;
                ProtectedData.KeyProvider = testProvider; ProtectedData.ResetCache();
                sealedWithTestKey = ProtectedData.Protect(Encoding.UTF8.GetBytes("other-key"), null, DataProtectionScope.CurrentUser);
                ProtectedData.KeyProvider = realKeyring; ProtectedData.ResetCache();
                bool rejected = false; try { ProtectedData.Unprotect(sealedWithTestKey, null, DataProtectionScope.CurrentUser); } catch (CryptographicException) { rejected = true; }
                Assert(rejected, "data sealed under a different key cannot be opened with the keyring key");
                store.Clear();
            }
            finally { ProtectedData.KeyProvider = testProvider; ProtectedData.ResetCache(); }
        }
    }
}
