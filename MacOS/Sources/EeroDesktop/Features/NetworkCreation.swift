import Foundation

// Translated from source/NetworkCreation.cs (Linux port: Linux/src/Features/NetworkCreation.cs).
// WiredNetworkSetup itself (the create/claim state machine) lives in Core.
extension MainWindow {
    /// Account-scoped wired setup state; MainWindow.clearPlatformState removes "wiredSetup" (Windows ClearData makes a new one).
    var wiredSetup: WiredNetworkSetup {
        get { stored("wiredSetup", WiredNetworkSetup()) }
        set { state["wiredSetup"] = newValue }
    }

    func createNetworkPage() {
        sub("Set up a brand-new eero network with a spare eero. Your current network keeps working.")
        group("Before you start",
            infoRow("You need", nil, "A new or reset eero, plugged into your modem with an Ethernet cable. The modem must hand out addresses automatically (DHCP)."),
            infoRow("Good to know", nil, "This hasn't been tested on every eero yet. If it fails, use the official eero app instead."))
        if wiredSetup.createdUrl != nil {
            group("Finish setup", settingRow("Add the gateway eero", "Your new network was created. This step links the eero to it.", "Network created", rowActionAsync("Continue", { [self] in try await claimCreatedGateway() })))
        } else if wiredSetup.creationUncertain {
            group("Finish setup", infoRow("Check your account first", nil, "The connection dropped while creating the network. Choose Refresh at the top right and look for the new network before trying again."))
        } else {
            group("Steps", settingRow("Create the network", "Enter the eero's serial number, read and accept eero's agreements, then choose a wifi name and password.", nil, rowAction("Start", { [self] in
                edit("Gateway for new network", [Field("serial", "Serial number of the unconfigured gateway")], { [self] vals in
                    let serial = try RuntimeContracts.serial(v(vals, "serial"))
                    if nodes.contains(where: { J.s($0, "serial").caseInsensitiveCompare(serial) == .orderedSame }) { throw AppError.argument("This eero belongs to the active network. Use a separate eero ready for setup.") }
                    if preview { throw AppError.invalidOperation("Sign in to create a network.") }
                    let agreements = try await api.send("GET", "/2.2/consents/network?serial=" + serial.escapedData)
                    afterEditor { [self] in
                        do { try networkCreationEditor(serial, J.list(agreements)) } catch { setNotice(MainWindow.message(error), true) }
                    }
                }, "Continue")
            })))
        }
        relatedPagesFor("Home/Add or replace eero/Find nearby eeros")
        page.append(text("Internet that needs a PPPoE login or static IP, cellular service and gateway replacement still need the official eero app.", 13, "#64738A"))
    }

    func networkCreationEditor(_ serial: String, _ agreements: [Any]) throws {
        var fields: [Field] = [Field("name", "New wifi name"), Field("password", "Wifi password", nil, "password"), Field("timezone", "Timezone", J.s(network, "timezone", "value")).help("For example America/New_York, America/Chicago or Europe/London."), Field("location", "Gateway location")]
        for i in 0..<agreements.count {
            let agreement = agreements[i]
            if J.s(agreement, "name").isEmpty || J.s(agreement, "localized_consent").isEmpty { throw AppError.invalidOperation("eero returned an incomplete agreement. Setup cannot continue.") }
            fields.append(Field("consent_\(i)", J.s(agreement, "localized_consent") + (J.b(agreement, "requires_consent") ? " (required)" : " (optional)"), false, "bool"))
            for (j, link) in J.list(J.get(agreement, "links")).enumerated() {
                let url = J.s(link, "link"); _ = try ServiceLinks.eero(url)
                // Windows uses an empty key for every link; link fields are never saved, and unique keys keep SwiftUI rows distinct.
                fields.append(Field("link_\(i)_\(j)", "Read agreement on eero.com", url, "link"))
            }
        }
        edit("Create new network", fields, { [self] vals in
            if wiredSetup.createdUrl != nil || wiredSetup.creationUncertain { throw AppError.invalidOperation("A creation request was already sent. Close this dialog and review its result.") }
            try MainWindow.validateName(v(vals, "location")); let consents = JObject()
            for i in 0..<agreements.count {
                let accepted = flag(vals, "consent_\(i)")
                if J.b(agreements[i], "requires_consent") && !accepted { throw AppError.argument("Review and accept each required agreement to continue.") }
                consents[J.s(agreements[i], "name")] = accepted
            }
            let body = try RuntimeContracts.createNetwork(v(vals, "name"), v(vals, "password"), v(vals, "timezone").trim(), consents)
            if !(await confirm("Create a new network named " + v(vals, "name") + " and add the gateway with serial " + serial + "? Your current network is separate.")) { return }
            do { try await wiredSetup.create(api, body, serial, v(vals, "location")) }
            catch { renderPage(); throw error }
            renderPage()
            afterEditor { [self] in runClaimCreatedGateway() }
        }, "Create network", "The gateway must be separate from your active network and already connected to internet by Ethernet.")
    }

    func runClaimCreatedGateway() {
        Task { @MainActor in await self.run({ [self] in try await claimCreatedGateway() }, "Adding new gateway…") }
    }

    func claimCreatedGateway() async throws {
        if preview || wiredSetup.createdUrl == nil { throw AppError.invalidOperation("Create a network first.") }
        // The created resource survives a failed claim; retrying never creates another network.
        let destination = try await wiredSetup.claim(api)
        do { try await loadAccount(); networkUrl = destination; try await loadNetwork(); currentPage = "Home"; showShell(); setNotice("Gateway addition accepted. Check the network status while setup finishes.") }
        catch { setNotice("Gateway addition was accepted. Refresh did not finish: " + MainWindow.message(error), true) }
    }
}
