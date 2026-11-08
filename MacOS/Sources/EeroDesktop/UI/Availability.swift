import Foundation

extension MainWindow {
    /// Pages that rely on routing; eero turns them off in Bridge and IP Passthrough modes.
    static let routingOnly: Set<String> = ["Home/Profiles", "Settings/Advanced networking/Reservations & port forwarding"]
    static let businessOnly: Set<String> = ["Settings/Business guest wifi", "Settings/Business guest wifi/eero Insight", "Settings/Account settings/Subscriptions/Business billing"]
    /// Pages the official app and eero's own text describe as part of eero Plus.
    static let plusOnly: Set<String> = ["Activity/Security activity", "Settings/Advanced networking/Dynamic DNS", "Home/Security & privacy/Identity protection", "Settings/Account settings/Subscriptions/Partner benefits", "Home/Security & privacy/VPN"]
    static let noPlanStates: Set<String> = ["inactive", "none", "expired", "canceled", "cancelled", "not_subscribed", "unsubscribed", "free", "lapsed"]

    /// What the app can tell from data it already has. Conservative: no badge unless eero's data clearly says so.
    static func ruleBadge(_ path: String?, _ network: Any?) -> String? {
        guard let path = path, !path.isEmpty, network != nil else { return nil }
        if routingOnly.contains(path) && ApkContracts.isNotRouting(network) { return "Not in bridge mode" }
        let type = J.first(network, "network_customer_type", "network_type").lowercased()
        if businessOnly.contains(path) && type.contains("resid") { return "Business networks only" }
        let premium = J.get(network, "premium_status")
        let plan = ((premium as? String) ?? J.first(premium, "status", "state")).lowercased()
        if plusOnly.contains(path) && noPlanStates.contains(plan) { return "Needs eero Plus" }
        return nil
    }
    var availabilityKey: String { (networkUrl ?? "").isEmpty ? "account" : networkId }
    /// The badge for a page, and a sentence explaining where it came from.
    func availabilityBadge(_ path: String) -> (String?, String?) {
        if let learned = preview ? nil : FeatureAvailability.get(availabilityKey, path) {
            return (learned, "eero said no the last time you opened this. Open it to check again; the note clears if it works.")
        }
        let rule = MainWindow.ruleBadge(path, network)
        if rule == "Not in bridge mode" { return (rule, "Your eero is in Bridge or IP Passthrough mode, so another router handles this. Change it under DHCP & NAT.") }
        if rule == "Business networks only" { return (rule, "This network is set up as a home network.") }
        if rule == "Needs eero Plus" { return (rule, "eero reports that this network doesn't have an active eero Plus plan.") }
        return (rule, nil)
    }
    /// Amber note under a page title when the page probably won't work.
    func availabilityNotice() {
        let (badge, why) = availabilityBadge(currentPage)
        guard let b = badge else { return }
        page.append(.notice(icon: "\u{E7BA}", text: b + ". " + (why ?? ""), tone: .warn))
    }
}
