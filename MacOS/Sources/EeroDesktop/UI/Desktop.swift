import AppKit
import Foundation
import SwiftUI
import UserNotifications

enum UiPreferences {
    static var theme = "System"
    static var notifications = false, background = false, startAtLogin = false
    static var allNetworks = true
    static let path = AppPaths.data("preferences.json")
    static func load() {
        guard let text = try? String(contentsOfFile: path, encoding: .utf8), let p = try? J.parse(text) else { return }
        let t = J.s(p, "theme"); if ["Light", "Dark", "System"].contains(t) { theme = t }
        notifications = J.b(p, "notifications"); background = J.b(p, "background"); startAtLogin = J.b(p, "start_at_login")
        allNetworks = J.get(p, "all_networks") == nil || J.b(p, "all_networks")
    }
    /// True when the app is showing its dark appearance (forced, or following macOS).
    static var dark: Bool {
        if theme == "Dark" { return true }
        if theme != "System" { return false }
        return NSApp?.effectiveAppearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
    }
    static func save() throws {
        try FileManager.default.createDirectory(atPath: AppPaths.dataDirectory, withIntermediateDirectories: true)
        try Files.writeText(J.stringify(J.obj("theme", theme, "notifications", notifications, "background", background, "start_at_login", startAtLogin, "all_networks", allNetworks)), path)
    }
    static func applyTheme() {
        NSApp.appearance = theme == "Dark" ? NSAppearance(named: .darkAqua) : theme == "Light" ? NSAppearance(named: .aqua) : nil
    }
}

protocol IStartupStore { func read() -> String?; func write(_ command: String?) throws }

/// Start at login with a per-user LaunchAgent (the macOS counterpart of the Windows Run key).
final class LaunchAgentStore: IStartupStore {
    static let label = "io.github.los33.EeroDesktop"
    let file: String
    init(_ path: String? = nil) {
        file = path ?? (NSHomeDirectory() as NSString).appendingPathComponent("Library/LaunchAgents/\(LaunchAgentStore.label).plist")
    }
    func read() -> String? {
        guard let dict = NSDictionary(contentsOfFile: file), let args = dict["ProgramArguments"] as? [String], let exe = args.first else { return nil }
        return "\"" + exe + "\"" + (args.count > 1 ? " " + args.dropFirst().joined(separator: " ") : "")
    }
    func write(_ command: String?) throws {
        guard let command = command else { try? FileManager.default.removeItem(atPath: file); return }
        guard let parts = Regex.match(command, "^\"([^\"]+)\"( --background)?$") else { throw AppError.argument("Choose an absolute macOS executable path for startup.") }
        var args = [parts[1]]; if !parts[2].isEmpty { args.append("--background") }
        let plist: [String: Any] = ["Label": LaunchAgentStore.label, "ProgramArguments": args, "RunAtLoad": true, "ProcessType": "Interactive", "LimitLoadToSessionType": "Aqua"]
        try FileManager.default.createDirectory(atPath: (file as NSString).deletingLastPathComponent, withIntermediateDirectories: true)
        let data = try PropertyListSerialization.data(fromPropertyList: plist, format: .xml, options: 0)
        try Files.writeAtomically(data, file)
        try? FileManager.default.setAttributes([.posixPermissions: 0o644], ofItemAtPath: file)
    }
}

enum StartupRegistration {
    static func command(_ executable: String?) throws -> String {
        guard let executable = executable, !executable.trim().isEmpty, executable.hasPrefix("/"), !executable.contains("\""), !executable.unicodeScalars.contains(where: { CharacterSet.controlCharacters.contains($0) }), !executable.lowercased().hasSuffix(".exe")
        else { throw AppError.argument("Choose an absolute macOS executable path for startup.") }
        return "\"" + (executable as NSString).standardizingPath + "\" --background"
    }
    static func apply(_ store: IStartupStore, _ executable: String?, _ enabled: Bool, _ savePreferences: () throws -> Void) throws {
        let desired = enabled ? try command(executable) : nil, previous = store.read()
        try store.write(desired)
        do { try savePreferences() } catch { try? store.write(previous); throw error }
    }
}

/// Shared state the SwiftUI scenes observe for background mode (menu bar extra).
@MainActor
final class AppState: ObservableObject {
    static let shared = AppState()
    /// Whether the menu bar icon is shown. SwiftUI writes this binding back on every scene update, so the
    /// setter only publishes real changes (publishing unchanged values re-evaluates the scene in a loop).
    @Published private(set) var menuBarExtraShown = false
    /// SwiftUI's openWindow(id: "main"), captured from the scene so AppKit code can reopen the window.
    var openMain: (() -> Void)?
    func captureOpen(_ action: OpenWindowAction) { if openMain == nil { openMain = { action(id: "main") } } }
    var menuBarExtra: Bool {
        get { menuBarExtraShown }
        set { if newValue != menuBarExtraShown { menuBarExtraShown = newValue } }
    }
}

extension MainWindow {
    func configureDesktopNotifications() {
        Dialogs.model = self
        AppState.shared.menuBarExtra = (UiPreferences.notifications || UiPreferences.background || backgroundLaunch) && !preview
        if UiPreferences.notifications && !preview {
            UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound]) { _, _ in }
            if notificationTimer == nil {
                notificationTimer = Timer.scheduledTimer(withTimeInterval: 60, repeats: true) { [weak self] _ in Task { @MainActor in await self?.pollNotifications() } }
            }
        } else { notificationTimer?.invalidate(); notificationTimer = nil }
    }
    func restoreWindow() {
        backgroundLaunch = false
        NSApp.setActivationPolicy(.regular)
        NSApp.activate(ignoringOtherApps: true)
        if let w = window { w.makeKeyAndOrderFront(nil) } else { AppState.shared.openMain?() }
    }
    func prepareForShutdown() { exiting = true; notificationTimer?.invalidate() }
    func sendNotification(_ id: String, _ title: String, _ body: String) {
        let content = UNMutableNotificationContent()
        content.title = String(title.prefix(63)); content.body = String(body.prefix(240)); content.sound = .default
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: id, content: content, trigger: nil)) { _ in }
    }
    func signInAttention() { sendNotification("sign-in", "eero Desktop needs sign-in", "Open eero Desktop from the menu bar to restore network alerts.") }
    func pollNotifications() async {
        if pollingNotifications || busy || !api.signedIn || (networkUrl ?? "").isEmpty || !UiPreferences.notifications { return }
        pollingNotifications = true; let epoch = notificationEpoch
        defer { pollingNotifications = false }
        do {
            if notificationJournal == nil { notificationJournal = NotificationJournal(AppPaths.data("notifications-state.dat")) }
            let owner = J.first(account, "id", "user_id", "email", "phone")
            let targets: [Any] = UiPreferences.allNetworks ? networkList : [J.obj("url", networkUrl, "name", J.s(network, "name"))]
            for target in targets {
                if !UiPreferences.notifications || !api.signedIn || epoch != notificationEpoch { return }
                let url = J.s(target, "url"); if url.isEmpty { continue }
                let data: Any?
                do { data = try await api.send("GET", url.trimEnd("/") + "/notifications_history?limit=50") }
                catch let e as ApiException { if e.sessionExpired { throw e }; continue }
                if !api.signedIn || epoch != notificationEpoch { return }
                let fresh = try notificationJournal!.observe(owner, url, entries(data, "notifications", "items"))
                if fresh.isEmpty || !UiPreferences.notifications { continue }
                let latest = fresh.last
                let title = fresh.count == 1 ? (J.s(latest, "title").nullIfEmpty ?? "eero network notification") : "\(fresh.count) eero network updates"
                let body = (J.s(target, "name").nullIfEmpty ?? "Your network") + ": " + (J.s(latest, "body").nullIfEmpty ?? "Open eero Desktop to view the update.")
                sendNotification("network-" + url, title, body)
            }
            try notificationJournal!.save()
        } catch let e as ApiException {
            if e.sessionExpired { api.forget(); clearData(); showLogin(false); setNotice("Sign in again to restore network alerts.", true); signInAttention() }
        } catch {}
    }

    func appearancePage() {
        sub("Choose how eero Desktop looks. This only changes this app for your Mac user account.")
        addGroupHeading("Theme")
        page.append(.view(ThemeTiles()))
        page.append(.spacer(18))
        group("More", settingRow("Text size and contrast", "macOS controls these for every app, including this one.", nil, rowAction("Open Accessibility settings ↗", {
            NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.universalaccess")!)
        })))
    }
    func desktopNotificationsPage() {
        sub("How this app alerts you on this Mac. These settings are saved for your Mac user account only.")
        let alerts = UiPreferences.notifications, background = UiPreferences.background, startup = UiPreferences.startAtLogin, all = UiPreferences.allNetworks
        group(nil,
              settingToggle("Mac alerts", "Show a notification when eero reports something new. Checks every minute while the app runs.", alerts) { [unowned self] on in try self.saveDesktopPreferences(on, background, startup, all) },
              settingToggle("Keep running when closed", "Stay in the menu bar after you close the window, so alerts keep coming.", background || startup) { [unowned self] on in try self.saveDesktopPreferences(alerts, on, on && startup, all) },
              settingToggle("Start at login", "Open quietly in the menu bar when you log in to your Mac. Keep the app in its current folder (Applications is best).", startup) { [unowned self] on in try self.saveDesktopPreferences(alerts, background || on, on, all) },
              settingToggle("Monitor all networks", "Alert about every network in your account, not just the one selected.", all) { [unowned self] on in try self.saveDesktopPreferences(alerts, background, startup, on) })
        page.append(text("Choosing Quit from the menu bar icon stops alerts until the next launch. Remember your eero session when you sign in so the app can restore it automatically.", 13, "#64738A"))
    }
    func saveDesktopPreferences(_ alerts: Bool, _ background: Bool, _ startup: Bool, _ all: Bool) throws {
        if preview { throw AppError.invalidOperation("Preview mode does not change start at login or your preferences.") }
        let old = (UiPreferences.notifications, UiPreferences.background, UiPreferences.startAtLogin, UiPreferences.allNetworks)
        do {
            try StartupRegistration.apply(LaunchAgentStore(), Bundle.main.executablePath, startup) {
                UiPreferences.notifications = alerts; UiPreferences.startAtLogin = startup; UiPreferences.background = background || startup; UiPreferences.allNetworks = all
                try UiPreferences.save()
            }
        } catch { (UiPreferences.notifications, UiPreferences.background, UiPreferences.startAtLogin, UiPreferences.allNetworks) = old; throw error }
        configureDesktopNotifications(); renderPage(); setNotice("Notification preferences saved.")
    }
}

/// Light / Dark / Match system tiles with small previews of each look.
struct ThemeTiles: View {
    @EnvironmentObject var model: MainWindow
    var body: some View {
        HStack(alignment: .top, spacing: 16) {
            ForEach([("System", "Match system", "Follows your Mac's light or dark appearance"), ("Light", "Light", "Always light"), ("Dark", "Dark", "Always dark")], id: \.0) { value, label, note in
                let chosen = UiPreferences.theme == value
                Button {
                    if model.busy || chosen { return }
                    UiPreferences.theme = value; try? UiPreferences.save(); UiPreferences.applyTheme(); model.renderPage()
                    model.setNotice("Theme changed to " + (value == "System" ? "match your Mac." : value.lowercased() + "."))
                } label: {
                    VStack(alignment: .leading, spacing: 10) {
                        ThemeSample(theme: value).frame(height: 130).clipShape(RoundedRectangle(cornerRadius: 8))
                        HStack(alignment: .top) {
                            VStack(alignment: .leading, spacing: 2) { Text(label).font(.system(size: 14, weight: .semibold)); Text(note).font(.system(size: 12)).foregroundColor(.secondary) }
                            Spacer()
                            if chosen { Image(systemName: "checkmark").foregroundColor(.accentColor) }
                        }
                    }
                    .padding(12)
                    .background(RoundedRectangle(cornerRadius: 10).fill(Color(nsColor: .controlBackgroundColor)))
                    .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(chosen ? Color.accentColor : Color(nsColor: .separatorColor), lineWidth: chosen ? 2 : 0.5))
                }
                .buttonStyle(.plain).accessibilityLabel(label + " theme" + (chosen ? ", selected" : ""))
            }
        }
    }
}
struct ThemeSample: View {
    let theme: String
    var body: some View {
        if theme == "System" { HStack(spacing: 0) { half(false); half(true) } } else { half(theme == "Dark") }
    }
    func half(_ dark: Bool) -> some View {
        let fg = dark ? Color(red: 0.95, green: 0.95, blue: 0.96) : Color(red: 0.13, green: 0.14, blue: 0.27)
        return VStack(alignment: .leading, spacing: 0) {
            Text("eero").font(.system(size: 20, weight: .light)).foregroundColor(fg)
            VStack(alignment: .leading, spacing: 6) {
                Text("Internet").font(.system(size: 11)).foregroundColor(fg)
                RoundedRectangle(cornerRadius: 3).fill(dark ? Color(red: 0.51, green: 0.67, blue: 1) : Color(red: 0, green: 0.34, blue: 1)).frame(width: 54, height: 5)
                RoundedRectangle(cornerRadius: 3).fill(dark ? Color(white: 0.25) : Color(white: 0.88)).frame(width: 80, height: 5)
            }
            .padding(.horizontal, 10).padding(.vertical, 8)
            .background(RoundedRectangle(cornerRadius: 5).fill(dark ? Color(white: 0.155) : .white))
            .overlay(RoundedRectangle(cornerRadius: 5).strokeBorder(dark ? Color(white: 0.25) : Color(white: 0.88)))
            .padding(.top, 10)
            Spacer(minLength: 0)
        }
        .padding(12).frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .background(dark ? Color(white: 0.11) : Color(white: 0.953))
    }
}
