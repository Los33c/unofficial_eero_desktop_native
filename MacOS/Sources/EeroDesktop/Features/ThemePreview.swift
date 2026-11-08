import Foundation
import SwiftUI

extension MainWindow {
    // Development-only route, available through --theme-preview / --render.
    func themePreviewPage() {
        sub("Sample controls for visual inspection. This screen does not make network requests.")
        page.append(card([text("Fields & choices", 20, "#17263C", true), .view(ThemePreviewSample())]))
        actionRow(localButton("Open sample settings form", { [self] in
            edit("Sample settings", [Field("name", "Network name", "Sample network"), Field("secret", "Password", nil, "password"), Field("mode", "Connection mode", "Automatic", "choice", "Automatic", "Manual", "Bridge"), Field("enabled", "Enable sample option", true, "bool"), Field("note", "Notes", "Sample text", "multiline")], { _ in }, "Close sample", "Preview controls only; no settings are saved.")
        }), ButtonSpec("Open sample confirmation", local: true, action: { [self] in _ = await confirm("This sample confirmation performs no network change.") }))
        // Sample of how raw eero data reads in detail views (taken from a real Ethernet port response shape).
        objectCards(J.obj(
            "interfaces", [J.obj("interface_number", 1, "name", "1", "actions", [J.obj("type", "DISABLE_PORT", "position", 0)], "network_type", "Unknown", "supported_speed", "P2500", "negotiated_speed", "P1000", "port_status", "DATA_CONNECTED", "ethernet_address", "a4:6b:1f:00:00:01", "capable_protocols", ["Data", "ETHERNET_CABLE_DIAGNOSTICS_PROVIDER"])],
            "is_guest", false, "upload_bytes", 1572864, "last_seen", "2026-11-07T15:30:00Z", "rssi", -58, "wan_ip_address", "203.0.113.10"), "Sample eero report")
        actionRow(localButton("Open sample backup priority", { [self] in backupPriority([J.obj("ssid", "Sample phone hotspot", "uuid", "sample-1"), J.obj("ssid", "Sample backup wifi", "uuid", "sample-2")]) }),
                  localButton("Open sample credential display", { [self] in showCredentials("Sample credentials", [("Sample username", "sample-user"), ("Sample password", "sample-password")]) }))
    }
}

/// Native controls in the current theme: text and password fields, a pop-up menu, checkboxes, a switch and a table.
struct ThemePreviewSample: View {
    struct SampleRow: Identifiable { let id: Int; let name: String; let status: String }
    @State private var name = "Sample network name"
    @State private var secret = "sample-password"
    @State private var choice = "Automatic"
    @State private var enabled = true
    @State private var sampleSwitch = true
    @State private var selection: Set<Int> = [0]
    private let rows = [SampleRow(id: 0, name: "Selected device", status: "Online"), SampleRow(id: 1, name: "Alternating row", status: "Offline")]
    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            TextField("", text: $name).textFieldStyle(.roundedBorder).frame(maxWidth: 420).accessibilityLabel("Sample network name")
            SecureField("", text: $secret).textFieldStyle(.roundedBorder).frame(maxWidth: 420).accessibilityLabel("Sample password")
            Picker("", selection: $choice) {
                ForEach(["Automatic", "Manual", "Bridge"], id: \.self) { Text($0).tag($0) }
                Text("Unavailable option").tag("Unavailable option").foregroundColor(.secondary)
            }.labelsHidden().fixedSize().accessibilityLabel("Sample choice")
            HStack(spacing: 24) {
                Toggle("Enabled option", isOn: $enabled).toggleStyle(.checkbox)
                Toggle("Disabled option", isOn: .constant(true)).toggleStyle(.checkbox).disabled(true)
                Toggle("", isOn: $sampleSwitch).toggleStyle(.switch).labelsHidden().accessibilityLabel("Sample switch")
            }.padding(.top, 5).padding(.bottom, 6)
            Table(rows, selection: $selection) {
                TableColumn("Name", value: \.name)
                TableColumn("Status", value: \.status)
            }
            .frame(height: 96).accessibilityLabel("Sample table")
        }
    }
}
