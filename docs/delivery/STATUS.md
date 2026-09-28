# Status

تاريخ التحديث: 2026-09-29. الفرع: `codex/06-slice4-attendance`.

## Slice 4 — منفذ فعليًا

- أضيفت `TrainingSessions` كوقائع فعلية tenant-scoped بحالات `Scheduled/Held/Cancelled` ومصدر يدوي أو جدول متكرر. التوليد لنطاق أقصاه 31 يومًا، يتجاهل المجموعات/الجداول غير النشطة، ويعيد التشغيل بلا تكرار. الإنشاء اليدوي يتحقق من المجموعة والوقت والتكرار.
- `PlayerAttendances` منفصل عن `StaffAttendances`. الحالات `NotRecorded/Present/Absent` صريحة، والـunique/FKs المركبة تمنع duplicate والحضور خارج المجموعة أو الأكاديمية. الحفظ batch وذري؛ معرف واحد غير صالح يرفض الدفعة كلها.
- `Present` مع `Duration` لا يغير رصيدًا. مع `Sessions/Combined` يختار الخادم الفترة المؤهلة زمنيًا لنفس `SportEnrollment` ويخصم واحدًا. `Absent/NotRecorded` لا يخصمان. الرصيد صفر يحفظ الحضور مع warning، بلا رصيد سالب أو دين/تحصيل.
- التصحيح `Present -> Absent|NotRecorded` يعيد فقط الخصم المرتبط بسجل الحضور. `SubscriptionSessionMovements` append-only يوثق before/after والمنفذ والسبب، ويربط restoration بـconsume الأصلية. العودة إلى حاضر تنشئ دورة audit جديدة.
- transaction وPostgreSQL advisory transaction lock مع unique constraints يمنعان double-click/retry وتحديثين متزامنين من الخصم مرتين. `RemainingSessions` و`SubscriptionPeriodId` لا يأتيان من العميل.
- المدرب يرى جلسات مجموعاته فقط ويسجل حضور اللاعبين وحضوره الشخصي؛ لا يصل لمجموعة أخرى أو Collections/Payments. Owner/Admin يديران كل جلسات الأكاديمية. ولي الأمر يرى `Present/Absent` لطفله المرتبط فقط، بلا ملاحظات داخلية أو حضور جهاز فني.
- واجهة عربية RTL وموبايل أولًا تضيف وحدة «الحضور» بثلاثة sub-modules لتقليل التكرار: «جلسات التدريب» (ومنها حضور اللاعبين)، «حضور المدربين»، و«سجل الحضور». توجد list-first، بحث بالاسم/كود اللاعب، أزرار لمس، batch save، إنشاء يدوي، توليد، إنهاء/إلغاء آمن، وfeedback/loading/error/empty. لا QR/export/SMS وهمية.
- API: `GET/POST /api/v1/attendance/sessions`, `POST /sessions/generate`, `PUT /sessions/{id}/status`, player/staff roster + batch save، `GET /attendance/history`، و`GET /guardian/children/{playerId}/attendance`.
- migration forward-only: `20260928205223_Slice4TrainingSessionsAttendance`؛ تضيف الجداول الأربعة وقيود التطابق والتفرد والرصيد غير السالب. لم تتغير migrations السابقة.
- Demo ثابت على `2026-09-28`: حصة أمس `Held` مع حاضر/غائب ومدرب حاضر، حصة اليوم وغدًا، حصة ملغاة، سباحة اليوم، وحصة للأكاديمية الثانية. «عمر أحمد حسن» له 5 حصص، «عمر أحمد محمود» Duration، والسباحة Combined. إعادة seed idempotent ومقصورة على `Demo`.

## مراجعة أمن الحالة والمحاسبة

- IDOR وAcademyId injection: كل query/relationship يحمل Academy من الخادم؛ الموارد غير المخولة تعيد 404/403.
- group/session/enrollment tampering: composite FKs والتحقق الخدمي يثبتان المجموعة؛ المدرب مقيد بـ`StaffGroupAssignment`.
- duplicate/concurrency/negative balance: unique indexes + transaction lock + check constraint؛ اختبار طلبي `Present` متزامنين أثبت خصمًا واحدًا.
- restoration abuse: لا restore دون consume غير معكوس لنفس attendance/period، وفهرس `ReversesMovementId` يمنع العكس مرتين.
- لا يقبل API رصيدًا جديدًا أو period من العميل، ولا يسمح بحضور حصة ملغاة. ولي الأمر لا يملك mutation ولا يرى لاعبًا غير مرتبط أو staff/internal notes.

## دليل التحقق

- migration طُبقت على PostgreSQL 17، و`has-pending-model-changes` أكد تطابق النموذج.
- اختبارات Slice 4 الحرجة: 27/27 PostgreSQL، وتشمل concurrency الحقيقي، التصحيح، zero balance، العزل والصلاحيات والـaudit.
- Release build: ناجح، 0 warnings / 0 errors. backend: 103/103 (`101 integration + 1 unit + 1 architecture`)، واختبارات Slice 3 وrenew-for-another بقيت خضراء.
- frontend: typecheck وlint وproduction build ناجحة، وUI tests هي 10/10. Mobile Chromium E2E هي 12/12، منها أربع رحلات حضور حقيقية للإداري/المدرب/ولي الأمر. `npm audit`: صفر vulnerabilities.
- نتيجة remote CI تُسجل بعد الدفع ولا تُفترض مسبقًا.

## مؤجل صراحة

Evaluations/Slice 5، Nutrition، Sport Products، Medical، Gallery، advanced Reports وتصدير الحضور، rescheduling متقدم، QR، provider دفع/SMS حقيقيان، freeze/day adjustments/refunds، والنشر الإنتاجي. لا production-readiness claim.

## نقطة التوقف

تتوقف المهمة عند Slice 4. لا يبدأ Slice 5 دون تفويض مستقل، و`main` لا يُدمج أو يُعدل ضمن هذه المهمة.
