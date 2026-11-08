using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace EeroDesktop
{
    public static class UiPreferences
    {
        public static string Theme = "System";
        public static bool Notifications, Background, StartWithWindows;
        public static bool AllNetworks = true;
        private static readonly string Path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EeroDesktop", "preferences.json");
        static UiPreferences()
        {
            try { if (File.Exists(Path)) { object p = J.Parse(File.ReadAllText(Path)); string theme = J.S(p, "theme"); if (new[] { "Light", "Dark", "System" }.Contains(theme)) Theme = theme; Notifications = J.B(p, "notifications"); Background = J.B(p, "background"); StartWithWindows = J.B(p, "start_with_windows"); AllNetworks = J.Get(p, "all_networks") == null || J.B(p, "all_networks"); } }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { }
        }
        public static bool Dark
        {
            get
            {
                if (Theme == "Dark") return true;
                if (Theme != "System") return false;
                try { using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) return key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 0; }
                catch (System.Security.SecurityException) { return false; }
            }
        }
        public static void Save() { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)); string temporary = Path + ".tmp"; File.WriteAllText(temporary, J.Stringify(J.Obj("theme", Theme, "notifications", Notifications, "background", Background, "start_with_windows", StartWithWindows, "all_networks", AllNetworks))); if (File.Exists(Path)) File.Replace(temporary, Path, null); else File.Move(temporary, Path); }
        public static string Color(string color)
        {
            string key = color.ToUpperInvariant();
            // Legacy page colors feed one semantic palette, including dialogs and chat.
            switch (key)
            {
                case "#17263C": case "#E5ECF8": return Dark ? "#F3F3F5" : "#202445";
                case "#64738A": case "#52647C": case "#A3B2CC": return Dark ? "#B2B3BD" : "#646879";
                case "#F4F6FA": case "#101A2B": return Dark ? "#1C1C1E" : "#F7F7F7";
                case "#F3F3F3": return Dark ? "#202022" : "#F3F3F3";
                case "#19253A": return Dark ? "#272729" : "#FFFFFF";
                case "#E5EAF3": case "#D6DFEC": case "#34425B": return Dark ? "#3D3D42" : "#E1E1E5";
                case "#274CDB": case "#345BEB": case "#9BB4FF": return Dark ? "#82AAFF" : "#0057FF";
                case "#0D7959": return Dark ? "#65D3B5" : "#007D68";
                case "#E4F5EE": return Dark ? "#193D34" : "#E4F5EE";
                case "#926410": return Dark ? "#F0C079" : "#8B5A0A";
                case "#B5333D": return Dark ? "#FFA4AC" : "#B22D3B";
                case "#EEF3FF": case "#233654": return Dark ? "#28364E" : "#EDF2FF";
                case "#152137": return Dark ? "#2D2D30" : "#FFFFFF";
                default: return color;
            }
        }
    }
    public sealed partial class MainWindow
    {
        private System.Windows.Forms.NotifyIcon tray;
        private DispatcherTimer notificationTimer;
        private bool exiting, pollingNotifications;
        private int notificationEpoch;
        private static SolidColorBrush Surface { get { return Brush("#19253A"); } }
        private void ConfigureDesktopNotifications()
        {
            if (notificationTimer == null)
            {
                notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
                notificationTimer.Tick += async delegate { await PollNotifications(); };
                Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!exiting && UiPreferences.Background && api.SignedIn && !preview) { e.Cancel = true; Hide(); } };
                Closed += delegate { notificationTimer.Stop(); if (tray != null) { var icon = tray.Icon; tray.Dispose(); if (icon != null) icon.Dispose(); } };
            }
            if ((UiPreferences.Notifications || UiPreferences.Background || backgroundLaunch) && !preview)
            {
                if (tray == null)
                {
                    tray = new System.Windows.Forms.NotifyIcon { Icon = ApplicationIcon.CreateTrayIcon(), Text = "eero Desktop", Visible = true };
                    var menu = new System.Windows.Forms.ContextMenuStrip();
                    menu.Items.Add("Open eero Desktop", null, delegate { Dispatcher.BeginInvoke(new Action(RestoreWindow)); });
                    menu.Items.Add("Exit", null, delegate { Dispatcher.BeginInvoke(new Action(delegate { exiting = true; Close(); })); }); tray.ContextMenuStrip = menu;
                    tray.DoubleClick += delegate { Dispatcher.BeginInvoke(new Action(RestoreWindow)); };
                    tray.BalloonTipClicked += delegate { Dispatcher.BeginInvoke(new Action(RestoreWindow)); };
                }
                tray.Visible = true;
            }
            else if (tray != null) tray.Visible = false;
            UpdateTrayAppearance();
            if (UiPreferences.Notifications && !preview) notificationTimer.Start(); else notificationTimer.Stop();
        }
        internal void UpdateTrayAppearance()
        {
            if (tray == null || tray.ContextMenuStrip == null) return;
            var menu = tray.ContextMenuStrip;
            menu.Renderer = UiPreferences.Dark ? new System.Windows.Forms.ToolStripProfessionalRenderer(new DarkTrayColors()) : new System.Windows.Forms.ToolStripProfessionalRenderer();
            menu.BackColor = System.Drawing.ColorTranslator.FromHtml(UiPreferences.Dark ? "#272729" : "#FFFFFF");
            menu.ForeColor = System.Drawing.ColorTranslator.FromHtml(UiPreferences.Dark ? "#F3F3F5" : "#202445");
            foreach (System.Windows.Forms.ToolStripItem item in menu.Items) item.ForeColor = menu.ForeColor;
        }
        private sealed class DarkTrayColors : System.Windows.Forms.ProfessionalColorTable
        {
            private static System.Drawing.Color SurfaceColor { get { return System.Drawing.Color.FromArgb(39, 39, 41); } }
            private static System.Drawing.Color HoverColor { get { return System.Drawing.Color.FromArgb(61, 61, 66); } }
            public override System.Drawing.Color ToolStripDropDownBackground { get { return SurfaceColor; } }
            public override System.Drawing.Color ImageMarginGradientBegin { get { return SurfaceColor; } }
            public override System.Drawing.Color ImageMarginGradientMiddle { get { return SurfaceColor; } }
            public override System.Drawing.Color ImageMarginGradientEnd { get { return SurfaceColor; } }
            public override System.Drawing.Color MenuBorder { get { return HoverColor; } }
            public override System.Drawing.Color MenuItemBorder { get { return HoverColor; } }
            public override System.Drawing.Color MenuItemSelected { get { return HoverColor; } }
            public override System.Drawing.Color MenuItemSelectedGradientBegin { get { return HoverColor; } }
            public override System.Drawing.Color MenuItemSelectedGradientEnd { get { return HoverColor; } }
            public override System.Drawing.Color MenuItemPressedGradientBegin { get { return HoverColor; } }
            public override System.Drawing.Color MenuItemPressedGradientMiddle { get { return HoverColor; } }
            public override System.Drawing.Color MenuItemPressedGradientEnd { get { return HoverColor; } }
        }
        private async Task PollNotifications()
        {
            if (pollingNotifications || busy || !api.SignedIn || String.IsNullOrEmpty(networkUrl) || !UiPreferences.Notifications) return;
            pollingNotifications = true; int epoch = notificationEpoch;
            try
            {
                if (notificationJournal == null) notificationJournal = new NotificationJournal(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EeroDesktop", "notifications-state.dat"));
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
                    if (fresh.Count == 0 || tray == null || !UiPreferences.Notifications) continue;
                    object latest = fresh.Last();
                    string title = fresh.Count == 1 ? J.S(latest, "title").NullIfEmpty() ?? "eero network notification" : fresh.Count + " eero network updates";
                    string body = (J.S(target, "name").NullIfEmpty() ?? "Your network") + ": " + (J.S(latest, "body").NullIfEmpty() ?? "Open eero Desktop to view the update.");
                    tray.BalloonTipTitle = title.Length > 63 ? title.Substring(0, 63) : title; tray.BalloonTipText = body.Length > 240 ? body.Substring(0, 240) : body; tray.ShowBalloonTip(5000);
                }
                notificationJournal.Save();
            }
            catch (ApiException e) { if (e.SessionExpired) { api.Forget(); ClearData(); ShowLogin(false); SetNotice("Sign in again to restore network alerts.", true); SignInAttention(); } }
            catch (Exception) { }
            finally { pollingNotifications = false; }
        }
        private void AppearancePage()
        {
            Sub("Choose how eero Desktop looks. This only changes this app on this Windows account.");
            AddGroupHeading(page, "Theme");
            var tiles = new Grid(); for (int c = 0; c < 3; c++) tiles.ColumnDefinitions.Add(new ColumnDefinition());
            string[] values = { "System", "Light", "Dark" }, labels = { "Match Windows", "Light", "Dark" }, notes = { "Follows your Windows light or dark setting", "Always light", "Always dark" };
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i]; bool chosen = UiPreferences.Theme == value;
                var content = new StackPanel();
                content.Children.Add(ThemeSample(value));
                var caption = new Grid { Margin = new Thickness(2, 12, 2, 0) }; caption.ColumnDefinitions.Add(new ColumnDefinition()); caption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var words = new StackPanel(); var name = Text(labels[i], 14, "#17263C", true); name.Margin = new Thickness(0); words.Children.Add(name);
                var note = Text(notes[i], 12, "#64738A"); note.Margin = new Thickness(0, 2, 0, 0); words.Children.Add(note); caption.Children.Add(words);
                if (chosen) { var check = Glyph("", 16, "#274CDB"); check.VerticalAlignment = VerticalAlignment.Top; Grid.SetColumn(check, 1); caption.Children.Add(check); }
                content.Children.Add(caption);
                var tile = new Button { Content = content, Padding = new Thickness(12), Margin = new Thickness(i == 0 ? 0 : 8, 0, i == 2 ? 0 : 8, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, Background = Surface,
                    BorderBrush = Brush(chosen ? "#274CDB" : "#E5EAF3"), BorderThickness = new Thickness(chosen ? 2 : 1), Cursor = System.Windows.Input.Cursors.Hand };
                System.Windows.Automation.AutomationProperties.SetName(tile, labels[i] + " theme" + (chosen ? ", selected" : ""));
                tile.Click += delegate { if (busy || UiPreferences.Theme == value) return; UiPreferences.Theme = value; UiPreferences.Save(); Program.ApplyTheme(); Background = Brush("#F4F6FA"); Foreground = Brush("#17263C"); ShowShell(); SetNotice("Theme changed to " + (value == "System" ? "match Windows." : value.ToLowerInvariant() + ".")); };
                Grid.SetColumn(tile, i); tiles.Children.Add(tile);
            }
            page.Children.Add(tiles);
            page.Children.Add(new Border { Height = 18 });
            Group("More", SettingRow("Text size and contrast", "Windows controls these for every app, including this one.", null, RowAction("Open Windows settings ↗", delegate { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:easeofaccess-display") { UseShellExecute = true }); })));
        }
        // A small picture of the app in a theme. "System" shows light and dark side by side.
        private static FrameworkElement ThemeSample(string theme)
        {
            Func<bool, FrameworkElement> half = dark =>
            {
                Func<string, SolidColorBrush> c = hex => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                var sample = new StackPanel { Margin = new Thickness(12) };
                sample.Children.Add(new TextBlock { Text = "eero", FontFamily = new FontFamily("Segoe UI Light"), FontSize = 20, Foreground = c(dark ? "#F3F3F5" : "#202445") });
                var tile = new Border { Background = c(dark ? "#272729" : "#FFFFFF"), BorderBrush = c(dark ? "#3D3D42" : "#E1E1E5"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(10, 8, 10, 10) };
                var lines = new StackPanel(); lines.Children.Add(new TextBlock { Text = "Internet", FontSize = 11, Foreground = c(dark ? "#F3F3F5" : "#202445") });
                lines.Children.Add(new Border { Background = c(dark ? "#82AAFF" : "#0057FF"), Width = 54, Height = 5, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) });
                lines.Children.Add(new Border { Background = c(dark ? "#3D3D42" : "#E1E1E5"), Width = 80, Height = 5, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) });
                tile.Child = lines; sample.Children.Add(tile);
                return new Border { Background = c(dark ? "#1C1C1E" : "#F3F3F3"), Child = sample };
            };
            var frame = new Grid { Height = 130, ClipToBounds = true };
            if (theme == "System")
            {
                frame.ColumnDefinitions.Add(new ColumnDefinition()); frame.ColumnDefinitions.Add(new ColumnDefinition());
                var light = half(false); frame.Children.Add(light); var dark = half(true); Grid.SetColumn(dark, 1); frame.Children.Add(dark);
            }
            else frame.Children.Add(half(theme == "Dark"));
            return new Border { CornerRadius = new CornerRadius(8), Child = frame, BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80808080")), BorderThickness = new Thickness(1), ClipToBounds = true };
        }
    }
}
