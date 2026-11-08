import Foundation

// "Find nearby eeros" page (Windows: BluetoothSetup.cs, MainWindow part).
extension MainWindow {
    /// Account-scoped; clearPlatformState() removes it on sign-out like Windows ClearData.
    var bluetoothResults: [BluetoothEero] { get { stored("bluetoothResults", [BluetoothEero]()) } set { state["bluetoothResults"] = newValue } }
    func bluetoothSetupPage() {
        sub("Use this Mac's Bluetooth to find eeros waiting to be set up nearby and read their serial number. Nothing on the eero is changed.")
        group(nil, settingRow("Search with Bluetooth", "Put the new eero close to this Mac and plug it in. The search takes about 15 seconds. The first search asks for permission to use Bluetooth.", nil,
            rowActionAsync("Search", { [unowned self] in
                if self.preview { throw AppError.invalidOperation("Preview mode does not scan your devices.") }
                self.setNotice("Searching Bluetooth for 15 seconds…")
                self.bluetoothResults = try await NativeBluetooth.scan(15)
                self.renderPage(); self.setNotice("Bluetooth search finished. Found \(self.bluetoothResults.count) eero(s).")
            })))
        let entries = preview ? [BluetoothEero.advertisement(0, "4.0.0-Sample-1", -47)] : bluetoothResults
        var rows: [Row] = []
        for eero in entries {
            let rssi = Int(eero.signal)
            let signal = (rssi >= -60 ? "Close by" : rssi >= -75 ? "Nearby" : "Far away") + " (\(rssi) dBm)" + (eero.inNetwork == true ? " · Already in a network" : "")
            rows.append(settingRow(J.str(eero.name), signal, nil, rowActionAsync("Read serial", { [unowned self] in
                self.info("Nearby eero", self.preview ? J.obj("serial", "SAMPLE-SERIAL", "firmware", "Preview", "ethernet_connected", true) : try await NativeBluetooth.readInfo(eero))
            })))
        }
        if rows.isEmpty { rows.append(infoRow("No eeros found yet", nil, "Make sure the new eero is plugged in and its light is blinking white, then search.")) }
        group("eeros found", rows: rows)
        addGroupHeading("Next step")
        page.append(rowGroup(FeatureCatalog.entries.filter { $0.path == "Home/Add or replace eero" }.map { listRow($0, false) }))
        page.append(text("Full Bluetooth setup and replacing your gateway eero still need the official eero app.", 13, "#64738A"))
    }
}
