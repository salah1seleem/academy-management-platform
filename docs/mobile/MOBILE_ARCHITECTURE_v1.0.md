# معمارية Native Mobile v1.0

الحالة: **Phase A — Backend mobile auth منفذ؛ Flutter ومراحل B–H غير مكتملة**. الأساس `a9624dc537f121bd1732ab4c0f692ea753d70724`، الفرع `codex/15-native-mobile-app`. التفويض في [ملحق النطاق](../requirements/NATIVE_MOBILE_AUTHORIZATION_2026-10-04.md).

## تطبيق واحد وخادم قائم

Flutter iOS/Android عميل أصلي، لا WebView ولا استبدال Next.js. يتصل بنفس ASP.NET Core API/PostgreSQL؛ لا backend ثانٍ أو deployment لكل دور. منطق التجديد والتحصيل والإيصال والخصم والحضور والتقييم يبقى على الخادم. الويب يستمر بجلسة cookie وCSRF، والموبايل يرسل bearer مستقلًا. لم تُنشأ `apps/mobile` في هذه المرحلة.

الهيكل المستهدف: `lib/app` للـrouter والتطبيق، `core/api` و`core/auth` و`core/storage` و`core/theme` و`core/widgets` للمشترك، و`features/{auth,guardian,coach,owner,nutrition,subscriptions,evaluations,attendance}`. لا تختزن الواجهات قواعد الصلاحيات أو الأرقام المالية. Flutter المثبت محليًا `3.47.2`/Dart `3.13.2`؛ التثبيت موجود وليس دليل CI أو build تطبيق. تثبيت واعتماد packages وlockfile ضمن Phase C فقط. Xcode/iOS Simulator متاحان؛ Android SDK غير موجود محليًا.

## العقد المنفذ: `/api/v1/mobile/auth`

| Endpoint | المدخل/النتيجة |
|---|---|
| `POST /otp/request` | `phoneNumber` ← `202 {challengeId,demo}` لهوية فعالة لها Guardian/Coach/AcademyOwner |
| `POST /otp/verify` | `challengeId,phoneNumber,code,deviceName` ← credentials وعضويات مخولة فقط |
| `POST /refresh` | `refreshToken` ← access/refresh جديدان؛ القديمان باطلان |
| `GET /memberships` | bearer صالح ← قائمة العضويات الحالية للمستخدم نفسه |
| `POST /select-role` | bearer + `membershipId` ← جلسة محدّثة وتدوير الرمزين |
| `POST /logout` | bearer ← إلغاء الجهاز الحالي، `204` |
| `POST /revoke-all` | bearer ← إلغاء جلسات الموبايل لكل أجهزة المستخدم نفسه، `204` |

الـcredentials تتضمن `accessToken`, `refreshToken`, `accessExpiresAtUtc`, `refreshExpiresAtUtc`, `membershipId`, `displayName`, `memberships[{id,academyId,academyName,role}]`. الاسم بالعربية؛ رموز الأدوار عقد API وتترجمها الواجهة. `400` إدخال غير صالح، `401` فشل تحقق/جلسة، `403` اختيار أو صلاحية ممنوعة، `429` throttling، `501` SMS غير منفذ. استجابات credentials موسومة `no-store`؛ يمنع تسجيل tokens/OTP أو نسخها إلى analytics.

الأرقام المحلية و`+20` و`0020` والأرقام العربية/الفارسية تطبع إلى صيغة موحدة. الهاتف وحده لا يثبت هوية أو علاقة طفل. طلب OTP لا ينشئ مستخدمًا أو وصاية. الهوية المجهولة أو غير المؤهلة تُرفض برسالة عامة، لا قائمة أدوار قبل التحقق. fixed OTP يعمل في Demo فقط؛ تشغيل Seed/FixedOtp خارج Demo يرفض startup. Production بلا SMS يرجع `501` ولا يختلق إرسال رسالة.

OTP صالح خمس دقائق، يُستهلك مرة واحدة. إعادة الطلب تلغي التحدي السابق. خمسة أخطاء تقفل التحدي، وثلاثة طلبات لهاتف خلال 15 دقيقة، إضافةً إلى 30 طلب auth لكل IP/دقيقة. قفل transaction-scoped في PostgreSQL يسلسل محاولات الهاتف، فلا يفوز تحققان متزامنان. محدد IP الحالي in-process لنسخة خادم واحدة؛ التوسع والإعداد الصحيح للـtrusted proxy ومعايير SMS abuse بوابات إنتاج لاحقة.

## الجلسة والأدوار

الـaccess token opaque عشوائي 256-bit، مدته عشر دقائق، والـrefresh عشوائي مستقل بعمر مطلق ثلاثين يومًا لا يتجدد بلا حد. تُخزن SHA-256 فقط لكليهما في `MobileSessions` مع الجهاز والمستخدم والعضوية المختارة وsecurity stamp والتوقيت والإلغاء. قاعدة البيانات تُراجع في كل طلب؛ لا JWT طويل العمر بصلاحيات قديمة. الدور/الأكاديمية يُستنتجان من عضوية فعالة للمستخدم نفسه، مع فحص المستخدم والأكاديمية وsecurity stamp. المدرب لا يحصل على طب الطفل، وولي الأمر لا يحصل على لاعب غير مرتبط.

عضوية مؤهلة واحدة تُختار تلقائيًا؛ أكثر من عضوية تُصدر جلسة دون tenant، ولا يسمح لها إلا بقائمة العضويات والاختيار والإلغاء. لا اختيار لأعلى صلاحية ولا اتحاد للأدوار. بعد الاختيار تصل `membership_id` و`academy_id` من الخادم؛ `CurrentTenant` يعيد فحصهما، ولا يثق في header أو ID مقدم من العميل. تغيير الدور لاحقًا يدور credentials، ويبطل القديم. تغيير العضوية إلى دور غير مدعوم يرفض جلسة الموبايل.

Refresh يسلسل الرمز والجلسة داخل transaction ثم يعيد فحص العضويات؛ فوز واحد فقط للطلبين المتزامنين. إعادة استعمال refresh سابق تفشل ولا تعيد credentials جديدة. إلغاء الجلسة يمنع access وrefresh فورًا. إذا فُقد رد ناجح بعد التدوير ولم يصل الرمز الجديد للجهاز، فالاسترداد الآمن الحالي هو OTP جديد؛ لا نافذة replay مخفية.

Flutter لاحقًا يخزن refresh في Keychain/Keystore-backed secure storage فقط، وaccess في الذاكرة. عند التشغيل يقرأ refresh ويجدده ثم يوجه للدور؛ لا SharedPreferences للأسرار. استخدام refresh واحد جارٍ داخل العميل، وتحديث التخزين ذرّيًا قبل استبدال الحالة، ورفض retry آلي لعملية مالية دون مفتاح idempotency محفوظ. Logout يمسح التخزين والـcache حتى إن فشلت الشبكة مع عرض أن الإلغاء البعيد لم يتأكد؛ تغيير الحساب يمسح بيانات الأطفال السابقة. التطبيق يحترم 401/403 وينهي الجلسة/يعيد اختيار الدور، ولا يعرض بيانات cache لحساب آخر.

## توافق الويب وmigration

policy scheme يختار mobile عند وجود Authorization؛ bearer خاطئ **لا** يرجع إلى cookie صالح. تخطي CSRF محصور في principal مصادق عليه بواسطة mobile handler، وليس وجود header. Cookies تبقى محمية بـCSRF. Native لا يستخدم `/session/academy` لإنشاء cookie؛ يستخدم `select-role`.

Migration `20261003214541_NativeMobileSessions` تضيف جدولَي sessions/challenges وتغير uniqueness إلى `(AcademyId,UserId,Role)`. لا تعديل لـmigrations السابقة ولا حذف بيانات. مسارات guardian registration وcoach attendance/seed صارت تختار الدور المعني. Cookies الجديدة تحفظ membership، وsecurity stamp refresh يحتفظ بها؛ legacy cookie بلا membership يُرفض عند تعدد الأدوار بدل الخطأ أو تصعيد الصلاحية. اختيار academy في الويب يرفض حالة تعدد غير محسومة بدل اختيار أعلى دور. Downgrade بعد إنشاء multi-role ليس إجراء تشغيل عاديًا: unique القديم لا يقبل ذلك؛ يلزم قرار ترحيل بيانات صريح، لا حذف تلقائي.

## التصميم والبيانات والتشغيل المتبقي

Guardian يتبع صور المحادثة: خلفية داكنة وبطاقات وصور وتبويبات واضحة وRTL، مع الإجراءات الثلاثة. Coach/Owner يستخدمان teal/white المتوافق مع الطلب. التقرير يحتفظ بالمركز وradar الستة وبقية المعايير دون ملعب. الوجبات ستتغير وفق التصحيح المباشر؛ لا تُستورد صور المرجع أو علاماته أو أطفاله.

Demo عرض كرة قدم نظيف وreset وCoach creation في Phase B؛ Flutter/auth في C؛ Guardian في D؛ وجبات مصورة مع مصدر وحصة وقواعد معلوماتية غير طبية في E؛ Coach/Owner في F/G؛ CI Flutter ومراجعة iOS ومقاسات الهاتف في H. لا يعني نجاح auth أن أي شاشة native جاهزة.

اختبارات Phase A تعمل على قواعد PostgreSQL 17 مستقلة؛ لا reset لقاعدة العرض أو قواعد أخرى. HTTPS إلزامي للنشر الحقيقي، وHTTP loopback للديمو فقط؛ reverse proxy الموثوق وتخزين الأسرار وSMS والتوقيع والمتاجر والخصوصية والنسخ الاحتياطي والمراقبة وبوابة الأمان غير منجزة. يُمنع الادعاء بجاهزية الإنتاج أو حسابات المتاجر.

مرجع التصميم التقني: [ASP.NET Core authentication policy schemes](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/policyschemes?view=aspnetcore-10.0). الحالة والاختبارات المنفذة في [STATUS](../delivery/STATUS.md).
