import Charts
import Foundation
import SwiftUI

// Translated from ActivityCharts.cs: daily usage and security series as cards with a bar chart.
extension MainWindow {
    static func number(_ value: Any?) -> Double { Double(J.s(value).trim()).map { max(0, $0) } ?? 0 }
    static func amount(_ value: Double, _ bytes: Bool) -> String {
        if !bytes {
            let f = NumberFormatter(); f.locale = .current; f.numberStyle = .decimal; f.maximumFractionDigits = 0; f.minimumFractionDigits = 0
            return f.string(from: NSNumber(value: value)) ?? Num.format(value, 0)
        }
        if value >= 1000000000 { return Num.format(value / 1000000000, 2) + " GB" }
        if value >= 1000000 { return Num.format(value / 1000000, 2) + " MB" }
        if value >= 1000 { return Num.format(value / 1000, 2) + " kB" }
        return Num.format(value, 0) + " B"
    }
    /// Plain titles for the series eero returns in usage and security insight charts.
    static func seriesTitle(_ item: Any?, _ bytes: Bool) -> String {
        let type = J.first(item, "insight_type_name", "type", "insight_type"), key = type.lowercased()
        if key == "download" { return "Downloaded" }
        if key == "upload" { return "Uploaded" }
        if !bytes && !key.isEmpty && !key.contains("block") { return human(type) + " blocked" }
        return !key.isEmpty ? human(type) : (bytes ? "Data used" : "Blocked")
    }
    /// DateTime.TryParse for the date strings eero sends ("2026-10-16" or ISO 8601).
    static func chartDate(_ s: String) -> Date? {
        if s.isEmpty { return nil }
        if let d = Dates.iso8601(s) { return d }
        let f = DateFormatter(); f.locale = Num.invariant; f.timeZone = .current
        for format in ["yyyy-MM-dd", "yyyy/MM/dd", "yyyy-MM-dd HH:mm:ss", "MM/dd/yyyy"] { f.dateFormat = format; if let d = f.date(from: s) { return d } }
        return nil
    }

    func addSeriesCharts(_ target: inout [Block], _ data: Any?, _ bytes: Bool) {
        let series = J.list(J.get(data, "series"))
        if series.isEmpty {
            target.append(card([text(data == nil || data is JNull ? "eero hasn't returned any activity for this period. Choose Refresh at the top right to try again." : bytes ? "No data usage was recorded for this period." : "Nothing was blocked in this period.", 14, "#64738A")], 18))
            return
        }
        for item in series.prefix(12) {
            let values = Array(J.list(J.get(item, "values")).prefix(60))
            let sum = J.get(item, "sum")
            let total = sum == nil || sum is JNull ? values.reduce(0) { $0 + MainWindow.number(J.get($1, "value")) } : MainWindow.number(sum)
            let words: [Block] = [text(MainWindow.seriesTitle(item, bytes), 16, "#64738A", true),
                                  .text(TextSpec(text: MainWindow.amount(total, bytes) + (bytes ? "" : total == 1 ? " time" : " times"), size: 28, tone: .accent, bold: true))]
            var stats: [Block] = []
            if values.count > 1 {
                // OrderByDescending is stable: the first of equal maxima wins.
                var busiest = values[0]
                for v in values.dropFirst() where MainWindow.number(J.get(v, "value")) > MainWindow.number(J.get(busiest, "value")) { busiest = v }
                let peakDay = MainWindow.chartDate(J.s(busiest, "time")).map { Dates.format($0, "EEE, MMM d") } ?? J.s(busiest, "time")
                stats = [.view(VStack(alignment: .trailing, spacing: 2) {
                    Text("Daily average " + MainWindow.amount(total / Double(values.count), bytes)).font(.system(size: 12)).foregroundColor(Tone.muted.color)
                    Text((bytes ? "Busiest day " : "Most on ") + peakDay).font(.system(size: 12)).foregroundColor(Tone.muted.color)
                }.frame(maxHeight: .infinity, alignment: .bottom))]
            }
            var bars: [SeriesBar] = []
            for (i, value) in values.enumerated() {
                let n = MainWindow.number(J.get(value, "value")); var date = J.s(value, "time"), full = date
                if let parsed = MainWindow.chartDate(date) { date = Dates.format(parsed, values.count > 14 ? "dd" : "MMM d"); full = Dates.format(parsed, "EEEE, MMMM d") }
                bars.append(SeriesBar(key: String(i), label: date, full: full, value: n, amount: MainWindow.amount(n, bytes)))
            }
            target.append(card([.split(left: words, right: stats), .view(SeriesChartView(bars: bars, showValues: values.count <= 14))]))
        }
    }
}

struct SeriesBar: Identifiable {
    let key: String, label: String, full: String
    let value: Double, amount: String
    var id: String { key }
}

/// One equal-width slot per day: the value above the bar (14 days or fewer), an accent bar, then the date.
struct SeriesChartView: View {
    let bars: [SeriesBar]; let showValues: Bool
    @State private var hovered: String?
    var body: some View {
        let maximum = max(1, bars.map { $0.value }.max() ?? 1)
        // Keep at least a sliver for empty days, like the 2 px minimum bar on Windows.
        let floor = maximum * 2 / 140
        let step = bars.count > 31 ? 5 : bars.count > 14 ? 2 : 1
        let shown = Set(bars.enumerated().filter { $0.offset % step == 0 }.map { $0.element.key })
        Chart(bars) { bar in
            BarMark(x: .value("Day", bar.key), y: .value("Amount", max(bar.value, floor)), width: .ratio(0.8))
                .foregroundStyle(Color.accentColor)
                .cornerRadius(3)
                .annotation(position: .top, spacing: 4) {
                    if showValues { Text(bar.amount).font(.system(size: 10)).foregroundColor(Tone.muted.color).lineLimit(1).fixedSize() }
                }
                .accessibilityLabel(bar.full)
                .accessibilityValue(bar.amount)
        }
        .chartXAxis {
            AxisMarks(values: bars.map { $0.key }) { value in
                if let key = value.as(String.self), shown.contains(key), let bar = bars.first(where: { $0.key == key }) {
                    AxisValueLabel { Text(bar.label).font(.system(size: 10)).foregroundColor(Tone.muted.color).lineLimit(1) }
                }
            }
        }
        .chartYAxis(.hidden)
        .chartYScale(domain: 0...(maximum * (showValues ? 1.18 : 1.02)))
        .chartOverlay { proxy in
            GeometryReader { geo in
                Rectangle().fill(Color.clear).contentShape(Rectangle())
                    .onContinuousHover { phase in
                        switch phase {
                        case .active(let point):
                            let x = point.x - geo[proxy.plotAreaFrame].origin.x
                            hovered = proxy.value(atX: x, as: String.self)
                        case .ended: hovered = nil
                        }
                    }
            }
        }
        .overlay(alignment: .topLeading) {
            // Hover tooltip (Windows: a ToolTip on each day's column).
            if let key = hovered, let bar = bars.first(where: { $0.key == key }) {
                Text(bar.full + ": " + bar.amount).font(.system(size: 11)).padding(.horizontal, 8).padding(.vertical, 4)
                    .background(RoundedRectangle(cornerRadius: 5).fill(Color(nsColor: .windowBackgroundColor)))
                    .overlay(RoundedRectangle(cornerRadius: 5).strokeBorder(Color(nsColor: .separatorColor)))
                    .allowsHitTesting(false)
            }
        }
        .frame(height: 196)
        .padding(.top, 14)
    }
}
