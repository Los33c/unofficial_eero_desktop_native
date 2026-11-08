import AppKit
import SwiftUI

// Page code builds a list of `Block`s with the same vocabulary as the Windows app
// (Text, Card, Group, SettingRow, ...); `BlockView` renders them with native SwiftUI controls.

/// Semantic colors; the Windows palette hex values map onto these.
enum Tone {
    case text, muted, accent, good, warn, danger
    static func of(_ hex: String) -> Tone {
        switch hex.uppercased() {
        case "#64738A", "#52647C", "#A3B2CC": return .muted
        case "#274CDB", "#345BEB", "#9BB4FF": return .accent
        case "#0D7959": return .good
        case "#926410": return .warn
        case "#B5333D": return .danger
        default: return .text
        }
    }
    var color: Color {
        switch self {
        case .text: return Color(nsColor: .labelColor)
        case .muted: return Color(nsColor: .secondaryLabelColor)
        case .accent: return Color.accentColor
        case .good: return Color(nsColor: .systemGreen)
        case .warn: return Color(nsColor: .systemOrange)
        case .danger: return Color(nsColor: .systemRed)
        }
    }
}

/// Segoe Fluent glyphs used by the shared page code, mapped to SF Symbols.
enum Glyphs {
    static let map: [String: String] = [
        "\u{E701}": "wifi", "\u{E707}": "mappin.and.ellipse", "\u{E710}": "plus", "\u{E713}": "gearshape", "\u{E716}": "person.2",
        "\u{E71B}": "link", "\u{E721}": "magnifyingglass", "\u{E72B}": "chevron.left", "\u{E72C}": "arrow.clockwise", "\u{E72E}": "lock.shield",
        "\u{E73E}": "checkmark", "\u{E76C}": "chevron.right", "\u{E772}": "desktopcomputer", "\u{E774}": "globe", "\u{E77B}": "person.crop.circle",
        "\u{E790}": "paintpalette", "\u{E7BA}": "exclamationmark.triangle", "\u{E7C1}": "flag", "\u{E80F}": "house", "\u{E839}": "cable.connector",
        "\u{E895}": "arrow.triangle.2.circlepath", "\u{E897}": "questionmark.circle", "\u{E8AB}": "arrow.left.arrow.right", "\u{E8D7}": "key",
        "\u{E8E0}": "hand.thumbsdown", "\u{E8E1}": "hand.thumbsup", "\u{E8F2}": "bubble.left.and.bubble.right", "\u{E8FA}": "person.badge.plus",
        "\u{E90F}": "wrench.and.screwdriver", "\u{E945}": "leaf", "\u{E946}": "info.circle", "\u{E950}": "wifi.router", "\u{E968}": "network",
        "\u{E9D9}": "chart.bar", "\u{EA18}": "checkmark.shield", "\u{EA8F}": "bell"
    ]
    static func symbol(_ glyph: String) -> String { map[glyph] ?? (glyph.contains(".") || glyph.allSatisfy({ $0.isLetter }) ? glyph : "circle") }
}

enum ButtonKind { case normal, accent, quiet, danger }

/// A button in page content. `run` buttons go through MainWindow.run (busy state + error notice).
final class ButtonSpec: Identifiable {
    let id = UUID()
    var title: String
    var kind: ButtonKind
    var icon: String?
    var help: String?
    var enabled = true
    let action: () async throws -> Void
    let local: Bool
    init(_ title: String, kind: ButtonKind = .normal, icon: String? = nil, help: String? = nil, local: Bool = false, action: @escaping () async throws -> Void) {
        self.title = title; self.kind = kind; self.icon = icon; self.help = help; self.local = local; self.action = action
    }
}

struct TextSpec {
    var text: String
    var size: CGFloat = 14
    var tone: Tone = .text
    var bold = false
    var wraps = true
    var selectable = false
    var light = false
}

indirect enum Row {
    /// Title, explanation, current value and the buttons that change it (SettingRow / InfoRow).
    case setting(title: String, description: String?, value: String?, actions: [ButtonSpec])
    /// Title, explanation and a switch that only changes after the save succeeds (SettingToggle).
    case toggle(title: String, description: String?, value: Bool, apply: (Bool) async throws -> Void)
    /// A tappable navigation row: icon, title, caption, optional value, chevron (ListRow / RouteRow).
    case link(icon: String, title: String, subtitle: String?, value: String?, badge: String?, tooltip: String?, tile: Bool, action: () -> Void)
    /// Arbitrary content inside a row.
    case content([Block])
}

indirect enum Block {
    case text(TextSpec)
    case spacer(CGFloat)
    case heading(String)
    case rows([Row])
    case card([Block], padding: CGFloat)
    case columns([[Block]], ratios: [CGFloat])
    /// Content on the left, controls on the right (WPF Grid with star + auto columns).
    case split(left: [Block], right: [Block])
    case buttons([ButtonSpec])
    case detailRow(label: String, value: String, flag: Bool)
    case indent([Block], CGFloat)
    case notice(icon: String, text: String, tone: Tone)
    case badge(String, online: Bool)
    case glyph(String, size: CGFloat, tone: Tone)
    case image(NSImage, width: CGFloat, height: CGFloat, tooltip: String?)
    case expander(title: String, content: [Block])
    case divider
    case padded([Block], EdgeInsets)
    case custom(AnyView)

    static func view<V: View>(_ v: V) -> Block { .custom(AnyView(v)) }
}

// MARK: - Rendering

struct BlocksView: View {
    let blocks: [Block]
    @EnvironmentObject var model: MainWindow
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            ForEach(Array(blocks.enumerated()), id: \.offset) { _, block in
                BlockView(block: block)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

struct BlockView: View {
    let block: Block
    @EnvironmentObject var model: MainWindow
    var body: some View {
        switch block {
        case .text(let t): TextView(spec: t).padding(.bottom, 8)
        case .spacer(let h): Color.clear.frame(height: h)
        case .heading(let h): Text(h).font(.system(size: 14, weight: .semibold)).padding(.leading, 4).padding(.top, 6).padding(.bottom, 8)
        case .rows(let rows): RowsView(rows: rows).padding(.bottom, 18)
        case .card(let content, let padding):
            BlocksView(blocks: content).padding(padding)
                .background(RoundedRectangle(cornerRadius: 10).fill(Color(nsColor: .controlBackgroundColor)))
                .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(Color(nsColor: .separatorColor), lineWidth: 0.5))
                .padding(.bottom, 16)
        case .columns(let cols, let ratios): ProportionalColumns(cols: cols, ratios: ratios)
        case .split(let left, let right):
            HStack(alignment: .center, spacing: 12) {
                BlocksView(blocks: left)
                HStack(spacing: 8) { ForEach(Array(right.enumerated()), id: \.offset) { _, b in BlockView(block: b) } }.fixedSize()
            }
        case .buttons(let specs): ButtonsRow(specs: specs).padding(.bottom, 8)
        case .detailRow(let label, let value, let flag): DetailRowView(label: label, value: value, flag: flag)
        case .indent(let content, let amount): BlocksView(blocks: content).padding(.leading, amount)
        case .notice(let icon, let text, let tone):
            HStack(alignment: .top, spacing: 8) {
                Image(systemName: Glyphs.symbol(icon)).foregroundColor(tone.color)
                Text(text).font(.system(size: 13, weight: .semibold)).foregroundColor(tone.color).fixedSize(horizontal: false, vertical: true)
            }.padding(.bottom, 12)
        case .badge(let text, let online): StatusBadgeView(text: text, online: online)
        case .glyph(let g, let size, let tone): Image(systemName: Glyphs.symbol(g)).font(.system(size: size * 0.8)).foregroundColor(tone.color).frame(width: size, height: size)
        case .image(let image, let w, let h, let tip): Image(nsImage: image).resizable().interpolation(.high).aspectRatio(contentMode: .fit).frame(width: w, height: h).help(tip ?? "")
        case .expander(let title, let content): ExpanderView(title: title, content: content).padding(.bottom, 8)
        case .divider: Divider().padding(.vertical, 16)
        case .padded(let content, let insets): BlocksView(blocks: content).padding(insets)
        case .custom(let v): v
        }
    }
}

/// Proportional columns using a Layout so the height follows the tallest column.
struct ProportionalColumns: View {
    let cols: [[Block]]; let ratios: [CGFloat]
    var body: some View {
        RatioLayout(ratios: ratios, spacing: 24) {
            ForEach(Array(cols.enumerated()), id: \.offset) { _, col in BlocksView(blocks: col) }
        }
    }
}
struct RatioLayout: Layout {
    let ratios: [CGFloat]; let spacing: CGFloat
    private func widths(_ total: CGFloat, _ n: Int) -> [CGFloat] {
        let sum = (0..<n).map { $0 < ratios.count ? ratios[$0] : 1 }.reduce(0, +)
        let usable = max(0, total - spacing * CGFloat(max(n - 1, 0)))
        return (0..<n).map { usable * ($0 < ratios.count ? ratios[$0] : 1) / max(sum, 0.0001) }
    }
    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let width = proposal.width ?? 800
        let w = widths(width, subviews.count)
        let height = subviews.enumerated().map { i, v in v.sizeThatFits(ProposedViewSize(width: w[i], height: nil)).height }.max() ?? 0
        return CGSize(width: width, height: height)
    }
    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        let w = widths(bounds.width, subviews.count); var x = bounds.minX
        for (i, v) in subviews.enumerated() { v.place(at: CGPoint(x: x, y: bounds.minY), anchor: .topLeading, proposal: ProposedViewSize(width: w[i], height: nil)); x += w[i] + spacing }
    }
}

struct TextView: View {
    let spec: TextSpec
    var body: some View {
        let t = Text(spec.text).font(.system(size: spec.size, weight: spec.bold ? .semibold : (spec.light ? .light : .regular))).foregroundColor(spec.tone.color)
        Group {
            if spec.wraps { t.fixedSize(horizontal: false, vertical: true) } else { t.lineLimit(1).truncationMode(.tail) }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .modifier(SelectableText(on: spec.selectable))
    }
}
struct SelectableText: ViewModifier {
    let on: Bool
    func body(content: Content) -> some View { if on { content.textSelection(.enabled) } else { content } }
}

struct StatusBadgeView: View {
    let text: String; let online: Bool
    var body: some View {
        Text((online ? "●  " : "○  ") + text).font(.system(size: 11, weight: .semibold))
            .foregroundColor(online ? Tone.good.color : Tone.muted.color)
            .padding(.horizontal, 9).padding(.vertical, 4)
            .background(RoundedRectangle(cornerRadius: 4).fill(online ? Color.green.opacity(0.14) : Color.secondary.opacity(0.12)))
            .fixedSize()
    }
}

struct DetailRowView: View {
    let label: String; let value: String; let flag: Bool
    var body: some View {
        VStack(spacing: 0) {
            HStack(alignment: .center, spacing: 12) {
                Text(label).font(.system(size: 13)).foregroundColor(Tone.muted.color).frame(width: 170, alignment: .leading).fixedSize(horizontal: false, vertical: true)
                if flag {
                    let on = value == "On"
                    Text((on ? "●  " : "○  ") + value).font(.system(size: 13)).foregroundColor(on ? Tone.good.color : Tone.muted.color)
                } else {
                    Text(value).font(.system(size: 13)).textSelection(.enabled).fixedSize(horizontal: false, vertical: true)
                }
                Spacer(minLength: 0)
            }.padding(.vertical, 7)
            Divider()
        }
    }
}

struct ExpanderView: View {
    let title: String; let content: [Block]
    @State private var open = false
    var body: some View {
        DisclosureGroup(isExpanded: $open) { BlocksView(blocks: content).padding(.top, 8) } label: { Text(title).font(.system(size: 13)) }
    }
}

struct ButtonsRow: View {
    let specs: [ButtonSpec]
    var body: some View {
        FlowLayout(spacing: 8) { ForEach(specs) { SpecButton(spec: $0) } }
    }
}

/// A wrapping row (WPF WrapPanel).
struct FlowLayout: Layout {
    var spacing: CGFloat = 8
    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let maxWidth = proposal.width ?? .infinity
        var x: CGFloat = 0, y: CGFloat = 0, lineHeight: CGFloat = 0, widest: CGFloat = 0
        for v in subviews {
            let s = v.sizeThatFits(.unspecified)
            if x > 0 && x + s.width > maxWidth { y += lineHeight + spacing; x = 0; lineHeight = 0 }
            x += s.width + spacing; lineHeight = max(lineHeight, s.height); widest = max(widest, x - spacing)
        }
        return CGSize(width: min(widest, maxWidth == .infinity ? widest : maxWidth), height: y + lineHeight)
    }
    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var x = bounds.minX, y = bounds.minY, lineHeight: CGFloat = 0
        for v in subviews {
            let s = v.sizeThatFits(.unspecified)
            if x > bounds.minX && x + s.width > bounds.maxX { y += lineHeight + spacing; x = bounds.minX; lineHeight = 0 }
            v.place(at: CGPoint(x: x, y: y), proposal: ProposedViewSize(s)); x += s.width + spacing; lineHeight = max(lineHeight, s.height)
        }
    }
}

struct SpecButton: View {
    let spec: ButtonSpec
    @EnvironmentObject var model: MainWindow
    var body: some View {
        let label = HStack(spacing: 6) {
            if let icon = spec.icon { Image(systemName: Glyphs.symbol(icon)) }
            if !spec.title.isEmpty { Text(spec.title) }
        }
        Group {
            switch spec.kind {
            case .accent: Button { model.perform(spec) } label: { label }.buttonStyle(.borderedProminent)
            case .quiet: Button { model.perform(spec) } label: { label }.buttonStyle(.link)
            case .danger: Button(role: .destructive) { model.perform(spec) } label: { label.foregroundColor(.red) }.buttonStyle(.bordered)
            case .normal: Button { model.perform(spec) } label: { label }.buttonStyle(.bordered)
            }
        }
        .disabled(!spec.enabled || (model.busy && !spec.local))
        .help(spec.help ?? "")
        .accessibilityLabel(spec.title)
    }
}

struct RowsView: View {
    let rows: [Row]
    var body: some View {
        VStack(spacing: 0) {
            ForEach(Array(rows.enumerated()), id: \.offset) { i, row in
                if i > 0 { Divider().padding(.leading, 14) }
                RowView(row: row)
            }
        }
        .background(RoundedRectangle(cornerRadius: 10).fill(Color(nsColor: .controlBackgroundColor)))
        .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(Color(nsColor: .separatorColor), lineWidth: 0.5))
    }
}

struct RowView: View {
    let row: Row
    @EnvironmentObject var model: MainWindow
    var body: some View {
        switch row {
        case .setting(let title, let description, let value, let actions):
            HStack(alignment: .center, spacing: 12) {
                VStack(alignment: .leading, spacing: 3) {
                    Text(title).font(.system(size: 13, weight: .semibold)).fixedSize(horizontal: false, vertical: true)
                    if let d = description, !d.isEmpty { Text(d).font(.system(size: 12)).foregroundColor(Tone.muted.color).fixedSize(horizontal: false, vertical: true) }
                }
                Spacer(minLength: 12)
                if let v = value, !v.isEmpty { Text(v).font(.system(size: 13)).lineLimit(1).truncationMode(.tail).frame(maxWidth: 320, alignment: .trailing).help(v).textSelection(.enabled) }
                ForEach(actions) { SpecButton(spec: $0).controlSize(.regular) }
            }
            .padding(.horizontal, 14).padding(.vertical, 10).frame(minHeight: 52)
        case .toggle(let title, let description, let value, let apply):
            HStack(alignment: .center, spacing: 12) {
                VStack(alignment: .leading, spacing: 3) {
                    Text(title).font(.system(size: 13, weight: .semibold)).fixedSize(horizontal: false, vertical: true)
                    if let d = description, !d.isEmpty { Text(d).font(.system(size: 12)).foregroundColor(Tone.muted.color).fixedSize(horizontal: false, vertical: true) }
                }
                Spacer(minLength: 12)
                // The switch shows the saved value; a successful save re-renders the page.
                Toggle("", isOn: Binding(get: { value }, set: { wanted in if wanted != value { model.runToggle(title, wanted, apply) } }))
                    .toggleStyle(.switch).labelsHidden().disabled(model.busy).accessibilityLabel(title)
            }
            .padding(.horizontal, 14).padding(.vertical, 10).frame(minHeight: 52)
        case .link(let icon, let title, let subtitle, let value, let badge, let tooltip, let tile, let action):
            Button(action: { if !model.busy { action() } }) {
                HStack(alignment: .center, spacing: 12) {
                    if tile {
                        Image(systemName: Glyphs.symbol(icon)).font(.system(size: 15)).foregroundColor(.accentColor)
                            .frame(width: 32, height: 32).background(RoundedRectangle(cornerRadius: 8).fill(Color.accentColor.opacity(0.12)))
                    } else {
                        Image(systemName: Glyphs.symbol(icon)).font(.system(size: 17)).foregroundColor(.accentColor).frame(width: 26)
                    }
                    VStack(alignment: .leading, spacing: 3) {
                        Text(title).font(.system(size: 13, weight: .semibold)).foregroundColor(Tone.text.color)
                        if let s = subtitle, !s.isEmpty { Text(s).font(.system(size: 12)).foregroundColor(Tone.muted.color).multilineTextAlignment(.leading).fixedSize(horizontal: false, vertical: true) }
                        if let b = badge { Label(b, systemImage: "exclamationmark.triangle").font(.system(size: 11, weight: .semibold)).foregroundColor(Tone.warn.color) }
                    }
                    Spacer(minLength: 8)
                    if let v = value, !v.isEmpty { Text(v).font(.system(size: 12)).foregroundColor(Tone.muted.color).lineLimit(1).frame(maxWidth: 200, alignment: .trailing) }
                    Image(systemName: "chevron.right").font(.system(size: 11, weight: .semibold)).foregroundColor(Tone.muted.color)
                }
                .padding(.horizontal, 14).padding(.vertical, 10).frame(minHeight: 52).contentShape(Rectangle())
            }
            .buttonStyle(.plain).help(tooltip ?? "").accessibilityLabel(title + ((value ?? "").isEmpty ? "" : ", " + value!))
        case .content(let blocks):
            BlocksView(blocks: blocks).padding(.horizontal, 14).padding(.vertical, 10)
        }
    }
}
