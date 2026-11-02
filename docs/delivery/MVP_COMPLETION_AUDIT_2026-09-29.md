# تدقيق اكتمال الـMVP وجاهزية العرض — 2026-09-29

**الأساس المراجع:** `codex/dashboard-premium-ui-remediation` عند `a0a27b78e705ba0d43521b9fa0366d64b4a1f554`.
**فرع التدقيق:** `codex/13-mvp-completion-audit`.
هذه مراجعة أدلة فقط؛ لا تعتمد قرار منتج جديد ولا تنفذ ميزة.

## الملخص التنفيذي

المنصة تملك نواة تشغيلية قوية تغطي العزل متعدد الأكاديميات، الحسابات والجلسات server-side، الهيكل واللاعب متعدد الرياضات، التجديد والدفع التجريبي الآمن، الحضور ورصيد الحصص، التقييمات، تجربة ولي الأمر، المحتوى، والتقارير والتعديلات والخصومات. قاعدة المراجعة تشمل صراحة معالجة **Premium Dashboard UI**: خط Cairo، shell موحد لـOwner/Admin/Coach، sidebar/drawer متجاوب، جداول تتحول إلى بطاقات، حالات موحدة، لوحة مالك محسنة، وإجراءات سريعة للإداري. لم تغيّر هذه المعالجة العقود أو الصلاحيات أو قواعد المال.

التحقق المحلي على schema PostgreSQL معزولة نجح: migrations كاملة حتى `20260929015730_Slice8CSubscriptionDiscounts`، لا pending model changes، build بلا تحذيرات، Backend `295/295`، Frontend `92/92`، وMobile Chromium E2E `35/35`. بقي تحذير React uncontrolled→controlled متكررًا في E2E ولا يفشل الاختبارات.

الحكم الصارم هو **NOT DEMO READY** حتى إغلاق عائق رحلة واحد وعيب عرض شامل: لا يستطيع Owner/Admin إنشاء تجديد للاعب قائم (`AC-SUB-004`)، كما أن Guardian checkout وroot PWA القديم وبعض القيم الديناميكية ما زالت إنجليزية/خامًا، ولذلك لا ينجح `AT-002` كاملًا. معالجة Premium أزالت overflow لوحة المالك وأكملت quick actions التشغيلية، لكنها لم تنفذ التجديد الإداري أو تعيد تصميم Guardian checkout.

تغطية المتطلبات: **78 IMPLEMENTED، 21 PARTIALLY IMPLEMENTED، 3 DEFERRED BY CURRENT DELIVERY PLAN، 12 BLOCKED BY OPEN DECISION، 2 NOT IMPLEMENTED، 1 OUT OF CURRENT VERSION**؛ المجموع `117` بلا نقص أو تكرار. الإنتاج **NOT PRODUCTION READY** لغياب OTP/SMS ومزود دفع ووسائط وعمليات نشر/استعادة/مراقبة إنتاجية فعلية.

## مفاتيح الأدلة

- `API`: `apps/api/src/Academy.Api`، و`DB`: `apps/api/src/Academy.Infrastructure`.
- `UI`: `apps/web/src`، و`FT`: اختبارات Vitest، و`IT`: `tests/integration`، و`E2E`: `tests/e2e`.
- `Premium UI`: `dashboard-shell.tsx` و`dashboard.css` و`DASHBOARD_UI_SYSTEM.md` واختبارات dashboard/E2E الحالية.
- `IMPLEMENTED` يتطلب منطقًا قابلًا للتشغيل وواجهة حين يطلبها المطلب ودليلًا مناسبًا. `PARTIALLY IMPLEMENTED` يعني أن النواة موجودة لكن جزءًا صريحًا غائب. القرار المفتوح لا يُعامل كموافقة لمجرد وجود default تقني قابل للعكس.

## مصفوفة المتطلبات — 117/117

| ID | العنوان المختصر | الحالة | دليل Backend / Frontend / Tests | الجزء غير المكتمل أو القرار |
|---|---|---|---|---|
| AC-GOV-001 | منتج مستقل | IMPLEMENTED | Repo مستقل؛ dependency architecture test | — |
| AC-GOV-002 | عدة أكاديميات | PARTIALLY IMPLEMENTED | DB tenant keys/membership؛ UI academy name؛ IT Academy B/IDOR | branding/contact settings غير مكتملة |
| AC-GOV-003 | البساطة والموعد | IMPLEMENTED | modular monolith وسجل slices؛ لا بنية موزعة | — |
| AC-GOV-004 | بيانات مشتركة للأدوار | IMPLEMENTED | API/DB مشتركان؛ E2E انتقال الحضور/التقييم/الدفع | — |
| AC-GOV-005 | العربية وهوية العرض | PARTIALLY IMPLEMENTED | `ar/rtl` وCairo/Premium UI؛ FT/E2E | checkout/root وبعض enums والعملات خام؛ branding غير مكتمل |
| AC-GOV-006 | المصدر وسجل التغيير | IMPLEMENTED | baseline وDecisions/Status وmigrations append-only | — |
| AC-ORG-001 | إعداد الأكاديمية | PARTIALLY IMPLEMENTED | Academy name/zone/currency وseed | لا شاشة شعار/تواصل/لون |
| AC-ORG-002 | الفروع والرياضات والفئات | IMPLEMENTED | Structure API/UI CRUD؛ S2 IT وdashboard E2E | — |
| AC-ORG-003 | المجموعات | PARTIALLY IMPLEMENTED | Group CRUD/filter/assignment؛ IT/FT | إدارة recurring schedule من سجل المجموعة غير مكتملة |
| AC-ORG-004 | المدربون والجهاز الفني | PARTIALLY IMPLEMENTED | staff API/assignment/attendance؛ IT | لا رحلة UI لإنشاء حساب موظف جديد |
| AC-ORG-005 | الجدول والحصة الفعلية | IMPLEMENTED | generator/session endpoints؛ idempotency/cancel IT | — |
| AC-IAM-001 | أنواع المستخدمين | IMPLEMENTED | roles/policies/routes؛ E2E لكل دور | — |
| AC-IAM-002 | حساب فردي | IMPLEMENTED | Identity + AcademyMembership؛ لا shared password | — |
| AC-IAM-003 | تجربة الإداري | PARTIALLY IMPLEMENTED | Premium Admin home به registration/requests/attendance/renewal-list quick actions؛ E2E | لا إنشاء تجديد إداري فعلي |
| AC-IAM-004 | تجربة المدرب | IMPLEMENTED | assigned attendance/evaluation routes؛ scope IT/E2E | — |
| AC-IAM-005 | تجربة المالك | PARTIALLY IMPLEMENTED | Premium owner dashboard/reports/finance؛ IT/E2E | إعدادات الأكاديمية الكاملة غائبة |
| AC-IAM-006 | Guardian activation/link | PARTIALLY IMPLEMENTED | Demo OTP وexplicit links؛ auth/IDOR IT | OTP/recovery production غير منفذ |
| AC-IAM-007 | صلاحيات الخادم | IMPLEMENTED | TenantPermissionHandler/resource checks؛ isolation IT | — |
| AC-IAM-008 | حساب لاعب اختياري | DEFERRED BY CURRENT DELIVERY PLAN | لا login/route لاعب؛ محفوظ بالخطة | يحتاج قرار إدراج |
| AC-PLY-001 | ملف اللاعب | PARTIALLY IMPLEMENTED | Player API/UI + measurements/foot/position | لا يجمع كل الحقول المرجعية في عرض واحد |
| AC-PLY-002 | ولي أمر لعدة أبناء | IMPLEMENTED | GuardianPlayerLink؛ IT وGuardian E2E | — |
| AC-PLY-003 | طفل بعدة رياضات | IMPLEMENTED | Player/SportEnrollment؛ multi-sport IT/E2E | — |
| AC-PLY-004 | عدم تكرار اللاعب | IMPLEMENTED | existing-player enrollment/renewal؛ duplicate IT | — |
| AC-PLY-005 | البحث والفلاتر | PARTIALLY IMPLEMENTED | scoped search + filters؛ IT/Premium UI | ليست المجموعة الكاملة موحدة في كل الشاشات |
| AC-PLY-006 | تسجيل لاعب | PARTIALLY IMPLEMENTED | atomic player/guardian/link/enrollment؛ mobile E2E | لا plan/start/collection في رحلة الإداري |
| AC-PLY-007 | الأرشفة والتاريخ | PARTIALLY IMPLEMENTED | IsActive/status/restrict FKs؛ IT | نقل المجموعة وسياسة archive/audit الكاملة غائبة |
| AC-SUB-001 | تعريف الباقات | IMPLEMENTED | plan API/UI CRUD؛ validation tests | — |
| AC-SUB-002 | الأنواع الثلاثة | IMPLEMENTED | Duration/Sessions/Combined؛ payment/attendance IT | — |
| AC-SUB-003 | الفترة والحالة والرصيد | IMPLEMENTED | period API/UI/status/balance؛ seed/IT | — |
| AC-SUB-004 | التجديد من الإدارة | NOT IMPLEMENTED | admin read/discount فقط؛ لا create API/UI/test | رحلة مطلوبة غائبة |
| AC-SUB-005 | تجديد ولي الأمر | IMPLEMENTED | Guardian renewal/checkout؛ IT/E2E | — |
| AC-SUB-006 | تجديد للغير | IMPLEMENTED | opaque hashed reference؛ ExternalRenewal IT/E2E | — |
| AC-SUB-007 | التاريخ والتجديد المبكر | IMPLEMENTED | append-only periods؛ early/expired IT | — |
| AC-SUB-008 | التجميد | IMPLEMENTED | adjustment API/UI/audit؛ 8B IT/E2E | default قابل للعكس تحت OD-001 |
| AC-SUB-009 | إضافة/خصم أيام | IMPLEMENTED | reason/version/idempotency؛ IT/E2E | — |
| AC-SUB-010 | إلغاء الاشتراك | IMPLEMENTED | cancel مع حفظ المال والتاريخ؛ IT/E2E | refund غير منفذ عمدًا |
| AC-SUB-011 | الخصومات | IMPLEMENTED | server snapshots/audit/payment replacement؛ 8C IT/E2E | السياسة التجارية الأوسع OD-001 |
| AC-SUB-012 | القريب والمنتهي | IMPLEMENTED | queries/UI/dashboard/seed؛ IT | — |
| AC-ATT-001 | حضور اللاعب والموظف | IMPLEMENTED | rosters منفصلة؛ IT/E2E | — |
| AC-ATT-002 | سجل الحصة ومنع التكرار | IMPLEMENTED | unique keys/correction/audit؛ concurrency IT | — |
| AC-ATT-003 | التسجيل بالكود | IMPLEMENTED | roster search بالاسم/PlayerCode؛ E2E | — |
| AC-ATT-004 | التاريخ والتقويم | PARTIALLY IMPLEMENTED | history/summary/report UI/API | لا calendar visual أو نسبة مقامها الجلسات المنعقدة |
| AC-ATT-005 | أثر الحضور على الرصيد | IMPLEMENTED | movement transaction/restore؛ IT/E2E | — |
| AC-ATT-006 | تصدير شهري | PARTIALLY IMPLEMENTED | CSV حقيقي بفلاتر وحماية injection؛ IT/E2E | baseline يطلب XLSX |
| AC-ATT-007 | التسجيل السريع | IMPLEMENTED | bulk present + edit/save UI؛ E2E | — |
| AC-EVA-001 | معايير التقييم | IMPLEMENTED | criteria API/UI/sport/axis/weight؛ IT/FT | — |
| AC-EVA-002 | مسودة ونشر | IMPLEMENTED | immutable publish؛ coach/guardian E2E | — |
| AC-EVA-003 | صفر لا يساوي missing | IMPLEMENTED | nullable 0–100؛ boundary IT | — |
| AC-EVA-004 | رأس التقرير | IMPLEMENTED | name/photo/position/overall UI؛ FT/E2E | — |
| AC-EVA-005 | المحاور الستة | IMPLEMENTED | calculator/radar/text؛ IT/E2E | — |
| AC-EVA-006 | العمر والقياسات والقدم | IMPLEMENTED | Player fields/cards؛ E2E | — |
| AC-EVA-007 | التفاصيل دون ملعب | IMPLEMENTED | score snapshots/detail cards؛ E2E | — |
| AC-EVA-008 | الإجمالي | BLOCKED BY OPEN DECISION | calculator default واختبارات دقيقة | formula/weights تنتظر OD-007 |
| AC-EVA-009 | الفترات والاتجاه | BLOCKED BY OPEN DECISION | reportingPeriod محفوظ | trends/comparison/colors تنتظر OD-007 |
| AC-EVA-010 | غير كرة القدم | IMPLEMENTED | swimming report بلا radar؛ IT | — |
| AC-PAR-001 | مرجع بصري عربي | PARTIALLY IMPLEMENTED | Guardian RTL/mobile cards/actions؛ E2E | checkout إنجليزي وbranding غير مكتمل |
| AC-PAR-002 | بطاقات الأبناء | IMPLEMENTED | deduped cards؛ FT/IT/E2E | — |
| AC-PAR-003 | أقسام الرياضات | BLOCKED BY OPEN DECISION | active sport union/dedupe؛ IT/E2E | eligibility النهائية OD-008 |
| AC-PAR-004 | الإجراءات الثلاثة | IMPLEMENTED | new/renew/renew-other links؛ FT/E2E | — |
| AC-PAR-005 | اشتراك جديد | BLOCKED BY OPEN DECISION | request/review/atomic approval؛ IT/E2E | اختيار المجموعة OD-006 |
| AC-PAR-006 | سياق الطفل والرياضة | PARTIALLY IMPLEMENTED | child/sport sections per enrollment | لا selector صريح ولا كل السياق في الرأس |
| AC-PAR-007 | قائمة ملف الطفل | IMPLEMENTED | report/schedule/nutrition/medical/gallery/renew؛ E2E | — |
| AC-PAR-008 | التدريب والمباريات | BLOCKED BY OPEN DECISION | recurring/upcoming training UI/API | Match/Event ينتظر OD-011 |
| AC-PAR-009 | التجديد من الملف | IMPLEMENTED | contextual renewal route؛ E2E | — |
| AC-PAR-010 | لا دورات/AI | OUT OF CURRENT VERSION | لا routes/models/buttons؛ FT | مستبعد صراحة |
| AC-SPP-001 | كتالوج الرياضة | IMPLEMENTED | admin CRUD + Guardian read؛ IT/E2E | — |
| AC-SPP-002 | بطاقة المنتج | BLOCKED BY OPEN DECISION | image/name/sport/display price metadata | favourites/ratings OD-008 |
| AC-SPP-003 | الربط والظهور | IMPLEMENTED | tenant/sport/active/dedupe filters؛ IT | — |
| AC-SPP-004 | حدود البيع | BLOCKED BY OPEN DECISION | catalog-only، لا commerce endpoints | seller/purchase/package OD-008 |
| AC-SPP-005 | seed المنتجات | IMPLEMENTED | football/swimming synthetic seed/assets؛ IT | — |
| AC-NUT-001 | مكتبة معلوماتية | IMPLEMENTED | model/read UI بلا تجارة؛ negative IT | — |
| AC-NUT-002 | ثلاثة أقسام | IMPLEMENTED | tabs؛ E2E | — |
| AC-NUT-003 | بطاقة الوجبة | IMPLEMENTED | Arabic info card بلا تجارة؛ E2E | — |
| AC-NUT-004 | التفاصيل والقيم | IMPLEMENTED | serving/facts/detail route؛ E2E | — |
| AC-NUT-005 | بيانات أول تشغيل | IMPLEMENTED | 15 items/18 placements؛ exact-count IT | — |
| AC-NUT-006 | 18/15 | IMPLEMENTED | seed + exact assertions | — |
| AC-NUT-007 | منع التجارة | IMPLEMENTED | لا money/order/cart fields/endpoints؛ IT | — |
| AC-NUT-008 | المصدر والمراجعة | IMPLEMENTED | serving/source/status/UI label؛ IT | الاعتماد العلمي النهائي OD-009 |
| AC-NUT-009 | لا وصفة فردية | IMPLEMENTED | general projection؛ IT | — |
| AC-NUT-010 | التحرير والترتيب | IMPLEMENTED | admin CRUD/categories/order/active؛ FT/IT | default تحت OD-009 |
| AC-MED-001 | إصابة/استشارة | IMPLEMENTED | admin/guardian routes + publish؛ IT/E2E | — |
| AC-MED-002 | سجل مبسط | BLOCKED BY OPEN DECISION | fields/notes/status/publish موجودة | attachments والمسؤوليات OD-010 |
| AC-MED-003 | العرض المقيد | IMPLEMENTED | published-only وStaffNotes hidden؛ IT/E2E | — |
| AC-GAL-001 | معرض الطفل | IMPLEMENTED | Guardian gallery/assets منشورة؛ E2E | — |
| AC-GAL-002 | إدارة الوسائط | BLOCKED BY OPEN DECISION | metadata CRUD/publish/order؛ IT | upload/group/coach role OD-010 |
| AC-GAL-003 | خصوصية الصور | IMPLEMENTED | allowlist + child/tenant/publish checks؛ IT | production object storage بوابة إنتاج |
| AC-FIN-001 | تحصيل الاشتراكات فقط | IMPLEMENTED | confirmed Collection totals؛ IT | — |
| AC-FIN-002 | سجل العملية | IMPLEMENTED | immutable snapshots/provider refs؛ IT | — |
| AC-FIN-003 | فصل الطلب والدفع | IMPLEMENTED | Renewal/Payment منفصلان؛ pending/fail بلا revenue | provider حقيقي خارج الديمو |
| AC-FIN-004 | إيصال وطباعة | IMPLEMENTED | Arabic HTML receipt + browser print؛ E2E | PDF/tax semantics OD-012 |
| AC-FIN-005 | منع تكرار المال | IMPLEMENTED | constraints/transactions/idempotency/replay IT | — |
| AC-RPT-001 | لوحة المالك | IMPLEMENTED | stored summary + Premium responsive UI؛ IT/E2E | — |
| AC-RPT-002 | التقرير المالي | IMPLEMENTED | filters/totals/CSV/receipt links؛ Reporting IT/E2E | — |
| AC-RPT-003 | تقارير التشغيل | IMPLEMENTED | attendance/current/expiring/expired/player lists | — |
| AC-RPT-004 | تعريف المؤشرات | IMPLEMENTED | distinct players/confirmed sums/actual sessions؛ IT | — |
| AC-COM-001 | بوت ولي الأمر | DEFERRED BY CURRENT DELIVERY PLAN | موثق بلا API/UI | OD-001/012 والتوقيت |
| AC-COM-002 | ربط البوت | BLOCKED BY OPEN DECISION | حدود أمن موثقة فقط | provider/link/audience OD-012 |
| AC-COM-003 | الرسائل | DEFERRED BY CURRENT DELIVERY PLAN | لا message model/API/UI | توقيت/قناة/جمهور غير معتمد |
| AC-COM-004 | ranking | BLOCKED BY OPEN DECISION | لا projection مزيف | formula/privacy OD-007/012 |
| AC-COM-005 | users/connections/support | BLOCKED BY OPEN DECISION | staff API لا يساوي هذه القدرة | المرجع غير كافٍ؛ OD-012 |
| AC-UX-001 | موبايل أولًا | IMPLEMENTED | Guardian mobile + Premium staff drawer/cards؛ E2E Pixel 7 ومراجعة 390–1440 | — |
| AC-UX-002 | loading/error/empty/persistence | PARTIALLY IMPLEMENTED | shared states وreload tests؛ FT/E2E | fetch errors غير موحدة وتحذير controlled input |
| AC-UX-003 | العرض العربي | PARTIALLY IMPLEMENTED | غالب النصوص والتواريخ/الأموال مترجمة | checkout/raw planType/status/currency وبعض ISO dates |
| AC-DEMO-001 | بيانات لكل ميزة | IMPLEMENTED | deterministic Slice1–8C seed + E2E | — |
| AC-DEMO-002 | أسماء مصرية خيالية | IMPLEMENTED | synthetic accounts/players/assets | — |
| AC-DEMO-003 | سيناريوهات مترابطة | IMPLEMENTED | siblings/multi-sport/status/attendance/evaluation/discount seeds | — |
| AC-DEMO-004 | ساعة مرجعية | IMPLEMENTED | Demo ReferenceDate/clock؛ IT | — |
| AC-DEMO-005 | تغذية جاهزة | IMPLEMENTED | DB seed 15/18 + status | — |
| AC-DEMO-006 | reset آمن | PARTIALLY IMPLEMENTED | seed rerun/guards IT | لا reset command صريح scoped |
| AC-DEMO-007 | حسابات وعزل | IMPLEMENTED | 4 roles + Academy B؛ negative IT | — |
| AC-DEMO-008 | قصة end-to-end | PARTIALLY IMPLEMENTED | أجزاء القصة عبر 35 E2E | لا رحلة واحدة كاملة؛ admin renewal غائب |
| AC-DEL-001 | توثيق ثم تنفيذ | IMPLEMENTED | baseline/architecture/plan قبل slices | — |
| AC-DEL-002 | اتجاه تقني مستقل | IMPLEMENTED | .NET/Next/PostgreSQL modular monolith | — |
| AC-DEL-003 | PWA أولًا | PARTIALLY IMPLEMENTED | responsive app + manifest/standalone metadata | لا service worker/offline policy |
| AC-DEL-004 | العمل بالمستودع | IMPLEMENTED | branches/CI/tests/docs | — |
| AC-DEL-005 | بوابة الإنتاج | NOT IMPLEMENTED | لا deploy/restore/backup/monitoring/HTTPS proof | Slice 9 مطلوب قبل عميل حقيقي |
| AC-DEL-006 | لا توسع غير معتمد | IMPLEMENTED | لا nutrition commerce/native/AI/broker | — |

### فحص المصفوفة

- baseline: `117 occurrence / 117 unique`.
- مصفوفة التدقيق: `117 occurrence / 117 unique`؛ missing `0`، duplicate `0`، extra `0`.
- الحالة الأصلية لكل مطلب بقيت في `REQUIREMENTS_MAPPING_v1.0.md` منفصلة عن حالة التنفيذ أعلاه؛ لم يُعدّل baseline ولم يتحول OD معلق إلى موافقة.

## تدقيق معايير القبول — AT-001..AT-034

| AT | الحالة | الاختبار أو الدليل الفعلي |
|---|---|---|
| AT-001 | COVERED INDIRECTLY | dependency architecture test وجرد repository/remotes/namespaces |
| AT-002 | NOT IMPLEMENTED | auth/E2E يثبت `ar/rtl` وPremium UI يحسن staff؛ checkout/root وبعض القيم الإنجليزية تمنع النجاح الكامل |
| AT-003 | PASS | TenantAuthentication وID tampering وexport/media/content isolation |
| AT-004 | PASS | `Player_persists_after_reload` وstructure-people E2E |
| AT-005 | PASS | linked siblings IT وGuardian E2E |
| AT-006 | PASS | multi-sport enrollment IT/profile E2E |
| AT-007 | PASS | catalog sport dedupe IT وSlice7 Journey A |
| AT-008 | PASS | Guardian actions FT وE2E للمسارات الثلاثة |
| AT-009 | PASS | child profile integration FT/E2E؛ لا AI/intensive routes |
| AT-010 | PASS | reuse existing player/repeated approval/renewal IT |
| AT-011 | PASS | early/expired renewal period tests |
| AT-012 | PASS | failed/pending payment بلا Collection/revenue |
| AT-013 | PASS | duplicate callback/idempotency للـCollection/Receipt/Period |
| AT-014 | PASS | ExternalRenewal suite + عدم منح profile access |
| AT-015 | PASS | filter mismatch/cross-tenant/coach unassigned tests |
| AT-016 | PASS | unique attendance، الحالات الثلاث، staff separation وE2E |
| AT-017 | PASS | consume/restore/concurrency/frozen-combined tests |
| AT-018 | NOT IMPLEMENTED | CSV صحيح ومختبر، لكن المعيار يطلب XLSX فعليًا |
| AT-019 | PASS | score boundary وnullable missing tests |
| AT-020 | PASS | draft hidden/published visible/assigned scope + E2E |
| AT-021 | PASS | report name/position/radar/cards/details/no pitch E2E |
| AT-022 | DEFERRED | calculator default مختبر؛ القاعدة المعتمدة تنتظر OD-007 |
| AT-023 | PASS | swimming report بلا football radar IT |
| AT-024 | PASS | 3 categories/18 placements/15 unique + E2E |
| AT-025 | PASS | no-commerce model/route tests وforbidden labels E2E |
| AT-026 | PASS | serving/facts/source status IT/E2E |
| AT-027 | PASS | linked-child/tenant/publish/notes media/medical tests |
| AT-028 | PASS | dashboard/report/receipts consistency وno content revenue |
| AT-029 | PASS | adjustments/discounts/receipt journeys واختبارات التاريخ |
| AT-030 | COVERED INDIRECTLY | seed rerun + Demo-only guards؛ لا reset command مستقل |
| AT-031 | COVERED INDIRECTLY | fixed clock وexpiry/early/list tests؛ ليس اختبارًا جامعًا واحدًا |
| AT-032 | COVERED INDIRECTLY | persistence/reload موجود؛ network false-success غير مغطى لكل mutations |
| AT-033 | PASS | honest medical/report/request/receipt empty-state tests |
| AT-034 | DEFERRED | Demo/Test guards تعمل؛ Production Gate لم تنفذ |

**الإجمالي:** PASS `26`، COVERED INDIRECTLY `4`، NOT TESTED `0`، NOT IMPLEMENTED `2`، DEFERRED `2`؛ المجموع `34`.

## تدقيق سيناريوهات الديمو — DM-01..DM-12

الحسابات التجريبية ومسارات الدخول موثقة في `docs/demo/SLICE1_DEMO_ACCOUNTS.md`؛ لا تُكرر كلمات المرور هنا.

| DM | الحالة | الحساب/المسار/البيانات وما يعرض | العائق |
|---|---|---|---|
| DM-01 | READY | Guardian home → طفل كرة القدم → renewal → Demo checkout/receipt؛ Owner dashboard/report يرى السجل | مسار Admin المباشر غائب لكنه ليس شرط هذا السيناريو المحدد |
| DM-02 | READY | Guardian home يعرض شقيقين ورياضاتهما منفصلة | — |
| DM-03 | READY | طفل واحد به Football وSwimming enrollments مستقلة | — |
| DM-04 | READY | رياضة مشتركة بين طفلين تظهر مرة في catalog | — |
| DM-05 | READY | renew-for-another بكود demo opaque؛ لا profile access للمستفيد | — |
| DM-06 | READY | Coach evaluation draft/publish → Guardian report؛ 0 و100 وmissing مختبرة | OD-007 لا يمنع عرض default المعلن |
| DM-07 | READY | Admin/Coach sessions/player/staff/history؛ الحالات الثلاث وتصحيح بلا duplicate | — |
| DM-08 | READY | Guardian nutrition tabs/detail؛ 15/18 وموسوم demo-unreviewed | — |
| DM-09 | READY | Admin period adjustment/discount/receipt print | المنفذ كله حقيقي داخل Demo؛ لا زر تجارة وهمي |
| DM-10 | READY | Academy B وIDs غير مرتبطة لا تتسرب عبر role/resource tests | — |
| DM-11 | READY | Guardian medical/gallery published synthetic فقط؛ empty state لطفل آخر | production upload مؤجل |
| DM-12 | PARTIAL | ReferenceDate يضبط expiry/metrics وseed rerun لا يضاعف | لا reset command صريح لقاعدة مستخدمة |

**الإجمالي:** READY `11`، PARTIAL `1`، NOT READY `0`.

## رحلات الأدوار وPremium Dashboard

### Owner

لوحة المالك الجديدة تستخدم سجلات مخزنة فقط وتعرض التحصيل اليومي/الشهري، الاشتراكات، اللاعبين، الحضور، التقسيم حسب الرياضة/الفرع وأحدث الإيصالات. sidebar والدراور والجداول المتحولة لبطاقات أصلحت overflow السابق عند الهاتف، وروابط التقارير والإيصالات حقيقية. تبقى settings/branding رحلة ناقصة، والتجديد الإداري غير موجود.

### Admin

الرئيسية Premium تقدم quick actions للتسجيل، الطلبات، جلسات وسجل الحضور، قائمة التجديدات والبحث. التسجيل والهيكل والحضور والتقييم والتقارير والمحتوى تعمل. رابط «طلبات التجديد» يفتح الطلبات القائمة فقط؛ لا توجد API/UI لإنشاء تجديد للاعب قائم، ولذلك لا تعتبر المعالجة البصرية تنفيذًا لـ`AC-SUB-004`.

### Coach

يرى navigation محدودًا للحضور والتقييم وتقارير الحضور، ثم بيانات المجموعات المسندة فقط. API تمنع المجموعة/اللاعب غير المسند والماليات والإدارة. واجهة Premium متجاوبة ولا يوجد dead navigation مثبت.

### Guardian

OTP التجريبي، home، الأطفال والرياضات deduplicated، الإجراءات الثلاثة، profile، التقرير المنشور، الجدول، الحضور، الاشتراكات، renew-for-another، الإيصالات، catalog، nutrition، medical وgallery قابلة للعرض. Guardian لم يكن ضمن نطاق Premium staff redesign؛ checkout ما زال بعنوان `Test Payment Gateway` ويعرض `EGP` وstatus/discount type خامًا.

## Dead UI والمسارات

- لا أزرار `TODO/Coming soon` ولا Upload/Apple Pay/notification/real payment وهمي ظاهر. CSV ينزل ملفًا فعليًا، Print يستدعي browser print، ومحاكاة الدفع تغيّر DB التجريبية.
- **BLOCKER وظيفي:** غياب admin renewal؛ لا يوجد زر ميت لأن القدرة نفسها غير معروضة.
- **SHOULD FIX BEFORE DEMO:** `/` صفحة Foundation قديمة تقول `Slice 1` وغياب بيانات لاعبين/اشتراكات، بينما `manifest.start_url` يقود إليها.
- **SHOULD FIX BEFORE DEMO:** Guardian checkout إنجليزي جزئيًا، و`SubscriptionPeriodDetail` يعرض `planType` الخام، وبعض statuses/currency/date formatting غير موحد.
- production build ولّد `42/42` static items، وجرد source يضم `76` صفحة: `/login`، `/dashboard` وكل structure/people/subscription/attendance/evaluation/content/report routes، وGuardian home/child/content/enrollment/renewal/checkout/receipt routes. لا route لـAI أو intensive courses أو nutrition commerce أو communications.
- role visibility في shell صحيحة، مع بقاء API authorization مصدر الحماية الحقيقي.

## الهاتف والعربية وRTL

المعالجة الجديدة اختبرت بصريًا staff screens عند `1440/1280/1024/768/390` على نفس SHA، وE2E الحالي يعمل Pixel 7 (`412×839`). عند أقل من 1152px يتحول sidebar إلى drawer، وعند أقل من 768px تتحول AdminDataTable إلى بطاقات. Owner/Admin/Coach/Guardian لا يملكون overflow صفحة مثبتًا على القاعدة الجديدة.

المتبقي ليس انهيار layout بل اتساق المنتج: checkout وعناصر ديناميكية إنجليزية، تواريخ ISO في مواضع، root قديم، ومعالجة errors غير موحدة. تحذير React controlled/uncontrolled ظهر أثناء E2E في guardian/evaluation flows؛ P1 لأنه لا يثبت فقد بيانات لكنه يقلل نظافة التشغيل.

## سلامة البيانات والماليات

- `Player` منفصل عن `SportEnrollment` وGuardian access لا ينشأ من الهاتف أو الدفع. composite tenant keys وresource predicates تمنع العلاقات والقراءات العابرة.
- enrollment approval transaction واحدة وreplay لا يكرر enrollment. attendance فريد لكل session/person وحركة الرصيد append-only وقابلة للتصحيح مرة واحدة.
- periods/collections/receipts/adjustments/discounts/published evaluations تحفظ snapshots/history؛ medical/media تحجب draft و`StaffNotes`.
- التسلسل المالي المثبت: `PaymentRequest.Amount = RenewalRequest.FinalAmount = Collection.Amount = Receipt.FinalAmount = financial report contribution = owner dashboard contribution`.
- callback replay، المبلغ/العملة الخاطئان، النجاح المتأخر لمحاولة فاشلة/ملغاة، والـdouble click لا ينشئون تحصيلًا أو فترة إضافية. freeze/day/cancel لا يعيد كتابة المال التاريخي.

## جاهزية الدفع الحقيقي

| العنصر | التصنيف | الوضع |
|---|---|---|
| Internal Test Gateway | Demo-ready فقط | يعمل ومحصور في Demo/Testing؛ ليس مزودًا ماليًا |
| بطاقات حقيقية | implementation required | لا adapter/checkout/webhook production أو PCI flow |
| Geidea | implementation + external onboarding | اتجاه مفضل موثق فقط؛ لا merchant/config/SDK |
| Apple Pay | implementation + external onboarding | لا entitlement/domain/certificate/provider support |
| webhook verification | architecture-ready جزئيًا | replay/idempotency/amount checks موجودة؛ لا signature parser لمزود حقيقي |
| secrets/reconciliation/refunds | implementation/policy required | لا secret store/settlement/dispute/refund flow |

## القرارات OD-001..OD-012

| OD | الحالة | أثره الحالي |
|---|---|---|
| OD-001 | PENDING؛ default قابل للعكس | adjustments/discount اليدوي يعملان؛ cut التجاري والقدرات المحفوظة غير محسومين |
| OD-002 | APPROVED | PWA-first منفذ؛ لا blocker قرار |
| OD-003 | APPROVED | الحساب والربط وDemo OTP منفذة؛ production provider بوابة تنفيذ |
| OD-004 | APPROVED | الأنواع والتواريخ/early renewal منفذة |
| OD-005 | APPROVED | online-first/internal demo/renew-other منفذة؛ provider الحقيقي مستقل |
| OD-006 | PENDING؛ default منفذ | admin-review enrollment يعمل؛ مسؤولية المجموعة غير معتمدة نهائيًا |
| OD-007 | PENDING؛ default منفذ | يحجب اعتماد formula/trends/ranking |
| OD-008 | PENDING؛ default منفذ | catalog-only يعمل؛ يحجب commerce/favourites/ratings |
| OD-009 | PENDING؛ default منفذ | seed/status/editor يعمل؛ يمنع تقديم القيم كحقائق إنتاجية موثوقة |
| OD-010 | PENDING؛ default منفذ | metadata/publish يعمل؛ يحجب upload/storage ومسؤوليات موسعة |
| OD-011 | PENDING | يحجب Match/Event؛ التدريب وحده في الديمو |
| OD-012 | PENDING | يحجب bot/messages/ranking/reference entries وPDF semantics |

لا يوجد قرار جديد في هذا التدقيق. العائق المباشر للديمو هو نقص تنفيذ/اتساق، لا ضرورة حسم كل OD. اكتمال baseline كله يتأثر بالـOD المعلقة، والإنتاج يحتاج تنفيذًا وتشغيلًا إضافيين حتى للقرارات المعتمدة.

## القدرات المرجعية المتبقية

| القدرة | المواصفة/التنفيذ | للديمو الحالي | المطلوب قبل التنفيذ |
|---|---|---|---|
| رسائل فردية/جماعية ومرفقات | مواصفة جزئية؛ غير منفذة | لا | OD-001/012 وقناة/جمهور/تخزين |
| Guardian bot | provider/linking غير محددين؛ غير منفذ | لا | OD-012 وربط آمن |
| Ranking | formula/privacy غير محددين؛ غير منفذ | لا | OD-007/012 |
| Contacts/Connections/Support | أسماء مرجعية فقط؛ غير منفذة | لا | product discovery؛ staff API ليست بديلًا |
| Refunds/chargebacks | غير محددة وغير منفذة | لا | قرار سياسة + provider |
| Real payment/Apple Pay | غير منفذ | لا | onboarding وتنفيذ وتشغيل |
| Production media | demo metadata/assets فقط | لا | OD-010 + private storage/scan |
| Real OTP/SMS | غير منفذ | لا؛ fixed OTP واضح | provider/rate limit/recovery |
| Production deployment | topology موثق فقط | لا | Production Gate كاملة |

## الأمن وبيئة الديمو

اختبارات tenant IDOR وGuardian-child وCoach-group وCSRF وcallback replay/forgery والمبلغ والخصم والتعديلات وتسرب `StaffNotes`/media وCSV injection وDemo gateway/OTP guards نجحت. لا finding عالية أو متوسطة مفتوحة داخل demo scope. هذا لا يمثل اعتماد أمن إنتاج؛ rate limiting/provider/secrets/HTTPS/monitoring/backup/private storage لم تثبت إنتاجيًا.

seed حتمي idempotent، والأسماء/الأصول/السجلات الطبية صناعية، وأكاديمية ثانية موجودة للاختبارات السلبية. `Demo__ReferenceDate=2026-09-28` يقود منطق الاشتراكات. migrations طبقت على schema تدقيق إضافية داخل PostgreSQL 16 المحلي؛ هذا تحقق توافق مؤقت لا يغيّر target PostgreSQL 17. لم تُمسح قاعدة أو volume ولم تُعدّل قاعدة الديمو القائمة.

## جرد الاختبارات والتحقق

| الفئة | النتيجة | التغطية |
|---|---:|---|
| Integration | `293/293` | tenant/auth، structure، payments، attendance، evaluation، guardian/content، reports، adjustments، discounts |
| Unit | `1/1` | live health |
| Architecture | `1/1` | dependency direction |
| Backend | `295/295` | Release، PostgreSQL schema معزولة |
| Frontend | `92/92` | 12 files؛ shell وfeatures والGuardian |
| E2E Mobile Chromium | `35/35` | 11 specs؛ Pixel 7؛ 1.3 دقيقة |
| Build/quality | PASS | restore locked، Release `0 warnings/0 errors`، typecheck، lint، Next build `42/42`، npm audit `0` |
| EF | PASS | كل migrations؛ `No changes have been made to the model since the last migration` |

محاولة الاختبار الأولى ورّثت Demo seed من `.env` إلى health tests التي تتعمد الاتصال بمنفذ مغلق، ففشلت قبل إعادة التشغيل. أعيد التشغيل بنفس نمط CI بعد إزالة متغيرات Demo الموروثة فنجح `295/295`؛ لا defect في التطبيق نتج عن المحاولة الأولى.

قاعدة الـUI `a0a27b78e705ba0d43521b9fa0366d64b4a1f554` لها GitHub Actions run `36533333162` ناجحًا للوظائف `backend`, `frontend`, `e2e` على PostgreSQL 17 حيث ينطبق. نتيجة CI لcommit وثيقة التدقيق لا تُعلن قبل push واكتمال run مطابق للـSHA.

## الأحكام

### Demo readiness

**NOT DEMO READY** وفق تعريف Prompt 13 الصارم. المنتج قريب جدًا وقابل للمراجعة الداخلية، وPremium UI أصلحت الجودة والاستجابة للموظفين، لكن رحلة Admin renewal المطلوبة غير موجودة، وAT-002 لا ينجح بسبب checkout/root والقيم الخام. لا توجد مشكلة مثبتة في العزل أو سلامة المال.

### Production readiness

**NOT PRODUCTION READY.** يلزم OTP/SMS/recovery موثوق، مزود دفع حقيقي وsignatures/reconciliation/refund policy، secrets وHTTPS، private media، backups وrestore drill، monitoring/alerting، support ownership، security gate واختبار topology الإنتاجية.

## الفجوات المرتبة

### P0 — قبل عرض أصحاب المصلحة (`2`)

| المتطلبات | الأثر | الإجراء المقترح | التعقيد |
|---|---|---|---|
| `AC-SUB-004`, `AC-IAM-003` | لا يستطيع Owner/Admin إنشاء تجديد للاعب قائم؛ قائمة الطلبات لا تعوض الإنشاء | Admin renewal creation على enrollment قائم بنفس idempotency/payment invariants + tests | Medium |
| `AC-GOV-005`, `AC-PAR-001`, `AC-UX-003`, `AT-002` | checkout/root وبعض plan/status/currency values تكسر العرض العربي المتسق وPWA start | تعريب checkout والقيم، تحويل start_url لمسار صالح، formatter موحد واختبار 390px | Small |

### P1 — قبل customer pilot (`7`)

| المتطلبات | الفجوة | الإجراء | التعقيد |
|---|---|---|---|
| `AC-PLY-006` | تسجيل الإداري بلا plan/start/collection | وصله بمسار اشتراك/دفع معتمد دون دمج الهويات بالمال | Medium |
| `AC-ATT-006`, `AT-018` | CSV لا يحقق XLSX | توليد XLSX حقيقي بنفس tenant/filter guards | Medium |
| `AC-GOV-002`, `AC-ORG-001`, `AC-IAM-005` | settings/branding ناقصة | إعداد محدود للشعار/التواصل/اللون مع media policy | Medium |
| `AC-PAR-006/008` | سياق الرياضة/المباريات ناقص | إصلاح header/selector؛ لا Match قبل OD-011 | Small/Medium |
| `AC-DEMO-006/008`, `AT-030/032` | لا reset scoped ولا story/network interruption test جامع | reset آمن + story E2E + false-success tests | Medium |
| `AC-UX-002` | warnings/error adapter غير موحد | تثبيت form state وAPI error handling | Small |
| `AC-DEL-003` | manifest بلا service worker/offline policy | حسم minimum installability وتوثيقه | Small |

### P2 — نطاق لاحق (`8`)

1. `AC-EVA-008/009` و`AC-COM-004`: اعتماد OD-007 قبل trends/ranking.
2. `AC-SPP-002/004`: اعتماد OD-008؛ إبقاء catalog-only.
3. `AC-MED-002` و`AC-GAL-002/003`: OD-010 ثم private media slice.
4. `AC-COM-001/002`: specification فقط بعد OD-012.
5. `AC-COM-003`: audience/channel/delivery specification أولًا.
6. `AC-COM-005`: product discovery؛ لا استنتاج من أسماء المرجع.
7. `AC-IAM-008`: قرار إدراج حساب اللاعب الاختياري.
8. `AC-DEL-005`: Production Gate مستقلة بعد إغلاق الديمو وقرارات providers.

## المهمة التالية الوحيدة الموصى بها

**Slice 8D — Demo Closure: Admin Renewal + Guardian Arabic/PWA Cleanup**.

النطاق المحدود: (1) إنشاء تجديد Owner/Admin للاعب/التسجيل القائم باستخدام نفس transaction/idempotency/payment rules، (2) ربط quick action الإداري بالرحلة الجديدة، (3) تعريب checkout وplan/status/currency/date display، (4) استبدال root القديمة وتصحيح `manifest.start_url`، (5) إصلاح تحذير form state، (6) إضافة integration/FT/E2E عند 390px لـAdmin renewal وAT-002. لا يبدأ Communications أو provider حقيقي أو Production Gate أو أي OD غير معتمد.

**نقطة التوقف:** لم يبدأ Slice 8D ولم يُنفذ أي application code في فرع التدقيق.

## Post-Audit Slice 8D Closure

هذا ملحق لاحق ولا يغيّر نتائج التدقيق التاريخية أعلاه. نفّذ فرع `codex/14-slice8d-demo-closure` العائقين P0 اللذين حددهما التدقيق:

- `AC-SUB-004`: **IMPLEMENTED**. Owner/Admin ينشئان تجديدًا لتسجيل رياضي قائم عبر خدمة إنشاء مشتركة مع Guardian؛ التحقق tenant/resource-scoped، والإنشاء idempotent ومحمي بقفل transaction، ولا يحدث تحصيل أو إيصال أو فترة قبل تأكيد الدفع. الخصم والدفع وإعادة المحاولة وقواعد early/expired/cancelled تستخدم المسار المالي القائم.
- `AC-GOV-005`, `AC-PAR-001`, `AC-UX-003`, `AT-002`: أضيفت formatters/mappers عربية مشتركة للعملة والتاريخ والحالات وأنواع الباقة، ونُظفت رحلات Guardian الأساسية، وأصبح `/` يوجّه حسب الجلسة و`manifest.start_url` يساوي `/login`. لا يوجد offline support أو Service Worker.
- `AT-002`: **PASS** بناءً على اختبارات الواجهة ورحلات Mobile Chromium عند `390×844` التي تغطي الاتجاه العربي، checkout وحالات الدفع/العملة، الجذر وmanifest، مع عدم ظهور enums الإنجليزية المستهدفة.
- عوائق P0 المتبقية: **0**. الحكم بعد الإغلاق: **READY FOR FINAL DEMO REVIEW**. الحكم الإنتاجي يبقى **NOT PRODUCTION READY**.
- لا يُغلق `DM-12`؛ يظل **PARTIAL** لأن reset command المقيد بسجلات Demo غير منفذ وخارج نطاق Slice 8D. لذلك يبقى ملخص سيناريوهات الديمو `11 READY / 1 PARTIAL`، مع نجاح قصة التجديد الإداري الجديدة كدليل إضافي لا كبديل لمطلب reset.
- تبقى عناصر P1 السبعة المسجلة أعلاه مفتوحة للـpilot/بوابات لاحقة؛ لم يبدأ Communications أو provider حقيقي أو Production Gate، ولم تعتمد أي قرارات OD جديدة.
