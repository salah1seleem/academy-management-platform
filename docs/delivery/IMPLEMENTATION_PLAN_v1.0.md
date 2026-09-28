# Implementation Plan v1.0

الخطة تحفظ كامل النطاق المعتمد وتفصل عنه demo cut مقترحًا. هدف ثلاثة أيام هدف عرض، لا ضمان لإكمال 117 مطلبًا أو جاهزية إنتاج.

| Slice | تسليم رأسي محدود | الاعتماد/التحقق وبيانات الديمو | نقطة التوقف |
|---|---|---|---|
| 0 — Runnable foundation | solution/API health، اتصال PostgreSQL، migration strategy، Next.js Arabic RTL role-shell، test harness، Compose محلي | smoke API+DB، unit/integration skeleton، accessibility/RTL smoke؛ بلا business seed | health/readiness ناجحان واختبارات CI الأساسية تعمل |
| 1 — Tenant + auth | Academy/Identity membership، ASP.NET Identity session، staff invitation، guardian activation contract، server policies | أكاديميتان وحسابات أربعة أدوار؛ AT-001/002/003 وDM-10؛ اختبارات تبديل IDs والجلسة/CSRF | لا business module قبل اجتياز isolation |
| 2 — Structure + people | branches/sports/categories/groups/schedule، Player/GuardianLink/SportEnrollment، admin registration/search | siblings، طفل برياضتين، duplicate-name؛ AT-004/005/006/010/015 وDM-02/03/04 | لا subscription قبل ثبات عدم تكرار Player |
| 3 — Subscription + online test payment | **منفذ:** plans/periods، guardian renewal، Internal Test Gateway، signed provider events، collection/receipt ذرية، idempotency، expiring/expired والقوائم الإدارية. **مؤجل:** renewal-other والخصومات/التجميد وprovider الحقيقي. | active/expiring/expired، double click/replay، failed/cancelled، tenant/guardian IDOR؛ AT-011..014/028 وDM-01/05؛ OD-004/005 معتمدان | تحقق مالي وأمني ثم توقف قبل Attendance |
| 4 — Sessions + attendance | actual sessions، trainee/staff attendance، NotRecorded، session balance effect؛ export لاحق حسب cut | شهر حضور وstaff؛ AT-016/017، وAT-018 عند export، DM-07 | uniqueness وتصحيح الرصيد يمران ذريًا |
| 5 — Evaluations | sport criteria 0–100، draft/publish، football report order، missing values، non-football fallback | draft/published و0/100؛ AT-019..023 وDM-06؛ قرار OD-007 | لا formula production قبل الموافقة |
| 6 — Guardian core | child cards، sport dedupe، actions الثلاثة، profile/schedule/renewal request | رحلات DM-01..05؛ AT-007/008/009/032؛ Arabic mobile e2e | PWA demo journey مكتملة بلا ادعاء native |
| 7 — Content | sport catalog display، seeded nutrition info، medical/gallery read boundaries | 18 موضع/15 مادة، حالة طبية فارغة، media رمزية؛ AT-024..027/033 وDM-08/11؛ OD-008..010 | فحص آلي أن Nutrition بلا commerce fields/endpoints |
| 8 — Reports + preserved capabilities | stored metrics، financial/attendance reports؛ ثم slices مستقلة للتجميد/خصم/إلغاء/تصدير/طباعة/bot/messages/ranking بعد اعتماد cut | AT-018/028/029 وDM-09/12؛ لا hardcoded totals | كل قدرة غير مدرجة تبقى موثقة لا زرًا وهميًا |
| 9 — Production gate | HTTPS/secrets، durable media، backup/restore، monitoring، retention/support، security and isolation regression | AT-034 وrestore drill موثق | owner go/no-go؛ demo success لا يكفي |

## قواعد كل slice

العقد والـ UI والـ persistence والاختبارات تُسلّم معًا. seed عربي مصري deterministic يستخدم clock محقونًا، upsert بمعرفات معروفة وAcademy marker، ويعمل فقط في Demo؛ reset scoped ولا ينفذ في Production. metrics من records. لكل slice قائمة "نفذ/لم ينفذ" وتوقف قبل التالي للمراجعة. لا background worker إلا مع side effect موثوق، ولا package أو خدمة بلا تحقق إصدار.

## المهمة التالية الدقيقة — تنتظر التفويض

تنفيذ **Slice 0 فقط**: إنشاء ASP.NET Core API health/readiness، اتصال PostgreSQL عبر EF Core مع migration strategy وأول test connectivity بلا domain schema، Next.js Arabic RTL role-shell فارغ للأدوار، Docker Compose محلي، وunit/integration/e2e harness بسيط. لا auth كامل، لا entities بيزنس، لا seed قصص، لا شاشات وظائف، لا deployment. بعدها مراجعة الأدلة والتوقف؛ المهمة التالية وحدها تكون minimal secure tenant/auth path.
