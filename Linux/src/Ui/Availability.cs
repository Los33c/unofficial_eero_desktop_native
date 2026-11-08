using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gtk;

namespace EeroDesktop
{

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
            var icon = Glyph("", 14, "#926410"); icon.SetValign(Gtk.Align.Start); icon.SetMarginTop(2);
            var text = Text(badge + ". " + why, 13, "#926410", true); text.SetMarginBottom(0); text.SetHexpand(true);
            var row = HBox(8); row.Append(icon); row.Append(text); row.SetMarginBottom(12);
            page.Append(row);
        }
    }
}
