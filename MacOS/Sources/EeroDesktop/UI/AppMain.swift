import AppKit
import SwiftUI

/// Entry point: command-line tools run without UI (like the Windows exe), otherwise the SwiftUI app starts.
@main
enum Program {
    static var args: [String] = Array(CommandLine.arguments.dropFirst())
    static func main() {
        debugLog("main args=" + args.joined(separator: " "))
        let a = args
        if a.first == "--self-test" { exit(Int32(Tests.run(a.count > 1 ? a[1] : "test-results.txt"))) }
        if a.count > 1 && a[0] == "--connection-test" { exit(Int32(ConnectionDiagnostics.run(a[1]))) }
        if a.count > 1 && a[0] == "--browser-runtime-test" { exit(Int32(ServiceLinks.checkRuntime(a[1]))) }
        if a.count > 1 && a[0] == "--bluetooth-test" { exit(Int32(NativeBluetooth.check(a[1]))) }
        if a.count == 2 && a[0] == "--vpn-control" { exit(Int32(MacVpn.elevatedControl(a[1]))) }
        if a.contains("--version") { print("eero Desktop (Unofficial) 1.1.0 for macOS"); exit(0) }
        UiPreferences.load()
        if let i = a.firstIndex(of: "--theme"), i + 1 < a.count, ["Light", "Dark", "System"].contains(a[i + 1]) { UiPreferences.theme = a[i + 1] }
        if a.contains("--background") && !UiPreferences.startAtLogin { exit(0) }
        // Build the window model before SwiftUI evaluates the scene, so its first render never
        // publishes changes from inside a view update.
        MainActor.assumeIsolated { _ = EeroDesktopApp.model }
        // The main window always opens at launch; don't let macOS window restoration keep it closed.
        UserDefaults.standard.set(false, forKey: "NSQuitAlwaysKeepsWindows")
        EeroDesktopApp.main()
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        debugLog("didFinishLaunching windows=\(NSApp.windows.count)")
        UiPreferences.applyTheme()
        if Program.args.contains("--background") { NSApp.setActivationPolicy(.accessory); for w in NSApp.windows where w.identifier?.rawValue.contains("main") == true { w.orderOut(nil) } }
        else { NSApp.activate(ignoringOtherApps: true) }
        // Start the session (or the render) even when the window is hidden, e.g. when started at login.
        DispatchQueue.main.async { EeroDesktopApp.onLaunch() }
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        // Background alerts keep running from the menu bar after the window closes.
        !(UiPreferences.background && EeroDesktopApp.model.api.signedIn && !EeroDesktopApp.model.preview)
    }
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { if !flag { EeroDesktopApp.model.restoreWindow() }; return true }
    func applicationWillTerminate(_ notification: Notification) { EeroDesktopApp.model.prepareForShutdown() }
}

struct EeroDesktopApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var delegate
    @MainActor static let model: MainWindow = {
        let a = Program.args
        let render = a.first == "--render"
        var statusPath: String? = nil
        if let i = a.firstIndex(of: "--status-file"), i + 1 < a.count { statusPath = a[i + 1] }
        return MainWindow(preview: a.contains("--preview") || a.contains("--theme-preview") || render, statusPath: statusPath, background: a.contains("--background"))
    }()
    @ObservedObject private var appState = AppState.shared
    @Environment(\.openWindow) private var openWindow

    var body: some Scene {
        let _ = AppState.shared.captureOpen(openWindow)
        Window("eero Desktop · Unofficial · v1.1", id: "main") {
            RootView()
                .environmentObject(EeroDesktopApp.model)
                .onAppear { EeroDesktopApp.onLaunch() }
        }
        .defaultSize(width: 1360, height: 880)
        .windowToolbarStyle(.unified(showsTitle: false))
        .commands {
            CommandGroup(replacing: .newItem) {}
            CommandMenu("Go") {
                Button("Back") { EeroDesktopApp.model.goBack() }.keyboardShortcut("[", modifiers: .command)
                Button("Refresh") { Task { await EeroDesktopApp.model.refreshAll() } }.keyboardShortcut("r", modifiers: .command)
                Divider()
                Button("Home") { EeroDesktopApp.model.navigate("Home") }.keyboardShortcut("1", modifiers: .command)
                Button("Devices") { EeroDesktopApp.model.navigate("Devices") }.keyboardShortcut("2", modifiers: .command)
                Button("Settings") { EeroDesktopApp.model.navigate("Settings") }.keyboardShortcut(",", modifiers: .command)
            }
        }
        MenuBarExtra("eero Desktop", systemImage: "wifi", isInserted: Binding(get: { appState.menuBarExtra }, set: { appState.menuBarExtra = $0 })) {
            Button("Open eero Desktop") { EeroDesktopApp.model.restoreWindow(); openWindow(id: "main") }
            Divider()
            Button("Quit") { EeroDesktopApp.model.prepareForShutdown(); NSApp.terminate(nil) }.keyboardShortcut("q")
        }
    }

    @MainActor static var launched = false
    @MainActor static func onLaunch() {
        if launched { return }
        launched = true
        debugLog("onLaunch args=\(Program.args) windows=\(NSApp.windows.count)")
        let a = Program.args
        if !a.contains("--background") && model.window == nil { AppState.shared.openMain?() }
        if a.contains("--theme-preview") { model.previewPage("Theme preview") }
        if a.contains("--chat-preview") { AuxiliaryWindows.open("Chat sample", MainWindow.chatSampleView(), model: model) }
        if a.first == "--render", a.count > 1 {
            if a.count > 2 && !a[2].hasPrefix("--") { model.previewPage(a[2]) }
            var size = CGSize(width: 1260, height: 820)
            if let i = a.firstIndex(of: "--render-size"), i + 1 < a.count {
                let d = a[i + 1].split("x"); if d.count == 2, let w = Double(d[0]), let h = Double(d[1]) { size = CGSize(width: w, height: h) }
            }
            Rendering.saveWhenReady(model, path: a[1], size: size)
            return
        }
        model.startup(amazonNativeSignIn: a.contains("--amazon-native-signin"))
    }
}

/// Developer tracing to stderr when EERO_DESKTOP_DEBUG is set.
func debugLog(_ message: String) {
    if ProcessInfo.processInfo.environment["EERO_DESKTOP_DEBUG"] != nil || Program.args.contains("--trace") { NSLog("eero-trace %@", message) }
}

/// Saves the main window to PNG once laid out (used by --render for screenshots).
@MainActor
enum Rendering {
    static func saveWhenReady(_ model: MainWindow, path: String, size: CGSize, attempt: Int = 0) {
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) {
            debugLog("render: model.window=\(model.window != nil) windows=\(NSApp.windows.map { "\($0.className) visible=\($0.isVisible)" })")
            guard let w = model.window ?? NSApp.windows.first(where: { $0.contentView != nil && !($0 is NSPanel) && $0.canBecomeMain }) else {
                if attempt < 50 { saveWhenReady(model, path: path, size: size, attempt: attempt + 1) }
                else { FileHandle.standardError.write(Data("Render failed: the window did not appear.\n".utf8)); exit(1) }
                return
            }
            w.makeKeyAndOrderFront(nil)
            w.setContentSize(size)
            DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) {
                if let view = w.contentView?.superview ?? w.contentView, let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds) {
                    view.cacheDisplay(in: view.bounds, to: rep)
                    try? rep.representation(using: .png, properties: [:])?.write(to: URL(fileURLWithPath: path))
                }
                NSApp.terminate(nil)
            }
        }
    }
}
