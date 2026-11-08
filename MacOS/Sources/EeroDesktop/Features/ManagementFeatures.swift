import AppKit
import Foundation
import SwiftUI

/// Uri.EscapeDataString: everything except RFC 3986 unreserved characters is percent-encoded.
fileprivate func escapeData(_ s: String) -> String {
    let allowed = CharacterSet(charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~")
    return s.addingPercentEncoding(withAllowedCharacters: allowed) ?? s
}
/// C# `??` treats a JSON null as missing.
fileprivate func present(_ v: Any?) -> Any? { v is JNull ? nil : v }

// Translated from ManagementFeatures.cs: internet, activity, permissions, updates, notifications,
// troubleshooting, account, subscription, connected services, network management, backup, multi-WAN, help, about.
extension MainWindow {
    var usageDays: Int { get { stored("usageDays", 7) } set { state["usageDays"] = newValue } }
    func activityMenu() { nativeActivity() }

    func internetPage(_ history: Any?) {
        sub("Your connection to your internet provider and how fast it is.")
        let online = MainWindow.isOnline(network); let isp = J.first(network, "isp", "isp_name")
        group("Connection",
              infoRow("Status", online ? "● Online" : "○ " + (MainWindow.friendly(J.s(network, "health", "internet", "status")).nullIfEmpty ?? "Not reported"), online ? nil : "Check the cable to your modem, then try Guided diagnostics under Settings › Troubleshooting."),
              !isp.isEmpty ? infoRow("Internet provider", isp) : nil,
              !J.s(network, "gateway_ip").isEmpty ? infoRow("Public IP address", J.s(network, "gateway_ip")) : nil)
        group("Speed",
              infoRow("Download", speed("down")),
              infoRow("Upload", speed("up")),
              settingRow("Speed test", "Measures your connection from the gateway eero. Takes about a minute; results then appear below.", nil, rowActionAsync("Run test", { [unowned self] in try await self.startSpeedTest() })))
        var rows: [Row] = []
        for test in MainWindow.entries(history, "speed_tests", "data").prefix(10) {
            let when = J.first(test, "date", "created", "created_at", "timestamp")
            let down = J.first(test, "down_mbps", "down"), up = J.first(test, "up_mbps", "up")
            let downText: String = Double(down).map { "↓ " + MainWindow.rounded($0) + " Mbps" } ?? ("↓ " + (J.s(test, "down", "value").nullIfEmpty ?? "—"))
            let upText: String = Double(up).map { "↑ " + MainWindow.rounded($0) + " Mbps" } ?? ("↑ " + (J.s(test, "up", "value").nullIfEmpty ?? "—"))
            let speeds = downText + "   " + upText
            rows.append(infoRow(when.nullIfEmpty ?? "Speed test", speeds))
        }
        if rows.isEmpty { rows.append(infoRow("No earlier tests", nil, "Run a speed test to start a history.")) }
        group("Recent speed tests", rows: rows)
        relatedPagesFor("Settings/Advanced networking/Internet connection", "Settings/Advanced networking/Internet backup", "Settings/Advanced networking/Multi-WAN")
    }
    /// Math.Round (to even) printed the way C# prints a whole double.
    fileprivate static func rounded(_ d: Double) -> String {
        let r = d.rounded(.toNearestOrEven)
        return abs(r) < 1e15 ? String(Int(r)) : J.str(r)
    }

    func statsRange(_ days: Int = 7) -> String {
        let end = Date(); let start = end.addingTimeInterval(-Double(days) * 86400)
        return "start=" + escapeData(Dates.utcStamp(start)) + "&end=" + escapeData(Dates.utcStamp(end))
    }

    func dataUsagePage(_ data: Any?) {
        sub("How much data your network used. How far back you can look depends on your eero plan.")
        let days = usageDays
        group(nil,
              settingRow("Period", nil, days == 1 ? "Last day" : "Last \(days) days", rowAction("Change", { [unowned self] in
                  self.edit("Show data usage for", [Field("days", "Period", days == 1 ? "1 day" : "\(days) days", "choice", "1 day", "7 days", "30 days")], { [unowned self] v in
                      guard let n = Int(self.v(v, "days").split(" ")[0]) else { throw AppError.invalidData("Input string was not in a correct format.") }
                      self.usageDays = n
                      self.featureData[self.currentPage] = try await self.read(try self.featureUrl("data_usage?") + self.statsRange(self.usageDays) + "&cadence=daily") ?? JNull.shared
                      self.renderPage()
                  }, "Show")
              })),
              settingRow("Who used the most", "Usage for each device and profile in this period.", nil, rowActionAsync("View", { [unowned self] in
                  self.info("Usage by device and profile", try await self.read(try self.featureUrl("data_usage/breakdown?") + self.statsRange(self.usageDays)))
              })),
              settingRow("Usage reports", "eero can send you a usage summary every week or month.", nil, rowActionAsync("Change", { [unowned self] in
                  let settings = try await self.read(try self.featureUrl("data_usage/report_settings"))
                  self.edit("Usage reports", [Field("cadence", "Send a report", J.s(settings, "cadence") == "monthly" ? "Monthly" : "Weekly", "choice", "Weekly", "Monthly")], { [unowned self] v in
                      let weekly = self.v(v, "cadence") == "Weekly"
                      try await self.change("PUT", try self.featureUrl("data_usage/report_settings"), J.obj("cadence", weekly ? "weekly" : "monthly", "notification_day", weekly ? 1 : 31), false, "Usage reports set to " + self.v(v, "cadence").lowercased() + ".")
                  })
              })))
        addSeriesCharts(&page, data, true)
        relatedPagesFor("Activity/Usage details")
    }

    /// Recent changes on the network, newest first, as one plain line each.
    func networkEventsPage(_ data: Any?) {
        sub("Recent things that happened on your network, like devices joining, updates and pauses. Newest first.")
        let events = MainWindow.entries(data, "data", "events", "app_events")
        var rows: [Row] = []
        for item in events.prefix(50) {
            let what = J.first(item, "message", "title", "description", "summary").nullIfEmpty ?? MainWindow.friendly(J.first(item, "event_type", "type")).nullIfEmpty ?? "Network event"
            let kind = MainWindow.friendly(J.first(item, "event_type", "type", "category"))
            let whenKey = ["created", "timestamp", "occurred_at", "time"].first { !J.s(item, $0).isEmpty } ?? "created"
            let when = MainWindow.friendlyValue(whenKey, J.get(item, whenKey))
            rows.append(settingRow(what, !kind.isEmpty && kind != what ? kind : nil, when.nullIfEmpty, rowAction("Details", { [unowned self] in self.info("Network event", item) })))
        }
        if rows.isEmpty { rows.append(infoRow(data == nil ? "Events not loaded" : "Nothing new", nil, data == nil ? "eero hasn't returned your network's events. Choose Refresh at the top right to try again." : "Nothing has happened on your network recently.")) }
        group(nil, rows: rows)
        if events.count > 50 { page.append(text("Showing the 50 most recent events.", 12, "#64738A")) }
    }

    func insightsPage(_ data: Any?) {
        sub("Threats, ads and websites eero blocked for you in the last 7 days. Needs an eero plan with Advanced Security.")
        group(nil,
              settingRow("By device", "Which devices eero protected most.", nil, rowActionAsync("View", { [unowned self] in self.info("Blocked by device", try await self.read(try self.featureUrl("insights/devices?") + self.statsRange())) })),
              settingRow("By profile", "Blocking for each profile.", nil, rowActionAsync("View", { [unowned self] in self.info("Blocked by profile", try await self.read(try self.featureUrl("insights/profiles?") + self.statsRange())) })))
        addSeriesCharts(&page, data, false)
        relatedPagesFor("Home/Security & privacy", "Activity/Usage details")
    }

    func permissionsPage(_ data: Any?) {
        sub("People who can see and manage this network. Administrators can change every setting.")
        var people: [Row] = []
        for member in MainWindow.entries(J.get(data, "members"), "members") {
            let id = J.first(member, "user_id", "id"); let role = J.s(member, "user_role")
            let name = J.first(member, "user_name", "name", "user_email", "email", "phone")
            var actions: [ButtonSpec] = []
            if !id.isEmpty && role != "owner" && role != "admin" {
                actions.append(rowActionAsync("Make admin", { [unowned self] in
                    if !(await self.confirm("Invite " + name + " to become an administrator?")) { return }
                    try await self.change("POST", try self.featureUrl("member_promotion"), J.obj("member_id", try ExtendedContracts.identifier(id)), false, "Administrator invitation sent.")
                }))
            }
            if !id.isEmpty && role != "owner" {
                actions.append(rowActionAsync("Remove", { [unowned self] in
                    if !(await self.confirm("Remove administrator access for " + name + "?")) { return }
                    try await self.change("DELETE", try self.featureUrl("admins/" + escapeData(id)), nil, false, "Administrator access removed.")
                }, true))
            }
            people.append(settingRow(name.nullIfEmpty ?? "Member", role == "owner" ? "Owner" : role == "admin" ? "Administrator" : !role.isEmpty ? MainWindow.friendly(role) : "Member", nil, actions: actions))
        }
        if people.isEmpty { people.append(infoRow(data == nil ? "Members not loaded" : "Only you so far", nil, data == nil ? "Choose Refresh at the top right to try again." : "Invite someone to help manage this network.")) }
        let inviteButton = rowActionAsync("Create link", { [unowned self] in
            if self.preview { throw AppError.invalidOperation("Preview mode is read only.") }
            if !(await self.confirm("Create a link that grants administrator access to the person who accepts it?")) { return }
            let invite = try await self.api.send("POST", try self.featureUrl("invites"), RuntimeContracts.adminInvite())
            let link = J.s(invite, "invite_url")
            if link.isEmpty { throw AppError.invalidOperation("eero created a response without an invitation link. Refresh invitations before trying again.") }
            let p: [Block] = [self.text("Admin invitation", 23, "#17263C", true), self.text("Share this link only with the person you want to administer your network.", 13, "#64738A"), .view(InvitationLinkView(link: link))]
            let shown = self.dialog("Admin invitation", p, 560, 320)
            shown.showDialog(); await shown.waitClosed()
            try await self.loadFeature(self.currentPage); self.renderPage()
        })
        people.append(settingRow("Invite an administrator", "Creates a link. Whoever opens it can manage this network, so share it carefully.", nil, inviteButton))
        group("People", rows: people)
        var pending: [Row] = []
        for invite in MainWindow.entries(J.get(data, "invites"), "invites") {
            let id = J.first(invite, "invite_id", "id"); if id.isEmpty { continue }
            let created = J.first(invite, "created", "created_at")
            pending.append(settingRow(J.s(invite, "invite_nickname").nullIfEmpty ?? "Administrator invitation", !created.isEmpty ? "Created " + created : "Waiting to be accepted", nil,
                rowAction("Rename", { [unowned self] in
                    self.edit("Invitation nickname", [Field("name", "Nickname", J.s(invite, "invite_nickname")).help("Only you see this, to remember who the link is for.")], { [unowned self] v in
                        try MainWindow.validateName(self.v(v, "name"))
                        try await self.change("PUT", try self.featureUrl("invites/" + escapeData(id)), J.obj("invite_nickname", self.v(v, "name").trim()), false, "Invitation nickname updated.")
                    })
                }),
                rowActionAsync("Cancel", { [unowned self] in
                    if !(await self.confirm("Cancel this invitation? Its link will stop granting access.")) { return }
                    try await self.change("DELETE", try self.featureUrl("invites/" + escapeData(id)), nil, false, "Invitation canceled.")
                }, true)))
        }
        if pending.isEmpty { pending.append(infoRow("No pending invitations", nil, "Invitations you create appear here until someone accepts them.")) }
        if pending.count > 1 {
            pending.append(settingRow("Cancel all invitations", "Every pending link stops working.", nil, rowActionAsync("Cancel all", { [unowned self] in
                if !(await self.confirm("Cancel every pending administrator invitation for this network?")) { return }
                try await self.change("POST", try self.featureUrl("invites/cancel_pending_admin"), nil, false, "Pending administrator invitations canceled.")
            }, true)))
        }
        group("Pending invitations", rows: pending)
        relatedPages("Invitations to you")
    }

    /// "available", "current" or "unknown". can_update_now only says eero would accept an install
    /// request right now, so it never decides on its own whether an update exists.
    static func updateState(_ data: Any?, _ nodes: [Any]?) -> String {
        if data == nil || data is JNull { return "unknown" }
        let status = J.s(data, "update_status").lowercased()
        var hasUpdate: Bool? = J.get(data, "has_update") is Bool ? J.b(data, "has_update") : J.get(data, "update_available") is Bool ? J.b(data, "update_available") : nil
        if hasUpdate == nil && !status.isEmpty { hasUpdate = status.contains("available") || status == "pending" || status == "downloading" }
        // Every eero already running the target version means there is nothing new, whatever the flags say.
        let target = normalizeVersion(J.first(data, "update_to_firmware", "target_firmware"))
        let versions = (nodes ?? []).map { normalizeVersion(J.first($0, "os_version", "firmware_version")) }
        if !target.isEmpty && !versions.isEmpty && versions.allSatisfy({ $0 == target }) { return "current" }
        if hasUpdate == true { return "available" }
        if hasUpdate == false || status == "up_to_date" || status == "current" { return "current" }
        return "unknown"
    }
    static func normalizeVersion(_ version: String?) -> String {
        var v = (version ?? "").trim().lowercased()
        if v.hasPrefix("v") { v = String(v.dropFirst()) }
        return v
    }

    func updatesPage(_ data: Any?) {
        sub("eero installs software updates automatically. You can install a ready update now or pick when updates happen.")
        let state = MainWindow.updateState(data, nodes)
        let available = state == "available", ready = available && J.b(data, "can_update_now")
        let firmware = J.first(data, "update_to_firmware", "target_firmware")
        var installed: [String] = []
        for n in nodes { let v = J.first(n, "os_version", "firmware_version"); if !v.isEmpty && !installed.contains(v) { installed.append(v) } }
        let hourValue = Int(J.s(data, "preferred_update_hour")); let hasHour = hourValue != nil && hourValue! >= 0 && hourValue! <= 23; let hour = hourValue ?? 0
        let hours = (0..<24).map { MainWindow.hourLabel($0) }
        let install = rowActionAsync("Install now", { [unowned self] in
            if !(await self.confirm("Install the available eero software update? Your network will restart during installation.")) { return }
            try await self.change("POST", try self.netResource("updates"), "", false, "Software update requested. Installation can take several minutes.")
        }, false)
        install.enabled = ready
        group(nil,
              infoRow("Status", state == "current" ? "Up to date" : available ? "Update available" : "Not reported by eero"),
              !installed.isEmpty ? infoRow("Installed software", installed.joined(separator: ", "), installed.count > 1 ? "Your eeros are on different versions." : nil) : nil,
              available && !firmware.isEmpty ? infoRow("New version", firmware) : nil,
              settingRow("Install update", ready ? "An update is ready. Your network restarts while it installs." : available ? "An update exists, but eero isn't ready to install it yet. It installs automatically at the time below." : "Nothing to install. Your eeros are on the latest software.", nil, install),
              settingRow("Automatic update time", "Updates install around this hour in your network's timezone.", hasHour ? MainWindow.hourLabel(hour) : "Not set",
                         rowAction("Change", { [unowned self] in
                             self.edit("Automatic update time", [Field("hour", "Update time", hasHour ? MainWindow.hourLabel(hour) : MainWindow.hourLabel(3), "choice", choices: hours).help("Pick a time when nobody is likely to be online.")], { [unowned self] v in
                                 guard let n = hours.firstIndex(of: self.v(v, "hour")) else { throw AppError.argument("Choose an update time.") }
                                 try await self.change("POST", try self.netResource("updates") + "/preferred_update_hour", J.obj("preferred_update_hour", n), false, "Automatic update time set to " + hours[n] + ".")
                             })
                         })))
        page.append(text("The app asks eero to install its own firmware. It doesn't bypass eero's rollout schedule.", 13, "#64738A"))
    }

    func notificationsPage(_ data: Any?) {
        sub("Choose what eero tells you about. Phone alerts come from eero; Mac alerts come from this app.")
        notificationToggles("Network alerts", data, (try? featureUrl("notifications")) ?? "", "eero hasn't reported which network alerts are on.")
        notificationToggles("Account alerts", J.get(account, "push_settings"), "/2.2/account/push_settings", nil)
        group("History",
              settingRow("Recent notifications", "The last 50 alerts eero sent for this network.", nil, rowActionAsync("View", { [unowned self] in self.info("Recent notifications", try await self.read(try self.featureUrl("notifications_history?limit=50"))) })),
              settingRow("Unread notifications", "Check whether anything is waiting, or clear the unread count.", nil,
                         rowActionAsync("Check", { [unowned self] in
                             let value = try await self.read(self.apiNetworkPath("notifications/has_unread"))
                             let unread = (value as? Bool) ?? J.b(value, "has_unread")
                             self.setNotice(unread ? "You have unread eero notifications." : "No unread eero notifications.")
                         }),
                         rowActionAsync("Mark all read", { [unowned self] in try await self.change("POST", try self.featureUrl("notifications/mark_read"), nil, false, "Notifications marked read.") })))
        relatedPages("On this Mac")
    }
    static let notificationLabels: [String: String] = [
        "device_new": "A new device joins", "new_device": "A new device joins", "network_updated": "eero software updated", "network_offline": "Your network goes offline", "network_online": "Your network comes back online",
        "eero_offline": "An eero goes offline", "node_offline": "An eero goes offline", "speed_test": "Speed test results", "security": "Security alerts", "data_usage": "Data usage reports"]
    /// One switch per alert eero reports; the whole set is saved together, as eero expects.
    func notificationToggles(_ heading: String, _ data: Any?, _ url: String, _ missing: String?) {
        var flags: [(key: String, value: Bool)] = []
        if let map = data as? JObject { for (k, v) in map { if let b = v as? Bool { flags.append((k, b)) } } }
        if flags.isEmpty { if let m = missing { group(heading, infoRow(heading, "Not reported", m)) }; return }
        var rows: [Row] = []
        for flag in flags {
            let key = flag.key
            let label = MainWindow.notificationLabels[key.replacingOccurrences(of: ".", with: "_")] ?? MainWindow.friendly(key.replacingOccurrences(of: ".", with: "_"))
            rows.append(settingToggle(label, nil, flag.value) { [unowned self] on in
                let body = JObject()
                for p in flags { body[p.key] = p.key == key ? on : p.value }
                try await self.change("PUT", url, body, false, label + (on ? " alerts turned on." : " alerts turned off."))
            })
        }
        group(heading, rows: rows)
    }
    func editReturnedBooleans(_ title: String, _ data: Any?, _ url: String) throws {
        guard let map = data as? JObject else { throw AppError.invalidOperation("eero did not return editable settings.") }
        var fields: [Field] = []
        for (k, v) in map where v is Bool { fields.append(Field(k, MainWindow.human(k), v, "bool")) }
        if fields.isEmpty { throw AppError.invalidOperation("No editable notification flags were returned.") }
        edit(title, fields, { [unowned self] v in
            let body = JObject(); for f in fields { body[f.key] = self.flag(v, f.key) }
            try await self.change("PUT", url, body, false, "Notification preferences updated.")
        })
    }

    func troubleshootingPage() {
        sub("Tools to find and fix network problems. If something isn't working, start with Guided diagnostics.")
        featureMenu(currentPage, false)
        let networkId = (networkUrl ?? "").trimEnd("/").split("/").last ?? ""
        group("Checks & scans",
              settingRow("Network diagnostics", "Run eero's full health check, then view what it found.", nil,
                         rowActionAsync("Run", { [unowned self] in
                             if self.preview { throw AppError.invalidOperation("Sign in to run diagnostics.") }
                             try await self.api.send("POST", try self.netResource("diagnostics"))
                             self.setNotice("Diagnostics started. Choose View results in a minute.")
                         }),
                         rowActionAsync("View results", { [unowned self] in self.info("Network diagnostics", try await self.read(try self.netResource("diagnostics"))) })),
              settingRow("Connection to eero", "Confirms this Mac can reach eero's cloud service securely.", nil, rowActionAsync("Check", { [unowned self] in try await self.api.checkConnection(); self.setNotice("Secure connection to eero verified.") })),
              settingRow("Network scan", "Lists what your eeros can see on your network.", nil, rowActionAsync("View", { [unowned self] in self.info("Network scan", try await self.read(try self.featureUrl("network_scan"))) })),
              settingRow("Coverage scan", "Shows how strong the wifi signal is for each device.", nil, rowActionAsync("View", { [unowned self] in self.info("Coverage scan", try await self.read("/2.4/networks/" + escapeData(networkId) + "/placement/wifi_coverage_scan?allDevices=true")) })),
              settingRow("eero upgrade report", "Shows which devices could get faster wifi from newer eero hardware.", nil, rowActionAsync("View", { [unowned self] in self.info("Upgrade report", try await self.read("/2.4/networks/" + escapeData(networkId) + "/placement/wifi_generation_scan?allDevices=true")) })))
        addGroupHeading("More help")
        var wireless: [Row] = FeatureCatalog.entries.filter { $0.path == "Settings/Advanced networking/Wireless settings" }.map { listRow($0, false) }
        wireless.append(settingRow("eero help articles", "Opens eero's support website in your browser.", nil, rowActionAsync("Open ↗", { [unowned self] in try self.official("https://www.eero.com/support") })))
        page.append(rowGroup(wireless))
    }

    static func contact(_ value: Any?) -> String { (value as? String) ?? J.first(value, "value", "email", "phone", "number") }

    func accountPage() {
        sub("Your eero account details and sign-in.")
        let auth = J.s(account, "auth", "type")
        let email = MainWindow.contact(J.get(account, "email")), phone = MainWindow.contact(J.get(account, "phone"))
        group("Profile",
              settingRow("Name", nil, J.s(account, "name").nullIfEmpty ?? "Not set",
                         rowAction("Change", { [unowned self] in
                             self.edit("Account name", [Field("name", "Name", J.s(self.account, "name"))], { [unowned self] v in
                                 try MainWindow.validateName(self.v(v, "name"))
                                 try await self.change("PUT", "/2.2/account/name", J.obj("name", self.v(v, "name").trim()), true, "Account name updated.")
                             })
                         })),
              settingRow("Email", "eero sends a code to the new address. Enter it with Verify.", email.nullIfEmpty ?? "Not set",
                         rowAction("Change", { [unowned self] in
                             self.edit("Account email", [Field("email", "New email address").help("You'll get a verification code at this address.")], { [unowned self] v in
                                 let value = self.v(v, "email").trim()
                                 if !Regex.isMatch(value, "^[^\\s@]+@[^\\s@]+\\.[^\\s@]+$") { throw AppError.argument("Enter a valid email address.") }
                                 try await self.change("PUT", "/2.2/account/email", J.obj("email", value), true, "Email change requested. Choose Verify next to Email and enter the code.")
                             })
                         }),
                         rowAction("Verify", { [unowned self] in self.verifyContact("email") })),
              settingRow("Phone", "eero texts a code to the new number. Enter it with Verify.", phone.nullIfEmpty ?? "Not set",
                         rowAction("Change", { [unowned self] in
                             self.edit("Account phone", [Field("phone", "New phone number").help("Include the country code, for example +1 for the US and Canada.")], { [unowned self] v in
                                 try await self.change("PUT", "/2.2/account/phone", J.obj("phone", try PhoneInput.normalize(self.v(v, "phone"))), true, "Phone change requested. Choose Verify next to Phone and enter the code.")
                             })
                         }),
                         rowAction("Verify", { [unowned self] in self.verifyContact("phone") })),
              infoRow("Sign-in method", auth == "eero" ? "Email or text code" : auth == "amazon" ? "Amazon" : auth == "sso" ? "Internet provider" : "Not reported by eero"),
              !J.s(account, "role").isEmpty ? infoRow("Role", MainWindow.friendly(J.s(account, "role"))) : nil)
        group("Email preferences",
              settingToggle("eero marketing emails", "News, tips and offers from eero. Account and security emails are always sent.", J.b(account, "consents", "marketing_emails")) { [unowned self] on in
                  try await self.change("PUT", "/2.2/account/consents", J.obj("marketing_emails", on), true, on ? "Marketing emails turned on." : "Marketing emails turned off.")
              })
        relatedPages("More account settings")
        addGroupHeading("Danger zone")
        let deleteAccount = rowAction("Delete account", { [unowned self] in
            self.edit("Delete account", [Field("confirm", "Type DELETE MY EERO ACCOUNT to confirm")], { [unowned self] v in
                if self.v(v, "confirm") != "DELETE MY EERO ACCOUNT" { throw AppError.argument("The confirmation text does not match.") }
                if !(await self.confirm("Permanently delete your eero account and associated account data? This can remove your ability to manage networks.")) { return }
                if self.preview { throw AppError.invalidOperation("Preview mode is read only.") }
                try await self.api.send("DELETE", "/2.2/account")
                self.api.forget(); self.stopTimer(); self.clearData(); self.showLogin(false); self.setNotice("Account deletion request accepted.")
            }, "Delete account")
        }, true)
        page.append(rowGroup([settingRow("Delete eero account", "Permanently removes your account and its data. You may lose access to your networks.", nil, deleteAccount)], 14))
    }
    func verifyContact(_ type: String) {
        edit("Verify new " + type, [Field("code", "Verification code")], { [unowned self] v in
            let code = self.v(v, "code").trim()
            if !Regex.isMatch(code, "^[0-9]{4,10}$") { throw AppError.argument("Enter the numeric verification code.") }
            try await self.change("POST", "/2.2/account/" + type + "/verify", J.obj("code", code), true, "Account " + type + " verified.")
        }, "Verify")
    }

    func subscriptionPage() {
        sub("Your eero plan, partner benefits and purchases.")
        let status = present(J.get(network, "premium_status")) ?? J.get(account, "premium_status"), details = present(J.get(network, "premium_details")) ?? J.get(account, "premium_details")
        let plan = J.first(details, "tier", "plan", "name")
        let state = (status as? String) ?? J.first(status, "status", "state")
        group("Your plan",
              settingRow("Current plan", !state.isEmpty ? MainWindow.friendly(state) : preview ? "Sample account. Sign in to see your plan." : "eero didn't return a plan summary.", !plan.isEmpty ? MainWindow.friendly(plan) : "Not reported",
                         rowActionAsync("Details", { [unowned self] in self.info("Subscription details", try await self.read("/2.2/premium/customer")) })),
              settingRow("About eero Plus", "What's included in eero's paid plans. Opens eero's website.", nil, rowActionAsync("Open ↗", { [unowned self] in try self.official("https://www.eero.com/eero-plus") })))
        addGroupHeading("Manage")
        featureMenu("Settings/Account settings/Subscriptions", false)
    }

    func connectedPage(_ data: Any?) {
        sub("Amazon, Alexa and other services connected to this network.")
        group("Status",
              J.get(network, "amazon_account_linked") is Bool ? infoRow("Amazon account", J.b(network, "amazon_account_linked") ? "Linked" : "Not linked", "Linking is managed by eero. New links can't be created here.") : nil,
              J.get(network, "alexa_skill") is Bool ? infoRow("Alexa skill", J.b(network, "alexa_skill") ? "Enabled" : "Not enabled") : nil,
              J.get(network, "ffs") is Bool ? infoRow("Amazon Frustration-Free Setup", MainWindow.onOff(J.b(network, "ffs")), "Change this under Wireless settings.") : nil)
        if J.get(data, "enabled") is Bool {
            group("eero Built-in", settingToggle("eero Built-in", "Lets devices with eero built in, such as some Amazon Echo models, extend your wifi network.", J.b(data, "enabled")) { [unowned self] on in
                try await self.change("PUT", try self.featureUrl("proxied_nodes"), J.obj("enabled", on), false, on ? "eero Built-in turned on." : "eero Built-in turned off.")
            })
        } else {
            group("eero Built-in", settingRow("eero Built-in", "Lets devices with eero built in, such as some Amazon Echo models, extend your wifi network.", "Not reported",
                rowActionAsync("Change", { [unowned self] in
                    let settings = try await self.read(try self.featureUrl("proxied_nodes"))
                    self.edit("eero Built-in", [Field("enabled", "Use eero Built-in devices", J.b(settings, "enabled"), "bool")], { [unowned self] v in
                        try await self.change("PUT", try self.featureUrl("proxied_nodes"), J.obj("enabled", self.flag(v, "enabled")), false, "eero Built-in setting updated.")
                    })
                })))
        }
        relatedPages("Connected service settings")
    }

    func networkManagementPage() {
        sub("Ownership, organization and removal for this network. To switch networks, use the network selector at the top.")
        let link: (String) -> Row? = { [unowned self] path in FeatureCatalog.entries.first { $0.path == path }.map { self.listRow($0, false) } }
        if network == nil {
            page.append(rowGroup([link("Settings/User permissions/Join a network"), link("Settings/Network management/Pending transfers"), link("Home/Add or replace eero/Create a network")].compactMap { $0 }))
            return
        }
        group("Ownership",
              settingRow("Transfer ownership", "Give this network to another eero account. Nothing changes until they accept.", nil,
                         rowAction("Transfer", { [unowned self] in
                             self.edit("Transfer ownership", [
                                Field("name", "Recipient name"),
                                Field("method", "Send the invitation by", "Email", "choice", "Email", "Text message"),
                                Field("email", "Recipient email").when("method", "Email"),
                                Field("phone", "Recipient phone").when("method", "Text message").help("Include the country code, for example +1 for the US and Canada.")], { [unowned self] v in
                                    try MainWindow.validateName(self.v(v, "name")); let byEmail = self.v(v, "method") == "Email"
                                    let email = byEmail ? self.v(v, "email").trim() : "", phone = byEmail ? "" : try PhoneInput.normalize(self.v(v, "phone").trim())
                                    if byEmail && !Regex.isMatch(email, "^[^\\s@]+@[^\\s@]+\\.[^\\s@]+$") { throw AppError.argument("Enter a valid email address.") }
                                    if !(await self.confirm("Send an ownership-transfer invitation to this recipient? Ownership changes when the recipient accepts.")) { return }
                                    try await self.change("POST", try self.netResource("transfer"), J.obj("name", self.v(v, "name").trim(), "email", email, "phone", phone), true, "Ownership transfer requested.")
                                }, "Send invitation")
                         }),
                         rowAction("Check recipient", { [unowned self] in self.checkTransferRecipient() })))
        relatedPages("Transfers & organization")
        addGroupHeading("Set up")
        page.append(rowGroup([link("Home/Add or replace eero/Create a network")].compactMap { $0 }))
        addGroupHeading("Danger zone")
        let delete = rowAction("Delete network", { [unowned self] in
            self.edit("Delete network", [Field("confirm", "Type the current wifi name to confirm")], { [unowned self] v in
                if self.v(v, "confirm") != J.s(self.network, "name") { throw AppError.argument("The wifi name does not match.") }
                if !(await self.confirm("Delete this network from your account? This will disrupt its management and may require setting it up again.")) { return }
                if self.preview { throw AppError.invalidOperation("Preview mode is read only.") }
                try await self.api.send("DELETE", self.networkUrl ?? "")
                self.featureData.removeAll(); self.network = nil; self.networkUrl = nil
                try await self.loadAccount(); self.currentPage = "Home"; self.showShell(); self.setNotice("Network deleted.")
            }, "Delete network")
        }, true)
        page.append(rowGroup([settingRow("Delete this network", "Removes the network from your account. You may need to set up your eeros again.", nil, delete)], 14))
        page.append(text("Full Bluetooth setup and replacing the gateway eero still need the official eero app.", 13, "#64738A"))
    }

    func backupPage(_ data: Any?) {
        sub("If your main internet goes down, eero can switch to a backup, such as a phone hotspot, nearby wifi or cellular backup.")
        let status = J.get(data, "status"); let points = MainWindow.entries(J.get(data, "access_points"), "access_points")
        group(nil,
              settingToggle("Internet backup", "Switch to a backup connection automatically while your internet is down.", J.b(status, "backup_internet_enabled")) { [unowned self] on in
                  try await self.change("PUT", try self.featureUrl("backupinternet"), J.obj("backup_internet_enabled", on), false, on ? "Internet backup turned on." : "Internet backup turned off.")
              },
              settingRow("Test backup connection", "Checks that your eero can reach the backup networks below.", nil, rowActionAsync("Test", { [unowned self] in
                  if self.preview { throw AppError.invalidOperation("Sign in to test backup connectivity.") }
                  try await self.api.send("POST", try self.featureUrl("backup_access_points/connectivity_check"))
                  self.setNotice("Backup connection test started. Choose Refresh in a minute to see the result.")
              })))
        var rows: [Row] = []
        for item in points {
            rows.append(settingRow(J.s(item, "ssid").nullIfEmpty ?? "Backup wifi", J.get(item, "enabled") is Bool && !J.b(item, "enabled") ? "Off" : "On", nil,
                rowAction("Edit", { [unowned self] in self.backupEdit(item) }),
                rowActionAsync("Remove", { [unowned self] in
                    if !(await self.confirm("Remove this backup wifi network?")) { return }
                    try await self.change("DELETE", try self.featureUrl("backup_access_points/" + escapeData(J.s(item, "uuid"))), nil, false, "Backup wifi removed.")
                }, true)))
        }
        if rows.isEmpty { rows.append(infoRow("No backup networks yet", nil, "Add a phone hotspot or another wifi network your eero can reach.")) }
        rows.append(settingRow("Add a backup network", "Enter a wifi name and password, or search for networks nearby.", nil,
                               rowAction("Add", { [unowned self] in self.backupEdit(nil) }), rowAction("Find nearby", { [unowned self] in self.navigate("Settings/Advanced networking/Internet backup/Find backup wifi") })))
        if points.count > 1 { rows.append(settingRow("Backup order", "Choose which backup network eero tries first.", nil, rowAction("Change", { [unowned self] in self.backupPriority(points) }))) }
        group("Backup networks", rows: rows)
        group("Devices & cellular",
              settingRow("Devices allowed on backup", "Choose which devices can use the backup connection, to save hotspot or cellular data.", nil, rowAction("Choose", { [unowned self] in self.chooseBackupDevices() })),
              settingRow("Cellular backup usage", "How much data cellular backup has used.", nil, rowActionAsync("View", { [unowned self] in self.info("Cellular backup usage", try await self.read(try self.featureUrl("cellular_backup_usage"))) })),
              settingRow("Cellular backup history", "When cellular backup turned on and off.", nil, rowActionAsync("View", { [unowned self] in self.info("Cellular backup events", try await self.read(try self.featureUrl("cellular_backup_events"))) })))
        technicalDetails(status)
    }
    func backupEdit(_ data: Any?, _ discoveredSsid: String? = nil) {
        let isNew = data == nil || data is JNull
        edit(isNew ? "Add backup wifi" : "Edit backup wifi", [Field("ssid", "Backup wifi name", discoveredSsid ?? J.s(data, "ssid")), Field("password", "Backup wifi password", nil, "password"), Field("enabled", "Enabled", isNew || J.b(data, "enabled"), "bool")], { [unowned self] v in
            try MainWindow.validateSsid(self.v(v, "ssid")); if isNew || !self.v(v, "password").isEmpty { try MainWindow.validatePassword(self.v(v, "password")) }
            let id = isNew ? UUID().uuidString.lowercased() : J.s(data, "uuid")
            let body = J.obj("ssid", self.v(v, "ssid").trim(), "uuid", id); if !isNew { body["enabled"] = self.flag(v, "enabled") }
            if !self.v(v, "password").isEmpty { body["password"] = self.v(v, "password") }
            try await self.change(isNew ? "POST" : "PUT", try self.featureUrl("backup_access_points" + (isNew ? "" : "/" + escapeData(id))), body, false, "Backup wifi saved.")
        }, "Save", isNew ? "Use a backup hotspot that your eero can reach." : "Leave the password blank to keep the existing password.")
    }

    func multiWanPage(_ data: Any?) {
        sub("Use more than one internet connection, such as cable plus fiber or cellular. Only on supported gateways and firmware.")
        let modeValues = ["MULTI_WAN_AUTOMATIC", "MULTI_WAN_STRICT", "MULTI_WAN_LEGACY"], modeLabels = ["Automatic failover", "Strict", "Legacy"]
        let modeIndex = modeValues.firstIndex(of: J.first(data, "wan_mode", "mode")) ?? -1
        group(nil,
              settingRow("Routing mode", "Automatic failover moves traffic to the next connection when one fails. Strict keeps traffic on the connection you choose.", modeIndex >= 0 ? modeLabels[modeIndex] : "Not reported",
                         rowAction("Change", { [unowned self] in
                             self.edit("Routing mode", [Field("mode", "Routing mode", modeIndex >= 0 ? modeLabels[modeIndex] : modeLabels[0], "choice", choices: modeLabels).help("Automatic failover suits most homes. Use Legacy only if eero support suggests it.")], { [unowned self] v in
                                 guard let i = modeLabels.firstIndex(of: self.v(v, "mode")) else { throw AppError.argument("Index was outside the bounds of the array.") }
                                 let mode = modeValues[i]
                                 if !(await self.confirm("Change the routing mode? Your internet may drop briefly.")) { return }
                                 try await self.change("PUT", try self.featureUrl("wan/enable_multi_wan"), J.obj("wan_mode", mode), false, "Routing mode updated.")
                             })
                         })),
              settingRow("Find connections", "Look for internet connections plugged into your eeros.", nil, rowActionAsync("Find", { [unowned self] in self.info("Discovered connections", try await self.read(try self.featureUrl("wan/discover"))) })),
              settingRow("Connection limits", "How many internet connections this gateway supports.", nil, rowActionAsync("View", { [unowned self] in self.info("Connection limits", try await self.read(try self.featureUrl("wan/limits"))) })))
        var rows: [Row] = []
        let links = MainWindow.entries(data, "links")
        for link in links {
            let id = J.s(link, "link_id"); if id.isEmpty { continue }
            let type = MainWindow.friendly(J.s(link, "connection_type"))
            rows.append(settingRow(J.first(link, "name", "link_id"), "Priority " + (J.s(link, "priority").nullIfEmpty ?? "—") + (!type.isEmpty ? " · " + type : ""), nil,
                rowActionAsync("Details", { [unowned self] in self.info("Internet connection", try await self.read(try self.featureUrl("wan/links/" + escapeData(id)))) }),
                rowAction("Edit", { [unowned self] in self.editWanPriority(link, links) }),
                rowActionAsync("Remove", { [unowned self] in
                    if !(await self.confirm("Remove this connection? It may be your active internet connection.")) { return }
                    try await self.change("DELETE", try self.featureUrl("wan/links/" + escapeData(id)), nil, false, "Connection removed.")
                }, true)))
        }
        if rows.isEmpty { rows.append(infoRow("No extra connections yet", nil, data == nil ? "eero hasn't returned connection details. Choose Refresh at the top right to try again." : "Add a second internet connection plugged into one of your eeros.")) }
        rows.append(settingRow("Add a connection", "Set up another internet connection on an eero Ethernet port.", nil, rowAction("Add", { [unowned self] in try self.addWanLink() })))
        group("Internet connections", rows: rows)
    }
    func addWanLink() throws {
        var nodeChoices: [(key: String, value: String)] = []
        for n in nodes where !J.s(n, "url").isEmpty {
            let key = J.first(n, "location", "model") + " · " + (J.s(n, "url").split("/").last ?? "")
            if nodeChoices.contains(where: { $0.key == key }) { throw AppError.argument("An item with the same key has already been added.") }
            nodeChoices.append((key, J.s(n, "url").split("/").last ?? ""))
        }
        if nodeChoices.isEmpty { throw AppError.invalidOperation("No eero node identifiers were returned.") }
        edit("Add internet connection", [
            Field("name", "Connection name").help("For example Fiber or Cellular backup."),
            Field("node", "Gateway eero", nodeChoices[0].key, "choice", choices: nodeChoices.map { $0.key }),
            Field("port", "Ethernet port number").help("The port the modem is plugged into."),
            Field("priority", "Priority", "1").help("Lower numbers are used first."),
            Field("type", "Connection type", "DHCP", "choice", "DHCP", "Static IP", "PPPoE").help("Most modems use DHCP. Use the settings your ISP gave you."),
            Field("vlan", "VLAN ID (optional)"),
            Field("ip", "Static IPv4 address").when("type", "Static IP"),
            Field("mask", "Subnet mask").when("type", "Static IP"),
            Field("gateway", "Gateway address").when("type", "Static IP"),
            Field("username", "PPPoE username").when("type", "PPPoE"),
            Field("password", "PPPoE password", nil, "password").when("type", "PPPoE")], { [unowned self] v in
                try MainWindow.validateName(self.v(v, "name"))
                let nodeId = nodeChoices.first { $0.key == self.v(v, "node") }?.value
                guard let node = nodeId.flatMap({ Int($0) }), let port = Int(self.v(v, "port")), port >= 0, port <= 48, let priority = Int(self.v(v, "priority")), priority >= 0, priority <= 100 else {
                    throw AppError.argument("Enter valid port and priority values.")
                }
                let connectionType = self.v(v, "type") == "Static IP" ? "STATIC_IP" : self.v(v, "type") == "PPPoE" ? "PPPOE" : "DHCP"
                let body = J.obj("name", self.v(v, "name").trim(), "eero_id", node, "port_number", port, "priority", priority, "connection_type", connectionType)
                if connectionType == "STATIC_IP" {
                    body["static_ip"] = J.obj("static_ip", try FeatureContracts.address(self.v(v, "ip"), AddressFamily.interNetwork), "subnet_mask", try FeatureContracts.address(self.v(v, "mask"), AddressFamily.interNetwork), "gateway_ip", try FeatureContracts.address(self.v(v, "gateway"), AddressFamily.interNetwork))
                }
                if connectionType == "PPPOE" {
                    if self.v(v, "username").isEmpty || self.v(v, "password").isEmpty { throw AppError.argument("Enter the PPPoE username and password supplied by your ISP.") }
                    body["pppoe"] = J.obj("username", self.v(v, "username"), "password", self.v(v, "password"))
                }
                if !self.v(v, "vlan").isEmpty {
                    guard let vlan = Int(self.v(v, "vlan")), vlan >= 1, vlan <= 4094 else { throw AppError.argument("VLAN IDs must be between 1 and 4094.") }
                    body["vlan_id"] = String(vlan)
                }
                if !(await self.confirm("Add this " + self.v(v, "type") + " internet connection?")) { return }
                try await self.change("POST", try self.featureUrl("wan/links"), body, false, "Internet connection added.")
            }, "Add connection")
    }
    func editWanPriority(_ link: Any?, _ links: [Any]) {
        // OrderBy is stable: equal priorities keep their original order.
        let ordered = links.enumerated().sorted { a, b in
            let pa = Int(J.s(a.element, "priority")) ?? 0, pb = Int(J.s(b.element, "priority")) ?? 0
            return pa != pb ? pa < pb : a.offset < b.offset
        }.map { $0.element }
        let linkId = J.s(link, "link_id")
        let position = (ordered.firstIndex { J.s($0, "link_id") == linkId } ?? -1) + 1
        edit("WAN name & priority", [Field("name", "Connection name", J.s(link, "name")), Field("position", "Priority position (1 is highest)", String(position))], { [unowned self] v in
            try MainWindow.validateName(self.v(v, "name"))
            guard let index = Int(self.v(v, "position")), index >= 1, index <= ordered.count else { throw AppError.argument("Enter a priority position between 1 and \(ordered.count).") }
            var others: [Any] = ordered.filter { J.s($0, "link_id") != linkId }; others.insert(link ?? JNull.shared, at: index - 1)
            let baseline = Int(J.s(ordered.first, "priority")) ?? 0
            var updates: [Any] = []
            for (i, item) in others.enumerated() {
                let entry = J.obj("link_id", J.s(item, "link_id"), "name", J.s(item, "link_id") == linkId ? self.v(v, "name").trim() : J.s(item, "name"), "priority", baseline + i)
                if let metadata = J.get(item, "metadata") { entry["metadata"] = metadata }
                updates.append(entry)
            }
            if !(await self.confirm("Apply the WAN priorities? This can switch your active internet connection.")) { return }
            try await self.change("PUT", try self.featureUrl("wan/links"), J.obj("links", updates), false, "WAN names and priorities updated.")
        })
    }

    func helpPage() {
        sub("Fix a problem yourself, ask eero, or contact a person.")
        group("Fix it yourself",
              settingRow("Guided diagnostics", "Tell eero what's wrong and it checks your network for that problem.", nil, rowAction("Start", { [unowned self] in self.navigate("Settings/Troubleshooting/Guided diagnostics") })),
              settingRow("Troubleshooting tools", "Scans, network checks and setup help.", nil, rowAction("Open", { [unowned self] in self.navigate("Settings/Troubleshooting") })),
              settingRow("eero help articles", "Step-by-step guides on eero's website.", nil, rowAction("Open ↗", { [unowned self] in try self.openServiceWindow("eero support", "https://www.eero.com/support") })))
        group("Ask eero",
              settingRow("Chat with eero", "eero's assistant answers questions about your network and can suggest changes for you to approve.", nil, rowAction("Start chat", { [unowned self] in try self.openChat() })),
              settingRow("Past conversations", "Read or continue earlier chats with eero.", nil, rowAction("Open", { [unowned self] in try self.openChat(true) })))
        group("Contact a person",
              settingRow("Contact support", "Phone, email or chat options for your account.", nil, rowAction("Open", { [unowned self] in self.navigate("Help/Contact support") })),
              settingRow("Report a problem", "Send eero a description of the problem, the affected eeros and devices, and a screenshot.", nil, rowAction("Write report", { [unowned self] in self.navigate("Help/Report an issue") })),
              api.signedIn ? settingRow("Your support details", "What eero's support team sees for your account.", nil, rowActionAsync("View", { [unowned self] in self.info("Your support details", try await self.read("/2.2/account/eero_support")) })) : nil)
        group("About this app",
              settingRow("What works in this app", "Features that still need the official eero app, and why.", nil, rowAction("Open", { [unowned self] in self.navigate("Help/Service availability") })),
              settingRow("Version & every feature", "App version and a list of every page.", nil, rowAction("Open", { [unowned self] in self.navigate("Settings/About") })))
    }

    func aboutPage() {
        sub("Information about this app, the Mac it runs on, and the software it uses.")
        let info = Bundle.main.infoDictionary ?? [:]
        let versionText = (info["CFBundleShortVersionString"] as? String).flatMap { $0.nullIfEmpty } ?? "1.1"
        let executable = Bundle.main.executablePath ?? CommandLine.arguments[0]
        let modified = (try? FileManager.default.attributesOfItem(atPath: executable))?[.modificationDate] as? Date
        let built = modified.map { Dates.format($0, "MMMM d, yyyy") } ?? ""
        let bundlePath = Bundle.main.bundlePath
        let insideApp = bundlePath.hasSuffix(".app")
        let location = insideApp ? bundlePath : (executable as NSString).deletingLastPathComponent
        let dataFolder = AppPaths.dataDirectory

        // Header: icon, name, version and credit.
        page.append(card([.view(AboutHeroView(version: versionText))], 22))

        let os = ProcessInfo.processInfo.operatingSystemVersion
        let osText = "macOS " + "\(os.majorVersion).\(os.minorVersion)" + (os.patchVersion > 0 ? ".\(os.patchVersion)" : "")
        let osBuild = MainWindow.systemBuild()
        let arch = MainWindow.machineArchitecture()
        let webKit = (Bundle(identifier: "com.apple.WebKit")?.infoDictionary?["CFBundleShortVersionString"] as? String).flatMap { $0.nullIfEmpty } ?? "Not installed"
        let summary = "eero Desktop " + versionText + (!built.isEmpty ? " (built " + built + ")" : "") + "\n" + osText + (osBuild.isEmpty ? "" : " build " + osBuild) + " · " + arch + "\n"
            + "Swift app (SwiftUI + AppKit)" + "\n" + "WebKit " + webKit
        group("This copy",
              infoRow("Version", versionText + (!built.isEmpty ? " · built " + built : "")),
              infoRow("macOS", osText + (osBuild.isEmpty ? "" : " (build " + osBuild + ")") + " · " + arch),
              infoRow("Web component", "WebKit " + webKit, "Used for sign-in, billing and other eero web pages."),
              settingRow("Copy app details", "Copies the lines above, handy when asking for help.", nil, rowAction("Copy", { [unowned self] in
                  NSPasteboard.general.clearContents(); NSPasteboard.general.setString(summary, forType: .string); self.setNotice("App details copied.")
              })))
        group("Your data on this Mac",
              settingRow("Settings and saved sign-in", "Stored only for your Mac user account. Your eero sign-in is encrypted with a key kept in your Keychain when you choose Remember me.", nil,
                         rowAction("Open folder", { [unowned self] in
                             try? FileManager.default.createDirectory(atPath: dataFolder, withIntermediateDirectories: true)
                             self.openLocalPath(dataFolder, false)
                         })),
              settingRow("App location", location, nil, rowAction("Open folder", { [unowned self] in self.openLocalPath(location, insideApp) })))
        group("Good to know",
              infoRow("Independent app", nil, "Not made by, endorsed by or affiliated with eero or Amazon. It talks to the same eero service the official app uses."),
              infoRow("What it can do", nil, "Features depend on your eero hardware, software, account role and plan."))

        // Every feature as a clickable row, folded away so the details above stay on top.
        var all: [Block] = []
        var order: [String] = []; var byRoot: [String: [FeatureEntry]] = [:]
        for e in FeatureCatalog.entries where !e.parent.isEmpty {
            let root = FeatureCatalog.root(e.path)
            if byRoot[root] == nil { order.append(root); byRoot[root] = [] }
            byRoot[root]!.append(e)
        }
        for root in order { all.append(groupHeading(root)); all.append(rowGroup(byRoot[root]!.map { listRow($0, true) })) }
        let featureCount = FeatureCatalog.entries.filter { !$0.parent.isEmpty }.count
        let fixed: [Row] = [
            settingRow("What works in this app", "Features that still need the official eero app.", nil, rowAction("Open", { [unowned self] in self.navigate("Help/Service availability") })),
            settingRow("Help & support", "Diagnostics, chat with eero and contact options.", nil, rowAction("Open", { [unowned self] in self.navigate("Help") }))]
        page.append(.view(AllPagesView(rows: fixed, caption: "\(featureCount) pages, grouped by section. Click one to open it.", all: all)))

        let notices = Resources.url("THIRD-PARTY-NOTICES.txt").map { $0.path } ?? (bundlePath as NSString).appendingPathComponent("Contents/Resources/THIRD-PARTY-NOTICES.txt")
        let openFile: (String) -> ButtonSpec? = { [unowned self] path in Files.exists(path) ? self.rowAction("View", { [unowned self] in self.openLocalPath(path, false) }) : nil }
        group("Open-source & licenses",
              infoRow("Core Image", "Part of macOS", "Creates the wifi QR codes."),
              infoRow("SwiftUI and AppKit", "Part of macOS", "Draw the app's windows and controls."),
              infoRow("WebKit", "Part of macOS", "Shows eero's web pages inside the app."),
              infoRow("WireGuard", "Installed separately", "The WireGuard tools for macOS, used for the Guardian VPN tunnel."),
              settingRow("All notices", Files.exists(notices) ? "Licenses and image sources for everything included." : "Licenses and image sources are listed in THIRD-PARTY-NOTICES.txt in the release download.", nil, openFile(notices)))
    }
    static func systemBuild() -> String {
        var size = 0
        if sysctlbyname("kern.osversion", nil, &size, nil, 0) != 0 || size == 0 { return "" }
        var buffer = [CChar](repeating: 0, count: size)
        if sysctlbyname("kern.osversion", &buffer, &size, nil, 0) != 0 { return "" }
        return String(cString: buffer)
    }
    static func machineArchitecture() -> String {
        #if arch(arm64)
        let native = "arm64"
        #else
        let native = "x86_64"
        #endif
        // An Intel build running under Rosetta still reports the real chip.
        var translated: Int32 = 0; var size = MemoryLayout<Int32>.size
        if native == "x86_64" && sysctlbyname("sysctl.proc_translated", &translated, &size, nil, 0) == 0 && translated == 1 { return "arm64 (Rosetta)" }
        return native
    }
    /// Folders open in Finder; an app bundle or a file is revealed in its containing folder when asked.
    func openLocalPath(_ path: String, _ containingFolder: Bool) {
        if !Files.exists(path) { setNotice("Could not open " + path, true); return }
        if containingFolder { NSWorkspace.shared.activateFileViewerSelecting([URL(fileURLWithPath: path)]); return }
        if !NSWorkspace.shared.open(URL(fileURLWithPath: path)) { setNotice("Could not open " + path, true) }
    }

    func platformPage() {
        sub("What you can do in this app, and what still needs the official eero app. Some features also depend on your plan, hardware or internet provider.")
        group("Works in this app",
              infoRow("Adding eeros", nil, "Add by serial number, find nearby eeros with Bluetooth, automatic setup, and creating a new wired network."),
              infoRow("Plans & billing", nil, "Managing your plan, payment and purchases opens eero's own payment pages. Purchases made through another store stay with that store."),
              infoRow("Signing in with Amazon", nil, "Opens Amazon's sign-in page. Your Amazon login must already be linked to an eero account."),
              infoRow("Guardian VPN", nil, "Connect this Mac under Security & privacy › VPN. Needs a plan that includes it, WireGuard on this Mac and administrator permission."),
              infoRow("Chat & alerts", nil, "Chat and support depend on your eero account. Mac alerts work while the app is running, including in the background."),
              infoRow("Business & provider tools", nil, "Guest portals, eero Insight, license keys, PPPoE and VLAN on an existing network, and accessory and phone-line details."))
        group("Needs the official eero app",
              infoRow("Full Bluetooth setup", nil, "Setting up a brand-new eero entirely over Bluetooth."),
              infoRow("Replacing the gateway eero", nil, "Swapping the eero that connects to your modem."),
              infoRow("First-time PPPoE or static IP setup", nil, "Setting up a new network whose internet needs a login or fixed address."),
              infoRow("Cellular & accessory activation", nil, "Activating new cellular service or pairing new accessories."))
    }
}

/// The admin invitation link with a copy button; works while the page's action is still running.
struct InvitationLinkView: View {
    let link: String
    @State private var copied = false
    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(link).font(.system(size: 13, design: .monospaced)).textSelection(.enabled).fixedSize(horizontal: false, vertical: true)
                .padding(10).frame(maxWidth: .infinity, alignment: .leading)
                .background(RoundedRectangle(cornerRadius: 6).fill(Color(nsColor: .textBackgroundColor)))
                .overlay(RoundedRectangle(cornerRadius: 6).strokeBorder(Color(nsColor: .separatorColor)))
                .accessibilityLabel("Invitation link")
            Button(copied ? "Copied" : "Copy invitation link") {
                NSPasteboard.general.clearContents(); NSPasteboard.general.setString(link, forType: .string); copied = true
            }
        }.padding(.top, 4)
    }
}

/// About page header: app icon, name, tagline and version/credit chips.
struct AboutHeroView: View {
    let version: String
    var body: some View {
        HStack(alignment: .center, spacing: 24) {
            Image(nsImage: NSApp.applicationIconImage ?? NSImage()).resizable().interpolation(.high).frame(width: 72, height: 72)
            VStack(alignment: .leading, spacing: 0) {
                Text("eero Desktop").font(.system(size: 26, weight: .semibold))
                Text("An unofficial Mac app for managing your eero wifi.").font(.system(size: 14)).foregroundColor(Tone.muted.color).padding(.top, 2).padding(.bottom, 8)
                HStack(spacing: 8) {
                    ForEach(["Version " + version, "Made by Carlos B. (Los33)"], id: \.self) { chip in
                        Text(chip).font(.system(size: 12, weight: .semibold)).foregroundColor(.accentColor)
                            .padding(.horizontal, 9).padding(.vertical, 3)
                            .background(RoundedRectangle(cornerRadius: 4).fill(Color.accentColor.opacity(0.12)))
                    }
                }
            }
            Spacer(minLength: 0)
        }
    }
}

/// "Help & features" group whose last row shows or hides every page in the app.
struct AllPagesView: View {
    let rows: [Row]; let caption: String; let all: [Block]
    @State private var show = false
    var body: some View {
        let toggle = ButtonSpec(show ? "Hide" : "Show", local: true) { show.toggle() }
        VStack(alignment: .leading, spacing: 0) {
            BlockView(block: .heading("Help & features"))
            BlockView(block: .rows(rows + [.setting(title: "Every page in the app", description: caption, value: nil, actions: [toggle])]))
            if show { BlocksView(blocks: all) }
        }
    }
}
