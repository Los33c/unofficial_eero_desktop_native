import AppKit
import CryptoKit
import Darwin
import Foundation

/// Guardian VPN on macOS through wireguard-tools (Windows: WindowsVpn with the WireGuard service).
///
/// Design (same safety properties as Windows):
/// - Connect/Disconnect re-run this executable as `--vpn-control <id>` inside the standard macOS administrator
///   prompt (`osascript … with administrator privileges`), like the Windows UAC helper. Every argument is
///   validated and single-quoted; the request id is 32 hex characters.
/// - The request (with the WireGuard profile) is a 0600 file in the private 0700 folder
///   `~/Library/Application Support/eero Desktop/VPN`, deleted as soon as the helper returns. The helper rebuilds
///   the configuration from the validated profile (VpnContracts.configuration), never from raw text.
/// - The running tunnel's configuration is a root-only 0600 file in `/private/var/db/eero-desktop-vpn`
///   (Windows: the machine VPN directory), removed on disconnect or failed startup.
/// - The tunnel name comes only from the validated scope; success is reported only after a fresh WireGuard
///   handshake (VpnTunnelLifecycle) and the tunnel is torn down automatically if that fails.
/// - The private key is never logged or passed on a command line.
enum MacVpn {
    static var userDirectory: String { AppPaths.data("VPN") }
    /// No spaces in this path: it is passed to the wg-quick bash script.
    static let machineDirectory = "/private/var/db/eero-desktop-vpn"
    static let toolDirectories = ["/opt/homebrew/bin", "/usr/local/bin"]
    static let wireGuardAppBundle = "com.wireguard.macos"
    static let installCommand = "brew install wireguard-tools"
    static let directoryVariable = "EERO_DESKTOP_VPN_DIRECTORY"

    /// wg-quick limits interface names to 15 characters: "eero-" plus the first 10 hex characters of the scope.
    static func tunnel(_ scope: String) throws -> String {
        if !Regex.isMatch(scope, "^[a-f0-9]{24}$") { throw AppError.argument("Invalid VPN profile.") }
        return "eero-" + String(scope.prefix(10))
    }
    static func shellQuote(_ value: String) -> String { "'" + value.replacingOccurrences(of: "'", with: "'\\''") + "'" }
    static func appleScriptString(_ value: String) -> String { "\"" + value.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"") + "\"" }

    // MARK: WireGuard tools
    /// Owner and mode of a file without following links.
    static func fileInfo(_ path: String) -> stat? {
        var s = stat()
        return lstat(path, &s) == 0 ? s : nil
    }
    /// A tool is trusted when it is a real executable (Homebrew links are resolved) owned by root or this user and
    /// not writable by anyone else. The directory holding all four tools wins; Apple Silicon Homebrew is checked first.
    static func trustedTool(_ path: String, _ owner: uid_t) -> Bool {
        let resolved = (path as NSString).resolvingSymlinksInPath
        guard let s = fileInfo(resolved), (s.st_mode & S_IFMT) == S_IFREG, (s.st_mode & 0o111) != 0, (s.st_mode & 0o022) == 0,
              s.st_uid == 0 || s.st_uid == owner else { return false }
        return true
    }
    static func toolDirectory(_ owner: uid_t = getuid()) throws -> String {
        for dir in toolDirectories where ["wg", "wg-quick", "wireguard-go", "bash"].allSatisfy({ trustedTool((dir as NSString).appendingPathComponent($0), owner) }) { return dir }
        throw AppError.notFound("Install WireGuard tools first: open Terminal and run \"" + installCommand + "\" (Homebrew), then return here.")
    }
    static var installed: Bool { (try? toolDirectory()) != nil }
    static var wireGuardAppUrl: URL? { NSWorkspace.shared.urlForApplication(withBundleIdentifier: wireGuardAppBundle) }

    // MARK: Status
    static func configurationPath(_ name: String) -> String { (machineDirectory as NSString).appendingPathComponent(name + ".conf") }
    static func markerPath(_ name: String) -> String { (machineDirectory as NSString).appendingPathComponent(name + ".utun") }
    /// The utun interface the helper recorded for this tunnel.
    static func interfaceName(_ name: String) -> String? {
        guard let s = fileInfo(markerPath(name)), (s.st_mode & S_IFMT) == S_IFREG, s.st_uid == 0, s.st_size < 64,
              let text = try? String(contentsOfFile: markerPath(name), encoding: .utf8) else { return nil }
        let value = text.trim()
        return Regex.isMatch(value, "^utun[0-9]{1,3}$") ? value : nil
    }
    static func interfaceUp(_ interface: String) -> Bool {
        var list: UnsafeMutablePointer<ifaddrs>? = nil
        guard getifaddrs(&list) == 0 else { return false }
        defer { freeifaddrs(list) }
        var cursor = list
        while let entry = cursor {
            if String(cString: entry.pointee.ifa_name) == interface && (entry.pointee.ifa_flags & UInt32(IFF_UP)) != 0 { return true }
            cursor = entry.pointee.ifa_next
        }
        return false
    }
    static func tunnelUp(_ name: String) -> Bool { interfaceName(name).map(interfaceUp) ?? false }
    static func status(_ scope: String) -> String {
        guard let name = try? tunnel(scope) else { return "Disconnected" }
        if tunnelUp(name) { return "Mac tunnel is running" }
        if fileInfo(configurationPath(name)) != nil || fileInfo(markerPath(name)) != nil { return "Mac tunnel: stopped" }
        return "Disconnected"
    }
    static func running(_ scope: String) -> Bool { status(scope) != "Disconnected" }
    static func ownActiveScopes() -> [String] {
        guard let files = try? FileManager.default.contentsOfDirectory(atPath: userDirectory) else { return [] }
        return files.filter { $0.hasSuffix(".dat") }.map { String($0.dropLast(4)) }.filter { Regex.isMatch($0, "^[a-f0-9]{24}$") && running($0) }.sorted()
    }
    static func anotherTunnel(_ scope: String) throws -> Bool {
        let own = try tunnel(scope)
        guard let files = try? FileManager.default.contentsOfDirectory(atPath: machineDirectory) else { return false }
        for f in files where Regex.isMatch(f, "^eero-[a-f0-9]{10}\\.utun$") {
            let name = String(f.dropLast(5))
            if name != own && tunnelUp(name) { return true }
        }
        return false
    }

    /// A WireGuard key pair (base64 Curve25519), generated in-process so the private key never touches a pipe.
    static func keyPair() throws -> [String] {
        let key = Curve25519.KeyAgreement.PrivateKey()
        let privateKey = try VpnContracts.key(key.rawRepresentation.base64EncodedString())
        let publicKey = try VpnContracts.key(key.publicKey.rawRepresentation.base64EncodedString())
        return [privateKey, publicKey]
    }

    // MARK: Processes
    struct CommandResult { let status: Int32; let output: String }
    /// Runs a program without a shell. Output is capped; stderr is discarded so nothing sensitive is echoed.
    static func command(_ executable: String, _ arguments: [String], _ environment: [String: String]? = nil, _ timeout: TimeInterval = 12) throws -> CommandResult {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = arguments
        if let environment = environment { process.environment = environment }
        let out = Pipe()
        process.standardOutput = out; process.standardError = FileHandle.nullDevice; process.standardInput = FileHandle.nullDevice
        let done = DispatchSemaphore(value: 0)
        process.terminationHandler = { _ in done.signal() }
        try process.run()
        var data = Data()
        let reader = DispatchQueue(label: "eero.vpn.read")
        let readDone = DispatchSemaphore(value: 0)
        reader.async { data = out.fileHandleForReading.readDataToEndOfFile(); readDone.signal() }
        if done.wait(timeout: .now() + timeout) == .timedOut {
            process.terminate()
            throw AppError.invalidOperation("The WireGuard tools did not respond.")
        }
        _ = readDone.wait(timeout: .now() + 2)
        if data.count > 65536 { throw AppError.invalidData("The WireGuard tools returned an unexpected response.") }
        return CommandResult(status: process.terminationStatus, output: (String(data: data, encoding: .utf8) ?? "").trim())
    }
    static func checked(_ executable: String, _ arguments: [String], _ environment: [String: String]? = nil, _ timeout: TimeInterval = 12) throws -> String {
        let r = try command(executable, arguments, environment, timeout)
        if r.status != 0 { throw AppError.invalidOperation("The WireGuard tools returned error \(r.status).") }
        return r.output
    }
    static var executablePath: String {
        let path = Bundle.main.executablePath ?? CommandLine.arguments[0]
        return ((path as NSString).isAbsolutePath ? path : (FileManager.default.currentDirectoryPath as NSString).appendingPathComponent(path) as String)
    }

    // MARK: Private user directory
    static func privateUserDirectory() throws {
        try FileManager.default.createDirectory(atPath: userDirectory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        guard let s = fileInfo(userDirectory), (s.st_mode & S_IFMT) == S_IFDIR else { throw AppError.io("The VPN data directory cannot be a link.") }
        if s.st_uid != getuid() { throw AppError.io("The VPN data directory has an unexpected owner.") }
        if chmod(userDirectory, 0o700) != 0 { throw AppError.io("The VPN data directory could not be made private.") }
    }

    /// Connect or disconnect through the administrator helper. Returns the helper's result object.
    @discardableResult
    static func control(_ action: String, _ scope: String, _ profile: Any? = nil) async throws -> Any? {
        if action != "connect" && action != "disconnect" { throw AppError.argument("Unsupported VPN action.") }
        _ = try tunnel(scope)
        if let profile = profile { _ = try VpnContracts.configuration(profile) }
        _ = try toolDirectory()
        try privateUserDirectory()
        let id = UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased()
        let file = (userDirectory as NSString).appendingPathComponent(id + ".request")
        let resultFile = (userDirectory as NSString).appendingPathComponent(id + ".result")
        defer { Files.delete(file); Files.delete(resultFile) }
        try Files.writeText(J.stringify(J.obj("action", action, "scope", scope, "created", Dates.roundTrip(Date()), "profile", profile)), file)
        let exe = executablePath
        if exe.unicodeScalars.contains(where: { $0.value < 32 || $0.value == 127 }) || userDirectory.unicodeScalars.contains(where: { $0.value < 32 || $0.value == 127 }) {
            throw AppError.invalidOperation("eero Desktop is installed in a folder the VPN helper cannot use. Move the app to Applications and try again.")
        }
        let shell = directoryVariable + "=" + shellQuote(userDirectory) + " " + shellQuote(exe) + " --vpn-control " + id
        let prompt = action == "connect" ? "eero Desktop needs administrator permission to start the eero VPN tunnel." : "eero Desktop needs administrator permission to stop the eero VPN tunnel."
        let script = "do shell script " + appleScriptString(shell) + " with prompt " + appleScriptString(prompt) + " with administrator privileges"
        _ = try await Task.detached { try command("/usr/bin/osascript", ["-e", script], nil, 600) }.value
        guard Files.exists(resultFile), let text = try? Files.readText(resultFile) else {
            throw AppError.invalidOperation("Administrator permission was cancelled or the VPN helper did not complete.")
        }
        let result = try J.parse(text)
        if !J.b(result, "ok") { throw AppError.invalidOperation(J.s(result, "message")) }
        return result
    }

    // MARK: Administrator helper (`--vpn-control <id>`, runs as root inside the macOS administrator prompt)
    /// Exit codes: 0 done, 1 failed (result file explains), 2 invalid request, 3 not running as administrator.
    static func elevatedControl(_ id: String) -> Int {
        if !Regex.isMatch(id, "^[a-f0-9]{32}$") { return 2 }
        if geteuid() != 0 { return 3 }
        guard let directory = ProcessInfo.processInfo.environment[directoryVariable], let owner = requestDirectoryOwner(directory) else { return 2 }
        let resultFile = (directory as NSString).appendingPathComponent(id + ".result")
        do {
            let requestFile = (directory as NSString).appendingPathComponent(id + ".request")
            let request = try J.parse(try readOwnedFile(requestFile, owner, 32768))
            guard let created = Dates.iso8601(J.s(request, "created")), abs(created.timeIntervalSinceNow) <= 600 else { throw AppError.invalidOperation("The VPN request expired. Try again.") }
            let scope = J.s(request, "scope"), action = J.s(request, "action")
            let name = try tunnel(scope)
            if action != "connect" && action != "disconnect" { throw AppError.invalidData("Unsupported VPN action.") }
            try secureMachineDirectory()
            let lock = open((machineDirectory as NSString).appendingPathComponent(".lock"), O_RDWR | O_CREAT | O_NOFOLLOW, 0o600)
            if lock < 0 { throw AppError.io("The VPN lock could not be opened.") }
            defer { flock(lock, LOCK_UN); close(lock) }
            if flock(lock, LOCK_EX | LOCK_NB) != 0 { throw AppError.invalidOperation("Another VPN action is in progress.") }
            let tools = try toolDirectory(owner)
            let environment = ["PATH": tools + ":/usr/bin:/bin:/usr/sbin:/sbin", "LANG": "C", "LC_ALL": "C"]
            let bash = (tools as NSString).appendingPathComponent("bash"), quick = (tools as NSString).appendingPathComponent("wg-quick"), wg = (tools as NSString).appendingPathComponent("wg")
            let path = configurationPath(name)
            if action == "disconnect" {
                if tunnelUp(name) {
                    if fileInfo(path) == nil { throw AppError.invalidOperation("The VPN configuration is missing. Restart this Mac to remove the eero tunnel.") }
                    _ = try checked(bash, [quick, "down", path], environment, 30)
                }
                unlink(path); unlink(markerPath(name))
                try writeResult(resultFile, owner, J.obj("ok", true, "message", "VPN disconnected.")); return 0
            }
            if tunnelUp(name) { throw AppError.invalidOperation("Disconnect the existing Mac tunnel before reconnecting or changing region.") }
            if try anotherTunnel(scope) { throw AppError.invalidOperation("Another eero VPN is active on this Mac. Disconnect it before connecting this network's VPN.") }
            // A stopped tunnel left behind (for example after a restart) is replaced.
            unlink(path); unlink(markerPath(name))
            let configuration = try VpnContracts.configuration(J.get(request, "profile"))
            try writeRootFile(path, Data(configuration.utf8), 0o600)
            do {
                try VpnTunnelLifecycle.start({
                    _ = try checked(bash, [quick, "up", path], environment, 45)
                    let runtime = "/var/run/wireguard/" + name + ".name"
                    let utun = ((try? String(contentsOfFile: runtime, encoding: .utf8)) ?? "").trim()
                    if !Regex.isMatch(utun, "^utun[0-9]{1,3}$") { throw AppError.invalidOperation("The WireGuard tunnel did not start.") }
                    try writeRootFile(markerPath(name), Data((utun + "\n").utf8), 0o644)
                }, { tunnelUp(name) }, {
                    guard let utun = interfaceName(name), let r = try? command(wg, ["show", utun, "latest-handshakes"], environment), r.status == 0 else { return false }
                    return handshakeFresh(r.output, Date())
                }, {
                    _ = try checked(bash, [quick, "down", path], environment, 30)
                }, {
                    try writeResult(resultFile, owner, J.obj("ok", true, "message", "Connected to Guardian. WireGuard handshake verified.", "verified_utc", Dates.roundTrip(Date())))
                }, 15, { Thread.sleep(forTimeInterval: 1) })
                return 0
            } catch let e as VpnCleanupException { throw e }
            catch { unlink(path); unlink(markerPath(name)); throw error }
        } catch {
            try? writeResult(resultFile, owner, J.obj("ok", false, "message", MainWindow.message(error)))
            return 1
        }
    }
    /// `wg show <if> latest-handshakes` lines are "<peer key>\t<unix time>"; a handshake within 3 minutes counts.
    static func handshakeFresh(_ output: String, _ now: Date) -> Bool {
        for line in output.split("\n") {
            let parts = line.trim().split("\t")
            if parts.count == 2, let epoch = Int64(parts[1]), epoch > 0, abs(now.timeIntervalSince1970 - Double(epoch)) < 180 { return true }
        }
        return false
    }
    /// The request folder must be the user's private eero Desktop VPN folder: a real 0700 directory owned by a normal user.
    static func requestDirectoryOwner(_ directory: String) -> uid_t? {
        guard (directory as NSString).isAbsolutePath, (directory as NSString).standardizingPath == directory, directory.hasSuffix("/eero Desktop/VPN"),
              !directory.unicodeScalars.contains(where: { $0.value < 32 }),
              let s = fileInfo(directory), (s.st_mode & S_IFMT) == S_IFDIR, (s.st_mode & 0o077) == 0, s.st_uid != 0 else { return nil }
        return s.st_uid
    }
    static func readOwnedFile(_ path: String, _ owner: uid_t, _ limit: Int) throws -> String {
        guard let s = fileInfo(path), (s.st_mode & S_IFMT) == S_IFREG, s.st_uid == owner, s.st_nlink == 1 else { throw AppError.invalidData("Invalid VPN request.") }
        if s.st_size > limit { throw AppError.invalidData("Invalid VPN request size.") }
        let fd = open(path, O_RDONLY | O_NOFOLLOW)
        if fd < 0 { throw AppError.invalidData("Invalid VPN request.") }
        let handle = FileHandle(fileDescriptor: fd, closeOnDealloc: true)
        let data = handle.readData(ofLength: limit + 1)
        if data.count > limit { throw AppError.invalidData("Invalid VPN request size.") }
        guard let text = String(data: data, encoding: .utf8) else { throw AppError.invalidData("Invalid VPN request.") }
        return text
    }
    /// The result goes back to the user's folder as a new 0600 file owned by that user (never through a link).
    static func writeResult(_ path: String, _ owner: uid_t, _ value: JObject) throws {
        unlink(path)
        let fd = open(path, O_WRONLY | O_CREAT | O_EXCL | O_NOFOLLOW, 0o600)
        if fd < 0 { throw AppError.io("The VPN result could not be saved.") }
        let handle = FileHandle(fileDescriptor: fd, closeOnDealloc: true)
        _ = fchown(fd, owner, 0)
        handle.write(Data(J.stringify(value).utf8))
    }
    static func writeRootFile(_ path: String, _ data: Data, _ mode: mode_t) throws {
        unlink(path)
        let fd = open(path, O_WRONLY | O_CREAT | O_EXCL | O_NOFOLLOW, mode)
        if fd < 0 { throw AppError.io("The VPN configuration could not be saved.") }
        let handle = FileHandle(fileDescriptor: fd, closeOnDealloc: true)
        _ = fchmod(fd, mode)
        handle.write(data)
    }
    /// root:wheel 0755 (tunnel status markers are readable; configurations inside are 0600).
    static func secureMachineDirectory() throws {
        if let s = fileInfo(machineDirectory) {
            if (s.st_mode & S_IFMT) != S_IFDIR { throw AppError.io("The system VPN directory cannot be a link.") }
            if s.st_uid != 0 { throw AppError.io("The system VPN directory has an unexpected owner.") }
        } else if mkdir(machineDirectory, 0o755) != 0 { throw AppError.io("The system VPN directory could not be created.") }
        if chown(machineDirectory, 0, 0) != 0 || chmod(machineDirectory, 0o755) != 0 { throw AppError.io("The system VPN directory could not be secured.") }
    }

    // MARK: WireGuard app (Mac App Store) alternative
    static var exportDirectory: String { (userDirectory as NSString).appendingPathComponent("Export") }
    /// Writes the tunnel as a .conf file the WireGuard app can import. The file holds the private key, so it lives in
    /// the private VPN folder (0600) and is deleted again shortly after the WireGuard app has opened it.
    static func exportConfiguration(_ scope: String, _ profile: Any?) throws -> String {
        let name = try tunnel(scope)
        let configuration = try VpnContracts.configuration(profile)
        try privateUserDirectory()
        try FileManager.default.createDirectory(atPath: exportDirectory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        _ = chmod(exportDirectory, 0o700)
        for old in (try? FileManager.default.contentsOfDirectory(atPath: exportDirectory)) ?? [] { Files.delete((exportDirectory as NSString).appendingPathComponent(old)) }
        let path = (exportDirectory as NSString).appendingPathComponent(name + ".conf")
        try Files.writeText(configuration, path)
        return path
    }
    @MainActor
    static func openInWireGuardApp(_ scope: String, _ profile: Any?) async throws {
        guard let app = wireGuardAppUrl else { throw AppError.notFound("Install WireGuard from the Mac App Store first, then try again.") }
        let path = try exportConfiguration(scope, profile)
        let configuration = NSWorkspace.OpenConfiguration(); configuration.activates = true
        do { _ = try await NSWorkspace.shared.open([URL(fileURLWithPath: path)], withApplicationAt: app, configuration: configuration) }
        catch { Files.delete(path); throw AppError.invalidOperation("The WireGuard app could not open the tunnel file.") }
        Task.detached { try? await Task.sleep(nanoseconds: 120_000_000_000); Files.delete(path) }
    }
}
