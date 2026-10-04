# Status

تاريخ التحديث: 2026-10-04. الفرع الحالي: `codex/15-native-mobile-app`، أساسه الكامل `a9624dc537f121bd1732ab4c0f692ea753d70724` من `codex/14-slice8d-demo-closure`.

## Master Execution — checkpoint C؛ البرنامج الكامل PARTIAL

- حُفظت كل أعمال `cb7ff342164d02530a6f222d5bebd957b833752e`. Phase B مدفوعة بالـcommit `4076bcddf1ca087b30d495e1190cf1ad173d3fe4`؛ [CI الخاص بها](https://github.com/salah1seleem/academy-management-platform/actions/runs/37168028902) نجح في backend/frontend/e2e بما فيه رحلتا Create Coach الجديدتان.
- `apps/mobile` تطبيق Flutter أصلي: **3.47.2 stable / Dart 3.13.2**، Cairo محلي مع OFL، RTL وtheme/widgets مشتركة، هاتف/OTP وعضوية/دور صريح وواجهة جلسة حقيقية. شاشات الأعمال D/F/G لا تُحاكى؛ shell الحالي يوضح أنها لم تُنفذ بعد.
- `flutter_secure_storage 11.2.0` مثبت مع lockfile. refresh وحده في Keychain (this-device-only)/Keystore، access والبيانات في الذاكرة؛ فصل مفاتيح البيئة/origin، single-flight refresh، حفظ قبل قبول التدوير، epoch ضد الردود المتأخرة، لا retry آلي للكتابة. network failure لا يمحو جلسة محفوظة؛ logout يمسح محليًا مع تنبيه إذا تعذر الإلغاء البعيد، وفشل مسح التخزين ليس نجاحًا وهميًا.
- إعداد API صريح؛ Production افتراضي ويتطلب HTTPS. HTTP المحلي فقط في Demo/Development ولعناوين loopback/LAN الخاصة المتحققة؛ لا fallback سري لـlocalhost ولا إعادة توجيه تحمل bearer. Android backup معطل. إعدادات iOS Keychain موصولة بالبناء.
- **flutter analyze PASS، Flutter unit/widget 16/16 PASS**، **iOS Simulator debug build PASS**. اختبار integration أصلي واحد غطى **Guardian + Coach + Owner** على API/PostgreSQL حقيقي: OTP، تخزين Keychain، restore/rotation، `/me` بالدور الصحيح، logout ومسح التخزين؛ نجح وأعيد بعد تقوية حماية الردود المتأخرة. فُحصت شاشة الدخول Cairo/RTL فعليًا على iPhone 17 Pro Max/iOS 26.2؛ هذه ليست المراجعة المرئية الكاملة للشاشات والمقاسات في H.
- أضيف job Flutter إلى CI مع الإبقاء على jobs الويب/backend؛ نتيجته تُراجع بعد push. لا إعادة اختبار كاملة للويب بسبب C وحدها: لم يتغير كود الويب/backend بعد checkpoint B ذي 379 اختبار backend مغطى و120 Web و50 E2E. لا migration أو تغيير baseline/main/بيانات الإنتاج.
- API المخصص للتحقق يعمل على 5190 بقاعدة `academy_football_review_20261004`، دون مساس بخدمات 5080/3000 القديمة أو قواعد المستخدم. لا Android build/test لأن SDK غير مثبت. التوقيع والمتاجر وSMS والدفع الحقيقي ووسائط الإنتاج خارج الجاهزية.

**نقطة التسليم عند حد هذه الدفعة: B ثم C محفوظتان؛ المتبقي D → E → F → G → H.** لم يُنفذ Guardian native التجاري أو وجبات Phase E وصورها/مصادرها ومحركها أو Coach/Owner native التجاري. التغذية ما زالت 3 أمثلة مؤقتة غير مراجعة، بلا قيم مختلقة أو تجارة. **NOT NATIVE DEMO READY / NOT PRODUCTION READY**. لا يُعاد A أو UI أو B أو C؛ تبدأ المتابعة من D على HEAD الحالي دون reset/rebase. [دليل التشغيل](../../apps/mobile/README.md).

## Master Execution — سجل checkpoint B (من الأساس المصحح cb7ff34)

Phase UI مكتملة ومحفوظة؛ لم يُنفذ reset/revert/rebase ولم يُعاد تصميم اللوحة. أضيفت فقط واجهة إنشاء المدرب ضمن النظام البصري الحالي وبيانات العرض الكروي.

- `Football` هو profile العرض الافتراضي؛ tenant جديد محجوز بعلامة ثابتة، لا حذف للأكاديميات القديمة. رياضة واحدة، 3 فروع بالأسماء المطلوبة، 6 مجموعات، 4 مدربين و30 لاعبًا فريدًا مع 29 ولي أمر. الحضور والتقييمات والاشتراكات والمدفوعات والإيصالات سجلات مترابطة، وتاريخ مرجعي صريح. اختبارات الانحدار والعزل تختار `LegacyRegression` صراحة ولا تعطل دعم multi-sport.
- DM-12: أمر `--reset-football-demo` مع تأكيد `football-demo-only`، بيئة Demo حصراً، ID/slug/marker ثابتة، transaction + advisory lock، حذف child-first لبيانات tenant المعروف فقط وإعادة seed. يحافظ على المستخدمين وكلمات مرورهم وأي أكاديمية أخرى، ويزيل جلسات tenant العرض. لا HTTP reset endpoint ولا drop/truncate. نُفذ CLI فعليًا على قاعدة العرض الجديدة فقط؛ قواعد المستخدم السابقة محفوظة.
- Create Coach: Owner/Admin فقط، CSRF، هاتف مطبع، حساب فردي بلا كلمة مرور مشتركة، إعادة استخدام هوية متطابقة دون تعديلها، عضوية وإسناد مجموعات داخل الأكاديمية ذريًا، ومنع تكرار الطلب المتزامن. المدرب الجديد النشط مؤهل لمسار OTP القائم؛ SMS الحقيقي غير منفذ.
- تحقق backend: الجولة الشاملة **375 integration + 1 unit + 1 architecture PASS**، ثم مجموعة B النهائية **13/13 PASS** بعد إضافة اختبارَي التزامن وإعادة استخدام الهوية. الإجمالي الفريد المغطى **379**. لم تُخفَ المحاولات الأولى: صُححت توقعات endpoint/رفض OTP للحساب المعطل وanalyzer الاختبار قبل النجاح.
- Web: **120/120 PASS** باستخدام عاملَي اختبار محليًا بعد timeout أولي تحت ضغط التوازي؛ typecheck/lint/production build PASS، و`npm audit --omit=dev`: صفر vulnerabilities. رحلة B الجديدة **2/2 E2E PASS** على 1440 و390، مع OTP للمدرب ورفض تقرير المالك. فُحصت screenshots فعليًا وصُححت أحجام checkboxes في النموذج الجديد ثم أُعيد التحقق.
- لا migration جديدة ولا pending EF model changes؛ آخر migration تبقى `20261003214541_NativeMobileSessions`. لا تغيير baseline/approval أو main، ولا ملفات أسرار/صور أطفال/بيانات حقيقية.
- التغذية في B **ثلاثة أمثلة مؤقتة فقط**، SVG توضيحي وقيم null و`DemoUnreviewed`؛ ليست مكتبة أطفال مصورة مدروسة ولا محرك توصيات. البحث والتوثيق والصور والحصص في E، ولا شكشوكة/كشري أو تجارة تغذية.

Web E2E الشامل **48/48 PASS** على قاعدة اختبار مستقلة ومنافذ 5191/3191؛ مع حالتَي B يصبح الإجمالي **50/50**. استُخدمت نسخة production محلية بعد رفض Next تشغيل dev ثانٍ؛ خادم المستخدم 3000 لم يُوقف. CI يُراجع بعد push دون ادعاء نجاح مسبق. المتبقي: **C ثم D ثم E ثم F ثم G ثم H**. لا Flutter أو native visual review بعد في هذا checkpoint؛ **NOT NATIVE DEMO READY / NOT PRODUCTION READY**. [الحسابات وreset والحدود](../mobile/MOBILE_DEMO_GUIDE.md).

## Master Execution — سجل checkpoint UI السابق

أساس هذه الدفعة `d90063912632cbdbcf9976f5944738bf349b5175` (Phase A المكتمل). لم تُعد كتابة mobile auth. التالي هو **Phase B**، وليس تسليم Native كامل.

- أعيد تصميم Dashboard الموظفين بطابع أكاديمية رياضية: teal/emerald/gold، هوية الأكاديمية والحساب، hero، بطاقات اللاعبين والمدربين والمجموعات والباقات، الجلسات والحضور والتقييمات، وتحسين التقارير والنماذج والبحث. الرسوم والمؤشرات من سجلات الخادم، بلا نسب نمو أو إجماليات ثابتة. حالات الصفر تبقى صفرًا، والاشتراكات القريبة من الانتهاء لا تُعرض كجزء منفصل غير متداخل من الإجمالي.
- أضيفت animations CSS خفيفة وskeleton وaccordion مع `inert` ودعم `prefers-reduced-motion`؛ دون مكتبة حركة جديدة. نمط الملعب الزخرفي داخل hero الموظفين فقط؛ تقرير الطفل لا يحتوي مخطط ملعب. صور الهوية محلية Demo فقط مع بديل الأحرف، لا صور أطفال حقيقية أو تحميل من مضيف خارجي.
- تعديلات Backend **إسقاطات قراءة إضافية فقط** للـDTO الحالي: سياق التسجيلات/الاشتراكات في قائمة اللاعبين، روابط أولياء الأمور الفعلية وتاريخ العرض في ملف اللاعب، أسماء المدربين والمواعيد وأعداد التسجيلات في البطاقات. كل استعلام مقيد بالأكاديمية والصلاحيات القائمة. لا تغيير commands أو قواعد دفع/اشتراك/حضور/تقييم/هوية؛ لا migration جديدة ولا pending EF model changes.
- الإجراءات السريعة العاملة: التسجيل والتجديد والحضور والتقييم والمال. **إضافة مدرب جديد بحساب فردي غير منفذة بعد**؛ القائمة الحالية تعرض المدربين وإسناد المجموعات فقط. تُضاف رحلة provisioning والإجراء السريع في B، بلا زر وهمي الآن.
- تحقق مكتمل قبل الحفظ: backend **366/366** (364 integration + 1 unit + 1 architecture)، web **116/116**، typecheck وlint وproduction build ناجحة، `npm audit --omit=dev`: **0 vulnerabilities**. أضيفت 4 اختبارات backend لإسقاطات القراءة والعزل و9 اختبارات web للهوية/المؤشرات/القوائم والملف.
- المراجعة البصرية الفعلية: Owner (dashboard، players، subscriptions، reports)، Admin (players، groups، attendance، evaluations، content)، Coach (sessions، attendance، evaluations) عند **1440×900 و1280×800 و390×844**؛ ومراجعة إضافية لملف اللاعب وقائمة المدربين. صُحح تداخل البحث وضيق أعمدة 1280px. صور محلية داخل `tmp/` لا تُرفع إلى Git؛ سيناريوهات الالتقاط التسعة محفوظة في `tests/e2e/specs/sports-dashboard.spec.ts` وتُرفق بالاختبار.
- E2E النهائي: **48/48 PASS** على قاعدة قبول مستقلة بعد التأكد من `/health/ready`، منها 9 حالات Dashboard تغطي الأدوار/المقاسات. لا تُعد المحاولات الأولية نجاحًا. اختبارات التجميد/التجديد تغيّر بياناتها، لذلك لا تصلح إعادة نفس المجموعة على قاعدة مستخدمة باعتبارها seed نظيفًا. أُصلحت selectors وانتظار اكتمال login في اختبارات التقييم، وطلب العزل صار نسبيًا لنسخة API قيد الاختبار. نتيجة CI تُراجع بعد push وتُذكر بدليلها في handoff.
- لا تغييرات للـbaseline/approval ولا `.env` أو أسرار أو artifacts مولدة في commit. لم يُعدل `main`؛ قواعد البيانات السابقة محفوظة دون reset/drop. خدمات الاختبار الحالية 5180/3100 مستقلة عن خدمات العرض القديمة 5080/3000؛ لا ندعي أن القديمة تعرض هذا الإصدار.

**غير منفذ في هذه الدفعة:** B تنظيف الديمو الكروي وreset/Create Coach؛ C Flutter/auth/secure storage؛ D Guardian native؛ E الوجبات الجديدة وصورها ومصادرها ومحرك الاقتراحات؛ F Coach native؛ G Owner native؛ H تحقق Flutter والمحاكيات وCI الخاص بها. لا `apps/mobile` ولا native build/test أو رحلة Simulator بعد. Flutter المحلي **3.47.2** وDart **3.13.2**، Android SDK غير متاح. لا ادعاء أن ديمو 1 رياضة/3 فروع/4 مدربين/~30 لاعب أصبح جاهزًا؛ لا إعادة اعتماد وجبات التغذية القديمة كوجبات الأطفال المطلوبة. **NOT NATIVE DEMO READY / NOT PRODUCTION READY**.

نقطة المتابعة: تنفيذ B كوحدة متماسكة مع حماية reset ببيئة Demo وtenant معروف، والحفاظ على بيانات الاختبارات متعددة الرياضات منفصلة عن العرض الكروي، ثم C→H بالترتيب. إغلاق هذه الدفعة وفق استراتيجية checkpoints، لا إعلان اكتمال البرنامج.

## Native Mobile — سجل checkpoint A السابق

- أُضيف auth backend للموبايل: phone OTP محروس ببيئة Demo، تطبيع الأرقام العربية/المحلية، انتهاء واستهلاك مرة واحدة وحدود محاولات/طلبات. لا SMS حقيقي.
- `MobileSessions` تخزن hash للـaccess والـrefresh، بعمر 10 دقائق/30 يومًا، مع تدوير ذري وlogout جهاز وrevoke-all وفحص المستخدم/العضوية/الأكاديمية/security stamp. التزامن لا يصدر نجاحين لنفس OTP أو refresh.
- أدوار متعددة للهوية نفسها مع اختيار membership صريح، دون أعلى صلاحية تلقائية أو دمج أدوار. cookies الويب تحفظ membership، وtenant resolution يفشل بأمان عند الغموض. bearer غير صالح لا يرجع إلى cookie، وCSRF يبقى مطلوبًا للويب.
- Migration الجديدة فقط: `20261003214541_NativeMobileSessions`. طُبقت على قاعدتي اختبارات مستقلتين محليًا بـPostgreSQL **17.11**، لا على قاعدة العرض القديمة. لا pending model changes. لا تغييرات migrations قديمة أو reset أو وصول لبيانات منتج آخر.
- Seed أضاف أرقام Owner/Coach الاصطناعية إلى الهويات الموجودة عندما يكون الهاتف فارغًا، بلا استبدال هوية أو كلمة مرور أو رقم معدّل. هذا ليس تنظيف Demo Phase B.
- [معمارية الموبايل](../mobile/MOBILE_ARCHITECTURE_v1.0.md)، [دليل checkpoint](../mobile/MOBILE_DEMO_GUIDE.md)، و[تفويض النطاق الإضافي](../requirements/NATIVE_MOBILE_AUTHORIZATION_2026-10-04.md) توثق ما نُفذ وما لم يُنفذ. الـbaseline وapproval الأصليان بلا تعديل.
- تحقق محلي مكتمل: backend **362/362** (360 integration + 1 unit + 1 architecture)، منها **30** حالة mobile auth جديدة؛ اختبارات الويب **107/107**، typecheck وlint وproduction build ناجحة؛ `npm audit --omit=dev`: **0 vulnerabilities**؛ روابط الوثائق الجديدة و`git diff --check` سليمة. لا pending EF model changes. نتيجة CI تُراجع بعد push لهذا checkpoint؛ لا ندعيها مسبقًا.
- خدمات العرض القديمة على 5080/3000 لم تُوقف أو يُدّعَ أنها تشغّل إصدار native الجديد. لم يُشغّل E2E محليًا على قاعدة العرض الملوثة؛ التحقق المعزول مطلوب في CI.
- لا تطبيق `apps/mobile` بعد، ولا native build أو Simulator journey أو visual review أو صور وجبات جديدة أو تغذية توصيات منفذة. لذلك **NOT NATIVE DEMO READY / NOT PRODUCTION READY**.

المتبقي بالترتيب: **B** تنظيف Demo كرة القدم وreset آمن/Create Coach؛ **C** Flutter/auth/secure storage؛ **D** Guardian؛ **E** التغذية والوجبات والصور والمصادر والقواعد؛ **F** Coach؛ **G** Owner؛ **H** Flutter CI والاختبارات والمراجعة المرئية. آخر تصحيح واجب: نفس تصميم صور Guardian مع وجبات أطفال مختلفة **بلا شكشوكة أو كشري**، وبلا تجارة أو تشخيص غذائي. ملف مصادر التغذية يُنشأ عند تنفيذ E، لا توجد قيم جديدة ندعي توثيقها الآن. Android SDK غير مثبت، بينما Flutter وXcode/iOS Simulator متاحان. نقطة المتابعة التالية هي Phase B، وليس إعادة كتابة auth أو اعتماد الديمو كاملًا.

## سجل التسليم السابق

Slice 8D أُنجز على `codex/14-slice8d-demo-closure`؛ أساسه التاريخي `9fc885d9c8a10e4832443b060c1ba227de92e099`، وناتجه الكامل هو أساس برنامج native أعلاه. الأحكام أدناه تخص الويب وقت تسليمها، لا اكتمال برنامج native.

## Slice 8D — Demo Closure منفذ فعليًا

- أصبح Owner/Admin قادرًا على البحث عن لاعب قائم واختيار `SportEnrollment` قائم وباقة متوافقة ثم إنشاء `RenewalRequest` و`PaymentRequest` فقط. الرحلة لا تنشئ Player أو Guardian link أو Enrollment جديدًا، ولا تنشئ Collection/Receipt/Period قبل نجاح الدفع.
- جُمعت قواعد الإنشاء في `RenewalCreationService` المشترك بين Guardian وAdmin، مع تحقق tenant/sport، وsnapshot مالي، و`Idempotency-Key`، وقفل PostgreSQL transaction-scoped يمنع طلبين مفتوحين متنافسين للتسجيل نفسه. نجاح `InternalTestPaymentGateway` يعيد استخدام معالج الدفع القائم لإنشاء Collection وReceipt وSubscriptionPeriod ذريًا.
- أضيفت مداخل التجديد من الاشتراكات الحالية وتفاصيل اللاعب/الفترة والإجراء السريع، وصفحات اختيار/مراجعة ودفع تجريبي للإداري. `AcademyOwner` و`AcademyAdmin` فقط؛ Coach وGuardian ممنوعان، والبحث/التفاصيل tenant-scoped.
- خصومات Slice 8C متوافقة: ينشئ الإداري الطلب أولًا، ثم يطبق الخصم من شاشة الطلب؛ يُستبدل PaymentRequest المعلق ويظل `FinalAmount` هو مبلغ الدفع والتحصيل والإيصال والتقرير. قواعد التجديد المبكر بعد `EndDate` المعدل والمنتهي/الملغي لم تتغير.
- أصبحت العملة والتواريخ وأنواع الباقات وحالات الدفع/التجديد/الفترات تعرض عبر formatters عربية مشتركة. أزيلت القيم الإنجليزية الخام من رحلات Guardian الأساسية، وأُصلح input التقييم ومداخل خطة الاشتراك لمنع انتقال uncontrolled/controlled.
- `/` أصبح مدخلًا واعيًا بالجلسة يوجّه غير المسجل إلى `/login` وGuardian إلى `/guardian` والموظف إلى `/dashboard`. أصبح `manifest.start_url=/login`. لا يوجد Service Worker أو offline support ولا ادعاء بذلك.
- `AT-002`: **PASS** بعد اختبارات التحويلات العربية، الجذر/manifest، ورحلات Mobile Chromium. `AC-SUB-004`: **IMPLEMENTED**. عوائق الديمو P0 المتبقية: **0**.
- الحكم الحالي: **READY FOR FINAL DEMO REVIEW**، وليس Production Ready. `DM-12` يبقى **PARTIAL** لأن أمر reset آمن ومقيد بسجلات Demo لم يُنفذ وهو خارج Slice 8D؛ لذلك يظل جرد السيناريوهات `11 READY / 1 PARTIAL`. عناصر P1 المفتوحة من التدقيق: **7**.

## Full MVP Completion Audit — سجل تاريخي سابق لـSlice 8D

- اكتمل تدقيق الأدلة للمتطلبات `117/117` ومعايير القبول `34/34` وسيناريوهات الديمو `12/12`، شاملًا معالجة Premium Dashboard UI، من دون تنفيذ application code أو تغيير baseline.
- الحكم الحالي: **NOT DEMO READY** حتى تنفيذ admin renewal وإغلاق اتساق العربية/PWA المتبقي؛ والحكم الإنتاجي **NOT PRODUCTION READY**.
- التقرير الكامل: [MVP_COMPLETION_AUDIT_2026-09-29.md](MVP_COMPLETION_AUDIT_2026-09-29.md).
- المهمة التالية الموصى بها فقط: `Slice 8D — Demo Closure: Admin Renewal + Guardian Arabic/PWA Cleanup`؛ لم تبدأ.

## Dashboard UI remediation — منفذ فعليًا

- أعيد تصميم واجهات الموظفين `Owner` و`Admin` و`Coach` فقط بنظام عربي موحد: خط Cairo، شريط جانبي متجاوب، رؤوس صفحات وأزرار وشارات وفلاتر وجداول/بطاقات وإجراءات صف وحوارات وحالات تحميل/فراغ/خطأ قابلة لإعادة الاستخدام.
- أعيد تنظيم لوحة المالك من البيانات الحقيقية القائمة، والقوائم والتقارير والنماذج وصفحات التفاصيل، دون تغيير API أو الحسابات أو الصلاحيات أو قواعد الاشتراكات والحضور والتقييم.
- أضيف `lucide-react` كاعتماد واجهة خفيف ومثبت، ووثقت القواعد في `docs/ui/DASHBOARD_UI_SYSTEM.md`. بقيت Guardian PWA مستقرة عدا الخط المشترك.
- روجعت الواجهات بصريًا للأدوار الثلاثة عند 1440 و1280 و1024 و768 و390، مع منع overflow للصفحة واستخدام drawer وبطاقات الهاتف عند المقاسات المناسبة. نجح typecheck وlint وproduction build و92/92 اختبار واجهة و35/35 E2E و295/295 اختبار backend، و`npm audit --omit=dev` بلا ثغرات؛ تُثبت نتيجة CI في handoff بعد push.

## Slice 8C — منفذ فعليًا

- أضيف snapshot مالي إلى `RenewalRequest`: السعر الأصلي، نوع/قيمة الخصم، قيمة الخصم، المبلغ النهائي، العملة، السبب والمنفذ/التوقيت. `RenewalDiscountAdjustment` tenant-scoped وappend-only يحفظ old/new وسبب كل تطبيق/تغيير/إزالة.
- Owner/Admin فقط يملكان `SubscriptionDiscountManage`. كل mutation يتطلب CSRF و`Idempotency-Key` وسببًا ونسخة `xmin`، ويعيد 409 للتعديل القديم. Guardian وCoach ممنوعان، وtenant/resource lookup يمنع IDOR.
- يدعم default التقني خصمًا واحدًا `Percentage` أو `FixedAmount`. الخادم يثبت `OriginalAmount` من سعر الباقة ويحسِب `DiscountAmount` و`FinalAmount`؛ لا يقبل قيمًا مالية نهائية من الواجهة.
- عند تغيير الخصم قبل الدفع يُلغى `PaymentRequest` المعلق ويصدر بديل بالمبلغ النهائي. callback لا يقبل إلا محاولة الدفع الحالية المطابقة لـ`RenewalRequest.FinalAmount`. الدفع الناجح وحده ينشئ Collection واحدة وإيصالًا وفترة؛ الفشل/الإلغاء بلا أثر مالي، وretry يحتفظ بالـsnapshot.
- الإيصال وcheckout يعرضان السعر الأصلي والخصم والمدفوع، والتقرير المالي ولوحة المالك يجمعان `Collection.Amount` الفعلية فقط. المسار نفسه يعمل لتجديد الغير دون منح وصول لملف المستفيد.
- migration أمامية `20260929015730_Slice8CSubscriptionDiscounts` ترحّل السجلات القديمة إلى original=final=المبلغ السابق، وتضيف القيود والعلاقات دون تعديل أي migration سابقة.
- Demo الحتمي يتضمن: مدفوع بلا خصم، مدفوع بنسبة 10%، معلق بخصم ثابت، وفشل بخصم. لا يضيف إيرادًا وهميًا ويظل مقيدًا ببيئة Demo.

## الحدود والقرار

- `OD-001` ما زال **PENDING / NOT APPROVED**. المنفذ default يدوي reversible وليس سياسة تجارية معتمدة.
- لا coupons، promo codes، campaigns، stacking، loyalty، refunds/chargebacks، خصومات متجر، provider حقيقي، اتصالات، bot، ranking، deployment أو Slice 9.
- خصومات كتالوج المنتجات metadata منفصلة تمامًا عن خصم تجديد الاشتراك، والتغذية بلا أي سعر أو خصم أو أثر مالي.

## دليل التحقق

- نجح Release build بلا تحذيرات أو أخطاء، وطبقت سلسلة migrations كاملة حتى `20260929015730_Slice8CSubscriptionDiscounts` على PostgreSQL حقيقي في schema معزولة، وأكد EF عدم وجود pending model changes.
- Backend: **295/295** (`293` integration + `1` unit + `1` architecture). ضمنها **31/31** حالة Slice 8C PostgreSQL للحساب والصلاحيات والعزل والتزامن/idempotency والدفع/الإيصال/التقارير والتجديد للغير وثبات التاريخ.
- Frontend: **89/89**، مع نجاح typecheck وlint وproduction build (`42/42` static-generation items) و`npm audit --omit=dev` بنتيجة **0 vulnerabilities**.
- Mobile Chromium E2E: **35/35**، منها الرحلات الأربع الجديدة للخصم. شُغلت المجموعة النهائية على schema جديدة بعد تطبيق كل migrations.
- تُسجل نتيجة GitHub Actions ورابطها في handoff بعد push؛ لا تُفترض قبل انتهاء التشغيل البعيد.

## نقطة التوقف

تتوقف المهمة عند Slice 8C بعد commit/push ونجاح CI. لا يُدمج أو يُعدل `main` ولا يبدأ Slice 9.
