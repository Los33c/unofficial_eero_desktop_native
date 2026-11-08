import AppKit
import Foundation
import SwiftUI

fileprivate func escapeData(_ s: String) -> String {
    s.addingPercentEncoding(withAllowedCharacters: CharacterSet(charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~")) ?? s
}

// Translated from ConversationUi.cs: the chat page and the free-standing chat window.
extension MainWindow {
    /// The open chat window's model; one chat window is reused like the Windows owned window.
    var chatWindow: ConversationModel? { get { state["chatWindow"] as? ConversationModel } set { state["chatWindow"] = newValue } }

    /// Sample chat window content for --chat-preview and --render-chat (no account, no requests).
    static func chatSampleView() -> AnyView {
        let chat = ConversationModel(nil, "sample-network", "Sample home", true, false)
        chat.preview()
        return AnyView(ConversationView(chat: chat).frame(minWidth: 780, minHeight: 600))
    }
    func openChat(_ history: Bool = false) throws {
        if (networkUrl ?? "").isEmpty { throw AppError.invalidOperation("Select a network before opening chat or its support history.") }
        if let existing = chatWindow, let w = existing.window, !existing.closed { w.makeKeyAndOrderFront(nil); return }
        if !history { let id = networkId; Task { @MainActor in try? await self.warmChat(id) } }
        let name = J.s(network, "name")
        let chat = ConversationModel(api, networkId, name, preview, history)
        let w = AuxiliaryWindows.open("Chat with eero · " + name, ConversationView(chat: chat).frame(minWidth: 780, minHeight: 600), model: self, width: 1060, height: 820)
        w.minSize = NSSize(width: 780, height: 600)
        chat.window = w; chatWindow = chat
        NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: w, queue: .main) { [weak self, weak chat] _ in
            Task { @MainActor in
                chat?.close()
                if let self = self, let chat = chat, self.chatWindow === chat { self.chatWindow = nil }
            }
        }
    }
    func chatPage(_ history: Bool) {
        sub(history ? "Read or continue your earlier conversations with eero." : "Ask eero about your network. It can suggest changes, and nothing changes until you approve it.")
        let open = accent(rowAction(history ? "Open history" : "Start chat", { [unowned self] in try self.openChat(history) })); compactAction(open)
        group(nil,
              settingRow(history ? "Past conversations" : "Chat with eero", history ? "Opens in its own window. Pick a conversation from the list on the left." : "Opens in its own window so you can keep using the app.", nil, open),
              settingRow(history ? "Start a new chat" : "Past conversations", history ? "Ask eero something new." : "Read or continue earlier chats.", nil, rowAction("Open", { [unowned self] in try self.openChat(!history) })))
        group("Other help",
              settingRow("eero help articles", "Step-by-step guides on eero's website.", nil, rowAction("Open ↗", { [unowned self] in try self.openServiceWindow("eero support", "https://www.eero.com/support") })),
              settingRow("Alexa", "Chat with Alexa on the web. Opens in its own window; features depend on your Amazon account.", nil, rowAction("Open ↗", { [unowned self] in try self.openServiceWindow("Alexa", "https://alexa.com/") })))
        page.append(text("Chat availability depends on your eero account, region and network.", 13, "#64738A"))
    }
}

// MARK: - Chat window model

/// One item in the conversation: a bubble, a note, feedback buttons, a permission request, a widget card or prompt chips.
@MainActor
final class ChatEntry: ObservableObject, Identifiable {
    enum Kind { case bubble(mine: Bool), note, feedback, permission, widget, chips }
    let id = UUID()
    let kind: Kind
    @Published var text: String
    @Published var done = false
    @Published var outcome: String?
    var key = ""
    var tools: [String] = []
    var details: [(String, String)] = []
    var choices: [String] = []
    var chips: [(label: String, prompt: String)] = []
    init(_ kind: Kind, _ text: String = "") { self.kind = kind; self.text = text }
}

/// Chat window: past conversations on the left, the conversation on the right, and a composer at the bottom.
@MainActor
final class ConversationModel: ObservableObject {
    static let starterPrompts = ["Is my internet working right now?", "Why is my wifi slow?", "Which devices use the most data?", "How can I improve coverage upstairs?"]
    static let maxInput = 8000
    struct HistoryItem: Identifiable { let id: String; let title: String; let when: String? }

    let api: EeroApi?
    let networkId: String
    let networkName: String
    let sample: Bool
    let showHistory: Bool
    weak var window: NSWindow?

    @Published var items: [ChatEntry] = []
    @Published var historyItems: [HistoryItem] = []
    @Published var conversationId: String?
    @Published var status = "Review any change eero asks to make before you approve it."
    @Published var inputText = ""
    @Published var running = false
    @Published var streaming = false
    @Published var welcome = true
    @Published var suggestions: [(label: String, prompt: String)] = []
    @Published var scrollTick = 0
    @Published var focusTick = 0

    private var deltas: [String: ChatEntry] = [:]
    private var rendered = Set<String>(), feedback = Set<String>(), answered = Set<String>()
    private var widgets: [String: ChatEntry] = [:]
    private var streamTask: Task<Void, Error>?
    private var cancelRequested = false
    private var mcpToken: String?
    private(set) var closed = false
    private var waitingForPermission = false, loaded = false

    init(_ api: EeroApi?, _ networkId: String, _ networkName: String, _ sample: Bool, _ showHistory: Bool) {
        self.api = api; self.networkId = networkId; self.networkName = networkName; self.sample = sample; self.showHistory = showHistory
        reset()
    }

    func appeared() {
        if loaded { return }
        loaded = true; focusTick += 1
        if sample { preview(); return }
        Task { @MainActor in await self.perform { try await self.loadHistory(); if !self.showHistory { try await self.recommendations() } } }
    }
    func close() {
        if closed { return }
        closed = true; cancelRequested = true; streamTask?.cancel()
        mcpToken = nil; conversationId = nil; deltas.removeAll(); items.removeAll(); inputText = ""
    }

    func fire(_ action: @escaping () async throws -> Void) { Task { @MainActor in await self.perform(action) } }
    func perform(_ action: () async throws -> Void) async {
        if running || closed { return }
        running = true
        defer { running = false; if !closed { streaming = false; focusTick += 1 } }
        do { try await action() }
        catch { if !closed { status = streamTask != nil && cancelRequested ? "Reply stopped. Reload conversations to see what eero saved before sending again." : ConversationContracts.error(error) } }
    }
    func stop() { if streamTask != nil { cancelRequested = true; streamTask?.cancel(); status = "Stopping…" } }
    func scrollToEnd() { scrollTick += 1 }

    // ---- Conversation list ----
    func loadHistory() async throws {
        if sample { preview(); return }
        guard let api = api else { return }
        let data = try await api.send("GET", "/2.2/conversations?networkId=" + escapeData(networkId) + "&excludeEmpty=true")
        if closed { return }
        var list: [HistoryItem] = []
        for row in J.list(J.get(data, "conversations")) {
            let id = J.s(row, "conversation_id"); if id.isEmpty { continue }
            let description = J.s(row, "description"), created = J.s(row, "created")
            list.append(HistoryItem(id: id, title: description.nullIfEmpty ?? created.nullIfEmpty ?? "Conversation", when: !description.isEmpty ? created : nil))
        }
        historyItems = list
        if !historyItems.isEmpty && (conversationId ?? "").isEmpty { status = "Pick a conversation on the left to read it, or ask something new." }
        if let id = conversationId, !id.isEmpty { try await loadConversation(id) }
    }
    func pickConversation(_ id: String) {
        if id == conversationId { return }
        fire { try await self.loadConversation(id) }
    }
    func loadConversation(_ id: String) async throws {
        guard let api = api else { return }
        let data = try await api.send("GET", "/2.2/conversations/" + escapeData(id)); if closed { return }
        reset(); conversationId = J.s(data, "conversation_id").nullIfEmpty ?? id
        for row in J.list(J.get(data, "message_history")) {
            let message = J.get(row, "message"); let messageId = J.s(row, "message_id")
            if let m = message, !(m is JNull) { let mine = J.s(m, "sender") == "USER"; bubble(mine ? "You" : "eero", J.s(m, "content"), messageId); if !mine { addFeedback(messageId) } }
            else if let p = J.get(row, "permission_request"), !(p is JNull) { note("eero asked: " + J.first(p, "friendly_sentence", "content")) }
            else if let g = J.get(row, "permission_grant"), !(g is JNull) { note(J.b(row, "permission_grant", "approved") ? "You approved this request." : "You declined this request.") }
            else if let w = J.get(row, "widget_event"), !(w is JNull) { widget(w, false) }
        }
        status = "Conversation loaded. You can carry on below."; scrollToEnd()
    }

    // ---- Welcome and suggestions ----
    func reset() {
        conversationId = nil; items.removeAll(); deltas.removeAll(); rendered.removeAll(); answered.removeAll(); feedback.removeAll(); widgets.removeAll(); waitingForPermission = false
        welcome = true; suggestions = []
    }
    func newChat() { fire { self.reset(); try await self.recommendations() } }
    func suggest(_ label: String, _ prompt: String) {
        if welcome { suggestions.append((label, prompt)); return }
        let row = ChatEntry(.chips); row.chips = [(label, prompt)]; items.append(row)
    }
    func useSuggestion(_ prompt: String) { inputText = prompt; fire { try await self.sendMessage() } }
    private func hideWelcome() { if welcome { welcome = false; suggestions = [] } }
    func recommendations() async throws {
        if sample { for prompt in ConversationModel.starterPrompts { suggest(prompt, prompt) }; return }
        guard let api = api else { return }
        let data = try await api.send("GET", "/2.2/conversations/recommended?networkId=" + escapeData(networkId)); if closed { return }
        var count = 0
        for row in J.list(J.get(data, "recommendations")) {
            let prompt = J.s(row, "suggested_prompt"); if prompt.isEmpty { continue }
            suggest(J.first(row, "title", "cta_text").nullIfEmpty ?? prompt, prompt); count += 1
        }
        if count == 0 { for prompt in ConversationModel.starterPrompts { suggest(prompt, prompt) } }
    }

    // ---- Sending and streaming ----
    private func token() async throws -> String? {
        // Tokens stay in memory and are refreshed for each submitted turn.
        guard let api = api else { return nil }
        do { let value = try await api.send("POST", "/2.2/conversations/token", J.obj("refresh_token", "")); return J.s(value, "access_token").nullIfEmpty }
        catch let e as ApiException { if e.sessionExpired { throw e }; return nil }
    }
    func sendMessage() async throws {
        let message = inputText.trim(); _ = try ConversationContracts.request(networkId, conversationId, message)
        if sample { bubble("You", message, nil); inputText = ""; bubble("eero", "This is a local sample. Sign in to send messages to eero.", nil); return }
        mcpToken = try await token(); if closed { return }
        bubble("You", message, nil); inputText = ""
        try await stream(try ConversationContracts.request(networkId, conversationId, message, mcpToken))
    }
    func submit() { if !inputText.trim().isEmpty { fire { try await self.sendMessage() } } }
    private func stream(_ body: Any?) async throws {
        if sample { return }
        guard let api = api else { return }
        deltas.removeValue(forKey: "stream-reply")
        waitingForPermission = false; cancelRequested = false; streaming = true; status = "eero is replying…"
        let work = Task<Void, Error> { @MainActor in
            try await api.streamConversation(body) { [weak self] value in
                // Stream callbacks may arrive on a worker thread; run them on the main thread and wait, so errors reach the stream.
                try ConversationModel.onMain { guard let self = self, !self.closed else { return }; try self.receive(value) }
            }
        }
        streamTask = work
        let timeout = Task { @MainActor in
            do { try await Task.sleep(nanoseconds: 180_000_000_000); self.cancelRequested = true; work.cancel() } catch {}
        }
        do {
            defer { timeout.cancel(); streamTask = nil; if !closed { streaming = false } }
            do { try await work.value; if !closed { status = "Reply finished." } }
            catch {
                if !cancelRequested { throw error }
                if !closed { status = waitingForPermission ? "eero is waiting for your decision on the request above." : "Reply stopped. Reload conversations before sending again." }
            }
        }
        if !closed, let id = conversationId, !id.isEmpty, !historyItems.contains(where: { $0.id == id }) {
            historyItems.insert(HistoryItem(id: id, title: "New conversation", when: "Just now"), at: 0)
        }
    }
    nonisolated static func onMain(_ action: @escaping @MainActor () throws -> Void) throws {
        if Thread.isMainThread { try MainActor.assumeIsolated { try action() }; return }
        var failure: Error?
        DispatchQueue.main.sync { do { try MainActor.assumeIsolated { try action() } } catch { failure = error } }
        if let e = failure { throw e }
    }

    // ---- Message rendering ----
    @discardableResult func bubble(_ sender: String, _ content: String, _ id: String?) -> ChatEntry {
        hideWelcome()
        let entry = ChatEntry(.bubble(mine: sender == "You"), content)
        items.append(entry)
        if let id = id, !id.isEmpty { deltas[id] = entry }
        scrollToEnd(); return entry
    }
    func note(_ content: String) { hideWelcome(); items.append(ChatEntry(.note, content)) }
    func receive(_ value: Any?) throws {
        if !J.s(value, "conversation_id").isEmpty { conversationId = J.s(value, "conversation_id") }
        let type = J.s(value, "type"), id = J.s(value, "message_id")
        if type == "Error" { throw AppError.invalidOperation("eero could not finish this reply. Reload conversations before trying again.") }
        if type == "InitialMessage" || type == "MessageDelta" {
            let key = !id.isEmpty ? id : "stream-reply"
            let target = deltas[key] ?? bubble("eero", "", key)
            if type == "InitialMessage" { target.text = J.s(value, "content") } else { target.text += J.s(value, "content") }
            if J.b(value, "finished") { addFeedback(id) }; scrollToEnd()
        }
        if type == "ToolUse" || type == "Reasoning" { status = "eero is checking your network…" }
        if let p = J.get(value, "permission_request"), !(p is JNull) { permission(p); waitingForPermission = true; cancelRequested = true; streamTask?.cancel() }
        if let w = J.get(value, "widget_event"), !(w is JNull) { widget(w, true) }
    }
    func addFeedback(_ id: String) {
        if id.isEmpty || feedback.contains(id) { return }
        feedback.insert(id)
        let entry = ChatEntry(.feedback); entry.key = id; items.append(entry)
    }
    func sendFeedback(_ entry: ChatEntry, _ positive: Bool) {
        fire {
            if !self.sample, let api = self.api { try await api.send("PUT", "/2.2/conversations/feedbacks", J.obj("message_id", entry.key, "positive", positive)) }
            entry.done = true; entry.text = "Thanks for the feedback."; self.status = "Feedback saved."
        }
    }
    private func approveDialog(_ message: String) async -> Bool {
        let alert = NSAlert()
        alert.messageText = "Approve eero's request?"; alert.informativeText = message; alert.alertStyle = .informational
        alert.addButton(withTitle: "No"); alert.addButton(withTitle: "Yes")
        alert.buttons[0].keyEquivalent = "\r"; alert.buttons[1].keyEquivalent = ""
        guard let host = window else { return alert.runModal() == .alertSecondButtonReturn }
        let response = await withCheckedContinuation { (c: CheckedContinuation<NSApplication.ModalResponse, Never>) in alert.beginSheetModal(for: host) { c.resume(returning: $0) } }
        return response == .alertSecondButtonReturn
    }
    func permission(_ permission: Any?) {
        let id = J.s(permission, "interrupt_id"); if id.isEmpty || rendered.contains("permission:" + id) { return }
        rendered.insert("permission:" + id)
        hideWelcome()
        let entry = ChatEntry(.permission, J.s(permission, "friendly_sentence")); entry.key = id
        entry.tools = J.list(J.get(permission, "tool_descriptions")).map { J.s($0) }.filter { !$0.isEmpty }
        items.append(entry); scrollToEnd()
    }
    func answer(_ entry: ChatEntry, _ choice: Bool) {
        fire {
            let id = entry.key
            if self.answered.contains(id) { self.status = "This request has already been answered."; return }
            if choice {
                let approved = await self.approveDialog(entry.text + "\n\n" + entry.tools.joined(separator: "\n"))
                if !approved { return }
            }
            self.answered.insert(id); entry.done = true; entry.outcome = choice ? "✓ You approved this." : "You declined this."
            if self.sample { self.status = "Sample decision only."; return }
            self.mcpToken = try await self.token()
            try await self.stream(try ConversationContracts.request(self.networkId, self.conversationId, nil, self.mcpToken, J.obj("approved", choice, "interrupt_id", id)))
        }
    }
    static let widgetFields = ["name", "location", "model", "status", "connected", "connection_type", "connection_state", "device_type", "is_guest", "is_private", "score_bars", "mesh_quality_bars", "wan_type", "eero", "port_number", "negotiated_speed", "total_devices", "paused_devices", "accessory_type", "bars", "cellular_backup_status", "configuration_status"]
    func widget(_ widget: Any?, _ active: Bool) {
        let id = J.s(widget, "instance_id"), action = J.s(widget, "action")
        if let previous = widgets[id] { items.removeAll { $0 === previous }; widgets.removeValue(forKey: id) }
        if action.caseInsensitiveCompare("remove") == .orderedSame || action.caseInsensitiveCompare("delete") == .orderedSame { return }
        hideWelcome()
        let properties = J.get(widget, "properties")
        let title = J.s(widget, "content_summary").nullIfEmpty ?? J.first(properties, "prompt_text", "title", "description").nullIfEmpty ?? MainWindow.human(J.s(widget, "widget_type"))
        let entry = ChatEntry(.widget, title)
        for field in ConversationModel.widgetFields {
            let raw = J.get(properties, field); let value = (raw as? Bool).map { $0 ? "Yes" : "No" } ?? MainWindow.friendly(J.s(properties, field))
            if !value.isEmpty { entry.details.append((MainWindow.human(field), value)) }
        }
        for contact in J.list(J.get(properties, "contacts")) { entry.details.append((J.s(contact, "title"), J.s(contact, "content"))) }
        if active { entry.choices = J.list(J.get(properties, "choices")).map { J.s($0) }.filter { !$0.isEmpty } }
        items.append(entry); widgets[id] = entry; scrollToEnd()
    }
    func preview() {
        historyItems = [HistoryItem(id: "sample-conversation", title: "Check my connectivity", when: "Today"), HistoryItem(id: "sample-older", title: "Slow wifi upstairs", when: "Last week")]
        reset(); conversationId = "sample-conversation"
        bubble("You", "Check my connectivity", nil); bubble("eero", "Your sample network is online. The gateway and two wireless eeros are connected.", "sample-reply"); addFeedback("sample-reply")
        permission(J.obj("interrupt_id", "sample-permission", "friendly_sentence", "May I run a connection diagnostic?", "tool_descriptions", ["Check internet health on the selected network."]))
        status = "Sample conversation · No account requests or changes."
    }
}

// MARK: - Chat window views

struct ConversationView: View {
    @ObservedObject var chat: ConversationModel
    var body: some View {
        HStack(spacing: 0) {
            sidebar.frame(width: 250).padding(.leading, 14).padding(.trailing, 10).padding(.top, 4).padding(.bottom, 14)
            main
                .background(Color(nsColor: .textBackgroundColor))
                .clipShape(RoundedRectangle(cornerRadius: 8))
                .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Color(nsColor: .separatorColor), lineWidth: 0.5))
                .padding(.trailing, 8).padding(.bottom, 8).padding(.top, 4)
        }
        .background(Color(nsColor: .windowBackgroundColor))
        .onAppear { chat.appeared() }
    }

    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 0) {
            Button(action: { chat.newChat() }) { Text("+  New chat").frame(maxWidth: .infinity) }
                .buttonStyle(.borderedProminent).controlSize(.large).disabled(chat.running).padding(.bottom, 16)
            HStack {
                Text("CONVERSATIONS").font(.system(size: 10, weight: .semibold)).foregroundColor(Tone.muted.color)
                Spacer()
                Button(action: { chat.fire { try await chat.loadHistory() } }) { Image(systemName: Glyphs.symbol("\u{E72C}")).font(.system(size: 12)) }
                    .buttonStyle(.plain).foregroundColor(Tone.muted.color).help("Reload conversations").accessibilityLabel("Reload conversations").disabled(chat.running)
            }.padding(.leading, 4).padding(.bottom, 6)
            ScrollView {
                VStack(alignment: .leading, spacing: 2) {
                    if chat.historyItems.isEmpty {
                        Text(chat.sample ? "Sample conversations appear here." : "No past conversations yet. Start one with New chat.")
                            .font(.system(size: 12)).foregroundColor(Tone.muted.color).fixedSize(horizontal: false, vertical: true).padding(.horizontal, 4).padding(.top, 6)
                    }
                    ForEach(chat.historyItems) { item in
                        let current = item.id == chat.conversationId
                        Button(action: { chat.pickConversation(item.id) }) {
                            VStack(alignment: .leading, spacing: 2) {
                                Text(item.title).font(.system(size: 13, weight: current ? .semibold : .regular)).lineLimit(1).truncationMode(.tail)
                                if let when = item.when, !when.isEmpty { Text(when).font(.system(size: 11)).foregroundColor(Tone.muted.color) }
                            }
                            .padding(.horizontal, 8).padding(.vertical, 6).frame(maxWidth: .infinity, minHeight: 44, alignment: .leading)
                            .background(RoundedRectangle(cornerRadius: 6).fill(current ? Color.accentColor.opacity(0.14) : Color.clear))
                            .contentShape(Rectangle())
                        }
                        .buttonStyle(.plain).help(item.title).accessibilityLabel(item.title).disabled(chat.running)
                    }
                }
            }
            Text(chat.networkName).font(.system(size: 12)).foregroundColor(Tone.muted.color).lineLimit(1).truncationMode(.tail).padding(.leading, 4).padding(.top, 10)
        }
    }

    private var main: some View {
        VStack(alignment: .leading, spacing: 0) {
            VStack(alignment: .leading, spacing: 4) {
                Text("Chat with eero").font(.system(size: 22, weight: .semibold))
                Text(chat.sample ? "Sample conversation · nothing is sent to eero" : "Answers come from eero's assistant. Changes always need your approval.")
                    .font(.system(size: 12)).foregroundColor(Tone.muted.color)
            }.padding(.horizontal, 28).padding(.top, 18).padding(.bottom, 6)
            ScrollViewReader { proxy in
                ScrollView {
                    VStack(alignment: .leading, spacing: 0) {
                        if chat.welcome { WelcomeView(chat: chat) }
                        ForEach(chat.items) { entry in ChatEntryView(entry: entry, chat: chat) }
                        Color.clear.frame(height: 1).id("chat-end")
                    }
                    .padding(.horizontal, 28).padding(.vertical, 8)
                    .frame(maxWidth: .infinity, alignment: .leading)
                }
                .onChange(of: chat.scrollTick) { _ in
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.06) { withAnimation(.easeOut(duration: 0.15)) { proxy.scrollTo("chat-end", anchor: .bottom) } }
                }
            }
            composer.padding(.horizontal, 24).padding(.top, 6).padding(.bottom, 12)
        }
    }

    private var composer: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .bottom, spacing: 8) {
                ZStack(alignment: .topLeading) {
                    ChatInputView(text: $chat.inputText, enabled: !chat.running, focusTick: chat.focusTick, onSubmit: { chat.submit() })
                        .frame(height: 64)
                    if chat.inputText.isEmpty {
                        Text("Ask eero about your network…").font(.system(size: 14)).foregroundColor(Tone.muted.color).padding(.leading, 5).padding(.top, 8).allowsHitTesting(false)
                    }
                }
                if chat.streaming { Button("Stop") { chat.stop() } }
                Button("Send") { chat.submit() }.buttonStyle(.borderedProminent).disabled(chat.running)
            }
            .padding(.leading, 10).padding(.trailing, 8).padding(.vertical, 6)
            .background(RoundedRectangle(cornerRadius: 12).fill(Color(nsColor: .controlBackgroundColor)))
            .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(Color(nsColor: .separatorColor), lineWidth: 0.5))
            HStack(alignment: .top) {
                Text(chat.status).font(.system(size: 12)).foregroundColor(Tone.muted.color).fixedSize(horizontal: false, vertical: true)
                Spacer(minLength: 12)
                Text("Return to send · Shift+Return for a new line").font(.system(size: 12)).foregroundColor(Tone.muted.color)
            }.padding(.horizontal, 6)
        }
    }
}

struct WelcomeView: View {
    @ObservedObject var chat: ConversationModel
    var body: some View {
        VStack(spacing: 0) {
            Image(systemName: Glyphs.symbol("\u{E8F2}")).font(.system(size: 22)).foregroundColor(.accentColor)
                .frame(width: 56, height: 56).background(Circle().fill(Color.accentColor.opacity(0.14)))
            Text("How can eero help?").font(.system(size: 22, weight: .semibold)).multilineTextAlignment(.center).padding(.top, 16).padding(.bottom, 4)
            Text("Ask about your internet, wifi, devices or settings. eero can check your network and suggest fixes.")
                .font(.system(size: 13)).foregroundColor(Tone.muted.color).multilineTextAlignment(.center).fixedSize(horizontal: false, vertical: true)
            LazyVGrid(columns: [GridItem(.flexible(), spacing: 8), GridItem(.flexible(), spacing: 8)], spacing: 8) {
                ForEach(Array(chat.suggestions.enumerated()), id: \.offset) { _, s in ChipButton(label: s.label, prompt: s.prompt, chat: chat) }
            }.padding(.top, 14)
        }
        .frame(maxWidth: 480)
        .frame(maxWidth: .infinity)
        .padding(.top, 60).padding(.bottom, 20)
    }
}

struct ChipButton: View {
    let label: String; let prompt: String
    @ObservedObject var chat: ConversationModel
    var body: some View {
        Button(action: { chat.useSuggestion(prompt) }) {
            Text(label).font(.system(size: 13)).multilineTextAlignment(.center).frame(maxWidth: .infinity).padding(.horizontal, 14).padding(.vertical, 8)
                .background(Capsule().fill(Color(nsColor: .controlBackgroundColor)))
                .overlay(Capsule().strokeBorder(Color(nsColor: .separatorColor), lineWidth: 0.5))
                .contentShape(Capsule())
        }
        .buttonStyle(.plain).help(prompt).disabled(chat.running)
    }
}

struct ChatEntryView: View {
    @ObservedObject var entry: ChatEntry
    @ObservedObject var chat: ConversationModel
    var body: some View {
        switch entry.kind {
        case .bubble(let mine):
            if mine {
                HStack(spacing: 0) {
                    Spacer(minLength: 80)
                    Text(entry.text).font(.system(size: 14)).textSelection(.enabled).fixedSize(horizontal: false, vertical: true)
                        .padding(.horizontal, 14).padding(.vertical, 10)
                        .background(RoundedRectangle(cornerRadius: 14).fill(Color.accentColor.opacity(0.16)))
                        .frame(maxWidth: 560, alignment: .trailing)
                        .contextMenu { copyButton }
                }.padding(.bottom, 12)
            } else {
                HStack(alignment: .top, spacing: 12) {
                    AvatarView(letter: "e", size: 30, fontSize: 15)
                    Text(entry.text).font(.system(size: 14)).textSelection(.enabled).fixedSize(horizontal: false, vertical: true)
                        .padding(.horizontal, 14).padding(.vertical, 10)
                        .background(RoundedRectangle(cornerRadius: 14).fill(Color(nsColor: .controlBackgroundColor)))
                        .overlay(RoundedRectangle(cornerRadius: 14).strokeBorder(Color(nsColor: .separatorColor), lineWidth: 0.5))
                        .frame(maxWidth: 560, alignment: .leading)
                        .contextMenu { copyButton }
                    Spacer(minLength: 60)
                }.padding(.bottom, 4)
            }
        case .note:
            Text(entry.text).font(.system(size: 12)).italic().foregroundColor(Tone.muted.color).fixedSize(horizontal: false, vertical: true)
                .padding(.leading, 42).padding(.top, 2).padding(.bottom, 12)
        case .feedback:
            HStack(spacing: 2) {
                ForEach([true, false], id: \.self) { positive in
                    Button(action: { chat.sendFeedback(entry, positive) }) { Image(systemName: Glyphs.symbol(positive ? "\u{E8E1}" : "\u{E8E0}")).font(.system(size: 12)) }
                        .buttonStyle(.plain).foregroundColor(Tone.muted.color).padding(.horizontal, 6).padding(.vertical, 4)
                        .help(positive ? "Helpful" : "Not helpful").accessibilityLabel(positive ? "Helpful" : "Not helpful")
                        .disabled(entry.done || chat.running)
                }
                if !entry.text.isEmpty { Text(entry.text).font(.system(size: 12)).foregroundColor(Tone.muted.color).padding(.leading, 6) }
            }.padding(.leading, 40).padding(.bottom, 10)
        case .permission:
            VStack(alignment: .leading, spacing: 0) {
                HStack(spacing: 8) {
                    Image(systemName: Glyphs.symbol("\u{E72E}")).font(.system(size: 14)).foregroundColor(.accentColor)
                    Text("eero needs your permission").font(.system(size: 13, weight: .semibold)).foregroundColor(.accentColor)
                }.padding(.bottom, 8)
                Text(entry.text).font(.system(size: 15, weight: .semibold)).fixedSize(horizontal: false, vertical: true).padding(.bottom, 4)
                ForEach(Array(entry.tools.enumerated()), id: \.offset) { _, tool in
                    Text("•  " + tool).font(.system(size: 13)).foregroundColor(Tone.muted.color).fixedSize(horizontal: false, vertical: true).padding(.top, 2)
                }
                if let outcome = entry.outcome {
                    Text(outcome).font(.system(size: 13)).foregroundColor(Tone.muted.color).padding(.top, 12)
                } else {
                    HStack(spacing: 8) {
                        Button("Approve") { chat.answer(entry, true) }.buttonStyle(.borderedProminent)
                        Button("Decline") { chat.answer(entry, false) }
                    }.padding(.top, 12).disabled(chat.running && !chat.streaming)
                }
            }
            .padding(.horizontal, 16).padding(.vertical, 14)
            .frame(maxWidth: 420, alignment: .leading)
            .background(RoundedRectangle(cornerRadius: 12).fill(Color(nsColor: .controlBackgroundColor)))
            .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(Color.accentColor, lineWidth: 1.5))
            .padding(.leading, 42).padding(.trailing, 60).padding(.top, 4).padding(.bottom, 14)
        case .widget:
            VStack(alignment: .leading, spacing: 0) {
                Text(entry.text).font(.system(size: 15, weight: .semibold)).fixedSize(horizontal: false, vertical: true).padding(.bottom, 6)
                ForEach(Array(entry.details.enumerated()), id: \.offset) { _, d in DetailRowView(label: d.0, value: d.1, flag: false) }
                if !entry.choices.isEmpty {
                    FlowLayout(spacing: 8) {
                        ForEach(Array(entry.choices.enumerated()), id: \.offset) { _, prompt in
                            Button(prompt) { chat.useSuggestion(prompt) }.disabled(chat.running)
                        }
                    }.padding(.top, 10)
                }
            }
            .padding(.horizontal, 16).padding(.vertical, 12)
            .frame(maxWidth: 420, alignment: .leading)
            .background(RoundedRectangle(cornerRadius: 12).fill(Color(nsColor: .controlBackgroundColor)))
            .overlay(RoundedRectangle(cornerRadius: 12).strokeBorder(Color(nsColor: .separatorColor), lineWidth: 0.5))
            .padding(.leading, 42).padding(.trailing, 60).padding(.bottom, 12)
        case .chips:
            FlowLayout(spacing: 8) {
                ForEach(Array(entry.chips.enumerated()), id: \.offset) { _, s in ChipButton(label: s.label, prompt: s.prompt, chat: chat).fixedSize() }
            }.padding(.bottom, 8)
        }
    }
    private var copyButton: some View {
        Button("Copy") { if !entry.text.isEmpty { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(entry.text, forType: .string) } }
    }
}

/// Multi-line message box: Return sends, Shift+Return adds a line; input is capped at 8,000 characters.
struct ChatInputView: NSViewRepresentable {
    @Binding var text: String
    let enabled: Bool
    let focusTick: Int
    let onSubmit: () -> Void

    final class Coordinator: NSObject, NSTextViewDelegate {
        var parent: ChatInputView
        var lastFocus = -1
        init(_ parent: ChatInputView) { self.parent = parent }
        func textDidChange(_ notification: Notification) {
            guard let view = notification.object as? NSTextView else { return }
            if view.string.count > ConversationModel.maxInput { view.string = String(view.string.prefix(ConversationModel.maxInput)) }
            parent.text = view.string
        }
        func textView(_ textView: NSTextView, doCommandBy selector: Selector) -> Bool {
            if selector == #selector(NSResponder.insertNewline(_:)) {
                if NSApp.currentEvent?.modifierFlags.contains(.shift) == true { textView.insertNewlineIgnoringFieldEditor(nil); return true }
                if !textView.string.trim().isEmpty { parent.onSubmit() }
                return true
            }
            return false
        }
    }
    func makeCoordinator() -> Coordinator { Coordinator(self) }
    func makeNSView(context: Context) -> NSScrollView {
        let scroll = NSTextView.scrollableTextView()
        scroll.drawsBackground = false; scroll.hasVerticalScroller = true; scroll.autohidesScrollers = true; scroll.borderType = .noBorder
        if let view = scroll.documentView as? NSTextView {
            view.delegate = context.coordinator
            view.font = NSFont.systemFont(ofSize: 14); view.drawsBackground = false
            view.isRichText = false; view.allowsUndo = true; view.isAutomaticQuoteSubstitutionEnabled = false
            view.textContainerInset = NSSize(width: 0, height: 8)
            view.string = text
            view.setAccessibilityLabel("Message to eero")
        }
        return scroll
    }
    func updateNSView(_ scroll: NSScrollView, context: Context) {
        context.coordinator.parent = self
        guard let view = scroll.documentView as? NSTextView else { return }
        if view.string != text { view.string = text }
        view.isEditable = enabled; view.isSelectable = true
        if context.coordinator.lastFocus != focusTick && enabled {
            context.coordinator.lastFocus = focusTick
            DispatchQueue.main.async { view.window?.makeFirstResponder(view) }
        }
    }
}
