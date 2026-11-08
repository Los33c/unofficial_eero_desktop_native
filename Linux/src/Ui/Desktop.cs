using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gtk;

namespace EeroDesktop
{
    public static class UiPreferences
    {
        public static string Theme = "System";
        public static bool Notifications, Background, StartAtLogin;
        public static bool AllNetworks = true;
        private static readonly string Path = System.IO.Path.Combine(MainWindow.DataDirectory, "preferences.json");
        static UiPreferences()
        {
            try { if (File.Exists(Path)) { object p = J.Parse(File.ReadAllText(Path)); string theme = J.S(p, "theme"); if (new[] { "Light", "Dark", "System" }.Contains(theme)) Theme = theme; Notifications = J.B(p, "notifications"); Background = J.B(p, "background"); StartAtLogin = J.B(p, "start_at_login"); AllNetworks = J.Get(p, "all_networks") == null || J.B(p, "all_networks"); } }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { }
        }
        // Follows libadwaita, which follows the desktop's light/dark preference (GNOME, KDE, xdg portal).
        public static bool Dark
        {
            get
            {
                if (Theme == "Dark") return true;
                if (Theme != "System") return false;
                try { return Adw.StyleManager.GetDefault().GetDark(); } catch (Exception) { return false; }
            }
        }
        public static void Save()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)); string temporary = Path + ".tmp";
            File.WriteAllText(temporary, J.Stringify(J.Obj("theme", Theme, "notifications", Notifications, "background", Background, "start_at_login", StartAtLogin, "all_networks", AllNetworks)));
            File.Move(temporary, Path, true);
        }
        public static string Color(string color) { return color; }
    }

    // XDG autostart entry, the freedesktop equivalent of the Windows Run key.
    public sealed class AutostartStore : IStartupStore
    {
        private readonly string file;
        public AutostartStore(string path = null)
        {
            if (path == null)
            {
                string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                if (String.IsNullOrEmpty(config) || !System.IO.Path.IsPathRooted(config)) config = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                path = System.IO.Path.Combine(config, "autostart", "eero-desktop.desktop");
            }
            file = path;
        }
        public string Read()
        {
            try
            {
                if (!File.Exists(file)) return null;
                string exec = File.ReadAllLines(file).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("Exec=", StringComparison.Ordinal));
                return exec == null ? null : exec.Substring(5);
            }
            catch (IOException) { return null; }
        }
        public void Write(string command)
        {
            if (command == null) { if (File.Exists(file)) File.Delete(file); return; }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
            string temporary = file + ".tmp";
            File.WriteAllText(temporary, "[Desktop Entry]\nType=Application\nName=eero Desktop\nComment=Unofficial eero network manager (starts hidden for network alerts)\nExec=" + command + "\nIcon=" + Program.AppId + "\nTerminal=false\nNoDisplay=true\nX-GNOME-Autostart-enabled=true\nX-GNOME-Autostart-Delay=5\n");
            File.Move(temporary, file, true);
        }
    }

    public sealed partial class MainWindow
    {
        private UiTimer notificationTimer;
        private bool exiting, pollingNotifications, heldInBackground;
        private int notificationEpoch;
        private NotificationJournal notificationJournal;
        private bool backgroundLaunch;
        private bool notificationsConfigured;

        private void ConfigureDesktopNotifications()
        {
            if (!notificationsConfigured)
            {
                notificationsConfigured = true;
                notificationTimer = new UiTimer(TimeSpan.FromMinutes(1), async delegate { await PollNotifications(); });
                // "open" from a notification brings the window back.
                var open = Gio.SimpleAction.New("open-window", null);
                open.OnActivate += delegate { RestoreWindow(); };
                application.AddAction(open);
                var quit = Gio.SimpleAction.New("quit", null);
                quit.OnActivate += delegate { exiting = true; ReleaseBackground(); Window.Destroy(); application.Quit(); };
                application.AddAction(quit);
                application.SetAccelsForAction("app.quit", new[] { "<Control>q" });
            }
            if (UiPreferences.Notifications && !preview) notificationTimer.Start(); else notificationTimer.Stop();
        }
        // Closing the window keeps the app running in the background when alerts are on (GNOME has no tray).
        private bool OnCloseRequested()
        {
            if (!exiting && UiPreferences.Background && api.SignedIn && !preview)
            {
                if (!heldInBackground) { application.Hold(); heldInBackground = true; }
                Window.SetVisible(false);
                SendNotification("background", "eero Desktop is still running", "Network alerts keep coming. Open eero Desktop again from your app launcher, or press Ctrl+Q in the window to quit.", false);
                return true;
            }
            ReleaseBackground();
            return false;
        }
        private void ReleaseBackground() { if (heldInBackground) { heldInBackground = false; application.Release(); } }
        internal void RestoreWindow()
        {
            backgroundLaunch = false;
            ReleaseBackground();
            Window.SetVisible(true); Window.Present();
        }
        internal void PrepareForShutdown() { exiting = true; if (notificationTimer != null) notificationTimer.Stop(); }
        private void SendNotification(string id, string title, string body, bool urgent)
        {
            try
            {
                var n = Gio.Notification.New(title.Length > 63 ? title.Substring(0, 63) : title);
                n.SetBody(body.Length > 240 ? body.Substring(0, 240) : body);
                n.SetDefaultAction("app.open-window");
                n.SetPriority(urgent ? Gio.NotificationPriority.High : Gio.NotificationPriority.Normal);
                application.SendNotification(id, n);
            }
            catch (Exception) { }
        }
        private void SignInAttention()
        {
            SendNotification("sign-in", "eero Desktop needs sign-in", "Open eero Desktop to sign in again and restore network alerts.", true);
        }
        private async Task PollNotifications()
        {
            if (pollingNotifications || busy || !api.SignedIn || String.IsNullOrEmpty(networkUrl) || !UiPreferences.Notifications) return;
            pollingNotifications = true; int epoch = notificationEpoch;
            try
            {
                if (notificationJournal == null) notificationJournal = new NotificationJournal(System.IO.Path.Combine(DataDirectory, "notifications-state.dat"));
                string owner = J.First(account, "id", "user_id", "email", "phone");
                var targets = UiPreferences.AllNetworks ? networkList.ToList() : new List<object> { J.Obj("url", networkUrl, "name", J.S(network, "name")) };
                foreach (object target in targets)
                {
                    if (!UiPreferences.Notifications || !api.SignedIn || epoch != notificationEpoch) return;
                    string url = J.S(target, "url"); if (String.IsNullOrEmpty(url)) continue;
                    object data;
                    try { data = await api.Send("GET", url.TrimEnd('/') + "/notifications_history?limit=50"); }
                    catch (ApiException e) { if (e.SessionExpired) throw; continue; }
                    if (!api.SignedIn || epoch != notificationEpoch) return;
                    var fresh = notificationJournal.Observe(owner, url, Entries(data, "notifications", "items"));
                    if (fresh.Count == 0 || !UiPreferences.Notifications) continue;
                    object latest = fresh.Last();
                    string title = fresh.Count == 1 ? J.S(latest, "title").NullIfEmpty() ?? "eero network notification" : fresh.Count + " eero network updates";
                    string body = (J.S(target, "name").NullIfEmpty() ?? "Your network") + ": " + (J.S(latest, "body").NullIfEmpty() ?? "Open eero Desktop to view the update.");
                    SendNotification("network-" + Math.Abs(url.GetHashCode()), title, body, false);
                }
                notificationJournal.Save();
            }
            catch (ApiException e) { if (e.SessionExpired) { api.Forget(); ClearData(); ShowLogin(false); SetNotice("Sign in again to restore network alerts.", true); SignInAttention(); } }
            catch (Exception) { }
            finally { pollingNotifications = false; }
        }
        private void AppearancePage()
        {
            Sub("Choose how eero Desktop looks. This only changes this app for your user account.");
            AddGroupHeading(page, "Theme");
            var tiles = Grid.New(); tiles.SetColumnHomogeneous(true); tiles.SetColumnSpacing(16); tiles.SetMarginBottom(18);
            string[] values = { "System", "Light", "Dark" }, labels = { "Match system", "Light", "Dark" }, notes = { "Follows your desktop's light or dark style", "Always light", "Always dark" };
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i]; bool chosen = UiPreferences.Theme == value;
                var content = VBox();
                content.Append(ThemeSample(value));
                var words = VBox(); words.SetHexpand(true); var name = Text(labels[i], 14, "#17263C", true); name.SetMarginBottom(0); words.Append(name);
                var note = Text(notes[i], 12, "#64738A"); note.SetMarginTop(2); words.Append(note);
                var caption = HBox(8); caption.Margin(2, 12, 2, 0); caption.Append(words);
                if (chosen) { var check = Glyph("", 16, "#274CDB"); check.SetValign(Gtk.Align.Start); caption.Append(check); }
                content.Append(caption);
                var tile = Gtk.Button.New(); tile.SetChild(content); tile.AddCssClass("card"); tile.AddCssClass("theme-tile"); if (chosen) tile.AddCssClass("chosen");
                tile.Name(labels[i] + " theme" + (chosen ? ", selected" : ""));
                tile.OnClicked += delegate { if (busy || UiPreferences.Theme == value) return; UiPreferences.Theme = value; UiPreferences.Save(); Program.ApplyTheme(); RenderPage(); SetNotice("Theme changed to " + (value == "System" ? "match your desktop." : value.ToLowerInvariant() + ".")); };
                tiles.Attach(tile, i, 0, 1, 1);
            }
            page.Append(tiles);
            Group("More", SettingRow("Text size and contrast", "Your desktop's accessibility settings control these for every app, including this one.", null, RowAction("Open system settings ↗", OpenAccessibilitySettings)));
        }
        private void OpenAccessibilitySettings()
        {
            // GNOME, KDE and others each ship their own settings app; try the common ones in order.
            foreach (var candidate in new[] { new[] { "gnome-control-center", "universal-access" }, new[] { "systemsettings", "kcm_access" }, new[] { "xfce4-accessibility-settings" }, new[] { "cinnamon-settings", "accessibility" } })
            {
                try { var start = new System.Diagnostics.ProcessStartInfo(candidate[0]) { UseShellExecute = false }; foreach (string a in candidate.Skip(1)) start.ArgumentList.Add(a); System.Diagnostics.Process.Start(start); return; }
                catch (System.ComponentModel.Win32Exception) { }
            }
            throw new InvalidOperationException("No system settings app was found. Open your desktop's accessibility settings yourself.");
        }
        // A small picture of the app in a theme. "System" shows light and dark side by side.
        private static Widget ThemeSample(string theme)
        {
            Func<bool, Widget> half = dark =>
            {
                string mode = dark ? "dark" : "light";
                var sample = VBox(); sample.AddCssClass("sample-" + mode); sample.SetHexpand(true); sample.SetVexpand(true);
                var inner = VBox(); inner.Margin(12, 12, 12, 12);
                var word = Label.New("eero"); word.AddCssClass("fs-20"); word.AddCssClass("light"); word.SetXalign(0); inner.Append(word);
                var tile = VBox(); tile.AddCssClass("sample-card-" + mode); tile.SetMarginTop(10);
                var internet = Label.New("Internet"); internet.AddCssClass("fs-11"); internet.SetXalign(0); tile.Append(internet);
                var bar = VBox(); bar.AddCssClass("sample-bar-" + mode); bar.SetSizeRequest(54, 5); bar.SetHalign(Gtk.Align.Start); bar.SetMarginTop(8); tile.Append(bar);
                var line = VBox(); line.AddCssClass("sample-line-" + mode); line.SetSizeRequest(80, 5); line.SetHalign(Gtk.Align.Start); line.SetMarginTop(6); tile.Append(line);
                inner.Append(tile); sample.Append(inner);
                return sample;
            };
            var frame = HBox(0); frame.SetSizeRequest(-1, 130); frame.SetOverflow(Overflow.Hidden); frame.AddCssClass("card");
            if (theme == "System") { frame.Append(half(false)); frame.Append(half(true)); }
            else frame.Append(half(theme == "Dark"));
            return frame;
        }
        private void DesktopNotificationsPage()
        {
            Sub("How this app alerts you on this computer. These settings are saved for your user account only.");
            bool alerts = UiPreferences.Notifications, background = UiPreferences.Background, startup = UiPreferences.StartAtLogin, all = UiPreferences.AllNetworks;
            Group(null,
                SettingToggle("Desktop alerts", "Show a desktop notification when eero reports something new. Checks every minute while the app runs.", alerts, async delegate(bool on) { await SaveDesktopPreferences(on, background, startup, all); }),
                SettingToggle("Keep running when closed", "Keep checking in the background after you close the window, so alerts keep coming.", background || startup, async delegate(bool on) { await SaveDesktopPreferences(alerts, on, on && startup, all); }),
                SettingToggle("Start at login", "Start quietly in the background when you log in. Keep the app where it is installed.", startup, async delegate(bool on) { await SaveDesktopPreferences(alerts, background || on, on, all); }),
                SettingToggle("Monitor all networks", "Alert about every network in your account, not just the one selected.", all, async delegate(bool on) { await SaveDesktopPreferences(alerts, background, startup, on); }));
            page.Append(Text("While running in the background, open eero Desktop from your app launcher to bring the window back; press Ctrl+Q in the window to quit and stop alerts until the next launch. Remember your eero session when you sign in so the app can restore it automatically.", 13, "#64738A"));
        }
        private async Task SaveDesktopPreferences(bool alerts, bool background, bool startup, bool all)
        {
            if (preview) throw new InvalidOperationException("Preview mode does not change start at login or your preferences.");
            bool oldEnabled = UiPreferences.Notifications, oldBackground = UiPreferences.Background, oldStartup = UiPreferences.StartAtLogin, oldAll = UiPreferences.AllNetworks;
            try
            {
                StartupRegistration.Apply(new AutostartStore(), LauncherPath, startup, delegate
                {
                    UiPreferences.Notifications = alerts; UiPreferences.StartAtLogin = startup; UiPreferences.Background = background || startup; UiPreferences.AllNetworks = all; UiPreferences.Save();
                });
            }
            catch { UiPreferences.Notifications = oldEnabled; UiPreferences.Background = oldBackground; UiPreferences.StartAtLogin = oldStartup; UiPreferences.AllNetworks = oldAll; throw; }
            ConfigureDesktopNotifications(); RenderPage(); SetNotice("Notification preferences saved."); await Task.FromResult(0);
        }
        // The command start-at-login runs: the AppImage itself when launched from one, otherwise this executable.
        internal static string LauncherPath
        {
            get
            {
                string appImage = Environment.GetEnvironmentVariable("APPIMAGE");
                if (!String.IsNullOrEmpty(appImage) && File.Exists(appImage)) return appImage;
                return Environment.ProcessPath;
            }
        }
    }
}
