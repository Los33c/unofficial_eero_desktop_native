import CryptoKit
import Foundation

// StartupRegistration and IStartupStore live in UI/Desktop.swift (macOS LaunchAgent).

/// Remembers which network notifications were already announced, per account and network.
/// The file is sealed with ProtectedData (Keychain-backed AES-GCM, the macOS counterpart of DPAPI).
final class NotificationJournal {
    private let path: String
    private var order: [String] = []
    private var seen: [String: [String]] = [:]
    private static let entropy = Data("EeroDesktop.NotificationJournal.v1".utf8)

    init(_ file: String) {
        path = file
        do {
            guard FileManager.default.fileExists(atPath: file) else { return }
            let size = (try FileManager.default.attributesOfItem(atPath: file)[.size] as? NSNumber)?.intValue ?? 0
            if size > 16 * 1024 * 1024 { return }
            let clear = try ProtectedData.unprotect(try Data(contentsOf: URL(fileURLWithPath: file)), NotificationJournal.entropy)
            guard let text = String(data: clear, encoding: .utf8), let data = try J.parse(text) as? JObject else { return }
            for item in data.prefix(64) where item.key.utf16.count == 64 {
                var ids: [String] = []
                for x in J.list(item.value) { let id = J.s(x); if NotificationJournal.validId(id) && !ids.contains(id) { ids.append(id) } }
                set(item.key, Array(ids.prefix(256)))
            }
        } catch {
            order = []; seen = [:]
        }
    }
    private func set(_ key: String, _ ids: [String]) { if seen[key] == nil { order.append(key) }; seen[key] = ids }
    private static func validId(_ id: String) -> Bool {
        !id.isEmpty && id.utf16.count <= 256 && !id.unicodeScalars.contains { $0.properties.generalCategory == .control }
    }
    private static func scope(_ account: String, _ network: String) throws -> String {
        let digest = SHA256.hash(data: Data((account + "\n" + (try EeroApi.resource(network)).absoluteString).utf8))
        return digest.map { String(format: "%02X", $0) }.joined()
    }
    func observe(_ account: String, _ network: String, _ newestFirst: [Any]) throws -> [Any] {
        let scope = try NotificationJournal.scope(account, network)
        let seeded = seen[scope] != nil
        var ids = seen[scope] ?? []
        if !seeded { set(scope, ids) }
        var result: [Any] = []
        for item in newestFirst.prefix(50).reversed() {
            let id = J.first(item, "uuid", "id"); if !NotificationJournal.validId(id) || ids.contains(id) { continue }
            ids.append(id); if seeded { result.append(item) }
        }
        if ids.count > 256 { ids.removeFirst(ids.count - 256) }
        seen[scope] = ids
        while seen.count > 64, let oldest = order.first(where: { $0 != scope }) { seen.removeValue(forKey: oldest); order.removeAll { $0 == oldest } }
        return result
    }
    func save() throws {
        let map = JObject()
        for k in order { if let ids = seen[k] { map[k] = ids } }
        let encrypted = try ProtectedData.protect(Data(J.stringify(map).utf8), NotificationJournal.entropy)
        try FileManager.default.createDirectory(atPath: (path as NSString).deletingLastPathComponent, withIntermediateDirectories: true)
        try Files.writeAtomically(encrypted, path)
    }
}
