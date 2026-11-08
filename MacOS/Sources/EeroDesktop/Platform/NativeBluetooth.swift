import CoreBluetooth
import Foundation

/// Bluetooth Low Energy discovery of eeros waiting for setup (Windows: NativeBluetooth with the WinRT watcher).
/// Read-only: it scans for the eero setup service and reads the serial and firmware characteristics. Nothing is written.
enum NativeBluetooth {
    static let eeroService = UUID(uuidString: "d99a3d3f-c1ef-4533-809d-82de05d509ca")!
    static let eeroServiceUuid = CBUUID(string: "d99a3d3f-c1ef-4533-809d-82de05d509ca")
    static let serial = CBUUID(string: "00002a25-0000-1000-8000-00805f9b34fb"), firmware = CBUUID(string: "00002a26-0000-1000-8000-00805f9b34fb")

    /// CoreBluetooth never shows hardware addresses; each peripheral's stable per-Mac identifier stands in for the
    /// Windows Bluetooth address (its first 8 bytes), and the identifier is kept to reconnect for reading.
    static func address(_ id: UUID) -> UInt64 {
        let u = id.uuid
        return [u.0, u.1, u.2, u.3, u.4, u.5, u.6, u.7].reduce(UInt64(0)) { ($0 << 8) | UInt64($1) }
    }

    /// Errors for the Bluetooth states macOS can report, worded for the person at the Mac.
    static func availabilityError(_ state: CBManagerState) -> AppError? {
        switch state {
        case .poweredOn: return nil
        case .poweredOff: return AppError.invalidOperation("Bluetooth is turned off. Turn it on in Control Center or System Settings › Bluetooth, then search again.")
        case .unauthorized: return unauthorized()
        case .unsupported: return AppError.invalidOperation("This Mac does not have an available Bluetooth Low Energy adapter.")
        default: return AppError.invalidOperation("macOS did not make Bluetooth available in time. Check that Bluetooth is turned on, then search again.")
        }
    }
    static func unauthorized() -> AppError {
        if CBManager.authorization == .restricted { return AppError.invalidOperation("Bluetooth use is restricted on this Mac, for example by a device management profile.") }
        return AppError.invalidOperation("eero Desktop is not allowed to use Bluetooth. Allow it in System Settings › Privacy & Security › Bluetooth, then search again.")
    }
    /// macOS stops any process that touches Bluetooth without a usage description, so check before creating a manager.
    static func precheck() throws {
        if Bundle.main.object(forInfoDictionaryKey: "NSBluetoothAlwaysUsageDescription") == nil {
            throw AppError.invalidOperation("Bluetooth needs the eero Desktop app. Open eero Desktop.app so macOS can ask for Bluetooth permission; the bare command-line executable cannot use Bluetooth.")
        }
        switch CBManager.authorization {
        case .denied, .restricted: throw unauthorized()
        default: break
        }
    }

    static func scan(_ seconds: Int) async throws -> [BluetoothEero] {
        try precheck()
        let central = BluetoothCentral.shared
        try await central.ready(15)
        let found = try await central.scan(max(1, min(seconds, 30)))
        return found.map { BluetoothEero.advertisement($0.address, $0.name, $0.rssi) }
            .sorted { Int($0.signal) > Int($1.signal) }
    }
    static func readInfo(_ eero: BluetoothEero) async throws -> Any? {
        try precheck()
        let central = BluetoothCentral.shared
        try await central.ready(15)
        let values = try await central.read(UInt64(truncatingIfNeeded: eero.address), 30)
        return J.obj("serial", values.serial, "firmware", values.firmware, "bluetooth_version", eero.version, "signal_dbm", Int(eero.signal),
                     "already_in_network", eero.inNetwork.map { $0 as Any } ?? "Not reported", "ethernet_connected", eero.ethernet.map { $0 as Any } ?? "Not reported")
    }
    /// `--bluetooth-test <output.json>`: a 3 second scan, no connections and no GATT writes.
    static func check(_ output: String) -> Int {
        let done = DispatchSemaphore(value: 0)
        final class Box: @unchecked Sendable { var json = ""; var code = 1 }
        let box = Box()
        Task.detached {
            do {
                let results = try await scan(3)
                box.json = J.stringify(J.obj("ok", true, "scan_completed", true, "eero_candidates", results.count, "gatt_writes", 0)); box.code = 0
            } catch {
                box.json = J.stringify(J.obj("ok", false, "error", MainWindow.message(error), "bluetooth_state", BluetoothCentral.shared.stateName, "authorization", authorizationName, "gatt_writes", 0))
            }
            done.signal()
        }
        done.wait()
        do { try box.json.write(toFile: output, atomically: true, encoding: .utf8) } catch { return 1 }
        return box.code
    }
    static var authorizationName: String {
        switch CBManager.authorization {
        case .allowedAlways: return "allowed"
        case .denied: return "denied"
        case .restricted: return "restricted"
        case .notDetermined: return "not_determined"
        @unknown default: return "unknown"
        }
    }
}

/// One CBCentralManager for the app, driven on a private queue (results are bridged to async/await).
final class BluetoothCentral: NSObject, CBCentralManagerDelegate, CBPeripheralDelegate, @unchecked Sendable {
    static let shared = BluetoothCentral()
    struct Seen { let address: UInt64; let name: String?; let rssi: Int }
    private let queue = DispatchQueue(label: "io.github.los33.EeroDesktop.bluetooth")
    private var manager: CBCentralManager?
    private var stateWaiters: [UUID: CheckedContinuation<Void, Error>] = [:]
    private var found: [UInt64: Seen] = [:]
    private var known: [UInt64: CBPeripheral] = [:]
    private var scanning = false
    // Current GATT read
    private var reading: CBPeripheral?
    private var readContinuation: CheckedContinuation<(serial: String, firmware: String), Error>?
    private var values: [CBUUID: String] = [:]
    private var pending: Set<CBUUID> = []

    var stateName: String {
        queue.sync {
            guard let m = manager else { return "not_started" }
            switch m.state {
            case .poweredOn: return "powered_on"
            case .poweredOff: return "powered_off"
            case .unauthorized: return "unauthorized"
            case .unsupported: return "unsupported"
            case .resetting: return "resetting"
            case .unknown: return "unknown"
            @unknown default: return "unknown"
            }
        }
    }

    /// Waits until Bluetooth reports a definite state; anything but powered on becomes a clear error.
    func ready(_ timeout: TimeInterval) async throws {
        try await withCheckedThrowingContinuation { (c: CheckedContinuation<Void, Error>) in
            queue.async {
                if self.manager == nil { self.manager = CBCentralManager(delegate: self, queue: self.queue, options: [CBCentralManagerOptionShowPowerAlertKey: false]) }
                let state = self.manager!.state
                if state != .unknown && state != .resetting {
                    if let e = NativeBluetooth.availabilityError(state) { c.resume(throwing: e) } else { c.resume() }
                    return
                }
                let id = UUID()
                self.stateWaiters[id] = c
                self.queue.asyncAfter(deadline: .now() + timeout) {
                    guard let w = self.stateWaiters.removeValue(forKey: id) else { return }
                    let state = self.manager?.state ?? .unknown
                    if let e = NativeBluetooth.availabilityError(state) { w.resume(throwing: e) } else { w.resume() }
                }
            }
        }
    }
    private func finishWaiters(_ error: Error?) {
        let waiters = stateWaiters.values; stateWaiters.removeAll()
        for w in waiters { if let e = error { w.resume(throwing: e) } else { w.resume() } }
    }
    func centralManagerDidUpdateState(_ central: CBCentralManager) {
        switch central.state {
        case .unknown, .resetting: return
        case .poweredOn: finishWaiters(nil)
        default:
            finishWaiters(NativeBluetooth.availabilityError(central.state))
            if let c = readContinuation { readContinuation = nil; c.resume(throwing: NativeBluetooth.availabilityError(central.state)!) }
        }
    }

    func scan(_ seconds: Int) async throws -> [Seen] {
        try await withCheckedThrowingContinuation { (c: CheckedContinuation<[Seen], Error>) in
            queue.async {
                guard let m = self.manager, m.state == .poweredOn else { c.resume(throwing: NativeBluetooth.availabilityError(self.manager?.state ?? .unknown)!); return }
                if self.scanning { c.resume(throwing: AppError.invalidOperation("A Bluetooth search is already running.")); return }
                self.scanning = true; self.found.removeAll()
                m.scanForPeripherals(withServices: [NativeBluetooth.eeroServiceUuid], options: [CBCentralManagerScanOptionAllowDuplicatesKey: true])
                self.queue.asyncAfter(deadline: .now() + .seconds(seconds)) {
                    if m.isScanning { m.stopScan() }
                    self.scanning = false
                    if m.state != .poweredOn {
                        c.resume(throwing: AppError.invalidOperation("macOS could not scan for Bluetooth devices. Check that Bluetooth is turned on and that eero Desktop is allowed to use it."))
                        return
                    }
                    c.resume(returning: Array(self.found.values))
                }
            }
        }
    }
    func centralManager(_ central: CBCentralManager, didDiscover peripheral: CBPeripheral, advertisementData: [String: Any], rssi RSSI: NSNumber) {
        let services = (advertisementData[CBAdvertisementDataServiceUUIDsKey] as? [CBUUID] ?? []) + (advertisementData[CBAdvertisementDataOverflowServiceUUIDsKey] as? [CBUUID] ?? [])
        if !services.contains(NativeBluetooth.eeroServiceUuid) { return }
        let rssi = RSSI.intValue
        if rssi == 127 { return } // 127 means the signal strength is unavailable.
        let address = NativeBluetooth.address(peripheral.identifier)
        known[address] = peripheral
        let name = (advertisementData[CBAdvertisementDataLocalNameKey] as? String) ?? peripheral.name ?? found[address]?.name
        found[address] = Seen(address: address, name: name, rssi: rssi)
    }

    func read(_ address: UInt64, _ timeout: TimeInterval) async throws -> (serial: String, firmware: String) {
        try await withCheckedThrowingContinuation { (c: CheckedContinuation<(serial: String, firmware: String), Error>) in
            queue.async {
                guard let m = self.manager, m.state == .poweredOn else { c.resume(throwing: NativeBluetooth.availabilityError(self.manager?.state ?? .unknown)!); return }
                if self.readContinuation != nil { c.resume(throwing: AppError.invalidOperation("Another eero is being read. Wait for it to finish.")); return }
                guard let peripheral = self.known[address] else {
                    c.resume(throwing: AppError.invalidOperation("The eero could not be reached over Bluetooth. Keep it nearby and try scanning again.")); return
                }
                self.reading = peripheral; self.readContinuation = c; self.values = [:]; self.pending = []
                peripheral.delegate = self
                m.connect(peripheral, options: nil)
                self.queue.asyncAfter(deadline: .now() + timeout) {
                    if self.reading === peripheral && self.readContinuation != nil {
                        self.finishRead(AppError.invalidOperation("The eero could not be reached over Bluetooth. Keep it nearby and try scanning again."))
                    }
                }
            }
        }
    }
    private func finishRead(_ error: Error?) {
        guard let c = readContinuation else { return }
        readContinuation = nil
        if let p = reading { p.delegate = nil; manager?.cancelPeripheralConnection(p) }
        reading = nil
        if let e = error { c.resume(throwing: e) }
        else { c.resume(returning: (values[NativeBluetooth.serial] ?? "Unavailable", values[NativeBluetooth.firmware] ?? "Unavailable")) }
    }
    func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
        guard peripheral === reading else { return }
        peripheral.discoverServices([NativeBluetooth.eeroServiceUuid])
    }
    func centralManager(_ central: CBCentralManager, didFailToConnect peripheral: CBPeripheral, error: Error?) {
        guard peripheral === reading else { return }
        finishRead(AppError.invalidOperation("The eero could not be reached over Bluetooth. Keep it nearby and try scanning again."))
    }
    func centralManager(_ central: CBCentralManager, didDisconnectPeripheral peripheral: CBPeripheral, error: Error?) {
        guard peripheral === reading else { return }
        finishRead(AppError.invalidOperation("The eero setup service is not available. It may be out of range or no longer advertising setup."))
    }
    func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: Error?) {
        guard peripheral === reading else { return }
        guard error == nil, let service = peripheral.services?.first(where: { $0.uuid == NativeBluetooth.eeroServiceUuid }) else {
            finishRead(AppError.invalidOperation("The eero setup service is not available. It may be out of range or no longer advertising setup.")); return
        }
        peripheral.discoverCharacteristics([NativeBluetooth.serial, NativeBluetooth.firmware], for: service)
    }
    func peripheral(_ peripheral: CBPeripheral, didDiscoverCharacteristicsFor service: CBService, error: Error?) {
        guard peripheral === reading else { return }
        let characteristics = (service.characteristics ?? []).filter { ($0.uuid == NativeBluetooth.serial || $0.uuid == NativeBluetooth.firmware) && $0.properties.contains(.read) }
        if error != nil || characteristics.isEmpty { finishRead(nil); return }
        pending = Set(characteristics.map { $0.uuid })
        for c in characteristics { peripheral.readValue(for: c) }
    }
    func peripheral(_ peripheral: CBPeripheral, didUpdateValueFor characteristic: CBCharacteristic, error: Error?) {
        guard peripheral === reading, pending.contains(characteristic.uuid) else { return }
        pending.remove(characteristic.uuid)
        if error == nil, let data = characteristic.value {
            values[characteristic.uuid] = (String(data: data, encoding: .utf8) ?? String(decoding: data, as: UTF8.self)).trimmingCharacters(in: CharacterSet(charactersIn: "\0\r\n"))
        }
        if pending.isEmpty { finishRead(nil) }
    }
}
