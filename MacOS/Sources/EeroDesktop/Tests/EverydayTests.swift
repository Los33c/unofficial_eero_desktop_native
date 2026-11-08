import Foundation

extension Tests {
    final class StartupFake: IStartupStore {
        var value: String?; var writes = 0
        init(_ v: String?) { value = v }
        func read() -> String? { value }
        func write(_ command: String?) throws { value = command; writes += 1 }
    }
    static func everydayChecks(_ scratch: String) throws {
        try check(J.stringify(try EverydayContracts.backupOrder(["backup-2", "backup-1"])) == "{\"rearranged_ids\":[\"backup-2\",\"backup-1\"]}", "backup priorities preserve every UUID in the chosen order")
        try check(rejects { _ = try EverydayContracts.backupOrder(["same", "same"]) }, "duplicate backup priority entries rejected before request")
        try check(rejects { _ = try EverydayContracts.backupOrder([]) }, "empty backup reorder rejected")
        for symptom in ["internet_slow", "internet_offline", "connection_drops", "eeros_red"] {
            try check(J.s(try EverydayContracts.symptom(symptom), "symptom") == symptom && J.get(try EverydayContracts.symptom(symptom), "device") == nil, "network-wide diagnostics symptom " + symptom)
        }
        try check(rejects { _ = try EverydayContracts.symptom("reboot") }, "diagnostics cannot substitute a restart action")
        try check(J.list(J.get(try EverydayContracts.offers(["product-1", "product-1", "product-2"]), "product_ids")).count == 2, "offer request uses unique product_ids from eligible plans")
        try check(J.stringify(try EverydayContracts.referral("network-1", "product-2")) == "{\"product_id\":\"product-2\",\"network_id\":\"network-1\"}", "referral URL and email use API product_id/network_id contract")
        try check(rejects { _ = try EverydayContracts.offers(["bad\nproduct"]) }, "control characters in returned product IDs rejected")
        var credentials = try EverydayContracts.guardianLogin(J.obj("guardian_id", "sample-user", "guardian_secret", "sample-secret"))
        try check(credentials["Guardian username"] == "sample-user" && credentials["Guardian password"] == "sample-secret", "Guardian account bridge uses returned provider credentials")
        try expectSync(invalidOperation, "incomplete Guardian credentials cannot appear as successful sign-in") { _ = try EverydayContracts.guardianLogin(J.obj("guardian_id", "only-user")) }
        credentials = try EverydayContracts.threadCredentials(J.obj("name", "Sample Thread", "master_key", "sample-key", "active_operational_dataset", "sample-dataset", "unrelated_secret", "excluded"))
        try check(credentials.count == 2 && credentials["unrelated secret"] == nil, "Thread reveal includes only explicit supported credential fields")

        let executable = "/Applications/eero Desktop.app/Contents/MacOS/EeroDesktop"
        try check(try StartupRegistration.command(executable) == "\"" + executable + "\" --background", "startup executable path is quoted with a fixed background argument")
        for path in ["relative/EeroDesktop", "https://example.com/app", "/tmp/bad\"app", "/tmp/bad\napp", "/tmp/start.exe"] {
            try check(rejects { _ = try StartupRegistration.command(path) }, "unsafe startup path rejected: " + path.replacingOccurrences(of: "\n", with: " "))
        }
        let startup = StartupFake("previous-registration"); var saved = false
        try StartupRegistration.apply(startup, executable, true) { saved = true }
        try check(saved && startup.value == (try StartupRegistration.command(executable)), "startup registration and preference save complete together")
        let before = startup.value
        var invalid = false
        do { try StartupRegistration.apply(startup, executable, false) { throw AppError.io("simulated save failure") } } catch let e as AppError where e.kind == .io { invalid = true }
        try check(invalid && startup.value == before, "failed preference save restores the previous startup registration")
        try StartupRegistration.apply(startup, executable, false) {}; try check(startup.value == nil, "disabling startup removes only this app's entry")

        let journalPath = (scratch as NSString).appendingPathComponent("notifications.dat"); var journal = NotificationJournal(journalPath)
        let first = J.obj("uuid", "notice-1", "body", "sample-sensitive-body"), second = J.obj("uuid", "notice-2", "body", "second-body")
        try check(try journal.observe("account-A", "/2.2/networks/1", [first]).isEmpty, "first notification check seeds without announcing old history"); try journal.save()
        let encrypted = String(decoding: try Data(contentsOf: URL(fileURLWithPath: journalPath)), as: UTF8.self)
        try check(!encrypted.contains("sample-sensitive-body") && !encrypted.contains("notice-1") && !encrypted.contains("account-A"), "persisted notification cursor is AES-GCM encrypted and contains no plaintext account/events")
        journal = NotificationJournal(journalPath)
        try check(try journal.observe("account-A", "/2.2/networks/1", [second, first]).count == 1, "new notifications after process restart are detected")
        try check(try journal.observe("account-A", "/2.2/networks/1", [second, first]).isEmpty, "repeated notification history is not announced again")
        try check(try journal.observe("account-B", "/2.2/networks/1", [second, first]).isEmpty, "notification cursors are isolated by account")
        try check(try journal.observe("account-A", "/2.2/networks/2", [second, first]).isEmpty, "notification cursors are isolated by network")
        let third = J.obj("uuid", "notice-3"); let fresh = try journal.observe("account-A", "/2.2/networks/1", [third, second, first])
        try check(fresh.count == 1 && J.s(fresh[0], "uuid") == "notice-3", "only unseen notification identifiers produce alerts")
        try journal.save(); journal = NotificationJournal(journalPath); try check(try journal.observe("account-A", "/2.2/networks/1", [third, second, first]).isEmpty, "cursor persistence suppresses duplicates on a second restart")
        try "corrupt-cache".write(toFile: journalPath, atomically: true, encoding: .utf8); journal = NotificationJournal(journalPath)
        try check(try journal.observe("account-A", "/2.2/networks/1", [first]).isEmpty, "corrupt notification cache recovers with a quiet initial seed")
        try check(rejects { _ = try journal.observe("account-A", "https://outside.example/2.2/networks/1", [first]) }, "notification history cannot be scoped to an untrusted API resource")
    }
}
