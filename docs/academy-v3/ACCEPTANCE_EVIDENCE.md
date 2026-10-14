# أدلة قبول V2 + V3

## B3a — نطاق الرياضة التشغيلي، 14 أكتوبر 2026

Code SHA `70f217fa57e58844b58a7dad35f02aa956fee9e4` مرفوع. CI [38609005372](https://github.com/salah1seleem/academy-management-platform/actions/runs/38609005372) **in_progress** عند الفحص، ولا تعادل النتائج المحلية نجاح CI. بعد مراجعة نطاق HR أضيف منع الحضور الوظيفي اليومي المشترك من التفويض الرياضي الضيق وأعيد الاختبار المتأثر **1/1**؛ لم تمس بقية نتائج الاختبارات أو assertions.

نفس قاعدتي الاختبار المعزولتين B2؛ لا reset أو تغيير لقاعدة المراجعة. Migrations `20261014165922_V3MembershipSportScope` و`20261014170814_V3PlanSportScopeLookup` مطبقتان على الاثنتين؛ `has-pending-model-changes --no-build` نجح.

الأمر النهائي المستهدف `dotnet test tests/integration/Academy.IntegrationTests --filter 'FullyQualifiedName~V2StaffScopeTests|FullyQualifiedName~V3_mixed_sport_family|FullyQualifiedName~Family_request_and_proof_require_every_branch|FullyQualifiedName~Branch_admin_cannot_publish_outside_scope|FullyQualifiedName~IMP_06_branch_admin_can_publish|FullyQualifiedName~Scoped_admin_collects_linked_family_across_branches' --no-restore` باستخدام اتصال قاعدة V3: **18/18 في61s**. يشمل 8 اختبارات V3 جديدة: نطاق/IDOR/CSV/كتابة صحيحة ومرفوضة، مدخلات أجنبية وتوافق عميل قديم، منع المجالات المشتركة وworker، كائن tracked وهوية طفل متعدد الرياضات، دور ثان لا يوسع الدور المختار وتعارض مدرب عبر الرياضات، سحب scope/action قبل حفظ أمر، seed آمن متكرر، طلب أسرة وإثبات متعدد الرياضات. الستة القديمة وأربعة مستهلكين اجتازوا دون خفض assertions.

مجموعة إضافية محددة للحصص والحجز/إنشاء المجموعة **7/7**، تضم 3 حالات V3 متداخلة أعيدت بعد توسيع التحقق؛ لا تجمع الأرقام كاختبارات فريدة. Web: staff-access / sport-operations-settings / facilities **12/12**؛ typecheck/lint ناجحان. متصفح mobile-chromium: v2-staff-access / v3-staff-templates **2/2 في25.9s** مع production build على5186/3106. تحقق حفظ ثم reload ثم دخول إداري مستقل ثم سحب الرياضة من المالك وانعكاسه في نفس الجلسة. صور فعلية [390](evidence/b3a/staff-scope-390.png) و[1440](evidence/b3a/staff-scope-1440.png)، assertions تمنع overflow. مراجعة لقطة390 الكاملة؛ ليست صور كل أدوار V3.

أول محاولة خادم كشفت دورة EF بين الباقة ونشرها بسبب فلاتر الرياضة؛ أصلحت بـlookup SQL tenant-parameterized، لا تعطيل حماية الأكاديمية. اختبار tracked أولي حاول تغيير مفتاح EF ثابت، فعدل السيناريو لاختبار تغيير حالة كائن مخفي وهوية مشتركة باستخدام عضوية فعلية مفوضة، مع بقاء رفض الكتابة وعدم الحفظ. أخطاء compilation في سيناريوهات الاختبار صُححت قبل النتائج النهائية. لا إخفاء فشل أو وصف تشغيل aborted بأنه ناجح.

القيود: تفويض رياضي ضيق ليس كل وظائف الإدارة أو دورًا مخصصًا متعدد المنح. توجد عمليات مشتركة ممنوعة صراحة حتى تطبيق مساراتها، والمالك والتفويض السابق الواسع لم يتغيرا. لا Full Regression موحد أو Flutter/device جديد أو ledger/F10 أو إعلانات أو بيانات V3 الشاملة. CI اللاحق نتيجة مستقلة؛ التشغيلات السابقة كانت Backend جاريًا ولا توصف خضراء بالكامل.

## خط أساس 2026-10-14

- Starting HEAD: `3d5a60d5026a5b8cd54bcf3f1f43abcf1ea6934e` على `codex/15-native-mobile-app`، شجرة نظيفة ومطابقةorigin. main لم يُغيّر.
- سلامة ZIP: حسابSHA-256 لجميع8 ملفات فيmanifest وطولbytes؛ **8/8** مطابقة. مرجعV2 مطابق لنسخة المستودع. المعرفات:V2-00..17=18، مصفوفةV2=85، V3-AC01..48=48. المرجع الحسابي يحوي29قيمة expected ولم ينفذ على المنتج بعد.
- CI للأساس: [run38570922252](https://github.com/salah1seleem/academy-management-platform/actions/runs/38570922252): backend/frontend/flutter success؛ e2e failure. logs تؤكد timeout دخولowner.football ضمنprofileLegacyRegression؛ النقل إلىtests/e2e/football يبقي نفس الرحلة والتوقعات. لا Passed للإعادة قبل تشغيلها.
- V3-AC01: قراءة ومطابقة المصادر والجرد مثبتة فيPROGRESS؛ لا تعني تنفيذ بقية48حالة.

## تنفيذ B1

B1 commit: `ee102e8844af289d5d7002c21503d99873ef3378`، مرفوع. CI38588910233: frontend/flutter/e2e success؛ backend كانin_progress عند الفحص، لا ادعاءCIكامل.

### B1 — تحقق فعلي على قاعدة academy_v3_dev_20261014

قاعدةPG16 جديدة معزولة، طبقت عليهاmigrationsالقائمة فقط، دونresetأو بيانات عميل. الأوامر: `dotnet test tests/integration/Academy.IntegrationTests/Academy.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~V2StaffScopeTests` **6/6**؛ `npm test -- src/features/staff-access.test.tsx` **4/4**؛ `npm run typecheck` و`npm run lint` وproductionbuild ناجحة. `npx playwright test ... v2-feedback-followup.spec.ts` علىfootballconfig **1/1**؛ `npm test -- v3-staff-templates.spec.ts v2-staff-access.spec.ts` علىports5186/3106 **2/2** معproductionbuild.

الصور390/1440 في`tests/e2e/test-results/v3-staff-templates-Owner-a-2bbbd-nd-reopens-the-saved-grants-mobile-chromium/`؛ روجعت390 فعليًا وفحصoverflowبالاختبار. محاولةBackendالأولى5/6 بسببContainsعلىjsonbفيtest، صُحح بقراءةJSONوالتأكد منTemplateKey، ثم6/6. محاولةtypecheck/buildالأولى كشفتrenderقديمًا unreachable فحذفناه؛ E2Eالأولى0/2 بسبب عنوانين متطابقين وتوقع201لعقدإنشاء200؛ صحح العنوان والتوقع وفقAPIثم2/2. لم تُحذفassertionsحفظ/عزل/أثر. القوالب ليستroleengineV3كاملًا. CIبعدpushبوابة مستقلة.

F10 / الدفتر / الإعلانات / إثبات الجهاز / بيانات3أكاديميات / النهائي الموحد / صورV3 / الأجهزة الحقيقية: **لم تنفذ**. لا تكامل خارجي مهيأ يعلن ناجحًا. نتائجV2 القديمة موثقة فيdocs/delivery/V2_EXECUTION.md وليست نتائجV3.

الإعادة النهائية للمتصفح **2/2 في32.3s** على قاعدة ثانية نظيفة `academy_v3_e2e_20261014` حتى لا تلوث فروع اختبارات التكامل صور العرض. البحث عن فرع والقائمة محدودة الارتفاع؛ أعدناWeb4/4 وtypecheck/lint بعد التغيير. روجعت لقطة390 النهائية. الصور تتضمن شاشةصلاحياتB1فقط؛ بقيةصورV3لمتنفذ.

## B2 — تشغيل سباحة معزول في المرافق

Code commit: `13c500331040684ba6fd3f33f639e79d5f921e4c` مرفوع؛ migrationالوحيدة لهذه الدفعة`20261014152943_V3SportScopedSwimmingPolicies`. النسخ المرئية الدائمة: [360](evidence/b2/sport-settings-360.png)، [390](evidence/b2/sport-settings-390.png)، [430](evidence/b2/sport-settings-430.png)، [Desktop](evidence/b2/sport-settings-1440.png)، [صلاحيات390](evidence/b2/staff-template-390.png)، [صلاحياتDesktop](evidence/b2/staff-template-1440.png). نسخ من لقطات التطبيق الفعلية، لا mockups أو صور مرجع، ولا تستبدل أدلة الصلاحيات.

القاعدة `academy_v3_dev_20261014` للاختبارات و`academy_v3_e2e_20261014` للمتصفح فقط؛ PG16 محلي، لا قاعدة مراجعة/عملاء. migration `20261014152943_V3SportScopedSwimmingPolicies` طبقت على الاثنتين، بما فيها backfill المسبح القديم فيE2E. `dotnet ef migrations has-pending-model-changes ... --no-build` نجح: لا تغييرات معلقة.

Backend: فلتر `V3_swimming|Resource_capacity_requires|Last_capacity_is_atomic|Shared_adjacent_bookings|Coach_conflicts_are_checked|Facility_tenant_branch_role|Day_pass_` **10/10**؛ بعد حراسةseed واختبارها، `V3_swimming|Day_pass_` **6/6**؛ المستهلكون `Resource_timetable_preview|Generated_sessions_reserve|Visit_entry_retry|Visit_demo_seed|Observation_expected6|Observation_demo_seed` **6/6**. ليست أعدادًا تجمع كاختبارات فريدة. تشمل رفضtenant/football/coach/guardian/CSRF، version409، تعطيلAوبقاءB، quoteوعروضالولي بعدالإيقاف، حفظ التاريخ وعدمإنشاءمال، وseedلايعيدالتفعيل.

Web: `npm test -- src/features/sport-operations-settings.test.tsx src/features/facilities.test.tsx src/features/staff-access.test.tsx` **11/11**، typecheck/lint ناجحان. E2E: `npm test -- v3-sport-settings.spec.ts v3-staff-templates.spec.ts` **2/2 في24s** معproductionbuild على5186/3106. الرحلة: رياضةGeneral→Swimming؛ حفظ فرع؛ reload؛ فرع آخرoff؛ إنشاءPoolوحارةواعتماد؛ حجزOpenSwim؛ إيقاف؛ POSTآخر403؛ الحجزالأولباقدونحذف؛ رابطالمرافقيحفظالفرع.

صور360/390/430/1440 تحت `tests/e2e/test-results/v3-sport-settings-Owner-co-24939-y-and-disables-new-bookings-mobile-chromium/`، مع overflow assertions؛ فحص390/1440فعليًا، وإصلاحselectخامفيالواجهةبمقاس48pxوتباعدمقروء، ثم إعادة2/2. محاولةE2Eالأولى تعثرتgetByLabelلـselect؛ الثانيةstatusغيرفريد بسببnoticeعام؛ استُخدمrolecomboboxوstatusخاصبنصالحفظ، ثم1/1و2/2 دونحذفassertions. lint كشفeffectloadوعولجباستدعاءfetchداخلeffectبـAbortController؛ typecheckكشفmockfetchوعولجبـstubGlobal. أمرmigrationالأولافتقدمتغيرconnectionوتصححعلىقاعدةالاختبار؛ لا مشكلةشبكة أو retryبلاسبب.

لا اختبار جهاز/Flutter جديد فيB2 ولا دور رياضة مترابط أو ledger أو3أكاديمياتDemo كاملة؛ هذه فجوات محفوظة. CIالجديد بعدpush مستقل عن النتائج المحلية.
