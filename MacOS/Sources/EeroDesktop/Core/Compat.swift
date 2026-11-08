import Foundation

// Small helpers so code translated from the Windows C# reads the same way.

extension String {
    func trim() -> String { trimmingCharacters(in: .whitespacesAndNewlines) }
    func trim(_ chars: String) -> String { trimmingCharacters(in: CharacterSet(charactersIn: chars)) }
    func trimEnd(_ chars: String) -> String {
        var s = Substring(self); let set = Set(chars)
        while let last = s.last, set.contains(last) { s = s.dropLast() }
        return String(s)
    }
    func trimStart(_ chars: String) -> String {
        var s = Substring(self); let set = Set(chars)
        while let first = s.first, set.contains(first) { s = s.dropFirst() }
        return String(s)
    }
    /// C# Split(char) — keeps empty entries.
    func split(_ separator: Character) -> [String] { components(separatedBy: String(separator)) }
    /// C# Split(chars, RemoveEmptyEntries).
    func splitRemovingEmpty(_ separators: String) -> [String] {
        components(separatedBy: CharacterSet(charactersIn: separators)).filter { !$0.isEmpty }
    }
    func lastPathPart(_ separator: Character = "/") -> String { split(separator).last ?? self }
    func substring(_ start: Int, _ length: Int? = nil) -> String {
        let u = Array(self)
        let end = length.map { min(u.count, start + $0) } ?? u.count
        return start >= end ? "" : String(u[start..<end])
    }
    var length: Int { count }
    func containsIgnoringCase(_ other: String) -> Bool { range(of: other, options: .caseInsensitive) != nil }
    func equalsIgnoringCase(_ other: String) -> Bool { caseInsensitiveCompare(other) == .orderedSame }
    func indexOf(_ s: String) -> Int { guard let r = range(of: s) else { return -1 }; return distance(from: startIndex, to: r.lowerBound) }
    func lastIndexOf(_ c: Character) -> Int { guard let i = lastIndex(of: c) else { return -1 }; return distance(from: startIndex, to: i) }
    /// C# Uri.EscapeDataString.
    var escapedData: String {
        addingPercentEncoding(withAllowedCharacters: CharacterSet(charactersIn: "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~")) ?? ""
    }
}

extension Optional where Wrapped == String {
    var orEmpty: String { self ?? "" }
}

/// Invariant number parsing/formatting like .NET (decimal point, no grouping).
enum Num {
    static let invariant = Locale(identifier: "en_US_POSIX")
    static func parseDouble(_ s: String) -> Double? { Double(s.trim()) }
    static func parseInt(_ s: String) -> Int? { Int(s.trim()) }
    /// .NET "0.#" style: up to `decimals` fraction digits, no trailing zeros, current locale separators.
    static func format(_ value: Double, _ decimals: Int, locale: Locale = .current) -> String {
        let f = NumberFormatter(); f.locale = locale; f.numberStyle = .decimal; f.usesGroupingSeparator = false
        f.minimumFractionDigits = 0; f.maximumFractionDigits = decimals; f.roundingMode = .halfEven
        return f.string(from: NSNumber(value: value)) ?? String(value)
    }
}

/// Date helpers mirroring the .NET formats the Windows UI uses.
enum Dates {
    static func iso8601(_ s: String) -> Date? {
        let f = ISO8601DateFormatter(); f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = f.date(from: s) { return d }
        f.formatOptions = [.withInternetDateTime]
        if let d = f.date(from: s) { return d }
        let g = DateFormatter(); g.locale = Num.invariant; g.timeZone = TimeZone.current
        for format in ["yyyy-MM-dd'T'HH:mm:ss.SSSSSSS", "yyyy-MM-dd'T'HH:mm:ss.SSS", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm"] {
            g.dateFormat = format; if let d = g.date(from: s) { return d }
        }
        return nil
    }
    static func format(_ d: Date, _ format: String) -> String {
        let f = DateFormatter(); f.locale = .current; f.dateFormat = format; return f.string(from: d)
    }
    static func utcStamp(_ d: Date) -> String {
        let f = DateFormatter(); f.locale = Num.invariant; f.timeZone = TimeZone(identifier: "UTC"); f.dateFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'"; return f.string(from: d)
    }
    static func roundTrip(_ d: Date) -> String {
        let f = ISO8601DateFormatter(); f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]; return f.string(from: d)
    }
}

/// Application data lives in ~/Library/Application Support/eero Desktop (Windows: %LOCALAPPDATA%\EeroDesktop).
enum AppPaths {
    static var dataDirectory: String {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!.path
        return (base as NSString).appendingPathComponent("eero Desktop")
    }
    static func data(_ name: String) -> String { (dataDirectory as NSString).appendingPathComponent(name) }
}
