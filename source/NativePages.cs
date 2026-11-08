using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private void NativeProfiles()
        {
            Sub("Group devices by person, then pause their internet, schedule bedtimes and set filters in one place."); Availability("Profiles");
            if (ApkContracts.IsNotRouting(network)) { page.Children.Add(Card(Text("Family profiles are unavailable while eero is in bridge or IP Passthrough mode."))); return; }
            var create = LocalButton("Add a profile", delegate { Edit("Create profile", new[] { new Field("name", "Profile name") }, async v => { ValidateName(V(v, "name")); await Change("POST", NetResource("profiles"), J.Obj("name", V(v, "name").Trim(), "paused", false, "devices", new object[0]), false, "Profile created."); }, "Create"); }); create.Style = (Style)FindResource("AccentButton"); page.Children.Add(create);
            var board = Columns(); int index = 0;
            foreach (object raw in profiles)
            {
                object item = raw; if (index % 2 == 0) board.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var body = new StackPanel(); var header = new StackPanel { Orientation = Orientation.Horizontal };
                var initial = Text(J.S(item, "name").Length > 0 ? J.S(item, "name").Substring(0, 1).ToUpperInvariant() : "P", 20, "#274CDB", true); initial.Margin = new Thickness(0); initial.HorizontalAlignment = HorizontalAlignment.Center; initial.VerticalAlignment = VerticalAlignment.Center;
                header.Children.Add(new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Background = Brush(UiPreferences.Dark ? "#233654" : "#EEF3FF"), Child = initial, Margin = new Thickness(0, 0, 14, 0) });
                var words = new StackPanel(); words.Children.Add(Text(J.S(item, "name"), 20, "#17263C", true)); var caption = Text(ProfileSummary(item), 12, "#64738A"); caption.Margin = new Thickness(0); words.Children.Add(caption); header.Children.Add(words); body.Children.Add(header);
                var footer = new Grid { Margin = new Thickness(58, 18, 0, 0) }; footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var open = Button("Manage", async delegate { selectedProfile = preview ? item : await Read(J.S(item, "url")); RecordHistory("Home/Profiles/Profile"); currentPage = "Home/Profiles/Profile"; RenderPage(); }); open.Margin = new Thickness(0); open.HorizontalAlignment = HorizontalAlignment.Left; footer.Children.Add(open);
                bool paused = J.B(item, "paused");
                var access = ToggleSwitch(!paused, "Internet for " + J.S(item, "name")); access.ToolTip = paused ? "Internet is paused. Switch on to resume." : "Switch off to pause internet for this profile.";
                access.Click += async delegate
                {
                    bool on = access.IsChecked == true; access.IsChecked = !on; if (busy) return;
                    await Run(async delegate { if (!Confirm((on ? "Resume" : "Pause") + " internet for every device in " + J.S(item, "name") + "?")) return; await Change("PUT", J.S(item, "url"), J.Obj("paused", !on), false, on ? "Internet resumed." : "Internet paused."); }, "Saving…");
                };
                var accessLabel = Text("Internet", 13, "#64738A"); accessLabel.Margin = new Thickness(0, 0, 10, 0); accessLabel.VerticalAlignment = VerticalAlignment.Center;
                var accessRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; accessRow.Children.Add(accessLabel); accessRow.Children.Add(access); Grid.SetColumn(accessRow, 1); footer.Children.Add(accessRow);
                body.Children.Add(footer);
                var card = Card(body); card.Margin = new Thickness(index % 2 == 0 ? 0 : 8, 12, index % 2 == 0 ? 8 : 0, 4); Grid.SetColumn(card, index % 2); Grid.SetRow(card, index / 2); board.Children.Add(card); index++;
            }
            page.Children.Add(board);
            if (profiles.Count == 0) page.Children.Add(Card(Text("No profiles yet. Add one for each person, for example Kids, then choose their devices.", 14, "#64738A")));
        }

        private void NativeProfileDetail(object profile)
        {
            if (profile == null) { page.Children.Add(Text("Select a profile from the Profiles page.")); return; }
            string url = J.S(profile, "url"); Sub("A group of devices you can pause, schedule and filter together, for example everything a child uses.");
            bool paused = J.B(profile, "paused");
            Group(null,
                SettingToggle("Internet access", paused ? "Paused. Devices in this profile can't go online." : "Devices in this profile can go online.", !paused, async delegate(bool on)
                { if (!Confirm((on ? "Resume" : "Pause") + " internet for every device in " + J.S(profile, "name") + "?")) return; await Change("PUT", url, J.Obj("paused", !on), false, on ? "Internet resumed." : "Internet paused."); }),
                SettingRow("Profile name", null, J.S(profile, "name"), RowAction("Rename", delegate { Edit("Profile name", new[] { new Field("name", "Name", J.S(profile, "name")) }, async v => { ValidateName(V(v, "name")); await Change("PUT", url, J.Obj("name", V(v, "name").Trim()), false, "Profile renamed."); }); })));
            var columns = Columns(); var left = Column(columns, 0); var right = Column(columns, 1); page.Children.Add(columns);
            var memberDevices = J.List(J.Get(profile, "devices")); var deviceRows = new List<UIElement>();
            foreach (object raw in memberDevices) { object device = raw; deviceRows.Add(SettingRow(Device(device).Name, Device(device).Status, null, RowAction("Details", delegate { DeviceDialog(device); }))); }
            if (deviceRows.Count == 0) deviceRows.Add(InfoRow("No devices yet", null, "Add the devices this profile should control."));
            deviceRows.Add(SettingRow("Choose devices", null, memberDevices.Count + (memberDevices.Count == 1 ? " device" : " devices"), RowAction("Choose", delegate { ProfileDevices(profile); })));
            AddGroupHeading(left, "Devices"); left.Children.Add(RowGroup(deviceRows, 14));
            string schedulesUrl = Resource(profile, "schedules", url + "/schedules");
            var pauseRows = new List<UIElement>();
            foreach (object raw in J.List(J.Get(profile, "schedule")))
            {
                object item = raw;
                string days = String.Join(", ", J.List(J.Get(item, "days")).Select(d => Human(J.S(d).ToLowerInvariant())).ToArray());
                pauseRows.Add(SettingRow(J.S(item, "name").NullIfEmpty() ?? "Pause", J.S(item, "start") + "–" + J.S(item, "end") + (days.Length > 0 ? " · " + days : "") + (J.B(item, "enabled") ? "" : " · Off"), null,
                    RowAction("Edit", delegate { ScheduleEdit(item, J.S(item, "url")); }),
                    RowActionAsync("Delete", async delegate { if (!Confirm("Delete this scheduled pause?")) return; await Change("DELETE", J.S(item, "url"), null, false, "Scheduled pause deleted."); }, true)));
            }
            if (pauseRows.Count == 0) pauseRows.Add(InfoRow("No scheduled pauses", null, "Pause internet automatically, for example at bedtime."));
            pauseRows.Add(SettingRow("Add a scheduled pause", null, null, RowAction("Add", delegate { ScheduleEdit(null, schedulesUrl); })));
            AddGroupHeading(left, "Scheduled pauses"); left.Children.Add(RowGroup(pauseRows, 14));
            AddGroupHeading(right, "Filters & app controls");
            right.Children.Add(RowGroup(new UIElement[] {
                SettingRow("Content filters", "Block adult content, SafeSearch and more for this profile.", null, RowActionAsync("Change", async delegate { string policyUrl = ProfilePolicyUrl(profile); object data = await Read(policyUrl); EditPolicies(data, policyUrl); })),
                SettingRow("Block & allow websites", "Block or allow specific websites for this profile.", null, RowActionAsync("Manage", async delegate { object domains = await Read(FeatureUrl("dns_policies/advanced_content_filter")); DomainDialog(domains, profile); })),
                SettingRow("Block apps", "Block apps such as games or social media.", null, RowActionAsync("Choose", async delegate { await BlockApps(profile); })),
                SettingToggle("Ad blocking", "Block ads on this profile's devices.", J.B(profile, "premium_dns", "ad_block_settings", "enabled"), async delegate(bool on) { await Change("POST", ProfilePolicyUrl(profile) + "/adblock", J.Obj("enable", on), false, on ? "Ad blocking turned on." : "Ad blocking turned off."); }),
                SettingRow("Filter details", "Everything eero reports for this profile's filters.", null, RowAction("View", delegate { Info("Profile filter details", J.Get(profile, "premium_dns")); })) }, 14));
            AddGroupHeading(right, "Danger zone");
            right.Children.Add(RowGroup(new UIElement[] { SettingRow("Delete profile", "Its devices stay on your network.", null, RowAction("Delete", delegate { Edit("Delete profile", new[] { new Field("confirm", "Type the profile name to confirm").Help("Profile: " + J.S(profile, "name")) }, async v => { if (V(v, "confirm") != J.S(profile, "name")) throw new ArgumentException("The profile name does not match."); currentPage = "Home/Profiles"; selectedProfile = null; await Change("DELETE", url, null, false, "Profile deleted. Devices remain on your network."); }, "Delete profile"); }, true)) }, 14));
        }

        private void NativeActivity()
        {
            Sub("Your internet speed, how much data you used, and what happened on your network.");
            var columns = Columns(1.2, 1); var left = Column(columns, 0); var right = Column(columns, 1); page.Children.Add(columns);
            var summary = new StackPanel(); summary.Children.Add(Text("Latest internet speed", 20, "#17263C", true));
            var speeds = Columns(); var download = new StackPanel(); download.Children.Add(Text("Download", 12, "#64738A")); download.Children.Add(Text("↓ " + Speed("down"), 28, "#17263C", true)); speeds.Children.Add(download);
            var upload = new StackPanel(); upload.Children.Add(Text("Upload", 12, "#64738A")); upload.Children.Add(Text("↑ " + Speed("up"), 28, "#17263C", true)); Grid.SetColumn(upload, 1); speeds.Children.Add(upload); summary.Children.Add(speeds);
            var actions = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) }; actions.Children.Add(Button("Run speed test", StartSpeedTest, true)); actions.Children.Add(Section("Speed test history", "Home/Internet")); summary.Children.Add(actions); left.Children.Add(Card(summary));
            right.Children.Add(RouteRow("Data usage", "How much data your network used, day by day", "Activity/Data usage"));
            right.Children.Add(RouteRow("Security activity", "What eero blocked for you", "Activity/Security activity"));
            right.Children.Add(RouteRow("Network events", "Devices joining, updates, pauses and more", "Activity/Network events"));
            right.Children.Add(RouteRow("Usage details", "Usage and blocking for one eero, profile or device", "Activity/Usage details"));
            object data = null; if (preview) data = PreviewFeature("Activity/Data usage"); else featureData.TryGetValue("Activity/Data usage", out data);
            if (data != null && !(data is Exception))
            {
                page.Children.Add(Text("Data usage", 20, "#17263C", true)); AddSeriesCharts(page, data, true);
            }
        }
    }
}
