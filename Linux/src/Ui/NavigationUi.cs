using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gtk;

namespace EeroDesktop
{
    // Navigation aids: settings search, back history, grouped settings menus and inline toggles.
    public sealed partial class MainWindow
    {
        private readonly List<string> history = new List<string>();
        private bool shellInputHooked;
        private SearchEntry railSearch;
        private ListBox railResultList;
        private Button backButton;
        private readonly Dictionary<ListBoxRow, string> railResultRoutes = new Dictionary<ListBoxRow, string>();

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
        // ---- Shell input: Ctrl+F search, Alt+Left / mouse back, Ctrl+R refresh ----
        private void HookShellInput()
        {
            if (shellInputHooked) return;
            shellInputHooked = true;
            var keys = EventControllerKey.New(); keys.SetPropagationPhase(PropagationPhase.Capture);
            keys.OnKeyPressed += (sender, args) =>
            {
                if (root == null || root != split) return false;
                bool control = (args.State & Gdk.ModifierType.ControlMask) != 0, alt = (args.State & Gdk.ModifierType.AltMask) != 0;
                if (control && (args.Keyval == Gdk.Constants.KEY_f || args.Keyval == Gdk.Constants.KEY_F) && railSearch != null) { split.SetShowSidebar(true); railSearch.GrabFocus(); railSearch.SelectRegion(0, -1); return true; }
                if ((alt && args.Keyval == Gdk.Constants.KEY_Left) || args.Keyval == Gdk.Constants.KEY_Back) { GoBack(); return true; }
                if ((control && (args.Keyval == Gdk.Constants.KEY_r || args.Keyval == Gdk.Constants.KEY_R)) || args.Keyval == Gdk.Constants.KEY_F5) { if (!busy) RefreshAll(); return true; }
                return false;
            };
            Window.AddController(keys);
            var mouse = GestureClick.New(); mouse.SetButton(8); mouse.SetPropagationPhase(PropagationPhase.Capture);
            mouse.OnPressed += delegate { if (root == split) GoBack(); };
            Window.AddController(mouse);
        }

        private void GoBack()
        {
            if (busy || history.Count == 0 || root != split) return;
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
            backButton.SetSensitive(history.Count > 0);
            backButton.SetTooltipText(history.Count > 0 ? "Back to " + history[history.Count - 1].Split('/').Last() + " (Alt+Left)" : "Back (Alt+Left)");
        }

        private Button BackButton()
        {
            backButton = ToolButton("", "Back", GoBack);
            UpdateBackButton();
            return backButton;
        }

        // ---- Search boxes ----
        private static SearchEntry SearchField(string hint)
        {
            var input = SearchEntry.New(); input.SetPlaceholderText(hint); input.SetMaxWidthChars(48); input.Name(hint);
            return input;
        }

        private Widget RailSearch()
        {
            railSearch = SearchField("Search settings");
            railSearch.SetTooltipText("Search every page and setting (Ctrl+F)"); railSearch.Margin(12, 0, 12, 10);
            railSearch.OnSearchChanged += delegate { UpdateRailResults(); };
            railSearch.OnActivate += delegate { var first = railResultList == null ? null : railResultList.GetRowAtIndex(0); string route; if (first != null && railResultRoutes.TryGetValue(first, out route)) OpenSearchResult(route); };
            railSearch.OnStopSearch += delegate { CloseRailSearch(true); };
            return railSearch;
        }
        private Widget RailResultsView()
        {
            railResultList = ListBox.New(); railResultList.AddCssClass("navigation-sidebar"); railResultList.SetSelectionMode(SelectionMode.None);
            railResultList.OnRowActivated += (sender, args) => { string route; if (railResultRoutes.TryGetValue(args.Row, out route)) OpenSearchResult(route); };
            var scroll = ScrolledWindow.New(); scroll.SetPolicy(PolicyType.Never, PolicyType.Automatic); scroll.SetChild(railResultList); scroll.SetVexpand(true);
            return scroll;
        }

        private void UpdateRailResults()
        {
            if (railSearch == null || railResultList == null || navigationStack == null) return;
            string q = railSearch.GetText().Trim();
            railResultList.RemoveAll(); railResultRoutes.Clear();
            if (q.Length == 0) { navigationStack.SetVisibleChildName("menu"); return; }
            var found = SearchCatalog(q, null, 8);
            if (found.Count == 0)
            {
                var none = Text("No pages or settings match “" + q + "”. Try a broader word such as wifi, dns or guest.", 13, "#64738A"); none.Margin(10, 8, 10, 8);
                var row = ListBoxRow.New(); row.SetChild(none); row.SetActivatable(false); railResultList.Append(row);
            }
            foreach (FeatureEntry entry in found) railResultList.Append(SearchResultRow(entry));
            navigationStack.SetVisibleChildName("search");
        }

        private ListBoxRow SearchResultRow(FeatureEntry entry)
        {
            var row = ListBoxRow.New();
            var grid = HBox(10); grid.Margin(6, 6, 6, 6);
            grid.Append(Glyph(RouteIcon(entry.Path), 16, "#274CDB"));
            var words = VBox(); words.SetHexpand(true); var title = Text(entry.Title, 13, "#17263C", true); title.SetMarginBottom(0); words.Append(title);
            string badge = AvailabilityBadge(entry.Path);
            var where = NoWrap(Text(Breadcrumb(entry.Path) + (badge != null ? "  ·  " + badge : ""), 11, badge != null ? "#926410" : "#64738A"), 30); where.SetMarginBottom(0); words.Append(where);
            grid.Append(words); row.SetChild(grid);
            row.Name(entry.Title + ", " + Breadcrumb(entry.Path));
            railResultRoutes[row] = entry.Path;
            return row;
        }

        private void OpenSearchResult(string path)
        {
            CloseRailSearch(true);
            Navigate(path);
        }

        private void CloseRailSearch(bool clear)
        {
            if (clear && railSearch != null && railSearch.GetText().Length > 0) railSearch.SetText("");
            if (navigationStack != null && (clear || railSearch == null || railSearch.GetText().Trim().Length == 0)) navigationStack.SetVisibleChildName("menu");
        }

        // ---- Rail section header ----
        private void AddNavigationHeader(string title)
        {
            var label = Text(title.ToUpperInvariant(), 10, "#64738A", true);
            label.Margin(18, navigation.Count() == 0 ? 0 : 14, 0, 4);
            navigation.Append(label);
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
            if (path == "Settings/Appearance") return UiPreferences.Theme == "System" ? "System setting" : UiPreferences.Theme;
            if (path == "Settings/Notifications" || path == "Settings/Notifications/Desktop notifications") return UiPreferences.Notifications ? "Desktop alerts on" : "";
            int children = FeatureCatalog.Children(path).Count();
            return children >= 3 ? children + " settings" : "";
        }

        private Widget ListRow(FeatureEntry entry, bool showPath)
        {
            var row = Adw.ActionRow.New(); row.SetUseMarkup(false); row.SetTitle(entry.Title); row.SetActivatable(true);
            var tile = VBox(); tile.AddCssClass("tile"); tile.AddCssClass("bg-soft"); tile.SetValign(Gtk.Align.Center);
            var icon = Glyph(RouteIcon(entry.Path), 17, "#274CDB"); icon.SetVexpand(true); tile.Append(icon); tile.SetSizeRequest(36, 36);
            row.AddPrefix(tile);
            string caption = (showPath ? Breadcrumb(entry.Path) + "  ·  " : "") + (entry.Description ?? "");
            string why; string badge = AvailabilityBadge(entry.Path, out why);
            if (badge != null || !String.IsNullOrEmpty(entry.Limitation)) caption = (caption.Length > 0 ? caption + "\n" : "") + "⚠  " + (badge ?? "Limited support");
            if (caption.Length > 0) row.SetSubtitle(caption);
            if (why != null) row.SetTooltipText(badge + ". " + why);
            string value = RouteValue(entry.Path);
            if (!String.IsNullOrEmpty(value)) { var shown = Label.New(value); shown.AddCssClass("fs-12"); shown.AddCssClass("c-muted"); shown.SetEllipsize(Pango.EllipsizeMode.End); shown.SetMaxWidthChars(24); shown.SetValign(Gtk.Align.Center); row.AddSuffix(shown); }
            row.AddSuffix(Glyph("", 12));
            row.OnActivated += delegate { Navigate(entry.Path); };
            row.Name(entry.Title + (String.IsNullOrEmpty(value) ? "" : ", " + value));
            return row;
        }

        // A card of stacked rows separated by hairlines (libadwaita boxed list).
        private Widget RowGroup(IEnumerable<Widget> rows, double inset = 62)
        {
            var list = ListBox.New(); list.AddCssClass("boxed-list"); list.SetSelectionMode(SelectionMode.None); list.SetMarginBottom(18);
            foreach (Widget row in rows)
            {
                if (row == null) continue;
                if (row is ListBoxRow) { list.Append(row); continue; }
                var holder = ListBoxRow.New(); holder.SetActivatable(false); row.Margin(12, 8, 12, 8); holder.SetChild(row); list.Append(holder);
            }
            return list;
        }

        private void AddGroupHeading(Box host, string heading)
        {
            var label = Text(heading, 14, "#17263C", true); label.Margin(4, 6, 0, 8);
            host.Append(label);
        }

        private void NativeFeatureMenu(string parent, bool explanation)
        {
            if (explanation) Sub(parent == "Settings" ? "Manage your wifi, network, account and app preferences." : "Choose a setting to view details and make changes.");
            var entries = FeatureCatalog.Children(parent).ToList();
            if (entries.Count == 0) return;
            string[][] groups = MenuGroups(parent);
            var host = VBox();
            var grouped = VBox(); var results = VBox(); results.SetVisible(false);
            if (groups == null) grouped.Append(RowGroup(entries.Select(e => ListRow(e, false))));
            else
            {
                var placed = new HashSet<string>();
                foreach (string[] group in groups)
                {
                    var members = group.Skip(1).Select(t => entries.FirstOrDefault(e => e.Title == t)).Where(e => e != null).ToList();
                    if (members.Count == 0) continue;
                    foreach (FeatureEntry m in members) placed.Add(m.Path);
                    AddGroupHeading(grouped, group[0]);
                    grouped.Append(RowGroup(members.Select(e => ListRow(e, false))));
                }
                var rest = entries.Where(e => !placed.Contains(e.Path)).ToList();
                if (rest.Count > 0) { AddGroupHeading(grouped, "More"); grouped.Append(RowGroup(rest.Select(e => ListRow(e, false)))); }
            }
            if (groups != null || entries.Count > 6)
            {
                var filter = SearchField("Find a setting in " + parent.Split('/').Last());
                filter.SetMarginBottom(18); filter.SetSizeRequest(420, -1); filter.SetHalign(Gtk.Align.Start);
                host.Append(filter);
                filter.OnSearchChanged += delegate
                {
                    string q = filter.GetText().Trim(); results.Clear();
                    grouped.SetVisible(q.Length == 0); results.SetVisible(q.Length > 0);
                    if (q.Length == 0) return;
                    var found = SearchCatalog(q, parent, 40);
                    AddGroupHeading(results, found.Count == 0 ? "No matches" : found.Count + (found.Count == 1 ? " match" : " matches"));
                    if (found.Count == 0) results.Append(Card(Text("Nothing in " + parent.Split('/').Last() + " matches “" + q + "”. The sidebar search (Ctrl+F) looks across the whole app.", 13, "#64738A"), 18));
                    else results.Append(RowGroup(found.Select(e => ListRow(e, true))));
                };
                filter.OnStopSearch += delegate { filter.SetText(""); };
                filter.OnActivate += delegate { var first = SearchCatalog(filter.GetText().Trim(), parent, 1).FirstOrDefault(); if (first != null) Navigate(first.Path); };
            }
            host.Append(grouped); host.Append(results);
            page.Append(host);
        }

        // Child pages of the current page, shown as a compact related-settings list.
        private void RelatedPages(string heading = "Related settings")
        {
            var children = FeatureCatalog.Children(currentPage).ToList();
            if (children.Count == 0) return;
            AddGroupHeading(page, heading);
            page.Append(RowGroup(children.Select(e => ListRow(e, false))));
        }

        // Specific catalog pages shown as link rows under a "Related settings" heading.
        private void RelatedPagesFor(params string[] paths)
        {
            var rows = paths.Select(p => FeatureCatalog.Entries.FirstOrDefault(e => e.Path == p)).Where(e => e != null).Select(e => ListRow(e, false)).ToList();
            if (rows.Count == 0) return;
            AddGroupHeading(page, "Related settings");
            page.Append(RowGroup(rows));
        }

        // ---- Inline settings ----
        private Switch ToggleSwitch(bool value, string name)
        {
            var toggle = Switch.New(); toggle.SetActive(value); toggle.SetValign(Gtk.Align.Center); toggle.Name(name);
            return toggle;
        }

        // A row with a title, explanation and switch; the switch only changes after the save succeeds.
        private Adw.ActionRow SettingToggle(string title, string description, bool value, Func<bool, Task> apply)
        {
            var row = Adw.ActionRow.New(); row.SetUseMarkup(false); row.SetTitle(title);
            if (!String.IsNullOrEmpty(description)) row.SetSubtitle(description);
            var toggle = ToggleSwitch(value, title); row.AddSuffix(toggle); row.SetActivatableWidget(toggle);
            toggle.OnStateSet += (sender, args) =>
            {
                bool wanted = args.State;
                // Returning true keeps the switch where it is; a successful save re-renders the page with the new value.
                if (wanted != value && !busy) GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT, delegate { RunToggle(title, wanted, apply); return false; });
                GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT, delegate { toggle.SetActive(value); return false; });
                return true;
            };
            return row;
        }
        private async void RunToggle(string title, bool wanted, Func<bool, Task> apply)
        { await Run(async delegate { await apply(wanted); }, "Saving " + title.ToLowerInvariant() + "…"); }

        private void NetworkSettingToggles(IEnumerable<string[]> settings, string confirmation)
        {
            var rows = new List<Widget>();
            foreach (string[] setting in settings)
            {
                string key = setting[0], title = setting[1];
                if (!(J.Get(network, key) is bool)) continue;
                rows.Add(SettingToggle(title, setting[2], J.B(network, key), async delegate(bool on)
                {
                    if (!await Confirm((on ? "Turn on " : "Turn off ") + title + "? " + confirmation)) return;
                    await Change("PUT", NetResource("settings"), J.Obj(key, on), false, title + (on ? " turned on." : " turned off."));
                }));
            }
            if (rows.Count == 0) { page.Append(Card(Text("eero did not report these settings for the selected network.", 14, "#64738A"))); return; }
            page.Append(RowGroup(rows, 14));
        }

        // Label / value line used by detail cards.
        private static Widget DetailRow(string label, string value, bool flag)
        {
            var row = Grid.New(); row.SetColumnSpacing(12); row.AddCssClass("detail-row");
            var name = Text(label, 13, "#64738A"); name.Margin(0, 7, 0, 7); name.SetValign(Gtk.Align.Center); name.SetSizeRequest(160, -1);
            row.Attach(name, 0, 0, 1, 1);
            Label shown;
            if (flag)
            {
                // Read-only state: a dot and a word, so it is not mistaken for a switch.
                bool on = value == "On";
                shown = Text((on ? "●  " : "○  ") + value, 13, on ? "#0D7959" : "#64738A");
            }
            else { shown = Text(value, 13); shown.SetSelectable(true); }
            shown.Margin(0, 7, 0, 7); shown.SetValign(Gtk.Align.Center); shown.SetHexpand(true);
            row.Attach(shown, 1, 0, 1, 1);
            return row;
        }
    }
}
