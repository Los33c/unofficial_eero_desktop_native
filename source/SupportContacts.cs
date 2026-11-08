using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace EeroDesktop
{
    public sealed partial class MainWindow
    {
        private void SupportContactsPage(object data)
        {
            Sub("Ways to reach a person about your network. These come from eero or your internet provider.");
            var rows = new System.Collections.Generic.List<UIElement>();
            foreach (object contact in J.List(J.Get(data, "contacts")))
            {
                string type = J.S(contact, "type"), content = J.S(contact, "content"), title = J.S(contact, "title").NullIfEmpty() ?? "Support";
                if ((type == "CHAT" || type == "HELP_LINK") && ServiceLinks.ProviderNavigation(content))
                    rows.Add(SettingRow(title, type == "CHAT" ? "Chat with a support agent. Opens in its own window." : "Help page from " + new Uri(content).Host + ".", null, RowAction(type == "CHAT" ? "Start chat ↗" : "Open ↗", delegate
                    { new ServiceBrowserWindow(title, content, null, null, false, null, null, true) { Owner = this }.Show(); })));
                else
                    rows.Add(SettingRow(title, type == "PHONE" ? "Call this number." : type == "EMAIL" ? "Send an email to this address." : null, content.NullIfEmpty() ?? "Not provided",
                        content.Length > 0 ? RowAction("Copy", delegate { Clipboard.SetText(content); SetNotice(title + " copied."); }) : null));
            }
            if (rows.Count == 0) rows.Add(InfoRow("No contacts for your account", null, data == null ? "Choose Refresh at the top right to try again." : "Use eero's website below for contact options."));
            Group(null, rows.ToArray());
            Group("Other help",
                SettingRow("eero support website", "Help articles and general contact options.", null, RowAction("Open ↗", delegate { OpenServiceWindow("eero support", "https://www.eero.com/support"); })),
                SettingRow("Report a problem", "Send eero details and a screenshot instead of calling.", null, RowAction("Write report", delegate { Navigate("Help/Report an issue"); })));
        }
    }
}
