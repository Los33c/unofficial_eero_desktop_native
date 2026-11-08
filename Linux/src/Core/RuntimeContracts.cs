using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EeroDesktop
{
    public static class RuntimeContracts
    {
        public static string Serial(string value)
        {
            string serial = (value ?? "").Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(serial, "^[A-Za-z0-9-]{8,40}$")) throw new ArgumentException("Enter the serial number printed on the eero or accessory.");
            return serial;
        }
        public static object AdminInvite() { return J.Obj("invite_role", "admin"); }
        public static object InvitationResponse(string inviteId, bool accept) { return J.Obj("invite_id", ExtendedContracts.Identifier(inviteId), "accept", accept); }
        public static object AutoSetup(string mode)
        {
            if (!new[] { "OFF", "ZTS", "LTS", "DEFAULT" }.Contains(mode)) throw new ArgumentException("Choose a supported automatic setup mode.");
            return J.Obj("mode", mode);
        }
        public static object CreateNetwork(string name, string password, string timezone, IDictionary<string, object> consents)
        {
            MainWindow.ValidateSsid(name); MainWindow.ValidatePassword(password);
            if (!System.Text.RegularExpressions.Regex.IsMatch(timezone ?? "", "^[A-Za-z_+-]+(?:/[A-Za-z_0-9+-]+){0,3}$")) throw new ArgumentException("Enter an IANA timezone such as America/Chicago or Europe/London.");
            return J.Obj("name", name.Trim(), "password", password, "timezone", J.Obj("value", timezone), "settings", J.Obj("consents", consents, "enabled_simple_setup_vendors", new int[0]));
        }
    }
}
