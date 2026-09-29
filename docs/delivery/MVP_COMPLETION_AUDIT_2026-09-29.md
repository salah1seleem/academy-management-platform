# تدقيق اكتمال الـMVP وجاهزية العرض — 2026-09-29

**نطاق المراجعة:** Slices 0–8C عند الأساس `d017bd44db74945fa6c7b9359be0703bb914a76a`، على فرع التدقيق `codex/13-mvp-completion-audit`. هذه مراجعة أدلة وليست اعتمادًا لقرار منتج جديد أو تنفيذًا لميزة.

## الملخص التنفيذي

المنصة تملك الآن نواة عملية قوية: عزل أكاديميات وجلسات server-side، هيكل الأكاديمية واللاعب/التسجيل متعدد الرياضات، تجديد ودفع تجريبي idempotent، حضور فعلي ورصيد حصص، تقييمات وتقارير، تجربة ولي أمر ومحتوى، وتقارير مالية وتشغيلية مع تعديلات وخصومات محفوظة تاريخيًا. نجحت سلسلة migrations على schema جديدة، ونجح `295/295` اختبار Backend و`89/89` Frontend و`35/35` Mobile Chromium E2E.

الحكم مع ذلك هو **NOT DEMO READY** وفق تعريف Prompt 13، لوجود عائقين قبل عرض أصحاب المصلحة: (1) التجديد المباشر من الإدارة `AC-SUB-004` غير موجود، ورئيسية الإداري لا تقدم الإجراءات الثلاثة المطلوبة في `AC-IAM-003`؛ (2) مسارات أساسية مرئية ليست عربية/موبايل بالكامل: checkout بعنوان `Test Payment Gateway` وحالات/أنواع/عملة خام، ولوحة المالك يحدث بها overflow أفقي عند عرض مراجع الإيصالات على عرض 390px. لا يوجد فشل معروف في سلامة الماليات أو عزل البيانات.

النتيجة العددية للمتطلبات: **77 IMPLEMENTED، 22 PARTIALLY IMPLEMENTED، 3 DEFERRED BY CURRENT DELIVERY PLAN، 12 BLOCKED BY OPEN DECISION، 2 NOT IMPLEMENTED، 1 OUT OF CURRENT VERSION**. مجموع الصفوف 117 ومعرّفاتها فريدة بلا نقص أو تكرار.

حكم الإنتاج مستقل: **NOT PRODUCTION READY**؛ لا OTP/SMS حقيقي، ولا مزود دفع/Geidea/Apple Pay، ولا media storage إنتاجي، ولا نشر HTTPS أو أسرار/نسخ احتياطي/استعادة/مراقبة/دعم مثبتة.

## ما هو منفذ فعليًا

- Foundation: ASP.NET Core 10، EF Core/Npgsql، PostgreSQL، Next.js/React/TypeScript، manifest لـPWA، health/readiness وmigrations مقفلة.
- Identity/Tenancy: ASP.NET Core Identity، ticket store بقاعدة البيانات، session مدة 30 دقيقة sliding، CSRF، عضويات فردية، tenant من claim موثوق وإعادة تحقق من العضوية، وأكاديمية ثانية للعزل.
- Structure/People: فروع ورياضات وفئات ومجموعات وجداول أسبوعية ومدربون وإسناد، Player مستقل عن SportEnrollment، وربط GuardianPlayerLink صريح.
- Subscription/Finance: الأنواع الثلاثة، فترات وتاريخ، renewal/payment/collection/receipt، تجديد للغير بكود opaque، تعديلات وخصومات append-only وidempotency/concurrency.
- Attendance/Evaluation: TrainingSession فعلية، حالات `Present/Absent/NotRecorded` للاعب والجهاز الفني، حركات رصيد، criteria ومسودة/نشر وsnapshots وتقرير كرة القدم والرياضات الأخرى.
- Guardian/Content/Reports: بطاقات أبناء ورياضات deduplicated، الإجراءات الثلاثة، ملف طفل، كتالوج عرض فقط، تغذية معلوماتية، طب/معرض منشوران، dashboard مالك، تقارير CSV وإيصالات طباعة متصفح.

## منهج التصنيف والأدلة

`IMPLEMENTED` يتطلب API/منطقًا قابلًا للتشغيل وواجهة عند طلبها ودليل اختبار مناسب. `PARTIALLY IMPLEMENTED` يعني أن النواة تعمل لكن جزءًا صريحًا غائب. `BLOCKED BY OPEN DECISION` لا يعني أن default التقني فاشل؛ يعني أن اكتمال المتطلب تجاريًا/سلوكيًا ينتظر OD غير معتمد. الملفات المختصرة أدناه: `S2..S8` لمسارات API داخل `apps/api/src/Academy.Api`، و`UI` للمسارات/components داخل `apps/web/src`، و`IT/FT/E2E` لاختبارات integration/frontend/Playwright.

## مصفوفة المتطلبات — 117/117

| ID | العنوان المختصر | الحالة | دليل تنفيذي قابل للتشغيل | الجزء غير المكتمل / القرار |
|---|---|---|---|---|
| AC-GOV-001 | منتج مستقل من الصفر | IMPLEMENTED | مستودع standalone؛ Architecture test وحدود namespaces؛ لا dependency تشغيلية خارجية قديمة | — |
| AC-GOV-002 | قابلية البيع لعدة أكاديميات | PARTIALLY IMPLEMENTED | `AcademyMembership` وtenant-scoped keys؛ `TenantAuthenticationTests` وAcademy B | تخصيص الشعار/الألوان/بيانات التواصل غير متاح بالكامل من UI |
| AC-GOV-003 | البساطة والموعد | IMPLEMENTED | modular monolith وسجل slices وغياب microservices/التوسعات غير المأذونة | — |
| AC-GOV-004 | بيانات واحدة لكل الأدوار | IMPLEMENTED | API وDB مشتركان؛ E2E يثبت انتقال الحضور/التقييم/الدفع بين الأدوار | — |
| AC-GOV-005 | العربية وهوية العرض | PARTIALLY IMPLEMENTED | `lang=ar`, `dir=rtl` وهوية عامة؛ E2E auth | raw enums و`EGP` و`Test Payment Gateway` و404 إنجليزي؛ branding غير مكتمل |
| AC-GOV-006 | المصدر الأعلى وسجل التغيير | IMPLEMENTED | baseline مجمّد، `DECISIONS_v1.0.md` و`STATUS.md` وmigrations append-only | — |
| AC-ORG-001 | إعداد الأكاديمية | PARTIALLY IMPLEMENTED | Academy entity/name/zone/currency وseed | لا شاشة إعداد شعار/تواصل/لون أساسي |
| AC-ORG-002 | الفروع والرياضات والفئات | IMPLEMENTED | S2 structure endpoints وCRUD routes؛ `Owner_can_create_structure_in_own_academy` وdashboard E2E | — |
| AC-ORG-003 | المجموعات | PARTIALLY IMPLEMENTED | TrainingGroup + filters + CRUD وإسناد؛ S2 tests | لا UI مكتملة لإدارة الجدول الأسبوعي من سجل المجموعة |
| AC-ORG-004 | المدربون والجهاز الفني | PARTIALLY IMPLEMENTED | `/api/v1/staff`، CoachAssignment، staff attendance؛ tests | لا رحلة UI لإنشاء حساب موظف جديد؛ الموجود يركز على الإسناد |
| AC-ORG-005 | الجدول والحصة الفعلية | IMPLEMENTED | `TrainingSessionGenerator` وsession endpoints؛ idempotency/cancel tests | rescheduling المتقدم مؤجل كما تسمح المتطلبات |
| AC-IAM-001 | أنواع المستخدمين | IMPLEMENTED | Owner/Admin/Coach/Guardian policies وrole routes؛ E2E لكل دور | — |
| AC-IAM-002 | حساب فردي | IMPLEMENTED | ASP.NET Core Identity وAcademyMembership؛ لا shared-role password | — |
| AC-IAM-003 | تجربة الإداري | PARTIALLY IMPLEMENTED | `/dashboard`, player registration, attendance nav | الرئيسية تعرض زر تسجيل لاعب فقط؛ ينقص إجراءا التجديد والحضور المباشران |
| AC-IAM-004 | تجربة المدرب | IMPLEMENTED | redirect للجلسات، attendance/evaluation assigned-scope؛ `Coach_cannot_evaluate_unrelated_group` | — |
| AC-IAM-005 | تجربة المالك | PARTIALLY IMPLEMENTED | Owner dashboard/reports/finance E2E | إعدادات الأكاديمية الكاملة غير موجودة |
| AC-IAM-006 | دخول ولي الأمر وربط الأبناء | PARTIALLY IMPLEMENTED | Demo OTP challenge وexplicit links؛ `Demo_guardian_otp...` وGuardian IDOR tests | OTP/recovery/activation الإنتاجي غير منفذ رغم اعتماد OD-003 |
| AC-IAM-007 | صلاحيات الخادم | IMPLEMENTED | `TenantPermissionHandler` + resource predicates؛ IDOR/export/media tests | — |
| AC-IAM-008 | حساب اللاعب الاختياري | DEFERRED BY CURRENT DELIVERY PLAN | لا route أو policy أو login لاعب؛ بقي اختياريًا في الخطة | يحتاج قرار إدراجه قبل بناءه |
| AC-PLY-001 | ملف اللاعب | PARTIALLY IMPLEMENTED | Player entity وlist/detail/edit؛ height/weight/foot/position | ملف UI لا يجمع الصورة/العمر المحسوب/هاتف الولي وكل الحقول المرجعية في عرض واحد |
| AC-PLY-002 | ولي أمر لعدة أبناء | IMPLEMENTED | GuardianPlayerLink؛ `Guardian_with_two_children_sees_only_both` وGuardian home E2E | — |
| AC-PLY-003 | طفل في عدة رياضات | IMPLEMENTED | Player→SportEnrollment؛ multi-sport integration وGuardian tests | — |
| AC-PLY-004 | عدم تكرار اللاعب | IMPLEMENTED | existing-player registration والrenewal على enrollment؛ duplicate tests | — |
| AC-PLY-005 | البحث والفلاتر الموحدة | PARTIALLY IMPLEMENTED | player search code/name/phone وbranch/sport/category/group؛ group/coach scope tests | ليست كل شاشات المدرب والإدارة تستخدم المجموعة الكاملة نفسها |
| AC-PLY-006 | تسجيل لاعب جديد | PARTIALLY IMPLEMENTED | transaction تنشئ player/guardian/link/enrollment؛ mobile E2E | لا اختيار باقة/تاريخ بداية/تحصيل داخل رحلة الإداري كما ينص baseline |
| AC-PLY-007 | الأرشفة وحفظ التاريخ | PARTIALLY IMPLEMENTED | IsActive/status toggles وrestrict FKs؛ deactivation relation test | نقل المجموعة وسياسة archive الكاملة وسجلها ليست رحلة مكتملة |
| AC-SUB-001 | تعريف الباقات | IMPLEMENTED | SubscriptionPlan CRUD/routes؛ duration/price/sessions fields | — |
| AC-SUB-002 | أنواع الباقات الثلاثة | IMPLEMENTED | `Duration/Sessions/Combined` validations؛ payment/attendance tests | — |
| AC-SUB-003 | فترة الاشتراك وحالته | IMPLEMENTED | SubscriptionPeriod/list/detail/status/balance؛ seed وحسابات | — |
| AC-SUB-004 | التجديد من الإدارة | NOT IMPLEMENTED | لا admin create-renewal API أو UI؛ admin يستطيع قراءة الطلب/الخصم فقط | رحلة مطلوبة كاملة غائبة |
| AC-SUB-005 | تجديد ولي الأمر | IMPLEMENTED | `/guardian/renew/[enrollmentId]` + CreateRenewal + checkout E2E | — |
| AC-SUB-006 | تجديد للغير | IMPLEMENTED | hashed expiring reference + external renewal؛ 17 IT وE2E | — |
| AC-SUB-007 | التاريخ والتجديد المبكر | IMPLEMENTED | append-only periods؛ `Early_renewal...` و`Expired_renewal...` | — |
| AC-SUB-008 | التجميد | IMPLEMENTED | adjustment service/UI/audit؛ Slice 8B IT/E2E | default معتمد تقنيًا تحت OD-001 وليس سياسة تجارية عامة |
| AC-SUB-009 | إضافة/خصم أيام | IMPLEMENTED | days adjustment مع سبب/version/idempotency؛ tests | — |
| AC-SUB-010 | إلغاء الاشتراك | IMPLEMENTED | cancel period فقط مع حفظ المال/الحضور؛ tests | لا refund تلقائي، وهذا مقصود |
| AC-SUB-011 | خصومات الاشتراك | IMPLEMENTED | server calculation/snapshot/audit/replacement payment؛ 31 IT و4 E2E | السياسة الأوسع بقيت OD-001 pending |
| AC-SUB-012 | القريب والمنتهي | IMPLEMENTED | state queries/routes/dashboard metrics وseed | نافذة السبعة أيام default تقني غير اعتماد تجاري |
| AC-ATT-001 | حضور اللاعبين والمدربين | IMPLEMENTED | player/staff rosters منفصلان؛ Attendance IT/E2E | — |
| AC-ATT-002 | سجل الحصة ومنع التكرار | IMPLEMENTED | unique keys + service correction/audit؛ duplicate/concurrency tests | — |
| AC-ATT-003 | التسجيل بالكود | IMPLEMENTED | roster search بالاسم أو PlayerCode مع عرض الاشتراك؛ no scanner dependency | — |
| AC-ATT-004 | التقويم والتاريخ | PARTIALLY IMPLEMENTED | child history/summary وattendance history/report | لا calendar visual ولا نسبة حضور بمقام الحصص المنعقدة |
| AC-ATT-005 | أثر الحضور على الرصيد | IMPLEMENTED | movement audit وtransaction؛ present/correction/freeze tests | — |
| AC-ATT-006 | التصدير الشهري | PARTIALLY IMPLEMENTED | فلترة شهر/سنة وتصدير CSV حقيقي مع tenant scope/injection guard | baseline يطلب XLSX صراحة ولا يسمح بتسميـة CSV كـExcel |
| AC-ATT-007 | التسجيل السريع | IMPLEMENTED | «تحديد الحاضرين» ثم تعديل واضح وحفظ صريح؛ E2E | — |
| AC-EVA-001 | معايير قابلة للإدارة | IMPLEMENTED | criteria CRUD/sport/axis/weight + UI؛ tests | — |
| AC-EVA-002 | حفظ ونشر التقييم | IMPLEMENTED | draft/update/publish immutable؛ coach/guardian E2E | — |
| AC-EVA-003 | صفر ليس missing | IMPLEMENTED | nullable scores وحدود 0–100؛ boundary/missing tests | — |
| AC-EVA-004 | أعلى تقرير اللاعب | IMPLEMENTED | report hero name/photo/position/overall؛ FT/E2E | — |
| AC-EVA-005 | محاور كرة القدم الستة | IMPLEMENTED | calculator + radar/text axes؛ weighted calculation test | — |
| AC-EVA-006 | العمر/الطول/الوزن/القدم | IMPLEMENTED | Player fields وأربع cards؛ Guardian E2E | — |
| AC-EVA-007 | المعايير التفصيلية | IMPLEMENTED | snapshots ثم detailed cards ولا pitch؛ E2E | — |
| AC-EVA-008 | حساب الإجمالي | BLOCKED BY OPEN DECISION | `IEvaluationReportCalculator` default موثق واختبارات دقيقة | OD-007 لم يعتمد الصيغة والأوزان |
| AC-EVA-009 | التقرير عبر الفترات | BLOCKED BY OPEN DECISION | reportingPeriod محفوظ، ولا trend مزيف | المقارنة/الأسبوعي/الشهري/السنوي والألوان تنتظر OD-007 |
| AC-EVA-010 | الرياضات الأخرى | IMPLEMENTED | swimming criteria وتقرير بلا football radar؛ IT | — |
| AC-PAR-001 | مرجع بصري عربي | PARTIALLY IMPLEMENTED | Guardian RTL mobile cards/actions/nav؛ E2E و390px capture | branding غير مكتمل ونصوص إنجليزية في checkout |
| AC-PAR-002 | بطاقات الأبناء | IMPLEMENTED | deduplicated child cards؛ Guardian home tests/E2E | — |
| AC-PAR-003 | أقسام المنتجات حسب الرياضات | BLOCKED BY OPEN DECISION | active-enrollment sport union/dedupe؛ catalog tests | أهلية الاشتراك المنتهي للظهور تنتظر OD-008 |
| AC-PAR-004 | الإجراءات الثلاثة | IMPLEMENTED | home links: new/renew/renew-other؛ FT/E2E | — |
| AC-PAR-005 | رحلة الاشتراك الجديد | BLOCKED BY OPEN DECISION | NewEnrollmentRequest + review/approval atomic؛ tests | مسؤولية اختيار المجموعة ما زالت default تحت OD-006 |
| AC-PAR-006 | رأس الطفل وسياق الرياضة | PARTIALLY IMPLEMENTED | اسم/رياضات ثم sections per enrollment | لا selector صريح، ولا الفرع والفترة في رأس الصفحة نفسه |
| AC-PAR-007 | قائمة ملف اللاعب | IMPLEMENTED | report/schedule/nutrition/medical/gallery/renewal، ولا AI/intensive | — |
| AC-PAR-008 | التدريبات والمباريات | BLOCKED BY OPEN DECISION | recurring/upcoming training sessions مع group/time | لا Match/Event؛ حقول وسلوك المباريات تنتظر OD-011 |
| AC-PAR-009 | التجديد من الملف | IMPLEMENTED | contextual `/guardian/renew/{enrollmentId}` وخطط نفس الرياضة | — |
| AC-PAR-010 | لا دورات مكثفة/AI | OUT OF CURRENT VERSION | لا routes/buttons/models؛ FT يؤكد الأقسام المطلوبة فقط | مستبعد صراحة من Product Owner |
| AC-SPP-001 | كتالوج المنتجات الرياضية | IMPLEMENTED | SportCatalogItem admin CRUD + Guardian read | — |
| AC-SPP-002 | بطاقة المنتج | BLOCKED BY OPEN DECISION | صورة/اسم/رياضة/سعر/خصم metadata | stars/favourites ومعناهما ينتظران OD-008 |
| AC-SPP-003 | الربط والظهور | IMPLEMENTED | tenant/sport/active/order filtering؛ Slice7 isolation/dedupe tests | — |
| AC-SPP-004 | معنى الحزمة وحدود البيع | BLOCKED BY OPEN DECISION | catalog-only ولا endpoints تجارة | البائع/الحزمة/الشراء تنتظر OD-008 |
| AC-SPP-005 | بيانات عرض المنتجات | IMPLEMENTED | football/swimming synthetic catalog/assets seeded | — |
| AC-NUT-001 | مكتبة معلوماتية | IMPLEMENTED | Nutrition model/read UI بلا commerce؛ model reflection test | — |
| AC-NUT-002 | ثلاثة أقسام | IMPLEMENTED | breakfast/lunch/dinner tabs؛ E2E | — |
| AC-NUT-003 | بطاقة الوجبة | IMPLEMENTED | Arabic image/name/description بلا price/rating/cart | — |
| AC-NUT-004 | تفاصيل الوجبة | IMPLEMENTED | image/description/four facts/serving؛ E2E | — |
| AC-NUT-005 | بيانات من أول تشغيل | IMPLEMENTED | 15 unique DB items / 18 placements + assets | — |
| AC-NUT-006 | 18 موضعًا/15 اسمًا | IMPLEMENTED | exact-count integration tests | — |
| AC-NUT-007 | منع التجارة | IMPLEMENTED | لا properties/endpoints مالية؛ negative model/route tests | — |
| AC-NUT-008 | المصدر وحالة المراجعة | IMPLEMENTED | serving/source/dataStatus ووسم demo-unreviewed | الاعتماد العلمي النهائي ينتظر OD-009 لكنه لا يمنع العرض الصادق |
| AC-NUT-009 | لا وصفة شخصية | IMPLEMENTED | read response عامة غير child-personalized؛ IT | — |
| AC-NUT-010 | الحالة والترتيب والتحرير | IMPLEMENTED | admin CRUD/categories/order/active/status | default قابل للعكس تحت OD-009 |
| AC-MED-001 | إصابات واستشارات | IMPLEMENTED | PlayerMedicalRecord admin/guardian routes؛ publish tests | — |
| AC-MED-002 | حقول سجل مبسط | BLOCKED BY OPEN DECISION | type/title/date/description/status/notes/publish موجودة | attachment ومسؤوليات الإدخال/الرؤية النهائية تنتظر OD-010 |
| AC-MED-003 | عرض مقيد وبيانات اصطناعية | IMPLEMENTED | guardian published-only، staff notes hidden، synthetic seed؛ IT/E2E | — |
| AC-GAL-001 | معرض الطفل | IMPLEMENTED | Guardian gallery وpublished synthetic assets؛ E2E | — |
| AC-GAL-002 | إدارة الوسائط | BLOCKED BY OPEN DECISION | metadata CRUD/player/publish/order موجود | upload/group assignment/coach role تنتظر OD-010 |
| AC-GAL-003 | خصوصية الصور | IMPLEMENTED | allowlisted local refs + linked-child/tenant/published checks | production object storage ضمن Production Gate |
| AC-FIN-001 | تحصيل الاشتراكات فقط | IMPLEMENTED | Collection/report totals من confirmed payments فقط | — |
| AC-FIN-002 | سجل العملية | IMPLEMENTED | immutable collection/receipt snapshots/provider refs | — |
| AC-FIN-003 | فصل الطلب عن الدفع | IMPLEMENTED | RenewalRequest/PaymentRequest منفصلان؛ pending/fail بلا revenue | provider الحقيقي غير داخل demo |
| AC-FIN-004 | إيصال وطباعة | IMPLEMENTED | receipt route + Arabic HTML + `window.print`؛ E2E | PDF/thermal/tax invoice غير معتمد تحت OD-012 |
| AC-FIN-005 | عدم تكرار المال | IMPLEMENTED | DB constraints/transactions/idempotency/provider event replay tests | — |
| AC-RPT-001 | لوحة المالك | IMPLEMENTED | stored owner summary + breakdowns/latest؛ IT/E2E | mobile overflow مسجل كعيب UX لا كغياب المنطق |
| AC-RPT-002 | التقارير المالية | IMPLEMENTED | filters, totals, CSV, receipt links؛ 32 Reporting IT | — |
| AC-RPT-003 | تقارير التشغيل | IMPLEMENTED | attendance report + current/expiring/expired/player lists | — |
| AC-RPT-004 | تعريف المؤشرات | IMPLEMENTED | player distinct counts، confirmed collection sums، actual sessions | — |
| AC-COM-001 | بوت ولي الأمر | DEFERRED BY CURRENT DELIVERY PLAN | لا integration أو route؛ محفوظ في mapping/plan | توقيت الإصدار يحتاج OD-001/OD-012 |
| AC-COM-002 | ربط البوت وضوابطه | BLOCKED BY OPEN DECISION | لا implementation؛ حدود الأمان موثقة فقط | provider/linking/audience تنتظر OD-012 |
| AC-COM-003 | رسائل فردية وجماعية | DEFERRED BY CURRENT DELIVERY PLAN | لا message model/API/UI | محفوظة ولم يُعتمد تنفيذها في cut الحالي |
| AC-COM-004 | أفضل اللاعبين والترتيب | BLOCKED BY OPEN DECISION | لا ranking projection | formula/period/privacy تنتظر OD-007 وOD-012 |
| AC-COM-005 | مستخدمون/اتصالات/دعم | BLOCKED BY OPEN DECISION | staff API جزئي ليس شاشة المرجع؛ لا tickets/connections | أسماء المرجع غير كافية؛ OD-012 |
| AC-UX-001 | موبايل أولًا | PARTIALLY IMPLEMENTED | Pixel 7 E2E و390×844 review؛ Guardian/Admin/Coach بلا overflow | Owner dashboard overflow أفقي؛ بعض المحتوى كثيف |
| AC-UX-002 | حالات الشاشة والحفظ | PARTIALLY IMPLEMENTED | loading/error/empty وserver persistence في أغلب screens | معالجة network response غير متسقة وتحذير controlled/uncontrolled متكرر |
| AC-UX-003 | عرض عربي متسق | PARTIALLY IMPLEMENTED | معظم النصوص وdates عربية/RTL | enums/currency/provider/status/404 وبعض dates خام |
| AC-DEMO-001 | بيانات لكل ميزة | IMPLEMENTED | deterministic Slice1–8C seeds وE2E journeys | — |
| AC-DEMO-002 | أسماء مصرية خيالية | IMPLEMENTED | Nogoom synthetic accounts/players/assets؛ لا بيانات حقيقية | — |
| AC-DEMO-003 | سيناريوهات مترابطة | IMPLEMENTED | siblings/multi-sport/statuses/attendance/evaluation/adjustment/discount seeds | — |
| AC-DEMO-004 | تاريخ عرض مرجعي | IMPLEMENTED | `Demo__ReferenceDate`/`ISubscriptionClock` يقود expiry/metrics | — |
| AC-DEMO-005 | مكتبة تغذية جاهزة | IMPLEMENTED | DB-seeded 15/18 وحالة مصادر واضحة | — |
| AC-DEMO-006 | إعادة تهيئة آمنة | PARTIALLY IMPLEMENTED | rerun idempotency واختبارات، وguards تمنع Demo خارج البيئة | لا reset command صريح ومقيد؛ التحقق تم على schema جديدة |
| AC-DEMO-007 | حسابات وعزل | IMPLEMENTED | 4 أدوار + Academy B + negative tests | — |
| AC-DEMO-008 | القصة الأساسية | PARTIALLY IMPLEMENTED | أجزاء القصة كلها قابلة للتنفيذ ومغطاة عبر E2E | ليست رحلة E2E واحدة من التسجيل حتى التحصيل؛ admin renewal غائب |
| AC-DEL-001 | التوثيق قبل التنفيذ | IMPLEMENTED | approved baseline/architecture/plan قبل slices | — |
| AC-DEL-002 | اتجاه تقني مستقل | IMPLEMENTED | modular monolith .NET/Next/PostgreSQL بلا إرث قديم | — |
| AC-DEL-003 | PWA أولًا | PARTIALLY IMPLEMENTED | responsive web + manifest/icon/standalone metadata | لا service worker/offline؛ native stores غير مدعاة |
| AC-DEL-004 | العمل على الكود | IMPLEMENTED | repo مستقل، branches صغيرة، CI/tests/docs | — |
| AC-DEL-005 | بوابة الإنتاج | NOT IMPLEMENTED | لا دليل deploy/restore/backup/monitoring/HTTPS/support gate | يجب تنفيذ Production Gate قبل أي عميل حقيقي |
| AC-DEL-006 | لا توسعات غير مأذونة | IMPLEMENTED | لا commerce nutrition/SaaS billing/Kafka/native/AI وغيرها | — |

### فحص المصفوفة

- baseline: 117 occurrence / 117 unique.
- هذه المصفوفة: 117 occurrence / 117 unique؛ missing `0`، duplicate `0`، extra `0`.
- الأصل بقي في `docs/requirements/ACADEMY_REQUIREMENTS_BASELINE_AR_v1.0.md` دون تعديل، ولم تتحول أي توصية أو OD pending إلى موافقة.

## تدقيق معايير القبول — 34/34

| AT | الحالة | الاختبار/الدليل الفعلي |
|---|---|---|
| AT-001 | COVERED INDIRECTLY | Architecture dependency test + جرد repository/remotes/namespaces؛ لا اختبار آلي مخصص باسم الاستقلال |
| AT-002 | NOT IMPLEMENTED | `authentication.spec.ts` يثبت `ar/rtl`، لكن الفحص المرئي وجد نصوصًا تقنية إنجليزية؛ السيناريو الكامل لا ينجح |
| AT-003 | PASS | `Academy_A_user_cannot_access_Academy_B_probe`, ID-tampering tests، export/media/content isolation suites |
| AT-004 | PASS | `Player_persists_after_reload` و`structure-people.spec.ts` (تسجيل حقيقي من الهاتف) |
| AT-005 | PASS | `Guardian_with_two_children_sees_only_both`, `Guardian_sees_each_linked_child_once...` وGuardian E2E |
| AT-006 | PASS | `Same_player_has_football_and_swimming_enrollments`, multi-sport Guardian profile tests |
| AT-007 | PASS | `Guardian_catalog_deduplicates_sport_sections` وSlice7 Journey A |
| AT-008 | PASS | `guardian-core.test.tsx` يثبت الإجراءات، وE2E يدخل new enrollment وrenew/renew-other في suites مستقلة |
| AT-009 | PASS | `integrates_the_five_required_content_areas...` وGuardian Journey D؛ لا AI/intensive routes |
| AT-010 | PASS | `Adding_sport_reuses_existing_player...`, `Repeated_approval_creates_no_duplicate_enrollment` وتجديدات الدفع |
| AT-011 | PASS | `Early_renewal_starts_after_existing_end`, `Expired_renewal_starts_on_reference_date` |
| AT-012 | PASS | `Failed_payment_creates_no_collection`, `Pending_payment_is_excluded_from_revenue` |
| AT-013 | PASS | `Duplicate_success_callback...` للـcollection/receipt/period وHTTP idempotency tests |
| AT-014 | PASS | ExternalRenewal suite + `Guardian renews for another player without gaining profile access` |
| AT-015 | PASS | cross-tenant/filter mismatch و`Coach_cannot_access_unassigned_group_session/evaluation` + dependent group UI E2E |
| AT-016 | PASS | unique attendance، الثلاث حالات، staff separation وAttendance E2E |
| AT-017 | PASS | consumption/correction/concurrency/staff tests وSlice8B frozen-combined journey |
| AT-018 | NOT IMPLEMENTED | CSV filters/security/downloads مختبرة، لكن معيار القبول يطلب اسم/صيغة XLSX فعلية |
| AT-019 | PASS | `Score_outside_zero_to_one_hundred_is_rejected`, `Draft_evaluation_keeps_nullable_scores` |
| AT-020 | PASS | draft hidden/published visible/assigned scope tests وCoach→Guardian E2E |
| AT-021 | PASS | Guardian report E2E يثبت position/radar/4 cards/details/no pitch |
| AT-022 | DEFERRED | الحساب default مختبر بـ`Football_axis_weighted_calculation_is_correct`، لكن «قاعدة معتمدة» تنتظر OD-007 |
| AT-023 | PASS | `Swimming_report_has_no_football_radar_data` |
| AT-024 | PASS | exact 3 categories/18 placements/15 unique + Nutrition Journey B |
| AT-025 | PASS | `Nutrition_model_has_no_commerce_properties`, no financial effect وE2E forbidden labels |
| AT-026 | PASS | serving/four facts/demo-unreviewed assertions في Slice7 IT/E2E |
| AT-027 | PASS | medical/media linked-child/tenant/publish/notes tests وJourneys C/D |
| AT-028 | PASS | `Dashboard_report_and_receipts_are_consistent`, `Content_areas_produce_no_financial_total` وdiscount truth suite |
| AT-029 | PASS | 41 declared adjustment facts + 31 discount facts + 8 E2E journeys؛ receipt print E2E |
| AT-030 | COVERED INDIRECTLY | seed rerun tests تغطي عدم المضاعفة وenvironment guards؛ لا reset command إنتاجي مستقل |
| AT-031 | COVERED INDIRECTLY | fixed clock وexpired/early tests وseed lists؛ لا اختبار واحد يجمع الأعمار والفئات وكل حالات التاريخ |
| AT-032 | COVERED INDIRECTLY | persistence/reload موجودان؛ false-success عند network interruption ليس مغطى مباشرة عبر كل mutations |
| AT-033 | PASS | `medical empty state is honest`, criteria/requests/reports/receipts empty-state frontend tests |
| AT-034 | DEFERRED | Demo/Test guards تعمل؛ Production Gate نفسها لم تنفذ |

**الإجمالي:** PASS `26`، COVERED INDIRECTLY `4`، NOT TESTED `0`، NOT IMPLEMENTED `2`، DEFERRED `2`.

## تدقيق سيناريوهات الديمو — DM-01..DM-12

كل الحسابات أدناه تخص أكاديمية النجوم الصناعية. Staff يبدأ من `/login`، وGuardian يستخدم الهاتف التجريبي وOTP الثابت الموثقين في `docs/demo/SLICE1_DEMO_ACCOUNTS.md`.

| DM | الحالة | الحساب والمسار والبيانات وما يُعرض | العائق |
|---|---|---|---|
| DM-01 | READY | Guardian «سارة» `/guardian` → عمر/كرة القدم → `/guardian/renew/{enrollment}` → checkout؛ Owner يراجع dashboard/report. عمر له مجموعة ومواعيد وحضور وتقييم واشتراك قريب؛ الدفع التجريبي ينشئ collection/receipt/period. | رحلة الإداري المباشرة ليست جزء هذا السيناريو المحدد |
| DM-02 | READY | Guardian `/guardian`: عمر ومريم/الأبناء، سباحة وكرة؛ بطاقات وسياقات منفصلة. | — |
| DM-03 | READY | Guardian → ملف عمر: PlayerId واحد وFootball/Swimming enrollments؛ كل جدول/اشتراك/تقييم scoped. | — |
| DM-04 | READY | `/guardian`: عمر ومريم مشتركان في كرة القدم لكن قسم الكتالوج يظهر مرة؛ Slice7 Journey A. | — |
| DM-05 | READY | Guardian `/guardian/renew-for-another`، الكود `RNW-DEMO-NG-0003-7K9M`، checkout/receipt؛ المستفيد «عمر أحمد حسن» لا يظهر كطفل للدافع. | — |
| DM-06 | READY | Coach `/dashboard/evaluations` → تقييم جديد/مسودة/نشر؛ Guardian child report. seed يحوي draft/published وtests تقبل 0/100 وترفض الخارج. | formula approval OD-007 لا يمنع عرض default المعلن |
| DM-07 | READY | Admin/Coach `/dashboard/attendance/sessions` و`/staff` و`/history`; حاضر/غائب/لم يسجل وجهاز فني وتصحيح بلا duplicate. | — |
| DM-08 | READY | Guardian child → `/nutrition` → tabs/meal detail؛ 15/18 ومعلومات بلا تجارة. | القيم موسومة demo-unreviewed كما يجب |
| DM-09 | READY | Admin current subscription → period detail لتجميد/أيام/إلغاء؛ renewal detail للخصم؛ receipt print. | لا زر وهمي؛ كل ما يظهر منفذ |
| DM-10 | READY | Academy B + unrelated coach/guardian عبر direct IDs؛ suites tenant/guardian/coach/content تمنع الوصول. | — |
| DM-11 | READY | Guardian عمر → medical/gallery؛ published synthetic فقط. مريم تعطي empty state صحيح. | production upload غير داخل الديمو |
| DM-12 | PARTIAL | تغيير `Demo__ReferenceDate` مع schema جديدة يعيد حساب expiry/metrics، وseed rerun لا يضاعف المصادر. | لا أمر reset صريح وآمن يعيد الحالة نفسها داخل DB مستخدمة بعد أسبوع |

**الإجمالي:** READY `11`، PARTIAL `1`، NOT READY `0`.

## مراجعة رحلات الأدوار

### Academy Owner

المسارات المطلوبة موجودة وقابلة للوصول من accordion navigation: dashboard، reports، collections، subscription periods/adjustments، renewal discounts، structure، players، requests، content وreceipts. الأذونات المالية Owner/Admin فقط صحيحة. الملاحظات: dashboard عند 390px له overflow أفقي من receipt/provider references الطويلة؛ status/currency غير معرّبة في بعض قوائم الاشتراك؛ settings/branding ليست رحلة مكتملة.

### Academy Admin

التسجيل، الطلبات، الحضور، التقييمات، التقارير، المحتوى، الطب والمعرض تعمل. الرئيسية لا تحقق التركيز على الإجراءات الثلاثة: تعرض «تسجيل لاعب جديد» فقط ولا shortcut للحضور أو تجديد إداري، والتجديد الإداري نفسه غير منفذ. النتيجة: رحلة تشغيل واسعة لكنها ليست رحلة MVP المكتملة المعتمدة.

### Coach

بعد الدخول ينتقل إلى sessions، ويرى attendance/evaluations/attendance report فقط. API يقيد الجلسات والتقييمات بالمجموعات المسندة ويمنع الماليات والمعايير الإدارية. لا dead navigation مثبت؛ تجربة «قائمة مجموعاتي/لاعبي» ليست صفحة مستقلة لكنها ممثلة داخل rosters/evaluation selectors.

### Guardian

OTP Demo، home، dedupe الأطفال/الرياضات، الإجراءات الثلاثة، profile، published report، schedule، attendance، subscriptions، renewal-other، test checkout، receipts، nutrition، medical، gallery/catalog كلها قابلة للعرض ومغطاة. العيوب المرئية الأساسية: checkout يحمل عنوانًا إنجليزيًا وحالات/عملة خام؛ بعض profile context موزع على sections بدل رأس واحد؛ «المباريات» غير منفذة تحت OD-011.

## تدقيق Dead UI

لم يجد البحث أزرار `TODO/Coming soon` أو روابط تنقل إلى route غير موجودة، ولا Export/Print/Payment/Upload/Apple Pay/Notification زائفًا ظاهرًا. CSV links ترجع تنزيلًا فعليًا، Print يستدعي browser print، والدفع موسوم بوضوح Test Gateway ويؤثر على DB التجريبية. النتائج:

| التصنيف | النتيجة |
|---|---|
| BLOCKER | لا يوجد dead button تقني؛ العائق الوظيفي هو غياب admin-renewal لا زر وهمي |
| SHOULD FIX BEFORE DEMO | `/` صفحة legacy يتيمة تعرض «Slice 1» و«لا توجد بيانات لاعبين أو اشتراكات» رغم اكتمال Slices 2–8C؛ `manifest.start_url` يشير إليها |
| SHOULD FIX BEFORE DEMO | Admin home يوحي بالاختيار من القائمة لكنه لا يقدم quick actions الثلاثة |
| ACCEPTABLE DEFERRED | لا Upload/Apple Pay/notifications/real gateway buttons؛ غيابها صادق ومعلن |

## تدقيق المسارات — 76 صفحة

نتيجة production build هي 42 static-generation items وroute tree صالح. التقسيم الكامل:

- عام: `/`, `/login`.
- Dashboard root: `/dashboard`.
- Academy: `/dashboard/academy/[kind]`, `/new`, `/[id]`, `/[id]/edit`؛ الأنواع branches/sports/categories/groups/coaches.
- People: `/dashboard/players`, `/new`, `/[id]`, `/[id]/edit`; `/dashboard/guardians`, `/[id]`, `/[id]/edit`; `/dashboard/enrollment-requests`, `/[id]`.
- Player content: `/dashboard/players/medical`, `/new`, `/[id]`, `/[id]/edit`; ونظائرها `/dashboard/players/media/...`.
- Catalog/Nutrition: `/dashboard/content/catalog`, `/new`, `/[id]`, `/[id]/edit`; ونظائرها nutrition.
- Attendance: `/dashboard/attendance/sessions`, `/sessions/[id]/players`, `/sessions/[id]/staff`, `/staff`, `/history`.
- Evaluations: `/dashboard/evaluations`, `/new`, `/[id]/edit`, `/[id]/report`, `/criteria`, `/criteria/new`, `/criteria/[id]/edit`, `/reports`.
- Subscriptions: `/dashboard/subscriptions/plans`, `/new`, `/[id]`, `/[id]/edit`, `/current`, `/expiring`, `/expired`, `/periods/[id]`, `/renewals`, `/renewals/[id]`, `/payments`, `/collections`, `/receipts/[receiptId]`.
- Reports: `/dashboard/reports/financial`, `/attendance`, `/receipts`.
- Guardian: `/guardian`, `/children/[playerId]`, `/children/[playerId]/nutrition`, `/medical`, `/gallery`, `/evaluations/[id]`, `/nutrition/[id]`, `/enrollment-requests`, `/new`, `/renew/[enrollmentId]`, `/renew-for-another`, `/checkout/[paymentId]`, `/receipts`, `/receipts/[receiptId]`, `/subscriptions/periods/[id]`.

المسارات dynamic detail/new/edit تصل إليها القوائم، باستثناء `/` legacy المذكورة. لا route يشير إلى AI/intensive/commerce nutrition/communications. `/dashboard/reports/receipts` قائمة و`/dashboard/subscriptions/receipts/[id]` detail؛ هذا فصل مقبول وليس duplicate page. role visibility في shell صحيحة إجمالًا ويعاد توجيه Coach وGuardian، لكن authorization الحقيقي يبقى في API كما تثبت الاختبارات.

## مراجعة الهاتف والعربية/RTL

أُجري capture فعلي headless عند `390×844` لـOwner/Admin/Coach/Guardian/child profile، إضافة إلى E2E الرسمي بمشروع Pixel 7 (`412×839`). القياس البرمجي لـ`scrollWidth > innerWidth`: Owner `true`، وبقية الشاشات الخمس `false`.

- Guardian home/profile: cards والأزرار والتنقل السفلي صالحون للمس؛ لا overflow. شريط Next development ظهر في capture المحلي فقط ولا يظهر في production build.
- Coach sessions: filters تتحول إلى عمود واحد، والأفعال قابلة للمس؛ لا overflow.
- Admin home: صالح بصريًا لكنه فارغ وظيفيًا نسبيًا ويفتقد shortcuts.
- Owner dashboard: latest receipt rows ذات IDs طويلة غير قابلة للكسر توسّع الصفحة؛ **SHOULD FIX BEFORE DEMO**.
- checkout/renewal/discount/plan pages تعرض `Test Payment Gateway`, `Pending`, `Confirmed`, `Failed`, `PaymentInProgress`, `Duration/Sessions/Combined` و`EGP` في مواضع للمستخدم؛ **SHOULD FIX BEFORE DEMO**.
- تواريخ عديدة تعرض ISO (`2026-09-28`) بينما مواضع أخرى تستخدم `ar-EG`؛ هذا عدم اتساق وليس فساد بيانات.
- أخطاء API غالبًا عربية، لكن بعض components تستبدل كل response غير ناجح برسالة عامة وبعضها يعرض `message` الخام؛ يلزم formatter موحد.

## سلامة البيانات والماليات

لا تناقض cross-slice مثبت في invariants الأساسية:

- `Player` منفصل عن `SportEnrollment`، وروابط `GuardianPlayerLink` صريحة؛ composite/alternate tenant keys تمنع علاقات عابرة للأكاديمية.
- الفترات والتحصيل والإيصالات والتعديلات والخصومات والتقييمات المنشورة تحفظ history/snapshots ولا تُعاد كتابتها بأثر رجعي.
- enrollment approval transaction واحدة وreplay لا ينشئ enrollment مكررًا.
- Attendance unique per actual session/person؛ movement audit يعالج الاستهلاك والتصحيح مرة واحدة.
- medical/media guardian projections تحجب draft و`StaffNotes` والأكاديمية/الطفل الآخر.

للدفع الناجح يتحقق التسلسل التالي في `PaymentProcessor` واختبارات Slice 8C:

`PaymentRequest.Amount = RenewalRequest.FinalAmount = Collection.Amount = Receipt.FinalAmount/Amount = financial report contribution = owner dashboard contribution`.

الأدلة المباشرة: `Financial_truth_is_consistent_across_all_records`, `Discounted_success_creates_one_final_collection`, `Receipt_contains_original_discount_and_final`, `Financial_report_total_increases_by_final_only`, `Owner_dashboard_revenue_increases_by_final_only`, `Duplicate_success_callback...`, و`Late_success_for_failed/cancelled...has_no_financial_effect`. التجميد/الأيام/الإلغاء لا تعدل المال؛ إلغاء الفترة لا يحذف الإيراد؛ retry يحتفظ snapshot ويمنع double charge.

## جاهزية الدفع الحقيقي

| العنصر | التصنيف | الوضع الدقيق |
|---|---|---|
| Internal Test Gateway | architecture-ready للديمو فقط | يعمل ومحصور في Demo/Testing مع guard؛ ليس provider ماليًا |
| بطاقات حقيقية | implementation required | لا adapter/checkout/webhook production ولا PCI/operational flow |
| Geidea | implementation required + external onboarding required | مذكور كمفضل في OD-005 فقط؛ لا SDK/config/merchant |
| Apple Pay | implementation required + external onboarding required | لا entitlement/domain verification/merchant certificate/provider support |
| webhook verification | architecture-ready جزئيًا | event replay/idempotency/amount checks موجودة، لكن لا signature/parser لprovider حقيقي |
| merchant configuration | external onboarding required | merchant account، agreements، currencies، callback URLs وtest/live approval مطلوبة |
| production secrets | implementation required | يجب secret store/rotation/separation؛ `.env` محلي فقط |
| reconciliation | implementation required | التقرير من collections الداخلية؛ لا settlement import/dispute/refund/reconciliation مع provider |

## القرارات المفتوحة OD-001..OD-012

لا قرار جديد اعتُمد في هذا التدقيق.

| OD | الحالة | أثره الحالي |
|---|---|---|
| OD-001 | PENDING but reversible default already implemented | default discount/adjustments موجود؛ يحجب تثبيت cut تجاري كامل وموعد preserved capabilities؛ لا يمنع core demo بعد remediation |
| OD-002 | APPROVED | PWA-first نُفذ؛ لا blocker |
| OD-003 | APPROVED | الحسابات والربط وDemo OTP منفذة؛ production OTP ما زال implementation gate لا قرارًا مفتوحًا |
| OD-004 | APPROVED | الأنواع والتواريخ/early renewal منفذة |
| OD-005 | APPROVED | online-first/internal demo/renew-other منفذة؛ real provider onboarding مستقل |
| OD-006 | PENDING but reversible default already implemented | admin-review enrollment يعمل؛ يحجب اعتماد مسؤولية المجموعة نهائيًا، لا العرض الحالي |
| OD-007 | PENDING but reversible default already implemented | calculator default يعمل؛ يحجب اعتماد formula/trends/ranking، ويؤثر MVP completeness |
| OD-008 | PENDING but reversible default already implemented | catalog-only يعمل؛ يحجب commerce/stars/favourites وeligibility policy، لا يحتاجه core demo |
| OD-009 | PENDING but reversible default already implemented | seed/status/editor يعمل؛ يمنع تقديم القيم كحقائق غذائية production |
| OD-010 | PENDING but reversible default already implemented | Owner/Admin metadata/publish يعمل؛ يحجب uploads/responsibility/storage production |
| OD-011 | PENDING and blocks future implementation | يمنع Match/Event fields؛ تدريب فقط في demo |
| OD-012 | PENDING and blocks future implementation | يحجب bot/messages/ranking/users-connections-support وPDF semantics |

ما يمنع الديمو مباشرة ليس OD مفتوحًا بل عيوب التنفيذ P0 المحددة. ما يمنع اكتمال كل baseline: OD-006..012 وOD-001 حسب المجالات. ما يمنع الإنتاج: تنفيذ provider/OTP/media/operations إضافة إلى اعتماد OD-009/010 والسياسات ذات الصلة.

## القدرات المرجعية والمتبقية

| القدرة | المواصفة كافية؟ | تنفيذ موجود؟ | للديمو؟ | للإنتاج؟ | تحتاج قرار مالك؟ |
|---|---|---|---|---|---|
| رسائل فردية | جزئية | لا | لا للعرض الأساسي | حسب المنتج | نعم، OD-001/012 |
| رسائل جماعية | جزئية | لا | لا | حسب المنتج | نعم |
| مرفقات الرسائل | غير كافية للأمان/التخزين | لا | لا | فقط إذا اعتُمدت الرسائل | نعم |
| Bot/Guardian follow-up | غير كافية للprovider/linking | لا | لا | حسب المنتج | نعم، OD-012 |
| Ranking | غير كافية للصيغة/الخصوصية | لا | لا | حسب المنتج | نعم، OD-007/012 |
| Contacts/Connections/Support | أسماء فقط | لا؛ staff API ليس هذه القدرة | لا | الدعم التشغيلي مطلوب لكن ticketing ليس مفروضًا | نعم |
| Refunds/chargebacks | غير محددة | لا | لا | نعم لأي تشغيل مالي حقيقي | نعم + provider policy |
| مزود دفع حقيقي | الاتجاه online معتمد، تفاصيل provider غير منفذة | لا | لا؛ gateway التجريبي يكفي إذا وُسم | نعم | onboarding/قرار تنفيذ |
| Apple Pay | مطلب مستقبلي بلا تفاصيل | لا | لا | حسب قناة الدفع | نعم + onboarding |
| Production media storage | الحدود الأمنية معروفة | لا؛ demo assets/metadata فقط | لا | نعم للصور الحقيقية | نعم OD-010 + تنفيذ |
| OTP/SMS حقيقي | المبدأ معتمد OD-003 | لا | لا؛ fixed OTP يكفي للديمو | نعم | اختيار/تعاقد provider |
| Production deployment | topology موثق فقط | لا | لا | نعم | authority وتشغيل Production Gate |

## تدقيق الأمان

| المجال | الدليل | النتيجة |
|---|---|---|
| Tenant IDOR | TenantAuthentication، Structure، Reporting، Content، Evaluation tests | لا HIGH/MEDIUM مفتوح ضمن demo scope |
| Guardian-child IDOR | guardian child/profile/attendance/evaluation/medical/media tests | سليم |
| Coach group scope | unassigned session/player/evaluation tests | سليم |
| CSRF | antiforgery filter على mutations + E2E عبر token | سليم؛ لا test شامل لكل endpoint لكن pattern مركزي |
| Callback forgery/replay | signing key guard، provider event id، amount/currency/current-payment checks | سليم للـInternal Test Gateway فقط |
| Amount tampering | client amount ignored وwrong callback rejected | سليم |
| Adjustment/discount auth | Guardian/Coach/cross-tenant/concurrency tests | سليم |
| Medical/media leakage | draft/staff notes/arbitrary reference/tenant tests | سليم |
| CSV injection | `Csv_formula_injection_is_escaped` | سليم |
| Test gateway guard | `Internal_gateway_is_refused_outside_demo_test` | سليم |
| Fixed OTP guard | `Fixed_demo_otp_is_refused_outside_Demo` | سليم |

لا توجد finding عالية أو متوسطة مثبتة داخل بيئة الديمو. مخاطر الإنتاج المفتوحة: لا rate-limit/provider حقيقي لطلب OTP، لا secrets/HTTPS/monitoring/backup/restore proof، ولا private object storage؛ لذلك لا تُفسر النتيجة كـsecurity approval للإنتاج.

## بيئة الديمو وقاعدة البيانات

- seed حتمي وidempotent؛ الحسابات والأسماء والصور/السجل الطبي synthetic، وAcademy B موجودة للاختبار السلبي.
- fixed OTP وInternal Test Gateway يوقفان startup خارج Demo/Testing.
- `Demo__ReferenceDate=2026-09-28` يقود subscription clock؛ بعض تواريخ المحتوى الوصفي ثابتة لكنها لا تُحسب expiry metrics.
- تعليمات التشغيل الحالية في `README.md` صحيحة للهدف الرسمي PostgreSQL 17/Docker، وآخر migration `20260929015730_Slice8CSubscriptionDiscounts`.
- المراجعة المحلية استخدمت PostgreSQL 16 مؤقتًا على schemas إضافية معزولة ونجحت migrations/tests؛ **هذا لا يستبدل** دليل CI الرسمي على PostgreSQL 17 ولا يغير architecture.
- لا reset/destructive volume/database operation نُفذ. أنشئت schemas additive للتدقيق فقط، ولم تُعدّل DB الديمو القائمة.

## جرد الاختبارات والتحقق المحلي

| الفئة | العدد | الوحدات المغطاة |
|---|---:|---|
| Integration | 293 | Tenant/Auth 12، Structure/People 18، Dashboard 7، Payments 18، External renewal 17، Attendance 27، Evaluation 23، Guardian 28، Content 31، Reporting 32، Adjustments 45، Discounts 31، Readiness 2 |
| Unit | 1 | live health |
| Architecture | 1 | dependency direction |
| Backend الإجمالي | 295 | 293 + 1 + 1 |
| Frontend | 89 | login/shell، structure، subscriptions، guardian، evaluations، content، reports |
| E2E Mobile Chromium | 35 | auth 1، structure 2، dashboard 1، payments 4، attendance 4، evaluation 3، guardian 4، content 4، reports 4، adjustments 4، discounts 4 |
| npm audit | 0 vulnerabilities | `npm audit --omit=dev` |

التحقق المنفذ في هذا التدقيق:

- `dotnet restore --locked-mode`: نجح.
- Release build: نجح، `0 warnings / 0 errors`.
- كل migrations على schema جديدة: نجحت؛ EF: `No changes have been made to the model since the last migration`.
- Backend: `295/295` نجح على PostgreSQL 16 المحلي المؤقت.
- typecheck/lint/Frontend tests/build: نجحت؛ production build ولّد `42/42` static items و76 page routes.
- E2E: `35/35` نجح على schema جديدة عند Pixel 7؛ سُجل تحذير React uncontrolled→controlled متكرر لا يفشل الاختبار.
- Runtime API أظهر EF model warnings عن mapping ثم ignoring لـ`IdentityUserLogin/IdentityUserToken`؛ P2 تنظيمي لا failure وظيفي مثبت.

## CI

الـbase المعتمد `d017bd44db74945fa6c7b9359be0703bb914a76a` له GitHub Actions run `36513895037` ناجح للوظائف `backend`, `frontend`, `e2e` على PostgreSQL 17 حيث ينطبق. نتيجة CI لcommit وثيقة التدقيق تُسجل بعد push؛ لا تعتبر خضراء قبل ظهور run مطابق للـSHA.

## الأحكام

### Demo readiness

**NOT DEMO READY** حاليًا. البيانات والمنطق والأمان المالي قوية، لكن salesperson/Product Owner لا يستطيع المرور بكل Owner/Admin/Coach/Guardian core journeys دون مقابلة نقص معروف: التجديد الإداري المطلوب غير موجود، وAdmin home لا يقدم الإجراءات الثلاثة، كما أن العربية/mobile polish يفشل في checkout/owner dashboard. هذه عوائق محددة وقابلة للمعالجة، وليست دعوة لبدء Communications أو توسيع النطاق.

### Production readiness

**NOT PRODUCTION READY.** يلزم على الأقل: OTP/SMS موثوق وrecovery، provider دفع حقيقي وwebhook signatures/reconciliation/refunds policy، production secret store وHTTPS/deployment، private media storage/scan/retention، backups وrestore drill، monitoring/alerting، operational support/ownership، security gate واختبار production topology.

## فجوات مرتبة

### P0 — يجب قبل عرض أصحاب المصلحة (`2`)

| المتطلب | الأثر | الإجراء التالي | التعقيد |
|---|---|---|---|
| `AC-SUB-004 + AC-IAM-003` | لا يمكن للإداري بدء تجديد لاعب قائم، ورئيسيته لا تعكس إجراءات التسجيل/التجديد/الحضور الثلاثة؛ رحلة رئيسية في baseline ناقصة | إضافة admin renewal creation على enrollment قائم مع plan/period/amount/pending-payment semantics وquick actions واختبارات idempotency/authorization | Medium |
| `AC-GOV-005 + AC-UX-001/003 + AT-002` | checkout/renewal/plans تعرض English enums/EGP وعنوانًا إنجليزيًا، ولوحة المالك overflow عند 390px؛ العرض العربي الموبايل ينكسر بصريًا | طبقة labels/currency/date موحدة، كسر refs الطويلة، mobile regression عند 390×844، وتعريب checkout/404 | Small |

### P1 — قبل customer pilot (`7`)

| المتطلب | الأثر | الإجراء التالي | التعقيد |
|---|---|---|---|
| `AC-PLY-006` | تسجيل الإداري لا يشمل plan/start/collection | وصل التسجيل الآمن بمسار الاشتراك/الدفع المعتمد دون خلط player creation بالتحصيل | Medium |
| `AC-ATT-006 + AT-018` | CSV يعمل لكنه لا يحقق XLSX المطلوب | توليد XLSX حقيقي بنفس filters/tenant guards واسم/MIME صحيح | Medium |
| `AC-GOV-002/AC-ORG-001/AC-IAM-005` | branding/settings غير مكتملة | شاشة إعداد academy محدودة للشعار/التواصل/اللون مع media policy | Medium |
| `AC-PAR-006/008` | رأس سياق الرياضة والمباريات ناقصان | إصلاح header/selector؛ لا تبنِ Match قبل OD-011 | Small للسياق / Medium بعد القرار |
| `AC-DEMO-006/008 + AT-030/032` | لا reset command مقيد ولا E2E واحدة للقصة الأساسية/انقطاع الشبكة | demo reset آمن scoped + end-to-end story + false-success tests | Medium |
| `AC-UX-002` | React controlled warning ومعالجة fetch errors غير متسقة | تثبيت form state وAPI error adapter واختبار network failure | Small |
| `AC-DEL-003` | manifest موجود بلا service worker/offline policy | حسم الحد الأدنى للـPWA وتوثيق/installability test؛ لا تدّع offline | Small |

### P2 — نطاق لاحق (`8`)

| المتطلب | الأثر | الإجراء التالي | التعقيد |
|---|---|---|---|
| `AC-EVA-008/009 وAC-COM-004` | formula/trends/ranking غير معتمدة | اعتماد OD-007 قبل أي UI أو ranking | Medium/Large |
| `AC-SPP-002/004` | commerce/stars/favourites غير محسومة | اعتماد OD-008؛ إبقاء catalog-only حتى ذلك | Large إذا اختير commerce |
| `AC-MED-002/AC-GAL-002/003` | attachments/uploads/storage production غير موجودة | اعتماد OD-010 ثم private media slice | Large |
| `AC-COM-001/002` | bot غير منفذ | specification slice فقط بعد OD-012 | Large |
| `AC-COM-003` | messages/attachments غير منفذة | audience/channel/delivery specification أولًا | Large |
| `AC-COM-005` | contacts/connections/support غير محددة | product discovery؛ لا تستنتج شاشة من الاسم | Medium |
| `AC-IAM-008` | player login اختياري غير منفذ | قرار إدراجه ثم scope/permissions | Medium |
| `AC-DEL-005` | Production Gate كاملة غير منفذة | task مستقل بعد demo closure/provider decisions | Large |

## المهمة التالية الوحيدة الموصى بها

**Slice 8D — Demo Closure: Admin Renewal + Arabic/RTL Mobile Remediation**.

نطاقها المحدود: (1) تنفيذ التجديد الإداري للاعب/التسجيل القائم بنفس invariants الدفع/الذرية/idempotency، (2) جعل Admin home تعرض الإجراءات الثلاثة، (3) تعريب statuses/types/currency/checkout و404، (4) إزالة Owner 390px overflow وصفحة `/` القديمة وتحذير forms، (5) إضافة E2E 390×844 للرحلات الأربع وAT-002/admin renewal. هذه أعلى قيمة لأنها تغلق P0 المثبتة دون بدء Communications أو مزود حقيقي أو Production Gate أو أي OD غير معتمد.

**نقطة توقف:** لم يبدأ Slice 8D ولم يُنفذ أي كود ميزة في هذا الفرع.
