import Foundation

final class VpnCleanupException: Error, LocalizedError, CustomStringConvertible {
    let message = "VPN startup failed and automatic disconnect failed. Restart your Mac or run \"sudo wg-quick down\" for the eero tunnel."
    init() {}
    var errorDescription: String? { message }
    var description: String { message }
}

enum VpnTunnelLifecycle {
    static func start(_ install: () throws -> Void, _ running: () throws -> Bool, _ handshake: () throws -> Bool, _ stop: () throws -> Void,
                      _ confirmed: () throws -> Void, _ attempts: Int, _ wait: () throws -> Void) throws {
        var installed = false
        do {
            try install(); installed = true
            for _ in 0..<max(attempts, 0) { try wait(); if try handshake() { try confirmed(); return } }
            throw AppError.invalidOperation("Guardian did not complete a VPN handshake. The tunnel was removed to restore your connection.")
        } catch {
            if try installed || running() {
                do { try stop() } catch { throw VpnCleanupException() }
            }
            throw error
        }
    }
}
