using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EeroDesktop
{
    public static class EverydayContracts
    {
        public static object BackupOrder(IEnumerable<string> ids)
        {
            string[] all = ids.Select(ExtendedContracts.Identifier).ToArray();
            if (all.Length == 0 || all.Distinct().Count() != all.Length) throw new ArgumentException("Each saved backup network must appear exactly once.");
            return J.Obj("rearranged_ids", all);
        }
        public static object Symptom(string value)
        {
            if (!new[] { "connection_drops", "eeros_red", "internet_offline", "internet_slow" }.Contains(value)) throw new ArgumentException("Choose a supported connection problem.");
            return J.Obj("symptom", value);
        }
        public static string ProductId(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || value.Length > 200 || value.Any(c => Char.IsControl(c))) throw new ArgumentException("eero did not return a valid product identifier.");
            return value;
        }
        public static object Offers(IEnumerable<string> products)
        {
            string[] all = products.Select(ProductId).Distinct().ToArray();
            if (all.Length == 0 || all.Length > 50) throw new ArgumentException("Select between one and fifty available products.");
            return J.Obj("product_ids", all);
        }
        public static object Referral(string networkId, string productId) { return J.Obj("product_id", ProductId(productId), "network_id", ExtendedContracts.Identifier(networkId)); }
        public static Dictionary<string, string> GuardianLogin(object data)
        {
            string id = J.S(data, "guardian_id"), password = J.S(data, "guardian_secret");
            if (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(password) || id.Length > 4096 || password.Length > 8192) throw new InvalidOperationException("eero did not return complete Guardian sign-in details for this account.");
            return new Dictionary<string, string> { { "Guardian username", id }, { "Guardian password", password } };
        }
        public static Dictionary<string, string> ThreadCredentials(object data)
        {
            var values = new Dictionary<string, string>();
            foreach (string key in new[] { "active_operational_dataset", "commissioning_credential", "master_key" })
            {
                string value = J.S(data, key); if (value.Length > 0 && value.Length <= 8192) values[key.Replace('_', ' ')] = value;
            }
            if (values.Count == 0) throw new InvalidOperationException("This network did not return Thread credentials for your account.");
            return values;
        }
    }
}
