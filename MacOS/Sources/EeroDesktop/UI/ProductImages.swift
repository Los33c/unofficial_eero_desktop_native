import AppKit
import Foundation
import SwiftUI

enum ProductImages {
    /// Prefixes cover regional/channel variants documented by eero. Match the
    /// hardware code first; a display label must never override a known model.
    static let codes: [(String, String)] = [
        ("A01", "eero-gen1"), ("B01", "eero-pro"), ("D01", "eero-beacon"), ("J01", "eero-gen2"),
        ("K01", "eero-pro-6"), ("N01", "eero-6"), ("Q01", "eero-6-extender"), ("R01", "eero-6-plus"),
        ("S01", "eero-pro-6e"), ("T01", "eero-poe-6"), ("U01", "eero-poe-gateway"), ("V01", "eero-max-7"),
        ("PA1", "eero-7"), ("ME1", "eero-pro-7"), ("SN1", "eero-outdoor-7"), ("NV01", "eero-poe-7"),
        ("RG4", "eero-signal-4g"), ("RG5", "eero-signal-5g"), ("FH01", "eero-telephone-adapter")
    ]
    static let names: [String: String] = [
        "eeromax7": "eero-max-7", "eeropro7": "eero-pro-7", "eero7": "eero-7", "eerooutdoor7": "eero-outdoor-7",
        "eeropoe7": "eero-poe-7", "eeropoe6": "eero-poe-6", "eeropoegateway": "eero-poe-gateway",
        "eeropro6e": "eero-pro-6e", "eeropro6": "eero-pro-6", "eero6+": "eero-6-plus", "eero6plus": "eero-6-plus",
        "eero6+extender": "eero-6-plus-extender", "eero6plusextender": "eero-6-plus-extender",
        "eero6extender": "eero-6-extender", "eero6": "eero-6", "eerobeacon": "eero-beacon",
        "eeropro": "eero-pro", "eeropro2ndgen": "eero-pro", "eero1stgen": "eero-gen1", "eero1stgeneration": "eero-gen1",
        "eero2ndgen": "eero-gen2", "eero2ndgeneration": "eero-gen2", "eerosignal4glte": "eero-signal-4g",
        "eerosignal4g": "eero-signal-4g", "eerosignal5g": "eero-signal-5g",
        "eerotelephoneadapter": "eero-telephone-adapter", "eerofh01": "eero-telephone-adapter"
    ]
    private static var cache: [String: NSImage] = [:]
    static func resolve(_ model: String?, _ code: String?) -> String? {
        let normalized = Regex.replace(code ?? "", "[^a-zA-Z0-9]", "").uppercased()
        for (key, value) in codes where normalized.hasPrefix(key) && normalized.count >= key.count + (key.count == 4 ? 0 : 1) { return value }
        let name = Regex.replace(model ?? "", "[\\s_()\\-]", "").lowercased()
        if let result = names[name] { return result }
        // Some API versions put the hardware number in the model field.
        let modelCode = Regex.replace(model ?? "", "[^a-zA-Z0-9]", "").uppercased()
        for (key, value) in codes where modelCode.hasPrefix(key) && modelCode.count >= key.count + (key.count == 4 ? 0 : 1) && Regex.isMatch(modelCode, "^[A-Z]{1,2}[0-9][A-Z0-9]{1,6}$") { return value }
        return nil
    }
    static func resolve(_ node: Any?) -> String? { resolve(J.first(node, "model", "model_name", "product_name"), J.first(node, "model_number", "model_code", "hardware_model")) }
    static func url(_ id: String) -> URL? { Resources.url("products/" + id + ".png") }
    static func load(_ id: String?) -> NSImage? {
        guard let id = id, !id.isEmpty else { return nil }
        if let image = cache[id] { return image }
        guard let u = url(id), let image = NSImage(contentsOf: u) else { return nil }
        cache[id] = image; return image
    }
    static var ids: [String] {
        guard let dir = Resources.url("products"), let files = try? FileManager.default.contentsOfDirectory(atPath: dir.path) else { return [] }
        return files.filter { $0.hasSuffix(".png") }.map { String($0.dropLast(4)) }.sorted()
    }
}

/// Bundled resources: Contents/Resources in the .app, or MacOS/Resources next to the package during development.
enum Resources {
    static func url(_ relative: String) -> URL? {
        var candidates: [URL] = []
        if let r = Bundle.main.resourceURL { candidates.append(r.appendingPathComponent(relative)) }
        let exe = URL(fileURLWithPath: CommandLine.arguments[0]).resolvingSymlinksInPath().deletingLastPathComponent()
        var dir = exe
        for _ in 0..<6 { candidates.append(dir.appendingPathComponent("Resources").appendingPathComponent(relative)); dir = dir.deletingLastPathComponent() }
        return candidates.first { FileManager.default.fileExists(atPath: $0.path) }
    }
}

extension MainWindow {
    func hardwareImage(_ node: Any?, _ size: CGFloat = 54) -> Block {
        guard let image = ProductImages.load(ProductImages.resolve(node)) else { return glyph("\u{E968}", size * 0.5) }
        let model = J.first(node, "model", "model_name", "product_name")
        return .view(Image(nsImage: image).resizable().interpolation(.high).aspectRatio(contentMode: .fit).frame(width: size, height: size).help(model).accessibilityLabel("Product image: " + model))
    }
    static func profileSummary(_ profile: Any?) -> String {
        let count = J.get(profile, "devices") != nil ? "\(J.list(J.get(profile, "devices")).count) devices · " : J.get(profile, "device_count") != nil ? J.s(profile, "device_count") + " devices · " : ""
        return count + (J.b(profile, "paused") ? "Paused" : "Internet enabled")
    }
    func compactNode(_ node: Any?) -> Block {
        let online = MainWindow.isOnline(node); let count = J.s(node, "connected_clients_count")
        let words: [Block] = [text(J.first(node, "location", "model"), 14, "#17263C", true), text((J.b(node, "gateway") ? "Gateway · " : "") + J.s(node, "model"), 12, "#64738A"),
                              text((online ? "● Online" : "○ " + (J.s(node, "status").nullIfEmpty ?? "Unknown")) + (count.isEmpty ? "" : " · " + count + " devices"), 11, online ? "#0D7959" : "#64738A")]
        let actions: [ButtonSpec] = [
            quiet(button("View details", { [unowned self] in self.selectedNode = self.preview ? node : try await self.read(J.s(node, "url")); self.currentPage = "Home/eeros/Details"; self.renderPage() })),
            quiet(button("Rename", { [unowned self] in
                guard let name = await self.prompt("eero location", J.s(node, "location")) else { return }
                try MainWindow.validateName(name); try await self.change("PUT", J.s(node, "url"), J.obj("location", name.trim()), true, "eero location updated.")
            }))]
        return card([.split(left: [.view(HStack(spacing: 12) { BlockView(block: hardwareImage(node, 52)); BlocksView(blocks: words) })], right: [.buttons(actions)])], 12)
    }
    func productGalleryPage() {
        sub("Bundled manufacturer imagery · matched by hardware number or model name.")
        let tiles: [Block] = ProductImages.ids.map { id in card([ProductImages.load(id).map { .image($0, width: 110, height: 90, tooltip: id) } ?? .spacer(90), text(id, 12)], 16) }
        page.append(.view(LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 12), count: 4), spacing: 12) { ForEach(Array(tiles.enumerated()), id: \.offset) { BlockView(block: $0.element) } }))
    }
}
