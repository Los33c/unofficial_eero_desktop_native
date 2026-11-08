using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace EeroDesktop
{
    // Setting rows keep each value next to the control that changes it.
    public sealed partial class MainWindow
    {
        private const string Hidden = "••••••••";

        private static void CompactAction(Button b)
        { b.Padding = new Thickness(14, 5, 14, 5); b.MinHeight = 30; b.Margin = new Thickness(8, 0, 0, 0); b.VerticalAlignment = VerticalAlignment.Center; }
        private static void Danger(Button b) { b.Foreground = Brush("#B5333D"); b.BorderBrush = Brush("#B5333D"); }

        private Button RowAction(string label, Action action, bool danger = false)
        { var b = LocalButton(label, action); CompactAction(b); if (danger) Danger(b); return b; }
        private Button RowActionAsync(string label, Func<Task> action, bool danger = false)
        { var b = Button(label, action); CompactAction(b); if (danger) Danger(b); return b; }

        // Title and help on the left, the current value, then the buttons that change it.
        private Grid SettingRow(string title, string description, string value, params Button[] actions)
        {
            var row = new Grid { MinHeight = 60, Margin = new Thickness(14, 10, 14, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var label = Text(title, 14, "#17263C", true); label.Margin = new Thickness(0); words.Children.Add(label);
            if (!String.IsNullOrEmpty(description)) { var sub = Text(description, 12, "#64738A"); sub.Margin = new Thickness(0, 3, 16, 0); words.Children.Add(sub); }
            row.Children.Add(words);
            if (!String.IsNullOrEmpty(value))
            {
                var shown = Text(value, 13, "#17263C"); shown.Margin = new Thickness(16, 0, 4, 0); shown.VerticalAlignment = VerticalAlignment.Center; shown.TextWrapping = TextWrapping.NoWrap; shown.TextTrimming = TextTrimming.CharacterEllipsis; shown.MaxWidth = 320; shown.ToolTip = value;
                Grid.SetColumn(shown, 1); row.Children.Add(shown);
            }
            if (actions != null && actions.Length > 0)
            {
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                foreach (Button b in actions.Where(b => b != null)) { AutomationProperties.SetName(b, J.S(b.Content) + " " + title); buttons.Children.Add(b); }
                Grid.SetColumn(buttons, 2); row.Children.Add(buttons);
            }
            return row;
        }
        private Grid InfoRow(string title, string value, string description = null) { return SettingRow(title, description, value); }

        // A titled card of rows separated by hairlines.
        private void Group(string heading, params UIElement[] rows)
        {
            var items = rows.Where(r => r != null).ToList();
            if (items.Count == 0) return;
            if (heading != null) AddGroupHeading(page, heading);
            page.Children.Add(RowGroup(items, 14));
        }

        // Raw values eero returned, collapsed until the user asks for them.
        private void TechnicalDetails(object data)
        {
            if (data == null) return;
            var p = new StackPanel(); Details(p, data, 0);
            if (p.Children.Count == 0) return;
            var card = Card(p, 18); card.Visibility = Visibility.Collapsed;
            Button toggle = null;
            toggle = LocalButton("Show technical details", delegate
            {
                bool show = card.Visibility != Visibility.Visible;
                card.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                toggle.Content = show ? "Hide technical details" : "Show technical details";
            });
            toggle.Style = (Style)FindResource("QuietButton"); toggle.Margin = new Thickness(-8, 0, 0, 8);
            page.Children.Add(toggle); page.Children.Add(card);
        }

        private static string OnOff(bool value) { return value ? "On" : "Off"; }

        private static readonly HashSet<string> Acronyms = new HashSet<string> { "DHCP", "IPV4", "IPV6", "VLAN", "UPNP", "SSID", "DDNS", "IEEE", "HTTP", "HTTPS", "MDNS", "SNMP" };

        // Plain-language text for values eero reports as API codes.
        internal static string Friendly(string value)
        {
            if (String.IsNullOrEmpty(value)) return value;
            switch (value)
            {
                case "nat": return "Automatic (NAT)";
                case "bridged": case "bridge": return "Bridge";
                case "ipPassthrough": return "IP Passthrough";
                case "dhcp": return "Automatic (DHCP)";
                case "static": return "Static IP";
                case "pppoe": return "PPPoE";
                case "automatic": return "Automatic";
                case "custom": return "Custom";
                case "up_to_date": return "Up to date";
                case "update_available": return "Update available";
                case "SUCCESS": case "success": return "Done";
                case "FAILED": case "failed": return "Failed";
                case "connected": return "Connected";
                case "disconnected": return "Disconnected";
                case "completed": return "Completed";
                case "none": return "None";
                case "admin": return "Administrator";
                case "green": return "Healthy";
                case "red": return "Needs attention";
                case "STATIC_IP": return "Static IP";
                case "PPPOE": return "PPPoE";
                case "WPA2_WPA3": return "WPA2 + WPA3";
                case "MULTI_WAN_AUTOMATIC": return "Automatic failover";
                case "MULTI_WAN_STRICT": return "Strict";
                case "MULTI_WAN_LEGACY": return "Legacy";
                case "PublicLAN": return "Public addresses on devices";
                case "NATWithPortFwd": return "Port forwarding (NAT)";
            }
            if (Regex.IsMatch(value, "^[a-z]+(_[a-z0-9]+)+$") || Regex.IsMatch(value, "^[A-Z][A-Z0-9]*(_[A-Z0-9]+)+$")) return Sentence(value.Split('_'));
            if (Regex.IsMatch(value, "^[A-Z]{4,}$") && !Acronyms.Contains(value)) return value[0] + value.Substring(1).ToLowerInvariant();
            return value;
        }
        private static string Sentence(IEnumerable<string> parts)
        {
            var words = parts.Where(w => w.Length > 0).Select(w => AcronymWord(w.ToLowerInvariant()) ?? w.ToLowerInvariant()).ToArray();
            if (words.Length == 0) return "";
            words[0] = Char.ToUpperInvariant(words[0][0]) + words[0].Substring(1);
            return String.Join(" ", words);
        }
        private static readonly Dictionary<string, string> AcronymMap = new Dictionary<string, string> {
            { "ip", "IP" }, { "ipv4", "IPv4" }, { "ipv6", "IPv6" }, { "mac", "MAC" }, { "wan", "WAN" }, { "lan", "LAN" }, { "dhcp", "DHCP" }, { "dns", "DNS" }, { "ssid", "SSID" }, { "vpn", "VPN" }, { "poe", "PoE" },
            { "usb", "USB" }, { "wpa", "WPA" }, { "wpa2", "WPA2" }, { "wpa3", "WPA3" }, { "nat", "NAT" }, { "upnp", "UPnP" }, { "mlo", "MLO" }, { "sfp", "SFP" }, { "lte", "LTE" }, { "id", "ID" }, { "os", "OS" },
            { "isp", "ISP" }, { "url", "URL" }, { "led", "LED" }, { "ghz", "GHz" }, { "mhz", "MHz" }, { "mbps", "Mbps" }, { "gbps", "Gbps" }, { "dbm", "dBm" }, { "ap", "AP" }, { "eero", "eero" }, { "eeros", "eeros" }, { "tcp", "TCP" }, { "udp", "UDP" }, { "sip", "SIP" }, { "zts", "ZTS" }, { "lts", "LTS" } };
        private static string AcronymWord(string word) { string value; return AcronymMap.TryGetValue(word, out value) ? value : null; }

        // Fields whose true/false answers read better as Yes/No than On/Off.
        private static bool YesNoKey(string key)
        {
            string k = (key ?? "").ToLowerInvariant();
            return k.StartsWith("is_") || k.StartsWith("has_") || k.StartsWith("can_") || k.EndsWith("_available") || k.EndsWith("_supported") || k.EndsWith("_required")
                || new[] { "connected", "online", "available", "registered", "joined", "verified", "wireless", "gateway", "blocked", "paused", "ok", "success", "succeeded", "heartbeat_ok" }.Contains(k);
        }
        internal static string Bytes(double bytes)
        {
            string[] units = { "bytes", "KB", "MB", "GB", "TB" }; int unit = 0;
            while (bytes >= 1024 && unit < units.Length - 1) { bytes /= 1024; unit++; }
            return (unit == 0 ? bytes.ToString("0", System.Globalization.CultureInfo.CurrentCulture) : bytes.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture)) + " " + units[unit];
        }
        internal static string SignalLabel(double dbm)
        { return (dbm >= -60 ? "Strong" : dbm >= -70 ? "Good" : dbm >= -80 ? "Weak" : "Very weak") + " (" + dbm.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " dBm)"; }

        // Turns one eero value into words, using its field name for context (speeds, sizes, dates, signal, codes).
        internal static string FriendlyValue(string key, object value)
        {
            if (value == null) return "";
            string k = (key ?? "").ToLowerInvariant();
            if (value is bool) { bool b = (bool)value; return YesNoKey(k) ? (b ? "Yes" : "No") : (b ? "On" : "Off"); }
            string s = J.S(value); if (s.Length == 0) return "";
            double number;
            if (!(value is string) && Double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number))
            {
                if (k.Contains("byte") || ((k == "download" || k == "upload" || k == "total" || k == "usage" || k.EndsWith("_usage") || k == "data_used") && Math.Abs(number) >= 1024)) return Bytes(number);
                if (k.EndsWith("_mbps") || k == "mbps") return number.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + " Mbps";
                if (k.EndsWith("_pct") || k.Contains("percent") || k.EndsWith("_percentage") || k == "utilization") return number.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + "%";
                if (k == "rssi" || k.EndsWith("_rssi") || k == "signal" || k == "signal_strength") return SignalLabel(number);
                if (k.EndsWith("_mhz") || k == "channel_width") return number.ToString("0", System.Globalization.CultureInfo.CurrentCulture) + " MHz";
                return s;
            }
            var speed = Regex.Match(s, "^P([0-9]+)$");
            if (speed.Success && k.Contains("speed")) { int megabits = Int32.Parse(speed.Groups[1].Value); return megabits >= 1000 ? (megabits / 1000.0).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + " Gbps" : megabits + " Mbps"; }
            DateTime when;
            if (Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}") && DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out when))
                return (when.Kind == DateTimeKind.Utc ? when.ToLocalTime() : when).ToString("MMM d, yyyy h:mm tt", System.Globalization.CultureInfo.CurrentCulture);
            if (Regex.IsMatch(s, @"^\d{4}-\d{2}-\d{2}$") && DateTime.TryParseExact(s, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out when))
                return when.ToString("MMM d, yyyy", System.Globalization.CultureInfo.CurrentCulture);
            return Friendly(s);
        }

        // Internal identifiers that mean nothing to people are left out of detail cards.
        internal static bool InternalKey(string key)
        {
            string k = key.ToLowerInvariant();
            return k == "id" || (k.EndsWith("_id") && k != "network_id") || k == "uuid" || k == "eero_id";
        }

        // Destructive actions are tinted red wherever a page builds them.
        private static readonly string[] DangerWords = { "Delete ", "Remove ", "Unlink ", "Regenerate ", "Revoke ", "Cancel all", "Cancel ownership", "Put network in holding" };
        internal static bool IsDangerLabel(string label) { return label != null && DangerWords.Any(w => label.StartsWith(w, StringComparison.Ordinal)); }
        private static void MarkDangerActions(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                var b = child as Button;
                if (b != null) { if (b.Style == null && IsDangerLabel(b.Content as string)) Danger(b); continue; }
                var d = child as DependencyObject; if (d != null) MarkDangerActions(d);
            }
        }

        private static string HourLabel(int hour) { return DateTime.Today.AddHours(hour).ToString("h:00 tt"); }
    }
}
