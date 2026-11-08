using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gtk;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private ConversationWindow chatWindow;

        // Sample chat window for --chat-preview and --render-chat (no account, no requests).
        internal static Gtk.Window ChatSample(Adw.Application app)
        { var chat = new ConversationWindow(null, "sample-network", "Sample home", true, false); chat.Window.SetApplication(app); chat.Preview(); return chat.Window; }
        private void OpenChat(bool history = false)
        {
            if (String.IsNullOrEmpty(networkUrl)) throw new InvalidOperationException("Select a network before opening chat or its support history.");
            if (chatWindow != null) { chatWindow.Window.Present(); return; }
            if (!history) { var warmup = WarmChat(NetworkId); }
            var chat = new ConversationWindow(api, NetworkId, J.S(network, "name"), preview, history);
            Own(chat.Window); chatWindow = chat;
            chat.Window.OnDestroy += delegate { if (chatWindow == chat) chatWindow = null; };
            chat.Window.Present();
        }
        private void ChatPage(bool history)
        {
            Sub(history ? "Read or continue your earlier conversations with eero." : "Ask eero about your network. It can suggest changes, and nothing changes until you approve it.");
            var open = RowAction(history ? "Open history" : "Start chat", delegate { OpenChat(history); }); Accent(open); CompactAction(open);
            Group(null,
                SettingRow(history ? "Past conversations" : "Chat with eero", history ? "Opens in its own window. Pick a conversation from the list on the left." : "Opens in its own window so you can keep using the app.", null, open),
                SettingRow(history ? "Start a new chat" : "Past conversations", history ? "Ask eero something new." : "Read or continue earlier chats.", null, RowAction("Open", delegate { OpenChat(!history); })));
            Group("Other help",
                SettingRow("eero help articles", "Step-by-step guides on eero's website.", null, RowAction("Open ↗", delegate { OpenServiceWindow("eero support", "https://www.eero.com/support"); })),
                SettingRow("Alexa", "Chat with Alexa on the web. Opens in its own window; features depend on your Amazon account.", null, RowAction("Open ↗", delegate { OpenServiceWindow("Alexa", "https://alexa.com/"); })));
            page.Append(Text("Chat availability depends on your eero account, region and network.", 13, "#64738A"));
        }

        // Chat window: past conversations on the left, the conversation on the right, and a composer at the bottom.
        private sealed class ConversationWindow
        {
            private static readonly string[] StarterPrompts = { "Is my internet working right now?", "Why is my wifi slow?", "Which devices use the most data?", "How can I improve coverage upstairs?" };
            public readonly Adw.Window Window;
            private readonly EeroApi api;
            private readonly string networkId;
            private readonly bool sample;
            private readonly int mainThread;
            private readonly Box messages, historyList;
            private readonly ScrolledWindow scroll;
            private readonly TextView input;
            private readonly Label status, placeholder;
            private readonly Button send, stop, fresh, reload;
            private readonly Dictionary<string, Label> deltas = new Dictionary<string, Label>();
            private readonly HashSet<string> rendered = new HashSet<string>();
            private readonly HashSet<string> feedback = new HashSet<string>();
            private readonly HashSet<string> answered = new HashSet<string>();
            private readonly Dictionary<string, Widget> widgets = new Dictionary<string, Widget>();
            private readonly List<HistoryItem> historyItems = new List<HistoryItem>();
            private Box welcome; private FlowBox suggestions;
            private CancellationTokenSource cancellation;
            private string conversationId, mcpToken;
            private bool running, closed, waitingForPermission, loaded;
            private const int MaxInput = 8000;

            public ConversationWindow(EeroApi api, string networkId, string networkName, bool sample, bool showHistory)
            {
                this.api = api; this.networkId = networkId; this.sample = sample; mainThread = Environment.CurrentManagedThreadId;
                PortedStyles();
                Window = Adw.Window.New();
                Window.SetTitle("Chat with eero · " + networkName); Window.SetDefaultSize(1060, 820); Window.SetSizeRequest(780, 600);
                var root = HBox(0); root.AddCssClass("chat-side");

                // Sidebar: new chat and past conversations.
                var side = VBox(); side.SetSizeRequest(250, -1); side.Margin(14, 4, 10, 14);
                fresh = Control("+  New chat", async delegate { Reset(); await Recommendations(); }); Accent(fresh); fresh.SetHexpand(true); fresh.Margin(0, 0, 0, 16); side.Append(fresh);
                var listTitle = Text("CONVERSATIONS", 10, "#64738A", true); listTitle.SetMarginBottom(0); listTitle.SetValign(Gtk.Align.Center);
                reload = Control("", LoadHistory); reload.SetChild(Glyph("view-refresh-symbolic", 13)); reload.AddCssClass("flat"); reload.Margin(0); reload.SetTooltipText("Reload conversations"); reload.Name("Reload conversations");
                var listHead = Split(listTitle, reload); listHead.Margin(4, 0, 0, 6); side.Append(listHead);
                historyList = VBox(2);
                var historyScroll = ScrolledWindow.New(); historyScroll.SetChild(historyList); historyScroll.SetPolicy(PolicyType.Never, PolicyType.Automatic); historyScroll.SetVexpand(true); side.Append(historyScroll);
                var network = NoWrap(Text(networkName, 12, "#64738A"), 30); network.Margin(4, 10, 0, 0); side.Append(network);
                root.Append(side);

                // Conversation area.
                var main = VBox(); main.AddCssClass("chat-frame"); main.Margin(0, 0, 8, 8); main.SetHexpand(true); main.SetOverflow(Overflow.Hidden);
                root.Append(main);
                var header = VBox(); header.Margin(28, 18, 28, 6);
                var heading = Text("Chat with eero", 22, "#17263C", true); heading.SetMarginBottom(0); header.Append(heading);
                header.Append(Text(sample ? "Sample conversation · nothing is sent to eero" : "Answers come from eero's assistant. Changes always need your approval.", 12, "#64738A"));
                main.Append(header);
                messages = VBox(); messages.Margin(28, 8, 28, 8);
                scroll = ScrolledWindow.New(); scroll.SetChild(messages); scroll.SetPolicy(PolicyType.Never, PolicyType.Automatic); scroll.SetVexpand(true); main.Append(scroll);

                // Composer.
                var bottom = VBox(); bottom.Margin(24, 6, 24, 12);
                var box = HBox(8); box.AddCssClass("chat-composer");
                input = TextView.New(); input.SetWrapMode(WrapMode.WordChar); input.SetTopMargin(10); input.SetBottomMargin(10); input.SetLeftMargin(4); input.SetRightMargin(4); input.SetAcceptsTab(false); input.Name("Message to eero");
                var inputScroll = ScrolledWindow.New(); inputScroll.SetChild(input); inputScroll.SetPolicy(PolicyType.Never, PolicyType.Automatic); inputScroll.SetMinContentHeight(44); inputScroll.SetMaxContentHeight(160); inputScroll.SetPropagateNaturalHeight(true); inputScroll.SetHexpand(true);
                placeholder = Text("Ask eero about your network…", 14, "#64738A"); placeholder.Margin(6, 10, 0, 0); placeholder.SetCanTarget(false); placeholder.SetValign(Gtk.Align.Start); placeholder.SetHalign(Gtk.Align.Start);
                var inputHost = Overlay.New(); inputHost.SetChild(inputScroll); inputHost.AddOverlay(placeholder); inputHost.SetHexpand(true);
                input.GetBuffer().OnChanged += delegate
                {
                    string text = InputText;
                    if (text.Length > MaxInput) { InputText = text.Substring(0, MaxInput); return; }
                    placeholder.SetVisible(text.Length == 0);
                };
                box.Append(inputHost);
                var buttons = HBox(8); buttons.SetValign(Gtk.Align.End); buttons.Margin(0, 0, 0, 2);
                // Stop must work while a reply is streaming, so it bypasses Perform's one-action-at-a-time guard.
                stop = Gtk.Button.NewWithLabel("Stop"); stop.SetVisible(false);
                stop.OnClicked += delegate { if (cancellation != null) { cancellation.Cancel(); status.SetText("Stopping…"); } };
                send = Control("Send", SendMessage); Accent(send); send.Margin(0);
                buttons.Append(stop); buttons.Append(send); box.Append(buttons);
                bottom.Append(box);
                var keys = EventControllerKey.New(); keys.SetPropagationPhase(PropagationPhase.Capture);
                keys.OnKeyPressed += (sender, args) =>
                {
                    if ((args.Keyval != Gdk.Constants.KEY_Return && args.Keyval != Gdk.Constants.KEY_KP_Enter) || (args.State & Gdk.ModifierType.ShiftMask) != 0) return false;
                    if (InputText.Trim().Length > 0) Fire(SendMessage);
                    return true;
                };
                input.AddController(keys);
                status = Text("Review any change eero asks to make before you approve it.", 12, "#64738A"); status.SetMarginBottom(0);
                var shortcut = Text("Enter to send · Shift+Enter for a new line", 12, "#64738A"); shortcut.SetMarginBottom(0); shortcut.SetHexpand(false); shortcut.SetXalign(1); shortcut.SetMarginStart(12);
                var foot = Split(status, shortcut); foot.Margin(6, 6, 6, 0);
                bottom.Append(foot); main.Append(bottom);

                var headerBar = Adw.HeaderBar.New(); headerBar.AddCssClass("flat");
                var view = Adw.ToolbarView.New(); view.AddTopBar(headerBar); view.SetContent(root); view.AddCssClass("chat-side");
                Window.SetContent(view);

                Reset();
                Window.OnDestroy += delegate { closed = true; if (cancellation != null) cancellation.Cancel(); mcpToken = conversationId = null; deltas.Clear(); messages.Clear(); InputText = ""; };
                Window.OnMap += async delegate
                {
                    if (loaded) return; loaded = true;
                    input.GrabFocus();
                    if (sample) { Preview(); return; }
                    await Perform(async delegate { await LoadHistory(); if (!showHistory) await Recommendations(); });
                };
            }

            private sealed class HistoryItem { public string Id, Title, When; }

            private string InputText
            {
                get { var buffer = input.GetBuffer(); TextIter start, end; buffer.GetStartIter(out start); buffer.GetEndIter(out end); return buffer.GetText(start, end, false) ?? ""; }
                set { input.GetBuffer().SetText(value ?? "", -1); }
            }

            private Button Control(string label, Func<Task> action)
            {
                var button = Gtk.Button.NewWithLabel(label); button.Margin(0, 0, 8, 8);
                button.OnClicked += delegate { Fire(action); }; return button;
            }
            private async void Fire(Func<Task> action) { await Perform(action); }

            private async Task Perform(Func<Task> action)
            {
                if (running || closed) return;
                running = true; SetBusy(true);
                try { await action(); }
                catch (Exception e) { if (!closed) status.SetText(cancellation != null && cancellation.IsCancellationRequested ? "Reply stopped. Reload conversations to see what eero saved before sending again." : ConversationContracts.Error(e)); }
                finally { running = false; if (!closed) { SetBusy(false); input.GrabFocus(); } }
            }
            private void SetBusy(bool value)
            {
                foreach (Widget w in new Widget[] { send, fresh, reload, historyList, input }) w.SetSensitive(!value);
                if (suggestions != null) suggestions.SetSensitive(!value);
                if (!value) stop.SetVisible(false);
            }
            // Stream callbacks arrive on a worker thread; run them on the GTK thread and wait, so errors reach the stream.
            private void OnMain(System.Action action)
            {
                if (Environment.CurrentManagedThreadId == mainThread) { action(); return; }
                var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT, delegate
                {
                    try { action(); done.TrySetResult(true); } catch (Exception e) { done.TrySetException(e); }
                    return false;
                });
                done.Task.GetAwaiter().GetResult();
            }
            private void ScrollToEnd()
            {
                GLib.Functions.TimeoutAdd(GLib.Constants.PRIORITY_DEFAULT, 60, delegate
                {
                    if (!closed) { var adjustment = scroll.GetVadjustment(); adjustment.SetValue(adjustment.GetUpper() - adjustment.GetPageSize()); }
                    return false;
                });
            }

            // ---- Conversation list ----
            private void RenderHistory()
            {
                historyList.Clear();
                if (historyItems.Count == 0)
                {
                    var none = Text(sample ? "Sample conversations appear here." : "No past conversations yet. Start one with New chat.", 12, "#64738A"); none.Margin(4, 6, 4, 0); historyList.Append(none); return;
                }
                foreach (HistoryItem raw in historyItems)
                {
                    HistoryItem item = raw;
                    var b = Gtk.Button.New(); b.AddCssClass("flat"); b.SetSizeRequest(-1, 44);
                    var words = VBox();
                    var title = NoWrap(Text(item.Title, 13, "#17263C", item.Id == conversationId), 28); title.SetMarginBottom(0); words.Append(title);
                    if (!String.IsNullOrEmpty(item.When)) { var when = Text(item.When, 11, "#64738A"); when.Margin(0, 2, 0, 0); words.Append(when); }
                    words.Margin(4, 3, 4, 3);
                    b.SetChild(words); b.SetTooltipText(item.Title);
                    if (item.Id == conversationId) b.AddCssClass("chat-history-current");
                    b.Name(item.Title);
                    b.OnClicked += delegate { if (item.Id == conversationId) return; Fire(async delegate { await LoadConversation(item.Id); }); };
                    historyList.Append(b);
                }
            }
            private async Task LoadHistory()
            {
                if (sample) { Preview(); return; }
                object data = await api.Send("GET", "/2.2/conversations?networkId=" + Uri.EscapeDataString(networkId) + "&excludeEmpty=true");
                if (closed) return;
                historyItems.Clear();
                foreach (object row in J.List(J.Get(data, "conversations")))
                {
                    string id = J.S(row, "conversation_id"); if (id.Length == 0) continue;
                    string description = J.S(row, "description"), created = J.S(row, "created");
                    historyItems.Add(new HistoryItem { Id = id, Title = description.NullIfEmpty() ?? created.NullIfEmpty() ?? "Conversation", When = description.Length > 0 ? created : null });
                }
                RenderHistory();
                if (historyItems.Count > 0 && String.IsNullOrEmpty(conversationId)) status.SetText("Pick a conversation on the left to read it, or ask something new.");
                if (!String.IsNullOrEmpty(conversationId)) await LoadConversation(conversationId);
            }
            private async Task LoadConversation(string id)
            {
                object data = await api.Send("GET", "/2.2/conversations/" + Uri.EscapeDataString(id)); if (closed) return;
                Reset(); conversationId = J.S(data, "conversation_id").NullIfEmpty() ?? id; RenderHistory();
                foreach (object row in J.List(J.Get(data, "message_history")))
                {
                    object message = J.Get(row, "message"); string messageId = J.S(row, "message_id");
                    if (message != null) { bool mine = J.S(message, "sender") == "USER"; Bubble(mine ? "You" : "eero", J.S(message, "content"), messageId); if (!mine) Feedback(messageId); }
                    else if (J.Get(row, "permission_request") != null) Note("eero asked: " + J.First(J.Get(row, "permission_request"), "friendly_sentence", "content"));
                    else if (J.Get(row, "permission_grant") != null) Note(J.B(row, "permission_grant", "approved") ? "You approved this request." : "You declined this request.");
                    else if (J.Get(row, "widget_event") != null) Widget(J.Get(row, "widget_event"), false);
                }
                status.SetText("Conversation loaded. You can carry on below."); ScrollToEnd();
            }

            // ---- Welcome and suggestions ----
            private void Reset()
            {
                conversationId = null; messages.Clear(); deltas.Clear(); rendered.Clear(); answered.Clear(); feedback.Clear(); widgets.Clear(); waitingForPermission = false;
                welcome = VBox(); welcome.SetHalign(Gtk.Align.Center); welcome.Margin(0, 60, 0, 20); welcome.SetSizeRequest(Math.Min(560, 480), -1);
                var mark = Glyph("user-available-symbolic", 26, "#274CDB"); mark.AddCssClass("avatar"); mark.SetSizeRequest(56, 56); mark.SetHalign(Gtk.Align.Center); welcome.Append(mark);
                var hello = Text("How can eero help?", 22, "#17263C", true); hello.Margin(0, 16, 0, 4); hello.SetXalign(0.5f); hello.SetJustify(Justification.Center); welcome.Append(hello);
                var sub = Text("Ask about your internet, wifi, devices or settings. eero can check your network and suggest fixes.", 13, "#64738A"); sub.SetXalign(0.5f); sub.SetJustify(Justification.Center); sub.SetMaxWidthChars(64); welcome.Append(sub);
                suggestions = WrapPanel(8); suggestions.SetHalign(Gtk.Align.Center); suggestions.SetMarginTop(14); suggestions.SetMaxChildrenPerLine(2); welcome.Append(suggestions);
                messages.Append(welcome);
                if (historyList != null) RenderHistory();
            }
            private void Suggest(string label, string prompt)
            {
                var chip = Gtk.Button.NewWithLabel(label); chip.AddCssClass("chat-chip"); chip.SetTooltipText(prompt); chip.Margin(4);
                chip.OnClicked += delegate { InputText = prompt; Fire(SendMessage); };
                if (suggestions != null && welcome != null) suggestions.Add(chip); else { var row = WrapPanel(); row.Add(chip); messages.Append(row); }
            }
            private void HideWelcome() { if (welcome != null) { messages.Remove(welcome); welcome = null; suggestions = null; } }
            private async Task Recommendations()
            {
                if (sample) { foreach (string prompt in StarterPrompts) Suggest(prompt, prompt); return; }
                object data = await api.Send("GET", "/2.2/conversations/recommended?networkId=" + Uri.EscapeDataString(networkId)); if (closed) return;
                int count = 0;
                foreach (object row in J.List(J.Get(data, "recommendations")))
                {
                    string prompt = J.S(row, "suggested_prompt"); if (prompt.Length == 0) continue;
                    Suggest(J.First(row, "title", "cta_text").NullIfEmpty() ?? prompt, prompt); count++;
                }
                if (count == 0) foreach (string prompt in StarterPrompts) Suggest(prompt, prompt);
            }

            // ---- Sending and streaming ----
            private async Task<string> Token()
            {
                // Tokens stay in memory and are refreshed for each submitted turn.
                try { object value = await api.Send("POST", "/2.2/conversations/token", J.Obj("refresh_token", "")); return J.S(value, "access_token").NullIfEmpty(); }
                catch (ApiException e) { if (e.SessionExpired) throw; return null; }
            }
            private async Task SendMessage()
            {
                string message = InputText.Trim(); ConversationContracts.Request(networkId, conversationId, message);
                if (sample) { Bubble("You", message, null); InputText = ""; Bubble("eero", "This is a local sample. Sign in to send messages to eero.", null); return; }
                mcpToken = await Token(); if (closed) return;
                Bubble("You", message, null); InputText = "";
                await Stream(ConversationContracts.Request(networkId, conversationId, message, mcpToken));
            }
            private async Task Stream(object body)
            {
                if (sample) return;
                deltas.Remove("stream-reply");
                using (var source = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
                {
                    waitingForPermission = false; cancellation = source; stop.SetVisible(true); stop.SetSensitive(true); status.SetText("eero is replying…");
                    try { await api.StreamConversation(body, value => OnMain(delegate { if (!closed) Receive(value); }), source.Token); if (!closed) status.SetText("Reply finished."); }
                    catch (Exception)
                    {
                        if (!source.IsCancellationRequested) throw;
                        if (!closed) status.SetText(waitingForPermission ? "eero is waiting for your decision on the request above." : "Reply stopped. Reload conversations before sending again.");
                    }
                    finally { cancellation = null; if (!closed) stop.SetVisible(false); }
                }
                if (!closed && !String.IsNullOrEmpty(conversationId) && !historyItems.Any(h => h.Id == conversationId))
                {
                    historyItems.Insert(0, new HistoryItem { Id = conversationId, Title = "New conversation", When = "Just now" }); RenderHistory();
                }
            }

            // ---- Message rendering ----
            private Label Bubble(string sender, string content, string id)
            {
                HideWelcome();
                bool mine = sender == "You";
                // Selectable text brings GTK's own context menu with Copy.
                var text = Text(content, 14, "#17263C"); text.SetMarginBottom(0); text.SetSelectable(true); text.SetMaxWidthChars(72);
                var bubble = VBox(); bubble.AddCssClass(mine ? "chat-bubble-mine" : "chat-bubble-theirs"); bubble.Append(text);
                bubble.SetHalign(mine ? Gtk.Align.End : Gtk.Align.Start);
                if (mine) { bubble.Margin(80, 0, 0, 12); messages.Append(bubble); }
                else
                {
                    // An overlay keeps the avatar beside the bubble without a horizontal box, so wrapped text is measured at its real width.
                    var row = Overlay.New(); row.Margin(0, 0, 60, 4);
                    bubble.SetMarginStart(42); row.SetChild(bubble);
                    var avatar = Avatar("e", 30, 15); avatar.SetHalign(Gtk.Align.Start); avatar.SetValign(Gtk.Align.Start); row.AddOverlay(avatar);
                    messages.Append(row);
                }
                if (!String.IsNullOrEmpty(id)) deltas[id] = text; ScrollToEnd(); return text;
            }
            private void Note(string content)
            {
                HideWelcome();
                var note = Text(content, 12, "#64738A"); note.Margin(42, 2, 0, 12); note.AddCssClass("italic"); messages.Append(note);
            }
            private void Receive(object value)
            {
                if (J.S(value, "conversation_id").Length > 0) conversationId = J.S(value, "conversation_id");
                string type = J.S(value, "type"), id = J.S(value, "message_id");
                if (type == "Error") throw new InvalidOperationException("eero could not finish this reply. Reload conversations before trying again.");
                if (type == "InitialMessage" || type == "MessageDelta")
                {
                    string key = id.Length > 0 ? id : "stream-reply"; Label target;
                    if (!deltas.TryGetValue(key, out target)) target = Bubble("eero", "", key);
                    if (type == "InitialMessage") target.SetText(J.S(value, "content")); else target.SetText(target.GetText() + J.S(value, "content"));
                    if (J.B(value, "finished")) Feedback(id); ScrollToEnd();
                }
                if (type == "ToolUse" || type == "Reasoning") status.SetText("eero is checking your network…");
                if (J.Get(value, "permission_request") != null)
                { Permission(J.Get(value, "permission_request")); waitingForPermission = true; if (cancellation != null) cancellation.Cancel(); }
                if (J.Get(value, "widget_event") != null) Widget(J.Get(value, "widget_event"), true);
            }
            private void Feedback(string id)
            {
                if (id.Length == 0 || !feedback.Add(id)) return;
                var row = HBox(2); row.Margin(40, 0, 0, 10);
                var thanks = Text("", 12, "#64738A"); thanks.Margin(6, 0, 0, 0); thanks.SetValign(Gtk.Align.Center);
                var choices = new List<Button>();
                foreach (bool positive in new[] { true, false })
                {
                    bool choice = positive;
                    var b = Control("", async delegate
                    {
                        if (!sample) await api.Send("PUT", "/2.2/conversations/feedbacks", J.Obj("message_id", id, "positive", choice));
                        foreach (Button child in choices) child.SetSensitive(false);
                        thanks.SetText("Thanks for the feedback."); status.SetText("Feedback saved.");
                    });
                    b.SetChild(Glyph(choice ? "face-smile-symbolic" : "face-sad-symbolic", 14)); b.AddCssClass("flat"); b.Margin(0, 0, 2, 0);
                    b.SetTooltipText(choice ? "Helpful" : "Not helpful"); b.Name(choice ? "Helpful" : "Not helpful");
                    choices.Add(b); row.Append(b);
                }
                row.Append(thanks); messages.Append(row);
            }
            private Task<bool> ApproveDialog(string message)
            {
                var done = new TaskCompletionSource<bool>();
                var alert = Adw.AlertDialog.New("Approve eero's request?", message);
                alert.AddResponse("no", "No"); alert.AddResponse("yes", "Yes");
                alert.SetResponseAppearance("yes", Adw.ResponseAppearance.Suggested);
                alert.SetDefaultResponse("no"); alert.SetCloseResponse("no");
                alert.OnResponse += (sender, args) => { done.TrySetResult(args.Response == "yes"); };
                alert.Present(Window);
                return done.Task;
            }
            private void Permission(object permission)
            {
                string id = J.S(permission, "interrupt_id"); if (id.Length == 0 || !rendered.Add("permission:" + id)) return;
                HideWelcome();
                var tools = J.List(J.Get(permission, "tool_descriptions")).Select(v => J.S(v)).Where(v => v.Length > 0).ToList();
                var panel = VBox();
                var top = HBox(8); top.SetMarginBottom(8); top.Append(Glyph("channel-secure-symbolic", 16, "#274CDB"));
                var label = Text("eero needs your permission", 13, "#274CDB", true); label.SetMarginBottom(0); top.Append(label); panel.Append(top);
                var ask = Text(J.S(permission, "friendly_sentence"), 15, "#17263C", true); ask.Margin(0, 0, 0, 4); panel.Append(ask);
                foreach (string tool in tools) { var line = Text("•  " + tool, 13, "#64738A"); line.Margin(0, 2, 0, 0); panel.Append(line); }
                var row = HBox(8); row.SetMarginTop(12);
                var outcome = Text("", 13, "#64738A"); outcome.Margin(0, 12, 0, 0); outcome.SetVisible(false);
                foreach (bool approve in new[] { true, false })
                {
                    bool choice = approve;
                    var b = Control(choice ? "Approve" : "Decline", async delegate
                    {
                        if (answered.Contains(id)) { status.SetText("This request has already been answered."); return; }
                        if (choice && !await ApproveDialog(J.S(permission, "friendly_sentence") + "\n\n" + String.Join("\n", tools))) return;
                        answered.Add(id); row.SetVisible(false); outcome.SetText(choice ? "✓ You approved this." : "You declined this."); outcome.SetVisible(true);
                        if (sample) { status.SetText("Sample decision only."); return; }
                        mcpToken = await Token();
                        await Stream(ConversationContracts.Request(networkId, conversationId, null, mcpToken, J.Obj("approved", choice, "interrupt_id", id)));
                    });
                    if (choice) Accent(b);
                    b.Margin(0); row.Append(b);
                }
                panel.Append(row); panel.Append(outcome);
                var card = VBox(); card.AddCssClass("chat-permission"); card.Append(panel); card.Margin(42, 4, 60, 14); card.SetHalign(Gtk.Align.Start); card.SetSizeRequest(Math.Min(640, 420), -1);
                messages.Append(card); ScrollToEnd();
            }
            private void Widget(object widget, bool active)
            {
                string id = J.S(widget, "instance_id"), action = J.S(widget, "action");
                Widget previous;
                if (widgets.TryGetValue(id, out previous)) { messages.Remove(previous); widgets.Remove(id); }
                if (action.Equals("remove", StringComparison.OrdinalIgnoreCase) || action.Equals("delete", StringComparison.OrdinalIgnoreCase)) return;
                HideWelcome();
                object properties = J.Get(widget, "properties");
                var panel = VBox();
                var title = Text(J.S(widget, "content_summary").NullIfEmpty() ?? J.First(properties, "prompt_text", "title", "description").NullIfEmpty() ?? Human(J.S(widget, "widget_type")), 15, "#17263C", true); title.Margin(0, 0, 0, 6); panel.Append(title);
                foreach (string field in new[] { "name", "location", "model", "status", "connected", "connection_type", "connection_state", "device_type", "is_guest", "is_private", "score_bars", "mesh_quality_bars", "wan_type", "eero", "port_number", "negotiated_speed", "total_devices", "paused_devices", "accessory_type", "bars", "cellular_backup_status", "configuration_status" })
                {
                    object raw = J.Get(properties, field); string value = raw is bool ? ((bool)raw ? "Yes" : "No") : Friendly(J.S(properties, field));
                    if (!String.IsNullOrEmpty(value)) panel.Append(DetailRow(Human(field), value, false));
                }
                foreach (object contact in J.List(J.Get(properties, "contacts"))) panel.Append(DetailRow(J.S(contact, "title"), J.S(contact, "content"), false));
                var choices = WrapPanel(); choices.SetMarginTop(10); int count = 0;
                foreach (object choice in J.List(J.Get(properties, "choices")))
                {
                    string prompt = J.S(choice); if (!active || prompt.Length == 0) continue;
                    var chip = Control(prompt, async delegate { InputText = prompt; await SendMessage(); }); chip.Margin(0); choices.Add(chip); count++;
                }
                if (count > 0) panel.Append(choices);
                var card = VBox(); card.AddCssClass("chat-card"); card.Append(panel); card.Margin(42, 0, 60, 12); card.SetHalign(Gtk.Align.Start); card.SetSizeRequest(Math.Min(640, 420), -1);
                messages.Append(card); widgets[id] = card; ScrollToEnd();
            }
            internal void Preview()
            {
                historyItems.Clear();
                historyItems.Add(new HistoryItem { Id = "sample-conversation", Title = "Check my connectivity", When = "Today" });
                historyItems.Add(new HistoryItem { Id = "sample-older", Title = "Slow wifi upstairs", When = "Last week" });
                Reset(); conversationId = "sample-conversation"; RenderHistory();
                Bubble("You", "Check my connectivity", null); Bubble("eero", "Your sample network is online. The gateway and two wireless eeros are connected.", "sample-reply"); Feedback("sample-reply");
                Permission(J.Obj("interrupt_id", "sample-permission", "friendly_sentence", "May I run a connection diagnostic?", "tool_descriptions", new[] { "Check internet health on the selected network." }));
                status.SetText("Sample conversation · No account requests or changes.");
            }
        }
    }
}
