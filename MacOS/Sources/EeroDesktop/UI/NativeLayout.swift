import AppKit
import SwiftUI

// MARK: - Page builders (Home, eero cards, Devices) — translated from NativeLayout.cs

extension MainWindow {
    static func routeIcon(_ route: String) -> String {
        let title = (route.split("/").last ?? route).lowercased()
        switch title {
        case "guest wifi network": return "\u{E8FA}"
        case "nickname & location": return "\u{E707}"
        case "dhcp & nat": return "\u{E839}"
        case "dns", "ipv6", "internet connection", "isp settings": return "\u{E774}"
        case "dynamic dns", "connected services", "reservations & port forwarding": return "\u{E71B}"
        case "upnp", "multi-wan": return "\u{E8AB}"
        case "power saving": return "\u{E945}"
        case "network flags": return "\u{E7C1}"
        case "user permissions": return "\u{E8D7}"
        case "troubleshooting", "guided diagnostics": return "\u{E90F}"
        case "trusted certificates": return "\u{E72E}"
        default: break
        }
        if title.contains("password") || title.contains("wifi") || title.contains("wireless") { return "\u{E701}" }
        if title.contains("profile") || title.contains("permissions") || title.contains("account") { return "\u{E77B}" }
        if title.contains("security") || title.contains("filter") || title.contains("protection") || title.contains("vpn") { return "\u{EA18}" }
        if title.contains("internet") || title.contains("wan") { return "\u{E774}" }
        if title.contains("activity") || title.contains("usage") || title.contains("analytics") { return "\u{E9D9}" }
        if title.contains("eero") || title.contains("device") || title.contains("setup") { return "\u{E950}" }
        if title.contains("notification") { return "\u{EA8F}" }
        if title.contains("appearance") { return "\u{E790}" }
        if title.contains("update") { return "\u{E895}" }
        if title.contains("help") || title.contains("support") || title.contains("troubleshoot") { return "\u{E897}" }
        if title.contains("about") { return "\u{E946}" }
        if title.contains("chat") { return "\u{E8F2}" }
        if title.contains("dns") || title.contains("dhcp") || title.contains("network") { return "\u{E968}" }
        return "\u{E713}"
    }

    func updateNavigation() {
        let selected = currentPage.hasPrefix("Home/Profiles") ? "Home/Profiles" : currentPage.hasPrefix("Home/Security & privacy") ? "Home/Security & privacy" : FeatureCatalog.root(currentPage)
        if navigationSelection != selected { navigationSelection = selected }
    }

    func pageHeading() {
        if currentPage.contains("/") {
            let parts = currentPage.split("/"); var route = ""; var crumbs: [ButtonSpec] = []
            for i in 0..<(parts.count - 1) {
                route += (i == 0 ? "" : "/") + parts[i]; let target = route
                crumbs.append(quiet(localButton(parts[i] + "  ›", { [unowned self] in self.navigate(target) })))
            }
            page.append(.buttons(crumbs))
        }
        var heading = currentPage.split("/").last ?? currentPage
        if heading == "Details" && selectedNode != nil { heading = J.first(selectedNode, "location", "model") }
        if heading == "Profile" && selectedProfile != nil { heading = J.s(selectedProfile, "name") }
        page.append(.padded([.text(TextSpec(text: heading, size: 28, bold: true))], EdgeInsets(top: 0, leading: 0, bottom: 6, trailing: 0)))
        availabilityNotice()
    }

    /// A clickable card row with an icon, a title, a caption and a chevron.
    func routeRow(_ title: String, _ subtitle: String?, _ route: String, _ action: (() -> Void)? = nil) -> Block {
        var badge: String? = nil, why: String? = nil
        if action == nil { (badge, why) = availabilityBadge(route) }
        return .rows([.link(icon: MainWindow.routeIcon(route), title: title, subtitle: subtitle, value: nil, badge: badge, tooltip: why.map { (badge ?? "") + ". " + $0 }, tile: false,
                            action: action ?? { [unowned self] in self.navigate(route) })])
    }
    static func isOnline(_ data: Any?) -> Bool {
        let status = J.s(data, "health", "internet", "status").nullIfEmpty ?? J.s(data, "status")
        return status == "connected" || status == "green" || status == "online"
    }
    func statusBadge(_ text: String, _ online: Bool) -> Block { .badge(text, online: online) }

    func nativeOverview() {
        page.append(text("Your network at a glance", 14, "#64738A"))
        var left: [Block] = [], right: [Block] = []
        let online = MainWindow.isOnline(network)
        var internet: [Block] = [.split(left: [.split(left: [glyph("\u{E774}", 28, "#274CDB")], right: []), text("Internet", 20, "#17263C", true)], right: [statusBadge(online ? "Online" : "Check connection", online)])]
        internet = [.split(left: [.view(HStack(spacing: 10) { Image(systemName: "globe").font(.system(size: 22)).foregroundColor(.accentColor); Text("Internet").font(.system(size: 20, weight: .semibold)) })], right: [statusBadge(online ? "Online" : "Check connection", online)])]
        internet.append(.divider)
        internet.append(.columns([[text("↓  Download", 12, "#64738A"), .text(TextSpec(text: speed("down"), size: 28, bold: true))], [text("↑  Upload", 12, "#64738A"), .text(TextSpec(text: speed("up"), size: 28, bold: true))]], ratios: [1, 1]))
        internet.append(text("Latest measured internet speed", 11, "#64738A"))
        internet.append(.buttons([button("Run speed test", { [unowned self] in try await self.startSpeedTest() }, true), section("Internet details", "Home/Internet")]))
        left.append(card(internet))
        var mesh: [Block] = [.split(left: [text("Your eeros", 18, "#17263C", true)], right: [text("\(nodes.count) on this network", 12, "#64738A")])]
        for node in nodes { mesh.append(compactNode(node)) }
        if nodes.isEmpty { mesh.append(text(loadErrors["eero nodes"] != nil ? "eero information could not be loaded. Refresh to try again." : "No eeros were returned.")) }
        left.append(card(mesh, 18))
        left.append(routeRow("Chat with eero", "Get help with your network", "Home/Chat"))
        let deviceError = loadErrors["Devices"] != nil
        right.append(card([text("Devices", 20, "#17263C", true),
                           .columns([[.text(TextSpec(text: deviceError ? "—" : "\(devices.filter { J.b($0, "connected") }.count)", size: 32, bold: true)), text("Online", 12, "#0D7959")],
                                     [.text(TextSpec(text: deviceError ? "—" : "\(devices.filter { !J.b($0, "connected") }.count)", size: 32, bold: true)), text("Offline", 12, "#64738A")]], ratios: [1, 1]),
                           .buttons([section("View all devices", "Devices")])]))
        // The wifi details people reach for most: name, password and a QR code for guests.
        right.append(card([.split(left: [text("Your wifi", 20, "#17263C", true)], right: [.buttons([section("Wifi settings", "Settings/Wifi name & password")])]),
                           text(J.s(network, "name").nullIfEmpty ?? "Wifi name not reported", 16),
                           .buttons([quiet(button("Share QR code", { [unowned self] in self.showWifiQr(J.s(self.network, "name"), try await self.wifiPassword(self.network, try self.netResource("password"))) })),
                                     quiet(button("Show password", { [unowned self] in self.showPassword(try await self.wifiPassword(self.network, try self.netResource("password"))) }))])]))
        var people: [Block] = [text("Profiles", 20, "#17263C", true)]
        let rows: [Row] = profiles.prefix(3).map { profile in
            .link(icon: MainWindow.routeIcon("Home/Profiles"), title: J.s(profile, "name"), subtitle: MainWindow.profileSummary(profile), value: nil, badge: nil, tooltip: nil, tile: false,
                  action: { [unowned self] in self.selectedProfile = profile; self.navigate("Home/Profiles/Profile") })
        }
        if profiles.isEmpty { people.append(text("Create profiles to manage devices and scheduled pauses.", 12, "#64738A")) } else { people.append(.rows(rows)) }
        people.append(.padded([.buttons([section("Manage profiles", "Home/Profiles")])], EdgeInsets(top: 6, leading: 0, bottom: 0, trailing: 0)))
        right.append(card(people))
        right.append(routeRow("Security & privacy", "Protection, content filters, and privacy", "Home/Security & privacy"))
        right.append(routeRow("Internet backup", "Keep your network connected", "Settings/Advanced networking/Internet backup"))
        page.append(.columns([left, right], ratios: [1.45, 1]))
        for key in loadErrorOrder { availability(key) }
    }

    func nativeNodeCard(_ node: Any?) -> Block {
        let online = MainWindow.isOnline(node)
        var stack: [Block] = [.split(left: [.split(left: [hardwareImage(node, 36)], right: []), text(J.first(node, "location", "model"), 15, "#17263C", true), text((J.b(node, "gateway") ? "Gateway · " : "") + J.s(node, "model"), 12, "#64738A")],
                                     right: [statusBadge(online ? "Online" : (J.s(node, "status").nullIfEmpty ?? "Unknown"), online)])]
        stack.append(.indent([text((J.first(node, "ip_address", "ip").nullIfEmpty ?? "IP unavailable") + "  ·  " + (J.s(node, "connected_clients_count").nullIfEmpty ?? "—") + " connected devices", 12, "#64738A")], 42))
        let restart = quiet(button("Restart", { [unowned self] in
            if !(await self.confirm("Restart " + J.first(node, "location", "model") + "? Connected devices may briefly lose internet access.")) { return }
            if self.preview { throw AppError.invalidOperation("Preview mode is read only.") }
            try await self.api.send("POST", J.s(node, "url") + "/reboot"); self.setNotice("Restart requested. Wait a few minutes, then refresh.")
        })); restart.help = "Restart this eero"
        stack.append(.indent([.buttons([
            quiet(button("View details", { [unowned self] in self.selectedNode = self.preview ? node : try await self.read(J.s(node, "url")); self.currentPage = "Home/eeros/Details"; self.renderPage() })),
            quiet(button("Rename", { [unowned self] in
                guard let name = await self.prompt("eero location", J.s(node, "location")) else { return }
                try MainWindow.validateName(name); try await self.change("PUT", J.s(node, "url"), J.obj("location", name.trim()), true, "eero location updated.")
            })), restart])], 42))
        return card(stack, 20)
    }

    func nativeDevicePage() {
        sub("Everything connected to your network. Select a device to see its details and change its settings."); availability("Devices")
        page.append(.view(DevicesWorkspace()))
    }
}

/// The Devices table with search, status filter and a detail panel.
struct DevicesWorkspace: View {
    @EnvironmentObject var model: MainWindow
    @State private var query = ""
    @State private var status = "All devices"
    @State private var selection: String?
    var body: some View {
        let all = model.devices.map { MainWindow.device($0) }
        let rows = all.filter { d in (status == "All devices" || d.status == status) && (query.trim().isEmpty || (d.name + " " + d.ip + " " + J.s(d.raw, "mac") + " " + J.s(d.raw, "manufacturer")).containsIgnoringCase(query.trim())) }
            .sorted { a, b in (a.status != "Online") == (b.status != "Online") ? a.name.localizedCompare(b.name) == .orderedAscending : a.status == "Online" }
        let keyed = rows.map { (key: MainWindow.deviceKey($0.raw), row: $0) }
        let selected = keyed.first { $0.key == selection }?.row ?? keyed.first?.row
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 12) {
                TextField("Search by name, IP, MAC or maker", text: $query).textFieldStyle(.roundedBorder).accessibilityLabel("Search devices")
                Picker("", selection: $status) { ForEach(["All devices", "Online", "Offline", "Paused", "Blocked"], id: \.self) { Text($0) } }.labelsHidden().frame(width: 160).accessibilityLabel("Show devices")
                SpecButton(spec: model.section("Blocked devices", "Devices/Blocked devices"))
            }
            HStack(alignment: .top, spacing: 20) {
                Table(keyed.map { DeviceTableRow(key: $0.key, row: $0.row) }, selection: Binding(get: { selection ?? keyed.first?.key }, set: { selection = $0 })) {
                    TableColumn("Name") { Text($0.row.name) }.width(min: 120, ideal: 220)
                    TableColumn("Status") { r in Text(r.row.status).foregroundColor(r.row.status == "Online" ? Tone.good.color : Tone.text.color) }.width(min: 70, ideal: 90)
                    TableColumn("IP address") { Text($0.row.ip) }.width(min: 100, ideal: 130)
                    TableColumn("Connection") { Text($0.row.connection) }.width(min: 90, ideal: 110)
                }
                .contextMenu(forSelectionType: String.self) { _ in } primaryAction: { keys in
                    if let k = keys.first, let r = keyed.first(where: { $0.key == k }) { model.deviceDialog(r.row.raw) }
                }
                .frame(minHeight: 440)
                VStack(alignment: .leading, spacing: 0) {
                    if let s = selected {
                        Text(s.name).font(.system(size: 20, weight: .semibold)).padding(.bottom, 6)
                        StatusBadgeView(text: s.status, online: s.status == "Online").padding(.bottom, 12)
                        BlocksView(blocks: model.devicePanel(s.raw, {}))
                    } else {
                        BlockView(block: .card([.glyph("\u{E772}", size: 32, tone: .muted), .text(TextSpec(text: "Device details", size: 18, bold: true)), .text(TextSpec(text: "Select a device to see its details and change its settings.", size: 13, tone: .muted))], padding: 20))
                    }
                }.frame(width: 380, alignment: .topLeading)
            }
            Text("\(rows.count)" + (rows.count == 1 ? " device" : " devices") + " · \(all.filter { J.b($0.raw, "connected") }.count) online" + (rows.isEmpty ? " · No devices match your search." : ""))
                .font(.system(size: 12)).foregroundColor(Tone.muted.color)
        }
    }
}
struct DeviceTableRow: Identifiable { let key: String; let row: MainWindow.DeviceRow; var id: String { key } }
extension MainWindow {
    static func deviceKey(_ d: Any?) -> String { J.first(d, "url", "mac", "nickname", "hostname") }
}

// MARK: - Window content

struct RootView: View {
    @EnvironmentObject var model: MainWindow
    var body: some View {
        Group {
            switch model.screen {
            case .login(let verify): LoginView(verify: verify)
            case .shell: ShellView()
            }
        }
        .frame(minWidth: 960, minHeight: 640)
        .background(WindowAccessor { model.window = $0 })
    }
}

struct LoginView: View {
    let verify: Bool
    @EnvironmentObject var model: MainWindow
    @State private var method = 0
    @State private var identifier = ""
    @State private var remember = true
    @State private var code = ""
    var body: some View {
        HStack(spacing: 0) {
            VStack(alignment: .leading, spacing: 0) {
                Spacer()
                Text("eero").font(.system(size: 66, weight: .light))
                Text("Desktop for Mac").font(.system(size: 13)).foregroundColor(.secondary).padding(.bottom, 42)
                Image(systemName: "wifi").font(.system(size: 64)).foregroundColor(.accentColor).padding(.bottom, 36)
                Text("Your wifi.\nFrom your desktop.").font(.system(size: 30, weight: .semibold)).padding(.bottom, 8)
                Text("A clear view of your network, devices, and profiles.").font(.system(size: 15)).foregroundColor(.secondary).padding(.bottom, 8)
                Text("Independent desktop client\nNot affiliated with Amazon or eero.").font(.system(size: 11)).foregroundColor(.secondary)
                Spacer()
            }
            .padding(48).frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
            .background(Color(nsColor: .underPageBackgroundColor))
            .frame(width: 400)
            ScrollView {
                VStack(alignment: .leading, spacing: 8) {
                    Text(verify ? "Check your messages" : "Welcome home").font(.system(size: 32, weight: .semibold))
                    Text(verify ? "Enter the verification code sent to your eero account. Keep this window open while you get it." : "Sign in with the email address or phone number linked to your eero account.")
                        .font(.system(size: 15)).foregroundColor(.secondary).fixedSize(horizontal: false, vertical: true).padding(.bottom, 14)
                    if !verify { requestForm } else { verifyForm }
                    Text(model.noticeText).font(.system(size: 13)).foregroundColor(model.noticeIsError ? Tone.danger.color : Tone.muted.color).fixedSize(horizontal: false, vertical: true).padding(.top, 6)
                    if !verify {
                        Text("Other ways to sign in").font(.system(size: 13, weight: .semibold)).padding(.top, 10)
                        RowsView(rows: [
                            .link(icon: "\u{E77B}", title: "Amazon", subtitle: "Use the Amazon login linked to your eero account.", value: nil, badge: nil, tooltip: nil, tile: false, action: { model.amazonConsumerSignIn() }),
                            .link(icon: "\u{E774}", title: "eero's website", subtitle: "Sign in on eero.com in a secure window.", value: nil, badge: nil, tooltip: nil, tile: false, action: { model.websiteSignIn() }),
                            .link(icon: "\u{E968}", title: "Your internet provider", subtitle: "If your provider gave you your eeros.", value: nil, badge: nil, tooltip: nil, tile: false, action: { Task { await model.run({ try await model.providerSignIn() }, "Working…") } })
                        ]).padding(.bottom, 8)
                    }
                    HStack(spacing: 14) {
                        if !verify { Button("New to eero? Create an account") { model.beginRegistrationUi() }.buttonStyle(.link) }
                        Button("Trouble connecting? Check connection") { Task { await model.run({ try await model.checkConnection() }, "Working…") } }.buttonStyle(.link)
                    }.padding(.vertical, 6)
                    if !model.preview && !MacVpn.ownActiveScopes().isEmpty {
                        Button("Disconnect active VPN") { Task { await model.run({ for scope in MacVpn.ownActiveScopes() { try await MacVpn.control("disconnect", scope) }; model.setNotice("VPN disconnected.") }, "Working…") } }
                    }
                    Text("Uses eero’s cloud service. An internet connection is required.").font(.system(size: 12)).foregroundColor(.secondary)
                }
                .frame(maxWidth: 410, alignment: .leading).padding(36).frame(maxWidth: .infinity)
            }
            .disabled(model.busy)
        }
    }
    private var requestForm: some View {
        VStack(alignment: .leading, spacing: 8) {
            Picker("Sign in using", selection: $method) { Text("Phone number").tag(0); Text("Email address").tag(1) }
                .onChange(of: method) { m in model.formatPhone = m == 0; if model.formatPhone { identifier = PhoneInput.formatForDisplay(identifier) } }
            Text(method == 0 ? "Phone number" : "Email address").font(.system(size: 13, weight: .semibold)).padding(.top, 6)
            TextField("", text: $identifier).textFieldStyle(.roundedBorder).accessibilityLabel("Email or phone number")
                .onChange(of: identifier) { value in if model.formatPhone { let f = PhoneInput.formatForDisplay(value); if f != value { identifier = f } } }
                .onSubmit(send)
            Text(method == 0 ? "10 digits automatically use +1 (US/Canada). For other countries, include + and the country code." : "Use the email address linked to your eero account.")
                .font(.system(size: 12)).foregroundColor(.secondary).fixedSize(horizontal: false, vertical: true)
            Toggle("Remember me on this Mac", isOn: $remember).padding(.vertical, 4)
                .onChange(of: remember) { model.state["rememberSignIn"] = $0 }
            Button(action: send) { Text("Send verification code").frame(maxWidth: .infinity) }.buttonStyle(.borderedProminent).controlSize(.large).keyboardShortcut(.defaultAction)
            Text("When Remember me is on, your session is encrypted with a key kept in your macOS Keychain.").font(.system(size: 12)).foregroundColor(.secondary).fixedSize(horizontal: false, vertical: true)
        }
        .onAppear { method = model.formatPhone ? 0 : 1; model.state["rememberSignIn"] = remember }
    }
    private func send() { let id = identifier, keep = remember; Task { await model.run({ try await model.sendCode(id, keep) }, "Working…") } }
    private var verifyForm: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Verification code").font(.system(size: 13, weight: .semibold))
            TextField("", text: $code).textFieldStyle(.roundedBorder).font(.system(size: 28)).accessibilityLabel("Verification code").onSubmit(confirm)
            Button(action: confirm) { Text("Verify and sign in").frame(maxWidth: .infinity) }.buttonStyle(.borderedProminent).controlSize(.large).keyboardShortcut(.defaultAction)
            HStack {
                Button("Resend code") { Task { await model.run({ try await model.resendCode() }, "Working…") } }
                Button("Use a different account") { model.api.cancelLogin(); model.showLogin(false) }
            }
        }
    }
    private func confirm() { let c = code; Task { await model.run({ try await model.verifyCode(c); code = "" }, "Working…") } }
}

struct ShellView: View {
    @EnvironmentObject var model: MainWindow
    @State private var search = ""
    var body: some View {
        NavigationSplitView {
            SidebarView(search: $search)
                .navigationSplitViewColumnWidth(min: 212, ideal: 230, max: 280)
        } detail: {
            VStack(spacing: 0) {
                ScrollViewReader { proxy in
                    ScrollView {
                        BlocksView(blocks: model.pageBlocks).id(model.renderToken)
                            .padding(.horizontal, 28).padding(.top, 10).padding(.bottom, 24)
                            .frame(maxWidth: 1240, alignment: .leading).frame(maxWidth: .infinity)
                            .id("top")
                    }
                    .onChange(of: model.currentPageForScroll) { _ in proxy.scrollTo("top", anchor: .top) }
                }
                Divider()
                Text(model.noticeText).font(.system(size: 11)).foregroundColor(model.noticeIsError ? Tone.danger.color : Tone.muted.color)
                    .lineLimit(2).frame(maxWidth: .infinity, alignment: .leading).padding(.horizontal, 24).padding(.vertical, 8)
            }
            .toolbar {
                ToolbarItemGroup(placement: .navigation) {
                    Button { model.goBack() } label: { Image(systemName: "chevron.left") }.disabled(model.history.isEmpty || model.busy).help(model.backTooltip)
                    NetworkPicker()
                }
                ToolbarItemGroup(placement: .primaryAction) {
                    Button { Task { await model.refreshAll() } } label: { Image(systemName: "arrow.clockwise") }.help("Refresh network (⌘R)").disabled(model.busy)
                    Button { model.navigate("Settings/Notifications") } label: { Image(systemName: "bell") }.help("Notifications")
                    Button { model.navigate("Home/Add or replace eero") } label: { Image(systemName: "plus") }.help("Add or replace eero")
                }
            }
        }
        .disabled(false)
    }
}

struct NetworkPicker: View {
    @EnvironmentObject var model: MainWindow
    var body: some View {
        let choices = model.networkList.map { MainWindow.NetworkChoice(name: J.first($0, "name", "url"), url: J.s($0, "url")) }
        HStack(spacing: 8) {
            Image(systemName: "wifi").foregroundColor(.accentColor)
            Picker("Network", selection: Binding(get: { model.networkUrl ?? "" }, set: { url in model.selectNetwork(url) })) {
                ForEach(choices, id: \.url) { Text($0.name).tag($0.url) }
                if choices.isEmpty { Text("No network").tag("") }
            }
            .labelsHidden().frame(width: 220).accessibilityLabel("Selected network").disabled(model.busy)
            Text(model.preview ? "Sample network" : "eero network").font(.system(size: 12)).foregroundColor(.secondary)
        }
    }
}

struct SidebarView: View {
    @Binding var search: String
    @EnvironmentObject var model: MainWindow
    var body: some View {
        VStack(spacing: 0) {
            VStack(alignment: .leading, spacing: 0) {
                Text("eero").font(.system(size: 34, weight: .light))
                Text("Desktop for Mac").font(.system(size: 11)).foregroundColor(.secondary)
            }.frame(maxWidth: .infinity, alignment: .leading).padding(.horizontal, 18).padding(.bottom, 10)
            if search.trim().isEmpty {
                List(selection: Binding(get: { model.navigationSelection }, set: { r in if let r = r, r != model.currentPage { model.navigate(r) } })) {
                    Section("Your network") {
                        NavRow(title: "Home", route: "Home", glyph: "\u{E80F}")
                        NavRow(title: "Devices", route: "Devices", glyph: "\u{E772}")
                        NavRow(title: "Profiles", route: "Home/Profiles", glyph: "\u{E716}")
                        NavRow(title: "Activity", route: "Activity", glyph: "\u{E9D9}")
                        NavRow(title: "Security & privacy", route: "Home/Security & privacy", glyph: "\u{EA18}")
                    }
                    Section("Manage") {
                        NavRow(title: "Settings", route: "Settings", glyph: "\u{E713}")
                        NavRow(title: "Help & support", route: "Help", glyph: "\u{E897}")
                    }
                }
                .listStyle(.sidebar)
            } else {
                let found = MainWindow.searchCatalog(search.trim(), nil, 8)
                List {
                    if found.isEmpty { Text("No pages or settings match “" + search.trim() + "”. Try a broader word such as wifi, dns or guest.").font(.system(size: 12)).foregroundColor(.secondary) }
                    ForEach(found, id: \.path) { entry in
                        Button { search = ""; model.navigate(entry.path) } label: {
                            HStack(spacing: 10) {
                                Image(systemName: Glyphs.symbol(MainWindow.routeIcon(entry.path))).foregroundColor(.accentColor).frame(width: 18)
                                VStack(alignment: .leading, spacing: 2) {
                                    Text(entry.title).font(.system(size: 13, weight: .semibold))
                                    let badge = model.availabilityBadge(entry.path).0
                                    Text(MainWindow.breadcrumb(entry.path) + (badge != nil ? "  ·  " + badge! : "")).font(.system(size: 11)).foregroundColor(badge != nil ? Tone.warn.color : .secondary).lineLimit(1)
                                }
                            }
                        }.buttonStyle(.plain)
                    }
                }.listStyle(.sidebar)
            }
            Divider()
            VStack(alignment: .leading, spacing: 6) {
                Button { model.navigate("Settings/Account settings") } label: {
                    Label(model.preview ? "Preview account" : J.first(model.account, "name", "email", "phone"), systemImage: "person.crop.circle").lineLimit(1)
                }.buttonStyle(.plain).accessibilityLabel("Account settings")
                Button(model.preview ? "Back to sign in" : "Sign out") { Task { await model.run({ try await model.signOut() }, "Working…") } }.buttonStyle(.link)
                Text("Unofficial client · v1.1").font(.system(size: 10)).foregroundColor(.secondary)
            }.frame(maxWidth: .infinity, alignment: .leading).padding(14)
        }
        .searchable(text: $search, placement: .sidebar, prompt: "Search settings")
    }
}

struct NavRow: View {
    let title: String; let route: String; let glyph: String
    var body: some View { Label(title, systemImage: Glyphs.symbol(glyph)).tag(route).accessibilityLabel(title) }
}

/// Captures the hosting NSWindow (for sheets and alerts).
struct WindowAccessor: NSViewRepresentable {
    let onWindow: (NSWindow?) -> Void
    func makeNSView(context: Context) -> NSView { let v = NSView(); DispatchQueue.main.async { onWindow(v.window) }; return v }
    func updateNSView(_ nsView: NSView, context: Context) { DispatchQueue.main.async { onWindow(nsView.window) } }
}

extension MainWindow {
    var currentPageForScroll: String { currentPage }
    func refreshAll() async {
        await run({
            if !self.preview { if self.network == nil { try await self.loadAccount(); self.showShell() } else { try await self.loadNetwork() } }
            if self.currentPage.contains("/") { try await self.loadFeature(self.currentPage) }
            self.renderPage(); self.reportStatus("dashboard_loaded")
        }, "Working…")
    }
    func selectNetwork(_ url: String) {
        if busy || loading || url.isEmpty || url == networkUrl { return }
        Task { @MainActor in
            await run({
                self.closeAccountWindows(); self.history.removeAll(); self.networkUrl = url; self.network = nil; self.selectedProfile = nil; self.selectedNode = nil
                self.currentPage = "Home"; self.devices.removeAll(); self.nodes.removeAll(); self.profiles.removeAll(); self.featureData.removeAll(); self.notificationEpoch += 1
                if !self.preview { try await self.loadNetwork() }
                self.renderPage()
            }, "Loading network…")
        }
    }
    func signOut() async throws {
        stopTimer(); closeAccountWindows()
        if !preview { await api.logout() }
        preview = false; clearData(); showLogin(false); setNotice("Signed out. Your saved session has been cleared.")
    }
}
