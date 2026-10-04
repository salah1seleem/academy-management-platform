# Academy Native — checkpoint C

Flutter أصلي لـiOS/Android على API الأكاديمية القائم، وليس WebView. المنفذ: هاتف/OTP، اختيار عضوية صريح، session restore وrefresh/logout، وواجهة عربية RTL بخط Cairo محلي. شاشات الأعمال Guardian/Coach/Owner تأتي في D/F/G؛ الشاشة الحالية توضح أنها أساس مصادقة فقط.

## التشغيل

Flutter **3.47.2 stable** وDart **3.13.2**. من هذا المجلد:

```bash
flutter pub get --enforce-lockfile
flutter run --dart-define=APP_ENV=Demo --dart-define=API_BASE_URL=http://127.0.0.1:5190
```

عنوان API إلزامي، لا localhost مخفي داخل الكود. iOS Simulator: عنوان loopback للخادم المحلي. Android Emulator: `http://10.0.2.2:5080` مع API على 5080. هاتف حقيقي: عنوان LAN خاص للخادم وتهيئة جدار الحماية/صلاحية local network؛ لا تفتح قاعدة PostgreSQL للشبكة. Android HTTP المحلي متاح في debug فقط. `Production` هو الوضع الافتراضي ويتطلب HTTPS؛ لا تستخدم Demo credentials في الإنتاج.

حسابات العرض وإعداد API في [دليل الموبايل](../../docs/mobile/MOBILE_DEMO_GUIDE.md). API المحلي المستخدم للتحقق C هو 5190، بقاعدة football Demo منفصلة. لا SMS أو دفع حقيقي.

## أمان الجلسة

`flutter_secure_storage 11.2.0` مُثبت وlockfile محفوظ: iOS Keychain (unlocked/this-device-only)، وAndroid تخزين مشفر مرتبط بـKeystore مع تعطيل backup. refresh وحده يُحفظ، ومفتاحه معزول حسب environment/origin؛ access والعضويات في الذاكرة. refresh متزامن واحد، وحفظ الرمز الجديد قبل قبول الجلسة. logout لا يدّعي النجاح إذا تعذر مسح التخزين؛ عند فشل الشبكة بعد المسح يظهر تنبيه أن الإلغاء البعيد غير مؤكد. استعادة الجلسة لا تحذف refresh بسبب عطل شبكة مؤقت. ردود متأخرة بعد logout/تبديل الدور لا تُقبل. HTTP لا يتبع redirect ولا يسجّل tokens؛ GET فقط يُعاد مرة بعد 401، ولا replay آلي للكتابة.

المصادر: [secure storage](https://pub.dev/packages/flutter_secure_storage)، [Cairo/OFL](https://github.com/google/fonts/tree/main/ofl/cairo). ملف الترخيص مرفق مع الخط. لا بيانات أطفال أو صور من المرجع ضمن التطبيق.

## التحقق

```bash
flutter analyze
flutter test
flutter build ios --simulator --debug --dart-define=APP_ENV=Demo --dart-define=API_BASE_URL=http://127.0.0.1:5190
flutter test integration_test/auth_smoke_test.dart -d <simulator-id> --dart-define=API_BASE_URL=http://127.0.0.1:5190
```

اختبار integration يستخدم مفتاح Keychain منفصلًا، ويتحقق من OTP والاستعادة والدور وGET /me وlogout للأدوار الثلاثة على الخادم الفعلي. لا يغير سجلات اللاعبين. Android SDK غير متاح محليًا: لا ادعاء ببناء/اختبار Android. التوقيع والمتاجر والإنتاج غير جاهزة؛ bundle identifier الحالي للديمو وليس اعتمادًا للنشر.
