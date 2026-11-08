import Darwin
import Foundation

/// System.Net.Sockets.AddressFamily (only the two families the contracts use).
enum AddressFamily { case interNetwork, interNetworkV6 }

/// System.Net.IPAddress, parsed and printed with inet_pton/inet_ntop. IPv4 accepts dotted-quad decimal only.
struct IPAddress: Equatable, CustomStringConvertible {
    let bytes: [UInt8]
    init(_ bytes: [UInt8]) { self.bytes = bytes }
    static let any = IPAddress([0, 0, 0, 0])
    static let ipv6Any = IPAddress([UInt8](repeating: 0, count: 16))
    var addressFamily: AddressFamily { bytes.count == 4 ? .interNetwork : .interNetworkV6 }
    func getAddressBytes() -> [UInt8] { bytes }
    static func tryParse(_ text: String?) -> IPAddress? {
        guard let text = text, !text.isEmpty, !text.contains("\0") else { return nil }
        var v4 = in_addr()
        if inet_pton(AF_INET, text, &v4) == 1 { return IPAddress(withUnsafeBytes(of: &v4) { Array($0) }) }
        var v6 = in6_addr()
        if inet_pton(AF_INET6, text, &v6) == 1 { return IPAddress(withUnsafeBytes(of: &v6) { Array($0) }) }
        return nil
    }
    static func parse(_ text: String?) throws -> IPAddress {
        guard let a = tryParse(text) else { throw AppError.argument("An invalid IP address was specified.") }
        return a
    }
    static func isLoopback(_ a: IPAddress) -> Bool {
        if a.bytes.count == 4 { return a.bytes[0] == 127 }
        return a.bytes == [UInt8](repeating: 0, count: 15) + [1]
    }
    func toString() -> String { description }
    var description: String {
        var buffer = [CChar](repeating: 0, count: Int(INET6_ADDRSTRLEN) + 1)
        var copy = bytes
        let family = bytes.count == 4 ? AF_INET : AF_INET6
        let ok = copy.withUnsafeMutableBytes { raw in inet_ntop(family, raw.baseAddress, &buffer, socklen_t(buffer.count)) != nil }
        return ok ? String(cString: buffer) : ""
    }
}

/// System.Globalization.IdnMapping.GetAscii: NFKC-normalized, lower-cased labels; non-ASCII labels become
/// "xn--" Punycode (RFC 3492). Throws `AppError.argument` for empty or oversized labels, like .NET.
enum IdnMapping {
    static func getAscii(_ input: String) throws -> String {
        if input.isEmpty { throw AppError.argument("Decoded string is not a valid IDN name.") }
        let text = input.precomposedStringWithCompatibilityMapping.lowercased()
        let labels = text.components(separatedBy: CharacterSet(charactersIn: ".\u{3002}\u{FF0E}\u{FF61}"))
        var out: [String] = []
        for (i, label) in labels.enumerated() {
            if label.isEmpty {
                // A single trailing dot is allowed; any other empty label is not.
                if i == labels.count - 1 && i > 0 { out.append(""); continue }
                throw AppError.argument("Decoded string is not a valid IDN name.")
            }
            let scalars = label.unicodeScalars.map { $0.value }
            var ascii: String
            if scalars.allSatisfy({ $0 < 0x80 }) { ascii = label }
            else {
                if scalars.contains(where: { $0 < 0x20 || (0x7F...0x9F).contains($0) }) || label.hasPrefix("xn--") { throw AppError.argument("Decoded string is not a valid IDN name.") }
                guard let encoded = punycode(scalars) else { throw AppError.argument("Decoded string is not a valid IDN name.") }
                ascii = "xn--" + encoded
            }
            if ascii.utf8.count > 63 { throw AppError.argument("The label exceeds the maximum length.") }
            out.append(ascii)
        }
        let result = out.joined(separator: ".")
        if result.utf8.count > (result.hasSuffix(".") ? 255 : 254) { throw AppError.argument("The host name exceeds the maximum length.") }
        return result
    }
    /// RFC 3492 Punycode encoder.
    static func punycode(_ input: [UInt32]) -> String? {
        let base = 36, tMin = 1, tMax = 26, skew = 38, damp = 700
        var n = 128, delta = 0, bias = 72
        var output = String(String.UnicodeScalarView(input.filter { $0 < 0x80 }.compactMap { Unicode.Scalar($0) }))
        let b = output.unicodeScalars.count
        var h = b
        if b > 0 { output += "-" }
        func digit(_ d: Int) -> Character { Character(Unicode.Scalar(UInt8(d < 26 ? 97 + d : 22 + d))) }
        func adapt(_ deltaIn: Int, _ points: Int, _ first: Bool) -> Int {
            var d = first ? deltaIn / damp : deltaIn / 2
            d += d / points
            var k = 0
            while d > ((base - tMin) * tMax) / 2 { d /= base - tMin; k += base }
            return k + (base - tMin + 1) * d / (d + skew)
        }
        while h < input.count {
            guard let m = input.filter({ Int($0) >= n }).min() else { return nil }
            let (product, overflow) = (Int(m) - n).multipliedReportingOverflow(by: h + 1)
            if overflow || delta > Int(Int32.max) - product { return nil }
            delta += product
            n = Int(m)
            for c in input {
                if Int(c) < n { delta += 1; if delta > Int(Int32.max) { return nil } }
                if Int(c) == n {
                    var q = delta, k = base
                    while true {
                        let t = k <= bias ? tMin : (k >= bias + tMax ? tMax : k - bias)
                        if q < t { break }
                        output.append(digit(t + (q - t) % (base - t)))
                        q = (q - t) / (base - t); k += base
                    }
                    output.append(digit(q))
                    bias = adapt(delta, h + 1, h == b); delta = 0; h += 1
                }
            }
            delta += 1; n += 1
        }
        return output
    }
}

enum FeatureContracts {
    static let deviceTypes: [String] = ["air_conditioner", "air_purifier", "alarm_system", "audio", "bed", "cable_box", "car", "cash_register", "coffee_maker", "credit_card_terminal", "desktop_computer", "digital_assistant", "door_bell", "door_lock", "exercise_bike", "e_reader", "fan", "game_console", "garage_door", "generic", "hard_drive", "hub", "laptop_computer", "light", "media_streamer", "network_equipment", "oven", "pet_device", "phone", "plug", "printer", "refrigerator", "remote", "scale", "security_camera", "smoke_detector", "sprinkler", "tablet", "television", "thermostat", "toaster", "unknown_computer", "vacuum", "washer_dryer", "watch"]

    static func mac(_ input: String?) throws -> String {
        let s = Regex.replace(input ?? "", "[-:. ]", "")
        if !Regex.isMatch(s, "^[0-9a-fA-F]{12}$") { throw AppError.argument("Enter a MAC address with 12 hexadecimal digits.") }
        let chars = Array(s)
        return (0..<6).map { String(chars[($0 * 2)..<($0 * 2 + 2)]).lowercased() }.joined(separator: ":")
    }
    static func address(_ input: String?, _ family: AddressFamily) throws -> String {
        let s = (input ?? "").trim()
        guard let a = IPAddress.tryParse(s), a.addressFamily == family, !(family == .interNetwork && !Regex.isMatch(s, "^(\\d{1,3}\\.){3}\\d{1,3}$")) else {
            throw AppError.argument("Enter a valid " + (family == .interNetwork ? "IPv4" : "IPv6") + " address.")
        }
        return a.toString()
    }
    /// Int32.TryParse with NumberStyles.Integer (surrounding white space and a sign are allowed).
    static func parseInt32(_ s: String) -> Int? {
        guard let n = Int(s.trim()), n >= Int(Int32.min), n <= Int(Int32.max) else { return nil }
        return n
    }
    static func portRange(_ input: String?) throws -> String {
        let parts = (input ?? "").trim().split("-")
        guard parts.count <= 2, let a = parseInt32(parts[0]), a >= 1, a <= 65535 else { throw AppError.argument("Ports must be between 1 and 65535, for example 443 or 8000-8010.") }
        var b = a
        if parts.count == 2 {
            guard let second = parseInt32(parts[1]), second >= a, second <= 65535 else { throw AppError.argument("Enter an ascending port range between 1 and 65535.") }
            b = second
        }
        return a == b ? String(a) : String(a) + "-" + String(b)
    }
    private static func portCount(_ range: String) -> Int { let a = range.split("-"); return a.count == 1 ? 1 : Int(a[1])! - Int(a[0])! + 1 }
    static func forward(_ ip: String?, _ external: String?, _ internalPort: String?, _ protocol: String, _ description: String, _ enabled: Bool) throws -> JObject {
        let gateway = try portRange(external), client = try portRange(internalPort)
        if portCount(gateway) != portCount(client) { throw AppError.argument("External and internal port ranges must contain the same number of ports.") }
        if !["tcp", "udp", "both"].contains(`protocol`) { throw AppError.argument("Choose TCP, UDP, or both.") }
        return J.obj("ip", try address(ip, .interNetwork), "gateway_port", gateway, "client_port", client, "protocol", `protocol`, "description", description.trim(), "enabled", enabled)
    }
    static func reservation(_ name: String, _ mac: String?, _ ip: String?) throws -> JObject {
        J.obj("description", name.trim(), "mac", try FeatureContracts.mac(mac), "ip", try address(ip, .interNetwork))
    }
    static func schedule(_ name: String, _ days: [String], _ start: String, _ end: String, _ enabled: Bool, _ power: Bool = false) throws -> JObject {
        try MainWindow.validateName(name)
        var selected: [String] = []
        for d in days where !selected.contains(d) { selected.append(d) }
        if selected.isEmpty || selected.contains(where: { !["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"].contains($0) }) { throw AppError.argument("Choose at least one day.") }
        let a = start.trim(), b = end.trim(), pattern = "^([01][0-9]|2[0-3]):[0-5][0-9]$"
        if !Regex.isMatch(a, pattern) || !Regex.isMatch(b, pattern) || a == b {
            throw AppError.argument("Use different start and end times in 24-hour HH:mm format. Overnight schedules are supported.")
        }
        return J.obj("name", name.trim(), "days", selected, power ? "start_time" : "start", a, power ? "end_time" : "end", b, "enabled", enabled)
    }
    static func dns(_ custom: Bool, _ servers: String?, _ caching: Bool) throws -> JObject {
        let ips = (servers ?? "").splitRemovingEmpty(",;\n\r ").map { $0.trim() }
        for s in ips where IPAddress.tryParse(s) == nil { throw AppError.argument("Every DNS server must be a valid IPv4 or IPv6 address.") }
        if custom && ips.isEmpty { throw AppError.argument("Enter at least one custom DNS server.") }
        if ips.count > 4 { throw AppError.argument("Enter no more than four DNS servers.") }
        let result = J.obj("mode", custom ? "custom" : "automatic", "caching", caching)
        if custom { result["custom"] = J.obj("ips", ips) }
        return J.obj("dns", result)
    }
    static func dhcp(_ mode: String, _ subnet: String?, _ mask: String?, _ first: String?, _ last: String?, _ randomization: Bool) throws -> JObject {
        if !["automatic", "custom", "bridge", "passthrough"].contains(mode) { throw AppError.argument("Choose Automatic, Manual, Bridge, or IP Passthrough.") }
        let result = J.obj("connection", J.obj("mode", mode == "bridge" ? "bridged" : mode == "passthrough" ? "ipPassthrough" : "nat"), "nat_port_randomization", randomization)
        let dhcp = J.obj("mode", mode == "custom" ? "custom" : "automatic")
        if mode == "custom" {
            let s = try address(subnet, .interNetwork), m = try address(mask, .interNetwork), f = try address(first, .interNetwork), l = try address(last, .interNetwork)
            func number(_ v: String) -> UInt32 { (IPAddress.tryParse(v)?.getAddressBytes() ?? []).reduce(UInt32(0)) { ($0 << 8) | UInt32($1) } }
            let network = number(s), netmask = number(m), begin = number(f), end = number(l), inverse = ~netmask
            if netmask == 0 || (inverse & (inverse &+ 1)) != 0 || (network & netmask) != network { throw AppError.argument("Enter a contiguous subnet mask and the matching subnet network address.") }
            if (begin & netmask) != network || (end & netmask) != network || begin <= network || end >= (network | inverse) || begin > end { throw AppError.argument("The DHCP range must be ordered and inside the subnet, excluding network and broadcast addresses.") }
            dhcp["custom"] = J.obj("subnet_ip", s, "subnet_mask", m, "start_ip", f, "end_ip", l)
        }
        if mode != "bridge" && mode != "passthrough" { result["dhcp"] = dhcp }
        return result
    }
    /// Uri.CheckHostName(s) == UriHostNameType.Dns.
    static func isDnsHostName(_ s: String) -> Bool {
        if s.isEmpty || IPAddress.tryParse(s) != nil { return false }
        let name = s.hasSuffix(".") ? String(s.dropLast()) : s
        return !name.isEmpty && name.split(".").allSatisfy { Regex.isMatch($0, "^[a-zA-Z0-9_][a-zA-Z0-9_-]{0,62}$") }
    }
    static func domain(_ value: String?) throws -> String {
        var s = (value ?? "").trim().trimEnd(".").lowercased()
        if s.contains("://") || s.contains("/") || s.contains(" ") || s.contains("@") { throw AppError.argument("Enter a domain such as example.com, without https:// or a path.") }
        do { s = try IdnMapping.getAscii(s) } catch { throw AppError.argument("Enter a valid domain name.") }
        if !isDnsHostName(s) || !s.contains(".") || s.count > 253 { throw AppError.argument("Enter a valid domain such as example.com.") }
        return s
    }
    static func wifiQr(_ name: String?, _ password: String?) -> String {
        func escape(_ s: String?) -> String {
            (s ?? "").replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: ";", with: "\\;").replacingOccurrences(of: ",", with: "\\,")
                .replacingOccurrences(of: ":", with: "\\:").replacingOccurrences(of: "\"", with: "\\\"")
        }
        return "WIFI:T:" + ((password ?? "").isEmpty ? "nopass" : "WPA") + ";S:" + escape(name) + ";P:" + escape(password) + ";;"
    }
}

final class FeatureEntry {
    var path: String
    var description: String?
    var evidence: String?
    var limitation: String?
    init(_ path: String, _ description: String?, _ evidence: String?, _ limitation: String? = nil) {
        self.path = path; self.description = description; self.evidence = evidence; self.limitation = limitation
    }
    var title: String { let i = path.lastIndexOf("/"); return path.substring(i + 1) }
    var parent: String { let i = path.lastIndexOf("/"); return i < 0 ? "" : path.substring(0, i) }
}

enum FeatureCatalog {
    static let entries: [FeatureEntry] = [
        FeatureEntry("Home/Internet", "Connection status, speed and speed test history", "TabBarMode, HomeViewModel"),
        FeatureEntry("Home/eeros", "Your eeros, their lights, ports and restarts", "IEeroService"),
        FeatureEntry("Home/Profiles", "Create profiles, assign devices, pause access and schedule pauses", "ProfileUpdateRequest, ScheduledPause"),
        FeatureEntry("Home/Security & privacy", "Threat and ad blocking, content filters and website rules", "DnsPolicySlug, INetworkService"),
        FeatureEntry("Home/Security & privacy/Content filters", "Separate filters for each extra wifi network", "SubnetContentFiltersRequest"),
        FeatureEntry("Home/Add or replace eero", "Add another eero to grow your wifi", "IEeroService.addEeroWithSerial"),
        FeatureEntry("Home/Add or replace eero/Find nearby eeros", "Use Bluetooth to find new eeros nearby", "EeroBleManagerLive, EeroUuids"),
        FeatureEntry("Home/Add or replace eero/Automatic setup", "Let new eeros join your network by themselves", "getAutoDiscoveredEeros, SetAutoSetupMode"),
        FeatureEntry("Home/Add or replace eero/Create a network", "Set up a brand-new network with a spare eero", "CreateNetworkRequest, IEeroService.addEeroWithSerial", "Not validated on unconfigured hardware; Bluetooth write provisioning and gateway replacement are incomplete."),
        FeatureEntry("Home/eeros/Placement tests", "Check whether each eero is in a good spot", "runPlacementTest, PlacementResult"),
        FeatureEntry("Home/Security & privacy/VPN", "Guardian VPN for this Mac, if your plan includes it", "GuardianVpnPetRequest, Guardian Connect API, WireGuard for macOS"),
        FeatureEntry("Devices/Blocked devices", "Review and unblock devices", "NetworkService.blockDevice, unblockDevice"),
        FeatureEntry("Activity/Data usage", "How much data your network used, day by day", "IDataUsageService"),
        FeatureEntry("Activity/Security activity", "What eero blocked for you: threats, ads and websites", "IInsightsService"),
        FeatureEntry("Activity/Network events", "Devices joining, software updates, pauses and more", "INetworkService.getNetworkEvents"),
        FeatureEntry("Settings/Wifi name & password", "View, change, copy, and share wifi details as a QR code", "NetworkResources.password"),
        FeatureEntry("Settings/Guest wifi network", "Guest network access, credentials and QR sharing", "GuestNetwork, GuestResources"),
        FeatureEntry("Settings/Additional wifi networks", "Additional SSIDs, guest and IoT access, bandwidth limits and subnet filters", "SubnetConfig"),
        FeatureEntry("Settings/Business guest wifi", "Existing guest portal branding, session duration and visitor fields", "CaptivePortalConfigurationRequest"),
        FeatureEntry("Settings/Nickname & location", "Network nickname and timezone", "NetworkService.updateBusinessNameOrNetworkNickname"),
        FeatureEntry("Settings/Advanced networking", "DHCP, DNS, reservations, firewall, wireless and smart-home options", "SettingsRoute, INetworkService"),
        FeatureEntry("Settings/Advanced networking/Internet connection", "How eero gets its internet address: automatic or a static IP", "Network.ip_settings, NetworkPppoeConfig"),
        FeatureEntry("Settings/Advanced networking/Internet connection/ISP settings", "PPPoE login and VLAN ID, only if your ISP gave you one", "NetworkPppoeConfig, Network.vlan"),
        FeatureEntry("Settings/Advanced networking/DHCP & NAT", "How devices get addresses: automatic, manual range, bridge or passthrough", "DhcpConfigurationRequest"),
        FeatureEntry("Settings/Advanced networking/DNS", "Automatic or custom DNS servers and local DNS caching", "Dns, HostInfoList"),
        FeatureEntry("Settings/Advanced networking/DNS/DNS performance", "Test how quickly your DNS servers respond", "DnsPerfTestRequest, NetworkService.runDnsPerfTest"),
        FeatureEntry("Settings/Advanced networking/Reservations & port forwarding", "IPv4 reservations, port mappings and IPv6 firewall rules", "NetRules, NetworkReservation, NetworkForward"),
        FeatureEntry("Settings/Advanced networking/IPv6", "Enable or disable upstream IPv6", "Network.ipv6_upstream"),
        FeatureEntry("Settings/Advanced networking/UPnP", "Automatic port mapping", "Network.upnp"),
        FeatureEntry("Settings/Advanced networking/Wireless settings", "WPA3, client steering, optimization, compatibility and MLO", "Network, MultiLinkOperationModeRequest"),
        FeatureEntry("Settings/Advanced networking/Thread", "Smart-home network for Matter and Thread devices", "UpdateThreadNetworkRequest"),
        FeatureEntry("Settings/Advanced networking/Dynamic DNS", "A hostname that follows your public IP for remote access", "INetworkService.ddnsEnable, ddnsDisable"),
        FeatureEntry("Settings/Advanced networking/Power saving", "Lower eero power use, always or on a schedule", "PowerOptimizationRequest, PowerSavingScheduleRequest"),
        FeatureEntry("Settings/Advanced networking/Internet backup", "Stay online with a hotspot or backup wifi when your internet drops", "AddBackupNetworkRequest, BackupInternetEnabledRequest"),
        FeatureEntry("Settings/Advanced networking/Multi-WAN", "Use more than one internet connection", "INetworkService.coGetWanLinks"),
        FeatureEntry("Settings/Advanced networking/Multiple static IPs", "Use extra public IP addresses from your ISP", "MultiStaticIpRequest"),
        FeatureEntry("Settings/User permissions", "Network members, admin invitations and revocation", "CreateInviteRequest, INetworkService.removeAdmin"),
        FeatureEntry("Settings/User permissions/Join a network", "Review, accept or decline an eero invitation", "InviteQueryRequest, ExternalInviteRequest"),
        FeatureEntry("Settings/User permissions/Incoming access invitations", "Review, accept or decline account access invitations", "MemberPromotionResponse, PromotionInviteRequest"),
        FeatureEntry("Settings/Software updates", "Update status, install available firmware and preferred update hour", "NetworkUpdates, PreferredUpdateHour"),
        FeatureEntry("Settings/Appearance", "Light, dark or system appearance", "SettingsRoute.Appearance"),
        FeatureEntry("Settings/Notifications", "Network notification preferences, history and desktop notifications", "NetworkNotifications, PushSettings"),
        FeatureEntry("Settings/Troubleshooting", "Connection checks, diagnostics, coverage and upgrade scans", "INetworkService.wifiCoverageScan, wifiGenerationScan"),
        FeatureEntry("Settings/Troubleshooting/Guided diagnostics", "Diagnose slow internet, offline service, dropped connections and red eeros", "HealthCheckSymptom, HealthCheckResults"),
        FeatureEntry("Settings/Troubleshooting/Connect a device", "Temporarily pause and restore 5 GHz for device setup", "TemporaryFlags.hide_5g, updateTemporaryFlags"),
        FeatureEntry("Settings/Advanced networking/Wireless settings/Fast roaming", "Review and configure fast transition compatibility", "FastTransitionStatus"),
        FeatureEntry("Settings/Advanced networking/Internet backup/Find backup wifi", "Discover nearby backup wifi networks and add a selected result", "BackupNetworkDiscoveryStatusResponse"),
        FeatureEntry("Settings/Notifications/Desktop notifications", "Start at login, background alerts and missed-update tracking", "Desktop background notifications"),
        FeatureEntry("Settings/Account settings", "Account details, name, verified email/phone changes and email preferences", "IUserService"),
        FeatureEntry("Settings/Account settings/Subscriptions", "Subscription status, software-key activation and partner benefits", "IPremiumService, ActivateLicenseKeyRequestBody"),
        FeatureEntry("Settings/Account settings/Subscriptions/Redeem a license key", "Activate a plan with a key from eero or your provider", "ValidateLicenseKeyRequestBody, ActivateLicenseKeyRequestBody"),
        FeatureEntry("Settings/Account settings/Subscriptions/Partner benefits", "1Password, Malwarebytes and VPN included with eero Plus", "IPremiumService"),
        FeatureEntry("Settings/Account settings/Subscriptions/Manage subscriptions & purchases", "Change or cancel your plan, see purchases and add-ons", "TemporaryExternalToken, TargetPage"),
        FeatureEntry("Settings/Account settings/Subscriptions/Offers & referrals", "Plans you can get, current offers and referral links", "EeroPlusOfferRequest, SubscriptionReferralRequest"),
        FeatureEntry("Settings/Account settings/Subscriptions/Billing & status", "Your plan, how you pay and past purchases", "IPremiumService.getCustomer, TargetPage"),
        FeatureEntry("Settings/Account settings/Subscriptions/Business billing", "Payment account for business organizations", "PaymentAccountManagementResponse, PaymentAccountOnboardingResponse"),
        FeatureEntry("Home/Security & privacy/Identity protection", "Watch for your email in data breaches", "DigitalFootprintScanRequest, DigitalFootprintVerifyCodeRequest"),
        FeatureEntry("Settings/Connected services", "Amazon, eero Built-in and connected-service status", "Network.proxied_nodes, simple_setup"),
        FeatureEntry("Settings/Connected services/Connected device setup", "Review and configure per-vendor device setup permissions", "SimpleSetupNetworkConfiguration"),
        FeatureEntry("Settings/Connected services/Amazon connected home", "Linked-account status, Zigbee discovery and account unlinking", "runZigbeeDeviceDiscovery, unlinkAmazonAccount"),
        FeatureEntry("Settings/Connected services/Accessories & cellular", "Cellular backup, phone adapters and other add-on hardware", "AccessoryStatus, Accessory, SipConfigAssociation"),
        FeatureEntry("Settings/Business guest wifi/eero Insight", "Open the official business management portal", "SettingsViewModel.setupEeroInsight"),
        FeatureEntry("Settings/Network management", "Network switching, ownership transfer and removal", "NetworkResources.transfer, deleteNetwork"),
        FeatureEntry("Settings/Network management/Pending transfers", "Accept or decline incoming network transfers", "PendingTransfersData, acceptNetworkTransfer, denyNetworkTransfer"),
        FeatureEntry("Settings/Network management/Outgoing transfer", "Review, resend or cancel a pending ownership invitation", "getTransferNetworkInvite, resendTransferNetwork"),
        FeatureEntry("Settings/About", "Version, sources, capabilities and compatibility information", "Feature inventory"),
        FeatureEntry("Home/Chat", "Ask eero about your network", "IConversationsService, ConversationStreamingService"),
        FeatureEntry("Help/Support history", "Read or continue earlier chats with eero", "ConversationsResponse, ConversationResponse"),
        FeatureEntry("Help/Contact support", "Phone, email or chat options for your account", "SupportInfo, SupportInfoItem, TYPE.CHAT"),
        FeatureEntry("Help", "Fix a problem, ask eero, or contact a person", "TabBarMode.Troubleshooting"),
        FeatureEntry("Home/Add or replace eero/Setup tools", "Extra tools for adding or replacing eeros", "pendingEeros, packagedEeros, checkOui, setNetworkToDefaultChannel, notifyNodeSwap, getPlacementTestResult"),
        FeatureEntry("Activity/Usage details", "Usage and blocking for one eero, profile or device", "IInsightsStatsService"),
        FeatureEntry("Settings/Advanced networking/Network flags", "Support-only switches. Change only if eero support asks", "INetworkService.updateFlag"),
        FeatureEntry("Settings/Network management/Organization & community", "For business, building and provider-managed networks", "getCommunity, getCobranding, getManagedNetworks, ICommunitiesService, updateNetworkType"),
        FeatureEntry("Settings/Account settings/Trusted certificates", "Add, replace or remove certificates trusted by your account", "IUserService.getTrust, addTrust, updateTrust, deleteTrust"),
        FeatureEntry("Settings/Account settings/Account data", "Email verification, privacy contact and technical account info", "getDataControllerInfo, getCountryCodes, getAppConfiguration, IDirectedIdService, resendEmailVerify"),
        FeatureEntry("Settings/Account settings/Subscriptions/Entitlements & trials", "What your plan includes and upgrades you can try", "IEntitledFeatures, createAutoSubscription"),
        FeatureEntry("Settings/Account settings/Subscriptions/Partner offers", "Products from eero partners", "IPartnerUpsell, createEncryptMeAccount"),
        FeatureEntry("Help/Report an issue", "Send eero a description of a problem and a screenshot", "IZendeskService.createIssue"),
        FeatureEntry("Help/Service availability", "What works here and what needs the official eero app", "Service integrations", "Full Bluetooth provisioning and gateway replacement are unavailable. Amazon sign-in requires an existing eero account using the same Amazon login; no developer account or Business subscription is needed. Email/phone sign-in remains available for accounts that use it. Purchases remain managed by their billing provider. Guardian VPN needs an eligible account, WireGuard tools (Homebrew wireguard-tools) or the WireGuard app, and administrator approval.")
    ]
    static func parent(_ path: String) -> String { let i = path.lastIndexOf("/"); return i < 0 ? "Home" : path.substring(0, i) }
    static func root(_ path: String) -> String { path.split("/")[0] }
    static func children(_ path: String) -> [FeatureEntry] { entries.filter { $0.parent == path } }
}
