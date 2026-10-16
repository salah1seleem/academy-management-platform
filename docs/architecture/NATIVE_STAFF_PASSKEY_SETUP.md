# إعداد التحقق الأصلي لحضور الموظف — E2c3a

## حدود التنفيذ

التطبيق الحالي يستخدم `passkeys` 2.23.1 لاستدعاء iOS AuthenticationServices، والخادم القائم هو الذي يتحقق من challenge وRP/origin وUV والتوقيع والعضوية ونسخة الفرع والفترة. لا خدمة Corbado مستضافة، ولا local_auth Boolean بديل، ولا تخزين بصمة أو وجه. `debugMode:false` يمنع تشغيل تشخيص SDK الاختياري. لا يعني UV استخدام بصمة تحديدًا، ولا يمنع مزامنة passkey أو تزوير GPS.

التفعيل **off افتراضيًا**، وAndroid محجوب في هذا adapter حتى إعداد origin/certificate وDigital Asset Links وفحصها صراحة. تشغيل iOS Simulator الحالي على HTTP localhost لا يكفي لإثبات التسجيل الأصلي أو Face ID مادي. `nativeIosOriginConfigured` مجرد توافق إعداد API مع HTTPS/RP، وليس فحص AASA أو entitlement أو شهادة التطبيق.

## مدخلات staging المطلوبة — ليست إعدادات موجودة مفترضة

- نطاق HTTPS مملوك ومراجع لـRP، وشهادة صالحة؛ ليس IP أو localhost أو نطاقًا تجريبيًا غير مملوك.
- Apple Team ID وApp ID/provisioning profile مخولان بميزة Associated Domains. Bundle ID الحالي `com.academyplatform.academyMobile`؛ يجب استعمال المعرّف الفعلي للتطبيق الموقع، لا تخمين Team ID.
- ملف `apple-app-site-association` صالح على خدمة HTTPS التابعة لـRP، يعلن `webcredentials.apps` بالقيمة `TEAM_ID.BUNDLE_ID` الفعلية. لا ملف placeholder ينشره هذا المستودع تلقائيًا.
- جهاز iOS فعلي مخول للاختبار، وحساب موظف/نقطة فرع synthetic في بيئة staging معزولة. لا بيانات أطفال حقيقية أو تحصيل أو رسائل فعلية.

لم تتوفر هذه المدخلات في إعداد الديمو المفحوص؛ لا يُستبدل غيابها بتجاوز TLS أو origin أو إثبات مصطنع. الباقي غير المحجوب من الخطة يستمر ولا يتوقف كله عليها.

## خطوات الإعداد عند توفيرها

1. راجع إعداد API القائم `StaffPasskeys__Enabled=true` و`StaffPasskeys__RpId=<owned-rp>` و`StaffPasskeys__Origins__0=https://<owned-rp>` في staging فقط. API transport يحتاج HTTPS صالحًا كذلك؛ قد يختلف مضيفه عن RP وفق ضبط الخدمة. لا توسع قائمة origins لتخطي خطأ ارتباط.
2. فعّل Associated Domains على App ID/profile، وانشر AASA الفعلي. وفق [Apple](https://developer.apple.com/documentation/authenticationservices/connecting-to-a-service-with-passkeys)، يجب ربط `webcredentials:<owned-rp>` بالتطبيق وخدمة RP في الاتجاهين.
3. `apps/mobile/ios/Runner/StaffPasskeys.entitlements` قالب **اختياري غير مربوط بالبناء الافتراضي**. في إعداد Xcode الخاص بـstaging عيّن `CODE_SIGN_ENTITLEMENTS=Runner/StaffPasskeys.entitlements` وbuild setting `STAFF_PASSKEY_RP_ID=<owned-rp>` مع Team/profile الصحيحين. لا تعيّن هذا الملف للتطبيق غير المهيأ أو تنشر قيمة فارغة. راجع entitlements الفعلية الناتجة بعد التوقيع.
4. ابنِ نفس Flutter app باستخدام `--dart-define=STAFF_PASSKEYS_ENABLED=true --dart-define=STAFF_PASSKEY_RP_ID=<owned-rp>` إضافةً إلى `APP_ENV` و`API_BASE_URL` المخولين. Dart defines لا تهيئ entitlement أو AASA من تلقاء نفسها.
5. من «وسائل التحقق الخاصة بي» أضف وسيلة ثم جربها. كلاهما لا يسجل حضورًا. ثم من «حضوري وانصرافي» اختر الفرع المهيأ وسجل حضورًا ثم انصرافًا. إثبات العملية الجديد مربوط بطلبها، وليس Verify أو LastVerifiedAt السابقين.

لا تنفذ هذه الخطوات ضد Production دون تفويض مستقل. [مرجع المكتبة](https://github.com/corbado/flutter-passkeys) و[الإصدار](https://pub.dev/packages/passkeys/versions/2.23.1)؛ إعدادات البيئة وحدها لا تكفي لتأكيد دعم الجهاز.

## القبول المتبقي على الجهاز

اختبر التسجيل/التجربة/الحضور/الانصراف الحقيقيين، الإلغاء/رفض الجهاز، رفض خارج النطاق وحدوده والدقة/القراءة القديمة، تغير الحساب/الفرع/السياسة، المفتاح الموقوف، وفقد الرد. أعد قراءة الفترة نفسها في Web وFlutter دون حضور طفل أو مال أو خصم تلقائي. تحقق من RP/origin الفعليين في الاستجابة الموقعة ومن رفض هوية/أكاديمية أخرى. نجاح المحاكي في رفض RP غير مرتبط ليس نجاح هذه الرحلة الإيجابية.

الموقع عند الضغط فقط، timestamp من القراءة الفعلية، ولا background GPS. الأمر الموقع المعلق يحتفظ به في الذاكرة لإعادة **نفس JSON** عند غموض الرد؛ تغيير السياق/إغلاق الشاشة يتلفه. لا يخزن proof أو raw GPS في ملف أو secure storage. رفض HTTP لا يعرض نجاحًا؛ القراءة بعد الإيصال لا تعيد كتابة المصدر. عند فقد الطلب من الذاكرة تُحدّث حالة الخادم قبل عملية جديدة، ولا يعاد اصطناع nonce أو userHandle.

الاستثناء الإداري القائم متاح، ومسؤول الفرع/Owner يراجع دون اعتماد طلبه بنفسه. لا يملك Coach تعديل الراتب أو حضور اللاعبين بسبب حضوره الوظيفي. تخصيص الإلزام الفردي وتقييم التأخير الموقع والقبول الموحد ما زالت أعمالًا مستقلة غير مغلقة بهذه الشريحة.
