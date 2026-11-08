using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gtk;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private List<BluetoothEero> bluetoothResults = new List<BluetoothEero>();
        private void BluetoothSetupPage()
        {
            Sub("Use this computer's Bluetooth to find eeros waiting to be set up nearby and read their serial number. Nothing on the eero is changed.");
            Group(null, SettingRow("Search with Bluetooth", "Put the new eero close to this computer and plug it in. The search takes about 15 seconds.", null,
                RowActionAsync("Search", async delegate { if (preview) throw new InvalidOperationException("Preview mode does not scan your devices."); SetNotice("Searching Bluetooth for 15 seconds…"); bluetoothResults = await NativeBluetooth.Scan(15); RenderPage(); SetNotice("Bluetooth search finished. Found " + bluetoothResults.Count + " eero(s)."); })));
            var entries = preview ? new List<BluetoothEero> { BluetoothEero.Advertisement(0, "4.0.0-Sample-1", -47) } : bluetoothResults;
            var rows = new List<Widget>();
            foreach (BluetoothEero raw in entries)
            {
                BluetoothEero eero = raw; int rssi = eero.Signal;
                string signal = (rssi >= -60 ? "Close by" : rssi >= -75 ? "Nearby" : "Far away") + " (" + rssi + " dBm)" + (eero.InNetwork == true ? " · Already in a network" : "");
                rows.Add(SettingRow(eero.Name, signal, null, RowActionAsync("Read serial", async delegate { Info("Nearby eero", preview ? J.Obj("serial", "SAMPLE-SERIAL", "firmware", "Preview", "ethernet_connected", true) : await NativeBluetooth.ReadInfo(eero)); })));
            }
            if (rows.Count == 0) rows.Add(InfoRow("No eeros found yet", null, "Make sure the new eero is plugged in and its light is blinking white, then search."));
            Group("eeros found", rows.ToArray());
            AddGroupHeading(page, "Next step");
            page.Append(RowGroup(FeatureCatalog.Entries.Where(e => e.Path == "Home/Add or replace eero").Select(e => (Widget)ListRow(e, false))));
            page.Append(Text("Full Bluetooth setup and replacing your gateway eero still need the official eero app.", 13, "#64738A"));
        }
    }
}
