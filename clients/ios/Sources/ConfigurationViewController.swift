import UIKit

final class ConfigurationViewController: UIViewController {
    private let configuration: ClientConfiguration
    private let onSave: () -> Void
    private let server = UITextField()
    private let appID = UITextField()
    private let universalLink = UITextField()

    init(configuration: ClientConfiguration, onSave: @escaping () -> Void) {
        self.configuration = configuration
        self.onSave = onSave
        super.init(nibName: nil, bundle: nil)
    }
    required init?(coder: NSCoder) { fatalError("Use the programmatic initializer") }

    override func viewDidLoad() {
        super.viewDidLoad()
        title = "连接星语"
        view.backgroundColor = UIColor(red: 0.98, green: 0.98, blue: 0.95, alpha: 1)
        let scroll = UIScrollView()
        let stack = UIStackView()
        stack.axis = .vertical
        stack.spacing = 18
        scroll.translatesAutoresizingMaskIntoConstraints = false
        stack.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(scroll)
        scroll.addSubview(stack)
        NSLayoutConstraint.activate([
            scroll.leadingAnchor.constraint(equalTo: view.leadingAnchor), scroll.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            scroll.topAnchor.constraint(equalTo: view.safeAreaLayoutGuide.topAnchor), scroll.bottomAnchor.constraint(equalTo: view.bottomAnchor),
            stack.leadingAnchor.constraint(equalTo: scroll.contentLayoutGuide.leadingAnchor, constant: 24),
            stack.trailingAnchor.constraint(equalTo: scroll.contentLayoutGuide.trailingAnchor, constant: -24),
            stack.topAnchor.constraint(equalTo: scroll.contentLayoutGuide.topAnchor, constant: 24),
            stack.bottomAnchor.constraint(equalTo: scroll.contentLayoutGuide.bottomAnchor, constant: -32),
            stack.widthAnchor.constraint(equalTo: scroll.frameLayoutGuide.widthAnchor, constant: -48)
        ])
        let introduction = label("把关心，好好回给他们。", style: .title2)
        stack.addArrangedSubview(introduction)
        stack.addArrangedSubview(label("填写你信任的星语 HTTPS 服务。没有微信 AppID 时，仍可分析、编辑和复制回复。", style: .body))
        field(server, title: "服务根地址", value: configuration.applicationURL, into: stack)
        field(appID, title: "微信移动应用 AppID（选填）", value: configuration.appID, into: stack)
        field(universalLink, title: "微信 Universal Link（选填）", value: configuration.universalLink, into: stack)
        stack.addArrangedSubview(label(configuration.editable ? "此页只保存这三项非密钥配置，不保存试用口令。AppID 和 Universal Link 必须与编译时的 URL Scheme、Associated Domains 及开放平台登记一致。只更新微信设置会保留草稿；更换服务地址会重新打开页面。" : "发布版配置随签名应用提供。需要变更时，请在 Xcode 的 Local.xcconfig 与微信开放平台中更新配置后重新构建。", style: .footnote))
        if configuration.editable {
            let save = UIButton(type: .system)
            save.setTitle("保存并打开", for: .normal)
            save.titleLabel?.font = .preferredFont(forTextStyle: .headline)
            save.backgroundColor = UIColor(red: 0.97, green: 0.81, blue: 0.35, alpha: 1)
            save.layer.cornerRadius = 12
            save.heightAnchor.constraint(greaterThanOrEqualToConstant: 48).isActive = true
            save.addTarget(self, action: #selector(saveConfiguration), for: .touchUpInside)
            stack.addArrangedSubview(save)
        }
    }

    private func label(_ text: String, style: UIFont.TextStyle) -> UILabel {
        let label = UILabel()
        label.text = text
        label.font = .preferredFont(forTextStyle: style)
        label.adjustsFontForContentSizeCategory = true
        label.numberOfLines = 0
        label.textColor = .label
        return label
    }

    private func field(_ field: UITextField, title: String, value: String, into stack: UIStackView) {
        stack.addArrangedSubview(label(title, style: .subheadline))
        field.text = value
        field.borderStyle = .roundedRect
        field.autocapitalizationType = .none
        field.autocorrectionType = .no
        field.keyboardType = .URL
        field.textContentType = .URL
        field.font = .preferredFont(forTextStyle: .body)
        field.adjustsFontForContentSizeCategory = true
        field.isEnabled = configuration.editable
        field.accessibilityLabel = title
        field.heightAnchor.constraint(greaterThanOrEqualToConstant: 48).isActive = true
        stack.addArrangedSubview(field)
    }

    @objc private func saveConfiguration() {
        let candidate = ClientConfiguration(applicationURL: (server.text ?? "").trimmingCharacters(in: .whitespacesAndNewlines), appID: (appID.text ?? "").trimmingCharacters(in: .whitespacesAndNewlines), universalLink: (universalLink.text ?? "").trimmingCharacters(in: .whitespacesAndNewlines), associatedDomain: configuration.associatedDomain, registeredSchemes: configuration.registeredSchemes, editable: true)
        guard candidate.serverURL != nil else {
            let alert = UIAlertController(title: "请检查服务地址", message: "只接受 HTTPS 根地址，可带端口；不要包含账号、密码、路径、查询参数或片段。", preferredStyle: .alert)
            alert.addAction(UIAlertAction(title: "知道了", style: .default))
            present(alert, animated: true)
            return
        }
        let save = { [weak self] in
            let defaults = UserDefaults.standard
            defaults.set(candidate.applicationURL, forKey: "XingyuApplicationURL")
            defaults.set(candidate.appID, forKey: "XingyuWeChatAppID")
            defaults.set(candidate.universalLink, forKey: "XingyuUniversalLink")
            self?.onSave()
        }
        if candidate.origin == configuration.origin { save(); return }
        let alert = UIAlertController(title: "打开这个服务？", message: "你提交的消息和附件将由这个地址处理。保存后会重新打开页面，当前草稿请先复制保留。\n\n" + candidate.applicationURL, preferredStyle: .alert)
        alert.addAction(UIAlertAction(title: "取消", style: .cancel))
        alert.addAction(UIAlertAction(title: "保存", style: .default) { _ in save() })
        present(alert, animated: true)
    }

    @objc func close() { dismiss(animated: true) }
}
