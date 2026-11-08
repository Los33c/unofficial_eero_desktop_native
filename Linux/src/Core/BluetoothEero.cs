using System;

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
}
