using System;
using System.Linq;
using System.Threading.Tasks;

namespace EeroDesktop
{
    public static class BillingContracts
    {
        public static bool Return(Uri uri)
        {
            if (uri.UserInfo.Length > 0) return false;
            string[] pages = { "pluspurchasecomplete", "businesspurchasecomplete", "datapackpurchasecomplete" };
            if (uri.Scheme == "eero" && uri.Host == "jumpto") return pages.Contains(uri.AbsolutePath.Trim('/'));
            if (uri.Scheme == "https" && uri.Host == "app.eero.com" && uri.Port == 443) return pages.Any(p => uri.AbsolutePath == "/" + p || uri.AbsolutePath == "/" + p + "/index.html" || uri.AbsolutePath == "/jumpto/" + p);
            return false;
        }
    }
}
