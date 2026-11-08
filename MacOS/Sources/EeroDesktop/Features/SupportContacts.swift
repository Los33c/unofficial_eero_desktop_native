import AppKit
import Foundation

// Translated from SupportContacts.cs.
extension MainWindow {
    func supportContactsPage(_ data: Any?) {
        sub("Ways to reach a person about your network. These come from eero or your internet provider.")
        var rows: [Row] = []
        for contact in J.list(J.get(data, "contacts")) {
            let type = J.s(contact, "type"), content = J.s(contact, "content"), title = J.s(contact, "title").nullIfEmpty ?? "Support"
            if (type == "CHAT" || type == "HELP_LINK") && ServiceLinks.providerNavigation(content) {
                rows.append(settingRow(title, type == "CHAT" ? "Chat with a support agent. Opens in its own window." : "Help page from " + (URL(string: content)?.host ?? "") + ".", nil,
                    rowAction(type == "CHAT" ? "Start chat ↗" : "Open ↗", { [unowned self] in
                        ServiceBrowserWindow.open(self, title, content, nil, nil, false, nil, nil, true)
                    })))
            } else {
                rows.append(settingRow(title, type == "PHONE" ? "Call this number." : type == "EMAIL" ? "Send an email to this address." : nil, content.nullIfEmpty ?? "Not provided",
                    !content.isEmpty ? rowAction("Copy", { [unowned self] in
                        NSPasteboard.general.clearContents(); NSPasteboard.general.setString(content, forType: .string); self.setNotice(title + " copied.")
                    }) : nil))
            }
        }
        if rows.isEmpty { rows.append(infoRow("No contacts for your account", nil, data == nil || data is JNull ? "Choose Refresh at the top right to try again." : "Use eero's website below for contact options.")) }
        group(nil, rows: rows)
        group("Other help",
              settingRow("eero support website", "Help articles and general contact options.", nil, rowAction("Open ↗", { [unowned self] in try self.openServiceWindow("eero support", "https://www.eero.com/support") })),
              settingRow("Report a problem", "Send eero details and a screenshot instead of calling.", nil, rowAction("Write report", { [unowned self] in self.navigate("Help/Report an issue") })))
    }
}
