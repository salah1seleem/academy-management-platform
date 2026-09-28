# Status

تاريخ التحديث: 2026-09-28. الفرع: `codex/05b-slice3-renew-for-another`.

## Slice 3 remediation — منفذ فعليًا

- أصبحت إجراءات ولي الأمر الثلاثة ظاهرة ومنفصلة: «اشتراك جديد» موسوم كلاحق، «تجديد الاشتراك»، و«تجديد اشتراك لغيره» برحلة عربية RTL وموبايل أولًا من إدخال الكود حتى الإيصال.
- `BeneficiaryRenewalReference` opaque وعالي العشوائية، tenant-scoped لتسجيل رياضي واحد؛ لا يخزن النص الخام بل SHA-256 وتلميحًا، ويدعم expiry/revocation/regeneration. قائمة الإدارة تعرض masked hint، والتوليد يعرض الكود الكامل مرة واحدة.
- resolution لا يعرض إلا اسم اللاعب للعرض، الرياضة، الأكاديمية والباقات المتاحة. لا يوجد بحث عام، ولا player/enrollment IDs أو هاتف/ولي أمر/حضور/تقييم/طب/صور.
- `RenewalRequest.RequestedByUserId` هو المسدّد، بينما `SportEnrollmentId` هو المستفيد. السعر والعملة والنطاق الرياضي مشتقة server-side. الدفع للغير لا ينشئ `GuardianPlayerLink` ولا يمنح profile access، لكنه يسمح للمسدّد برؤية هذه المعاملة وإيصالها.
- `Pending` وحدها تقبل نتيجة provider. `Confirmed`, `Failed`, `Cancelled`, `Expired` نهائية للطلب نفسه. نجاح متأخر بعد حالة نهائية يُسجل بلا Collection/Receipt/Period؛ retry ينشئ RenewalRequest وPaymentRequest جديدين ولا يعيد إحياء القديم.
- `Idempotency-Key` يمنع double-click، وprovider event uniqueness والقيود المالية والـserializable transaction تمنع تكرار Collection/Receipt/SubscriptionPeriod.
- Demo seed حتمي يضيف مستفيدًا غير مرتبط بالمسدّد في أكاديمية النجوم وكودًا لأكاديمية المستقبل لاختبارات العزل، ويبقى idempotent ومحصورًا في Demo.
- migration forward-only: `SecureBeneficiaryRenewalReference`؛ تضيف `BeneficiaryRenewalReferences` فقط بعلاقات tenant-aware وفهارس hash/expiry/revocation.

## دليل التحقق

- طُبقت migration محليًا على PostgreSQL، و`has-pending-model-changes` أكد تطابق النموذج.
- Release build: ناجح، 0 warnings / 0 errors.
- backend: 76/76 إجمالًا (`74 integration + 1 unit + 1 architecture`)؛ مجموعة remediation الجديدة 17/17 على PostgreSQL.
- frontend: typecheck وlint وproduction build ناجحة؛ 10/10 اختبارات UI.
- Mobile Chromium E2E: 8/8، ومنها رحلة كاملة لتجديد الغير وإثبات أن المستفيد لا يظهر كطفل للمسدّد.
- `npm audit`: صفر vulnerabilities. نتيجة remote CI تُسجل بعد الدفع ولا تُفترض مسبقًا.

## غير منفذ

لا Attendance/Slice 4 أو استهلاك حصص أو Evaluations أو Nutrition أو Sport Products أو Medical أو Gallery أو advanced Reports. لا provider دفع حقيقي أو Cash/InstaPay/Vodafone/Apple Pay، ولا خصومات/تجميد/تعديل أيام/refund، ولا SMS أو نشر أو اختبار Production. `main` لم يُمس.

## نقطة التوقف

تتوقف المهمة عند Slice 3 remediation. لا يبدأ Slice 4 دون تفويض مستقل.
