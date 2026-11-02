package com.academyplatform.academy_mobile

import android.content.ComponentName
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import io.flutter.embedding.android.FlutterFragmentActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel
import java.util.UUID

class MainActivity : FlutterFragmentActivity() {
    override fun onNewIntent(intent: Intent) { super.onNewIntent(intent);setIntent(intent);AcademyPush.intent(this,intent) }
    private val iconPrefs by lazy { getSharedPreferences("iacademy_launcher_icon", MODE_PRIVATE) }
    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        AcademyPush.channel=MethodChannel(flutterEngine.dartExecutor.binaryMessenger,"iacademy/mobile_push")
        AcademyPush.channel?.setMethodCallHandler { call,result ->
            when(call.method) {
                "state" -> AcademyPush.state(this,false,result)
                "request" -> AcademyPush.state(this,true,result)
                "takeIntent" -> result.success(AcademyPush.take(this))
                else -> result.notImplemented()
            }
        }
        AcademyPush.intent(this,intent)
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "iacademy/launcher_icon").setMethodCallHandler { call, result ->
            when (call.method) {
                "state" -> result.success(mapOf("supported" to true, "keys" to listOf("professional-demo"), "currentKey" to iconPrefs.getString("key", null), "academyId" to iconPrefs.getString("academy", null), "pending" to iconPrefs.contains("pending"), "build" to (packageManager.getPackageInfo(packageName, 0).let { if (Build.VERSION.SDK_INT >= 28) it.longVersionCode.toInt() else @Suppress("DEPRECATION") it.versionCode })))
                "set" -> {
                    val key = call.argument<String>("key")
                    val academy = call.argument<String>("academyId")
                    val validAcademy = try { UUID.fromString(academy); true } catch (_: Exception) { false }
                    if (key != null && (key != "professional-demo" || !validAcademy)) result.error("unavailable", "الأيقونة غير متاحة في هذا البناء.", null)
                    else {
                        // Apply at backgrounding: launcher replacement must not
                        // interrupt a form or kill the visible Flutter activity.
                        iconPrefs.edit().putString("pending", key ?: "standard").putString("pendingAcademy", academy).apply()
                        result.success(null)
                    }
                }
                else -> result.notImplemented()
            }
        }
    }
    override fun onStop() {
        super.onStop()
        val pending = iconPrefs.getString("pending", null) ?: return
        val professional = pending == "professional-demo"
        val standardName = ComponentName(this, "$packageName.LauncherStandard")
        val professionalName = ComponentName(this, "$packageName.LauncherProfessional")
        val enabled = PackageManager.COMPONENT_ENABLED_STATE_ENABLED
        val disabled = PackageManager.COMPONENT_ENABLED_STATE_DISABLED
        try {
            if (Build.VERSION.SDK_INT >= 33) packageManager.setComponentEnabledSettings(listOf(
                PackageManager.ComponentEnabledSetting(standardName, if (professional) disabled else enabled, PackageManager.DONT_KILL_APP),
                PackageManager.ComponentEnabledSetting(professionalName, if (professional) enabled else disabled, PackageManager.DONT_KILL_APP)))
            else {
                packageManager.setComponentEnabledSetting(if (professional) professionalName else standardName, enabled, PackageManager.DONT_KILL_APP)
                packageManager.setComponentEnabledSetting(if (professional) standardName else professionalName, disabled, PackageManager.DONT_KILL_APP)
            }
            iconPrefs.edit().putString("key", if (professional) pending else null).putString("academy", if (professional) iconPrefs.getString("pendingAcademy", null) else null).remove("pending").remove("pendingAcademy").apply()
        } catch (_: Exception) { /* Retain pending request and report it on next foreground. */ }
    }
}
