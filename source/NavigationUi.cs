using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace EeroDesktop
{
    // Navigation aids: settings search, back history, grouped settings menus and inline toggles.
    public sealed partial class MainWindow
    {
        private readonly List<string> history = new List<string>();
        private bool shellInputHooked;
        private TextBox railSearch;
        private TextBlock railHint;
        private Popup railResults;
        private StackPanel railResultList;
        private Button backButton;

        // First item of each array is the group heading; the rest are child page titles.
        internal static readonly string[][] SettingsGroups = {
            new[] { "Wifi", "Wifi name & password", "Guest wifi network", "Additional wifi networks", "Business guest wifi" },
            new[] { "Network", "Nickname & location", "Advanced networking", "Software updates", "Connected services", "Network management" },
            new[] { "People & account", "User permissions", "Account settings" },
            new[] { "This app", "Appearance", "Notifications", "About" },
            new[] { "Help", "Troubleshooting" }
        };
        internal static readonly string[][] AdvancedGroups = {
            new[] { "Internet & addressing", "Internet connection", "DHCP & NAT", "DNS", "IPv6", "Dynamic DNS", "Multiple static IPs" },
            new[] { "Reliability", "Internet backup", "Multi-WAN" },
            new[] { "Wireless & smart home", "Wireless settings", "Thread", "Power saving" },
            new[] { "Ports & access", "Reservations & port forwarding", "UPnP" },
            new[] { "Expert", "Network flags" }
        };
        private static readonly FeatureEntry[] TopPages = {
            new FeatureEntry("Home", "Network overview, internet status, speed and your eeros", "Shell"),
            new FeatureEntry("Devices", "Every connected device, its IP and MAC address, pause and block", "Shell"),
            new FeatureEntry("Activity", "Data usage, security activity and network events", "Shell"),
            new FeatureEntry("Settings", "Wifi, network, account and app preferences", "Shell")
        };

        private static string[][] MenuGroups(string parent)
        { return parent == "Settings" ? SettingsGroups : parent == "Settings/Advanced networking" ? AdvancedGroups : null; }

        // Titles of catalog children that a grouped menu does not place; kept empty by the self-tests.
        internal static List<string> UngroupedMenuEntries()
        {
            var missing = new List<string>();
            foreach (string parent in new[] { "Settings", "Settings/Advanced networking" })
            {
                var grouped = MenuGroups(parent).SelectMany(g => g.Skip(1)).ToList();
                missing.AddRange(FeatureCatalog.Children(parent).Where(e => !grouped.Contains(e.Title)).Select(e => e.Path));
                missing.AddRange(grouped.Where(t => !FeatureCatalog.Children(parent).Any(e => e.Title == t)).Select(t => parent + "/" + t + " (stale)"));
            }
            return missing;
        }

        internal static List<FeatureEntry> SearchCatalog(string query, string within, int limit)
        {
            string[] words = (query ?? "").ToLowerInvariant().Split(new[] { ' ', '/', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return new List<FeatureEntry>();
            return TopPages.Concat(FeatureCatalog.Entries)
                .Where(e => within == null || e.Path.StartsWith(within + "/", StringComparison.Ordinal))
                .Select(e => new { Entry = e, Score = SearchScore(e, words) }).Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score).ThenBy(x => x.Entry.Path.Count(c => c == '/')).ThenBy(x => x.Entry.Title, StringComparer.OrdinalIgnoreCase)
                .Take(limit).Select(x => x.Entry).ToList();
        }
        private static int SearchScore(FeatureEntry e, string[] words)
        {
            string title = e.Title.ToLowerInvariant(), path = e.Path.ToLowerInvariant(), description = (e.Description ?? "").ToLowerInvariant();
            int total = 0;
            foreach (string w in words)
            {
                int s = title == w ? 10 : title.StartsWith(w) ? 7 : title.Contains(w) ? 5 : path.Contains(w) ? 3 : description.Contains(w) ? 1 : 0;
                if (s == 0) return 0;
                total += s;
            }
            return total;
        }
        private static string Breadcrumb(string path)
        {
            int i = path.LastIndexOf('/');
            return i < 0 ? "Page" : path.Substring(0, i).Replace("/", " › ");
        }

        // ---- Shell input: Ctrl+F search, Alt+Left / mouse back ----
        private void HookShellInput()
        {
            if (shellInputHooked) return;
            shellInputHooked = true;
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (navigation == null || !navigation.IsVisible) return;
                if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && railSearch != null) { railSearch.Focus(); railSearch.SelectAll(); e.Handled = true; }
                else if ((e.Key == Key.System && e.SystemKey == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt) || e.Key == Key.BrowserBack) { GoBack(); e.Handled = true; }
            };
            PreviewMouseDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ChangedButton == MouseButton.XButton1 && navigation != null && navigation.IsVisible) { GoBack(); e.Handled = true; return; }
                if (railResults != null && railResults.IsOpen && railSearch != null && !railSearch.IsMouseOver) railResults.IsOpen = false;
            };
            Deactivated += delegate { CloseRailSearch(false); };
            LocationChanged += delegate { CloseRailSearch(false); };
            SizeChanged += delegate { CloseRailSearch(false); };
        }

        private void GoBack()
        {
            if (busy || history.Count == 0 || navigation == null || !navigation.IsVisible) return;
            string target = history[history.Count - 1]; history.RemoveAt(history.Count - 1);
            NavigateTo(target, false);
        }

        private void RecordHistory(string route)
        {
            if (route == currentPage) return;
            history.Add(currentPage);
            if (history.Count > 50) history.RemoveAt(0);
        }

        private void UpdateBackButton()
        {
            if (backButton == null) return;
            backButton.IsEnabled = history.Count > 0;
            backButton.ToolTip = history.Count > 0 ? "Back to " + history[history.Count - 1].Split('/').Last() + " (Alt+Left)" : "Back (Alt+Left)";
        }

        private Button BackButton()
        {
            backButton = ToolButton("", "Back", GoBack);
            backButton.Margin = new Thickness(0, 0, 12, 0);
            UpdateBackButton();
            return backButton;
        }

        // ---- Search boxes ----
        private Grid SearchField(string hint, out TextBox box, out TextBlock placeholder)
        {
            var host = new Grid();
            var input = new TextBox { MaxLength = 80, Padding = new Thickness(34, 0, 11, 0), Margin = new Thickness(0), Height = 36, MinHeight = 36, VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(input, hint);
            host.Children.Add(input);
            var icon = Glyph("", 14); icon.HorizontalAlignment = HorizontalAlignment.Left; icon.Margin = new Thickness(12, 0, 0, 0); icon.IsHitTestVisible = false; host.Children.Add(icon);
            var label = Text(hint, 13, "#64738A"); label.Margin = new Thickness(34, 0, 12, 0); label.VerticalAlignment = VerticalAlignment.Center; label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis; label.IsHitTestVisible = false; host.Children.Add(label);
            input.TextChanged += delegate { label.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            box = input; placeholder = label;
            return host;
        }

        private UIElement RailSearch()
        {
            var field = SearchField("Search settings", out railSearch, out railHint);
            field.Margin = new Thickness(12, 0, 12, 14);
            railSearch.ToolTip = "Search every page and setting (Ctrl+F)";
            railResultList = new StackPanel();
            railResults = new Popup
            {
                PlacementTarget = railSearch, Placement = PlacementMode.Bottom, VerticalOffset = 4, StaysOpen = true, AllowsTransparency = true, Width = 380,
                Child = new Border { Background = Surface, BorderBrush = Brush("#E5EAF3"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(6),
                    Child = new ScrollViewer { MaxHeight = 440, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = railResultList } }
            };
            railSearch.TextChanged += delegate { UpdateRailResults(); };
            railSearch.GotKeyboardFocus += delegate { if (railSearch.Text.Trim().Length > 0) UpdateRailResults(); };
            railSearch.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                var first = railResultList.Children.OfType<Button>().FirstOrDefault();
                if (e.Key == Key.Down && first != null && railResults.IsOpen) { first.Focus(); e.Handled = true; }
                else if (e.Key == Key.Enter && first != null && railResults.IsOpen) { OpenSearchResult((string)first.Tag); e.Handled = true; }
                else if (e.Key == Key.Escape) { CloseRailSearch(true); e.Handled = true; }
            };
            return field;
        }

        private void UpdateRailResults()
        {
            if (railSearch == null) return;
            string q = railSearch.Text.Trim();
            railResultList.Children.Clear();
            if (q.Length == 0) { railResults.IsOpen = false; return; }
            var found = SearchCatalog(q, null, 8);
            if (found.Count == 0)
            {
                var none = Text("No pages or settings match “" + q + "”. Try a broader word such as wifi, dns or guest.", 13, "#64738A"); none.Margin = new Thickness(10, 8, 10, 8); railResultList.Children.Add(none);
            }
            foreach (FeatureEntry entry in found) railResultList.Children.Add(SearchResultRow(entry));
            railResults.IsOpen = true;
        }

        private Button SearchResultRow(FeatureEntry entry)
        {
            var b = LocalButton(entry.Title, delegate { OpenSearchResult(entry.Path); });
            b.Style = (Style)FindResource("ListRowButton"); b.MinHeight = 48; b.Padding = new Thickness(10, 8, 10, 8); b.Tag = entry.Path;
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = Glyph(RouteIcon(entry.Path), 16, "#274CDB"); icon.HorizontalAlignment = HorizontalAlignment.Left; grid.Children.Add(icon);
            var words = new StackPanel(); var title = Text(entry.Title, 13, "#17263C", true); title.Margin = new Thickness(0); words.Children.Add(title);
            string badge = AvailabilityBadge(entry.Path);
            var where = Text(Breadcrumb(entry.Path) + (badge != null ? "  ·  " + badge : ""), 11, badge != null ? "#926410" : "#64738A"); where.Margin = new Thickness(0, 2, 0, 0); where.TextWrapping = TextWrapping.NoWrap; where.TextTrimming = TextTrimming.CharacterEllipsis; words.Children.Add(where);
            Grid.SetColumn(words, 1); grid.Children.Add(words); b.Content = grid;
            b.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Down) { b.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)); e.Handled = true; }
                else if (e.Key == Key.Up)
                {
                    if (railResultList.Children.OfType<Button>().FirstOrDefault() == b) railSearch.Focus();
                    else b.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape) { railSearch.Focus(); railResults.IsOpen = false; e.Handled = true; }
            };
            AutomationProperties.SetName(b, entry.Title + ", " + Breadcrumb(entry.Path));
            return b;
        }

        private void OpenSearchResult(string path)
        {
            CloseRailSearch(true);
            Navigate(path);
        }

        private void CloseRailSearch(bool clear)
        {
            if (railResults != null) railResults.IsOpen = false;
            if (clear && railSearch != null && railSearch.Text.Length > 0) railSearch.Text = "";
        }

        // ---- Rail section header ----
        private void AddNavigationHeader(string title)
        {
            var label = Text(title.ToUpperInvariant(), 10, "#64738A", true);
            label.Margin = new Thickness(10, navigation.Children.Count == 0 ? 0 : 14, 0, 6);
            navigation.Children.Add(label);
        }

        // ---- Grouped settings menus ----
        private string RouteValue(string path)
        {
            if (network != null)
            {
                switch (path)
                {
                    case "Settings/Wifi name & password": return J.S(network, "name");
                    case "Settings/Nickname & location": return J.S(network, "timezone", "value");
                    case "Settings/Advanced networking/IPv6": return J.Get(network, "ipv6_upstream") is bool ? (J.B(network, "ipv6_upstream") ? "On" : "Off") : "";
                    case "Settings/Advanced networking/UPnP": return J.Get(network, "upnp") is bool ? (J.B(network, "upnp") ? "On" : "Off") : "";
                    case "Settings/Advanced networking/DNS": { string mode = J.S(network, "dns", "mode"); return mode == "custom" ? "Custom" : mode.Length > 0 ? "Automatic" : ""; }
                    case "Settings/Advanced networking/Internet connection": { string mode = J.S(network, "lease", "mode"); return mode == "static" ? "Static IP" : mode == "dhcp" ? "Automatic" : ""; }
                    case "Settings/Advanced networking/Internet connection/ISP settings": return J.Get(network, "pppoe_enabled") is bool ? (J.B(network, "pppoe_enabled") ? "PPPoE on" : "Not used") : "";
                    case "Settings/Advanced networking/Dynamic DNS": return J.Get(network, "dynamic_dns", "enabled") is bool ? OnOff(J.B(network, "dynamic_dns", "enabled")) : "";
                    case "Settings/Advanced networking/Power saving": return J.Get(network, "power_saving") is bool ? OnOff(J.B(network, "power_saving")) : "";
                    case "Settings/Advanced networking/Thread": return J.Get(network, "thread") is bool ? OnOff(J.B(network, "thread")) : "";
                    case "Settings/Advanced networking/DHCP & NAT": return J.S(network, "connection", "mode").Length > 0 ? DhcpModeLabel() : "";
                }
            }
            if (path == "Settings/Appearance") return UiPreferences.Theme == "System" ? "Windows setting" : UiPreferences.Theme;
            if (path == "Settings/Notifications" || path == "Settings/Notifications/Windows notifications") return UiPreferences.Notifications ? "Windows alerts on" : "";
            int children = FeatureCatalog.Children(path).Count();
            return children >= 3 ? children + " settings" : "";
        }

        private Button ListRow(FeatureEntry entry, bool showPath)
        {
            var b = LocalButton(entry.Title, delegate { Navigate(entry.Path); });
            b.Style = (Style)FindResource("ListRowButton");
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            var tile = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(8), Background = Brush("#EEF3FF"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Child = Glyph(RouteIcon(entry.Path), 17, "#274CDB") };
            grid.Children.Add(tile);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = Text(entry.Title, 14, "#17263C", true); title.Margin = new Thickness(0); words.Children.Add(title);
            string caption = (showPath ? Breadcrumb(entry.Path) + "  ·  " : "") + (entry.Description ?? "");
            if (caption.Length > 0) { var sub = Text(caption, 12, "#64738A"); sub.Margin = new Thickness(0, 3, 12, 0); words.Children.Add(sub); }
            string why; string badge = AvailabilityBadge(entry.Path, out why);
            if (badge != null || !String.IsNullOrEmpty(entry.Limitation))
            {
                var note = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
                if (badge != null) { var mark = Glyph("", 11, "#926410"); mark.Margin = new Thickness(0, 0, 5, 0); note.Children.Add(mark); }
                var limited = Text(badge ?? "Limited support", 11, "#926410", true); limited.Margin = new Thickness(0); note.Children.Add(limited);
                words.Children.Add(note);
                if (why != null) b.ToolTip = badge + ". " + why;
            }
            Grid.SetColumn(words, 1); grid.Children.Add(words);
            string value = RouteValue(entry.Path);
            if (!String.IsNullOrEmpty(value))
            {
                var shown = Text(value, 12, "#64738A"); shown.Margin = new Thickness(8, 0, 8, 0); shown.VerticalAlignment = VerticalAlignment.Center; shown.TextWrapping = TextWrapping.NoWrap; shown.TextTrimming = TextTrimming.CharacterEllipsis; shown.MaxWidth = 200;
                Grid.SetColumn(shown, 2); grid.Children.Add(shown);
            }
            var chevron = Glyph("", 12); Grid.SetColumn(chevron, 3); grid.Children.Add(chevron);
            b.Content = grid;
            AutomationProperties.SetName(b, entry.Title + (String.IsNullOrEmpty(value) ? "" : ", " + value));
            return b;
        }

        // A card of stacked rows separated by hairlines.
        private Border RowGroup(IEnumerable<UIElement> rows, double inset = 62)
        {
            var panel = new StackPanel(); int count = 0;
            foreach (UIElement row in rows)
            {
                if (count++ > 0) panel.Children.Add(new Border { Height = 1, Background = Brush("#E5EAF3"), Margin = new Thickness(inset, 0, 10, 0) });
                panel.Children.Add(row);
            }
            var card = Card(panel, 4); card.Margin = new Thickness(0, 0, 0, 18);
            return card;
        }

        private void AddGroupHeading(Panel host, string heading)
        {
            var label = Text(heading, 14, "#17263C", true); label.Margin = new Thickness(4, 4, 0, 8);
            host.Children.Add(label);
        }

        private void NativeFeatureMenu(string parent, bool explanation)
        {
            if (explanation) Sub(parent == "Settings" ? "Manage your wifi, network, account and app preferences." : "Choose a setting to view details and make changes.");
            var entries = FeatureCatalog.Children(parent).ToList();
            if (entries.Count == 0) return;
            string[][] groups = MenuGroups(parent);
            var host = new StackPanel();
            var grouped = new StackPanel(); var results = new StackPanel { Visibility = Visibility.Collapsed };
            if (groups == null) grouped.Children.Add(RowGroup(entries.Select(e => (UIElement)ListRow(e, false))));
            else
            {
                var placed = new HashSet<string>();
                foreach (string[] group in groups)
                {
                    var members = group.Skip(1).Select(t => entries.FirstOrDefault(e => e.Title == t)).Where(e => e != null).ToList();
                    if (members.Count == 0) continue;
                    foreach (FeatureEntry m in members) placed.Add(m.Path);
                    AddGroupHeading(grouped, group[0]);
                    grouped.Children.Add(RowGroup(members.Select(e => (UIElement)ListRow(e, false))));
                }
                var rest = entries.Where(e => !placed.Contains(e.Path)).ToList();
                if (rest.Count > 0) { AddGroupHeading(grouped, "More"); grouped.Children.Add(RowGroup(rest.Select(e => (UIElement)ListRow(e, false)))); }
            }
            if (groups != null || entries.Count > 6)
            {
                TextBox filter; TextBlock hint;
                var field = SearchField("Find a setting in " + parent.Split('/').Last(), out filter, out hint);
                field.Margin = new Thickness(0, 0, 0, 18); field.Width = 420; field.HorizontalAlignment = HorizontalAlignment.Left;
                host.Children.Add(field);
                filter.TextChanged += delegate
                {
                    string q = filter.Text.Trim(); results.Children.Clear();
                    grouped.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                    results.Visibility = q.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                    if (q.Length == 0) return;
                    var found = SearchCatalog(q, parent, 40);
                    AddGroupHeading(results, found.Count == 0 ? "No matches" : found.Count + (found.Count == 1 ? " match" : " matches"));
                    if (found.Count == 0) results.Children.Add(Card(Text("Nothing in " + parent.Split('/').Last() + " matches “" + q + "”. The sidebar search (Ctrl+F) looks across the whole app.", 13, "#64738A"), 18));
                    else results.Children.Add(RowGroup(found.Select(e => (UIElement)ListRow(e, true))));
                };
                filter.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.Key == Key.Escape) { filter.Text = ""; e.Handled = true; }
                    else if (e.Key == Key.Enter) { var first = SearchCatalog(filter.Text.Trim(), parent, 1).FirstOrDefault(); if (first != null) { e.Handled = true; Navigate(first.Path); } }
                };
            }
            host.Children.Add(grouped); host.Children.Add(results);
            page.Children.Add(host);
        }

        // Child pages of the current page, shown as a compact related-settings list.
        private void RelatedPages(string heading = "Related settings")
        {
            var children = FeatureCatalog.Children(currentPage).ToList();
            if (children.Count == 0) return;
            AddGroupHeading(page, heading);
            page.Children.Add(RowGroup(children.Select(e => (UIElement)ListRow(e, false))));
        }

        // Specific catalog pages shown as link rows under a "Related settings" heading.
        private void RelatedPagesFor(params string[] paths)
        {
            var rows = paths.Select(p => FeatureCatalog.Entries.FirstOrDefault(e => e.Path == p)).Where(e => e != null).Select(e => (UIElement)ListRow(e, false)).ToList();
            if (rows.Count == 0) return;
            AddGroupHeading(page, "Related settings");
            page.Children.Add(RowGroup(rows));
        }

        // ---- Inline settings ----
        private CheckBox ToggleSwitch(bool value, string name)
        {
            var toggle = new CheckBox { Style = (Style)FindResource("ToggleSwitch"), IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(toggle, name);
            return toggle;
        }

        // A row with a title, explanation and switch; the switch only changes after the save succeeds.
        private Grid SettingToggle(string title, string description, bool value, Func<bool, Task> apply)
        {
            var row = new Grid { MinHeight = 60, Margin = new Thickness(14, 10, 14, 10), Background = Brushes.Transparent };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var label = Text(title, 14, "#17263C", true); label.Margin = new Thickness(0); words.Children.Add(label);
            if (!String.IsNullOrEmpty(description)) { var sub = Text(description, 12, "#64738A"); sub.Margin = new Thickness(0, 3, 16, 0); words.Children.Add(sub); }
            row.Children.Add(words);
            var toggle = ToggleSwitch(value, title); toggle.Margin = new Thickness(16, 0, 0, 0); Grid.SetColumn(toggle, 1); row.Children.Add(toggle);
            toggle.Click += async delegate
            {
                bool wanted = toggle.IsChecked == true;
                toggle.IsChecked = !wanted;
                if (busy) return;
                await Run(async delegate { await apply(wanted); }, "Saving " + title.ToLowerInvariant() + "…");
            };
            return row;
        }

        private void NetworkSettingToggles(IEnumerable<string[]> settings, string confirmation)
        {
            var rows = new List<UIElement>();
            foreach (string[] setting in settings)
            {
                string key = setting[0], title = setting[1];
                if (!(J.Get(network, key) is bool)) continue;
                rows.Add(SettingToggle(title, setting[2], J.B(network, key), async delegate(bool on)
                {
                    if (!Confirm((on ? "Turn on " : "Turn off ") + title + "? " + confirmation)) return;
                    await Change("PUT", NetResource("settings"), J.Obj(key, on), false, title + (on ? " turned on." : " turned off."));
                }));
            }
            if (rows.Count == 0) { page.Children.Add(Card(Text("eero did not report these settings for the selected network.", 14, "#64738A"))); return; }
            page.Children.Add(RowGroup(rows, 14));
        }

        // Label / value line used by detail cards.
        private static FrameworkElement DetailRow(string label, string value, bool flag)
        {
            var row = new Grid { MinHeight = 34 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star), MinWidth = 120 }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
            var name = Text(label, 13, "#64738A"); name.Margin = new Thickness(0, 7, 12, 7); name.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(name);
            FrameworkElement shown;
            if (flag)
            {
                // Read-only state: a dot and a word, so it is not mistaken for a switch.
                bool on = value == "On";
                var text = Text((on ? "●  " : "○  ") + value, 13, on ? "#0D7959" : "#64738A"); text.Margin = new Thickness(0, 7, 0, 7); text.VerticalAlignment = VerticalAlignment.Center;
                shown = text;
            }
            else { var text = Text(value, 13); text.Margin = new Thickness(0, 7, 0, 7); text.VerticalAlignment = VerticalAlignment.Center; shown = text; }
            Grid.SetColumn(shown, 1); row.Children.Add(shown);
            return new Border { BorderBrush = Brush("#E5EAF3"), BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
        }
    }
}
