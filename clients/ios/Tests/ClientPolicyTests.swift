import XCTest
@testable import Xingyu

final class ClientPolicyTests: XCTestCase {
    private func configuration(_ url: String = "https://reply.example.test/") -> ClientConfiguration {
        ClientConfiguration(applicationURL: url, appID: "", universalLink: "", associatedDomain: "", registeredSchemes: [], editable: true)
    }

    func testServiceRootRequiresHTTPSAndNoCredentialsOrExtraComponents() {
        XCTAssertNotNil(configuration().serverURL)
        XCTAssertNotNil(configuration("https://reply.example.test:8443/").serverURL)
        for value in ["http://reply.example.test/", "file:///tmp/x", "https://name:secret@reply.example.test/", "https://reply.example.test/a", "https://reply.example.test/?token=x", "https://reply.example.test/#x", "https://reply.example.test:0/", "https://reply.example.test:65536/"] {
            XCTAssertNil(configuration(value).serverURL, value)
        }
    }

    func testBridgeRequiresMainFrameAndAllThreeOrigins() {
        let config = configuration()
        let trusted = URL(string: "https://reply.example.test/?v=5")!
        let hostile = URL(string: "https://reply.example.test.attacker.test/")!
        func check(main: Bool = true, document: URL? = nil, frame: URL? = nil, scheme: String = "https", host: String = "reply.example.test", port: Int = 443) -> Bool {
            BridgePolicy.trusted(isMainFrame: main, documentURL: document ?? trusted, frameURL: frame ?? trusted, configuration: config, securityProtocol: scheme, securityHost: host, securityPort: port)
        }
        XCTAssertTrue(check())
        XCTAssertTrue(check(port: 0))
        XCTAssertFalse(check(main: false))
        XCTAssertFalse(check(document: hostile))
        XCTAssertFalse(check(frame: hostile))
        XCTAssertFalse(check(scheme: "http"))
        XCTAssertFalse(check(host: "attacker.test"))
        XCTAssertFalse(check(port: 8443))
    }

    func testTextIsValidatedInUTF8BytesWithoutChangingReply() {
        let original = "  妈，收到啦。\n'\"\\🙂  "
        XCTAssertEqual(BridgePolicy.validText(original), original)
        XCTAssertNotNil(BridgePolicy.validText(String(repeating: "🙂", count: 2560)))
        XCTAssertNil(BridgePolicy.validText(String(repeating: "🙂", count: 2560) + "a"))
        XCTAssertNotNil(BridgePolicy.validText(String(repeating: "中", count: 3413)))
        XCTAssertNil(BridgePolicy.validText(String(repeating: "中", count: 3414)))
        XCTAssertNil(BridgePolicy.validText(" \n\t "))
        XCTAssertNil(BridgePolicy.validText("妈\0收到啦"))
        XCTAssertNil(BridgePolicy.validText(["text": "hello"]))
    }

    func testRequestIDsCannotBecomeExecutableScript() {
        XCTAssertNotNil(BridgePolicy.requestID("cafebabe-0123-4567-89ab-cdef01234567"))
        for value in ["", "123", "a');alert(1)//", String(repeating: "a", count: 101), "含中文的请求编号"] {
            XCTAssertNil(BridgePolicy.requestID(value))
        }
    }

    func testEmptyWeChatConfigurationStaysUnavailable() {
        XCTAssertFalse(configuration().sharingConfigurationValid)
        let missingCallback = ClientConfiguration(applicationURL: "https://reply.example.test/", appID: "", universalLink: "https://reply.example.test/wechat/", associatedDomain: "applinks:reply.example.test", registeredSchemes: [], editable: true)
        XCTAssertFalse(missingCallback.sharingConfigurationValid)
    }

    func testSharingRequiresMatchingSchemeAndAssociatedDomain() {
        // Synthetic parser input only; this value is never registered or passed to the SDK.
        let id = "wx" + String(repeating: "0", count: 16)
        func candidate(link: String = "https://links.example.test/wechat/", domain: String = "applinks:links.example.test", registered: Bool = true) -> ClientConfiguration {
            ClientConfiguration(applicationURL: "https://reply.example.test/", appID: id, universalLink: link, associatedDomain: domain, registeredSchemes: registered ? [id] : [], editable: true)
        }
        XCTAssertTrue(candidate().sharingConfigurationValid)
        XCTAssertFalse(candidate(registered: false).sharingConfigurationValid)
        XCTAssertFalse(candidate(domain: "applinks:attacker.test").sharingConfigurationValid)
        for link in ["http://links.example.test/wechat/", "https://links.example.test/wechat", "https://links.example.test/wechat/?key=x", "https://links.example.test:8443/wechat/", "https://name:secret@links.example.test/wechat/"] {
            XCTAssertFalse(candidate(link: link).sharingConfigurationValid)
        }
    }
}
