using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gtk;

namespace EeroDesktop
{
    // Native GTK 4 / libadwaita building blocks. Page code from the Windows build keeps
    // its vocabulary (Text, Card, Group, SettingRow...) but every helper returns real GTK
    // widgets styled with libadwaita's own palette, so light/dark follow the desktop.
    internal static class Css
    {
        private static CssProvider provider;
        // Legacy palette values used across page code, mapped to semantic style classes.
        public static string ColorClass(string color)
        {
            switch ((color ?? "").ToUpperInvariant())
            {
                case "#64738A": case "#52647C": case "#A3B2CC": return "c-muted";
                case "#274CDB": case "#345BEB": case "#9BB4FF": return "c-accent";
                case "#0D7959": return "c-good";
                case "#926410": return "c-warn";
                case "#B5333D": return "c-danger";
                default: return null;
            }
        }
        public static string BackgroundClass(string color)
        {
            switch ((color ?? "").ToUpperInvariant())
            {
                case "#E4F5EE": return "bg-good";
                case "#EEF3FF": case "#233654": return "bg-soft";
                case "#F3F3F3": return "bg-rail";
                case "#E5EAF3": case "#D6DFEC": case "#34425B": return "bg-edge";
                case "#19253A": case "#152137": return "bg-surface";
                default: return null;
            }
        }
        public static void Load()
        {
            if (provider != null) return;
            var css = new StringBuilder();
            css.Append(".c-muted { color: alpha(currentColor, 0.62); } .c-muted.c-strong { color: alpha(@window_fg_color, 0.62); }");
            css.Append(".c-accent { color: @accent_color; } .c-good { color: @success_color; } .c-warn { color: @warning_color; } .c-danger { color: @error_color; }");
            css.Append(".bold { font-weight: 600; } .light { font-weight: 300; }");
            css.Append(".bg-good { background-color: alpha(@success_color, 0.14); } .bg-soft { background-color: alpha(@accent_bg_color, 0.14); } .bg-rail { background-color: @sidebar_bg_color; } .bg-edge { background-color: alpha(currentColor, 0.15); } .bg-surface { background-color: @card_bg_color; }");
            css.Append(".pill-badge { border-radius: 4px; padding: 3px 9px; } .tile { border-radius: 8px; min-width: 36px; min-height: 36px; }");
            css.Append(".hairline { min-height: 1px; background-color: alpha(currentColor, 0.12); }");
            css.Append(".card.pad-0 { padding: 0; } .page-card { padding: 24px; } .detail-row { border-bottom: 1px solid alpha(currentColor, 0.10); min-height: 34px; }");
            css.Append(".danger-button { color: @error_color; } .danger-button.suggested-action { color: @accent_fg_color; background-color: @error_bg_color; }");
            css.Append(".status-bar { border-top: 1px solid alpha(currentColor, 0.12); padding: 8px 24px; }");
            css.Append(".brand { font-weight: 300; } .theme-tile { padding: 12px; } .theme-tile.chosen { box-shadow: inset 0 0 0 2px @accent_color; }");
            css.Append(".chat-bubble { border-radius: 12px; padding: 10px 14px; } .chat-mine { background-color: @accent_bg_color; color: @accent_fg_color; } .chat-theirs { background-color: @card_bg_color; }");
            css.Append(".sample-light { background-color: #F3F3F3; color: #202445; } .sample-dark { background-color: #1C1C1E; color: #F3F3F5; }");
            css.Append(".sample-card-light { background-color: #FFFFFF; border: 1px solid #E1E1E5; border-radius: 5px; padding: 8px 10px; } .sample-card-dark { background-color: #272729; border: 1px solid #3D3D42; border-radius: 5px; padding: 8px 10px; }");
            css.Append(".sample-bar-light { background-color: #0057FF; border-radius: 3px; min-height: 5px; } .sample-bar-dark { background-color: #82AAFF; border-radius: 3px; min-height: 5px; } .sample-line-light { background-color: #E1E1E5; border-radius: 3px; min-height: 5px; } .sample-line-dark { background-color: #3D3D42; border-radius: 3px; min-height: 5px; }");
            foreach (int size in Enumerable.Range(8, 70)) css.Append(".fs-" + size + " { font-size: " + size + "px; }");
            foreach (int pad in new[] { 0, 4, 8, 10, 12, 14, 16, 18, 20, 24, 28 }) css.Append(".pad-" + pad + " { padding: " + pad + "px; }");
            provider = CssProvider.New();
            provider.LoadFromString(css.ToString());
            StyleContext.AddProviderForDisplay(Gdk.Display.GetDefault(), provider, 600);
        }
    }

    internal static class Icons
    {
        // Segoe Fluent glyphs used by the shared page code, mapped to freedesktop/Adwaita symbolic icons.
        private static readonly Dictionary<char, string> Map = new Dictionary<char, string> {
            { '', "network-wireless-symbolic" }, { '', "mark-location-symbolic" }, { '', "list-add-symbolic" },
            { '', "emblem-system-symbolic" }, { '', "system-users-symbolic" }, { '', "insert-link-symbolic" },
            { '', "system-search-symbolic" }, { '', "go-previous-symbolic" }, { '', "view-refresh-symbolic" },
            { '', "channel-secure-symbolic" }, { '', "object-select-symbolic" }, { '', "go-next-symbolic" },
            { '', "computer-symbolic" }, { '', "web-browser-symbolic" }, { '', "avatar-default-symbolic" },
            { '', "preferences-color-symbolic" }, { '', "dialog-warning-symbolic" }, { '', "emblem-important-symbolic" },
            { '', "go-home-symbolic" }, { '', "network-wired-symbolic" }, { '', "software-update-available-symbolic" },
            { '', "help-browser-symbolic" }, { '', "network-transmit-receive-symbolic" }, { '', "dialog-password-symbolic" },
            { '', "face-sad-symbolic" }, { '', "face-smile-symbolic" }, { '', "user-available-symbolic" },
            { '', "folder-publicshare-symbolic" }, { '', "applications-system-symbolic" }, { '', "power-profile-power-saver-symbolic" },
            { '', "dialog-information-symbolic" }, { '', "network-server-symbolic" }, { '', "network-workgroup-symbolic" },
            { '', "x-office-spreadsheet-symbolic" }, { '', "security-high-symbolic" }, { '', "preferences-system-notifications-symbolic" }
        };
        public static string Name(string glyph)
        {
            if (String.IsNullOrEmpty(glyph)) return null;
            if (glyph.EndsWith("-symbolic", StringComparison.Ordinal)) return glyph;
            string name; return Map.TryGetValue(glyph[0], out name) ? name : null;
        }
    }

    // Small fluent helpers that keep page code compact.
    internal static class WidgetExtensions
    {
        public static T Margin<T>(this T w, int left, int top, int right, int bottom) where T : Widget
        { w.SetMarginStart(Math.Max(0, left)); w.SetMarginTop(Math.Max(0, top)); w.SetMarginEnd(Math.Max(0, right)); w.SetMarginBottom(Math.Max(0, bottom)); return w; }
        public static T Margin<T>(this T w, int all) where T : Widget { return w.Margin(all, all, all, all); }
        public static T Css<T>(this T w, params string[] classes) where T : Widget { foreach (string c in classes) if (!String.IsNullOrEmpty(c)) w.AddCssClass(c); return w; }
        public static T Tip<T>(this T w, string text) where T : Widget { w.SetTooltipText(text); return w; }
        public static T Visible<T>(this T w, bool visible) where T : Widget { w.SetVisible(visible); return w; }
        public static T Expand<T>(this T w, bool horizontal = true, bool vertical = false) where T : Widget { w.SetHexpand(horizontal); w.SetVexpand(vertical); return w; }
        public static T Align<T>(this T w, Align horizontal, Align vertical) where T : Widget { w.SetHalign(horizontal); w.SetValign(vertical); return w; }
        [System.Runtime.InteropServices.DllImport("libgtk-4.so.1")]
        private static extern void gtk_accessible_update_property(IntPtr self, int property, [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPUTF8Str)] string value, int end);
        // Accessible name for screen readers (WPF AutomationProperties.Name).
        public static T Name<T>(this T w, string accessibleName) where T : Widget
        {
            if (!String.IsNullOrEmpty(accessibleName)) gtk_accessible_update_property(w.Handle.DangerousGetHandle(), (int)AccessibleProperty.Label, accessibleName, -1);
            return w;
        }
        public static List<Widget> ChildWidgets(this Widget parent)
        {
            var list = new List<Widget>();
            for (Widget c = parent.GetFirstChild(); c != null; c = c.GetNextSibling()) list.Add(c);
            return list;
        }
        public static void Clear(this Box box) { foreach (Widget c in box.ChildWidgets()) box.Remove(c); }
        public static void Clear(this ListBox box) { box.RemoveAll(); }
        public static int Count(this Box box) { return box.ChildWidgets().Count; }
        public static void Add(this Box box, Widget child) { if (child != null) box.Append(child); }
        public static void Add(this FlowBox box, Widget child) { if (child != null) box.Append(child); }
    }

    // A one-shot or repeating GLib timer with DispatcherTimer-like Start/Stop.
    internal sealed class UiTimer
    {
        private uint source; private readonly uint seconds; private readonly Action tick;
        public UiTimer(TimeSpan interval, Action onTick) { seconds = (uint)Math.Max(1, interval.TotalSeconds); tick = onTick; }
        public bool IsEnabled { get { return source != 0; } }
        public void Start()
        {
            if (source != 0) return;
            source = GLib.Functions.TimeoutAddSeconds(GLib.Constants.PRIORITY_DEFAULT, seconds, delegate { tick(); return source != 0; });
        }
        public void Stop() { if (source == 0) return; uint id = source; source = 0; GLib.Functions.SourceRemove(id); }
    }

    public sealed partial class MainWindow
    {
        internal static Label Text(string s, double size = 14, string color = "#17263C", bool bold = false)
        {
            var l = Label.New(s ?? "");
            l.SetWrap(true); l.SetWrapMode(Pango.WrapMode.WordChar); l.SetXalign(0); l.SetHalign(Gtk.Align.Fill); l.SetSelectable(false);
            l.AddCssClass("fs-" + (int)Math.Round(Math.Max(8, Math.Min(77, size))));
            string c = Css.ColorClass(color); if (c != null) l.AddCssClass(c);
            if (bold) l.AddCssClass("bold");
            l.SetMarginBottom(8);
            return l;
        }
        internal static Label NoWrap(Label l, int maxChars = 40) { l.SetWrap(false); l.SetEllipsize(Pango.EllipsizeMode.End); l.SetMaxWidthChars(maxChars); return l; }
        internal static Widget Glyph(string glyph, double size = 20, string color = "#64738A")
        {
            string name = Icons.Name(glyph);
            if (name == null) { var l = Label.New(glyph ?? ""); l.AddCssClass("fs-" + (int)Math.Round(size)); l.Css(Css.ColorClass(color)); l.SetValign(Gtk.Align.Center); return l; }
            var image = Image.NewFromIconName(name);
            image.SetPixelSize((int)Math.Round(size));
            image.Css(Css.ColorClass(color));
            image.SetValign(Gtk.Align.Center); image.SetHalign(Gtk.Align.Center);
            return image;
        }
        // Content with a flat header bar that carries the window controls.
        internal static Widget WithHeader(Widget content, string title = null)
        {
            var header = Adw.HeaderBar.New(); header.SetShowTitle(title != null); if (title != null) header.SetTitleWidget(Adw.WindowTitle.New(title, ""));
            header.AddCssClass("flat");
            var view = Adw.ToolbarView.New(); view.AddTopBar(header); view.SetContent(content);
            return view;
        }
        internal static Box VBox(int spacing = 0) { return Box.New(Orientation.Vertical, spacing); }
        internal static Box HBox(int spacing = 0) { return Box.New(Orientation.Horizontal, spacing); }
        internal static Widget Spacer(int height) { var b = VBox(); b.SetSizeRequest(-1, height); return b; }
        internal static Widget Hairline(int left = 0, int top = 0, int right = 0, int bottom = 0) { var b = VBox(); b.AddCssClass("hairline"); return b.Margin(left, top, right, bottom); }
        // libadwaita card: rounded surface on the window background.
        internal static Box Card(Widget child, double padding = 24)
        {
            var card = VBox(); card.AddCssClass("card"); card.AddCssClass("pad-" + NearestPad(padding));
            card.Append(child); card.SetMarginBottom(16);
            return card;
        }
        private static int NearestPad(double p) { return new[] { 0, 4, 8, 10, 12, 14, 16, 18, 20, 24, 28 }.OrderBy(v => Math.Abs(v - p)).First(); }
        // Wrapping row of buttons/links (WPF WrapPanel).
        internal static FlowBox WrapPanel(int spacing = 8)
        {
            var f = FlowBox.New(); f.SetSelectionMode(SelectionMode.None); f.SetHomogeneous(false); f.SetMaxChildrenPerLine(30);
            f.SetColumnSpacing((uint)spacing); f.SetRowSpacing((uint)spacing); f.SetHalign(Gtk.Align.Start); f.SetValign(Gtk.Align.Center);
            return f;
        }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Grid, int[]> ColumnSpans = new System.Runtime.CompilerServices.ConditionalWeakTable<Grid, int[]>();
        // Two proportional columns; GTK has no star sizing, so homogeneous grid spans give the ratio.
        internal static Grid Columns(double first = 1, double second = 1)
        {
            var g = Grid.New(); g.SetColumnHomogeneous(true); g.SetColumnSpacing(24); g.SetHexpand(true);
            int a = (int)Math.Max(1, Math.Round(first * 10)), b = (int)Math.Max(1, Math.Round(second * 10));
            ColumnSpans.AddOrUpdate(g, new[] { a, b });
            return g;
        }
        internal static Box Column(Grid grid, int column)
        {
            int[] spans; if (!ColumnSpans.TryGetValue(grid, out spans)) spans = new[] { 1, 1 }; int a = spans[0], b = spans[1];
            var panel = VBox(); panel.SetValign(Gtk.Align.Start); panel.SetHexpand(true);
            grid.Attach(panel, column == 0 ? 0 : a, 0, column == 0 ? a : b, 1);
            return panel;
        }
        // Places any widget in a proportional column slot (for code that built Grid.SetColumn layouts).
        internal static void PlaceInColumn(Grid grid, Widget child, int column)
        {
            int[] spans; if (!ColumnSpans.TryGetValue(grid, out spans)) spans = new[] { 1, 1 }; int a = spans[0], b = spans[1];
            grid.Attach(child, column == 0 ? 0 : a, 0, column == 0 ? a : b, 1);
        }
        // Label on the left, widget(s) on the right (WPF Grid with a star and auto columns).
        internal static Box Split(Widget left, params Widget[] right)
        {
            var row = HBox(12); left.SetHexpand(true); row.Append(left);
            foreach (Widget w in right) if (w != null) { w.SetValign(Gtk.Align.Center); row.Append(w); }
            return row;
        }
        internal Button Button(string title, Func<Task> action, bool primary = false)
        {
            var b = Gtk.Button.NewWithLabel(title);
            if (primary) b.AddCssClass("suggested-action");
            b.OnClicked += async (s, e) => { await Run(action, "Working…"); };
            return b;
        }
        internal Button LocalButton(string title, System.Action action)
        { var b = Gtk.Button.NewWithLabel(title); b.OnClicked += (s, e) => { if (!busy) action(); }; return b; }
        internal static void Quiet(Button b) { b.AddCssClass("flat"); b.AddCssClass("c-accent"); }
        internal static void Accent(Button b) { b.AddCssClass("suggested-action"); }
        internal static string ButtonText(Button b) { return b.GetLabel() ?? ""; }
        // Replaces a button's label with arbitrary content (icon rows, etc.).
        internal static void SetContent(Button b, Widget content) { b.SetChild(content); }
        internal static Widget Picture(byte[] png, int width, int height)
        {
            using (var bytes = GLib.Bytes.New(png))
            {
                var texture = Gdk.Texture.NewFromBytes(bytes);
                var p = Gtk.Picture.NewForPaintable(texture);
                p.SetContentFit(ContentFit.Contain); p.SetSizeRequest(width, height); p.SetCanShrink(true);
                return p;
            }
        }
    }
}
