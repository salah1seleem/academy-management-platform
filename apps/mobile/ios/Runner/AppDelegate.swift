import Flutter
import UIKit
import UserNotifications

@main
@objc class AppDelegate: FlutterAppDelegate, FlutterImplicitEngineDelegate {
  override func application(
    _ application: UIApplication,
    didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?
  ) -> Bool {
    return super.application(application, didFinishLaunchingWithOptions: launchOptions)
  }

  override func application(_ application: UIApplication, didRegisterForRemoteNotificationsWithDeviceToken deviceToken: Data) {
    AcademyPush.shared.registered(deviceToken)
    super.application(application, didRegisterForRemoteNotificationsWithDeviceToken: deviceToken)
  }
  override func application(_ application: UIApplication, didFailToRegisterForRemoteNotificationsWithError error: Error) {
    AcademyPush.shared.failed()
    super.application(application, didFailToRegisterForRemoteNotificationsWithError: error)
  }
  override func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse, withCompletionHandler completionHandler: @escaping () -> Void) {
    AcademyPush.shared.received(response.notification.request.content.userInfo)
    completionHandler()
  }

  func didInitializeImplicitFlutterEngine(_ engineBridge: FlutterImplicitEngineBridge) {
    GeneratedPluginRegistrant.register(with: engineBridge.pluginRegistry)
    AcademyPush.shared.install(engineBridge.applicationRegistrar.messenger())
    UNUserNotificationCenter.current().delegate = self
    let channel = FlutterMethodChannel(name: "iacademy/launcher_icon", binaryMessenger: engineBridge.applicationRegistrar.messenger())
    channel.setMethodCallHandler { call, result in
      let app = UIApplication.shared
      let defaults = UserDefaults.standard
      let icons = (Bundle.main.infoDictionary?["CFBundleIcons"] as? [String: Any])?["CFBundleAlternateIcons"] as? [String: Any] ?? [:]
      if call.method == "state" {
        result(["supported": app.supportsAlternateIcons, "keys": icons["ProfessionalDemo"] == nil ? [] : ["professional-demo"], "currentKey": app.alternateIconName == "ProfessionalDemo" ? "professional-demo" : NSNull(), "academyId": defaults.string(forKey: "iacademy.icon.academy") as Any? ?? NSNull(), "build": Int(Bundle.main.infoDictionary?["CFBundleVersion"] as? String ?? "0") ?? 0, "pending": false])
      } else if call.method == "set" {
        let args = call.arguments as? [String: Any] ?? [:]
        let key = args["key"] as? String
        let academy = args["academyId"] as? String
        guard app.supportsAlternateIcons, app.applicationState == .active,
          key == nil || (key == "professional-demo" && icons["ProfessionalDemo"] != nil && UUID(uuidString: academy ?? "") != nil) else {
          result(FlutterError(code: "unavailable", message: "تعذر تغيير الأيقونة في هذه اللحظة.", details: nil)); return
        }
        let previousIcon = app.alternateIconName
        app.setAlternateIconName(key == nil ? nil : "ProfessionalDemo") { error in
          DispatchQueue.main.async {
            if let error = error {
              NSLog("iAcademy alternate icon failed: %@ / %ld", (error as NSError).domain, (error as NSError).code)
              let failure = FlutterError(code: "icon", message: "تعذر تطبيق الأيقونة. حاول مجددًا.", details: nil)
              // Some OS failures update preferredIconName before rejecting the
              // request. Restore the previous public-API choice, without recording
              // the failed request as the user's installed selection.
              if key != nil && app.alternateIconName != previousIcon {
                app.setAlternateIconName(previousIcon) { _ in DispatchQueue.main.async { result(failure) } }
              } else { result(failure) }
              return
            }
            if key == nil { defaults.removeObject(forKey: "iacademy.icon.academy") }
            else { defaults.set(academy, forKey: "iacademy.icon.academy") }
            result(nil)
          }
        }
      } else { result(FlutterMethodNotImplemented) }
    }
  }
}
