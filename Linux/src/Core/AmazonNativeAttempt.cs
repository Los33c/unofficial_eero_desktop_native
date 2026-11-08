using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EeroDesktop
{
    // Protocol constants, not SDK code. See the local APK verification notes.
    internal sealed class AmazonNativeAttempt : IDisposable
    {
        internal const string TokenEndpoint = "https://api.amazon.com/auth/token";
        private const string DeviceType = "A2W01JEFAJFTXI";
        private readonly HttpClient http;
        private readonly string verifier, state, client;
        private int consumed;
        internal AmazonNativeAttempt(HttpMessageHandler handler = null)
        {
            verifier = Random(); state = Random();
            string serial = Guid.NewGuid().ToString("N").ToUpperInvariant();
            client = String.Concat(Encoding.UTF8.GetBytes(serial + "#" + DeviceType).Select(b => b.ToString("x2")));
            http = new HttpClient(handler ?? new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
            http.Timeout = TimeSpan.FromSeconds(30); http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            http.DefaultRequestHeaders.UserAgent.ParseAdd("EeroDesktop/1.1.0 (Linux; unofficial)");
        }
        private static string Base64Url(byte[] bytes) { return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        private static string Random() { var bytes = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes); return Base64Url(bytes); }
        internal static string Challenge(string value) { using (var hash = SHA256.Create()) return Base64Url(hash.ComputeHash(Encoding.ASCII.GetBytes(value))); }
        internal string SignInUrl
        {
            get
            {
                var fields = new Dictionary<string, string> {
                    { "openid.assoc_handle", "amzn_eero_mobile_android_us" }, { "pageId", "eero_mobile_accounts" }, { "language", "en_US" },
                    { "openid.ns", "http://specs.openid.net/auth/2.0" }, { "openid.mode", "checkid_setup" },
                    { "openid.claimed_id", "http://specs.openid.net/auth/2.0/identifier_select" }, { "openid.identity", "http://specs.openid.net/auth/2.0/identifier_select" },
                    { "openid.ns.pape", "http://specs.openid.net/extensions/pape/1.0" }, { "openid.pape.max_auth_age", "0" },
                    { "openid.ns.oa2", "http://www.amazon.com/ap/ext/oauth/2" }, { "openid.oa2.response_type", "code" },
                    { "openid.oa2.scope", "device_auth_access" }, { "openid.oa2.client_id", "device:" + client },
                    { "openid.oa2.code_challenge", Challenge(verifier) }, { "openid.oa2.code_challenge_method", "S256" },
                    { "openid.return_to", "https://www.amazon.com/ap/maplanding?state=" + state }, { "accountStatusPolicy", "P1" }
                };
                return "https://www.amazon.com/ap/signin?" + String.Join("&", fields.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)).ToArray());
            }
        }
        internal static bool ReturnAddress(Uri uri)
        { return uri != null && uri.Scheme == "https" && uri.Host == "www.amazon.com" && uri.Port == 443 && uri.UserInfo.Length == 0 && uri.AbsolutePath == "/ap/maplanding" && uri.Fragment.Length == 0; }
        internal static bool Navigation(string value)
        {
            Uri uri; if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length > 0) return false;
            return new[] { "amazon.com", "amazon.co.uk", "amazon.ca", "amazon.de", "amazon.fr", "amazon.it", "amazon.es", "amazon.co.jp", "amazon.com.au", "amazon.in", "amazon.com.mx", "amazon.com.br" }
                .Any(host => uri.Host == host || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
        }
        internal object ExchangeBody(Uri returned)
        {
            if (!ReturnAddress(returned)) throw new InvalidDataException("Amazon returned to an unexpected address.");
            var query = SignInContracts.Parameters(returned.Query);
            string received, code;
            if (!query.TryGetValue("state", out received) || received != state) throw new InvalidDataException("The Amazon response did not match this sign-in attempt. Close this window and try again.");
            if (query.ContainsKey("error") || query.ContainsKey("openid.error")) throw new InvalidOperationException("Amazon sign-in was cancelled or rejected.");
            if (!query.TryGetValue("openid.oa2.authorization_code", out code) || code.Length == 0 || code.Length > 8192 || code.Any(c => c <= 32 || c >= 127)) throw new InvalidDataException("Amazon did not return a valid authorization code.");
            if (query.ContainsKey("openid.oa2.scope") && query["openid.oa2.scope"] != "device_auth_access") throw new InvalidDataException("Amazon returned an unexpected sign-in scope.");
            return J.Obj("app_name", "EeroDesktop", "app_version", "1.1.0", "source_token_type", "authorization_code", "source_token", code,
                "requested_token_type", "access_token", "code_verifier", verifier, "code_algorithm", "SHA-256", "client_id", client, "client_domain", "DeviceLegacy");
        }
        internal async Task<string> Exchange(Uri returned, CancellationToken cancellation)
        {
            object body = ExchangeBody(returned);
            if (Interlocked.Exchange(ref consumed, 1) != 0) throw new InvalidOperationException("This sign-in attempt has already been used. Close this window and start again.");
            using (var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint))
            {
                request.Headers.Add("x-amzn-identity-auth-domain", "api.amazon.com");
                request.Content = new StringContent(J.Stringify(body), Encoding.UTF8, "application/json");
                using (var response = await http.SendAsync(request, cancellation).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string category = "";
                        string failure = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (failure.Length < 65536)
                        {
                            try
                            {
                                string name = J.S(J.Parse(failure), "error").ToLowerInvariant();
                                if (new[] { "invalid_request", "invalid_grant", "invalid_client", "invalid_token", "unauthorized_client", "access_denied", "invalid_scope" }.Contains(name)) category = "." + name;
                            }
                            catch (ArgumentException) { }
                        }
                        throw new ApiException((int)response.StatusCode, "amazon.token.exchange" + category, false);
                    }
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (text.Length > 65536) throw new InvalidDataException("Amazon returned an oversized sign-in response.");
                    object result; try { result = J.Parse(text); } catch (ArgumentException) { throw new InvalidDataException("Amazon returned an unexpected sign-in response."); }
                    string token = J.S(result, "access_token");
                    if (!J.S(result, "token_type").Equals("bearer", StringComparison.OrdinalIgnoreCase) || token.Length == 0 || token.Length > 8192 || token.Any(c => c <= 32 || c >= 127 || c == ';' || c == ',')) throw new InvalidDataException("Amazon did not return a valid access token.");
                    cancellation.ThrowIfCancellationRequested(); return token;
                }
            }
        }
        public void Dispose() { http.Dispose(); }
    }
}
