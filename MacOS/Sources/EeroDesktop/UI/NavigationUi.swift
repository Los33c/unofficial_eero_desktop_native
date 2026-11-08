import Foundation
import SwiftUI

// Navigation aids: settings search, back history, grouped settings menus and inline toggles.
extension MainWindow {
    /// First item of each array is the group heading; the rest are child page titles.
    static let settingsGroups: [[String]] = [
        ["Wifi", "Wifi name & password", "Guest wifi network", "Additional wifi networks", "Business guest wifi"],
        ["Network", "Nickname & location", "Advanced networking", "Software updates", "Connected services", "Network management"],
        ["People & account", "User permissions", "Account settings"],
        ["This app", "Appearance", "Notifications", "About"],
        ["Help", "Troubleshooting"]
    ]
    static let advancedGroups: [[String]] = [
        ["Internet & addressing", "Internet connection", "DHCP & NAT", "DNS", "IPv6", "Dynamic DNS", "Multiple static IPs"],
        ["Reliability", "Internet backup", "Multi-WAN"],
        ["Wireless & smart home", "Wireless settings", "Thread", "Power saving"],
        ["Ports & access", "Reservations & port forwarding", "UPnP"],
        ["Expert", "Network flags"]
    ]
    static let topPages: [FeatureEntry] = [
        FeatureEntry("Home", "Network overview, internet status, speed and your eeros", "Shell"),
        FeatureEntry("Devices", "Every connected device, its IP and MAC address, pause and block", "Shell"),
        FeatureEntry("Activity", "Data usage, security activity and network events", "Shell"),
        FeatureEntry("Settings", "Wifi, network, account and app preferences", "Shell")
    ]
    static func menuGroups(_ parent: String) -> [[String]]? { parent == "Settings" ? settingsGroups : parent == "Settings/Advanced networking" ? advancedGroups : nil }

    /// Titles of catalog children that a grouped menu does not place; kept empty by the self-tests.
    static func ungroupedMenuEntries() -> [String] {
        var missing: [String] = []
        for parent in ["Settings", "Settings/Advanced networking"] {
            let grouped = menuGroups(parent)!.flatMap { $0.dropFirst() }
            missing += FeatureCatalog.children(parent).filter { !grouped.contains($0.title) }.map { $0.path }
            missing += grouped.filter { t in !FeatureCatalog.children(parent).contains { $0.title == t } }.map { parent + "/" + $0 + " (stale)" }
        }
        return missing
    }
    static func searchCatalog(_ query: String?, _ within: String?, _ limit: Int) -> [FeatureEntry] {
        let words = (query ?? "").lowercased().splitRemovingEmpty(" /,")
        if words.isEmpty { return [] }
        return (topPages + FeatureCatalog.entries)
            .filter { within == nil || $0.path.hasPrefix(within! + "/") }
            .map { (entry: $0, score: searchScore($0, words)) }.filter { $0.score > 0 }
            .sorted { a, b in
                if a.score != b.score { return a.score > b.score }
                let da = a.entry.path.filter { $0 == "/" }.count, db = b.entry.path.filter { $0 == "/" }.count
                if da != db { return da < db }
                return a.entry.title.caseInsensitiveCompare(b.entry.title) == .orderedAscending
            }
            .prefix(limit).map { $0.entry }
    }
    static func searchScore(_ e: FeatureEntry, _ words: [String]) -> Int {
        let title = e.title.lowercased(), path = e.path.lowercased(), description = (e.description ?? "").lowercased()
        var total = 0
        for w in words {
            let s = title == w ? 10 : title.hasPrefix(w) ? 7 : title.contains(w) ? 5 : path.contains(w) ? 3 : description.contains(w) ? 1 : 0
            if s == 0 { return 0 }
            total += s
        }
        return total
    }
    static func breadcrumb(_ path: String) -> String {
        guard let i = path.lastIndex(of: "/") else { return "Page" }
        return path[..<i].replacingOccurrences(of: "/", with: " › ")
    }

    // MARK: History (⌘[ / mouse back)
    func goBack() {
        if busy || history.isEmpty || screen != .shell { return }
        let target = history.removeLast()
        navigateTo(target, false)
    }
    func recordHistory(_ route: String) {
        if route == currentPage { return }
        history.append(currentPage)
        if history.count > 50 { history.removeFirst() }
    }
    var backTooltip: String { history.isEmpty ? "Back (⌘[)" : "Back to " + (history.last!.split("/").last ?? "") + " (⌘[)" }

    // MARK: Grouped settings menus
    func routeValue(_ path: String) -> String {
        if network != nil {
            switch path {
            case "Settings/Wifi name & password": return J.s(network, "name")
            case "Settings/Nickname & location": return J.s(network, "timezone", "value")
            case "Settings/Advanced networking/IPv6": return J.get(network, "ipv6_upstream") is Bool ? (J.b(network, "ipv6_upstream") ? "On" : "Off") : ""
            case "Settings/Advanced networking/UPnP": return J.get(network, "upnp") is Bool ? (J.b(network, "upnp") ? "On" : "Off") : ""
            case "Settings/Advanced networking/DNS": let mode = J.s(network, "dns", "mode"); return mode == "custom" ? "Custom" : !mode.isEmpty ? "Automatic" : ""
            case "Settings/Advanced networking/Internet connection": let mode = J.s(network, "lease", "mode"); return mode == "static" ? "Static IP" : mode == "dhcp" ? "Automatic" : ""
            case "Settings/Advanced networking/Internet connection/ISP settings": return J.get(network, "pppoe_enabled") is Bool ? (J.b(network, "pppoe_enabled") ? "PPPoE on" : "Not used") : ""
            case "Settings/Advanced networking/Dynamic DNS": return J.get(network, "dynamic_dns", "enabled") is Bool ? MainWindow.onOff(J.b(network, "dynamic_dns", "enabled")) : ""
            case "Settings/Advanced networking/Power saving": return J.get(network, "power_saving") is Bool ? MainWindow.onOff(J.b(network, "power_saving")) : ""
            case "Settings/Advanced networking/Thread": return J.get(network, "thread") is Bool ? MainWindow.onOff(J.b(network, "thread")) : ""
            case "Settings/Advanced networking/DHCP & NAT": return !J.s(network, "connection", "mode").isEmpty ? dhcpModeLabel() : ""
            default: break
            }
        }
        if path == "Settings/Appearance" { return UiPreferences.theme == "System" ? "System setting" : UiPreferences.theme }
        if path == "Settings/Notifications" || path == "Settings/Notifications/Desktop notifications" { return UiPreferences.notifications ? "Mac alerts on" : "" }
        let children = FeatureCatalog.children(path).count
        return children >= 3 ? "\(children) settings" : ""
    }

    func listRow(_ entry: FeatureEntry, _ showPath: Bool) -> Row {
        let caption = (showPath ? MainWindow.breadcrumb(entry.path) + "  ·  " : "") + (entry.description ?? "")
        let (badge, why) = availabilityBadge(entry.path)
        let shownBadge = badge ?? (!(entry.limitation ?? "").isEmpty ? "Limited support" : nil)
        let value = routeValue(entry.path)
        return .link(icon: MainWindow.routeIcon(entry.path), title: entry.title, subtitle: caption.isEmpty ? nil : caption, value: value.isEmpty ? nil : value,
                     badge: shownBadge, tooltip: why.map { (badge ?? "") + ". " + $0 }, tile: true, action: { [unowned self] in self.navigate(entry.path) })
    }

    /// A card of stacked rows separated by hairlines.
    func rowGroup(_ rows: [Row], _ inset: CGFloat = 62) -> Block { .rows(rows) }
    func groupHeading(_ heading: String) -> Block { .heading(heading) }
    func addGroupHeading(_ heading: String) { page.append(.heading(heading)) }

    func nativeFeatureMenu(_ parent: String, _ explanation: Bool) {
        if explanation { sub(parent == "Settings" ? "Manage your wifi, network, account and app preferences." : "Choose a setting to view details and make changes.") }
        let entries = FeatureCatalog.children(parent)
        if entries.isEmpty { return }
        var grouped: [Block] = []
        if let groups = MainWindow.menuGroups(parent) {
            var placed = Set<String>()
            for group in groups {
                let members = group.dropFirst().compactMap { t in entries.first { $0.title == t } }
                if members.isEmpty { continue }
                members.forEach { placed.insert($0.path) }
                grouped.append(.heading(group[0]))
                grouped.append(rowGroup(members.map { listRow($0, false) }))
            }
            let rest = entries.filter { !placed.contains($0.path) }
            if !rest.isEmpty { grouped.append(.heading("More")); grouped.append(rowGroup(rest.map { listRow($0, false) })) }
        } else { grouped.append(rowGroup(entries.map { listRow($0, false) })) }
        if MainWindow.menuGroups(parent) != nil || entries.count > 6 {
            page.append(.view(MenuFilterView(parent: parent, grouped: grouped)))
        } else { page.append(contentsOf: grouped) }
    }

    /// Child pages of the current page, shown as a compact related-settings list.
    func relatedPages(_ heading: String = "Related settings") {
        let children = FeatureCatalog.children(currentPage)
        if children.isEmpty { return }
        addGroupHeading(heading)
        page.append(rowGroup(children.map { listRow($0, false) }))
    }
    /// Specific catalog pages shown as link rows under a "Related settings" heading.
    func relatedPagesFor(_ paths: String...) {
        let rows = paths.compactMap { p in FeatureCatalog.entries.first { $0.path == p } }.map { listRow($0, false) }
        if rows.isEmpty { return }
        addGroupHeading("Related settings")
        page.append(rowGroup(rows))
    }

    /// A row with a title, explanation and switch; the switch only changes after the save succeeds.
    func settingToggle(_ title: String, _ description: String?, _ value: Bool, _ apply: @escaping (Bool) async throws -> Void) -> Row {
        .toggle(title: title, description: description, value: value, apply: apply)
    }
    func networkSettingToggles(_ settings: [[String]], _ confirmation: String) {
        var rows: [Row] = []
        for setting in settings {
            let key = setting[0], title = setting[1]
            guard J.get(network, key) is Bool else { continue }
            rows.append(settingToggle(title, setting[2], J.b(network, key)) { [unowned self] on in
                if !(await self.confirm((on ? "Turn on " : "Turn off ") + title + "? " + confirmation)) { return }
                try await self.change("PUT", try self.netResource("settings"), J.obj(key, on), false, title + (on ? " turned on." : " turned off."))
            })
        }
        if rows.isEmpty { page.append(card([text("eero did not report these settings for the selected network.", 14, "#64738A")])); return }
        page.append(rowGroup(rows, 14))
    }

    /// Label / value line used by detail cards.
    func detailRow(_ label: String, _ value: String, _ flag: Bool) -> Block { .detailRow(label: label, value: value, flag: flag) }
}

/// The "Find a setting in …" filter above grouped menus.
struct MenuFilterView: View {
    let parent: String
    let grouped: [Block]
    @EnvironmentObject var model: MainWindow
    @State private var query = ""
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            TextField("Find a setting in " + (parent.split("/").last ?? parent), text: $query)
                .textFieldStyle(.roundedBorder).frame(maxWidth: 420).padding(.bottom, 18)
                .onSubmit { if let first = MainWindow.searchCatalog(query.trim(), parent, 1).first { model.navigate(first.path) } }
                .onExitCommand { query = "" }
            if query.trim().isEmpty { BlocksView(blocks: grouped) }
            else {
                let found = MainWindow.searchCatalog(query.trim(), parent, 40)
                BlockView(block: .heading(found.isEmpty ? "No matches" : "\(found.count)" + (found.count == 1 ? " match" : " matches")))
                if found.isEmpty {
                    BlockView(block: .card([.text(TextSpec(text: "Nothing in " + (parent.split("/").last ?? parent) + " matches “" + query.trim() + "”. The sidebar search (⌘F) looks across the whole app.", size: 13, tone: .muted))], padding: 18))
                } else { BlockView(block: .rows(found.map { model.listRow($0, true) })) }
            }
        }
    }
}
