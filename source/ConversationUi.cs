using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        internal static Window ChatSample()
        { var window = new ConversationWindow(null, "sample-network", "Sample home", true, false); window.Preview(); return window; }
        private void OpenChat(bool history = false)
        {
            if (String.IsNullOrEmpty(networkUrl)) throw new InvalidOperationException("Select a network before opening chat or its support history.");
            var existing = OwnedWindows.OfType<ConversationWindow>().FirstOrDefault();
            if (existing != null) { existing.Activate(); return; }
            if (!history) { var warmup = WarmChat(NetworkId); }
            new ConversationWindow(api, NetworkId, J.S(network, "name"), preview, history) { Owner = this }.Show();
        }
        private void ChatPage(bool history)
        {
            Sub(history ? "Read or continue your earlier conversations with eero." : "Ask eero about your network. It can suggest changes, and nothing changes until you approve it.");
            var open = RowAction(history ? "Open history" : "Start chat", delegate { OpenChat(history); }); open.Style = (Style)FindResource("AccentButton"); CompactAction(open);
            Group(null,
                SettingRow(history ? "Past conversations" : "Chat with eero", history ? "Opens in its own window. Pick a conversation from the list on the left." : "Opens in its own window so you can keep using the app.", null, open),
                SettingRow(history ? "Start a new chat" : "Past conversations", history ? "Ask eero something new." : "Read or continue earlier chats.", null, RowAction("Open", delegate { OpenChat(!history); })));
            Group("Other help",
                SettingRow("eero help articles", "Step-by-step guides on eero's website.", null, RowAction("Open ↗", delegate { OpenServiceWindow("eero support", "https://www.eero.com/support"); })),
                SettingRow("Alexa", "Chat with Alexa on the web. Opens in its own window; features depend on your Amazon account.", null, RowAction("Open ↗", delegate { OpenServiceWindow("Alexa", "https://alexa.com/"); })));
            page.Children.Add(Text("Chat availability depends on your eero account, region and network.", 13, "#64738A"));
        }

        // Chat window: past conversations on the left, the conversation on the right, and a composer at the bottom.
        private sealed class ConversationWindow : Window
        {
            private static readonly string[] StarterPrompts = { "Is my internet working right now?", "Why is my wifi slow?", "Which devices use the most data?", "How can I improve coverage upstairs?" };
            private readonly EeroApi api;
            private readonly string networkId;
            private readonly bool sample;
            private readonly StackPanel messages, historyList;
            private readonly ScrollViewer scroll;
            private readonly TextBox input;
            private readonly TextBlock status, placeholder;
            private readonly Button send, stop, fresh, reload;
            private readonly Dictionary<string, TextBlock> deltas = new Dictionary<string, TextBlock>();
            private readonly HashSet<string> rendered = new HashSet<string>();
            private readonly HashSet<string> feedback = new HashSet<string>();
            private readonly HashSet<string> answered = new HashSet<string>();
            private readonly Dictionary<string, UIElement> widgets = new Dictionary<string, UIElement>();
            private readonly List<HistoryItem> historyItems = new List<HistoryItem>();
            private StackPanel welcome; private WrapPanel suggestions;
            private CancellationTokenSource cancellation;
            private string conversationId, mcpToken;
            private bool running, closed, waitingForPermission;

            public ConversationWindow(EeroApi api, string networkId, string networkName, bool sample, bool showHistory)
            {
                this.api = api; this.networkId = networkId; this.sample = sample;
                Title = "Chat with eero · " + networkName; Width = 1060; Height = 820; MinWidth = 780; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
                SourceInitialized += delegate { Program.ThemeNativeWindow(this); };
                var root = new Grid { Background = Brush("#F3F3F3") };
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) }); root.ColumnDefinitions.Add(new ColumnDefinition());

                // Sidebar: new chat and past conversations.
                var side = new Grid { Margin = new Thickness(14, 18, 10, 14) };
                side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); side.RowDefinitions.Add(new RowDefinition()); side.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                fresh = Control("+  New chat", async delegate { Reset(); await Recommendations(); }); fresh.Style = (Style)FindResource("AccentButton"); fresh.HorizontalAlignment = HorizontalAlignment.Stretch; fresh.Margin = new Thickness(0, 0, 0, 16); side.Children.Add(fresh);
                var listHead = new Grid { Margin = new Thickness(4, 0, 0, 6) }; listHead.ColumnDefinitions.Add(new ColumnDefinition()); listHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var listTitle = Text("CONVERSATIONS", 10, "#64738A", true); listTitle.Margin = new Thickness(0); listTitle.VerticalAlignment = VerticalAlignment.Center; listHead.Children.Add(listTitle);
                reload = Control("", LoadHistory); reload.Content = Glyph("", 13); reload.Style = (Style)FindResource("QuietButton"); reload.Margin = new Thickness(0); reload.Padding = new Thickness(6, 4, 6, 4); reload.ToolTip = "Reload conversations";
                System.Windows.Automation.AutomationProperties.SetName(reload, "Reload conversations"); Grid.SetColumn(reload, 1); listHead.Children.Add(reload);
                Grid.SetRow(listHead, 1); side.Children.Add(listHead);
                historyList = new StackPanel();
                var historyScroll = new ScrollViewer { Content = historyList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false }; Grid.SetRow(historyScroll, 2); side.Children.Add(historyScroll);
                var network = Text(networkName, 12, "#64738A"); network.Margin = new Thickness(4, 10, 0, 0); network.TextTrimming = TextTrimming.CharacterEllipsis; network.TextWrapping = TextWrapping.NoWrap; Grid.SetRow(network, 3); side.Children.Add(network);
                root.Children.Add(side);

                // Conversation area.
                var main = new Grid(); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); main.RowDefinitions.Add(new RowDefinition()); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var frame = new Border { Background = Brush("#F4F6FA"), BorderBrush = Brush("#E5EAF3"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 8, 8, 8), Child = main };
                Grid.SetColumn(frame, 1); root.Children.Add(frame);
                var header = new StackPanel { Margin = new Thickness(28, 18, 28, 6) };
                var heading = Text("Chat with eero", 22, "#17263C", true); heading.Margin = new Thickness(0); header.Children.Add(heading);
                header.Children.Add(Text(sample ? "Sample conversation · nothing is sent to eero" : "Answers come from eero's assistant. Changes always need your approval.", 12, "#64738A"));
                main.Children.Add(header);
                messages = new StackPanel { Margin = new Thickness(28, 8, 28, 8) };
                scroll = new ScrollViewer { Content = messages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 1); main.Children.Add(scroll);

                // Composer.
                var bottom = new StackPanel { Margin = new Thickness(24, 6, 24, 12) };
                var box = new Grid(); box.ColumnDefinitions.Add(new ColumnDefinition()); box.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var inputHost = new Grid();
                input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 44, MaxHeight = 160, MaxLength = 8000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent, Padding = new Thickness(4, 10, 4, 10), VerticalContentAlignment = VerticalAlignment.Top };
                System.Windows.Automation.AutomationProperties.SetName(input, "Message to eero");
                inputHost.Children.Add(input);
                placeholder = Text("Ask eero about your network…", 14, "#64738A"); placeholder.Margin = new Thickness(6, 11, 0, 0); placeholder.IsHitTestVisible = false; placeholder.VerticalAlignment = VerticalAlignment.Top; inputHost.Children.Add(placeholder);
                input.TextChanged += delegate { placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
                box.Children.Add(inputHost);
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(8, 0, 0, 2) };
                // Stop must work while a reply is streaming, so it bypasses Perform's one-action-at-a-time guard.
                stop = new Button { Content = "Stop", Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 8, 0) };
                stop.Click += delegate { if (cancellation != null) { cancellation.Cancel(); status.Text = "Stopping…"; } };
                send = Control("Send", SendMessage); send.Style = (Style)FindResource("AccentButton"); send.Margin = new Thickness(0);
                buttons.Children.Add(stop); buttons.Children.Add(send); Grid.SetColumn(buttons, 1); box.Children.Add(buttons);
                bottom.Children.Add(new Border { Background = Surface, BorderBrush = Brush("#E5EAF3"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4, 8, 8), Child = box });
                input.PreviewKeyDown += async delegate(object sender, KeyEventArgs e)
                {
                    if (e.Key != Key.Enter || (Keyboard.Modifiers & ModifierKeys.Shift) != 0) return;
                    e.Handled = true; if (input.Text.Trim().Length > 0) await Perform(SendMessage);
                };
                var foot = new Grid { Margin = new Thickness(6, 6, 6, 0) }; foot.ColumnDefinitions.Add(new ColumnDefinition()); foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                status = Text("Review any change eero asks to make before you approve it.", 12, "#64738A"); status.Margin = new Thickness(0); foot.Children.Add(status);
                var keys = Text("Enter to send · Shift+Enter for a new line", 12, "#64738A"); keys.Margin = new Thickness(12, 0, 0, 0); Grid.SetColumn(keys, 1); foot.Children.Add(keys);
                bottom.Children.Add(foot); Grid.SetRow(bottom, 2); main.Children.Add(bottom);
                Content = root;

                Reset();
                Closed += delegate { closed = true; if (cancellation != null) cancellation.Cancel(); mcpToken = conversationId = null; deltas.Clear(); messages.Children.Clear(); input.Clear(); };
                Loaded += async delegate
                {
                    input.Focus();
                    if (sample) { Preview(); return; }
                    await Perform(async delegate { await LoadHistory(); if (!showHistory) await Recommendations(); });
                };
            }

            private sealed class HistoryItem { public string Id, Title, When; }

            private Button Control(string label, Func<Task> action)
            {
                var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 8) };
                button.Click += async delegate { await Perform(action); }; return button;
            }

            private async Task Perform(Func<Task> action)
            {
                if (running || closed) return;
                running = true; SetBusy(true);
                try { await action(); }
                catch (Exception e) { if (!closed) status.Text = cancellation != null && cancellation.IsCancellationRequested ? "Reply stopped. Reload conversations to see what eero saved before sending again." : ConversationContracts.Error(e); }
                finally { running = false; if (!closed) { SetBusy(false); input.Focus(); } }
            }
            private void SetBusy(bool value)
            {
                send.IsEnabled = fresh.IsEnabled = reload.IsEnabled = historyList.IsEnabled = input.IsEnabled = !value;
                if (suggestions != null) suggestions.IsEnabled = !value;
                if (!value) stop.Visibility = Visibility.Collapsed;
            }

            // ---- Conversation list ----
            private void RenderHistory()
            {
                historyList.Children.Clear();
                if (historyItems.Count == 0)
                {
                    var none = Text(sample ? "Sample conversations appear here." : "No past conversations yet. Start one with New chat.", 12, "#64738A"); none.Margin = new Thickness(4, 6, 4, 0); historyList.Children.Add(none); return;
                }
                foreach (HistoryItem raw in historyItems)
                {
                    HistoryItem item = raw;
                    var b = new Button { Style = (Style)FindResource("ListRowButton"), MinHeight = 44, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 2), Tag = item.Id };
                    var words = new StackPanel();
                    var title = Text(item.Title, 13, "#17263C", item.Id == conversationId); title.Margin = new Thickness(0); title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis; words.Children.Add(title);
                    if (!String.IsNullOrEmpty(item.When)) { var when = Text(item.When, 11, "#64738A"); when.Margin = new Thickness(0, 2, 0, 0); words.Children.Add(when); }
                    b.Content = words; b.ToolTip = item.Title;
                    if (item.Id == conversationId) b.Background = Brush("#EEF3FF");
                    System.Windows.Automation.AutomationProperties.SetName(b, item.Title);
                    b.Click += async delegate { if (item.Id == conversationId) return; await Perform(async delegate { await LoadConversation(item.Id); }); };
                    historyList.Children.Add(b);
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
                if (historyItems.Count > 0 && String.IsNullOrEmpty(conversationId)) status.Text = "Pick a conversation on the left to read it, or ask something new.";
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
                status.Text = "Conversation loaded. You can carry on below."; scroll.ScrollToEnd();
            }

            // ---- Welcome and suggestions ----
            private void Reset()
            {
                conversationId = null; messages.Children.Clear(); deltas.Clear(); rendered.Clear(); answered.Clear(); feedback.Clear(); widgets.Clear(); waitingForPermission = false;
                welcome = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 60, 0, 20), MaxWidth = 560 };
                var mark = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(28), Background = Brush("#EEF3FF"), HorizontalAlignment = HorizontalAlignment.Center, Child = Glyph("", 26, "#274CDB") }; welcome.Children.Add(mark);
                var hello = Text("How can eero help?", 22, "#17263C", true); hello.Margin = new Thickness(0, 16, 0, 4); hello.HorizontalAlignment = HorizontalAlignment.Center; welcome.Children.Add(hello);
                var sub = Text("Ask about your internet, wifi, devices or settings. eero can check your network and suggest fixes.", 13, "#64738A"); sub.TextAlignment = TextAlignment.Center; sub.HorizontalAlignment = HorizontalAlignment.Center; welcome.Children.Add(sub);
                suggestions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) }; welcome.Children.Add(suggestions);
                messages.Children.Add(welcome);
                if (historyList != null) RenderHistory();
            }
            private void Suggest(string label, string prompt)
            {
                var chip = new Button { Content = label, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(4), Background = Surface, ToolTip = prompt };
                chip.Click += async delegate { input.Text = prompt; await Perform(SendMessage); };
                if (suggestions != null && welcome != null) suggestions.Children.Add(chip); else { var row = new WrapPanel(); row.Children.Add(chip); messages.Children.Add(row); }
            }
            private void HideWelcome() { if (welcome != null) { messages.Children.Remove(welcome); welcome = null; suggestions = null; } }
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
                string message = input.Text.Trim(); ConversationContracts.Request(networkId, conversationId, message);
                if (sample) { Bubble("You", message, null); input.Clear(); Bubble("eero", "This is a local sample. Sign in to send messages to eero.", null); return; }
                mcpToken = await Token(); if (closed) return;
                Bubble("You", message, null); input.Clear();
                await Stream(ConversationContracts.Request(networkId, conversationId, message, mcpToken));
            }
            private async Task Stream(object body)
            {
                if (sample) return;
                deltas.Remove("stream-reply");
                using (var source = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
                {
                    waitingForPermission = false; cancellation = source; stop.Visibility = Visibility.Visible; stop.IsEnabled = true; status.Text = "eero is replying…";
                    try { await api.StreamConversation(body, value => Dispatcher.Invoke(new Action(delegate { if (!closed) Receive(value); })), source.Token); if (!closed) status.Text = "Reply finished."; }
                    catch (Exception)
                    {
                        if (!source.IsCancellationRequested) throw;
                        if (!closed) status.Text = waitingForPermission ? "eero is waiting for your decision on the request above." : "Reply stopped. Reload conversations before sending again.";
                    }
                    finally { cancellation = null; if (!closed) stop.Visibility = Visibility.Collapsed; }
                }
                if (!closed && !String.IsNullOrEmpty(conversationId) && !historyItems.Any(h => h.Id == conversationId))
                {
                    historyItems.Insert(0, new HistoryItem { Id = conversationId, Title = "New conversation", When = "Just now" }); RenderHistory();
                }
            }

            // ---- Message rendering ----
            private TextBlock Bubble(string sender, string content, string id)
            {
                HideWelcome();
                bool mine = sender == "You";
                var text = Text(content, 14, "#17263C"); text.Margin = new Thickness(0); text.LineHeight = 21;
                var copy = new MenuItem { Header = "Copy" }; copy.Click += delegate { if (text.Text.Length > 0) Clipboard.SetText(text.Text); };
                var bubble = new Border
                {
                    Background = mine ? Brush("#EEF3FF") : Surface, BorderBrush = Brush("#E5EAF3"), BorderThickness = new Thickness(mine ? 0 : 1),
                    CornerRadius = mine ? new CornerRadius(14, 14, 4, 14) : new CornerRadius(14, 14, 14, 4), Padding = new Thickness(14, 10, 14, 10),
                    Child = text, MaxWidth = 640, HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left, ContextMenu = new ContextMenu { Items = { copy } }
                };
                if (mine) { bubble.Margin = new Thickness(80, 0, 0, 12); messages.Children.Add(bubble); }
                else
                {
                    var row = new Grid { Margin = new Thickness(0, 0, 60, 4) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) }); row.ColumnDefinitions.Add(new ColumnDefinition());
                    var initial = Text("e", 15, "#274CDB", true); initial.Margin = new Thickness(0, -2, 0, 0); initial.HorizontalAlignment = HorizontalAlignment.Center; initial.VerticalAlignment = VerticalAlignment.Center;
                    row.Children.Add(new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Brush("#EEF3FF"), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Child = initial });
                    Grid.SetColumn(bubble, 1); row.Children.Add(bubble); messages.Children.Add(row);
                }
                if (!String.IsNullOrEmpty(id)) deltas[id] = text; scroll.ScrollToEnd(); return text;
            }
            private void Note(string content)
            {
                HideWelcome();
                var note = Text(content, 12, "#64738A"); note.Margin = new Thickness(42, 2, 0, 12); note.FontStyle = FontStyles.Italic; messages.Children.Add(note);
            }
            private void Receive(object value)
            {
                if (J.S(value, "conversation_id").Length > 0) conversationId = J.S(value, "conversation_id");
                string type = J.S(value, "type"), id = J.S(value, "message_id");
                if (type == "Error") throw new InvalidOperationException("eero could not finish this reply. Reload conversations before trying again.");
                if (type == "InitialMessage" || type == "MessageDelta")
                {
                    string key = id.Length > 0 ? id : "stream-reply"; TextBlock target;
                    if (!deltas.TryGetValue(key, out target)) target = Bubble("eero", "", key);
                    if (type == "InitialMessage") target.Text = J.S(value, "content"); else target.Text += J.S(value, "content");
                    if (J.B(value, "finished")) Feedback(id); scroll.ScrollToEnd();
                }
                if (type == "ToolUse" || type == "Reasoning") status.Text = "eero is checking your network…";
                if (J.Get(value, "permission_request") != null)
                { Permission(J.Get(value, "permission_request")); waitingForPermission = true; if (cancellation != null) cancellation.Cancel(); }
                if (J.Get(value, "widget_event") != null) Widget(J.Get(value, "widget_event"), true);
            }
            private void Feedback(string id)
            {
                if (id.Length == 0 || !feedback.Add(id)) return;
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(40, 0, 0, 10) };
                var thanks = Text("", 12, "#64738A"); thanks.Margin = new Thickness(6, 0, 0, 0); thanks.VerticalAlignment = VerticalAlignment.Center;
                foreach (bool positive in new[] { true, false })
                {
                    bool choice = positive;
                    var b = Control("", async delegate
                    {
                        if (!sample) await api.Send("PUT", "/2.2/conversations/feedbacks", J.Obj("message_id", id, "positive", choice));
                        foreach (UIElement child in row.Children) if (child is Button) ((Button)child).IsEnabled = false;
                        thanks.Text = "Thanks for the feedback."; status.Text = "Feedback saved.";
                    });
                    b.Content = Glyph(choice ? "" : "", 14); b.Style = (Style)FindResource("QuietButton"); b.Margin = new Thickness(0, 0, 2, 0); b.Padding = new Thickness(6, 4, 6, 4);
                    b.ToolTip = choice ? "Helpful" : "Not helpful"; System.Windows.Automation.AutomationProperties.SetName(b, choice ? "Helpful" : "Not helpful");
                    row.Children.Add(b);
                }
                row.Children.Add(thanks); messages.Children.Add(row);
            }
            private void Permission(object permission)
            {
                string id = J.S(permission, "interrupt_id"); if (id.Length == 0 || !rendered.Add("permission:" + id)) return;
                HideWelcome();
                var tools = J.List(J.Get(permission, "tool_descriptions")).Select(v => J.S(v)).Where(v => v.Length > 0).ToList();
                var panel = new StackPanel();
                var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) }; var shield = Glyph("", 16, "#274CDB"); shield.Margin = new Thickness(0, 0, 8, 0); top.Children.Add(shield);
                var label = Text("eero needs your permission", 13, "#274CDB", true); label.Margin = new Thickness(0); top.Children.Add(label); panel.Children.Add(top);
                var ask = Text(J.S(permission, "friendly_sentence"), 15, "#17263C", true); ask.Margin = new Thickness(0, 0, 0, 4); panel.Children.Add(ask);
                foreach (string tool in tools) { var line = Text("•  " + tool, 13, "#64738A"); line.Margin = new Thickness(0, 2, 0, 0); panel.Children.Add(line); }
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
                var outcome = Text("", 13, "#64738A"); outcome.Margin = new Thickness(0, 12, 0, 0); outcome.Visibility = Visibility.Collapsed;
                foreach (bool approve in new[] { true, false })
                {
                    bool choice = approve;
                    var b = Control(choice ? "Approve" : "Decline", async delegate
                    {
                        if (answered.Contains(id)) { status.Text = "This request has already been answered."; return; }
                        if (choice && MessageBox.Show(this, J.S(permission, "friendly_sentence") + "\n\n" + String.Join("\n", tools), "Approve eero's request?", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                        answered.Add(id); row.Visibility = Visibility.Collapsed; outcome.Text = choice ? "✓ You approved this." : "You declined this."; outcome.Visibility = Visibility.Visible;
                        if (sample) { status.Text = "Sample decision only."; return; }
                        mcpToken = await Token();
                        await Stream(ConversationContracts.Request(networkId, conversationId, null, mcpToken, J.Obj("approved", choice, "interrupt_id", id)));
                    });
                    if (choice) b.Style = (Style)FindResource("AccentButton");
                    b.Margin = new Thickness(0, 0, 8, 0); row.Children.Add(b);
                }
                panel.Children.Add(row); panel.Children.Add(outcome);
                var card = new Border { Background = Surface, BorderBrush = Brush("#274CDB"), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(42, 4, 60, 14), Child = panel, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 640 };
                messages.Children.Add(card); scroll.ScrollToEnd();
            }
            private void Widget(object widget, bool active)
            {
                string id = J.S(widget, "instance_id"), action = J.S(widget, "action");
                UIElement previous;
                if (widgets.TryGetValue(id, out previous)) { messages.Children.Remove(previous); widgets.Remove(id); }
                if (action.Equals("remove", StringComparison.OrdinalIgnoreCase) || action.Equals("delete", StringComparison.OrdinalIgnoreCase)) return;
                HideWelcome();
                object properties = J.Get(widget, "properties");
                var panel = new StackPanel();
                var title = Text(J.S(widget, "content_summary").NullIfEmpty() ?? J.First(properties, "prompt_text", "title", "description").NullIfEmpty() ?? Human(J.S(widget, "widget_type")), 15, "#17263C", true); title.Margin = new Thickness(0, 0, 0, 6); panel.Children.Add(title);
                foreach (string field in new[] { "name", "location", "model", "status", "connected", "connection_type", "connection_state", "device_type", "is_guest", "is_private", "score_bars", "mesh_quality_bars", "wan_type", "eero", "port_number", "negotiated_speed", "total_devices", "paused_devices", "accessory_type", "bars", "cellular_backup_status", "configuration_status" })
                {
                    object raw = J.Get(properties, field); string value = raw is bool ? ((bool)raw ? "Yes" : "No") : Friendly(J.S(properties, field));
                    if (!String.IsNullOrEmpty(value)) panel.Children.Add(DetailRow(Human(field), value, false));
                }
                foreach (object contact in J.List(J.Get(properties, "contacts"))) panel.Children.Add(DetailRow(J.S(contact, "title"), J.S(contact, "content"), false));
                var choices = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
                foreach (object choice in J.List(J.Get(properties, "choices")))
                {
                    string prompt = J.S(choice); if (!active || prompt.Length == 0) continue;
                    var chip = Control(prompt, async delegate { input.Text = prompt; await SendMessage(); }); chip.Padding = new Thickness(12, 6, 12, 6); choices.Children.Add(chip);
                }
                if (choices.Children.Count > 0) panel.Children.Add(choices);
                var card = new Border { Background = Surface, BorderBrush = Brush("#E5EAF3"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(42, 0, 60, 12), Child = panel, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
                messages.Children.Add(card); widgets[id] = card; scroll.ScrollToEnd();
            }
            internal void Preview()
            {
                historyItems.Clear();
                historyItems.Add(new HistoryItem { Id = "sample-conversation", Title = "Check my connectivity", When = "Today" });
                historyItems.Add(new HistoryItem { Id = "sample-older", Title = "Slow wifi upstairs", When = "Last week" });
                Reset(); conversationId = "sample-conversation"; RenderHistory();
                Bubble("You", "Check my connectivity", null); Bubble("eero", "Your sample network is online. The gateway and two wireless eeros are connected.", "sample-reply"); Feedback("sample-reply");
                Permission(J.Obj("interrupt_id", "sample-permission", "friendly_sentence", "May I run a connection diagnostic?", "tool_descriptions", new[] { "Check internet health on the selected network." }));
                status.Text = "Sample conversation · No account requests or changes.";
            }
        }
    }
}
