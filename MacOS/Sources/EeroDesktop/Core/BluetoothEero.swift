import Foundation

final class BluetoothEero {
    var address: UInt64 = 0
    var name: String?
    var version: String?
    var signal: Int = 0
    var inNetwork: Bool?
    var ethernet: Bool?

    /// Accepts any integer types for the address (ulong on Windows) and signal (short on Windows).
    static func advertisement<A: BinaryInteger, S: BinaryInteger>(_ address: A, _ name: String?, _ signal: S) -> BluetoothEero {
        let row = BluetoothEero()
        row.address = UInt64(truncatingIfNeeded: address)
        row.name = (name ?? "").isEmpty ? "eero Setup" : name
        row.signal = Int(signal)
        let parts = (name ?? "").split("-")
        if parts.count >= 3, let major = versionMajor(parts[0]) {
            row.version = parts[0]
            if major == 3 { row.inNetwork = parts[1] == "1" } else if major >= 4 { row.ethernet = parts[2] == "1" }
        }
        return row
    }
    /// System.Version.TryParse: two to four non-negative integer components.
    private static func versionMajor(_ text: String) -> Int? {
        let c = text.trim().split(".")
        guard (2...4).contains(c.count), c.allSatisfy({ !$0.isEmpty && $0.allSatisfy { $0 >= "0" && $0 <= "9" } && Int32($0) != nil }) else { return nil }
        return Int(c[0])
    }
}
