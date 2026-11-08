using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EeroDesktop
{
    public static class SignInContracts
    {
        public static bool SsoReturn(Uri uri)
        { return uri.UserInfo.Length == 0 && (uri.Scheme == "https" && uri.Host == "app.eero.com" && uri.Port == 443 && uri.AbsolutePath == "/auth/index.html" || uri.Scheme == "eero" && uri.Host == "jumpto" && uri.AbsolutePath == "/auth"); }
        public static Dictionary<string, string> Parameters(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string item in query.TrimStart('?', '#').Split('&'))
            {
                if (item.Length == 0) continue;
                string[] pair = item.Split(new[] { '=' }, 2); string key = Uri.UnescapeDataString(pair[0].Replace("+", " "));
                if (result.ContainsKey(key)) throw new InvalidDataException("The sign-in response contains duplicate parameters.");
                result[key] = pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace("+", " ")) : "";
            }
            return result;
        }
        public static string ProviderUrl(string id)
        {
            if (!Regex.IsMatch(id ?? "", "^[a-zA-Z0-9_-]{1,128}$")) throw new ArgumentException("The provider identifier is invalid.");
            return "https://sso.eero.com/?idp=" + Uri.EscapeDataString(id) + "&redirect=" + Uri.EscapeDataString("https://app.eero.com/auth/index.html") + "&clientID=28971b56-0016-4416-bef8-689caac23011";
        }
    }
    public sealed class SignInService : IDisposable
    {
        private readonly HttpClient http;
        public SignInService(HttpMessageHandler handler = null)
        { http = new HttpClient(handler ?? new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }); http.Timeout = TimeSpan.FromSeconds(30); }
        public async Task<object> Providers()
        {
            using (var response = await http.GetAsync("https://sso.eero.com/api/v1/providers?assetType=mdpi").ConfigureAwait(false))
            { response.EnsureSuccessStatusCode(); string value = await response.Content.ReadAsStringAsync().ConfigureAwait(false); if (value.Length > 1024 * 1024) throw new InvalidDataException("The provider list exceeded its size limit."); return J.Parse(value); }
        }
        public void Dispose() { http.Dispose(); }
    }
}
