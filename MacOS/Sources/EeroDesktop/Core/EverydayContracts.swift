import Foundation

enum EverydayContracts {
    static func backupOrder(_ ids: [String]) throws -> JObject {
        let all = try ids.map { try ExtendedContracts.identifier($0) }
        if all.isEmpty || Set(all).count != all.count { throw AppError.argument("Each saved backup network must appear exactly once.") }
        return J.obj("rearranged_ids", all)
    }
    static func symptom(_ value: String?) throws -> JObject {
        guard let value = value, ["connection_drops", "eeros_red", "internet_offline", "internet_slow"].contains(value) else { throw AppError.argument("Choose a supported connection problem.") }
        return J.obj("symptom", value)
    }
    static func productId(_ value: String?) throws -> String {
        guard let value = value, !value.trim().isEmpty, value.utf16.count <= 200, !value.unicodeScalars.contains(where: { $0.properties.generalCategory == .control })
        else { throw AppError.argument("eero did not return a valid product identifier.") }
        return value
    }
    static func offers(_ products: [String]) throws -> JObject {
        var all: [String] = []
        for p in products { let id = try productId(p); if !all.contains(id) { all.append(id) } }
        if all.isEmpty || all.count > 50 { throw AppError.argument("Select between one and fifty available products.") }
        return J.obj("product_ids", all)
    }
    static func referral(_ networkId: String?, _ productId: String?) throws -> JObject {
        J.obj("product_id", try EverydayContracts.productId(productId), "network_id", try ExtendedContracts.identifier(networkId))
    }
    static func guardianLogin(_ data: Any?) throws -> [String: String] {
        let id = J.s(data, "guardian_id"), password = J.s(data, "guardian_secret")
        if id.isEmpty || password.isEmpty || id.utf16.count > 4096 || password.utf16.count > 8192 { throw AppError.invalidOperation("eero did not return complete Guardian sign-in details for this account.") }
        return ["Guardian username": id, "Guardian password": password]
    }
    static func threadCredentials(_ data: Any?) throws -> [String: String] {
        var values: [String: String] = [:]
        for key in ["active_operational_dataset", "commissioning_credential", "master_key"] {
            let value = J.s(data, key); if !value.isEmpty && value.utf16.count <= 8192 { values[key.replacingOccurrences(of: "_", with: " ")] = value }
        }
        if values.isEmpty { throw AppError.invalidOperation("This network did not return Thread credentials for your account.") }
        return values
    }
}
