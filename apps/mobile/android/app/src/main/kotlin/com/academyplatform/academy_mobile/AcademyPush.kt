package com.academyplatform.academy_mobile

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Notification
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Handler
import android.os.Looper
import com.google.firebase.FirebaseApp
import com.google.firebase.messaging.FirebaseMessaging
import com.google.firebase.messaging.FirebaseMessagingService
import com.google.firebase.messaging.RemoteMessage
import io.flutter.plugin.common.MethodChannel
import java.util.UUID

object AcademyPush {
    var channel: MethodChannel? = null
    var token: String? = null
    fun configured(context: Context): Boolean = try {
        FirebaseApp.getApps(context).isNotEmpty() || FirebaseApp.initializeApp(context) != null
    } catch (_: Exception) { false }
    fun state(activity: MainActivity, request: Boolean, result: MethodChannel.Result) {
        if (!configured(activity)) { result.success(mapOf("configured" to false)); return }
        if (Build.VERSION.SDK_INT >= 33 && activity.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
            if (request) activity.requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 4207)
            result.success(mapOf("configured" to true, "permission" to "denied")); return
        }
        if (token == null) FirebaseMessaging.getInstance().token.addOnSuccessListener { token = it; channel?.invokeMethod("tokenChanged", null) }
        result.success(mapOf("configured" to true,"permission" to "granted","provider" to "FCM","token" to token))
    }
    fun intent(context: Context, intent: Intent?) {
        val notice = intent?.getStringExtra("noticeId") ?: return
        val academy = intent.getStringExtra("academyId") ?: return
        try { UUID.fromString(notice); UUID.fromString(academy) } catch (_: Exception) { return }
        context.getSharedPreferences("iacademy_push",Context.MODE_PRIVATE).edit().putString("notice",notice).putString("academy",academy).apply()
        channel?.invokeMethod("intent",mapOf("noticeId" to notice,"academyId" to academy))
        intent.removeExtra("noticeId"); intent.removeExtra("academyId")
    }
    fun take(context: Context): Map<String,String>? {
        val prefs=context.getSharedPreferences("iacademy_push",Context.MODE_PRIVATE)
        val notice=prefs.getString("notice",null) ?: return null
        val academy=prefs.getString("academy",null) ?: return null
        prefs.edit().clear().apply();return mapOf("noticeId" to notice,"academyId" to academy)
    }
}
class AcademyMessagingService: FirebaseMessagingService() {
    override fun onNewToken(token: String) { Handler(Looper.getMainLooper()).post { AcademyPush.token=token;AcademyPush.channel?.invokeMethod("tokenChanged",null) } }
    override fun onMessageReceived(message: RemoteMessage) {
        val notice=message.data["noticeId"] ?: return
        val academy=message.data["academyId"] ?: return
        try {UUID.fromString(notice);UUID.fromString(academy)} catch (_: Exception) {return}
        if(Build.VERSION.SDK_INT>=33&&checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=PackageManager.PERMISSION_GRANTED)return
        val manager=getSystemService(NotificationManager::class.java)
        if(Build.VERSION.SDK_INT>=26)manager.createNotificationChannel(NotificationChannel("academy_matches","ملخصات المباريات",NotificationManager.IMPORTANCE_DEFAULT))
        val intent=Intent(this,MainActivity::class.java).putExtra("noticeId",notice).putExtra("academyId",academy).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP or Intent.FLAG_ACTIVITY_SINGLE_TOP)
        val pending=PendingIntent.getActivity(this,notice.hashCode(),intent,PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
        val builder=if(Build.VERSION.SDK_INT>=26)Notification.Builder(this,"academy_matches") else @Suppress("DEPRECATION") Notification.Builder(this)
        manager.notify(notice,0,builder.setSmallIcon(R.drawable.ic_stat_academy).setContentTitle("iAcademy One").setContentText("لديك ملخص مباراة جديد داخل التطبيق.").setContentIntent(pending).setAutoCancel(true).build())
    }
}
