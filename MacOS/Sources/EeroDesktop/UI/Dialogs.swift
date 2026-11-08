import AppKit
import SwiftUI

/// A dialog shown as a sheet on the main window (Windows: owned modal window).
/// `showDialog()` does not block; `await waitClosed()` when code must run after it closes.
@MainActor
final class DialogHandle {
    let panel: NSPanel
    private var continuation: [CheckedContinuation<Bool, Never>] = []
    private(set) var isClosed = false
    var result = false
    var canClose = true
    var onClosed: (() -> Void)?
    init(panel: NSPanel) { self.panel = panel }
    func showDialog() { Dialogs.present(self) }
    @discardableResult func waitClosed() async -> Bool {
        if isClosed { return result }
        return await withCheckedContinuation { continuation.append($0) }
    }
    /// User-initiated close (Close/Cancel buttons): ignored while a save is running.
    func requestClose() { if canClose { close() } }
    func close(_ result: Bool) { self.result = result; close() }
    func close() {
        guard !isClosed else { return }
        isClosed = true
        if let parent = panel.sheetParent { parent.endSheet(panel) } else { panel.orderOut(nil) }
        Dialogs.open.removeAll { $0 === self }
        onClosed?()
        for c in continuation { c.resume(returning: result) }
        continuation.removeAll()
    }
}

@MainActor
enum Dialogs {
    static var open: [DialogHandle] = []
    static weak var model: MainWindow?

    /// The window a new sheet should attach to: the deepest open sheet of the main window.
    static func host() -> NSWindow? {
        var w: NSWindow? = model?.window ?? NSApp.mainWindow ?? NSApp.windows.first { $0.isVisible && !($0 is NSPanel) }
        while let sheet = w?.attachedSheet { w = sheet }
        return w
    }
    static func present(_ d: DialogHandle) {
        open.append(d)
        if let host = host() { host.beginSheet(d.panel) { _ in } }
        else { d.panel.center(); d.panel.makeKeyAndOrderFront(nil) }
    }
    static func closeAll() { for d in open.reversed() { d.close() } }

    static func makePanel<V: View>(_ title: String, _ content: V, width: CGFloat = 620, height: CGFloat = 700, model: MainWindow) -> NSPanel {
        let panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: width, height: height), styleMask: [.titled, .resizable, .fullSizeContentView], backing: .buffered, defer: false)
        panel.title = title
        panel.minSize = NSSize(width: 480, height: 260)
        panel.isReleasedWhenClosed = false
        panel.contentView = NSHostingView(rootView: content.environmentObject(model))
        return panel
    }

    /// Asks before a change (NSAlert sheet). Returns true for Continue.
    static func confirm(_ message: String) async -> Bool {
        let alert = NSAlert()
        alert.messageText = "Confirm change"; alert.informativeText = message; alert.alertStyle = .warning
        let go = alert.addButton(withTitle: "Continue"); alert.addButton(withTitle: "Cancel").keyEquivalent = "\u{1b}"
        go.keyEquivalent = ""
        if MainWindow.dangerWords.contains(where: { message.hasPrefix($0.trim()) }) { go.hasDestructiveAction = true }
        return await run(alert) == .alertFirstButtonReturn
    }
    static func prompt(_ title: String, _ value: String?) async -> String? {
        let alert = NSAlert()
        alert.messageText = title
        let field = NSTextField(frame: NSRect(x: 0, y: 0, width: 300, height: 24)); field.stringValue = value ?? ""
        field.setAccessibilityLabel(title)
        alert.accessoryView = field
        alert.addButton(withTitle: "Save"); alert.addButton(withTitle: "Cancel")
        alert.window.initialFirstResponder = field
        let response = await run(alert)
        let text = String(field.stringValue.prefix(100))
        return response == .alertFirstButtonReturn ? text : nil
    }
    static func run(_ alert: NSAlert) async -> NSApplication.ModalResponse {
        guard let host = host() else { return alert.runModal() }
        return await withCheckedContinuation { c in alert.beginSheetModal(for: host) { c.resume(returning: $0) } }
    }

    static func form(_ model: MainWindow, _ spec: EditForm) {
        let state = EditFormState(spec)
        var handle: DialogHandle!
        let view = EditFormView(state: state, close: { handle.requestClose() })
        handle = DialogHandle(panel: makePanel(spec.title, view, width: 560, height: 520, model: model))
        state.handle = handle
        handle.onClosed = { state.wipeSecrets() }
        handle.showDialog()
    }
}

extension MainWindow {
    /// A dialog with page blocks (Windows `Dialog(title, body)`).
    @discardableResult func dialog(_ title: String, _ body: [Block], _ width: CGFloat = 620, _ height: CGFloat = 700) -> DialogHandle {
        var handle: DialogHandle!
        let view = DialogContent(title: title, blocks: body, close: { handle.requestClose() })
        handle = DialogHandle(panel: Dialogs.makePanel(title, view, width: width, height: height, model: self))
        return handle
    }
    /// A dialog whose content is any SwiftUI view (for custom panels such as chat or the device editor).
    @discardableResult func dialog<V: View>(_ title: String, view: V, _ width: CGFloat = 620, _ height: CGFloat = 700) -> DialogHandle {
        var handle: DialogHandle!
        let content = DialogFrame(title: title, close: { handle.requestClose() }) { view }
        handle = DialogHandle(panel: Dialogs.makePanel(title, content, width: width, height: height, model: self))
        return handle
    }
    func confirm(_ message: String) async -> Bool { await Dialogs.confirm(message) }
    func prompt(_ title: String, _ value: String?) async -> String? { await Dialogs.prompt(title, value) }
}

struct DialogFrame<Content: View>: View {
    let title: String; let close: () -> Void
    @ViewBuilder let content: () -> Content
    var body: some View {
        VStack(spacing: 0) {
            ScrollView { content().padding(28).frame(maxWidth: .infinity, alignment: .leading) }
            Divider()
            HStack { Spacer(); Button("Close", action: close).keyboardShortcut(.cancelAction) }.padding(12)
        }
    }
}
struct DialogContent: View {
    let title: String; let blocks: [Block]; let close: () -> Void
    var body: some View { DialogFrame(title: title, close: close) { BlocksView(blocks: blocks) } }
}

// MARK: - Edit form

struct EditForm {
    let title: String; let description: String?; let fields: [Field]; let submit: String
    let save: ([String: Any]) async throws -> Void
}

@MainActor
final class EditFormState: ObservableObject {
    let spec: EditForm
    @Published var texts: [String: String] = [:]
    @Published var bools: [String: Bool] = [:]
    @Published var choices: [String: String] = [:]
    @Published var status = ""
    @Published var working = false
    weak var handle: DialogHandle?
    init(_ spec: EditForm) {
        self.spec = spec
        for f in spec.fields {
            switch f.type {
            case "bool": bools[f.key] = (f.value as? Bool) ?? false
            case "choice": let current = J.str(f.value); choices[f.key] = f.choices.contains(current) ? current : (f.choices.first ?? "")
            case "password": texts[f.key] = ""
            default: texts[f.key] = J.str(f.value)
            }
        }
    }
    func value(_ f: Field) -> Any {
        switch f.type {
        case "bool": return bools[f.key] ?? false
        case "choice": return choices[f.key] ?? ""
        case "hidden": return f.value ?? JNull.shared
        case "multiline", "long": return String((texts[f.key] ?? "").prefix(4096))
        case "password": return String((texts[f.key] ?? "").prefix(512))
        default: return String((texts[f.key] ?? "").prefix(254))
        }
    }
    /// A field whose controlling field is hidden is hidden too, so nested conditions work in order.
    func visibleKeys() -> Set<String> {
        var hidden = Set<String>(), visible = Set<String>()
        for f in spec.fields {
            if f.type == "hidden" { hidden.insert(f.key); continue }
            if let when = f.whenKey, spec.fields.contains(where: { $0.key == when }) {
                let current = J.str(spec.fields.first { $0.key == when }.map { value($0) })
                let show = !hidden.contains(when) && f.whenValues.contains { $0.caseInsensitiveCompare(current) == .orderedSame }
                if !show { hidden.insert(f.key); continue }
            }
            visible.insert(f.key)
        }
        return visible
    }
    func commit(_ model: MainWindow) {
        if working { return }
        working = true; status = "Saving…"; handle?.canClose = false
        var values: [String: Any] = [:]
        for f in spec.fields where f.type != "link" { values[f.key] = value(f) }
        Task { @MainActor in
            defer { self.working = false; self.handle?.canClose = true }
            do { try await spec.save(values); self.handle?.close() }
            catch {
                self.status = MainWindow.message(error)
                if let auth = error as? ApiException, auth.sessionExpired {
                    self.handle?.close(); model.api.forget(); model.clearData(); model.showLogin(false); model.setNotice("Your session expired. Sign in again.", true)
                }
            }
        }
    }
    func wipeSecrets() { for f in spec.fields where f.type == "password" { texts[f.key] = "" } }
}

struct EditFormView: View {
    @ObservedObject var state: EditFormState
    let close: () -> Void
    @EnvironmentObject var model: MainWindow
    var body: some View {
        let visible = state.visibleKeys()
        VStack(spacing: 0) {
            ScrollView {
                VStack(alignment: .leading, spacing: 6) {
                    Text(state.spec.title).font(.system(size: 22, weight: .semibold))
                    if let d = state.spec.description { Text(d).font(.system(size: 13)).foregroundColor(.secondary).fixedSize(horizontal: false, vertical: true) }
                    ForEach(state.spec.fields.filter { visible.contains($0.key) || $0.type == "link" }, id: \.key) { f in field(f).padding(.top, 8) }
                    if !state.status.isEmpty { Text(state.status).font(.system(size: 13)).foregroundColor(Tone.danger.color).fixedSize(horizontal: false, vertical: true).padding(.top, 6) }
                }.padding(28).frame(maxWidth: .infinity, alignment: .leading)
            }
            Divider()
            HStack {
                Spacer()
                Button("Cancel") { if !state.working { close() } }.keyboardShortcut(.cancelAction).disabled(state.working)
                Button(state.spec.submit) { state.commit(model) }.buttonStyle(.borderedProminent).disabled(state.working)
                    .keyboardShortcut(state.spec.fields.contains { $0.type == "multiline" } ? nil : .defaultAction)
            }.padding(12)
        }
        .frame(minWidth: 480, minHeight: 240)
    }
    @ViewBuilder func field(_ f: Field) -> some View {
        switch f.type {
        case "link":
            Button(f.label) { do { try model.official(J.str(f.value)) } catch { state.status = MainWindow.message(error) } }
        case "bool":
            VStack(alignment: .leading, spacing: 2) {
                Toggle(f.label, isOn: Binding(get: { state.bools[f.key] ?? false }, set: { state.bools[f.key] = $0 })).toggleStyle(.switch)
                if let h = f.helpText, !h.isEmpty { Text(h).font(.system(size: 12)).foregroundColor(.secondary).fixedSize(horizontal: false, vertical: true) }
            }
        default:
            VStack(alignment: .leading, spacing: 4) {
                Text(f.label).font(.system(size: 13, weight: .semibold))
                if let h = f.helpText, !h.isEmpty { Text(h).font(.system(size: 12)).foregroundColor(.secondary).fixedSize(horizontal: false, vertical: true) }
                switch f.type {
                case "choice":
                    Picker("", selection: Binding(get: { state.choices[f.key] ?? "" }, set: { state.choices[f.key] = $0 })) { ForEach(f.choices, id: \.self) { Text($0).tag($0) } }
                        .labelsHidden().accessibilityLabel(f.label)
                case "password":
                    SecureField("", text: Binding(get: { state.texts[f.key] ?? "" }, set: { state.texts[f.key] = String($0.prefix(512)) })).textFieldStyle(.roundedBorder).accessibilityLabel(f.label)
                case "multiline":
                    TextEditor(text: Binding(get: { state.texts[f.key] ?? "" }, set: { state.texts[f.key] = String($0.prefix(4096)) }))
                        .font(.system(size: 13)).frame(minHeight: 80, maxHeight: 240).overlay(RoundedRectangle(cornerRadius: 5).strokeBorder(Color(nsColor: .separatorColor))).accessibilityLabel(f.label)
                default:
                    TextField("", text: Binding(get: { state.texts[f.key] ?? "" }, set: { state.texts[f.key] = String($0.prefix(f.type == "long" ? 4096 : 254)) }))
                        .textFieldStyle(.roundedBorder).accessibilityLabel(f.label).onSubmit { state.commit(model) }
                }
            }
        }
    }
}

/// Free-standing windows (chat, secure browser) owned by the app (Windows: owned non-modal windows).
@MainActor
enum AuxiliaryWindows {
    static var windows: [NSWindow] = []
    @discardableResult static func open<V: View>(_ title: String, _ view: V, model: MainWindow, width: CGFloat = 960, height: CGFloat = 780) -> NSWindow {
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: width, height: height), styleMask: [.titled, .closable, .resizable, .miniaturizable], backing: .buffered, defer: false)
        w.title = title; w.isReleasedWhenClosed = false
        w.contentView = NSHostingView(rootView: view.environmentObject(model))
        w.center(); w.makeKeyAndOrderFront(nil)
        windows.append(w)
        NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: w, queue: .main) { _ in
            Task { @MainActor in windows.removeAll { $0 === w } }
        }
        return w
    }
    static func adopt(_ w: NSWindow) {
        windows.append(w)
        NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: w, queue: .main) { _ in Task { @MainActor in windows.removeAll { $0 === w } } }
    }
    static func closeAll() { for w in windows { w.close() }; windows.removeAll() }
}
