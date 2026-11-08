import CryptoKit
import Foundation
import Security

/// macOS counterpart of Windows DPAPI (CurrentUser scope). Data is sealed with AES-256-GCM under a
/// random per-user key stored in the login Keychain, so files written with it are useless without
/// the user's unlocked Keychain. Format: version(1) | nonce(12) | tag(16) | ciphertext.
enum ProtectedData {
    private static let version: UInt8 = 1
    private static let lock = NSLock()
    private static var cachedKey: SymmetricKey?
    /// Self-tests replace the key source so they never touch the user's Keychain.
    static var keyProvider: () throws -> Data = KeychainKey.loadOrCreate
    static func resetCache() { lock.lock(); cachedKey = nil; lock.unlock() }

    private static func key() throws -> SymmetricKey {
        lock.lock(); defer { lock.unlock() }
        if let k = cachedKey { return k }
        let data: Data
        do { data = try keyProvider() }
        catch let e as AppError { throw e }
        catch { throw AppError.crypto("The macOS Keychain is not available: \(error.localizedDescription)") }
        guard data.count == 32 else { throw AppError.crypto("The macOS Keychain returned an unusable key.") }
        let k = SymmetricKey(data: data); cachedKey = k; return k
    }
    static func protect(_ userData: Data, _ entropy: Data?) throws -> Data {
        let box = try AES.GCM.seal(userData, using: try key(), authenticating: entropy ?? Data())
        var out = Data([version])
        out.append(contentsOf: box.nonce); out.append(box.tag); out.append(box.ciphertext)
        return out
    }
    static func unprotect(_ encrypted: Data, _ entropy: Data?) throws -> Data {
        let bytes = [UInt8](encrypted)
        guard bytes.count >= 29, bytes[0] == version else { throw AppError.crypto("The protected data is not in a supported format.") }
        do {
            let box = try AES.GCM.SealedBox(nonce: AES.GCM.Nonce(data: Data(bytes[1..<13])), ciphertext: Data(bytes[29...]), tag: Data(bytes[13..<29]))
            return try AES.GCM.open(box, using: try key(), authenticating: entropy ?? Data())
        } catch let e as AppError { throw e }
        catch { throw AppError.crypto("The protected data was changed or belongs to another user.") }
    }
}

/// The per-user data key, a generic password item in the login Keychain.
enum KeychainKey {
    static let service = "io.github.los33.EeroDesktop.DataKey"
    static let account = "data-key"
    static func loadOrCreate() throws -> Data {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account, kSecReturnData as String: true, kSecMatchLimit as String: kSecMatchLimitOne]
        var item: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &item)
        if status == errSecSuccess, let data = item as? Data, data.count == 32 { return data }
        if status != errSecItemNotFound && status != errSecSuccess { throw AppError.crypto("The macOS Keychain refused access (\(status)). Unlock your login keychain, or sign in without Remember me.") }
        var bytes = [UInt8](repeating: 0, count: 32)
        guard SecRandomCopyBytes(kSecRandomDefault, bytes.count, &bytes) == errSecSuccess else { throw AppError.crypto("Could not create a random key.") }
        let data = Data(bytes)
        SecItemDelete([kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account] as CFDictionary)
        let add: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account, kSecValueData as String: data,
                                  kSecAttrLabel as String: "eero Desktop data protection key", kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly]
        let added = SecItemAdd(add as CFDictionary, nil)
        guard added == errSecSuccess else { throw AppError.crypto("The macOS Keychain refused to store the data key (\(added)).") }
        return data
    }
}
