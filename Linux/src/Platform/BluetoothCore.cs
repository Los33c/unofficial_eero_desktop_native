using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Variant = GLib.Variant;
using VariantType = GLib.VariantType;

namespace EeroDesktop
{
    // Bluetooth Low Energy discovery of eeros waiting for setup, through BlueZ on the system D-Bus.
    // Like Windows, it only scans and reads two GATT characteristics; it never writes to the eero.
    public static class NativeBluetooth
    {
        public static readonly Guid EeroService = new Guid("d99a3d3f-c1ef-4533-809d-82de05d509ca");
        private static readonly Guid Serial = new Guid("00002a25-0000-1000-8000-00805f9b34fb"), Firmware = new Guid("00002a26-0000-1000-8000-00805f9b34fb");
        private const string BlueZ = "org.bluez", AdapterInterface = "org.bluez.Adapter1", DeviceInterface = "org.bluez.Device1";
        private const string ServiceInterface = "org.bluez.GattService1", CharacteristicInterface = "org.bluez.GattCharacteristic1";
        private static readonly object Sync = new object();
        private static Gio.DBusConnection bus;

        // BlueZ holds a discovery session only while its D-Bus client stays connected, so one system bus connection is kept.
        private static Gio.DBusConnection Bus()
        {
            lock (Sync)
            {
                if (bus != null && !bus.IsClosed()) return bus;
                // --bluetooth-test runs before the GTK libraries are set up.
                try { Gio.Module.Initialize(); }
                catch (Exception e) { throw new InvalidOperationException("Bluetooth is not available because the GLib/GIO libraries could not be loaded." + Reason(e)); }
                try { bus = Gio.Functions.BusGetSync(Gio.BusType.System, null); }
                catch (Exception e) { throw new InvalidOperationException("Bluetooth is not available because the system message bus (D-Bus) could not be reached." + Reason(e)); }
                return bus;
            }
        }
        private static string Reason(Exception e)
        {
            string message = (e.Message ?? "").Replace("\n", " ");
            if (message.Length > 160) message = message.Substring(0, 160);
            return message.Length == 0 ? "" : " (" + message.TrimEnd('.') + ")";
        }
        // Turns BlueZ and D-Bus errors into messages a person can act on.
        private static Exception Explain(Exception e, string fallback)
        {
            string m = e.Message ?? "";
            if (m.Contains("org.freedesktop.DBus.Error.ServiceUnknown") || m.Contains("org.freedesktop.DBus.Error.NameHasNoOwner") || m.Contains("was not provided by any .service files") || m.Contains("Failed to activate service") || m.Contains("org.freedesktop.DBus.Error.Spawn"))
                return new InvalidOperationException("BlueZ, the Linux Bluetooth service, is not running. Install the bluez package and start the bluetooth service (for example: systemctl start bluetooth), then search again.");
            if (m.Contains("org.freedesktop.DBus.Error.AccessDenied") || m.Contains("org.bluez.Error.NotAuthorized") || m.Contains("org.bluez.Error.NotPermitted"))
                return new InvalidOperationException("This account is not allowed to use Bluetooth. Run the app from your desktop session, or ask your administrator to allow Bluetooth access (some distributions require the bluetooth group).");
            if (m.Contains("org.bluez.Error.NotReady") || m.Contains("Resource Not Ready"))
                return new InvalidOperationException("Bluetooth is turned off. Turn it on in your system settings, then search again.");
            if (m.Contains("org.bluez.Error.Blocked") || m.Contains("rfkill"))
                return new InvalidOperationException("Bluetooth is blocked (airplane mode or a hardware switch). Unblock it, then search again.");
            if (m.Contains("org.freedesktop.DBus.Error.NoReply") || m.Contains("Timeout"))
                return new System.TimeoutException("BlueZ did not respond in time. Try again.");
            if (e is InvalidOperationException || e is System.TimeoutException) return e;
            return new InvalidOperationException(fallback + Reason(e));
        }
        private static Variant Call(string path, string iface, string method, Variant parameters, int timeout = 10000)
        {
            return Bus().CallSync(BlueZ, path, iface, method, parameters, null, Gio.DBusCallFlags.None, timeout, null);
        }
        // object path -> interface -> property -> value
        private static Dictionary<string, object> ManagedObjects()
        {
            // Without Bluetooth hardware, systemd does not start bluetoothd and D-Bus activation only times out.
            if (!Directory.Exists("/sys/class/bluetooth") && !BlueZRunning()) throw new InvalidOperationException(NoAdapter);
            Variant reply;
            try { reply = Call("/", "org.freedesktop.DBus.ObjectManager", "GetManagedObjects", null); }
            catch (Exception e) { throw Explain(e, "Bluetooth devices could not be listed"); }
            return Unbox(reply.GetChildValue(0)) as Dictionary<string, object> ?? new Dictionary<string, object>();
        }
        private const string NoAdapter = "This computer does not have an available Bluetooth Low Energy adapter.";
        private static bool BlueZRunning()
        {
            try { return Bus().CallSync("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "NameHasOwner", Variant.NewTuple(new[] { Variant.NewString(BlueZ) }), null, Gio.DBusCallFlags.None, 5000, null).GetChildValue(UIntPtr.Zero).GetBoolean(); }
            catch (InvalidOperationException) { throw; }
            catch (Exception) { return false; }
        }
        private static Dictionary<string, object> Interface(object node, string name)
        {
            var interfaces = node as Dictionary<string, object>; object value;
            return interfaces != null && interfaces.TryGetValue(name, out value) ? value as Dictionary<string, object> : null;
        }
        private static object Property(Dictionary<string, object> properties, string name)
        { object value; return properties != null && properties.TryGetValue(name, out value) ? value : null; }
        private static bool HasUuid(Dictionary<string, object> properties, Guid uuid)
        {
            var list = Property(properties, "UUIDs") as List<object>; Guid parsed;
            return list != null && list.Any(u => Guid.TryParse(u as string, out parsed) && parsed == uuid);
        }
        private static Guid Uuid(Dictionary<string, object> properties) { Guid parsed; return Guid.TryParse(Property(properties, "UUID") as string, out parsed) ? parsed : Guid.Empty; }
        internal static ulong Address(string text)
        {
            string hex = (text ?? "").Replace(":", "");
            ulong value; return hex.Length == 12 && UInt64.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value) ? value : 0;
        }
        // The adapter to use: the first powered one, else the first one.
        private static string Adapter(Dictionary<string, object> objects)
        {
            var adapters = objects.Where(o => Interface(o.Value, AdapterInterface) != null).OrderBy(o => o.Key, StringComparer.Ordinal).ToList();
            if (adapters.Count == 0) throw new InvalidOperationException(NoAdapter);
            var powered = adapters.FirstOrDefault(o => Property(Interface(o.Value, AdapterInterface), "Powered") as bool? == true);
            if (powered.Key == null) throw new InvalidOperationException("Bluetooth is turned off. Turn it on in your system settings, then search again.");
            return powered.Key;
        }
        private static Variant Options(params KeyValuePair<string, Variant>[] items)
        {
            var entries = items.Select(i => Variant.NewDictEntry(Variant.NewString(i.Key), Variant.NewVariant(i.Value))).ToArray();
            return Variant.NewArray(VariantType.New("{sv}"), entries);
        }
        private static KeyValuePair<string, Variant> Option(string key, Variant value) { return new KeyValuePair<string, Variant>(key, value); }

        public static Task<List<BluetoothEero>> Scan(int seconds) { return Task.Run(() => ScanNow(seconds)); }
        private static List<BluetoothEero> ScanNow(int seconds)
        {
            string adapter = Adapter(ManagedObjects());
            // Only LE advertisements of the eero setup service (BlueZ scans actively; Windows used a passive scan).
            try { Call(adapter, AdapterInterface, "SetDiscoveryFilter", Variant.NewTuple(new[] { Options(Option("Transport", Variant.NewString("le")), Option("UUIDs", Variant.NewStrv(new[] { EeroService.ToString() })), Option("DuplicateData", Variant.NewBoolean(false))) })); }
            catch (Exception e)
            {
                if ((e.Message ?? "").Contains("org.bluez.Error.NotSupported")) throw new InvalidOperationException(NoAdapter);
                throw Explain(e, "Bluetooth could not prepare the search");
            }
            bool started = false;
            try
            {
                try { Call(adapter, AdapterInterface, "StartDiscovery", null); started = true; }
                catch (Exception e) { if (!(e.Message ?? "").Contains("org.bluez.Error.InProgress")) throw Explain(e, "Bluetooth could not scan for devices. Check that Bluetooth is enabled"); }
                Thread.Sleep(TimeSpan.FromSeconds(Math.Max(1, Math.Min(seconds, 30))));
                // BlueZ reports RSSI only for devices heard during the current discovery.
                var found = new Dictionary<ulong, BluetoothEero>();
                foreach (var entry in ManagedObjects())
                {
                    var device = Interface(entry.Value, DeviceInterface);
                    if (device == null || Property(device, "Adapter") as string != adapter || !(Property(device, "RSSI") is short) || !HasUuid(device, EeroService)) continue;
                    ulong address = Address(Property(device, "Address") as string); if (address == 0) continue;
                    found[address] = BluetoothEero.Advertisement(address, Property(device, "Name") as string, (short)Property(device, "RSSI"));
                }
                return found.Values.OrderByDescending(d => d.Signal).ToList();
            }
            finally
            {
                if (started) { try { Call(adapter, AdapterInterface, "StopDiscovery", null); } catch (Exception) { } }
                try { Call(adapter, AdapterInterface, "SetDiscoveryFilter", Variant.NewTuple(new[] { Options() })); } catch (Exception) { }
            }
        }

        public static Task<object> ReadInfo(BluetoothEero eero) { return Task.Run(() => ReadInfoNow(eero)); }
        private static object ReadInfoNow(BluetoothEero eero)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            var objects = ManagedObjects();
            string path = objects.Where(o => Address(Property(Interface(o.Value, DeviceInterface), "Address") as string) == eero.Address && eero.Address != 0).Select(o => o.Key).FirstOrDefault();
            if (path == null) throw new InvalidOperationException("The eero could not be reached over Bluetooth. Keep it nearby and try scanning again.");
            bool wasConnected = Property(Interface(objects[path], DeviceInterface), "Connected") as bool? == true;
            try
            {
                if (!wasConnected)
                {
                    try { Call(path, DeviceInterface, "Connect", null, 25000); }
                    catch (Exception e)
                    {
                        var explained = Explain(e, "");
                        if (explained.Message.StartsWith("This account", StringComparison.Ordinal) || explained.Message.StartsWith("Bluetooth is", StringComparison.Ordinal) || explained.Message.StartsWith("BlueZ,", StringComparison.Ordinal)) throw explained;
                        throw new InvalidOperationException("The eero could not be reached over Bluetooth. Keep it nearby and try scanning again.");
                    }
                }
                // Wait until BlueZ has discovered the GATT services.
                while (true)
                {
                    objects = ManagedObjects();
                    if (Property(Interface(objects[path], DeviceInterface), "ServicesResolved") as bool? == true) break;
                    if (DateTime.UtcNow > deadline || !objects.ContainsKey(path)) throw new InvalidOperationException("The eero setup service is not available. It may be out of range or no longer advertising setup.");
                    Thread.Sleep(500);
                }
                string service = objects.Where(o => { var s = Interface(o.Value, ServiceInterface); return s != null && Property(s, "Device") as string == path && Uuid(s) == EeroService; }).Select(o => o.Key).FirstOrDefault();
                if (service == null) throw new InvalidOperationException("The eero setup service is not available. It may be out of range or no longer advertising setup.");
                string serial = ReadString(objects, service, Serial), firmware = ReadString(objects, service, Firmware);
                return J.Obj("serial", serial, "firmware", firmware, "bluetooth_version", eero.Version, "signal_dbm", eero.Signal, "already_in_network", eero.InNetwork.HasValue ? (object)eero.InNetwork.Value : "Not reported", "ethernet_connected", eero.Ethernet.HasValue ? (object)eero.Ethernet.Value : "Not reported");
            }
            finally { if (!wasConnected) { try { Call(path, DeviceInterface, "Disconnect", null); } catch (Exception) { } } }
        }
        private static string ReadString(Dictionary<string, object> objects, string service, Guid characteristic)
        {
            string path = objects.Where(o => { var c = Interface(o.Value, CharacteristicInterface); return c != null && Property(c, "Service") as string == service && Uuid(c) == characteristic; }).Select(o => o.Key).FirstOrDefault();
            if (path == null) return "Unavailable";
            try
            {
                var bytes = Unbox(Call(path, CharacteristicInterface, "ReadValue", Variant.NewTuple(new[] { Options() }), 15000).GetChildValue(0)) as byte[];
                return bytes == null ? "Unavailable" : Encoding.UTF8.GetString(bytes).Trim('\0', '\r', '\n');
            }
            catch (Exception) { return "Unavailable"; }
        }

        // GVariant to .NET: strings, numbers, booleans, byte[] for "ay", List<object> for arrays,
        // Dictionary<string, object> for dictionaries and object[] for tuples.
        internal static object Unbox(Variant value)
        {
            if (value == null) return null;
            string type = value.GetTypeString();
            switch (type[0])
            {
                case 's': case 'o': case 'g': { UIntPtr length; return value.GetString(out length); }
                case 'b': return value.GetBoolean();
                case 'y': return value.GetByte();
                case 'n': return value.GetInt16();
                case 'q': return value.GetUint16();
                case 'i': return value.GetInt32();
                case 'u': return value.GetUint32();
                case 'x': return value.GetInt64();
                case 't': return value.GetUint64();
                case 'd': return value.GetDouble();
                case 'h': return value.GetHandle();
                case 'v': return Unbox(value.GetVariant());
                case 'm': return value.NChildren() == UIntPtr.Zero ? null : Unbox(value.GetChildValue(UIntPtr.Zero));
            }
            int count = (int)value.NChildren();
            if (type == "ay")
            {
                var bytes = new byte[count];
                for (int i = 0; i < count; i++) bytes[i] = value.GetChildValue((UIntPtr)i).GetByte();
                return bytes;
            }
            if (type.StartsWith("a{", StringComparison.Ordinal))
            {
                var map = new Dictionary<string, object>(StringComparer.Ordinal);
                for (int i = 0; i < count; i++)
                {
                    Variant entry = value.GetChildValue((UIntPtr)i);
                    map[Convert.ToString(Unbox(entry.GetChildValue(UIntPtr.Zero)), CultureInfo.InvariantCulture)] = Unbox(entry.GetChildValue((UIntPtr)1));
                }
                return map;
            }
            if (type[0] == 'a')
            {
                var list = new List<object>(count);
                for (int i = 0; i < count; i++) list.Add(Unbox(value.GetChildValue((UIntPtr)i)));
                return list;
            }
            var tuple = new object[count];
            for (int i = 0; i < count; i++) tuple[i] = Unbox(value.GetChildValue((UIntPtr)i));
            return tuple;
        }

        public static int Check(string output)
        {
            try { var results = Scan(3).GetAwaiter().GetResult(); File.WriteAllText(output, J.Stringify(J.Obj("ok", true, "scan_completed", true, "eero_candidates", results.Count, "gatt_writes", 0, "backend", "bluez"))); return 0; }
            catch (Exception e) { File.WriteAllText(output, J.Stringify(J.Obj("ok", false, "error", MainWindow.Message(e), "gatt_writes", 0, "backend", "bluez"))); return 1; }
        }
    }
}
