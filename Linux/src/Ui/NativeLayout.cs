using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gtk;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private ScrolledWindow pageScroll;
        private string renderedRoute;
        private Adw.OverlaySplitView split;
        private readonly Dictionary<ListBoxRow, string> navigationRoutes = new Dictionary<ListBoxRow, string>();
        private ListBox navigationList;
        private bool updatingNavigation;

        private static string RouteIcon(string route)
        {
            string title = route.Split('/').Last().ToLowerInvariant();
            switch (title)
            {
                case "guest wifi network": return "";
                case "nickname & location": return "";
                case "dhcp & nat": return "";
                case "dns": case "ipv6": case "internet connection": case "isp settings": return "";
                case "dynamic dns": case "connected services": case "reservations & port forwarding": return "";
                case "upnp": case "multi-wan": return "";
                case "power saving": return "";
                case "network flags": return "";
                case "user permissions": return "";
                case "troubleshooting": case "guided diagnostics": return "";
                case "trusted certificates": return "";
            }
            if (title.Contains("password") || title.Contains("wifi") || title.Contains("wireless")) return "";
            if (title.Contains("profile") || title.Contains("permissions") || title.Contains("account")) return "";
            if (title.Contains("security") || title.Contains("filter") || title.Contains("protection") || title.Contains("vpn")) return "";
            if (title.Contains("internet") || title.Contains("wan")) return "";
            if (title.Contains("activity") || title.Contains("usage") || title.Contains("analytics")) return "";
            if (title.Contains("eero") || title.Contains("device") || title.Contains("setup")) return "";
            if (title.Contains("notification")) return "";
            if (title.Contains("appearance")) return "";
            if (title.Contains("update")) return "";
            if (title.Contains("help") || title.Contains("support") || title.Contains("troubleshoot")) return "";
            if (title.Contains("about")) return "";
            if (title.Contains("chat")) return "";
            if (title.Contains("dns") || title.Contains("dhcp") || title.Contains("network")) return "";
            return "";
        }

        private Button ToolButton(string icon, string label, System.Action action)
        {
            var b = Gtk.Button.NewFromIconName(Icons.Name(icon) ?? "image-missing-symbolic"); b.SetTooltipText(label); b.Name(label);
            b.OnClicked += delegate { if (!busy) action(); };
            return b;
        }

        private void AddNavigation(string title, string route, string icon)
        {
            var row = ListBoxRow.New();
            var content = HBox(12); content.Margin(6, 8, 6, 8);
            content.Append(Glyph(icon, 16, "#17263C"));
            var label = Label.New(title); label.SetXalign(0); label.SetHexpand(true); content.Append(label);
            row.SetChild(content); row.Name(title);
            navigationRoutes[row] = route; navigationList.Append(row);
        }

        private void UpdateNavigation()
        {
            if (navigationList == null) { UpdateBackButton(); return; }
            string selected = currentPage.StartsWith("Home/Profiles") ? "Home/Profiles" : currentPage.StartsWith("Home/Security & privacy") ? "Home/Security & privacy" : FeatureCatalog.Root(currentPage);
            updatingNavigation = true;
            var match = navigationRoutes.FirstOrDefault(p => p.Value == selected).Key;
            foreach (ListBox list in navigationLists)
            {
                bool owns = match != null && match.GetParent() == list;
                if (owns) list.SelectRow(match); else list.UnselectAll();
            }
            updatingNavigation = false;
            UpdateBackButton();
        }

        private void NativeShell()
        {
            navigationRoutes.Clear();
            split = Adw.OverlaySplitView.New(); split.SetMinSidebarWidth(212); split.SetMaxSidebarWidth(260); split.SetSidebarWidthFraction(0.2);
            root = split; controls = split;

            // Sidebar: brand, search, navigation and the account footer.
            var rail = VBox(); rail.SetVexpand(true);
            var brand = VBox(); brand.Margin(18, 4, 0, 14);
            var wordmark = Text("eero", 38); wordmark.AddCssClass("brand"); wordmark.SetMarginBottom(0); brand.Append(wordmark);
            brand.Append(Text("Desktop for Linux", 11, "#64738A")); rail.Append(brand);
            rail.Append(RailSearch());
            navigation = VBox();
            var homeList = SidebarList(); var manageList = SidebarList();
            AddNavigationHeader("Your network");
            navigationList = homeList;
            AddNavigation("Home", "Home", ""); AddNavigation("Devices", "Devices", ""); AddNavigation("Profiles", "Home/Profiles", ""); AddNavigation("Activity", "Activity", ""); AddNavigation("Security & privacy", "Home/Security & privacy", "");
            navigation.Append(homeList);
            AddNavigationHeader("Manage");
            navigationList = manageList;
            AddNavigation("Settings", "Settings", ""); AddNavigation("Help & support", "Help", "");
            navigation.Append(manageList);
            navigationList = homeList;
            // Two lists share one selection: selecting a row in one clears the other.
            foreach (var pair in new[] { new[] { homeList, manageList }, new[] { manageList, homeList } })
            {
                ListBox list = pair[0], other = pair[1];
                list.OnRowSelected += (sender, args) =>
                {
                    if (updatingNavigation || args.Row == null) return;
                    updatingNavigation = true; other.UnselectAll(); updatingNavigation = false;
                    string route; if (navigationRoutes.TryGetValue(args.Row, out route) && route != currentPage) Navigate(route);
                };
                list.OnRowActivated += (sender, args) => { string route; if (navigationRoutes.TryGetValue(args.Row, out route) && route != currentPage) Navigate(route); };
            }
            navigationLists = new[] { homeList, manageList };
            var navigationScroll = ScrolledWindow.New(); navigationScroll.SetPolicy(PolicyType.Never, PolicyType.Automatic); navigationScroll.SetChild(navigation); navigationScroll.SetVexpand(true);
            navigationStack = Stack.New(); navigationStack.AddNamed(navigationScroll, "menu"); navigationStack.AddNamed(RailResultsView(), "search"); navigationStack.SetVexpand(true);
            rail.Append(navigationStack);
            var accountPanel = VBox(); accountPanel.Margin(12, 8, 12, 12);
            var accountButton = LocalButton("Account settings", delegate { Navigate("Settings/Account settings"); }); accountButton.AddCssClass("flat");
            var accountRow = HBox(10); accountRow.Append(Glyph("", 16));
            accountLabel = NoWrap(Text(preview ? "Preview account" : J.First(account, "name", "email", "phone"), 12), 24); accountLabel.SetMarginBottom(0); accountLabel.SetHexpand(true); accountRow.Append(accountLabel); accountButton.SetChild(accountRow); accountButton.Name("Account settings"); accountPanel.Append(accountButton);
            var signout = Button(preview ? "Back to sign in" : "Sign out", async delegate { timer.Stop(); CloseAccountWindows(); if (!preview) await api.Logout(); preview = false; ClearData(); ShowLogin(false); SetNotice("Signed out. Your saved session has been cleared."); });
            Quiet(signout); signout.SetHalign(Gtk.Align.Start); signout.SetMarginBottom(6); accountPanel.Append(signout);
            accountPanel.Append(Text("Unofficial client · v1.1", 10, "#64738A"));
            rail.Append(accountPanel);
            var sidebarHeader = Adw.HeaderBar.New(); sidebarHeader.SetShowTitle(false);
            var sidebarView = Adw.ToolbarView.New(); sidebarView.AddTopBar(sidebarHeader); sidebarView.SetContent(rail);
            split.SetSidebar(sidebarView);

            // Content: header with back, network picker and tools; the page; a status line.
            var header = Adw.HeaderBar.New(); header.SetShowTitle(false);
            var toggleSidebar = Gtk.ToggleButton.New(); toggleSidebar.SetIconName("sidebar-show-symbolic"); toggleSidebar.SetTooltipText("Show sidebar"); toggleSidebar.SetActive(true);
            toggleSidebar.OnToggled += delegate { split.SetShowSidebar(toggleSidebar.GetActive()); };
            split.OnNotify += (sender, args) => { if (args.Pspec.GetName() == "show-sidebar" && toggleSidebar.GetActive() != split.GetShowSidebar()) toggleSidebar.SetActive(split.GetShowSidebar()); };
            header.PackStart(toggleSidebar);
            header.PackStart(BackButton());
            var networkRow = HBox(10); networkRow.Append(Glyph("", 18, "#274CDB"));
            networkChoices = networkList.Select(n => new NetworkChoice { Name = J.First(n, "name", "url"), Url = J.S(n, "url") }).ToList();
            networks = DropDown.NewFromStrings(networkChoices.Select(c => c.Name).DefaultIfEmpty("No network").ToArray()); networks.SetSizeRequest(230, -1); networks.Name("Selected network");
            int selectedIndex = networkChoices.FindIndex(c => c.Url == networkUrl); if (selectedIndex >= 0) networks.SetSelected((uint)selectedIndex);
            networkRow.Append(networks);
            var connection = Text(preview ? "Sample network" : "eero network", 12, "#64738A"); connection.SetMarginBottom(0); connection.SetValign(Gtk.Align.Center); networkRow.Append(connection);
            header.PackStart(networkRow);
            networks.OnNotify += async (sender, args) =>
            {
                if (args.Pspec.GetName() != "selected" || busy || loading) return;
                int index = (int)networks.GetSelected(); if (index < 0 || index >= networkChoices.Count) return;
                var choice = networkChoices[index]; if (networkUrl == choice.Url) return;
                await Run(async delegate { CloseAccountWindows(); history.Clear(); networkUrl = choice.Url; network = selectedProfile = selectedNode = null; currentPage = "Home"; devices.Clear(); nodes.Clear(); profiles.Clear(); featureData.Clear(); notificationEpoch++; if (!preview) await LoadNetwork(); RenderPage(); }, "Loading network…");
            };
            var add = ToolButton("", "Add or replace eero", delegate { Navigate("Home/Add or replace eero"); });
            var bell = ToolButton("", "Notifications", delegate { Navigate("Settings/Notifications"); });
            var refresh = Gtk.Button.NewFromIconName("view-refresh-symbolic"); refresh.SetTooltipText("Refresh network (Ctrl+R)"); refresh.Name("Refresh network");
            refresh.OnClicked += async delegate { await RefreshAll(); };
            header.PackEnd(add); header.PackEnd(bell); header.PackEnd(refresh);
            pageScroll = ScrolledWindow.New(); pageScroll.SetPolicy(PolicyType.Never, PolicyType.Automatic); pageScroll.SetVexpand(true);
            page = VBox(); page.Margin(28, 10, 28, 24);
            var clamp = Adw.Clamp.New(); clamp.SetMaximumSize(1240); clamp.SetTighteningThreshold(900); clamp.SetChild(page);
            pageScroll.SetChild(clamp);
            notice = Text("", 11, "#64738A"); notice.SetMarginBottom(0);
            var statusBar = VBox(); statusBar.AddCssClass("status-bar"); statusBar.Append(notice);
            var content = Adw.ToolbarView.New(); content.AddTopBar(header); content.SetContent(pageScroll); content.AddBottomBar(statusBar);
            split.SetContent(content);
            // Collapse the sidebar into an overlay on narrow windows.
            var breakpoint = Adw.Breakpoint.New(Adw.BreakpointCondition.Parse("max-width: 820sp"));
            breakpoint.AddSetter(split, "collapsed", new GObject.Value(true));
            Window.AddBreakpoint(breakpoint);
            Window.SetContent(split);
            renderedRoute = null; RenderPage(); if (!preview) timer.Start();
        }
        private ListBox[] navigationLists = new ListBox[0];
        private Stack navigationStack;
        private List<NetworkChoice> networkChoices = new List<NetworkChoice>();
        private static ListBox SidebarList() { var l = ListBox.New(); l.AddCssClass("navigation-sidebar"); l.SetSelectionMode(SelectionMode.Single); return l; }

        private async Task RefreshAll()
        {
            await Run(async delegate
            {
                if (!preview) { if (network == null) { await LoadAccount(); ShowShell(); } else await LoadNetwork(); }
                if (currentPage.Contains("/")) await LoadFeature(currentPage);
                RenderPage(); ReportStatus("dashboard_loaded");
            }, "Working…");
        }

        private void PageHeading()
        {
            if (renderedRoute != currentPage && pageScroll != null) pageScroll.GetVadjustment().SetValue(0); renderedRoute = currentPage;
            if (currentPage.Contains("/"))
            {
                var crumbs = HBox(0); crumbs.SetMarginBottom(8); string[] parts = currentPage.Split('/'); string route = "";
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    route += (i == 0 ? "" : "/") + parts[i]; string target = route;
                    var crumb = LocalButton(parts[i], delegate { Navigate(target); }); Quiet(crumb); crumbs.Append(crumb);
                    var chevron = Glyph("", 10); chevron.Margin(2, 0, 2, 0); crumbs.Append(chevron);
                }
                page.Append(crumbs);
            }
            string heading = currentPage.Split('/').Last(); if (heading == "Details" && selectedNode != null) heading = J.First(selectedNode, "location", "model"); if (heading == "Profile" && selectedProfile != null) heading = J.S(selectedProfile, "name");
            var title = Text(heading, 30, "#17263C", true); title.SetMarginBottom(6); page.Append(title);
            AvailabilityNotice();
        }

        // A tappable card row with an icon, a title, a caption and a chevron.
        private Widget RouteRow(string title, string subtitle, string route, System.Action action = null)
        {
            var row = Adw.ActionRow.New(); row.SetUseMarkup(false); row.SetTitle(title); row.SetActivatable(true);
            if (!String.IsNullOrEmpty(subtitle)) row.SetSubtitle(subtitle);
            row.AddPrefix(Glyph(RouteIcon(route), 20, "#274CDB"));
            string why = null; string badge = action == null ? AvailabilityBadge(route, out why) : null;
            if (badge != null) { row.SetSubtitle((String.IsNullOrEmpty(subtitle) ? "" : subtitle + "\n") + "⚠ " + badge); row.SetTooltipText(badge + ". " + why); }
            row.AddSuffix(Glyph("", 12));
            row.OnActivated += delegate { if (busy) return; if (action != null) action(); else Navigate(route); };
            row.Name(title);
            var list = ListBox.New(); list.AddCssClass("boxed-list"); list.SetSelectionMode(SelectionMode.None); list.Append(row); list.SetMarginBottom(12);
            return list;
        }
        private static bool IsOnline(object data)
        {
            string status = J.S(data, "health", "internet", "status").NullIfEmpty() ?? J.S(data, "status");
            return status == "connected" || status == "green" || status == "online";
        }
        private Widget StatusBadge(string text, bool online)
        {
            var label = Label.New((online ? "●  " : "○  ") + text); label.AddCssClass("fs-11"); label.AddCssClass("bold"); label.AddCssClass(online ? "c-good" : "c-muted");
            var badge = VBox(); badge.AddCssClass("pill-badge"); badge.AddCssClass(online ? "bg-good" : "bg-edge"); badge.Append(label);
            badge.SetHalign(Gtk.Align.Start); badge.SetValign(Gtk.Align.Center);
            return badge;
        }
        private void NativeOverview()
        {
            page.Append(Text("Your network at a glance", 14, "#64738A"));
            var columns = Columns(1.45, 1); var left = Column(columns, 0); var right = Column(columns, 1); page.Append(columns);
            var internet = VBox();
            var internetTitle = Text("Internet", 20, "#17263C", true); internetTitle.SetMarginBottom(0); internetTitle.SetValign(Gtk.Align.Center);
            var internetTop = Split(internetTitle, StatusBadge(IsOnline(network) ? "Online" : "Check connection", IsOnline(network)));
            internetTop.Prepend(Glyph("", 28, "#274CDB"));
            internet.Append(internetTop);
            internet.Append(Hairline(0, 16, 0, 16));
            var speeds = Grid.New(); speeds.SetColumnHomogeneous(true);
            int column = 0;
            foreach (var metric in new[] { new[] { "↓  Download", Speed("down") }, new[] { "↑  Upload", Speed("up") } })
            {
                var stack = VBox(); stack.Append(Text(metric[0], 12, "#64738A")); var value = Text(metric[1], 28, "#17263C", true); value.SetMarginBottom(0); stack.Append(value); speeds.Attach(stack, column++, 0, 1, 1);
            }
            internet.Append(speeds); internet.Append(Text("Latest measured internet speed", 11, "#64738A"));
            var speedActions = WrapPanel(); speedActions.SetMarginTop(10); speedActions.Add(Button("Run speed test", StartSpeedTest, true)); speedActions.Add(Section("Internet details", "Home/Internet")); internet.Append(speedActions); left.Append(Card(internet));
            var mesh = VBox();
            var meshTitle = Text("Your eeros", 18, "#17263C", true); meshTitle.SetMarginBottom(0);
            var meshCount = Text(nodes.Count + " on this network", 12, "#64738A"); meshCount.SetMarginBottom(0); meshCount.SetHexpand(false);
            var meshHeading = Split(meshTitle, meshCount); meshHeading.SetMarginBottom(16); mesh.Append(meshHeading);
            foreach (object node in nodes) mesh.Append(CompactNode(node));
            if (nodes.Count == 0) mesh.Append(Text(loadErrors.ContainsKey("eero nodes") ? "eero information could not be loaded. Refresh to try again." : "No eeros were returned."));
            left.Append(Card(mesh, 18));
            left.Append(RouteRow("Chat with eero", "Get help with your network", "Home/Chat"));
            var deviceSummary = VBox(); deviceSummary.Append(Text("Devices", 20, "#17263C", true)); var counts = Grid.New(); counts.SetColumnHomogeneous(true);
            var online = VBox(); online.Append(Text(loadErrors.ContainsKey("Devices") ? "—" : devices.Count(d => J.B(d, "connected")).ToString(), 32, "#17263C", true)); online.Append(Text("Online", 12, "#0D7959")); counts.Attach(online, 0, 0, 1, 1);
            var offline = VBox(); offline.Append(Text(loadErrors.ContainsKey("Devices") ? "—" : devices.Count(d => !J.B(d, "connected")).ToString(), 32, "#17263C", true)); offline.Append(Text("Offline", 12, "#64738A")); counts.Attach(offline, 1, 0, 1, 1); deviceSummary.Append(counts);
            var allDevices = Section("View all devices", "Devices"); allDevices.SetHalign(Gtk.Align.Start); deviceSummary.Append(allDevices); right.Append(Card(deviceSummary));
            // The wifi details people reach for most: name, password and a QR code for guests.
            var wifi = VBox();
            var wifiTitle = Text("Your wifi", 20, "#17263C", true); wifiTitle.SetMarginBottom(0);
            wifi.Append(Split(wifiTitle, Section("Wifi settings", "Settings/Wifi name & password")));
            var ssid = Text(J.S(network, "name").NullIfEmpty() ?? "Wifi name not reported", 16); ssid.Margin(0, 8, 0, 10); wifi.Append(ssid);
            var wifiActions = WrapPanel();
            var qr = Button("Share QR code", async delegate { ShowWifiQr(J.S(network, "name"), await WifiPassword(network, NetResource("password"))); }); Quiet(qr); wifiActions.Add(qr);
            var pass = Button("Show password", async delegate { ShowPassword(await WifiPassword(network, NetResource("password"))); }); Quiet(pass); wifiActions.Add(pass);
            wifi.Append(wifiActions); right.Append(Card(wifi));
            var people = VBox(); people.Append(Text("Profiles", 20, "#17263C", true));
            var profileList = ListBox.New(); profileList.SetSelectionMode(SelectionMode.None); profileList.AddCssClass("boxed-list");
            foreach (object item in profiles.Take(3))
            {
                object profile = item;
                var row = Adw.ActionRow.New(); row.SetUseMarkup(false); row.SetTitle(J.S(profile, "name")); row.SetSubtitle(ProfileSummary(profile)); row.SetActivatable(true);
                row.AddPrefix(Glyph(RouteIcon("Home/Profiles"), 18, "#274CDB")); row.AddSuffix(Glyph("", 12));
                row.OnActivated += delegate { if (busy) return; selectedProfile = profile; Navigate("Home/Profiles/Profile"); };
                profileList.Append(row);
            }
            if (profiles.Count == 0) people.Append(Text("Create profiles to manage devices and scheduled pauses.", 12, "#64738A")); else people.Append(profileList);
            var manageProfiles = Section("Manage profiles", "Home/Profiles"); manageProfiles.SetHalign(Gtk.Align.Start); manageProfiles.SetMarginTop(6); people.Append(manageProfiles); right.Append(Card(people));
            right.Append(RouteRow("Security & privacy", "Protection, content filters, and privacy", "Home/Security & privacy"));
            right.Append(RouteRow("Internet backup", "Keep your network connected", "Settings/Advanced networking/Internet backup"));
            foreach (string key in loadErrors.Keys) Availability(key);
        }

        private Widget NativeNodeCard(object node)
        {
            bool online = IsOnline(node);
            var words = VBox(); words.Append(Text(J.First(node, "location", "model"), 15, "#17263C", true)); var caption = Text((J.B(node, "gateway") ? "Gateway · " : "") + J.S(node, "model"), 12, "#64738A"); caption.SetMarginBottom(0); words.Append(caption);
            var row = Split(words, StatusBadge(online ? "Online" : (J.S(node, "status").NullIfEmpty() ?? "Unknown"), online));
            row.Prepend(HardwareImage(node, 36));
            var stack = VBox(); stack.Append(row);
            var details = Text((J.First(node, "ip_address", "ip").NullIfEmpty() ?? "IP unavailable") + "  ·  " + (J.S(node, "connected_clients_count").NullIfEmpty() ?? "—") + " connected devices", 12, "#64738A"); details.Margin(48, 12, 0, 12); stack.Append(details);
            var actions = WrapPanel(); actions.SetMarginStart(42);
            var open = Button("View details", async delegate { selectedNode = preview ? node : await Read(J.S(node, "url")); currentPage = "Home/eeros/Details"; RenderPage(); }); Quiet(open); actions.Add(open);
            var rename = Button("Rename", async delegate { string name = await Prompt("eero location", J.S(node, "location")); if (name == null) return; ValidateName(name); await Change("PUT", J.S(node, "url"), J.Obj("location", name.Trim()), true, "eero location updated."); }); Quiet(rename); actions.Add(rename);
            var restart = Button("Restart", async delegate { if (!await Confirm("Restart " + J.First(node, "location", "model") + "? Connected devices may briefly lose internet access.")) return; if (preview) throw new InvalidOperationException("Preview mode is read only."); await api.Send("POST", J.S(node, "url") + "/reboot"); SetNotice("Restart requested. Wait a few minutes, then refresh."); });
            Quiet(restart); restart.SetTooltipText("Restart this eero"); actions.Add(restart); stack.Append(actions); return Card(stack, 20);
        }

        private void NativeDevicePage()
        {
            Sub("Everything connected to your network. Select a device to see its details and change its settings."); Availability("Devices");
            var search = SearchEntry.New(); search.SetPlaceholderText("Search by name, IP, MAC or maker"); search.SetHexpand(true); search.Name("Search devices");
            var statusFilter = DropDown.NewFromStrings(new[] { "All devices", "Online", "Offline", "Paused", "Blocked" }); statusFilter.Name("Show devices");
            var blocked = Section("Blocked devices", "Devices/Blocked devices");
            var tools = HBox(12); tools.Append(search); tools.Append(statusFilter); tools.Append(blocked); tools.SetMarginBottom(16); page.Append(tools);
            var workspace = Grid.New(); workspace.SetColumnSpacing(20); workspace.SetHexpand(true);
            var store = Gio.ListStore.New(StringObject.GetGType());
            var selection = SingleSelection.New(store); selection.SetAutoselect(true); selection.SetCanUnselect(false);
            var table = ColumnView.New(selection); table.SetShowRowSeparators(true); table.AddCssClass("data-table"); table.SetVexpand(true);
            var rows = new List<DeviceRow>();
            foreach (string name in new[] { "Name", "Status", "IP", "Connection" })
            {
                string field = name;
                var factory = SignalListItemFactory.New();
                factory.OnSetup += (sender, args) => { var item = (ListItem)args.Object; var label = Label.New(""); label.SetXalign(0); label.SetEllipsize(Pango.EllipsizeMode.End); item.SetChild(label); };
                factory.OnBind += (sender, args) =>
                {
                    var item = (ListItem)args.Object; var key = item.GetItem() as StringObject; var label = item.GetChild() as Label;
                    int index; if (key == null || label == null || !Int32.TryParse(key.GetString(), out index) || index >= rows.Count) return;
                    var r = rows[index]; label.SetText(field == "Name" ? r.Name : field == "Status" ? r.Status : field == "IP" ? r.IP : r.Connection);
                    label.RemoveCssClass("c-good"); if (field == "Status" && r.Status == "Online") label.AddCssClass("c-good");
                };
                var column = ColumnViewColumn.New(name == "IP" ? "IP address" : name, factory); column.SetResizable(true); column.SetExpand(name == "Name");
                table.AppendColumn(column);
            }
            var tableScroll = ScrolledWindow.New(); tableScroll.SetChild(table); tableScroll.SetMinContentHeight(440); tableScroll.SetHexpand(true); tableScroll.SetVexpand(true); tableScroll.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
            var tableCard = Card(tableScroll, 0); tableCard.SetMarginBottom(0); workspace.Attach(tableCard, 0, 0, 1, 1);
            var details = VBox(); details.SetSizeRequest(380, -1); details.SetValign(Gtk.Align.Start); workspace.Attach(details, 1, 0, 1, 1); page.Append(workspace);
            var count = Text("", 12, "#64738A"); count.SetMarginTop(12); page.Append(count);
            object shown = null;
            System.Action updateDetails = delegate
            {
                uint position = selection.GetSelected();
                DeviceRow selected = position != uint.MaxValue && position < rows.Count ? rows[(int)position] : null;
                if (selected != null && shown == selected.Raw && details.Count() > 0) return;
                details.Clear(); shown = selected == null ? null : selected.Raw;
                if (selected == null) { var empty = VBox(); empty.Append(Glyph("", 32)); empty.Append(Text("Device details", 18, "#17263C", true)); empty.Append(Text("Select a device to see its details and change its settings.", 13, "#64738A")); details.Append(Card(empty, 20)); return; }
                var head = VBox(); var name = Text(selected.Name, 20, "#17263C", true); name.SetMarginBottom(6); head.Append(name); head.Append(StatusBadge(selected.Status, selected.Status == "Online")); head.Margin(4, 0, 0, 12); details.Append(head);
                foreach (Widget part in DevicePanel(selected.Raw, delegate { })) details.Append(part);
            };
            System.Action filter = delegate
            {
                string q = search.GetText().Trim();
                uint position = selection.GetSelected();
                object previous = position != uint.MaxValue && position < rows.Count ? rows[(int)position].Raw : null;
                string status = new[] { "All devices", "Online", "Offline", "Paused", "Blocked" }[(int)Math.Min(4, statusFilter.GetSelected())];
                var found = devices.Where(d => (status == "All devices" || Device(d).Status == status) && (q.Length == 0 || (Device(d).Name + " " + Device(d).IP + " " + J.S(d, "mac") + " " + J.S(d, "manufacturer")).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)).Select(Device).OrderBy(d => d.Status != "Online").ThenBy(d => d.Name).ToList();
                rows.Clear(); rows.AddRange(found); shown = null;
                store.RemoveAll(); for (int i = 0; i < rows.Count; i++) store.Append(StringObject.New(i.ToString()));
                int keep = previous == null ? 0 : Math.Max(0, rows.FindIndex(d => d.Raw == previous));
                if (rows.Count > 0) selection.SetSelected((uint)keep);
                count.SetText(rows.Count + (rows.Count == 1 ? " device" : " devices") + " · " + devices.Count(d => J.B(d, "connected")) + " online" + (rows.Count == 0 ? " · No devices match your search." : "")); updateDetails();
            };
            search.OnSearchChanged += delegate { filter(); };
            statusFilter.OnNotify += (sender, args) => { if (args.Pspec.GetName() == "selected") filter(); };
            selection.OnSelectionChanged += delegate { updateDetails(); };
            table.OnActivate += (sender, args) => { if (!busy && args.Position < rows.Count) DeviceDialog(rows[(int)args.Position].Raw); };
            filter();
        }
    }
}
