import AppKit
import Foundation
import SwiftUI

// Translated from NativePages.cs: the profile board, profile details and the Activity overview.
extension MainWindow {
    /// A round initial in the accent color (profile cards, chat avatars).
    static func avatar(_ letter: String, _ size: CGFloat, _ fontSize: CGFloat) -> Block { .view(AvatarView(letter: letter, size: size, fontSize: fontSize)) }

    func nativeProfiles() {
        sub("Group devices by person, then pause their internet, schedule bedtimes and set filters in one place."); availability("Profiles")
        if ApkContracts.isNotRouting(network) { page.append(card([text("Family profiles are unavailable while eero is in bridge or IP Passthrough mode.")])); return }
        let create = accent(localButton("Add a profile", { [unowned self] in
            self.edit("Create profile", [Field("name", "Profile name")], { [unowned self] v in
                try MainWindow.validateName(self.v(v, "name"))
                try await self.change("POST", try self.netResource("profiles"), J.obj("name", self.v(v, "name").trim(), "paused", false, "devices", [Any]()), false, "Profile created.")
            }, "Create")
        }))
        page.append(.padded([.buttons([create])], EdgeInsets(top: 0, leading: 0, bottom: 4, trailing: 0)))
        var cards: [ProfileCardView] = []
        for item in profiles {
            let name = J.s(item, "name")
            let open = button("Manage", { [unowned self] in
                self.selectedProfile = self.preview ? item : try await self.read(J.s(item, "url"))
                self.recordHistory("Home/Profiles/Profile"); self.currentPage = "Home/Profiles/Profile"; self.renderPage()
            })
            cards.append(ProfileCardView(name: name, summary: MainWindow.profileSummary(item), paused: J.b(item, "paused"), manage: open,
                                         onToggle: { [unowned self] on in self.toggleProfileAccess(item, on) }))
        }
        page.append(.view(ProfileBoardView(cards: cards)))
        if profiles.isEmpty { page.append(card([text("No profiles yet. Add one for each person, for example Kids, then choose their devices.", 14, "#64738A")])) }
    }
    func toggleProfileAccess(_ item: Any?, _ on: Bool) {
        if busy { return }
        Task { @MainActor in
            await self.run({ [unowned self] in
                if !(await self.confirm((on ? "Resume" : "Pause") + " internet for every device in " + J.s(item, "name") + "?")) { return }
                try await self.change("PUT", J.s(item, "url"), J.obj("paused", !on), false, on ? "Internet resumed." : "Internet paused.")
            }, "Saving…")
            // The switch stays put until a successful change re-renders the page with the new state.
            self.renderToken += 1
        }
    }

    func nativeProfileDetail(_ profile: Any?) {
        guard let profile = profile, !(profile is JNull) else { page.append(text("Select a profile from the Profiles page.")); return }
        let url = J.s(profile, "url"); sub("A group of devices you can pause, schedule and filter together, for example everything a child uses.")
        let paused = J.b(profile, "paused")
        group(nil,
              settingToggle("Internet access", paused ? "Paused. Devices in this profile can't go online." : "Devices in this profile can go online.", !paused) { [unowned self] on in
                  if !(await self.confirm((on ? "Resume" : "Pause") + " internet for every device in " + J.s(profile, "name") + "?")) { return }
                  try await self.change("PUT", url, J.obj("paused", !on), false, on ? "Internet resumed." : "Internet paused.")
              },
              settingRow("Profile name", nil, J.s(profile, "name"), rowAction("Rename", { [unowned self] in
                  self.edit("Profile name", [Field("name", "Name", J.s(profile, "name"))], { [unowned self] v in
                      try MainWindow.validateName(self.v(v, "name"))
                      try await self.change("PUT", url, J.obj("name", self.v(v, "name").trim()), false, "Profile renamed.")
                  })
              })))
        var left: [Block] = [], right: [Block] = []
        let memberDevices = J.list(J.get(profile, "devices")); var deviceRows: [Row] = []
        for device in memberDevices { deviceRows.append(settingRow(MainWindow.device(device).name, MainWindow.device(device).status, nil, rowAction("Details", { [unowned self] in self.deviceDialog(device) }))) }
        if deviceRows.isEmpty { deviceRows.append(infoRow("No devices yet", nil, "Add the devices this profile should control.")) }
        deviceRows.append(settingRow("Choose devices", nil, "\(memberDevices.count)" + (memberDevices.count == 1 ? " device" : " devices"), rowAction("Choose", { [unowned self] in self.profileDevices(profile) })))
        left.append(groupHeading("Devices")); left.append(rowGroup(deviceRows, 14))
        let schedulesUrl = resource(profile, "schedules", url + "/schedules")
        var pauseRows: [Row] = []
        for item in J.list(J.get(profile, "schedule")) {
            let days = J.list(J.get(item, "days")).map { MainWindow.human(J.s($0).lowercased()) }.joined(separator: ", ")
            pauseRows.append(settingRow(J.s(item, "name").nullIfEmpty ?? "Pause", J.s(item, "start") + "–" + J.s(item, "end") + (!days.isEmpty ? " · " + days : "") + (J.b(item, "enabled") ? "" : " · Off"), nil,
                rowAction("Edit", { [unowned self] in self.scheduleEdit(item, J.s(item, "url")) }),
                rowActionAsync("Delete", { [unowned self] in
                    if !(await self.confirm("Delete this scheduled pause?")) { return }
                    try await self.change("DELETE", J.s(item, "url"), nil, false, "Scheduled pause deleted.")
                }, true)))
        }
        if pauseRows.isEmpty { pauseRows.append(infoRow("No scheduled pauses", nil, "Pause internet automatically, for example at bedtime.")) }
        pauseRows.append(settingRow("Add a scheduled pause", nil, nil, rowAction("Add", { [unowned self] in self.scheduleEdit(nil, schedulesUrl) })))
        left.append(groupHeading("Scheduled pauses")); left.append(rowGroup(pauseRows, 14))
        right.append(groupHeading("Filters & app controls"))
        right.append(rowGroup([
            settingRow("Content filters", "Block adult content, SafeSearch and more for this profile.", nil, rowActionAsync("Change", { [unowned self] in
                let policyUrl = try self.profilePolicyUrl(profile); let data = try await self.read(policyUrl); try self.editPolicies(data, policyUrl)
            })),
            settingRow("Block & allow websites", "Block or allow specific websites for this profile.", nil, rowActionAsync("Manage", { [unowned self] in
                let domains = try await self.read(try self.featureUrl("dns_policies/advanced_content_filter")); self.domainDialog(domains, profile)
            })),
            settingRow("Block apps", "Block apps such as games or social media.", nil, rowActionAsync("Choose", { [unowned self] in try await self.blockApps(profile) })),
            settingToggle("Ad blocking", "Block ads on this profile's devices.", J.b(profile, "premium_dns", "ad_block_settings", "enabled")) { [unowned self] on in
                try await self.change("POST", try self.profilePolicyUrl(profile) + "/adblock", J.obj("enable", on), false, on ? "Ad blocking turned on." : "Ad blocking turned off.")
            },
            settingRow("Filter details", "Everything eero reports for this profile's filters.", nil, rowAction("View", { [unowned self] in self.info("Profile filter details", J.get(profile, "premium_dns")) }))], 14))
        right.append(groupHeading("Danger zone"))
        right.append(rowGroup([settingRow("Delete profile", "Its devices stay on your network.", nil, rowAction("Delete", { [unowned self] in
            self.edit("Delete profile", [Field("confirm", "Type the profile name to confirm").help("Profile: " + J.s(profile, "name"))], { [unowned self] v in
                if self.v(v, "confirm") != J.s(profile, "name") { throw AppError.argument("The profile name does not match.") }
                self.currentPage = "Home/Profiles"; self.selectedProfile = nil
                try await self.change("DELETE", url, nil, false, "Profile deleted. Devices remain on your network.")
            }, "Delete profile")
        }, true))], 14))
        page.append(columns(left, right))
    }

    func nativeActivity() {
        sub("Your internet speed, how much data you used, and what happened on your network.")
        var summary: [Block] = [text("Latest internet speed", 20, "#17263C", true)]
        summary.append(columns([text("Download", 12, "#64738A"), text("↓ " + speed("down"), 28, "#17263C", true)], [text("Upload", 12, "#64738A"), text("↑ " + speed("up"), 28, "#17263C", true)]))
        summary.append(.padded([.buttons([button("Run speed test", { [unowned self] in try await self.startSpeedTest() }, true), section("Speed test history", "Home/Internet")])], EdgeInsets(top: 18, leading: 0, bottom: 0, trailing: 0)))
        let left: [Block] = [card(summary)]
        let right: [Block] = [
            routeRow("Data usage", "How much data your network used, day by day", "Activity/Data usage"),
            routeRow("Security activity", "What eero blocked for you", "Activity/Security activity"),
            routeRow("Network events", "Devices joining, updates, pauses and more", "Activity/Network events"),
            routeRow("Usage details", "Usage and blocking for one eero, profile or device", "Activity/Usage details")]
        page.append(columns(left, right, 1.2, 1))
        var data: Any? = preview ? previewFeature("Activity/Data usage") : featureData["Activity/Data usage"]
        if data is JNull { data = nil }
        if let d = data, !(d is Error) {
            page.append(text("Data usage", 20, "#17263C", true)); addSeriesCharts(&page, d, true)
        }
    }
}

/// Round initial with a soft accent background.
struct AvatarView: View {
    let letter: String; let size: CGFloat; let fontSize: CGFloat
    var body: some View {
        Text(letter).font(.system(size: fontSize, weight: .semibold)).foregroundColor(.accentColor)
            .frame(width: size, height: size)
            .background(Circle().fill(Color.accentColor.opacity(0.14)))
            .accessibilityHidden(true)
    }
}

/// One profile on the Profiles board: avatar, name, summary, Manage and an Internet switch.
struct ProfileCardView: View {
    let name: String; let summary: String; let paused: Bool; let manage: ButtonSpec
    let onToggle: (Bool) -> Void
    @EnvironmentObject var model: MainWindow
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .center, spacing: 14) {
                AvatarView(letter: name.isEmpty ? "P" : String(name.prefix(1)).uppercased(), size: 44, fontSize: 20)
                VStack(alignment: .leading, spacing: 4) {
                    Text(name).font(.system(size: 20, weight: .semibold)).lineLimit(1).truncationMode(.tail)
                    Text(summary).font(.system(size: 12)).foregroundColor(Tone.muted.color).lineLimit(1).truncationMode(.tail)
                }
                Spacer(minLength: 0)
            }
            HStack(alignment: .center, spacing: 10) {
                SpecButton(spec: manage)
                Spacer(minLength: 8)
                Text("Internet").font(.system(size: 13)).foregroundColor(Tone.muted.color)
                // The switch shows the saved state; a successful change re-renders the page.
                Toggle("", isOn: Binding(get: { !paused }, set: { on in if on != !paused && !model.busy { onToggle(on) } }))
                    .toggleStyle(.switch).labelsHidden().disabled(model.busy)
                    .accessibilityLabel("Internet for " + name)
                    .help(paused ? "Internet is paused. Switch on to resume." : "Switch off to pause internet for this profile.")
            }
            .padding(.leading, 58).padding(.top, 18)
        }
        .padding(24)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 10).fill(Color(nsColor: .controlBackgroundColor)))
        .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(Color(nsColor: .separatorColor), lineWidth: 0.5))
    }
}

/// Two equal columns of profile cards.
struct ProfileBoardView: View {
    let cards: [ProfileCardView]
    var body: some View {
        LazyVGrid(columns: [GridItem(.flexible(), spacing: 16, alignment: .top), GridItem(.flexible(), spacing: 16, alignment: .top)], alignment: .leading, spacing: 16) {
            ForEach(Array(cards.enumerated()), id: \.offset) { $0.element }
        }
        .padding(.top, 12).padding(.bottom, 16)
    }
}
