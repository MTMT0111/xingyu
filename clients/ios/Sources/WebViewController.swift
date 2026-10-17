import UIKit
import WebKit

private final class WeakScriptHandler: NSObject, WKScriptMessageHandler {
    weak var target: WKScriptMessageHandler?
    init(_ target: WKScriptMessageHandler) { self.target = target }
    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) { target?.userContentController(userContentController, didReceive: message) }
}

final class WebViewController: UIViewController, WKNavigationDelegate, WKUIDelegate, WKScriptMessageHandler {
    private var configuration: ClientConfiguration
    private let weChat: XYWeChatBridge
    private let onConfigure: () -> Void
    private var webView: WKWebView!
    private let errorPanel = UIStackView()
    private let errorLabel = UILabel()
    private var documentID = UUID()
    private var shareDocument: (id: String, document: UUID)?
    private var authenticationPresented = false

    init(configuration: ClientConfiguration, weChat: XYWeChatBridge, onConfigure: @escaping () -> Void) {
        self.configuration = configuration
        self.weChat = weChat
        self.onConfigure = onConfigure
        super.init(nibName: nil, bundle: nil)
    }
    required init?(coder: NSCoder) { fatalError("Use the programmatic initializer") }

    func updateConfigurationIfSameService(_ next: ClientConfiguration) -> Bool {
        guard next.serverURL != nil, next.origin == configuration.origin else { return false }
        configuration = next
        // Let the page request fresh capabilities without reloading its draft or attachment.
        if isViewLoaded {
            webView.callAsyncJavaScript("window.dispatchEvent(new Event('focus'))", arguments: [:], in: nil, in: .page, completionHandler: nil)
        }
        return true
    }

    override func viewDidLoad() {
        super.viewDidLoad()
        title = "言外"
        view.backgroundColor = .systemBackground
        navigationItem.rightBarButtonItem = UIBarButtonItem(title: "设置", style: .plain, target: self, action: #selector(openSettings))
        let options = WKWebViewConfiguration()
        options.websiteDataStore = .nonPersistent()
        options.preferences.javaScriptCanOpenWindowsAutomatically = false
        options.defaultWebpagePreferences.allowsContentJavaScript = true
        options.userContentController.add(WeakScriptHandler(self), name: "xingyuWeChat")
        webView = WKWebView(frame: .zero, configuration: options)
        webView.navigationDelegate = self
        webView.uiDelegate = self
        webView.allowsBackForwardNavigationGestures = true
        webView.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(webView)
        NSLayoutConstraint.activate([
            webView.leadingAnchor.constraint(equalTo: view.leadingAnchor), webView.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            webView.topAnchor.constraint(equalTo: view.safeAreaLayoutGuide.topAnchor), webView.bottomAnchor.constraint(equalTo: view.safeAreaLayoutGuide.bottomAnchor)
        ])
        configureErrorPanel()
        weChat.resultHandler = { [weak self] id, status, code in
            guard let self = self, self.shareDocument?.id == id, self.shareDocument?.document == self.documentID else { return }
            self.emit(["type": "shareResult", "id": id, "status": status, "code": code])
            self.shareDocument = nil
        }
        loadService()
    }

    private func configureErrorPanel() {
        errorPanel.axis = .vertical
        errorPanel.alignment = .fill
        errorPanel.spacing = 18
        errorPanel.backgroundColor = .systemBackground
        errorPanel.isLayoutMarginsRelativeArrangement = true
        errorPanel.layoutMargins = UIEdgeInsets(top: 24, left: 24, bottom: 24, right: 24)
        errorPanel.translatesAutoresizingMaskIntoConstraints = false
        errorLabel.numberOfLines = 0
        errorLabel.font = .preferredFont(forTextStyle: .body)
        errorLabel.adjustsFontForContentSizeCategory = true
        errorPanel.addArrangedSubview(errorLabel)
        for (name, action) in [("重试", #selector(loadService)), ("检查设置", #selector(openSettings))] {
            let button = UIButton(type: .system)
            button.setTitle(name, for: .normal)
            button.heightAnchor.constraint(greaterThanOrEqualToConstant: 48).isActive = true
            button.addTarget(self, action: action, for: .touchUpInside)
            errorPanel.addArrangedSubview(button)
        }
        view.addSubview(errorPanel)
        NSLayoutConstraint.activate([errorPanel.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 20), errorPanel.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -20), errorPanel.centerYAnchor.constraint(equalTo: view.centerYAnchor)])
        errorPanel.isHidden = true
    }

    @objc private func loadService() {
        guard let url = configuration.serverURL else { onConfigure(); return }
        errorPanel.isHidden = true
        webView.load(URLRequest(url: url))
    }
    @objc private func openSettings() { onConfigure() }
    @objc private func goBack() { if webView.canGoBack { webView.goBack() } }

    private func showFailure(_ text: String) { errorLabel.text = text; errorPanel.isHidden = false }
    private func reason() -> String {
        if configuration.appID.isEmpty { return "missing_app_id" }
        if !configuration.sharingConfigurationValid { return "invalid_configuration" }
        if !weChat.sdkAvailable { return "sdk_unavailable" }
        if !weChat.configured { return "invalid_configuration" }
        return weChat.isWeChatInstalled() ? "ready" : "wechat_not_installed"
    }

    func userContentController(_ userContentController: WKUserContentController, didReceive message: WKScriptMessage) {
        let origin = message.frameInfo.securityOrigin
        guard message.name == "xingyuWeChat", BridgePolicy.trusted(isMainFrame: message.frameInfo.isMainFrame, documentURL: webView.url,
              frameURL: message.frameInfo.request.url, configuration: configuration, securityProtocol: origin.`protocol`, securityHost: origin.host, securityPort: origin.port),
              let body = message.body as? [String: Any], let id = BridgePolicy.requestID(body["id"]), let type = body["type"] as? String else { return }
        if type == "capabilities" {
            let availability = reason()
            emit(["type": type, "id": id, "available": availability == "ready", "reason": availability, "platform": "ios"])
            return
        }
        guard type == "shareText" else { return }
        func failure(_ code: String) { emit(["type": "shareText", "id": id, "status": "error", "code": code]) }
        guard let text = BridgePolicy.validText(body["text"]) else { failure("invalid_text"); return }
        let availability = reason()
        guard availability == "ready" else { failure(availability); return }
        guard weChat.pendingRequestID == nil else { failure("busy"); return }
        let requestDocument = documentID
        shareDocument = (id, requestDocument)
        weChat.share(text: text, requestID: id) { [weak self] accepted in
            guard let self = self, self.documentID == requestDocument else { return }
            self.emit(accepted ? ["type": "shareText", "id": id, "status": "handoff"] : ["type": "shareText", "id": id, "status": "error", "code": "sdk_rejected"])
            if !accepted { self.shareDocument = nil }
        }
    }

    private func emit(_ payload: [String: Any]) {
        guard HTTPSOrigin(webView.url) == configuration.origin else { return }
        webView.callAsyncJavaScript("window.dispatchEvent(new CustomEvent('xingyu-wechat', { detail: payload }))", arguments: ["payload": payload], in: nil, in: .page, completionHandler: nil)
    }

    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        guard navigationAction.targetFrame != nil, HTTPSOrigin(navigationAction.request.url) == configuration.origin else { decisionHandler(.cancel); return }
        decisionHandler(.allow)
    }
    func webView(_ webView: WKWebView, decidePolicyFor navigationResponse: WKNavigationResponse, decisionHandler: @escaping (WKNavigationResponsePolicy) -> Void) {
        guard HTTPSOrigin(navigationResponse.response.url) == configuration.origin else { decisionHandler(.cancel); return }
        if navigationResponse.isForMainFrame, let response = navigationResponse.response as? HTTPURLResponse, response.statusCode >= 400 {
            showFailure(response.statusCode == 401 ? "服务需要试用口令，或口令未通过。请重试。" : "服务暂时没有正常响应。请重试或检查服务设置。")
            decisionHandler(.cancel)
            return
        }
        decisionHandler(.allow)
    }
    func webView(_ webView: WKWebView, didStartProvisionalNavigation navigation: WKNavigation!) { documentID = UUID(); shareDocument = nil }
    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        errorPanel.isHidden = true
        navigationItem.leftBarButtonItem = webView.canGoBack ? UIBarButtonItem(title: "返回", style: .plain, target: self, action: #selector(goBack)) : nil
    }
    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) { handleFailure(error) }
    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) { handleFailure(error) }
    private func handleFailure(_ error: Error) {
        guard (error as NSError).code != NSURLErrorCancelled else { return }
        showFailure("没有安全地连接到服务。请确认网络、HTTPS 证书和服务地址，再重试。")
    }
    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) { showFailure("页面进程已被系统回收，未复制的草稿可能丢失。点击重试重新打开。") }
    func webView(_ webView: WKWebView, createWebViewWith configuration: WKWebViewConfiguration, for navigationAction: WKNavigationAction, windowFeatures: WKWindowFeatures) -> WKWebView? { nil }

    func webView(_ webView: WKWebView, didReceive challenge: URLAuthenticationChallenge, completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void) {
        let space = challenge.protectionSpace
        guard let origin = configuration.origin, space.host.lowercased() == origin.host, space.port == origin.port,
              space.`protocol`?.lowercased() == "https" else { completionHandler(.cancelAuthenticationChallenge, nil); return }
        if space.authenticationMethod == NSURLAuthenticationMethodServerTrust {
            // System certificate validation remains mandatory; never construct a trust credential.
            completionHandler(.performDefaultHandling, nil)
            return
        }
        guard space.authenticationMethod == NSURLAuthenticationMethodHTTPBasic, challenge.previousFailureCount < 2,
              !authenticationPresented, presentedViewController == nil else { completionHandler(.cancelAuthenticationChallenge, nil); return }
        authenticationPresented = true
        let alert = UIAlertController(title: "连接私有试用服务", message: "向 \(origin.host) 提交试用口令。口令仅用于本次会话，不写入应用设置。", preferredStyle: .alert)
        alert.addTextField { field in field.text = "trial"; field.placeholder = "用户名"; field.autocapitalizationType = .none; field.autocorrectionType = .no }
        alert.addTextField { field in field.placeholder = "试用口令"; field.isSecureTextEntry = true; field.textContentType = .password }
        alert.addAction(UIAlertAction(title: "取消", style: .cancel) { [weak self] _ in
            self?.authenticationPresented = false
            completionHandler(.cancelAuthenticationChallenge, nil)
            self?.showFailure("尚未连接私有服务。点击重试后可重新输入口令。")
        })
        alert.addAction(UIAlertAction(title: "连接", style: .default) { [weak self, weak alert] _ in
            self?.authenticationPresented = false
            let username = alert?.textFields?.first?.text ?? "trial"
            let password = alert?.textFields?.last?.text ?? ""
            alert?.textFields?.last?.text = nil
            completionHandler(.useCredential, URLCredential(user: username, password: password, persistence: .forSession))
        })
        present(alert, animated: true)
    }
}
