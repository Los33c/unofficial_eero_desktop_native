using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EeroDesktop
{
    // Remembers, per network, which pages eero refused to load, so menus can warn before you open them again.
    // Stored in ~/.local/share/eero-desktop/availability.json; a page that loads successfully clears its entry.
    public static class FeatureAvailability
    {
        internal static string StorePath = Path.Combine(MainWindow.DataDirectory, "availability.json");
        private static Dictionary<string, string> entries;

        // Only answers that say "not for you / not here" are remembered; other failures (offline, server errors) are not.
        internal static string ReasonFor(int code)
        {
            if (code == 402) return "Needs a plan upgrade";
            if (code == 401 || code == 403) return "Not available on your account";
            if (code == 404) return "Not available on this network";
            return null;
        }
        private static string Key(string network, string route) { return (network ?? "account") + "|" + route; }
        private static void Load()
        {
            if (entries != null) return;
            entries = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(StorePath)) return;
                var map = J.Parse(File.ReadAllText(StorePath)) as IDictionary<string, object>;
                if (map != null) foreach (var pair in map) { string value = J.S(pair.Value); if (value.Length > 0) entries[pair.Key] = value; }
            }
            catch (Exception) { entries = new Dictionary<string, string>(); }
        }
        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath));
                File.WriteAllText(StorePath, J.Stringify(entries.ToDictionary(p => p.Key, p => (object)p.Value)));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        public static string Get(string network, string route) { Load(); string value; return entries.TryGetValue(Key(network, route), out value) ? value : null; }
        public static void Mark(string network, string route, string reason)
        {
            if (String.IsNullOrEmpty(reason)) return;
            Load(); string key = Key(network, route); string existing;
            if (entries.TryGetValue(key, out existing) && existing == reason) return;
            entries[key] = reason; Save();
        }
        public static void Clear(string network, string route) { Load(); if (entries.Remove(Key(network, route))) Save(); }
        internal static void Reset() { entries = null; }
    }
}
