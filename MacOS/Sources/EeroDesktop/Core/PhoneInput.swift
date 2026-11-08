import Foundation

enum PhoneInput {
    static func normalize(_ input: String?) throws -> String {
        let text = (input ?? "").trim()
        if !Regex.isMatch(text, "^\\+?[0-9\\s().-]+$") { throw AppError.argument("Enter a phone number, or choose Email address to sign in with email.") }
        var digits = String(text.filter { $0 >= "0" && $0 <= "9" })
        let international = text.hasPrefix("+")
        if !international {
            if digits.count == 10 { digits = "1" + digits }
            else if digits.count != 11 || !digits.hasPrefix("1") { throw AppError.argument("Enter a 10-digit US/Canada number, or include + and your country code.") }
        }
        if digits.count < 8 || digits.count > 15 || digits.first == "0" { throw AppError.argument("Enter a complete phone number with a valid country code (up to 15 digits).") }
        if digits.hasPrefix("1") && digits.count != 11 { throw AppError.argument("US/Canada numbers need 10 digits after +1.") }
        return "+" + digits
    }

    /// Format complete numbers only, so backspacing and partially typed numbers stay editable.
    static func formatForDisplay(_ input: String?) -> String {
        guard let normalized = try? normalize(input) else { return input ?? "" }
        if normalized.hasPrefix("+1") && normalized.count == 12 {
            return "+1 (" + normalized.substring(2, 3) + ") " + normalized.substring(5, 3) + "-" + normalized.substring(8, 4)
        }
        return normalized
    }

    /// Caret positions are UTF-16 offsets (NSText selection ranges).
    static func preserveCaret(_ before: String, _ caret: Int, _ after: String) -> Int {
        let b = Array(before.utf16), a = Array(after.utf16)
        func digit(_ u: UInt16) -> Bool { u >= 48 && u <= 57 }
        var followingDigits = b[min(max(caret, 0), b.count)...].filter(digit).count
        if followingDigits == 0 { return a.count }
        var i = a.count - 1
        while i >= 0 {
            if digit(a[i]) { followingDigits -= 1; if followingDigits == 0 { return i } }
            i -= 1
        }
        return 0
    }
}
