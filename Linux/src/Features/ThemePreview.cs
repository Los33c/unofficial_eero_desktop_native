using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gtk;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        // Development-only route, available through --theme-preview / --render.
        private void ThemePreviewPage()
        {
            Sub("Sample controls for visual inspection. This screen does not make network requests.");
            var p = VBox(); p.Append(Text("Fields & choices", 20, "#17263C", true));
            var name = Entry.New(); name.SetText("Sample network name"); name.SetMarginBottom(10); name.Name("Sample network name"); p.Append(name);
            var secret = PasswordEntry.New(); secret.SetText("sample-password"); secret.SetShowPeekIcon(true); secret.SetMarginBottom(10); secret.Name("Sample password"); p.Append(secret);
            var choice = DropDown.NewFromStrings(new[] { "Automatic", "Manual", "Bridge", "Unavailable option" }); choice.SetSelected(0); choice.SetHalign(Gtk.Align.Start); choice.SetMarginBottom(10); choice.Name("Sample choice"); p.Append(choice);
            var row = HBox(24); row.SetMarginTop(5); row.SetMarginBottom(16);
            var enabled = CheckButton.NewWithLabel("Enabled option"); enabled.SetActive(true); row.Append(enabled);
            var disabled = CheckButton.NewWithLabel("Disabled option"); disabled.SetActive(true); disabled.SetSensitive(false); row.Append(disabled);
            row.Append(ToggleSwitch(true, "Sample switch"));
            p.Append(row);
            // WPF DataGrid sample: a selectable two-column list with a header row.
            var header = Columns(); PlaceInColumn(header, Text("Name", 13, "#64738A", true), 0); PlaceInColumn(header, Text("Status", 13, "#64738A", true), 1); header.Margin(14, 0, 14, 0); p.Append(header);
            var table = ListBox.New(); table.AddCssClass("boxed-list"); table.SetSelectionMode(SelectionMode.Single); table.Name("Sample table");
            foreach (var sample in new[] { new[] { "Selected device", "Online" }, new[] { "Alternating row", "Offline" } })
            {
                var cells = Columns(); var first = Text(sample[0], 14); first.SetMarginBottom(0); var second = Text(sample[1], 14); second.SetMarginBottom(0);
                PlaceInColumn(cells, first, 0); PlaceInColumn(cells, second, 1); cells.Margin(14, 10, 14, 10);
                var item = ListBoxRow.New(); item.SetChild(cells); table.Append(item);
            }
            table.SelectRow(table.GetRowAtIndex(0));
            p.Append(table); page.Append(Card(p));
            ActionRow(LocalButton("Open sample settings form", delegate { Edit("Sample settings", new[] { new Field("name", "Network name", "Sample network"), new Field("secret", "Password", null, "password"), new Field("mode", "Connection mode", "Automatic", "choice", "Automatic", "Manual", "Bridge"), new Field("enabled", "Enable sample option", true, "bool"), new Field("note", "Notes", "Sample text", "multiline") }, async v => { await Task.FromResult(0); }, "Close sample", "Preview controls only; no settings are saved."); }), LocalButton("Open sample confirmation", async delegate { await Confirm("This sample confirmation performs no network change."); }));
            // Sample of how raw eero data reads in detail views (taken from a real Ethernet port response shape).
            ObjectCards(J.Obj(
                "interfaces", new object[] { J.Obj("interface_number", 1, "name", "1", "actions", new object[] { J.Obj("type", "DISABLE_PORT", "position", 0) }, "network_type", "Unknown", "supported_speed", "P2500", "negotiated_speed", "P1000", "port_status", "DATA_CONNECTED", "ethernet_address", "a4:6b:1f:00:00:01", "capable_protocols", new object[] { "Data", "ETHERNET_CABLE_DIAGNOSTICS_PROVIDER" }) },
                "is_guest", false, "upload_bytes", 1572864L, "last_seen", "2026-11-07T15:30:00Z", "rssi", -58, "wan_ip_address", "203.0.113.10"), "Sample eero report");
            ActionRow(LocalButton("Open sample backup priority", delegate { BackupPriority(new List<object> { J.Obj("ssid", "Sample phone hotspot", "uuid", "sample-1"), J.Obj("ssid", "Sample backup wifi", "uuid", "sample-2") }); }), LocalButton("Open sample credential display", delegate { ShowCredentials("Sample credentials", new Dictionary<string, string> { { "Sample username", "sample-user" }, { "Sample password", "sample-password" } }); }));
        }
    }
}
