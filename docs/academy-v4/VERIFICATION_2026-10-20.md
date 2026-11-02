# V4.1 — سجل التحقق الفعلي، 20 أكتوبر2026

النطاق18نقطة في [المصفوفة](MOBILE_FIRST_REFINEMENT.md)، فوق رأس البداية f1a8c3b. لا نتائج مفترضة ولا جمع مضلل لإعادات الاختبارات المتداخلة.

**تحديث بعد القبول الأول:** [إصلاحات الانحدار وأدلتها](POST_ACCEPTANCE_REPAIRS.md): Web667/667 وFlutter381/381، ثم اختبارات المتأثر بإصلاح CSV، وإغلاق47سيناريو متصفح فريدًا وإعادة12سيناريو مقاسات متأثرة. حفظ CSV داخل حوار iOS اكتمل فعليًا (33سجلًا/27930). الجدول التالي يوثق جولة القبول الأولى، وليس بديلًا عن التحديث أو CI للرأس الجديد.

## البيئة وحدود البيانات

- PostgreSQL16 محلي127.0.0.1:55443، cluster خاص داخل `tmp/v41-postgres/data`؛ قاعدتا `academy_v4_refinement_20261020` و`academy_v4_refinement_integration_20261020`. الخدمة القديمة المؤقتة5433 غابت بعد انقطاع الجلسة؛ لم تُحذف أو تُصلح أو reset قاعدة أخرى. خدمة5432 الموجودة غير مرتبطة ولم تتغير.
- Football tenant معلوم البصمة فقط لقبول الواجهات، وأكاديمية ثانية في اختبارات العزل.140migration مطبقة في كل قاعدة.3الجدد: `20261020044952_V4MobileExperience`، `20261020051931_V4TrainingNotesAndPageHelp`، `20261020064003_V4RoleScopedInbox`. EF لا يجد تغيّر نموذج معلق. لم ينفذ downgrade؛ حواجز Down تحمي البيانات الجديدة المحفوظة.
- API5260 وWeb3260 وMedia root داخلtmp منفصلة عن الديمو اليدوي. source من.env المحلي ثم overrides صريحة لهذه البيئة، بلا طباعة أسراره أو حفظه في Git. ReferenceDate2026-10-20 للاشتراكات الاصطناعية؛ رأس/فلاتر/شهر النجوم من ساعة الأكاديمية الحقيقية.
- ترخيص الإعلانات fixture اختباري صريح، محروس باسم قاعدة القبول وبصمة Football؛ بقية الموافقات عبر API وإصدارات إعدادات الأكاديمية العادية. لا شراء ترخيص أو تغيير إنتاج.

## النتائج المنفذة

|المجموعة|النتيجة الفعلية|السجل المحلي|
|---|---|---|
|API build|نجاح0أخطاء/0تحذيرات|`/tmp/academity-v41-api-final-build.log`|
|EF model check|No changes منذ آخرmigration|`/tmp/academity-v41-model-check.log`|
|Web production build، lint كامل المصدر، typecheck|نجاح، لا تجاهل أخطاء TS/lint|`/tmp/academity-v41-web-final-{build,lint,typecheck}.log`|
|Flutter analyze|No issues found|`/tmp/academity-v41-flutter-final-analyze.log`|
|.NET unit موجهة V41|14ناجحة|`/tmp/academity-v41-unit.log`|
|Flutter unit موجهة|2ناجحتان: أرقام/كسور المال وحساب الشهر/نهاية الشهر/الكبيسة|`/tmp/academity-v41-mobile-unit.log`|
|Web unit،4ملفات|16ناجحة،8منها عقد الرئيسية الجديد|`/tmp/academity-v41-web-final-unit.log`|
|Integration مجمعة V41 +VIP +Community +Cockpit|19ناجحة،0فشل،1دقيقة42ثانية|`/tmp/academity-v41-final-integration.log`|
|Regression مالية/إعلانات موجهة|5ناجحة،4Community متداخلة مع السابق و1financial golden إضافية؛ ليست24حالة مستقلة|`/tmp/academity-v41-regression.log`|
|Flutter فعلي S،iPhone16e،390×844|رحلة واحدة كاملة ناجحة30ثانية؛ teardown يظهر+2ولا يعني رحلتين|`/tmp/academity-v41-native-small-final.log`|
|Flutter فعلي T،iPhone17ProMax،440×956|رحلة موسعة ناجحة45ثانية شملت الهدف والتقارير وخبر draft/edit/publish؛ إعادة تسليم نهائية ناجحة46ثانية|`/tmp/academity-v41-native-typical-complete.log` و`/tmp/academity-v41-native-final-delivery.log`|
|Web E2E مجمعة نهائية|4/4ناجحة في28.3ثانية|`/tmp/academity-v41-web-delivery-e2e.log`|
|Main App العادي، لا harness|دخول OTP، More/Ads، Files picker حقيقي، رفع شعار اصطناعي، save draft، ثم Submit/Approve منiOS→Guardian Home|لقطة `evidence/mobile-refinement/guardian-live-ad-typical.jpg`|
|Health live/ready وWeb login|Healthy/Healthy وHTTP200 على5260/3260|أعيد فحصها عند التسليم؛ ليست Production probe|

اختبارات integration تثبت عزل صندوق التنبيهات بالأكاديمية والمستخدم والدور وحالة القراءة، وصول الإداري الضيق لتجربته الذاتية دون الإدارة، فيديو الدور/الشاشة الآمن، منح نجوم متزامنة وعدم تجاوز3وreplay وثبات التاريخ واستقلال الفرق ورفض الأكاديمية الأخرى، تاريخ الإعلان المحلي الشامل، تجميد اشتراك مع فترات منتهية قديمة، أهلية المكافأة وتسعيرها واستهلاكها وتنافس الإيصال، وحدود الإعلانات والرضا والموافقة والقياس والصورة الخاصة.

## السيناريو المتكامل ومطابقة المصادر

1. منح المدرب من واجهة Flutter فعلًا3نجوم للاعب تقييمه85في فريق، و2للاعب تقييمه87في فريقه الآخر؛ عدد الميزانية لكل فريق مستقل. ظهر85/3قبل87/2في أبرز اللاعبين على الموبايل وWeb، دون عرض composite كأنه تقييم. API concurrency اختبر خمسة طلبات متنافسة والرصيد المتبقي، وأعاد الطلب دون تكرار ومنع UPDATE/DELETE.
2. فتح المالك الفلاتر، حفظ هدف50000من التطبيق، فتح الشهر الحالي، وتحقق ظهور الهدف واختفائه في النطاق المتحرك/الفرع غير المطابق. قوائم الاشتراكات الأربع طابقت الملخص بنفس SportEnrollment، وحالةFrozen لم تتحول إلىexpired بسبب التاريخ.
3. مصروفات Rent5000،Electricity650،Water350،Maintenance450 مرت عبر إنشاء/اعتماد/Quote/Post. كشف راتب اعتمد وصُرفت دفعة1000ضمن سجل صرف موجود3500، فالإجمالي الحقيقي4500، لا1000فقط.
4. طلب منتج380وطلب بطولة250من ولي الأمر، رفع إثبات اصطناعي، قبول مالك؛ انتقلاConfirmed وانعكسا على المصادر المالية القائمة. الاشتراكات المحصلة27300؛ إجمالي التحصيل **27930**. المصروفات **10950**، الفائض التشغيلي النقدي **16980**؛ تطابق statement وcockpit والتحصيلات والتصدير. الفترة السابقة5400؛ المقارنة من مصادر فعلية لا أرقام UI.
5. اختيار الأسرة ذات الطفلين01099900101في تجديد المتابعة، رسالة مخصصة، معاينة بلا تغيير عدد الصندوق، ثم تأكيد واحد→سجل جديد واحد، قراءة محفوظة، ورفض وصول المالك لسجل الأسرة404. تفاصيل المكافآت وأثر الخصم علىQuote/Receipt/Review مغطاة بمحرك VIP القائم في8اختبارات تكامل، لا محاكاة نسبة ثابتة.
6. تعديل نتيجة منتخب2018إلى4–2من فورم الداشبورد، ثم ظهورها في تطبيق المالك وإحصاء الفريق؛ المدرب يرى فرقًا مسندة فقط ويُرفض فلتر فرع. الولي بدّل بين طفلين على S/T دون بقاء مساحة الطفل السابق. المدرّب والإداري أظهرا فعليًا «حصتان متبقيتان»/«الاشتراك انتهى»/الاحتياط التدريبي الاصطناعي المخول.
7. أخبار: إنشاء draft عربي منFlutter→تحرير→نشر→ظهور لولي الأمر. إعلانات: رفع منFiles وحفظ منMain App، تحرير النص العربي منWeb في نسخة جديدة دون توريث موافقة، تقديم واعتماد الفرع منMain App، ثم ظهور لأهل بالغين موافقين. القراءة/المعاينة ليست impression؛ cap حساب/يوم محترم ولم تمسح أحداثه للحصول على صورة.

## أوامر إعادة التحقق المستهدفة

من جذر المستودع، مع قاعدة الاختبارات المعزولة أعلاه فقط:

```sh
dotnet build apps/api/src/Academy.Api --no-restore
# Set ACADEMY_TEST_CONNECTION_STRING to the isolated integration database.
dotnet test tests/integration/Academy.IntegrationTests --filter 'FullyQualifiedName~V41_|FullyQualifiedName~V4_VIP_|FullyQualifiedName~Community_|FullyQualifiedName~V4_cockpit_'
dotnet test tests/unit/Academy.UnitTests --filter FullyQualifiedName~V41ExperienceTests
npm run lint -w @academy/web
npm run typecheck -w @academy/web
API_INTERNAL_URL=http://127.0.0.1:5260 ACADEMY_WEB_DIST_DIR=.next-refinement-final npm run build -w @academy/web
ACADEMY_V41_ISOLATED=true npx playwright test --config=tests/e2e/playwright.v41.config.ts
```

رحلة Webهذه تقبل بيئة العرض المجهزة فعليًا وليست bootstrap لقاعدة مجهولة: يلزم منح النجوم منرحلةFlutter وfixture الترخيص والإعلان المحفوظ/المعتمد بالطريقة المذكورة. لا تغيّرportsلتشغيلها على الديمو اليدوي. ملفSQLالمحروس في `tests/e2e/refinement/ads-license-fixture.sql`. جلسات OTP يعاد استخدامها داخل الـworker؛ للتشغيل المتكرر انتظر حد3طلبات/15دقيقة، لا تعطل الحماية أو تمسح سجلها.

من `apps/mobile`:

```sh
flutter analyze
flutter test test/v41_experience_test.dart
flutter drive --driver=test_driver/visual_driver.dart --target=integration_test/v41_mobile_refinement_test.dart -d 8123D59D-B8B5-418A-8B7A-68E53E07154E --dart-define=APP_ENV=Demo --dart-define=API_BASE_URL=http://127.0.0.1:5260 --dart-define=ISOLATED_DEMO=true --dart-define=CAPTURE_SIZE=typical --dart-define=EXTENDED_ACCEPTANCE=true
```

S نفذ علىiPhone16e معCAPTURE_SIZE=small ودونEXTENDED_ACCEPTANCE. المحاكي الصغير الذي أُنشئ لهذا القبول أوقف بعد الاختبار لتقليل الذاكرة؛ الاعتيادي وMain App أعيدا للتشغيل للمراجعة.

## إخفاقات كُشفت وصُححت، لا إخفاء نتائج

- Guid اختياري فارغ في روابط branch أعطى400؛ أُصلحت URL builders لتُسقط الحقل الفارغ، لا تخفيف تحقق الخادم.
- Frozen مع فترة قديمة منتهية ظهرت expired؛ أُصلح تعريف المصدر والقوائم واختبر عبر Freeze service القائم.
- Native test كان يضغط عنصرًا خارج lazy viewport، أو يحتفظ navigator لدور سابق، أو يجد عدة Scrollable؛ أُصلح harness بالتمرير ومفتاح هوية ومحدد رأسي، دون حذف assertions. أضيف ScaffoldMessenger لكل جلسة لتجنب تسرب snackbar بين الأدوار.
- اختبار نتيجة Web كان على تبويب بطولات، وfixture المصروف أعاد اعتماد مرحلة Posted، وupload توقع200بدل201؛ أُصلح مسار الاختبار وfixture بالتحقق من المصدر، لا تغيير محرك المال.
- VIP fixture افترض أهلية الطفلين مع أنه فعّل واحدًا؛ صار يفعلهما فعليًا قبل التنافس وبقيت assertions700/800. مجموعة اختبار مستقلة تمنع امتلاء المجموعة المشتركة عبر الإعادات، دون تخفيف قيد السعة.
- كشف TS أن exact غير مدعوم في TestingLibrary؛ استبدل بـregex ذي مثبتين لنفس المطابقة الدقيقة. ByLabel لم يلتقط تفاصيل محرر الإعلان؛ استعمل الاختبار دور textbox واسمه المتاح.
- جولة Web3/4 اصطدمت بحماية OTP429 في السيناريو الرابع، لا مشكلة إنترنت. أُعيد استعمال جلسة الهاتف داخل worker، وانتُظرت نافذة الحماية دون reset، ثم حققت الجولة4/4. محاولة EF الأولى بلا ConnectionString رُفضت قبل أي عمل؛ أُعيدت بالقاعدة المعزولة ونجحت.
- لوحة المفاتيح العربية عبر أتمتة macOS لم تُدخل النص كما يلزم؛ رُفعت الصورة وحُفظ الإعلان منiOS، ثم صححت نسخة النص العربي منWeb، واعتمدت وشوهدت في التطبيق العادي. إدخال العربية باختبار Flutter نجح في الأخبار؛ لا ادعاء إصلاح لوحة مفاتيح Mac العامة.

## ما لم يثبت

انحدار الوحدات الكامل للويب وFlutter نُفذ، ثم أعيد الفاشل والمتأثر؛ ليس ذلك ادعاء نجاح كل انحدار الخادم أو CI للرأس الجديد. لا Android أو جهاز مادي أو نشر Production. لا فيديو شرح حقيقي أو رابط قدمه المالك؛ URL.example.test في الاختبار إعداد اصطناعي فقط بلا طلب خارجي. حوار حفظ CSV الخاص بـiOS نُفذ يدويًا وتحقق الملف المحفوظ. المراجع الثلاث ومخرجات Claude غائبة، فلا ادعاء مطابقتها. لا SMS/push أو رسائل أو دفعات أو إعلانات خارجية. الصور لست شخصيات اصطناعية؛ بقية السجلات لها fallback مقصود.
