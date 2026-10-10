"""Static source/configuration checks only; this does not compile or run iOS."""
from datetime import datetime, timezone
import json
from pathlib import Path
import plistlib
import re

root = Path(__file__).resolve().parent.parent
info = plistlib.loads((root / "Resources/Info.plist").read_bytes())
entitlements = plistlib.loads((root / "Resources/Xingyu.entitlements").read_bytes())
web = (root / "Sources/WebViewController.swift").read_text(encoding="utf-8")
native = (root / "Sources/XYWeChatBridge.m").read_text(encoding="utf-8")
policy = (root / "Sources/ClientPolicy.swift").read_text(encoding="utf-8")
correlation = (root / "Sources/XYShareCorrelation.m").read_text(encoding="utf-8")
checks = []


def check(name, condition):
    assert condition, name
    checks.append(name)


check("plist_has_no_ATS_exceptions", "NSAppTransportSecurity" not in info)
check("official_wechat_query_schemes", info["LSApplicationQueriesSchemes"] == ["weixin", "weixinULAPI", "weixinURLParamsAPI"])
check("associated_domain_uses_build_configuration", entitlements["com.apple.developer.associated-domains"] == ["$(XINGYU_ASSOCIATED_DOMAIN)"])
check("official_sdk_version_pinned", "pod 'WechatOpenSDK-XCFramework', '2.0.9'" in (root / "Podfile").read_text())
check("app_id_and_service_example_are_blank", all(re.search(r"^" + key + r"\s*=\s*$", (root / "Config/Local.xcconfig.example").read_text(), re.M) for key in ["XINGYU_APP_URL", "XINGYU_WECHAT_APP_ID", "XINGYU_UNIVERSAL_LINK", "XINGYU_ASSOCIATED_DOMAIN", "DEVELOPMENT_TEAM"]))
check("share_is_text_only_session", "request.bText = YES" in native and "request.text = text" in native and "request.scene = WXSceneSession" in native and "request.message =" not in native)
check("only_single_pending_sdk_request", "self.pendingRequestID" in native and "[SendMessageToWXResp class]" in native and "transaction" not in native.replace("// iOS 2.0.9 has no transaction field; only one outstanding share can be correlated.", ""))
check("bridge_checks_main_document_frame_and_security_origin", "BridgePolicy.trusted(isMainFrame:" in web and "HTTPSOrigin(documentURL) == expected" in policy and "HTTPSOrigin(frameURL) == expected" in policy and "securityHost.lowercased() == expected.host" in policy)
check("reply_payload_uses_argument_serialization", 'arguments: ["payload": payload]' in web and "callAsyncJavaScript" in web and "evaluateJavaScript" not in web)
check("popups_and_external_navigation_blocked", "javaScriptCanOpenWindowsAutomatically = false" in web and "navigationAction.targetFrame != nil" in web and "decisionHandler(.cancel)" in web and "-> WKWebView? { nil }" in web)
check("certificate_validation_not_bypassed", ".performDefaultHandling" in web and "URLCredential(trust:" not in web)
check("basic_auth_is_same_origin_session_only", "space.host.lowercased() == origin.host" in web and "space.port == origin.port" in web and "NSURLAuthenticationMethodHTTPBasic" in web and "persistence: .forSession" in web and ".nonPersistent()" in web)
check("release_disables_runtime_configuration", "XINGYU_ALLOW_RUNTIME_CONFIG = NO" in (root / "Config/Release.xcconfig").read_text())
check("boundary_xctests_exist", all(name in (root / "Tests/ClientPolicyTests.swift").read_text(encoding="utf-8") for name in ["testServiceRootRequiresHTTPS", "testBridgeRequiresMainFrame", "testTextIsValidatedInUTF8Bytes", "testRequestIDsCannotBecomeExecutableScript", "testEmptyWeChatConfiguration"]))
check("nul_text_is_rejected", "!text.utf8.contains(0)" in policy and 'BridgePolicy.validText("妈\\0收到啦")' in (root / "Tests/ClientPolicyTests.swift").read_text(encoding="utf-8"))
update = web.split("func updateConfigurationIfSameService", 1)[1].split("override func viewDidLoad", 1)[0]
check("same_service_settings_reuse_webview", "next.origin == configuration.origin" in update and "webView.load(" not in update and "current.updateConfigurationIfSameService(configuration)" in (root / "Sources/AppDelegate.swift").read_text(encoding="utf-8"))
check("sdk_response_wait_is_bounded", "scheduledTimerWithTimeInterval:120" in native and "expireRequest:requestID" in native and '@"result_unknown"' in native)
check("timeout_permanently_stops_response_attribution", "self.correlationLost = YES" in correlation and "if (self.correlationLost) return nil" in correlation and "self.correlationLost = NO" not in correlation)
check("handoff_after_timeout_releases_pending", "(!accepted || self.correlationLost)" in correlation and "testTimeoutUnlocksSharingAndNeverAttributesLateResponses" in (root / "Tests/ShareCorrelationTests.m").read_text())
result = {"checkedAt": datetime.now(timezone.utc).isoformat(), "status": "passed", "checks": checks, "staticChecks": len(checks), "iosCompiled": False, "xctestsExecuted": False, "deviceTested": False, "wechatSDKCalled": False, "limitations": ["Windows static source/configuration inspection only.", "Xcode build, Swift/Objective-C interoperability, actual UIKit/WebKit behavior and registered WeChat handoff remain unverified."]}
(root / "source-validation-results.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(result, ensure_ascii=False))
