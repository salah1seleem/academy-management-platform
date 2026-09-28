# Status

تاريخ التحديث: 2026-09-28. الفرع: `codex/04-slice2-structure-people`.

## Slice 2 — منفذ فعليًا

- هيكل الأكاديمية: `Branch`, `Sport`, `AgeCategory`, `TrainingGroup`, `RecurringSchedule`, `StaffGroupAssignment`.
- الأشخاص: `GuardianProfile`, `Player`, `GuardianPlayerLink` الصريح. العمر مشتق من `DateOfBirth` ولا ينشأ وصول من الهاتف/الاسم.
- `SportEnrollment` يفصل هوية اللاعب عن سياق الرياضة/الفرع/المجموعة. الفئة مشتقة من المجموعة، والتحقق يمنع Group/Sport/Branch mismatch.
- كل entity تحمل `AcademyId`، والعلاقات الرئيسية تستخدم composite alternate/foreign keys `(AcademyId, Id)` لمنع cross-tenant references في PostgreSQL، مع فحص `CurrentTenant` والسياسات عند API.
- registration endpoint ذري: ينشئ أو يعيد استخدام Player/Guardian داخل الأكاديمية، ثم link وenrollment في transaction؛ لا Subscription أو Payment.
- بحث staff بالاسم/الكود/هاتف ولي الأمر وفلاتر branch/sport/category/group، scoped بالأكاديمية. Guardian endpoint يعرض linked children فقط، وCoach endpoint يعرض assigned groups فقط.
- Dashboard عربي RTL responsive مع sidebar Module → Sub-module، list-first، PageHeader/Create، search/filters، row actions وحالات empty/error، ونموذج تسجيل mobile-friendly dependent selects.
- migrations forward-only: `StructurePeopleEnrollment` ثم `EnrollmentGroupContextInvariant`. الجداول الجديدة: `Branches`, `Sports`, `AgeCategories`, `TrainingGroups`, `RecurringSchedules`, `StaffGroupAssignments`, `GuardianProfiles`, `Players`, `GuardianPlayerLinks`, `SportEnrollments`. الثانية لا تنشئ جدولًا؛ بل تفرض في قاعدة البيانات تطابق Group/Branch/Sport داخل التسجيل.
- Demo seed idempotent: ثلاثة فروع/رياضات/فئات، ثلاث مجموعات وجدول أسبوعي، sibling family، طفل برياضتين، طفلان بنفس الرياضة، اسم عربي مشابه في أسرة أخرى، فرع مختلف، وبيانات Academy B معزولة.

## دليل التحقق

- Release build: ناجح، 0 warnings / 0 errors.
- migration طُبقت على PostgreSQL 17 محليًا، وفحصها أكد عدم وجود Subscription/Attendance/Evaluation/Nutrition/Product/Medical/Gallery tables.
- integration tests: 32/32 (منها 18 اختبار Slice 2 تغطي البنود الـ16 المطلوبة وإعادة استخدام اللاعب/ولي الأمر عند إضافة رياضة، مع فصل Coach وGuardian authorization).
- architecture/unit tests: 2/2؛ الإجمالي المحلي للـbackend هو 34/34.
- frontend typecheck/lint: ناجحان؛ frontend tests 2/2.
- production build: ناجح؛ routes الخاصة بالـdashboard/registration/guardian بُنيت.
- mobile E2E: 3/3 على Mobile Chromium، ويشمل إنشاء Player/Guardian/Enrollment حقيقي ثم ظهوره في القائمة، ورؤية ولي الأمر للطفلين المرتبطين فقط.
- `npm audit`: صفر vulnerabilities. Remote CI ينتظر دفع الفرع، لذلك لا تسجل هذه الوثيقة نتيجة غير منفذة مسبقًا.

## مراجعة الأمان المركزة

- tenant/IDOR: لا DTO كتابة يقبل `AcademyId`; كل lookup يضيف trusted tenant. composite FKs ترفض cross-tenant Branch/Sport/Group/Player/Guardian/Coach relationships.
- guardian/player: phone lookup لا يمنح وصولًا؛ `GuardianPlayerLink` unique وصريح، وقراءة الطفل تعيد `404` خارج روابط المستخدم.
- coach scope: assignment يتطلب Coach membership وGroup داخل الأكاديمية نفسها؛ القراءة عبر assignment للمستخدم الحالي.
- search: endpoint محصور Owner/Admin، والاستعلام والـguardian-phone subquery scoped بالأكاديمية؛ Guardian لا يملك staff search policy.
- mass assignment/transaction: request models allowlist الحقول؛ المجموعة هي مصدر branch/sport/category الموثوق، ويؤكد composite FK الرباعي ذلك في PostgreSQL. تبدأ transaction قبل إنشاء أي person/link/enrollment وتُلغى عند أي failure. إعادة استخدام Player/Guardian مرتبطين تضيف SportEnrollment فقط ولا تكرر اللاعب أو الرابط.
- archive/deactivate هو النمط؛ لا hard-delete endpoints أضيفت.

## غير منفذ

لا SubscriptionPeriod أو plans/payment/collection، ولا TrainingSession فعلية أو Attendance، ولا Evaluation أو Nutrition/Product/Medical/Gallery/Reports. لا Parent Experience كاملة، ولا coach evaluations، ولا production SMS/media/deployment/tests. approved requirements لم تتغير و`main` لم يُمس.

## نقطة التوقف

Slice 2 فقط؛ Slice 3 لا يبدأ دون تفويض جديد.
