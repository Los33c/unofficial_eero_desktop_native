using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EeroDesktop
{
    public static class PortalUpload
    {
        public static async Task Send(object upload, byte[] data, string mime, HttpMessageHandler handler = null)
        {
            Uri url = ExtendedContracts.UploadUrl(J.S(upload, "signed_upload_url")); string key = J.S(upload, "kms_key_id");
            if (key.Length == 0 || key.Length > 2048 || key.Any(Char.IsControl)) throw new InvalidDataException("The upload encryption key is missing or invalid.");
            // A separate client never forwards the eero session cookie to storage.
            using (var http = new HttpClient(handler ?? new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }))
            using (var request = new HttpRequestMessage(HttpMethod.Put, url))
            {
                http.Timeout = TimeSpan.FromSeconds(120); request.Headers.Add("x-amz-server-side-encryption", "aws:kms"); request.Headers.Add("x-amz-server-side-encryption-aws-kms-key-id", key);
                request.Content = new ByteArrayContent(data); request.Content.Headers.ContentType = new MediaTypeHeaderValue(mime);
                using (var response = await http.SendAsync(request)) if (!response.IsSuccessStatusCode) throw new InvalidOperationException("The storage service rejected the upload (" + (int)response.StatusCode + "). No import was requested.");
            }
        }
    }
}
