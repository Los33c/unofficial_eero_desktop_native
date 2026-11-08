using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EeroDesktop
{
    public interface IStartupStore { string Read(); void Write(string command); }
    // Start-at-login uses an XDG autostart entry (~/.config/autostart/eero-desktop.desktop).
    public static class StartupRegistration
    {
        public static string Command(string executable)
        {
            if (String.IsNullOrWhiteSpace(executable) || executable.Any(c => c == '"' || c == '\\' || c == '`' || c == '$' || Char.IsControl(c)) || !Path.IsPathRooted(executable) || Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose an absolute Linux executable path for startup.");
            return "\"" + Path.GetFullPath(executable) + "\" --background";
        }
        public static void Apply(IStartupStore store, string executable, bool enabled, Action savePreferences)
        {
            string desired = enabled ? Command(executable) : null, previous = store.Read();
            store.Write(desired);
            try { savePreferences(); }
            catch { store.Write(previous); throw; }
        }
    }
    public sealed class NotificationJournal
    {
        private readonly string path;
        private readonly Dictionary<string, List<string>> seen = new Dictionary<string, List<string>>();
        private static readonly byte[] entropy = Encoding.UTF8.GetBytes("EeroDesktop.NotificationJournal.v1");
        public NotificationJournal(string file)
        {
            path = file;
            try
            {
                if (!File.Exists(file) || new FileInfo(file).Length > 16 * 1024 * 1024) return;
                byte[] clear = ProtectedData.Unprotect(File.ReadAllBytes(file), entropy, DataProtectionScope.CurrentUser);
                try
                {
                    var data = J.Parse(Encoding.UTF8.GetString(clear)) as IDictionary<string, object>;
                    if (data != null) foreach (var item in data.Take(64)) if (item.Key.Length == 64) seen[item.Key] = J.List(item.Value).Select(x => J.S(x)).Where(ValidId).Distinct().Take(256).ToList();
                }
                finally { Array.Clear(clear, 0, clear.Length); }
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (CryptographicException) { } catch (ArgumentException) { } catch (InvalidOperationException) { }
        }
        private static bool ValidId(string id) { return id.Length > 0 && id.Length <= 256 && !id.Any(Char.IsControl); }
        private static string Scope(string account, string network)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(account + "\n" + EeroApi.Resource(network).AbsoluteUri))).Replace("-", "");
        }
        public List<object> Observe(string account, string network, IEnumerable<object> newestFirst)
        {
            string scope = Scope(account, network); List<string> ids; bool seeded = seen.TryGetValue(scope, out ids);
            if (!seeded) { ids = new List<string>(); seen[scope] = ids; }
            var result = new List<object>();
            foreach (object item in newestFirst.Take(50).Reverse())
            {
                string id = J.First(item, "uuid", "id"); if (!ValidId(id) || ids.Contains(id)) continue;
                ids.Add(id); if (seeded) result.Add(item);
            }
            if (ids.Count > 256) ids.RemoveRange(0, ids.Count - 256);
            while (seen.Count > 64) seen.Remove(seen.Keys.First(k => k != scope));
            return result;
        }
        public void Save()
        {
            byte[] clear = Encoding.UTF8.GetBytes(J.Stringify(seen)), encrypted;
            try { encrypted = ProtectedData.Protect(clear, entropy, DataProtectionScope.CurrentUser); }
            finally { Array.Clear(clear, 0, clear.Length); }
            Directory.CreateDirectory(Path.GetDirectoryName(path)); string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, encrypted); if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
    }
}
