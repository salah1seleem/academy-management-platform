# Academy Management Platform

منصة مستقلة عربية أولاً لإدارة الأكاديميات، تشمل لوحة إدارة وFlutter أصليًا للولي والمدرب والمالك. هذه نسخة عمل محلية تضيف مراجعة المالك **CH-016..022** إلى البرنامج المتكامل عند HEAD `086a52f4eb409b55c9d36c6846a499c71e61312c`. التنفيذ الحالي وبوابات التحقق وحدود الإنتاج في [STATUS](docs/delivery/STATUS.md)؛ لا يُستنتج Production Ready من إمكانية تشغيل Demo.

[معمارية الموبايل](docs/mobile/MOBILE_ARCHITECTURE_v1.0.md) · [دليل checkpoint](docs/mobile/MOBILE_DEMO_GUIDE.md) · [النطاق الإضافي](docs/requirements/NATIVE_MOBILE_AUTHORIZATION_2026-10-04.md). المسار الجديد يدعم OTP لـGuardian/Coach/Owner، جلسات أجهزة مع تدوير وإلغاء، واختيار عضوية/دور صريحًا. cookie/CSRF الويب وواجهاته باقية.

## ما الموجود الآن؟

- [Flutter](apps/mobile/README.md): هاتف/OTP، اختيار الدور، تدوير/logout وKeychain/Keystore، وتجارب أعمال للولي والمدرب والمالك بثيم موحد Dark/Light وعربي/English وتنقل سفلي وonboarding.

- [واجهة أكاديمية رياضية](docs/ui/DASHBOARD_UI_SYSTEM.md): مؤشرات حقيقية قابلة للنقر، طلبات الدفع أولًا، فلاتر مشتركة وقائمة مختصرة. مراجعة المالك الحالية تشمل workflows مالية وتشغيلية وmigrations إضافية، وليست تغيير قراءة/ألوان فقط.
- ASP.NET Core Web API مع `/health/live` و`/health/ready`.
- هوية The Professional Academy للعرض، واسم/شعار/ألوان مستقلة لكل Academy بإدارة مشغّل منصة مخوّل. إعدادات الأكاديمية تجمع حسابات التحويل وسياسة حضور المدرب؛ رسوم البطولة والمباريات اليدوية مستقلة عن اشتراك التدريب. [تفاصيل التحديث](docs/delivery/BRANDING_MATCHES_EXECUTION.md).
- EF Core وNpgsql مع ASP.NET Core Identity وعضويات Academy معزولة وجلسات browser محفوظة على الخادم.
- Next.js login للإدارة (`Owner/Admin` فقط) وshell عربي RTL، مع تبديل Academy مخوّل من الخادم وPWA manifest/icon. لا دخول داشبورد للمدرب أو ولي الأمر.
- PostgreSQL 17 محلي عبر Docker Compose.
- Demo كروي مستقل حتمي وreset صريح محروس، وprofile متعدد الرياضات منفصل لاختبارات الانحدار والعزل؛ OTP ثابت في `Demo` فقط.
- فروع ورياضات وفئات ومجموعات وجداول أسبوعية وإسناد مدرب، مع Player مستقل عن `SportEnrollment` وروابط وصاية صريحة.
- Dashboard عربي RTL: قوائم وسياق لاعب ثابت، تسجيل مدفوع بباقة ومجموعة وصورة اختيارية، وروابط وصاية صريحة. هوية اللاعب وحدها لا تثبت اشتراكًا نشطًا.
- باقات `Duration/Sessions/Combined` وفترات تاريخية وتجديد وتحصيل وإيصال ذريون idempotent. تجديد الغير في Flutter يبدأ ببحث مصادق ومحدود ثم مرجع مشفر قصير العمر، بلا وصول للطفل. كود Web التاريخي يبقى متوافقًا.
- `TrainingSession` فعلية مولّدة دون تكرار من الجدول الأسبوعي أو منشأة يدويًا، وحضور لاعبين وجهاز فني منفصلان. `Present` يخصم حصة واحدة فقط من `Sessions/Combined` المؤهل، والتصحيح يعيدها بحركات audit append-only.
- تعديلات فترة الاشتراك لـOwner/Admin: تجميد/استئناف `Duration` و`Combined`، إضافة/خصم الأيام، وإلغاء فترة واحدة، مع سجل `SubscriptionAdjustment` append-only وidempotency وتعارض متفائل. لا تعديل لتحصيل أو إيصال ولا refund ضمن هذه الإجراءات، وولي الأمر يرى الحالة المبسطة لطفله المرتبط.
- خصم يدوي واحد لكل `RenewalRequest` بنوع `Percentage` أو `FixedAmount` يطبقه Owner/Admin قبل تأكيد الدفع مع سبب وسجل `RenewalDiscountAdjustment` append-only. تغيير الخصم يلغي طلب الدفع المعلق ويصدر طلبًا جديدًا بالمبلغ النهائي؛ ولي الأمر يرى السعر الأصلي والخصم والإجمالي فقط ولا يستطيع تعديلها.
- تجديد Owner/Admin للاعب وتسجيل رياضي قائمين من البحث أو الاشتراكات الحالية أو ملف اللاعب، باستخدام محرك التجديد والدفع نفسه دون ازدواج لاعب/تسجيل أو أثر مالي قبل تأكيد الدفع. الإنشاء idempotent ومحمي من طلبين مفتوحين متنافسين، ومتوافق مع الخصم والدفع التجريبي والإيصال.
- معايير تقييم خاصة بالرياضة، مسودات ودرجات 0–100 ونشر immutable. تقرير كرة القدم يحسب ستة محاور من snapshots الخادم ويعرض radar وقيمًا نصية وتاريخًا لولي الأمر؛ السباحة لها criteria مستقلة بلا radar كرة قدم.
- رئيسية ولي أمر عربية mobile-first تجمع الطفل مرة واحدة وتعرض رياضاته دون تكرار، مع الإجراءات الثلاثة: اشتراك جديد، تجديد، وتجديد للغير. ملف الطفل يعرض التقرير المنشور والجدول والحضور والاشتراكات من البيانات المحفوظة.
- `NewEnrollmentRequest` بباقة وإثبات تحويل ومراجعة إدارية وإسناد مجموعة؛ لا تفعيل قبل القبول. إعداد InstaPay/محفظة لكل أكاديمية، ونسخ ورفع إيصال وإشعار داخلي. المسار التاريخي غير المدفوع لا يختلق تحصيلًا.
- منتجات رياضية بسعر ومخزون وشراء من الإدارة أو Flutter للولي، ومراجعة تحويل وتقرير مبيعات. التأكيد يخصم المخزون مرة واحدة؛ لا شحن أو سلة عامة أو بوابة دفع حية.
- مكتبة تغذية معلوماتية مصورة واقتراحات يومية حتمية حسب العمر واليوم/التدريب، مع مصدر وحصة وحالة مراجعة؛ لا تجارة أو وصفة علاجية. [مصادر وحدود القيم](docs/nutrition/YOUTH_FOOTBALL_NUTRITION_DEMO_DATA.md).
- خصومات إخوة/مدرسة مستمرة، استرداد جزئي بسجل مستقل، أخبار مسودة/نشر بمدة ظهور، حضور مدرب يومي وخصم تأخير معتمد وصلاحيات فردية يضبطها المالك.
- سجلات إصابة/استشارة factual ينشئها Owner/Admin وينشرها صراحة؛ Guardian يرى المنشور فقط لطفله المرتبط ولا يرى `StaffNotes`، وCoach بلا وصول طبي. معرض الطفل يتبع قاعدة النشر والربط نفسها.

المتطلبات الحاكمة في [`docs/requirements/CURRENT_REQUIREMENTS.md`](docs/requirements/CURRENT_REQUIREMENTS.md)، وحالة التنفيذ الدقيقة في [`docs/delivery/STATUS.md`](docs/delivery/STATUS.md).

## 1. المتطلبات على الجهاز

- Git.
- .NET 10 SDK؛ تم التحقق باستخدام `10.0.401`.
- Node.js 24 LTS وnpm؛ تم التحقق باستخدام Node `24.21.0` وnpm `11.19.0`.
- Docker Desktop أو Docker Engine مع Compose لتشغيل PostgreSQL 17.

افتح Terminal ونفّذ كل الأوامر التالية من جذر المستودع:

```bash
cd "/Users/omar/Downloads/Academy Management Platform "
```

## 2. الإعداد لأول مرة

أنشئ ملف الإعداد المحلي. الملف `.env` مستبعد من Git:

```bash
cp .env.example .env
set -a
source .env
set +a
```

استعد الأدوات والحزم المقفلة:

```bash
dotnet tool restore
dotnet restore AcademyManagementPlatform.slnx
npm ci
```

## 3. تشغيل PostgreSQL

تأكد أن Docker يعمل، ثم:

```bash
docker compose --env-file .env -f infra/local/compose.yaml up -d postgres
docker compose --env-file .env -f infra/local/compose.yaml ps
```

لا تستخدم `down -v` إلا إذا كنت تقصد حذف بيانات قاعدة التطوير المحلية.

## 4. تطبيق migrations يدويًا

لا يطبق API migrations تلقائيًا عند التشغيل. بعد تحميل `.env` نفّذ:

```bash
dotnet ef database update \
  --project apps/api/src/Academy.Infrastructure \
  --startup-project apps/api/src/Academy.Api
```

آخر migration حاليًا `20261007225559_AcademyBrandingAndMatches`، إضافة إلى سبع migrations لمراجعة المالك. migrations الهوية والاشتراك والتغذية السابقة محفوظة كما هي. لا تعدّل migrations مطبقة أو تحذف بيانات لتسهيل downgrade.

## 5. تشغيل Demo آمن محليًا

العرض الافتراضي الآن هو الديمو الكروي المنفصل الموثق في [دليل Mobile Demo](docs/mobile/MOBILE_DEMO_GUIDE.md): رياضة واحدة، 3 فروع، 4 مدربين و30 لاعبًا. بيانات الشرائح السابقة في `LegacyRegression` للاختبارات فقط، وليست رحلة العرض الحالية. لا تنسخ حسابات Demo إلى Production. عدّل `.env` محليًا فقط:

```bash
ASPNETCORE_ENVIRONMENT=Demo
Demo__SeedEnabled=true
Demo__SeedProfile=Football
Demo__FixedOtpEnabled=true
Demo__FixedOtp=246810
Demo__StaffPassword='Demo-Only-123!'
Demo__ReferenceDate=2026-09-28
Payments__InternalTest__Enabled=true
Payments__InternalTest__SigningKey='Demo-Test-Signing-Key-Only-123456'
```

يرفض API البدء إذا فُعّل seed/fixed OTP أو بوابة الدفع الداخلية خارج بيئة مسموحة. بوابة الاختبار لا تحرك أموالًا حقيقية، ولا تعرض أو تحاكي Apple Pay أو provider إنتاجيًا. لا توجد خدمة SMS أو payment provider فعلية في هذه الشريحة.

## 6. تشغيل API

في Terminal أول، من جذر المستودع:

```bash
set -a
source .env
set +a
dotnet run --project apps/api/src/Academy.Api --no-launch-profile
```

تحقق من الصحة في Terminal آخر:

```bash
curl -i http://127.0.0.1:5080/health/live
curl -i http://127.0.0.1:5080/health/ready
```

`live` يجب أن ينجح ما دامت عملية API تعمل. `ready` ينجح فقط عندما يستطيع API الاتصال بـPostgreSQL.

## 7. تشغيل Web/PWA

في Terminal مستقل، من جذر المستودع:

```bash
export PATH="/opt/homebrew/opt/node@24/bin:$PATH"
npm run dev:web -- --hostname 127.0.0.1
```

افتح `http://127.0.0.1:3000/login`. مالك العرض `owner.football@example.test` والإداري `admin.football@example.test` بكلمة Demo `Demo-Only-123!`. ولي الأمر الأول `01099900100`/OTP `246810` لديه طفل واحد؛ الثاني `01099900101` لديه طفلان في Flutter. ابدأ بطلبات الدفع واللاعبين والمجموعات؛ إعداد حسابات التحويل من طلبات الدفع للمالك فقط. لا شكشوكة/كشري ولا شراء للتغذية. ما حسمته المراجعة من قرارات تجارة المنتجات/الدفع موثق في [DECISIONS](docs/architecture/DECISIONS_v1.0.md)، وبقية القرارات المفتوحة لا تصبح معتمدة ضمنيًا.

الفقرات التالية توثق رحلات الشرائح **التاريخية** على `Demo__SeedProfile=LegacyRegression` في قاعدة اختبار مستقلة. أسماء عمر/مريم والرياضات المتعددة فيها ليست بيانات العرض الكروي الجديد؛ بعض روابط الاشتراكات باقية مباشرة لكن أُزيلت من القائمة. التقارير الآن للمالك افتراضيًا، إلا عند منحة صريحة للموظف.

لتجربة Slice 8A، ادخل كمالك وافتح «لوحة المالك»، ثم «التقرير المالي» لتصفية التحصيلات المؤكدة حسب الفترة/الرياضة/الفرع/الباقة وتنزيل CSV عربي حقيقي. كإداري افتح «التقارير ← تقارير الحضور»، اختر سبتمبر 2026 وسنة الميلاد 2019 ومجموعة كرة القدم، ثم صدّر CSV؛ يعرض التقرير سجلات الحضور المحفوظة فقط ولا يستنتج الغياب من سجل مفقود. كولي أمر افتح «الإيصالات»، اختر إيصالًا واضغط «طباعة الإيصال» لاستخدام طباعة المتصفح العربية؛ لا يتم إنشاء PDF أو فاتورة ضريبية.

لتجربة Slice 8B، ادخل كإداري وافتح «الاشتراكات ← الاشتراكات الحالية» ثم تفاصيل فترة عمر. جرّب التجميد بتاريخ `2026-09-27` ثم سجل حضورًا أثناء التجميد لمشاهدة التحذير بلا خصم، وبعدها الاستئناف بتاريخ `2026-09-28` لمشاهدة تمديد النهاية يومًا. من التفاصيل نفسها جرّب إضافة 5 أيام ثم خصم يومين، وراجع «سجل التعديلات». يمكن إلغاء فترة مريم التاريخية مع بقاء رابط الإيصال وإجمالي التقرير المالي كما هو. كولي أمر افتح ملف الطفل ثم رابط حالة الاشتراك لرؤية الحالة والتاريخ المبسطين دون أسباب أو بيانات الموظف.

لتجربة Slice 8C، ابدأ طلب تجديد كولي الأمر ثم ادخل كـOwner/Admin وافتح «الاشتراكات ← طلبات التجديد ← عرض وإدارة الخصم». طبّق نسبة أو مبلغًا ثابتًا مع سبب؛ سيُلغى checkout القديم ويُنشأ بديل بالمبلغ النهائي. ارجع كولي الأمر إلى checkout الحالي، راجع السعر الأصلي والخصم والإجمالي، نفّذ الدفع التجريبي ثم راجع الإيصال والتقرير المالي. بيانات Demo تشمل تجديدًا مدفوعًا بلا خصم، وآخر مدفوعًا بنسبة، ومعلقًا بمبلغ ثابت، ومحاولة فاشلة بخصم.

لتجربة Slice 8D، ادخل كـOwner/Admin واختر الإجراء «تجديد اشتراك» أو افتح «الاشتراكات الحالية» واضغط «تجديد». ابحث باسم اللاعب أو الكود أو هاتف ولي الأمر، اختر التسجيل الرياضي والباقة، وراجع البداية المتوقعة والمبلغ ثم أنشئ طلب الدفع. يمكنك تطبيق خصم من تفاصيل طلب التجديد، ثم فتح طلب الدفع التجريبي ومحاكاة النجاح ومراجعة الإيصال والفترة الجديدة. كولي أمر، راجع التجديد وcheckout والنجاح/الفشل والإيصال بصياغة عربية؛ `/` يوجه تلقائيًا حسب الجلسة، وPWA يبدأ من `/login`. التطبيق installable/responsive ببيانات manifest، لكنه لا يدعم العمل دون اتصال ولا يحتوي Service Worker.

صور Demo محلية اصطناعية؛ التغذية تستخدم صور الوجبات الموثقة، لا صور أطفال حقيقية. يتوفر رفع خاص للصور/الفيديو/إثبات الدفع إلى تخزين محلي مستبعد من Git، مع تحقق صلاحيات عند كل قراءة. هذا ليس تخزينًا production-grade؛ التخزين الدائم والنسخ الاحتياطي وفحص البرمجيات الخبيثة وفك الترميز الكامل بوابات إنتاج لاحقة.

## 8. تشغيل الاختبارات

مع PostgreSQL يعمل وبعد تحميل `.env`: عرّف `ACADEMY_TEST_CONNECTION_STRING` باتصال قاعدة اختبار مستقلة، وليس قاعدة العرض التي تراجعها. الاختبارات تغير بياناتها.

```bash
test -n "$ACADEMY_TEST_CONNECTION_STRING"
dotnet build AcademyManagementPlatform.slnx --configuration Release
dotnet test AcademyManagementPlatform.slnx --no-build --configuration Release
npm run typecheck
npm run lint
npm run test:web
npm run build:web
```

لتشغيل E2E أول مرة، نزّل Chromium الاختباري ثم شغّل الاختبار. يشغّل Playwright الـAPI والويب تلقائيًا إذا لم يكونا يعملان:

```bash
npm exec --workspace @academy/e2e playwright install chromium
npm run test:e2e
```

## 9. إيقاف البيئة المحلية

أوقف API والويب بـ`Ctrl+C` في نافذتيهما، ثم:

```bash
docker compose --env-file .env -f infra/local/compose.yaml down
```

## هيكل المستودع

```text
apps/api/          API وPersistence foundation
apps/web/          Next.js Arabic RTL PWA shell
apps/mobile/       Flutter native للولي والمدرب والمالك
tests/             unit, integration, architecture, e2e
infra/local/       PostgreSQL Docker Compose
docs/              requirements, architecture, delivery
```

لا يوجد حتى الآن: بوابة دفع/SMS/push إنتاجية أو استرداد آلي عبر بنك، شحن/ضرائب/سلة متجر عامة، تجارة تغذية، media storage إنتاجي، نظام رواتب كامل، Communications/bot/ranking/AI Reports أو advanced BI، revision/supersede UI، PDF receipts، signing والمتاجر أو إثبات production-readiness. تفاصيل النطاق المنفذ والاختبارات في STATUS.
