import Foundation

// Translated from Availability.cs. The file is named FeatureAvailability.swift because UI/Availability.swift
// already exists in this module and SwiftPM refuses two sources with the same file name.

/// Remembers, per network, which pages eero refused to load, so menus can warn before you open them again.
/// Stored in ~/Library/Application Support/eero Desktop/availability.json; a page that loads successfully clears its entry.
enum FeatureAvailability {
    static var storePath = (AppPaths.dataDirectory as NSString).appendingPathComponent("availability.json")
    private static var entries: [String: String]?

    /// Only answers that say "not for you / not here" are remembered; other failures (offline, server errors) are not.
    static func reasonFor(_ code: Int) -> String? {
        if code == 402 { return "Needs a plan upgrade" }
        if code == 401 || code == 403 { return "Not available on your account" }
        if code == 404 { return "Not available on this network" }
        return nil
    }
    private static func key(_ network: String?, _ route: String) -> String { (network ?? "account") + "|" + route }
    private static func load() -> [String: String] {
        if let e = entries { return e }
        var loaded: [String: String] = [:]
        if FileManager.default.fileExists(atPath: storePath), let text = try? String(contentsOfFile: storePath, encoding: .utf8),
           let map = (try? J.parse(text)) as? JObject {
            for (k, v) in map { let value = J.s(v); if !value.isEmpty { loaded[k] = value } }
        }
        entries = loaded
        return loaded
    }
    private static func save() {
        guard let e = entries else { return }
        do {
            try FileManager.default.createDirectory(atPath: (storePath as NSString).deletingLastPathComponent, withIntermediateDirectories: true)
            var map: [String: Any] = [:]; for (k, v) in e { map[k] = v }
            try Files.writeText(J.stringify(map), storePath)
        } catch {}
    }
    static func get(_ network: String?, _ route: String) -> String? { load()[key(network, route)] }
    static func mark(_ network: String?, _ route: String, _ reason: String?) {
        guard let reason = reason, !reason.isEmpty else { return }
        let k = key(network, route)
        if load()[k] == reason { return }
        entries![k] = reason; save()
    }
    static func clear(_ network: String?, _ route: String) {
        _ = load()
        if entries!.removeValue(forKey: key(network, route)) != nil { save() }
    }
    static func reset() { entries = nil }
}
