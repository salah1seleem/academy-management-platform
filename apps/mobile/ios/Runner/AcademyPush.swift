import Flutter
import UserNotifications
import UIKit

final class AcademyPush: NSObject {
  private var channel: FlutterMethodChannel?
  private var token: String?
  private var registrationFailed = false
  static let shared = AcademyPush()
  func install(_ messenger: FlutterBinaryMessenger) {
    channel = FlutterMethodChannel(name: "iacademy/mobile_push", binaryMessenger: messenger)
    channel?.setMethodCallHandler { [weak self] call, result in
      guard let self = self else { return }
      if call.method == "takeIntent" {
        let defaults = UserDefaults.standard
        let intent = defaults.dictionary(forKey: "iacademy.push.intent")
        defaults.removeObject(forKey: "iacademy.push.intent")
        result(intent); return
      }
      guard call.method == "state" || call.method == "request" else { result(FlutterMethodNotImplemented); return }
      let configured = Bundle.main.object(forInfoDictionaryKey: "IACPushEnabled") as? Bool == true
      guard configured else { result(["configured": false, "permission": "unavailable"]); return }
      func state() {
        UNUserNotificationCenter.current().getNotificationSettings { settings in
          DispatchQueue.main.async {
            let granted = settings.authorizationStatus == .authorized || settings.authorizationStatus == .provisional
            if granted && self.token == nil && !self.registrationFailed { UIApplication.shared.registerForRemoteNotifications() }
            result(["configured": true, "permission": granted ? "granted" : settings.authorizationStatus == .denied ? "denied" : "notDetermined", "provider": "APNs", "token": self.token as Any? ?? NSNull(), "registrationFailed": self.registrationFailed])
          }
        }
      }
      if call.method == "request" {
        UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .badge]) { _, _ in state() }
      } else { state() }
    }
  }
  func registered(_ data: Data) {
    token = data.map { String(format: "%02x", $0) }.joined()
    registrationFailed = false
    channel?.invokeMethod("tokenChanged", arguments: nil)
  }
  func failed() { registrationFailed = true; channel?.invokeMethod("tokenChanged", arguments: nil) }
  func received(_ payload: [AnyHashable: Any]) {
    guard let notice = payload["noticeId"] as? String, let academy = payload["academyId"] as? String,
      UUID(uuidString: notice) != nil, UUID(uuidString: academy) != nil else { return }
    let intent = ["noticeId": notice, "academyId": academy]
    UserDefaults.standard.set(intent, forKey: "iacademy.push.intent")
    channel?.invokeMethod("intent", arguments: intent)
  }
}
