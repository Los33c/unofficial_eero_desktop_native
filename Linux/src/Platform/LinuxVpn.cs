using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EeroDesktop
{
    // WireGuard tunnels for Guardian VPN on Linux.
    // NetworkManager is preferred: desktop users may add and start connections through polkit without sudo,
    // and the imported connection is kept in memory only (--temporary), so the private key is never stored by NM.
    // Without NetworkManager, wg-quick runs through pkexec by re-invoking this app with --vpn-control <id>,
    // like the elevated helper on Windows. The request travels over the helper's stdin and the result over its
    // stdout, so the root helper never reads or writes files in the user's home directory.
    public static class LinuxVpn
    {
        public static string UserDirectory { get { return Path.Combine(MainWindow.DataDirectory, "VPN"); } }
        // Root-only, in tmpfs: wg-quick configurations disappear on reboot.
        private const string MachineDirectory = "/run/eero-desktop-vpn";
        private const string ResultMarker = "EERO-VPN-RESULT ";
        private static readonly string[] SystemPaths = { "/usr/bin", "/usr/sbin", "/bin", "/sbin", "/usr/local/bin", "/usr/local/sbin", "/run/current-system/sw/bin" };

        // Linux interface names are limited to 15 characters, so the tunnel uses the first 40 bits of the scope hash.
        public static string Tunnel(string scope) { if (!Regex.IsMatch(scope ?? "", "^[a-f0-9]{24}$")) throw new ArgumentException("Invalid VPN profile."); return "eero-" + scope.Substring(0, 10); }

        internal static string Find(string name)
        {
            foreach (string directory in SystemPaths)
            {
                string path = Path.Combine(directory, name);
                if (File.Exists(path)) return path;
            }
            return null;
        }
        private static string Binary(string name)
        {
            string path = Find(name);
            if (path == null) throw new FileNotFoundException(MissingComponents);
            return path;
        }
        private static DateTime backendChecked; private static string backend;
        // "NetworkManager", "wg-quick" or null when neither can run a tunnel.
        public static string Backend
        {
            get
            {
                if ((DateTime.UtcNow - backendChecked).TotalSeconds < 5) return backend;
                string found = null;
                try
                {
                    string nmcli = Find("nmcli");
                    if (nmcli != null && Command(nmcli, new[] { "-t", "-f", "RUNNING", "general" }, null, 5000, false).Trim() == "running") found = "NetworkManager";
                }
                catch (Exception) { }
                if (found == null && Find("wg-quick") != null && Find("pkexec") != null) found = "wg-quick";
                backend = found; backendChecked = DateTime.UtcNow;
                return found;
            }
        }
        public static bool Installed { get { return Find("wg") != null && Backend != null; } }
        // What to install, in distribution-neutral words.
        public static string MissingComponents
        {
            get
            {
                var missing = new List<string>();
                if (Find("wg") == null) missing.Add("WireGuard tools (the wireguard-tools package)");
                if (Backend == null) missing.Add("NetworkManager with WireGuard support, or wg-quick from wireguard-tools together with polkit's pkexec");
                if (missing.Count == 0) return "The WireGuard components are installed.";
                return "Install " + String.Join(" and ", missing) + " with your distribution's package manager, then return here.";
            }
        }
        public static string Status(string scope)
        {
            if (!Running(scope)) return "Disconnected";
            return Backend == "NetworkManager" && NetworkManagerHas(Tunnel(scope)) ? "Tunnel is running (NetworkManager)" : "Tunnel is running";
        }
        public static bool Running(string scope) { return Directory.Exists("/sys/class/net/" + Tunnel(scope)); }
        public static string[] OwnActiveScopes()
        {
            try
            {
                if (!Directory.Exists(UserDirectory)) return new string[0];
                return Directory.GetFiles(UserDirectory, "*.dat").Select(Path.GetFileNameWithoutExtension).Where(scope => Regex.IsMatch(scope, "^[a-f0-9]{24}$") && Running(scope)).ToArray();
            }
            catch (IOException) { return new string[0]; }
            catch (UnauthorizedAccessException) { return new string[0]; }
        }
        public static bool AnotherTunnel(string scope)
        {
            string name = Tunnel(scope);
            if (!Directory.Exists("/sys/class/net")) return false;
            return Directory.GetFileSystemEntries("/sys/class/net").Select(Path.GetFileName).Any(n => n.StartsWith("eero-", StringComparison.Ordinal) && n != name);
        }
        public static string[] KeyPair()
        {
            string wg = Binary("wg");
            string privateKey = VpnContracts.Key(Command(wg, new[] { "genkey" }, null, 12000, false));
            string publicKey = VpnContracts.Key(Command(wg, new[] { "pubkey" }, privateKey + "\n", 12000, false));
            return new[] { privateKey, publicKey };
        }
        // Runs a program without a shell. Error details are included only for tools that never print keys.
        internal static string Command(string exe, IEnumerable<string> arguments, string input, int timeout, bool details, int limit = 8192)
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            start.Environment["LC_ALL"] = "C";
            using (var process = Process.Start(start))
            {
                if (input != null) process.StandardInput.Write(input);
                process.StandardInput.Close();
                Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeout)) { try { process.Kill(true); } catch (Exception) { } throw new System.TimeoutException("The VPN component did not respond."); }
                Task.WaitAll(output, error);
                if (process.ExitCode != 0) throw new InvalidOperationException("The VPN component returned error " + process.ExitCode + (details ? Detail(error.Result) : "") + ".");
                if (output.Result.Length > limit) throw new InvalidDataException("The VPN component returned an unexpected response.");
                return output.Result.Trim();
            }
        }
        // Last meaningful line of a tool's error output (wg-quick's "[#] command" echoes skipped), without control characters.
        private static string Detail(string error)
        {
            string line = (error ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("[#]", StringComparison.Ordinal)).LastOrDefault() ?? "";
            line = new string(line.Where(c => c >= 32 && c < 127).ToArray());
            if (line.Length > 200) line = line.Substring(0, 200);
            return line.Length == 0 ? "" : " (" + line.TrimEnd('.') + ")";
        }
        internal static void PrivateUserDirectory()
        {
            Directory.CreateDirectory(UserDirectory);
            if ((File.GetAttributes(UserDirectory) & FileAttributes.ReparsePoint) != 0) throw new IOException("The VPN data directory cannot be a link.");
            File.SetUnixFileMode(UserDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        // Creates a new file readable only by its owner and writes the text to it.
        private static void WritePrivate(string path, string text)
        {
            if (File.Exists(path) || Directory.Exists(path)) File.Delete(path);
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            try
            {
                using (var stream = new FileStream(path, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite }))
                    stream.Write(bytes, 0, bytes.Length);
            }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }
        // One VPN action at a time (flock on a file in a private directory).
        private static FileStream Lock(string directory)
        {
            try { return new FileStream(Path.Combine(directory, "control.lock"), new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite }); }
            catch (IOException) { throw new InvalidOperationException("Another VPN action is in progress."); }
        }

        public static async Task<object> Control(string action, string scope, object profile = null)
        {
            if (action != "connect" && action != "disconnect") throw new ArgumentException("Unsupported VPN action.");
            string name = Tunnel(scope); if (profile != null) VpnContracts.Configuration(profile);
            PrivateUserDirectory();
            string mode = Backend;
            if (action == "connect" && (mode == null || Find("wg") == null)) throw new FileNotFoundException(MissingComponents);
            if (mode == "NetworkManager" && (action == "connect" || NetworkManagerHas(name)))
            {
                try { return await Task.Run(delegate { return NetworkManagerControl(action, scope, profile); }); }
                // Some systems only let administrators change NetworkManager connections; use wg-quick with an admin prompt then.
                catch (Exception e) when (action == "connect" && NotAuthorized(e) && Find("wg-quick") != null && Find("pkexec") != null) { }
            }
            if (action == "disconnect" && !Running(scope)) return J.Obj("ok", true, "message", "VPN disconnected.");
            if (Find("wg-quick") == null || Find("pkexec") == null) throw new FileNotFoundException(MissingComponents);
            string id = Guid.NewGuid().ToString("N");
            string request = J.Stringify(J.Obj("id", id, "action", action, "scope", scope, "created", DateTime.UtcNow.ToString("o"), "profile", profile));
            return await Task.Run(delegate { return Elevated(id, request); });
        }

        // ---- NetworkManager (runs as the user; NetworkManager asks polkit when needed) ----
        private static bool NetworkManagerHas(string name)
        {
            try
            {
                string nmcli = Find("nmcli"); if (nmcli == null) return false;
                return Command(nmcli, new[] { "-t", "-f", "NAME", "connection", "show" }, null, 8000, false, 262144).Split('\n').Any(line => line.Trim() == name);
            }
            catch (Exception) { return false; }
        }
        private static void NetworkManagerRemove(string nmcli, string name)
        {
            try { Command(nmcli, new[] { "connection", "down", "id", name }, null, 20000, false); } catch (Exception) { }
            for (int i = 0; i < 8 && NetworkManagerHas(name); i++) Command(nmcli, new[] { "connection", "delete", "id", name }, null, 20000, true);
        }
        private static object NetworkManagerControl(string action, string scope, object profile)
        {
            string nmcli = Binary("nmcli"), name = Tunnel(scope);
            using (Lock(UserDirectory))
            {
                if (action == "disconnect")
                {
                    NetworkManagerRemove(nmcli, name);
                    return J.Obj("ok", true, "message", "VPN disconnected.");
                }
                if (Running(scope)) throw new InvalidOperationException("Disconnect the existing tunnel before reconnecting or changing region.");
                if (AnotherTunnel(scope)) throw new InvalidOperationException("Another eero VPN is active. Disconnect it before connecting this network's VPN.");
                if (NetworkManagerHas(name)) NetworkManagerRemove(nmcli, name);
                object result = null;
                string file = Path.Combine(UserDirectory, name + ".conf");
                VpnTunnelLifecycle.Start(delegate
                {
                    // nmcli names the connection and interface after the file; the file exists only for the import.
                    try
                    {
                        WritePrivate(file, VpnContracts.Configuration(profile));
                        Command(nmcli, new[] { "connection", "import", "--temporary", "type", "wireguard", "file", file }, null, 30000, true);
                    }
                    finally { if (File.Exists(file)) File.Delete(file); }
                    Command(nmcli, new[] { "connection", "modify", "--temporary", "id", name, "connection.autoconnect", "no", "ipv4.dns-priority", "-50", "ipv6.dns-priority", "-50" }, null, 20000, true);
                    Command(nmcli, new[] { "--wait", "30", "connection", "up", "id", name }, null, 45000, true);
                }, () => Running(scope) || NetworkManagerHas(name), () => Handshake(name), delegate { NetworkManagerRemove(nmcli, name); }, delegate
                { result = J.Obj("ok", true, "message", "Connected to Guardian. WireGuard handshake verified.", "verified_utc", DateTime.UtcNow.ToString("o")); }, 15, delegate { Thread.Sleep(1000); });
                return result;
            }
        }

        // A handshake is verified from wg's latest-handshakes when allowed (root). Otherwise any decrypted data received
        // on the interface proves the peer completed a handshake; a DNS query through the tunnel makes sure some arrives.
        private static bool Handshake(string name)
        {
            string wg = Find("wg");
            if (wg != null)
            {
                try
                {
                    foreach (string line in Command(wg, new[] { "show", name, "latest-handshakes" }, null, 8000, false).Split('\n'))
                    {
                        string[] parts = line.Trim().Split('\t'); long epoch;
                        if (parts.Length == 2 && Int64.TryParse(parts[1], out epoch) && epoch > 0 && Math.Abs((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds - epoch) < 180) return true;
                    }
                    return false;
                }
                catch (InvalidOperationException) { } // Not permitted for normal users, or the interface is still being created.
                catch (System.TimeoutException) { }
            }
            try
            {
                long received;
                if (Int64.TryParse(File.ReadAllText("/sys/class/net/" + name + "/statistics/rx_bytes").Trim(), out received) && received > 0) return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            Probe();
            return false;
        }
        // A DNS query for guardianapp.com to the tunnel's resolver; it carries no user data.
        private static void Probe()
        {
            try
            {
                byte[] query = { 0x45, 0x52, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0, 11, (byte)'g', (byte)'u', (byte)'a', (byte)'r', (byte)'d', (byte)'i', (byte)'a', (byte)'n', (byte)'a', (byte)'p', (byte)'p', 3, (byte)'c', (byte)'o', (byte)'m', 0, 0, 1, 0, 1 };
                using (var udp = new UdpClient(AddressFamily.InterNetwork)) udp.Send(query, query.Length, "1.1.1.1", 53);
            }
            catch (SocketException) { }
        }

        private static bool NotAuthorized(Exception e)
        {
            for (Exception x = e; x != null; x = x.InnerException)
                if (x.Message.IndexOf("Insufficient privileges", StringComparison.OrdinalIgnoreCase) >= 0 || x.Message.IndexOf("not authorized", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // ---- wg-quick through pkexec ----
        private static object Elevated(string id, string request)
        {
            string pkexec = Binary("pkexec"), self = Environment.ProcessPath;
            if (String.IsNullOrEmpty(self) || !Path.IsPathRooted(self) || !File.Exists(self)) throw new InvalidOperationException("The VPN helper could not be started.");
            var start = new ProcessStartInfo(pkexec) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(self);
            // Development builds run through the dotnet host; pass the app assembly to it.
            if (Path.GetFileNameWithoutExtension(self) == "dotnet") start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "eero-desktop.dll"));
            start.ArgumentList.Add("--vpn-control"); start.ArgumentList.Add(id);
            string output;
            using (var process = Process.Start(start))
            {
                process.StandardInput.Write(request); process.StandardInput.Close();
                Task<string> read = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
                // Leaves time for the password prompt and the 15-second handshake check.
                if (!process.WaitForExit(180000)) { try { process.Kill(true); } catch (Exception) { } throw new System.TimeoutException("The VPN helper did not respond."); }
                Task.WaitAll(read, error); output = read.Result;
            }
            string line = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith(ResultMarker, StringComparison.Ordinal));
            if (line == null) throw new InvalidOperationException("VPN permission was cancelled or the helper did not complete.");
            object result = J.Parse(line.Substring(ResultMarker.Length));
            if (!J.B(result, "ok")) throw new InvalidOperationException(J.S(result, "message"));
            return result;
        }
        private static void Reply(object result) { Console.Out.Write(ResultMarker + J.Stringify(result) + "\n"); Console.Out.Flush(); }

        // Runs only as root through pkexec after the user presses Connect/Disconnect and authenticates.
        public static int ElevatedControl(string id)
        {
            if (!Regex.IsMatch(id ?? "", "^[a-f0-9]{32}$")) return 2;
            if (!Environment.IsPrivilegedProcess) return 3;
            try
            {
                string text;
                using (var input = Console.OpenStandardInput())
                {
                    var buffer = new byte[32769]; int total = 0, count;
                    while (total < buffer.Length && (count = input.Read(buffer, total, buffer.Length - total)) > 0) total += count;
                    if (total > 32768) throw new InvalidDataException("Invalid VPN request size.");
                    text = Encoding.UTF8.GetString(buffer, 0, total); Array.Clear(buffer, 0, buffer.Length);
                }
                object request = J.Parse(text); text = null;
                if (J.S(request, "id") != id) throw new InvalidDataException("The VPN request does not match this helper.");
                DateTime created;
                if (!DateTime.TryParse(J.S(request, "created"), null, System.Globalization.DateTimeStyles.RoundtripKind, out created) || Math.Abs((DateTime.UtcNow - created.ToUniversalTime()).TotalMinutes) > 10) throw new InvalidOperationException("The VPN request expired. Try again.");
                string scope = J.S(request, "scope"), name = Tunnel(scope), action = J.S(request, "action");
                if (action != "connect" && action != "disconnect") throw new InvalidDataException("Unsupported VPN action.");
                SecureMachineDirectory();
                using (Lock(MachineDirectory))
                {
                    string exe = Binary("wg-quick"), path = Path.Combine(MachineDirectory, name + ".conf");
                    if (action == "disconnect")
                    {
                        if (Running(scope))
                        {
                            if (File.Exists(path)) Command(exe, new[] { "down", path }, null, 30000, true);
                            else Command(Binary("ip"), new[] { "link", "delete", "dev", name }, null, 12000, true);
                        }
                        if (File.Exists(path)) File.Delete(path);
                        Reply(J.Obj("ok", true, "message", "VPN disconnected.")); return 0;
                    }
                    if (Running(scope)) throw new InvalidOperationException("Disconnect the existing tunnel before reconnecting or changing region.");
                    if (AnotherTunnel(scope)) throw new InvalidOperationException("Another eero VPN is active. Disconnect it before connecting this network's VPN.");
                    string configuration = VpnContracts.Configuration(J.Get(request, "profile"));
                    if (Find("resolvconf") == null) throw new FileNotFoundException("wg-quick needs the resolvconf command to set the VPN's DNS servers. Install openresolv or systemd-resolved's resolvconf, or use NetworkManager.");
                    WritePrivate(path, configuration); configuration = null;
                    try
                    {
                        VpnTunnelLifecycle.Start(delegate { Command(exe, new[] { "up", path }, null, 45000, true); }, () => Running(scope), () => Handshake(name),
                            delegate { Command(exe, new[] { "down", path }, null, 30000, true); },
                            delegate { Reply(J.Obj("ok", true, "message", "Connected to Guardian. WireGuard handshake verified.", "verified_utc", DateTime.UtcNow.ToString("o"))); }, 15, delegate { Thread.Sleep(1000); });
                        return 0;
                    }
                    catch (VpnCleanupException) { throw; }
                    catch { if (File.Exists(path)) File.Delete(path); throw; }
                }
            }
            catch (Exception e)
            {
                try { Reply(J.Obj("ok", false, "message", MainWindow.Message(e))); } catch (Exception) { }
                return 1;
            }
        }
        private static void SecureMachineDirectory()
        {
            if (Directory.Exists(MachineDirectory) || File.Exists(MachineDirectory))
            {
                if ((File.GetAttributes(MachineDirectory) & FileAttributes.ReparsePoint) != 0) throw new IOException("The system VPN directory cannot be a link.");
            }
            else Directory.CreateDirectory(MachineDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(MachineDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
