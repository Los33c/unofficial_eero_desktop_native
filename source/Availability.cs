using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EeroDesktop
{
    // Remembers, per network, which pages eero refused to load, so menus can warn before you open them again.
    // Stored in %LOCALAPPDATA%\EeroDesktop\availability.json; a page that loads successfully clears its entry.
    public static class FeatureAvailability
    {
        internal static string StorePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EeroDesktop", "availability.json");
        private static Dictionary<string, string> entries;

        // Only answers that say "not for you / not here" are remembered; other failures (offline, server errors) are not.
        internal static string ReasonFor(int code)
        {
            if (code == 402) return "Needs a plan upgrade";
            if (code == 401 || code == 403) return "Not available on your account";
            if (code == 404) return "Not available on this network";
            return null;
        }
        private static string Key(string network, string route) { return (network ?? "account") + "|" + route; }
        private static void Load()
        {
            if (entries != null) return;
            entries = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(StorePath)) return;
                var map = J.Parse(File.ReadAllText(StorePath)) as IDictionary<string, object>;
                if (map != null) foreach (var pair in map) { string value = J.S(pair.Value); if (value.Length > 0) entries[pair.Key] = value; }
            }
            catch (Exception) { entries = new Dictionary<string, string>(); }
        }
        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath));
                File.WriteAllText(StorePath, J.Stringify(entries.ToDictionary(p => p.Key, p => (object)p.Value)));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        public static string Get(string network, string route) { Load(); string value; return entries.TryGetValue(Key(network, route), out value) ? value : null; }
        public static void Mark(string network, string route, string reason)
        {
            if (String.IsNullOrEmpty(reason)) return;
            Load(); string key = Key(network, route); string existing;
            if (entries.TryGetValue(key, out existing) && existing == reason) return;
            entries[key] = reason; Save();
        }
        public static void Clear(string network, string route) { Load(); if (entries.Remove(Key(network, route))) Save(); }
        internal static void Reset() { entries = null; }
    }

    public sealed partial class MainWindow
    {
        // Pages that rely on routing; eero turns them off in Bridge and IP Passthrough modes.
        private static readonly HashSet<string> RoutingOnly = new HashSet<string> { "Home/Profiles", "Settings/Advanced networking/Reservations & port forwarding" };
        private static readonly HashSet<string> BusinessOnly = new HashSet<string> { "Settings/Business guest wifi", "Settings/Business guest wifi/eero Insight", "Settings/Account settings/Subscriptions/Business billing" };
        // Pages the official app and eero's own text describe as part of eero Plus.
        private static readonly HashSet<string> PlusOnly = new HashSet<string> { "Activity/Security activity", "Settings/Advanced networking/Dynamic DNS", "Home/Security & privacy/Identity protection", "Settings/Account settings/Subscriptions/Partner benefits", "Home/Security & privacy/VPN" };
        private static readonly HashSet<string> NoPlanStates = new HashSet<string> { "inactive", "none", "expired", "canceled", "cancelled", "not_subscribed", "unsubscribed", "free", "lapsed" };

        // What the app can tell from data it already has. Conservative: no badge unless eero's data clearly says so.
        internal static string RuleBadge(string path, object network)
        {
            if (network == null || String.IsNullOrEmpty(path)) return null;
            if (RoutingOnly.Contains(path) && ApkContracts.IsNotRouting(network)) return "Not in bridge mode";
            string type = J.First(network, "network_customer_type", "network_type").ToLowerInvariant();
            if (BusinessOnly.Contains(path) && type.Contains("resid")) return "Business networks only";
            object premium = J.Get(network, "premium_status");
            string plan = (premium is string ? (string)premium : J.First(premium, "status", "state")).ToLowerInvariant();
            if (PlusOnly.Contains(path) && NoPlanStates.Contains(plan)) return "Needs eero Plus";
            return null;
        }
        private string AvailabilityKey { get { return String.IsNullOrEmpty(networkUrl) ? "account" : NetworkId; } }
        // The badge for a page, and a sentence explaining where it came from.
        private string AvailabilityBadge(string path, out string why)
        {
            why = null;
            string learned = preview ? null : FeatureAvailability.Get(AvailabilityKey, path);
            if (learned != null) { why = "eero said no the last time you opened this. Open it to check again; the note clears if it works."; return learned; }
            string rule = RuleBadge(path, network);
            if (rule == "Not in bridge mode") why = "Your eero is in Bridge or IP Passthrough mode, so another router handles this. Change it under DHCP & NAT.";
            else if (rule == "Business networks only") why = "This network is set up as a home network.";
            else if (rule == "Needs eero Plus") why = "eero reports that this network doesn't have an active eero Plus plan.";
            return rule;
        }
        private string AvailabilityBadge(string path) { string why; return AvailabilityBadge(path, out why); }

        // Amber note under a page title when the page probably won't work.
        private void AvailabilityNotice()
        {
            string why; string badge = AvailabilityBadge(currentPage, out why);
            if (badge == null) return;
            var row = new System.Windows.Controls.Grid { Margin = new System.Windows.Thickness(0, 0, 0, 12) };
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = System.Windows.GridLength.Auto }); row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
            var icon = Glyph("", 14, "#926410"); icon.Margin = new System.Windows.Thickness(0, 1, 8, 0); icon.VerticalAlignment = System.Windows.VerticalAlignment.Top; row.Children.Add(icon);
            var text = Text(badge + ". " + why, 13, "#926410", true); text.Margin = new System.Windows.Thickness(0); System.Windows.Controls.Grid.SetColumn(text, 1); row.Children.Add(text);
            page.Children.Add(row);
        }
    }
}
