import Foundation
import SwiftUI

// Setting rows keep each value next to the control that changes it.
extension MainWindow {
    static let hidden = "••••••••"

    // MARK: Building blocks (Text, Card, Glyph, buttons)
    func text(_ s: String, _ size: CGFloat = 14, _ color: String = "#17263C", _ bold: Bool = false) -> Block {
        .text(TextSpec(text: s, size: size, tone: Tone.of(color), bold: bold))
    }
    func noWrap(_ s: String, _ size: CGFloat = 14, _ color: String = "#17263C", _ bold: Bool = false) -> Block {
        .text(TextSpec(text: s, size: size, tone: Tone.of(color), bold: bold, wraps: false))
    }
    func card(_ child: [Block], _ padding: CGFloat = 24) -> Block { .card(child, padding: padding) }
    func glyph(_ glyph: String, _ size: CGFloat = 20, _ color: String = "#64738A") -> Block { .glyph(glyph, size: size, tone: Tone.of(color)) }
    func columns(_ first: [Block], _ second: [Block], _ a: CGFloat = 1, _ b: CGFloat = 1) -> Block { .columns([first, second], ratios: [a, b]) }
    func button(_ title: String, _ action: @escaping () async throws -> Void, _ primary: Bool = false) -> ButtonSpec {
        ButtonSpec(title, kind: primary ? .accent : .normal, action: action)
    }
    func localButton(_ title: String, _ action: @escaping () throws -> Void) -> ButtonSpec {
        ButtonSpec(title, local: true, action: { try action() })
    }
    @discardableResult func quiet(_ b: ButtonSpec) -> ButtonSpec { b.kind = .quiet; return b }
    @discardableResult func accent(_ b: ButtonSpec) -> ButtonSpec { b.kind = .accent; return b }
    @discardableResult func danger(_ b: ButtonSpec) -> ButtonSpec { if b.kind != .accent { b.kind = .danger }; return b }
    func compactAction(_ b: ButtonSpec) {}

    func rowAction(_ label: String, _ action: @escaping () throws -> Void, _ isDanger: Bool = false) -> ButtonSpec {
        let b = localButton(label, action); if isDanger { danger(b) }; return b
    }
    func rowActionAsync(_ label: String, _ action: @escaping () async throws -> Void, _ isDanger: Bool = false) -> ButtonSpec {
        let b = button(label, action); if isDanger { danger(b) }; return b
    }

    /// Title and help on the left, the current value, then the buttons that change it.
    func settingRow(_ title: String, _ description: String?, _ value: String?, _ actions: ButtonSpec?...) -> Row {
        .setting(title: title, description: description, value: value, actions: actions.compactMap { $0 })
    }
    func settingRow(_ title: String, _ description: String?, _ value: String?, actions: [ButtonSpec]) -> Row {
        .setting(title: title, description: description, value: value, actions: actions)
    }
    func infoRow(_ title: String, _ value: String?, _ description: String? = nil) -> Row { settingRow(title, description, value) }

    /// A titled card of rows separated by hairlines.
    func group(_ heading: String?, _ rows: Row?...) { group(heading, rows: rows.compactMap { $0 }) }
    func group(_ heading: String?, rows: [Row]) {
        if rows.isEmpty { return }
        if let h = heading { addGroupHeading(h) }
        page.append(rowGroup(rows, 14))
    }

    /// Raw values eero returned, collapsed until the user asks for them.
    func technicalDetails(_ data: Any?) {
        guard data != nil else { return }
        let p = details(data, 0)
        if p.isEmpty { return }
        page.append(.expander(title: "Technical details", content: [card(p, 18)]))
    }

    static func onOff(_ value: Bool) -> String { value ? "On" : "Off" }

    static let acronyms: Set<String> = ["DHCP", "IPV4", "IPV6", "VLAN", "UPNP", "SSID", "DDNS", "IEEE", "HTTP", "HTTPS", "MDNS", "SNMP"]

    /// Plain-language text for values eero reports as API codes.
    static func friendly(_ value: String?) -> String {
        guard let value = value, !value.isEmpty else { return value ?? "" }
        switch value {
        case "nat": return "Automatic (NAT)"
        case "bridged", "bridge": return "Bridge"
        case "ipPassthrough": return "IP Passthrough"
        case "dhcp": return "Automatic (DHCP)"
        case "static": return "Static IP"
        case "pppoe": return "PPPoE"
        case "automatic": return "Automatic"
        case "custom": return "Custom"
        case "up_to_date": return "Up to date"
        case "update_available": return "Update available"
        case "SUCCESS", "success": return "Done"
        case "FAILED", "failed": return "Failed"
        case "connected": return "Connected"
        case "disconnected": return "Disconnected"
        case "completed": return "Completed"
        case "none": return "None"
        case "admin": return "Administrator"
        case "green": return "Healthy"
        case "red": return "Needs attention"
        case "STATIC_IP": return "Static IP"
        case "PPPOE": return "PPPoE"
        case "WPA2_WPA3": return "WPA2 + WPA3"
        case "MULTI_WAN_AUTOMATIC": return "Automatic failover"
        case "MULTI_WAN_STRICT": return "Strict"
        case "MULTI_WAN_LEGACY": return "Legacy"
        case "PublicLAN": return "Public addresses on devices"
        case "NATWithPortFwd": return "Port forwarding (NAT)"
        default: break
        }
        if Regex.isMatch(value, "^[a-z]+(_[a-z0-9]+)+$") || Regex.isMatch(value, "^[A-Z][A-Z0-9]*(_[A-Z0-9]+)+$") { return sentence(value.split("_")) }
        if Regex.isMatch(value, "^[A-Z]{4,}$") && !acronyms.contains(value) { return String(value.prefix(1)) + value.dropFirst().lowercased() }
        return value
    }
    static func sentence(_ parts: [String]) -> String {
        var words = parts.filter { !$0.isEmpty }.map { acronymWord($0.lowercased()) ?? $0.lowercased() }
        if words.isEmpty { return "" }
        words[0] = words[0].prefix(1).uppercased() + words[0].dropFirst()
        return words.joined(separator: " ")
    }
    static let acronymMap: [String: String] = [
        "ip": "IP", "ipv4": "IPv4", "ipv6": "IPv6", "mac": "MAC", "wan": "WAN", "lan": "LAN", "dhcp": "DHCP", "dns": "DNS", "ssid": "SSID", "vpn": "VPN", "poe": "PoE",
        "usb": "USB", "wpa": "WPA", "wpa2": "WPA2", "wpa3": "WPA3", "nat": "NAT", "upnp": "UPnP", "mlo": "MLO", "sfp": "SFP", "lte": "LTE", "id": "ID", "os": "OS",
        "isp": "ISP", "url": "URL", "led": "LED", "ghz": "GHz", "mhz": "MHz", "mbps": "Mbps", "gbps": "Gbps", "dbm": "dBm", "ap": "AP", "eero": "eero", "eeros": "eeros", "tcp": "TCP", "udp": "UDP", "sip": "SIP", "zts": "ZTS", "lts": "LTS"]
    static func acronymWord(_ word: String) -> String? { acronymMap[word] }

    /// Fields whose true/false answers read better as Yes/No than On/Off.
    static func yesNoKey(_ key: String?) -> Bool {
        let k = (key ?? "").lowercased()
        return k.hasPrefix("is_") || k.hasPrefix("has_") || k.hasPrefix("can_") || k.hasSuffix("_available") || k.hasSuffix("_supported") || k.hasSuffix("_required")
            || ["connected", "online", "available", "registered", "joined", "verified", "wireless", "gateway", "blocked", "paused", "ok", "success", "succeeded", "heartbeat_ok"].contains(k)
    }
    static func bytes(_ value: Double) -> String {
        let units = ["bytes", "KB", "MB", "GB", "TB"]; var unit = 0; var bytes = value
        while bytes >= 1024 && unit < units.count - 1 { bytes /= 1024; unit += 1 }
        return (unit == 0 ? Num.format(bytes, 0) : Num.format(bytes, 1)) + " " + units[unit]
    }
    static func signalLabel(_ dbm: Double) -> String {
        (dbm >= -60 ? "Strong" : dbm >= -70 ? "Good" : dbm >= -80 ? "Weak" : "Very weak") + " (" + Num.format(dbm, 0, locale: Num.invariant) + " dBm)"
    }

    /// Turns one eero value into words, using its field name for context (speeds, sizes, dates, signal, codes).
    static func friendlyValue(_ key: String?, _ value: Any?) -> String {
        guard let value = value, !(value is JNull) else { return "" }
        let k = (key ?? "").lowercased()
        if let b = value as? Bool { return yesNoKey(k) ? (b ? "Yes" : "No") : (b ? "On" : "Off") }
        let s = J.str(value); if s.isEmpty { return "" }
        if !(value is String), let number = Double(s) {
            if k.contains("byte") || ((k == "download" || k == "upload" || k == "total" || k == "usage" || k.hasSuffix("_usage") || k == "data_used") && abs(number) >= 1024) { return bytes(number) }
            if k.hasSuffix("_mbps") || k == "mbps" { return Num.format(number, 1) + " Mbps" }
            if k.hasSuffix("_pct") || k.contains("percent") || k.hasSuffix("_percentage") || k == "utilization" { return Num.format(number, 1) + "%" }
            if k == "rssi" || k.hasSuffix("_rssi") || k == "signal" || k == "signal_strength" { return signalLabel(number) }
            if k.hasSuffix("_mhz") || k == "channel_width" { return Num.format(number, 0) + " MHz" }
            return s
        }
        if let speed = Regex.match(s, "^P([0-9]+)$"), k.contains("speed"), let megabits = Int(speed[1]) {
            return megabits >= 1000 ? Num.format(Double(megabits) / 1000.0, 1) + " Gbps" : "\(megabits) Mbps"
        }
        if Regex.isMatch(s, "^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}"), let when = Dates.iso8601(s) { return Dates.format(when, "MMM d, yyyy h:mm a") }
        if Regex.isMatch(s, "^\\d{4}-\\d{2}-\\d{2}$") {
            let f = DateFormatter(); f.locale = Num.invariant; f.dateFormat = "yyyy-MM-dd"; f.timeZone = .current
            if let when = f.date(from: s) { return Dates.format(when, "MMM d, yyyy") }
        }
        return friendly(s)
    }

    /// Internal identifiers that mean nothing to people are left out of detail cards.
    static func internalKey(_ key: String) -> Bool {
        let k = key.lowercased()
        return k == "id" || (k.hasSuffix("_id") && k != "network_id") || k == "uuid" || k == "eero_id"
    }

    /// Destructive actions are tinted red wherever a page builds them.
    static let dangerWords = ["Delete ", "Remove ", "Unlink ", "Regenerate ", "Revoke ", "Cancel all", "Cancel ownership", "Put network in holding"]
    static func isDangerLabel(_ label: String?) -> Bool { guard let l = label else { return false }; return dangerWords.contains { l.hasPrefix($0) } }
    func markDangerActions() { MainWindow.markDanger(page) }
    private static func markDanger(_ blocks: [Block]) {
        for b in blocks {
            switch b {
            case .buttons(let specs): for s in specs where s.kind == .normal && isDangerLabel(s.title) { s.kind = .danger }
            case .rows(let rows): for r in rows { markDanger(r) }
            case .card(let c, _), .indent(let c, _), .padded(let c, _), .expander(_, let c): markDanger(c)
            case .columns(let cols, _): for c in cols { markDanger(c) }
            case .split(let l, let r): markDanger(l); markDanger(r)
            default: break
            }
        }
    }
    private static func markDanger(_ row: Row) {
        switch row {
        case .setting(_, _, _, let actions): for s in actions where s.kind == .normal && isDangerLabel(s.title) { s.kind = .danger }
        case .content(let c): markDanger(c)
        default: break
        }
    }

    static func hourLabel(_ hour: Int) -> String {
        let d = Calendar.current.date(bySettingHour: hour, minute: 0, second: 0, of: Date()) ?? Date()
        return Dates.format(d, "h:00 a")
    }
}
