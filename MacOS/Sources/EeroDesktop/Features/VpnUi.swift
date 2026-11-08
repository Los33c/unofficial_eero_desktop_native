import AppKit
import Foundation

// Guardian VPN controls for this Mac (Windows: VpnUi.cs). The page shell (vpnPage) lives in RuntimeFeatures.
extension MainWindow {
    var vpnScope: String { get throws { try VpnContracts.scope(preview ? "preview-account" : J.first(account, "log_id", "url", "id", "email"), networkId) } }
    var vpnStore: SessionStore { get throws { SessionStore((MacVpn.userDirectory as NSString).appendingPathComponent(try vpnScope + ".dat")) } }
    func vpnState() throws -> JObject {
        if preview { return J.obj("nickname", "Mac", "region_label", "New York", "pet", "sample") }
        guard let value = try vpnStore.load(), !value.isEmpty else { return J.obj() }
        return (try J.parse(value) as? JObject) ?? J.obj()
    }
    func vpnControls() {
        guard let scope = try? vpnScope else {
            group("This Mac", infoRow("VPN connection", nil, "Sign in and select a network first.")); return
        }
        let state = (try? vpnState()) ?? J.obj()
        let registered = !J.s(state, "pet").isEmpty, installed = !preview && MacVpn.installed, located = !J.s(state, "region_label").isEmpty
        let status = preview ? "Disconnected · sample" : MacVpn.status(scope)
        let install = rowActionAsync(installed ? "Check again" : "How to install", { [unowned self] in
            if self.preview { throw AppError.invalidOperation("Sample mode does not install software.") }
            let availability = try await self.loadGuardianAvailability()
            if availability is ApiException || !(J.get(availability, "seats_available") is Bool) { throw AppError.invalidOperation(VpnContracts.eligibilityMessage(availability)) }
            self.wireGuardInstallHelp()
        })
        var app: Row? = nil
        if !preview && J.get(state, "profile") != nil {
            app = settingRow("WireGuard app (alternative)", "Adds this location to the WireGuard app from the Mac App Store. That tunnel is then turned on and off in the WireGuard app; eero Desktop does not control or verify it.",
                             MacVpn.wireGuardAppUrl == nil ? "Not installed" : "Installed",
                             rowActionAsync("Add to WireGuard app", { [unowned self] in try await self.openGuardianInWireGuardApp() }))
        }
        group("This Mac",
              settingRow("VPN connection", "Connect and Disconnect ask for an administrator password on this Mac. Quitting this app leaves the VPN running.", status,
                         rowActionAsync("Connect", { [unowned self] in try await self.connectGuardian() }), rowActionAsync("Disconnect", { [unowned self] in try await self.disconnectGuardian() }),
                         rowAction("Refresh", { [unowned self] in self.renderPage() })),
              settingRow("VPN location", "Where your traffic appears to come from.", J.s(state, "region_label").nullIfEmpty ?? "Not chosen", rowActionAsync("Change", { [unowned self] in try await self.chooseGuardianRegion() })),
              !J.s(state, "verified_utc").isEmpty ? infoRow("Last verified", J.s(state, "verified_utc"), "The last time the VPN tunnel confirmed it was working.") : nil,
              app)
        group("Set up in order",
              settingRow("1. Register this Mac", "Uses one of your plan's VPN device slots.", registered ? "✓ " + (J.s(state, "nickname").nullIfEmpty ?? "Registered") : "Not registered",
                         rowActionAsync(registered ? "Register again" : "Register", { [unowned self] in try await self.registerGuardian() })),
              settingRow("2. Install WireGuard tools", "The open-source WireGuard command-line tools create the VPN tunnel. Install them with Homebrew: " + MacVpn.installCommand + ".",
                         installed ? "✓ Installed" : preview ? "Sample" : "Not installed", install),
              settingRow("3. Choose a location, then Connect", "Pick a location above, then choose Connect.", located ? "✓ Done" : "Not done", rowActionAsync("Choose", { [unowned self] in try await self.chooseGuardianRegion() })))
        group("Devices", settingRow("Your VPN devices", "See and remove devices that use your plan's VPN slots.", nil, rowActionAsync("Manage", { [unowned self] in try await self.guardianDevices() })))
    }
    /// macOS has no signed WireGuard installer to download: explain both supported ways to get the tunnel.
    func wireGuardInstallHelp() {
        var p: [Block] = [text("Install WireGuard tools", 23, "#17263C", true),
                          text("eero Desktop runs the VPN with the open-source WireGuard tools (wg, wg-quick and wireguard-go). Install them with Homebrew (brew.sh), then return here and choose Check again.", 14),
                          card([text(MacVpn.installCommand, 15, "#17263C", true)], 16),
                          .buttons([localButton("Copy command", {
                              NSPasteboard.general.clearContents(); NSPasteboard.general.setString(MacVpn.installCommand, forType: .string)
                          }), localButton("Open Terminal", {
                              if let terminal = NSWorkspace.shared.urlForApplication(withBundleIdentifier: "com.apple.Terminal") { NSWorkspace.shared.openApplication(at: terminal, configuration: NSWorkspace.OpenConfiguration()) }
                          })]),
                          .spacer(8),
                          text("Prefer the WireGuard app?", 17, "#17263C", true),
                          text("You can use the free WireGuard app from the Mac App Store instead. After you choose a location, use Add to WireGuard app on this page. The WireGuard app then controls that tunnel; eero Desktop cannot connect, disconnect or verify it.", 14, "#64738A")]
        if MacVpn.installed { p.insert(.notice(icon: "checkmark.circle", text: "WireGuard tools are installed on this Mac.", tone: .good), at: 1) }
        dialog("Install WireGuard tools", p, 620, 560).showDialog()
    }
    func loadGuardianAvailability() async throws -> Any? {
        if preview { throw AppError.invalidOperation("Sign in to check Guardian VPN eligibility.") }
        let scope = try vpnScope
        var result: Any?
        featureData.removeValue(forKey: "guardian_availability")
        do { result = try await api.send("GET", "/2.2/partners/guardian/networks/" + networkId + "/availability") }
        catch let error as ApiException { if error.sessionExpired { throw error }; result = error }
        if scope == (try? vpnScope) { featureData["guardian_availability"] = result ?? JNull.shared }
        return result
    }
    func registerGuardian() async throws {
        let availability = try await loadGuardianAvailability()
        if !VpnContracts.canRegister(availability) { throw AppError.invalidOperation(VpnContracts.eligibilityMessage(availability)) }
        let machine = Host.current().localizedName ?? "Mac"
        edit("Register this Mac VPN device", [Field("nickname", "Device name", machine), Field("terms_link", "Read Guardian terms", "https://guardianapp.com/terms-and-conditions/", "link"), Field("agree", "I agree to Guardian's terms and privacy policy", false, "bool")], { [unowned self] values in
            if self.preview { throw AppError.invalidOperation("Sample mode does not register VPN devices.") }
            if !self.flag(values, "agree") { throw AppError.argument("Review and accept Guardian's terms before registering.") }
            let nickname = self.v(values, "nickname").trim(); try MainWindow.validateName(nickname)
            let state = try self.vpnState()
            if !J.s(state, "pet").isEmpty { throw AppError.invalidOperation("This network already has a saved registration on this Mac. Choose a location or use Manage VPN devices.") }
            if !(await self.confirm("Use an available Guardian VPN device slot for " + nickname + " on this network?")) { return }
            let result = try await self.api.send("POST", "/2.2/partners/guardian/networks/" + self.networkId + "/pet", J.obj("nickname", nickname))
            let pet = J.s(result, "guardian_pet"); if pet.isEmpty { throw AppError.invalidData("eero did not return a VPN device token. Refresh Manage VPN devices before registering again.") }
            do { try MacVpn.privateUserDirectory(); try self.vpnStore.save(J.stringify(J.obj("pet", pet, "uuid", J.s(result, "uuid"), "nickname", nickname, "expiration", J.s(result, "expiration")))) }
            catch { throw AppError.io("eero registered the device, but this Mac could not save it. Check Manage VPN devices and remove the registration before trying again.") }
            self.setNotice("Mac registered. Install WireGuard tools, then choose a VPN location."); self.renderPage()
        }, "Register device", "Registration uses one of your subscription's VPN device slots. No connection is started here.")
    }
    func chooseGuardianRegion() async throws {
        if preview { throw AppError.invalidOperation("Sign in to choose a Guardian VPN location.") }
        let regions = try await GuardianApi().regions()
        var choices: [String: String] = [:]
        for region in J.list(regions) {
            let name = J.s(region, "name"), label = J.first(region, "name-pretty", "name"); if name.isEmpty { continue }
            var key = label + " · " + J.s(region, "country-iso-code"); if choices[key] != nil { key += " · " + name }; choices[key] = name
        }
        if choices.isEmpty { throw AppError.invalidOperation("Guardian did not return any VPN locations.") }
        let keys = choices.keys.sorted { $0.compare($1, options: [.caseInsensitive]) == .orderedAscending }
        edit("VPN location", [Field("region", "Location", keys[0], "choice", choices: keys)], { [unowned self] values in
            if MacVpn.running(try self.vpnScope) { throw AppError.invalidOperation("Disconnect VPN before changing its location.") }
            let state = try self.vpnState(); let pet = J.s(state, "pet"); if pet.isEmpty { throw AppError.invalidOperation("Register this Mac first.") }
            let selected = self.v(values, "region")
            guard let region = choices[selected] else { throw AppError.argument("Choose a VPN location.") }
            var keys = try MacVpn.keyPair()
            defer { keys = [] }
            state["profile"] = try await GuardianApi().createProfile(pet, region, keys[0], keys[1])
            state["region"] = region; state["region_label"] = selected; state.remove("verified_utc")
            try MacVpn.privateUserDirectory(); try self.vpnStore.save(J.stringify(state))
            self.renderPage(); self.setNotice("VPN location saved. Choose Connect to establish the tunnel on this Mac.")
        }, "Use location")
    }
    func connectGuardian() async throws {
        if preview { throw AppError.invalidOperation("Sample mode does not connect a VPN.") }
        let state = try vpnState(); let profile = J.get(state, "profile")
        if profile == nil { throw AppError.invalidOperation("Register this Mac and choose a location first.") }
        _ = try MacVpn.toolDirectory()
        if !(await confirm("Connect this Mac to Guardian through " + J.s(state, "region_label") + "? macOS will ask for an administrator password to start the tunnel. Full-tunnel protection can block local network access while connected.")) { return }
        let result = try await MacVpn.control("connect", try vpnScope, profile)
        state["verified_utc"] = J.s(result, "verified_utc"); try vpnStore.save(J.stringify(state)); renderPage(); setNotice(J.s(result, "message"))
    }
    func disconnectGuardian() async throws {
        if preview { throw AppError.invalidOperation("Sample mode does not change a VPN.") }
        if !MacVpn.running(try vpnScope) { setNotice("This network's VPN on this Mac is already disconnected."); return }
        let result = try await MacVpn.control("disconnect", try vpnScope); renderPage(); setNotice(J.s(result, "message"))
    }
    func openGuardianInWireGuardApp() async throws {
        if preview { throw AppError.invalidOperation("Sample mode does not export VPN settings.") }
        let state = try vpnState(); guard let profile = J.get(state, "profile") else { throw AppError.invalidOperation("Register this Mac and choose a location first.") }
        if MacVpn.wireGuardAppUrl == nil {
            if await confirm("The WireGuard app is not installed. Open the Mac App Store page for WireGuard?") { openExternal("macappstore://apps.apple.com/app/id1451685025") }
            return
        }
        if !(await confirm("Add " + J.s(state, "region_label") + " to the WireGuard app? The WireGuard app will then manage this tunnel: turn it on and off there. eero Desktop cannot control or verify a tunnel the WireGuard app manages, and its private key will be stored by the WireGuard app.")) { return }
        try await MacVpn.openInWireGuardApp(try vpnScope, profile)
        setNotice("Opened in the WireGuard app. Choose Allow there to add the tunnel, then turn it on in WireGuard.")
    }
    func guardianDevices() async throws {
        if preview { info("VPN devices", J.obj("device", "Sample Mac", "status", "Registered")); return }
        var credentials: Any? = try await api.send("POST", "/2.2/partners/guardian")
        let devices = try await GuardianApi().devices(credentials)
        var p: [Block] = [text("Devices using your Guardian subscription", 23, "#17263C", true)]
        var handle: DialogHandle? = nil
        for device in J.list(devices) {
            let id = J.s(device, "ep-grd-device-uuid"); if id.isEmpty { continue }
            let name = J.s(device, "ep-grd-device-nickname").nullIfEmpty ?? "VPN device"
            p.append(card([text(name, 19, "#17263C", true), text("Activated: " + J.s(device, "ep-grd-device-created-at"), 13, "#64738A"),
                           .buttons([danger(button("Remove this VPN device", { [unowned self] in
                               if !(await self.confirm("Remove " + name + " from your Guardian subscription? Its VPN access will stop.")) { return }
                               let state = try self.vpnState(); let local = J.s(state, "uuid") == id
                               if try local && MacVpn.running(self.vpnScope) { throw AppError.invalidOperation("Disconnect this Mac's VPN before removing its registration.") }
                               _ = try await GuardianApi().removeDevice(credentials, id)
                               if local { try self.vpnStore.clear() }
                               handle?.close(); self.setNotice("VPN device removed."); self.renderPage()
                           }))])]))
        }
        if p.count == 1 { p.append(text("No devices were returned.", 14)) }
        handle = dialog("VPN devices · eero Desktop", p, 720, 690)
        handle?.onClosed = { credentials = nil }
        handle?.showDialog()
    }
}
