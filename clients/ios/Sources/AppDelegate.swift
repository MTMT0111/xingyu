import UIKit

@main
class AppDelegate: UIResponder, UIApplicationDelegate {
    var window: UIWindow?
    let weChat = XYWeChatBridge()
    private var navigation: UINavigationController?

    func application(_ application: UIApplication, didFinishLaunchingWithOptions options: [UIApplication.LaunchOptionsKey: Any]?) -> Bool {
        let window = UIWindow(frame: UIScreen.main.bounds)
        self.window = window
        navigation = UINavigationController()
        window.rootViewController = navigation
        installClient()
        window.makeKeyAndVisible()
        return true
    }

    func installClient() {
        let configuration = ClientConfiguration.load()
        if configuration.sharingConfigurationValid { _ = weChat.configure(appID: configuration.appID, universalLink: configuration.universalLink) }
        else { _ = weChat.configure(appID: "", universalLink: "") }
        if let current = navigation?.viewControllers.first as? WebViewController,
           current.updateConfigurationIfSameService(configuration) { return }
        if configuration.serverURL != nil {
            navigation?.setViewControllers([WebViewController(configuration: configuration, weChat: weChat, onConfigure: { [weak self] in self?.showSettings() })], animated: false)
        } else {
            navigation?.setViewControllers([ConfigurationViewController(configuration: configuration, onSave: { [weak self] in self?.installClient() })], animated: false)
        }
    }

    private func showSettings() {
        let configuration = ClientConfiguration.load()
        let settings = ConfigurationViewController(configuration: configuration, onSave: { [weak self] in
            self?.navigation?.dismiss(animated: true) { self?.installClient() }
        })
        settings.navigationItem.leftBarButtonItem = UIBarButtonItem(title: "返回", style: .plain, target: settings, action: #selector(ConfigurationViewController.close))
        navigation?.present(UINavigationController(rootViewController: settings), animated: true)
    }

    func application(_ app: UIApplication, open url: URL, options: [UIApplication.OpenURLOptionsKey: Any] = [:]) -> Bool { weChat.handle(url: url) }
    func application(_ application: UIApplication, continue userActivity: NSUserActivity, restorationHandler: @escaping ([UIUserActivityRestoring]?) -> Void) -> Bool { weChat.handle(activity: userActivity) }
}
