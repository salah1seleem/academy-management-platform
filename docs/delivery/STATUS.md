# Status

تاريخ التحديث: 2026-10-04. الفرع الحالي: `codex/15-native-mobile-app`، أساسه الكامل `a9624dc537f121bd1732ab4c0f692ea753d70724` من `codex/14-slice8d-demo-closure`.

## Master Execution — checkpoint UI، البرنامج الكامل PARTIAL

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
