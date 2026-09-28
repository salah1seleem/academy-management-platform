# Status

تاريخ التحديث: 2026-09-29. الفرع: `codex/08-slice6-guardian-core`.

## Slice 6 — منفذ فعليًا

- أصبحت واجهة ولي الأمر عربية RTL وموبايل أولًا: ترحيب وهوية الأكاديمية، وثلاثة إجراءات ثابتة هي «اشتراك جديد»، «تجديد الاشتراك»، و«تجديد اشتراك لغيره». أُعيد استخدام مساري التجديد والدفع الموجودين بلا تكرار لمنطق التحصيل.
- الصفحة الرئيسية تجمع تسجيلات الرياضات في بطاقة واحدة لكل طفل، وتعرض سياق الرياضات مرة واحدة عبر الأطفال، مع حالة الاشتراك الفعلية: فعال، ينتهي قريبًا وفق نافذة الخادم الحالية (7 أيام)، منتهي، أو غير مفعل.
- ملف الطفل يجمع التقييمات المنشورة فقط، جدول المجموعة والجلسات القادمة، سجل وملخص الحضور من السجلات المخزنة، والاشتراكات وروابط التجديد. لا تظهر Draft ولا بيانات طفل غير مرتبط.
- أضيف `NewEnrollmentRequest` بحالات `Pending → UnderReview → Approved/Rejected` مع `Cancelled` محفوظة للمستقبل، وبيانات طفل قائم أو طفل جديد، ورياضة وفرع مفضل، وملاحظات منفصلة داخلية/مرئية، وروابط اللاعب والتسجيل الناتجين، والمراجع الزمنية والمراجع التدقيقية و`Version`.
- ولي الأمر يرسل طلب إضافة رياضة لطفل مرتبط أو طلب طفل جديد بحد أدنى من البيانات، من دون اختيار مجموعة. الإنشاء لا ينشئ لاعبًا أو رابط وصاية أو تسجيلًا رياضيًا أو اشتراكًا أو تحصيلًا أو دفعًا.
- منطقة «اللاعبون ← طلبات الاشتراك الجديدة» توفر البحث وفلاتر الحالة/الرياضة/الفرع/الفترة، بدء المراجعة، الرفض بسبب ظاهر منفصل، والاعتماد بعد اختيار مجموعة مطابقة. في طلب الطفل الجديد تظهر المطابقات الدقيقة للاسم وتاريخ الميلاد فقط، ولا يعاد استخدام لاعب إلا باختيار إداري صريح.
- الاعتماد يعمل داخل معاملة `Serializable`: يتحقق من tenant والرياضة والفرع والمجموعة والرابط، ينشئ أو يعيد استخدام اللاعب صراحة، ينشئ `GuardianPlayerLink` صريحًا و`SportEnrollment` واحدًا، ثم يربط نتيجة الطلب؛ ولا ينشئ أي أثر مالي.
- أضيفت صلاحيات `EnrollmentRequestRead`, `EnrollmentRequestManage`, و`GuardianEnrollmentRequestCreate`. Owner/Admin يديران الطلبات، Guardian ينشئ ويرى طلباته وموارد أطفاله المرتبطين فقط، وCoach محجوب عن الإدارة.
- migration forward-only: `20260928221209_Slice6GuardianCore`، وتضيف جدول `NewEnrollmentRequests` فقط. لم تُعدّل migrations السابقة ولم تُنشأ جداول Nutrition/Products/Medical/Gallery/Matches/Messages.
- بيانات Demo حتمية ومحددة النطاق تشمل Pending وApproved وRejected وطلب طفل جديد، وطلبًا مستقلًا لأكاديمية ثانية. إعادة seed idempotent ولا تمس بيانات Production.

## مراجعة الأمن والاتساق

- `AcademyId` لا يؤخذ من العميل؛ tenant من الجلسة وتُثبت العلاقات المركبة اتساق Player/Sport/Branch/Enrollment. كل قراءة طفل تتطلب `GuardianPlayerLink` نشطًا، وكل قراءة طلب Guardian تقيد بالمستخدم الحالي.
- تم اختبار child/request IDOR، عزل Academy B، تبديل player/sport/branch/group، منع Coach، فصل التعليق الداخلي عن سبب الرفض، وعدم استنتاج الوصاية من الهاتف.
- مفتاح idempotency فريد لكل academy/guardian، والطلبات القائمة والتسجيل النشط محميان منطقيًا، وإعادة الاعتماد لا تنشئ تسجيلًا ثانيًا. DTOs محددة تمنع overposting، والاعتماد المتكرر آمن.
- نموذج Player منفصل عن SportEnrollment؛ الطفل متعدد الرياضات لا يتكرر، وطلب الطفل القائم لا يكرر Player. Nutrition وSport Products وMedical وGallery وCommunications وAI Reports لم تبدأ.
- `OD-006` ما زال **PENDING / NOT APPROVED**؛ الافتراضي القابل للعكس هو اختيار الطفل/الرياضة/الفرع ثم تعيين الإدارة للمجموعة. `OD-011` ما زال **PENDING / NOT APPROVED**؛ المعروض تدريب وجلسات فقط بلا Match entity.

## دليل التحقق الحالي

- `dotnet restore` وRelease build ناجحان: 0 warnings / 0 errors. migration مطبقة على PostgreSQL 17، وEF pending-model check أكد عدم وجود تغييرات غير مهاجرة.
- Backend كامل: **156/156** (`154 integration + 1 unit + 1 architecture`)؛ منها **28/28** اختبار PostgreSQL مخصصًا لـSlice 6، مع بقاء auth/tenancy/payments/attendance/evaluations خضراء.
- Frontend: typecheck وlint وproduction build (30 صفحة) ناجحة؛ **34/34** UI tests، و`npm audit` = 0 vulnerabilities.
- Mobile Chromium E2E: **19/19** تشمل كل الرحلات السابقة وأربع رحلات Guardian/Admin حقيقية لـSlice 6. نتيجة GitHub Actions تسجل بعد الدفع ولا تفترض مسبقًا.

## مؤجل صراحة

Nutrition، Sport Products، Medical، Gallery/media، Slice 7، communications/messages، Match entity، مزود دفع أو SMS حقيقي، Apple Pay، advanced reports، والنشر الإنتاجي. لا production-readiness claim.

## نقطة التوقف

تتوقف المهمة عند Slice 6. لا يبدأ Slice 7، و`main` لا يُدمج أو يُعدل ضمن هذه المهمة.
