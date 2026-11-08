using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using System.Windows.Controls;

namespace EeroDesktop
{
    public sealed class BluetoothEero
    {
        public ulong Address; public string Name, Version; public short Signal; public bool? InNetwork, Ethernet;
        public static BluetoothEero Advertisement(ulong address, string name, short signal)
        {
            var row = new BluetoothEero { Address = address, Name = String.IsNullOrEmpty(name) ? "eero Setup" : name, Signal = signal };
            string[] parts = (name ?? "").Split('-'); Version version;
            if (parts.Length >= 3 && System.Version.TryParse(parts[0], out version))
            {
                row.Version = parts[0]; if (version.Major == 3) row.InNetwork = parts[1] == "1"; else if (version.Major >= 4) row.Ethernet = parts[2] == "1";
            }
            return row;
        }
    }
    public static class NativeBluetooth
    {
        public static readonly Guid EeroService = new Guid("d99a3d3f-c1ef-4533-809d-82de05d509ca");
        private static readonly Guid Serial = new Guid("00002a25-0000-1000-8000-00805f9b34fb"), Firmware = new Guid("00002a26-0000-1000-8000-00805f9b34fb");
        public static async Task<List<BluetoothEero>> Scan(int seconds)
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                var adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(timeout.Token);
                if (adapter == null || !adapter.IsLowEnergySupported) throw new InvalidOperationException("This PC does not have an available Bluetooth Low Energy adapter.");
            }
            var found = new Dictionary<ulong, BluetoothEero>(); var sync = new object();
            var stopped = new TaskCompletionSource<string>();
            var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Passive };
            Windows.Foundation.TypedEventHandler<BluetoothLEAdvertisementWatcher, BluetoothLEAdvertisementReceivedEventArgs> received = delegate(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
            {
                if (!args.Advertisement.ServiceUuids.Contains(EeroService)) return;
                var item = BluetoothEero.Advertisement(args.BluetoothAddress, args.Advertisement.LocalName, args.RawSignalStrengthInDBm);
                lock (sync) found[item.Address] = item;
            };
            Windows.Foundation.TypedEventHandler<BluetoothLEAdvertisementWatcher, BluetoothLEAdvertisementWatcherStoppedEventArgs> ended = delegate(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args) { stopped.TrySetResult(args.Error.ToString()); };
            watcher.Received += received;
            watcher.Stopped += ended;
            try { watcher.Start(); await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(Math.Max(1, Math.Min(seconds, 30)))), stopped.Task); }
            finally { if (watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started) watcher.Stop(); watcher.Received -= received; watcher.Stopped -= ended; }
            if (watcher.Status == BluetoothLEAdvertisementWatcherStatus.Aborted) throw new InvalidOperationException("Windows could not scan for Bluetooth devices" + (stopped.Task.IsCompleted ? " (" + stopped.Task.Result + ")" : "") + ". Check that Bluetooth is enabled and available to desktop apps.");
            lock (sync) return found.Values.OrderByDescending(d => d.Signal).ToList();
        }
        public static async Task<object> ReadInfo(BluetoothEero eero)
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            using (var device = await BluetoothLEDevice.FromBluetoothAddressAsync(eero.Address).AsTask(timeout.Token))
            {
                if (device == null) throw new InvalidOperationException("The eero could not be reached over Bluetooth. Keep it nearby and try scanning again.");
                var result = await device.GetGattServicesForUuidAsync(EeroService, BluetoothCacheMode.Uncached).AsTask(timeout.Token);
                if (result.Status != GattCommunicationStatus.Success || result.Services.Count == 0) throw new InvalidOperationException("The eero setup service is not available. It may be out of range or no longer advertising setup.");
                string serial = "", firmware = "";
                try { serial = await ReadString(result.Services[0], Serial, timeout.Token); firmware = await ReadString(result.Services[0], Firmware, timeout.Token); }
                finally { foreach (var service in result.Services) service.Dispose(); }
                return J.Obj("serial", serial, "firmware", firmware, "bluetooth_version", eero.Version, "signal_dbm", eero.Signal, "already_in_network", eero.InNetwork.HasValue ? (object)eero.InNetwork.Value : "Not reported", "ethernet_connected", eero.Ethernet.HasValue ? (object)eero.Ethernet.Value : "Not reported");
            }
        }
        private static async Task<string> ReadString(GattDeviceService service, Guid characteristic, CancellationToken cancel)
        {
            var list = await service.GetCharacteristicsForUuidAsync(characteristic, BluetoothCacheMode.Uncached).AsTask(cancel);
            if (list.Status != GattCommunicationStatus.Success || list.Characteristics.Count == 0) return "Unavailable";
            var value = await list.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(cancel);
            if (value.Status != GattCommunicationStatus.Success || value.Value == null) return "Unavailable";
            using (var reader = DataReader.FromBuffer(value.Value)) { var bytes = new byte[reader.UnconsumedBufferLength]; reader.ReadBytes(bytes); return Encoding.UTF8.GetString(bytes).Trim('\0', '\r', '\n'); }
        }
        public static int Check(string output)
        {
            try { var results = Scan(3).GetAwaiter().GetResult(); File.WriteAllText(output, J.Stringify(J.Obj("ok", true, "scan_completed", true, "eero_candidates", results.Count, "gatt_writes", 0))); return 0; }
            catch (Exception e) { File.WriteAllText(output, J.Stringify(J.Obj("ok", false, "error", MainWindow.Message(e), "gatt_writes", 0))); return 1; }
        }
    }
    public sealed partial class MainWindow
    {
        private List<BluetoothEero> bluetoothResults = new List<BluetoothEero>();
        private void BluetoothSetupPage()
        {
            Sub("Use this PC's Bluetooth to find eeros waiting to be set up nearby and read their serial number. Nothing on the eero is changed.");
            Group(null, SettingRow("Search with Bluetooth", "Put the new eero close to this PC and plug it in. The search takes about 15 seconds.", null,
                RowActionAsync("Search", async delegate { if (preview) throw new InvalidOperationException("Preview mode does not scan your devices."); SetNotice("Searching Bluetooth for 15 seconds…"); bluetoothResults = await NativeBluetooth.Scan(15); RenderPage(); SetNotice("Bluetooth search finished. Found " + bluetoothResults.Count + " eero(s)."); })));
            var entries = preview ? new List<BluetoothEero> { BluetoothEero.Advertisement(0, "4.0.0-Sample-1", -47) } : bluetoothResults;
            var rows = new List<System.Windows.UIElement>();
            foreach (BluetoothEero raw in entries)
            {
                BluetoothEero eero = raw; int rssi = eero.Signal;
                string signal = (rssi >= -60 ? "Close by" : rssi >= -75 ? "Nearby" : "Far away") + " (" + rssi + " dBm)" + (eero.InNetwork == true ? " · Already in a network" : "");
                rows.Add(SettingRow(eero.Name, signal, null, RowActionAsync("Read serial", async delegate { Info("Nearby eero", preview ? J.Obj("serial", "SAMPLE-SERIAL", "firmware", "Preview", "ethernet_connected", true) : await NativeBluetooth.ReadInfo(eero)); })));
            }
            if (rows.Count == 0) rows.Add(InfoRow("No eeros found yet", null, "Make sure the new eero is plugged in and its light is blinking white, then search."));
            Group("eeros found", rows.ToArray());
            AddGroupHeading(page, "Next step");
            page.Children.Add(RowGroup(FeatureCatalog.Entries.Where(e => e.Path == "Home/Add or replace eero").Select(e => (System.Windows.UIElement)ListRow(e, false))));
            page.Children.Add(Text("Full Bluetooth setup and replacing your gateway eero still need the official eero app.", 13, "#64738A"));
        }
    }
}
