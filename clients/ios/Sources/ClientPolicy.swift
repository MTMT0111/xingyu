import Foundation

struct HTTPSOrigin: Equatable {
    let host: String
    let port: Int

    init?(_ url: URL?) {
        guard let url = url, let parts = URLComponents(url: url, resolvingAgainstBaseURL: false),
              parts.scheme?.lowercased() == "https", let host = parts.host, !host.isEmpty,
              parts.user == nil, parts.password == nil else { return nil }
        let port = parts.port ?? 443
        guard (1...65535).contains(port) else { return nil }
        self.host = host.lowercased()
        self.port = port
    }
}

struct ClientConfiguration {
    let applicationURL: String
    let appID: String
    let universalLink: String
    let associatedDomain: String
    let registeredSchemes: [String]
    let editable: Bool

    var serverURL: URL? {
        guard let url = URL(string: applicationURL), HTTPSOrigin(url) != nil,
              let parts = URLComponents(url: url, resolvingAgainstBaseURL: false),
              parts.path.isEmpty || parts.path == "/", parts.query == nil, parts.fragment == nil else { return nil }
        return url
    }

    var origin: HTTPSOrigin? { HTTPSOrigin(serverURL) }

    var sharingConfigurationValid: Bool {
        guard serverURL != nil, appID.range(of: "^wx[0-9A-Fa-f]{16}$", options: .regularExpression) != nil,
              registeredSchemes.contains(appID), let url = URL(string: universalLink),
              let linkOrigin = HTTPSOrigin(url), linkOrigin.port == 443,
              let parts = URLComponents(url: url, resolvingAgainstBaseURL: false),
              parts.query == nil, parts.fragment == nil, parts.path.hasSuffix("/"), !parts.path.isEmpty,
              associatedDomain == "applinks:" + linkOrigin.host else { return false }
        return true
    }

    static func load(bundle: Bundle = .main, defaults: UserDefaults = .standard) -> ClientConfiguration {
        let editable = bundle.object(forInfoDictionaryKey: "XingyuAllowRuntimeConfiguration") as? String == "YES"
        func value(_ key: String) -> String {
            let saved = editable ? defaults.string(forKey: key) : nil
            return (saved ?? bundle.object(forInfoDictionaryKey: key) as? String ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        }
        let types = bundle.object(forInfoDictionaryKey: "CFBundleURLTypes") as? [[String: Any]] ?? []
        return ClientConfiguration(applicationURL: value("XingyuApplicationURL"), appID: value("XingyuWeChatAppID"), universalLink: value("XingyuUniversalLink"),
                                   associatedDomain: bundle.object(forInfoDictionaryKey: "XingyuAssociatedDomain") as? String ?? "",
                                   registeredSchemes: types.flatMap { $0["CFBundleURLSchemes"] as? [String] ?? [] }, editable: editable)
    }
}

enum BridgePolicy {
    static func requestID(_ value: Any?) -> String? {
        guard let id = value as? String, id.range(of: "^[A-Za-z0-9_-]{8,100}$", options: .regularExpression) != nil else { return nil }
        return id
    }

    static func validText(_ value: Any?) -> String? {
        guard let text = value as? String, !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
              text.utf8.count <= 10240, !text.utf8.contains(0) else { return nil }
        return text
    }

    static func trusted(isMainFrame: Bool, documentURL: URL?, frameURL: URL?, configuration: ClientConfiguration,
                        securityProtocol: String, securityHost: String, securityPort: Int) -> Bool {
        guard isMainFrame, let expected = configuration.origin,
              HTTPSOrigin(documentURL) == expected, HTTPSOrigin(frameURL) == expected,
              securityProtocol.lowercased() == "https", securityHost.lowercased() == expected.host else { return false }
        return (securityPort == 0 ? 443 : securityPort) == expected.port
    }
}
