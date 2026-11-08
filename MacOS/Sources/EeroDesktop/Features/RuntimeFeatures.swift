import AppKit
import Foundation
import SwiftUI

// Translated from source/RuntimeFeatures.cs (Linux port: Linux/src/Features/RuntimeFeatures.cs).
// RuntimeContracts (the request bodies at the top of the Windows file) live in Core.
extension MainWindow {
    func incomingAccessPage(_ data: Any?) {
        sub("Networks that invited you to help manage them.")
        let invites = entries(data, "promotions"); var rows: [Row] = []
        for raw in invites {
            let invite: Any? = raw
            let id = J.s(invite, "invite_id"), net = J.s(invite, "network_id"), role = J.s(invite, "invite_role")
            let name = J.s(invite, "network_name").nullIfEmpty ?? "A network"
            var actions: [ButtonSpec] = []
            if !id.isEmpty && !net.isEmpty {
                for decision in [true, false] {
                    let accept = decision
                    let b = rowActionAsync(accept ? "Accept" : "Decline", { [self] in
                        if !(await confirm((accept ? "Accept" : "Decline") + " this access invitation for " + name + "?")) { return }
                        try await change("POST", "/2.2/networks/" + (try ExtendedContracts.identifier(net)) + "/invites/response", try RuntimeContracts.invitationResponse(id, accept), false, accept ? "Access invitation accepted." : "Access invitation declined.")
                    })
                    if accept { accent(b) }
                    if accept { compactAction(b) }
                    actions.append(b)
                }
            }
            rows.append(settingRow(name, role == "admin" ? "Administrator: can change every setting on this network." : "Access: " + MainWindow.friendly(role), nil, actions: actions))
        }
        if rows.isEmpty { rows.append(infoRow("No invitations", nil, "When someone invites you to manage their network, it shows up here.")) }
        group(nil, rows: rows)
    }

    func automaticSetupPage(_ data: Any?) {
        sub("New eeros you plug in can join this network by themselves. eero finds them through its cloud service.")
        let current = J.s(network, "auto_setup_mode")
        let modes = ["DEFAULT", "ZTS", "LTS", "OFF"], labels = ["Provider default", "ZTS policy", "LTS policy", "Off"]
        let index = modes.firstIndex(of: current)
        group(nil, settingRow("Automatic setup", "Provider default suits most homes. ZTS and LTS are eero's automatic setup policies; use one only if your provider asks. Off means you add every eero yourself.", index.map { labels[$0] } ?? "Not reported", rowAction("Change", { [self] in
            edit("Automatic setup", [Field("mode", "Mode", index.map { labels[$0] } ?? labels[0], "choice", choices: labels).help("If you're unsure, choose Provider default.")], { [self] vals in
                guard let chosenIndex = labels.firstIndex(of: v(vals, "mode")) else { throw AppError.argument("Choose a supported automatic setup mode.") }
                let chosen = modes[chosenIndex]
                if !(await confirm("Change automatic setup to " + v(vals, "mode") + "?")) { return }
                try await change("PUT", try featureUrl("auto_setup_mode"), try RuntimeContracts.autoSetup(chosen), false, "Automatic setup set to " + v(vals, "mode") + ".")
            })
        })))
        var rows: [Row] = []
        for raw in entries(data, "eeros") {
            let eero: Any? = raw; let serial = J.s(eero, "serial")
            let reason = J.first(J.get(eero, "ineligible_for_zts"), "reason", "message")
            rows.append(settingRow(J.first(eero, "model", "serial").nullIfEmpty ?? "eero", "Serial " + serial + (!reason.isEmpty ? " · Can't join automatically: " + reason : ""), nil,
                !serial.isEmpty ? rowAction("Add", { [self] in addDiscoveredEero(serial) }) : nil))
        }
        if rows.isEmpty { rows.append(infoRow("No new eeros found", nil, "Plug in the new eero and wait a few minutes. You can also search with Bluetooth under Add or replace eero.")) }
        group("eeros waiting to join", rows: rows)
    }

    func addDiscoveredEero(_ serial: String) {
        edit("Add discovered eero", [Field("serial", "Serial number", serial), Field("location", "Location")], { [self] vals in
            let validated = try RuntimeContracts.serial(v(vals, "serial")); try MainWindow.validateName(v(vals, "location"))
            if !(await confirm("Add eero " + validated + " to the selected network?")) { return }
            try await change("POST", "/2.2/eeros?requireSession=true", J.obj("serial", validated, "location", v(vals, "location").trim(), "network", networkUrl), true, "eero addition requested.")
        }, "Add eero")
    }

    func accessoriesPage(_ status: Any?) {
        sub("Add-on hardware such as cellular backup and phone adapters connected to your eeros.")
        let accessories = J.list(J.get(network, "accessories"))
        for raw in accessories {
            let accessory: Any? = raw; let serial = J.first(accessory, "dsn", "serial"), eeroId = J.s(accessory, "eero_id")
            let host = nodes.first { (J.s($0, "url").trimEnd("/").split("/").last ?? "") == eeroId }
            let state = J.b(accessory, "joined") ? "Connected" : J.b(accessory, "registered") ? "Registered, not connected" : "Not set up"
            let header = Row.content([.view(FeatureImageBeside(image: hardwareImage(accessory, 64), size: 64, spacing: 20, content: [
                text(J.first(accessory, "name", "model"), 18, "#17263C", true),
                text(J.s(accessory, "model") + (host != nil ? " · Connected to " + J.first(host, "location", "model") : ""), 12, "#64738A")]))])
            group(nil, header,
                infoRow("Status", state),
                !serial.isEmpty ? settingRow("Serial number", nil, serial, rowActionAsync("Details", { [self] in info("Accessory", try await read(try featureUrl("accessories/" + (try RuntimeContracts.serial(serial)).escapedData))) })) : nil,
                !eeroId.isEmpty ? settingRow("Phone lines", "Phone numbers linked to this accessory, if it's a phone adapter.", nil, rowActionAsync("View", { [self] in info("Phone lines", try await read(try featureUrl("sip_config_associations/" + (try ExtendedContracts.identifier(eeroId))))) })) : nil,
                !serial.isEmpty ? settingRow("Remove accessory", "Its cellular backup or phone service may stop working.", nil, rowAction("Remove", { [self] in
                    edit("Remove accessory", [Field("serial", "Type the serial number to confirm").help("Serial: " + serial)], { [self] vals in
                        if v(vals, "serial").trim() != serial { throw AppError.argument("The accessory serial number does not match.") }
                        if !(await confirm("Remove this accessory? Its cellular backup or phone service may stop working.")) { return }
                        try await change("DELETE", try featureUrl("accessories/" + (try RuntimeContracts.serial(serial)).escapedData), nil, false, "Accessory removed.")
                    }, "Remove accessory")
                }, true)) : nil)
        }
        if accessories.isEmpty { group(nil, infoRow("No accessories", nil, "No add-on hardware is registered on this network.")) }
        let cellular = J.get(network, "cellular_backup"); if cellular != nil { addGroupHeading("Cellular backup"); objectCards(cellular) }
        technicalDetails(status)
        page.append(text("Activating new cellular service or pairing new accessories still happens through your provider's setup.", 13, "#64738A"))
    }

    func placementPage() {
        sub("Check whether each eero is in a good spot. Run a test, wait a minute, then view the results.")
        var rows: [Row] = []
        for raw in nodes {
            let node: Any? = raw; let serial = J.s(node, "serial"); if serial.isEmpty { continue }
            // Windows lets an invalid serial escape the page render; here that eero is left out instead.
            guard let validSerial = try? RuntimeContracts.serial(serial) else { continue }
            let url = "/2.3/networks/" + networkId + "/placement_test/" + validSerial.escapedData
            rows.append(settingRow(J.first(node, "location", "model"), (J.b(node, "gateway") ? "Gateway · " : "") + J.s(node, "model"), nil,
                rowActionAsync("Run test", { [self] in
                    if !(await confirm("Run a placement test for " + J.first(node, "location", "model") + "?")) { return }
                    try await change("POST", url, nil, false, "Placement test started. Choose View results in a minute.")
                }),
                rowActionAsync("View results", { [self] in info("Placement results", try await read(url)) })))
        }
        if rows.isEmpty { rows.append(infoRow("No eeros to test", nil, "eero didn't return serial numbers for your eeros.")) }
        group(nil, rows: rows)
    }

    func vpnPage() {
        sub("A VPN from Guardian that protects this Mac on any network, such as public wifi. It must be included with your eero plan.")
        let availability = featureData["guardian_availability"]
        group("Eligibility", settingRow("VPN included with your plan?", VpnContracts.eligibilityMessage(availability), nil,
            rowActionAsync("Check", { [self] in
                let result = try await loadGuardianAvailability(); renderPage()
                setNotice(VpnContracts.eligibilityMessage(result), result is ApiException)
            })))
        vpnControls()
        group("Guardian account & help",
            settingRow("Guardian sign-in details", "Your login for Guardian's own app or website.", nil, rowActionAsync("Show", { [self] in try await guardianCredentials() })),
            settingRow("Guardian account", "Activate or manage your Guardian account on their website.", nil, rowAction("Open ↗", { [self] in try openServiceWindow("Guardian VPN", "https://guardianapp.com/login?connect-api-env=wifi-company-prod") })),
            settingRow("Guardian's own apps", "Prefer Guardian's own app on your other devices? Get it from Guardian's website.", nil, rowAction("Open ↗", { [self] in try official("https://guardianapp.com/") })),
            settingRow("Setup instructions", "eero's step-by-step guide to the VPN.", nil, rowAction("Open ↗", { [self] in try openServiceWindow("Guardian setup", "https://eero.com/support/articles/how-do-i-set-up-vpn-powered-by-guardian") })))
    }

    func subscriptionWebPage() {
        sub("These open eero's payment service in a separate window, signed in for you. Check prices and terms there before you buy or cancel anything.")
        group("Bought through eero",
            settingRow("Subscription", "Change, renew or cancel your eero plan and payment method.", nil, rowActionAsync("Manage ↗", { [self] in try await openPaymentService("MANAGE_SUBSCRIPTION") })),
            settingRow("Purchases", "Receipts and things you've bought.", nil, rowActionAsync("View ↗", { [self] in try await openPaymentService("MANAGE_PURCHASES") })),
            settingRow("Add-ons", "Extras you can add to your plan.", nil, rowActionAsync("Browse ↗", { [self] in try await openPaymentService("MOBILE_ADDON_PAYMENT") })))
        group("Bought somewhere else",
            settingRow("Google Play subscription", "Opens Google Play in your browser. Sign in with the Google account that made the purchase.", nil, rowAction("Open ↗", { [self] in
                openExternal("https://play.google.com/store/account/subscriptions")
            })))
    }

    func businessInsightsPage() {
        sub("eero's website for businesses to manage their networks, guest portals and staff access. You need a business account with permission to use it.")
        group(nil, settingRow("eero Insight", "Opens in its own window, already signed in to this network.", nil, rowActionAsync("Open ↗", { [self] in
            if preview { throw AppError.invalidOperation("Sign in to open eero Insight.") }
            let temporary = try await api.send("GET", "/2.2/login/temporary")
            try openServiceWindow("eero Insight", "https://insight.eero.com/login/mobile?next=/networks/" + networkId, try ServiceLinks.tokenHeader(J.s(temporary, "temporary_token")))
        })))
        relatedPagesFor("Settings/Business guest wifi")
    }
}
