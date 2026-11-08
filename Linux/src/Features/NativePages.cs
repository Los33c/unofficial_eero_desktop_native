using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gtk;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private static CssProvider portedStyles;
        // Extra style classes used by the profile, activity and chat pages (round avatars, chat surfaces).
        private static void PortedStyles()
        {
            if (portedStyles != null) return;
            portedStyles = CssProvider.New();
            portedStyles.LoadFromString(
                ".avatar { border-radius: 9999px; background-color: alpha(@accent_bg_color, 0.14); }" +
                ".chat-side { background-color: @sidebar_bg_color; }" +
                ".chat-frame { background-color: @view_bg_color; border: 1px solid alpha(currentColor, 0.12); border-radius: 8px; }" +
                ".chat-composer { background-color: @card_bg_color; border: 1px solid alpha(currentColor, 0.15); border-radius: 12px; padding: 4px 8px 8px 10px; }" +
                ".chat-composer textview, .chat-composer textview text { background-color: transparent; }" +
                ".chat-bubble-mine { background-color: alpha(@accent_bg_color, 0.16); border-radius: 14px 14px 4px 14px; padding: 10px 14px; }" +
                ".chat-bubble-theirs { background-color: @card_bg_color; border: 1px solid alpha(currentColor, 0.12); border-radius: 14px 14px 14px 4px; padding: 10px 14px; }" +
                ".chat-card { background-color: @card_bg_color; border: 1px solid alpha(currentColor, 0.12); border-radius: 12px; padding: 12px 16px; }" +
                ".chat-permission { background-color: @card_bg_color; border: 1.5px solid @accent_color; border-radius: 12px; padding: 14px 16px; }" +
                ".chat-history-current { background-color: alpha(@accent_bg_color, 0.14); }" +
                ".chat-chip { border-radius: 9999px; padding: 8px 14px; }" +
                ".italic { font-style: italic; }");
            StyleContext.AddProviderForDisplay(Gdk.Display.GetDefault(), portedStyles, 610);
        }
        private static Widget Avatar(string letter, int size, double fontSize)
        {
            PortedStyles();
            // The label is the circle: yalign centers the letter, and nothing expands the page around it.
            var initial = Text(letter, fontSize, "#274CDB", true); initial.SetWrap(false); initial.SetMarginBottom(0); initial.SetXalign(0.5f); initial.SetYalign(0.5f);
            initial.AddCssClass("avatar"); initial.SetSizeRequest(size, size); initial.SetHalign(Gtk.Align.Start); initial.SetValign(Gtk.Align.Start);
            return initial;
        }

        private void NativeProfiles()
        {
            Sub("Group devices by person, then pause their internet, schedule bedtimes and set filters in one place."); Availability("Profiles");
            if (ApkContracts.IsNotRouting(network)) { page.Append(Card(Text("Family profiles are unavailable while eero is in bridge or IP Passthrough mode."))); return; }
            var create = LocalButton("Add a profile", delegate { Edit("Create profile", new[] { new Field("name", "Profile name") }, async v => { ValidateName(V(v, "name")); await Change("POST", NetResource("profiles"), J.Obj("name", V(v, "name").Trim(), "paused", false, "devices", new object[0]), false, "Profile created."); }, "Create"); }); Accent(create); create.SetMarginBottom(4); page.Append(create);
            var board = Grid.New(); board.SetColumnHomogeneous(true); board.SetColumnSpacing(16); board.SetHexpand(true); int index = 0;
            foreach (object raw in profiles)
            {
                object item = raw;
                var body = VBox(); var header = HBox(14);
                header.Append(Avatar(J.S(item, "name").Length > 0 ? J.S(item, "name").Substring(0, 1).ToUpperInvariant() : "P", 44, 20));
                var words = VBox(); words.SetValign(Gtk.Align.Center); words.SetHexpand(true); var profileName = NoWrap(Text(J.S(item, "name"), 20, "#17263C", true), 40); profileName.SetMarginBottom(4); words.Append(profileName); var caption = NoWrap(Text(ProfileSummary(item), 12, "#64738A"), 60); caption.SetMarginBottom(0); words.Append(caption); header.Append(words); body.Append(header);
                var open = Button("Manage", async delegate { selectedProfile = preview ? item : await Read(J.S(item, "url")); RecordHistory("Home/Profiles/Profile"); currentPage = "Home/Profiles/Profile"; RenderPage(); }); open.SetHalign(Gtk.Align.Start);
                bool paused = J.B(item, "paused");
                var access = ToggleSwitch(!paused, "Internet for " + J.S(item, "name")); access.SetTooltipText(paused ? "Internet is paused. Switch on to resume." : "Switch off to pause internet for this profile.");
                access.OnStateSet += (sender, args) =>
                {
                    bool on = args.State;
                    // The switch stays put; a successful change re-renders the page with the new state.
                    GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT, delegate { access.SetActive(!paused); return false; });
                    if (on != !paused && !busy) GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT, delegate { ToggleProfileAccess(item, on); return false; });
                    return true;
                };
                var accessLabel = NoWrap(Text("Internet", 13, "#64738A"), 20); accessLabel.SetMarginBottom(0); accessLabel.SetValign(Gtk.Align.Center); accessLabel.SetHexpand(false);
                var accessRow = HBox(10); accessRow.SetValign(Gtk.Align.Center); accessRow.Append(accessLabel); accessRow.Append(access);
                var footer = Split(open, accessRow); footer.Margin(58, 18, 0, 0);
                body.Append(footer);
                var card = Card(body); card.SetMarginTop(12); card.SetMarginBottom(4); board.Attach(card, index % 2, index / 2, 1, 1); index++;
            }
            page.Append(board);
            if (profiles.Count == 0) page.Append(Card(Text("No profiles yet. Add one for each person, for example Kids, then choose their devices.", 14, "#64738A")));
        }
        private async void ToggleProfileAccess(object item, bool on)
        {
            await Run(async delegate { if (!await Confirm((on ? "Resume" : "Pause") + " internet for every device in " + J.S(item, "name") + "?")) return; await Change("PUT", J.S(item, "url"), J.Obj("paused", !on), false, on ? "Internet resumed." : "Internet paused."); }, "Saving…");
        }

        private void NativeProfileDetail(object profile)
        {
            if (profile == null) { page.Append(Text("Select a profile from the Profiles page.")); return; }
            string url = J.S(profile, "url"); Sub("A group of devices you can pause, schedule and filter together, for example everything a child uses.");
            bool paused = J.B(profile, "paused");
            Group(null,
                SettingToggle("Internet access", paused ? "Paused. Devices in this profile can't go online." : "Devices in this profile can go online.", !paused, async delegate(bool on)
                { if (!await Confirm((on ? "Resume" : "Pause") + " internet for every device in " + J.S(profile, "name") + "?")) return; await Change("PUT", url, J.Obj("paused", !on), false, on ? "Internet resumed." : "Internet paused."); }),
                SettingRow("Profile name", null, J.S(profile, "name"), RowAction("Rename", delegate { Edit("Profile name", new[] { new Field("name", "Name", J.S(profile, "name")) }, async v => { ValidateName(V(v, "name")); await Change("PUT", url, J.Obj("name", V(v, "name").Trim()), false, "Profile renamed."); }); })));
            var columns = Columns(); var left = Column(columns, 0); var right = Column(columns, 1); page.Append(columns);
            var memberDevices = J.List(J.Get(profile, "devices")); var deviceRows = new List<Widget>();
            foreach (object raw in memberDevices) { object device = raw; deviceRows.Add(SettingRow(Device(device).Name, Device(device).Status, null, RowAction("Details", delegate { DeviceDialog(device); }))); }
            if (deviceRows.Count == 0) deviceRows.Add(InfoRow("No devices yet", null, "Add the devices this profile should control."));
            deviceRows.Add(SettingRow("Choose devices", null, memberDevices.Count + (memberDevices.Count == 1 ? " device" : " devices"), RowAction("Choose", delegate { ProfileDevices(profile); })));
            AddGroupHeading(left, "Devices"); left.Append(RowGroup(deviceRows, 14));
            string schedulesUrl = Resource(profile, "schedules", url + "/schedules");
            var pauseRows = new List<Widget>();
            foreach (object raw in J.List(J.Get(profile, "schedule")))
            {
                object item = raw;
                string days = String.Join(", ", J.List(J.Get(item, "days")).Select(d => Human(J.S(d).ToLowerInvariant())).ToArray());
                pauseRows.Add(SettingRow(J.S(item, "name").NullIfEmpty() ?? "Pause", J.S(item, "start") + "–" + J.S(item, "end") + (days.Length > 0 ? " · " + days : "") + (J.B(item, "enabled") ? "" : " · Off"), null,
                    RowAction("Edit", delegate { ScheduleEdit(item, J.S(item, "url")); }),
                    RowActionAsync("Delete", async delegate { if (!await Confirm("Delete this scheduled pause?")) return; await Change("DELETE", J.S(item, "url"), null, false, "Scheduled pause deleted."); }, true)));
            }
            if (pauseRows.Count == 0) pauseRows.Add(InfoRow("No scheduled pauses", null, "Pause internet automatically, for example at bedtime."));
            pauseRows.Add(SettingRow("Add a scheduled pause", null, null, RowAction("Add", delegate { ScheduleEdit(null, schedulesUrl); })));
            AddGroupHeading(left, "Scheduled pauses"); left.Append(RowGroup(pauseRows, 14));
            AddGroupHeading(right, "Filters & app controls");
            right.Append(RowGroup(new Widget[] {
                SettingRow("Content filters", "Block adult content, SafeSearch and more for this profile.", null, RowActionAsync("Change", async delegate { string policyUrl = ProfilePolicyUrl(profile); object data = await Read(policyUrl); EditPolicies(data, policyUrl); })),
                SettingRow("Block & allow websites", "Block or allow specific websites for this profile.", null, RowActionAsync("Manage", async delegate { object domains = await Read(FeatureUrl("dns_policies/advanced_content_filter")); DomainDialog(domains, profile); })),
                SettingRow("Block apps", "Block apps such as games or social media.", null, RowActionAsync("Choose", async delegate { await BlockApps(profile); })),
                SettingToggle("Ad blocking", "Block ads on this profile's devices.", J.B(profile, "premium_dns", "ad_block_settings", "enabled"), async delegate(bool on) { await Change("POST", ProfilePolicyUrl(profile) + "/adblock", J.Obj("enable", on), false, on ? "Ad blocking turned on." : "Ad blocking turned off."); }),
                SettingRow("Filter details", "Everything eero reports for this profile's filters.", null, RowAction("View", delegate { Info("Profile filter details", J.Get(profile, "premium_dns")); })) }, 14));
            AddGroupHeading(right, "Danger zone");
            right.Append(RowGroup(new Widget[] { SettingRow("Delete profile", "Its devices stay on your network.", null, RowAction("Delete", delegate { Edit("Delete profile", new[] { new Field("confirm", "Type the profile name to confirm").Help("Profile: " + J.S(profile, "name")) }, async v => { if (V(v, "confirm") != J.S(profile, "name")) throw new ArgumentException("The profile name does not match."); currentPage = "Home/Profiles"; selectedProfile = null; await Change("DELETE", url, null, false, "Profile deleted. Devices remain on your network."); }, "Delete profile"); }, true)) }, 14));
        }

        private void NativeActivity()
        {
            Sub("Your internet speed, how much data you used, and what happened on your network.");
            var columns = Columns(1.2, 1); var left = Column(columns, 0); var right = Column(columns, 1); page.Append(columns);
            var summary = VBox(); summary.Append(Text("Latest internet speed", 20, "#17263C", true));
            var speeds = Grid.New(); speeds.SetColumnHomogeneous(true); speeds.SetColumnSpacing(24);
            var download = VBox(); download.Append(Text("Download", 12, "#64738A")); download.Append(Text("↓ " + Speed("down"), 28, "#17263C", true)); speeds.Attach(download, 0, 0, 1, 1);
            var upload = VBox(); upload.Append(Text("Upload", 12, "#64738A")); upload.Append(Text("↑ " + Speed("up"), 28, "#17263C", true)); speeds.Attach(upload, 1, 0, 1, 1); summary.Append(speeds);
            var actions = WrapPanel(); actions.SetMarginTop(18); actions.Add(Button("Run speed test", StartSpeedTest, true)); actions.Add(Section("Speed test history", "Home/Internet")); summary.Append(actions); left.Append(Card(summary));
            right.Append(RouteRow("Data usage", "How much data your network used, day by day", "Activity/Data usage"));
            right.Append(RouteRow("Security activity", "What eero blocked for you", "Activity/Security activity"));
            right.Append(RouteRow("Network events", "Devices joining, updates, pauses and more", "Activity/Network events"));
            right.Append(RouteRow("Usage details", "Usage and blocking for one eero, profile or device", "Activity/Usage details"));
            object data = null; if (preview) data = PreviewFeature("Activity/Data usage"); else featureData.TryGetValue("Activity/Data usage", out data);
            if (data != null && !(data is Exception))
            {
                page.Append(Text("Data usage", 20, "#17263C", true)); AddSeriesCharts(page, data, true);
            }
        }
    }
}
