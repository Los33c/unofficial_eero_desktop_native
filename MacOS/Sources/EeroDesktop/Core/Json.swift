import Foundation

/// JSON null inside containers (a missing value is plain `nil`).
final class JNull: CustomStringConvertible {
    static let shared = JNull()
    private init() {}
    var description: String { "null" }
}

/// An ordered JSON object. eero responses are shown in document order, and request bodies
/// must serialize keys in insertion order exactly like the Windows build.
final class JObject: Sequence {
    private(set) var keys: [String] = []
    private var storage: [String: Any] = [:]
    init() {}
    init(_ pairs: [(String, Any?)]) { for (k, v) in pairs { self[k] = v } }
    var count: Int { keys.count }
    subscript(key: String) -> Any? {
        get { storage[key] }
        set {
            if let value = newValue {
                if storage[key] == nil { keys.append(key) }
                storage[key] = value
            } else {
                // Assigning nil stores a JSON null, like `d[key] = null` in C#.
                if storage[key] == nil { keys.append(key) }
                storage[key] = JNull.shared
            }
        }
    }
    func containsKey(_ key: String) -> Bool { storage[key] != nil }
    @discardableResult func remove(_ key: String) -> Bool {
        guard storage.removeValue(forKey: key) != nil else { return false }
        keys.removeAll { $0 == key }; return true
    }
    func tryGetValue(_ key: String) -> Any? { storage[key] }
    func makeIterator() -> AnyIterator<(key: String, value: Any)> {
        var index = 0
        return AnyIterator { [self] in
            guard index < keys.count else { return nil }
            let k = keys[index]; index += 1
            return (key: k, value: storage[k]!)
        }
    }
}

struct JsonError: Error, LocalizedError {
    let message: String
    var errorDescription: String? { message }
}

/// Helpers with the same semantics as the Windows `J` class (JavaScriptSerializer object model):
/// objects are `JObject`, arrays `[Any]`, whole numbers `Int`, other numbers `Decimal`, `Bool`, `String`, null `JNull`.
enum J {
    static let maxLength = 16 * 1024 * 1024
    static let maxDepth = 100

    static func obj(_ pairs: Any?...) -> JObject {
        let o = JObject()
        var i = 0
        while i + 1 < pairs.count { o[pairs[i] as! String] = pairs[i + 1] ?? JNull.shared; i += 2 }
        return o
    }
    static func get(_ o: Any?, _ keys: String...) -> Any? { getPath(o, keys) }
    static func getPath(_ o: Any?, _ keys: [String]) -> Any? {
        var current: Any? = o
        for k in keys {
            guard let d = current as? JObject, let v = d.tryGetValue(k) else { return nil }
            current = v
        }
        if current is JNull { return nil }
        return current
    }
    /// Same text as C# `Convert.ToString(value, InvariantCulture)`.
    static func s(_ o: Any?, _ keys: String...) -> String { str(keys.isEmpty ? o : getPath(o, keys)) }
    static func str(_ v: Any?) -> String {
        guard let v = v, !(v is JNull) else { return "" }
        switch v {
        case let x as String: return x
        case let x as Bool: return x ? "True" : "False"
        case let x as Int: return String(x)
        case let x as Int64: return String(x)
        case let x as Decimal: return NSDecimalNumber(decimal: x).stringValue
        case let x as Double: return formatDouble(x)
        case let x as Float: return formatDouble(Double(x))
        case let x as JObject: _ = x; return "System.Collections.Generic.Dictionary`2[System.String,System.Object]"
        case is [Any]: return "System.Object[]"
        default: return String(describing: v)
        }
    }
    static func formatDouble(_ d: Double) -> String {
        if d.isNaN { return "NaN" }
        if d.isInfinite { return d < 0 ? "-Infinity" : "Infinity" }
        if d == d.rounded() && abs(d) < 1e15 { return String(Int64(d)) }
        return "\(d)"
    }
    static func b(_ o: Any?, _ keys: String...) -> Bool {
        str(keys.isEmpty ? o : getPath(o, keys)).caseInsensitiveCompare("true") == .orderedSame
    }
    static func list(_ o: Any?) -> [Any] {
        if let d = o as? JObject { return d.containsKey("data") ? list(d.tryGetValue("data")) : [] }
        if o is String { return [] }
        if let a = o as? [Any] { return a }
        return []
    }
    static func first(_ o: Any?, _ keys: String...) -> String {
        for k in keys { let v = s(o, k); if !v.isEmpty { return v } }
        return ""
    }

    // MARK: Parsing
    static func parse(_ text: String) throws -> Any? {
        if text.utf16.count > maxLength { throw JsonError(message: "The JSON input is too large.") }
        var reader = Reader(Array(text.unicodeScalars))
        reader.space()
        let value = try reader.value(0)
        reader.space()
        if !reader.atEnd { throw JsonError(message: "Invalid JSON primitive after the document.") }
        return value is JNull ? nil : value
    }
    private struct Reader {
        let s: [Unicode.Scalar]; var i = 0
        init(_ scalars: [Unicode.Scalar]) { s = scalars }
        var atEnd: Bool { i >= s.count }
        mutating func space() { while i < s.count, s[i].properties.isWhitespace { i += 1 } }
        func bad(_ what: String) -> JsonError { JsonError(message: "Invalid JSON: \(what) at position \(i).") }
        mutating func value(_ depth: Int) throws -> Any {
            if depth > J.maxDepth { throw bad("nesting too deep") }
            if atEnd { throw bad("unexpected end") }
            let c = s[i]
            if c == "{" { return try object(depth) }
            if c == "[" { return try array(depth) }
            if c == "\"" || c == "'" { return try string() }
            if c == "-" || (c >= "0" && c <= "9") { return try number() }
            if word("true") { return true }
            if word("false") { return false }
            if word("null") { return JNull.shared }
            throw bad("unexpected character '\(c)'")
        }
        mutating func word(_ w: String) -> Bool {
            let scalars = Array(w.unicodeScalars)
            guard i + scalars.count <= s.count, Array(s[i..<(i + scalars.count)]) == scalars else { return false }
            let end = i + scalars.count
            if end < s.count, s[end].properties.isAlphabetic || (s[end] >= "0" && s[end] <= "9") || s[end] == "_" { return false }
            i = end; return true
        }
        mutating func object(_ depth: Int) throws -> JObject {
            let d = JObject(); i += 1; space()
            if !atEnd, s[i] == "}" { i += 1; return d }
            while true {
                space()
                if atEnd || (s[i] != "\"" && s[i] != "'") { throw bad("expected a property name") }
                let key = try string(); space()
                if atEnd || s[i] != ":" { throw bad("expected ':'") }
                i += 1; space()
                d[key] = try value(depth + 1); space()
                if atEnd { throw bad("unterminated object") }
                if s[i] == "," { i += 1; continue }
                if s[i] == "}" { i += 1; return d }
                throw bad("expected ',' or '}'")
            }
        }
        mutating func array(_ depth: Int) throws -> [Any] {
            var list: [Any] = []; i += 1; space()
            if !atEnd, s[i] == "]" { i += 1; return list }
            while true {
                space(); list.append(try value(depth + 1)); space()
                if atEnd { throw bad("unterminated array") }
                if s[i] == "," { i += 1; continue }
                if s[i] == "]" { i += 1; return list }
                throw bad("expected ',' or ']'")
            }
        }
        mutating func string() throws -> String {
            let quote = s[i]; i += 1
            var out = String.UnicodeScalarView()
            var pendingHigh: UInt32? = nil
            func flushHigh() { if let h = pendingHigh { out.append(Unicode.Scalar(0xFFFD)!); _ = h; pendingHigh = nil } }
            while true {
                if atEnd { throw bad("unterminated string") }
                let c = s[i]; i += 1
                if c == quote { flushHigh(); return String(out) }
                if c != "\\" { flushHigh(); out.append(c); continue }
                if atEnd { throw bad("unterminated escape") }
                let e = s[i]; i += 1
                switch e {
                case "\"": flushHigh(); out.append("\"")
                case "'": flushHigh(); out.append("'")
                case "\\": flushHigh(); out.append("\\")
                case "/": flushHigh(); out.append("/")
                case "b": flushHigh(); out.append("\u{8}")
                case "f": flushHigh(); out.append("\u{c}")
                case "n": flushHigh(); out.append("\n")
                case "r": flushHigh(); out.append("\r")
                case "t": flushHigh(); out.append("\t")
                case "u":
                    guard i + 4 <= s.count, let code = UInt32(String(String.UnicodeScalarView(s[i..<(i + 4)])), radix: 16) else { throw bad("invalid unicode escape") }
                    i += 4
                    if (0xD800...0xDBFF).contains(code) { flushHigh(); pendingHigh = code }
                    else if (0xDC00...0xDFFF).contains(code), let high = pendingHigh {
                        let combined = 0x10000 + ((high - 0xD800) << 10) + (code - 0xDC00)
                        out.append(Unicode.Scalar(combined)!); pendingHigh = nil
                    } else { flushHigh(); out.append(Unicode.Scalar(code) ?? Unicode.Scalar(0xFFFD)!) }
                default: throw bad("invalid escape")
                }
            }
        }
        mutating func number() throws -> Any {
            let start = i
            if s[i] == "-" { i += 1 }
            while i < s.count, (s[i] >= "0" && s[i] <= "9") || s[i] == "." || s[i] == "e" || s[i] == "E" || s[i] == "+" || s[i] == "-" { i += 1 }
            let text = String(String.UnicodeScalarView(s[start..<i]))
            if let n = Int32(text) { return Int(n) }
            if let l = Int64(text) { return Int(l) }
            if let d = Decimal(string: text, locale: Locale(identifier: "en_US_POSIX")), text.range(of: "^-?[0-9]+(\\.[0-9]+)?([eE][-+]?[0-9]+)?$", options: .regularExpression) != nil { return d }
            if let d = Double(text) { return d }
            throw bad("invalid number")
        }
    }

    // MARK: Serializing
    static func stringify(_ o: Any?) -> String {
        var out = ""
        write(&out, o, 0)
        return out
    }
    private static func write(_ b: inout String, _ o: Any?, _ depth: Int) {
        guard depth <= maxDepth else { b += "null"; return }
        guard let o = o, !(o is JNull) else { b += "null"; return }
        switch o {
        case let x as String: quote(&b, x)
        case let x as Bool: b += x ? "true" : "false"
        case let x as Int: b += String(x)
        case let x as Int64: b += String(x)
        case let x as UInt: b += String(x)
        case let x as Decimal: b += NSDecimalNumber(decimal: x).stringValue
        case let x as Double: b += (x.isNaN || x.isInfinite) ? "\"\(formatDouble(x))\"" : formatDouble(x)
        case let x as JObject:
            b += "{"; var first = true
            for (k, v) in x { if !first { b += "," }; first = false; quote(&b, k); b += ":"; write(&b, v, depth + 1) }
            b += "}"
        case let x as [(String, Any?)]:
            b += "{"; var first = true
            for (k, v) in x { if !first { b += "," }; first = false; quote(&b, k); b += ":"; write(&b, v, depth + 1) }
            b += "}"
        case let x as [String: Any]:
            // Unordered Swift dictionaries serialize with sorted keys for stable output.
            b += "{"; var first = true
            for k in x.keys.sorted() { if !first { b += "," }; first = false; quote(&b, k); b += ":"; write(&b, x[k], depth + 1) }
            b += "}"
        case let x as [Any?]:
            b += "["; var first = true
            for v in x { if !first { b += "," }; first = false; write(&b, v, depth + 1) }
            b += "]"
        case let x as [Any]:
            b += "["; var first = true
            for v in x { if !first { b += "," }; first = false; write(&b, v, depth + 1) }
            b += "]"
        default: quote(&b, String(describing: o))
        }
    }
    /// Same escaping as JavaScriptSerializer: short escapes for common controls and \u00XX for other
    /// controls and the HTML-sensitive characters < > ' &.
    private static func quote(_ b: inout String, _ s: String) {
        b += "\""
        for scalar in s.unicodeScalars {
            let u = scalar.value
            switch u {
            case 0x22: b += "\\\""
            case 0x5C: b += "\\\\"
            case 0x08: b += "\\b"
            case 0x0C: b += "\\f"
            case 0x0A: b += "\\n"
            case 0x0D: b += "\\r"
            case 0x09: b += "\\t"
            default:
                if u < 0x20 || u == 0x3C || u == 0x3E || u == 0x27 || u == 0x26 { b += String(format: "\\u%04x", u) }
                else { b.unicodeScalars.append(scalar) }
            }
        }
        b += "\""
    }
}

extension String {
    /// C# `NullIfEmpty()` extension.
    var nullIfEmpty: String? { isEmpty ? nil : self }
}
