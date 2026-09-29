# Status

تاريخ التحديث: 2026-09-29. الفرع: `codex/11-slice8b-subscription-adjustments`.

## Slice 8B — منفذ فعليًا

- أضيف `SubscriptionAdjustment` tenant-scoped كسجل append-only لأنواع `FreezeStarted`, `FreezeEnded`, `DaysAdded`, `DaysDeducted`, `Cancelled`، مع السبب والمنفذ والتوقيت والنهاية قبل/بعد وربط الاستئناف ببداية التجميد. أضيف `FrozenFromDate` للفترة وmigration أمامية `20260929005032_Slice8BSubscriptionAdjustments`؛ لم تُعدل migrations سابقة.
- Owner/Admin فقط يملكان `SubscriptionAdjustmentManage`. كل mutation يحل tenant من الجلسة، يتحقق من الفترة resource-side، يتطلب CSRF وسببًا و`Idempotency-Key`، ويستخدم transaction وPostgreSQL `xmin` optimistic concurrency. Coach وGuardian ممنوعان من التعديل، وIDOR بين الأكاديميات يعيد NotFound.
- التجميد لفترة `Active` من `Duration/Combined` فقط ولا يغير النهاية. الاستئناف يمدد النهاية بعدد الأيام من بداية التجميد شاملة حتى اليوم السابق للاستئناف ولا يعيد رصيد الحصص. إضافة/خصم الأيام يغير النهاية فقط بحد تقني 1–365 ولا يسمح بتاريخ قبل البداية. `Sessions` بلا مدة يرفض التجميد/الأيام برسائل عربية محددة.
- الإلغاء يخص الفترة المختارة فقط ويترك الفترات السابقة/اللاحقة والرياضات الأخرى. لا يحذف أو يعدل `Collection`, `Receipt`, `Attendance`, `SubscriptionSessionMovement` أو المبلغ المدفوع، ولا ينشئ refund أو proration أو payment reversal.
- الحضور أثناء التجميد يبقى حقيقة مسجلة بلا استهلاك ويعيد التحذير «تم تسجيل الحضور دون خصم: الاشتراك مجمد أو غير مؤهل.». بعد الاستئناف يمكن لحضور لاحق استهلاك الحصة، ولا توجد reconciliation رجعية. التجديد المبكر يستخدم `EndDate` المعدلة، والفترة الملغاة لا تمنع تجديدًا جديدًا وتظل في التاريخ.
- صفحة تفاصيل اشتراك عربية mobile-first تعرض الإجراءات الصالحة فقط، confirmation للتجميد/الخصم/الإلغاء، preview غير موثوق للاستئناف، رابط الإيصال وسجل التعديلات. Guardian يرى Frozen/Cancelled وتاريخًا مبسطًا لطفله المرتبط بلا معرف موظف أو سبب داخلي.
- Demo seed يضيف تاريخ `DaysAdded` حتميًا لفترة مريم القائمة دون عكس مالي. فترات Duration وCombined النشطة الموجودة تغطي رحلات التجميد والاستئناف؛ seed idempotent ومقيد بـDemo.

## الأمن والاتساق

- اختبارات PostgreSQL تغطي صلاحيات Owner/Admin، منع Coach/Guardian، tenant isolation وIDOR، تجاهل الحقول غير الموثوقة، حدود الأيام، السبب، replay، التعارض المتزامن، وثبات المال/الإيصال/الحضور والحركات والفترات اللاحقة.
- `SubscriptionAdjustment` بلا edit/delete endpoint. قيود DB تثبت نوع العملية، إشارة `DaysDelta`، وجود نهايات before/after، رابط `FreezeEnded`، FK مركب academy+period، ومفتاح idempotency الفريد.
- لوحة المالك والتقرير المالي يظلان مبنيين على `Collections` المؤكدة؛ التجميد/الأيام/الإلغاء لا يغير الإيراد. active metric يستبعد Frozen وCancelled والمنتهي/منفد الحصص.
- `OD-001` ما زال **PENDING / NOT APPROVED**. defaults أعلاه محصورة في Slice 8B ولا تعتمد الخصومات أو demo cut الكامل.

## دليل التحقق

- migration طُبقت فعليًا على PostgreSQL schema نظيفة ومعزولة، وEF pending-model check أكد عدم وجود model changes غير مهاجرة.
- اختبارات Slice 8B PostgreSQL: **45/45** ناجحة، وتشمل التزامن والأمن والتكامل المالي/الحضور/التجديد.
- Backend كامل: **264/264** (`262 integration + 1 unit + 1 architecture`)؛ Release build ناجح بـ**0 warnings / 0 errors**.
- Frontend: **79/79** UI tests، مع نجاح typecheck وlint وproduction build لـ**42 صفحة**، و`npm audit --omit=dev` = **0 vulnerabilities**.
- Mobile Chromium E2E: **31/31** من schema PostgreSQL نظيفة، منها أربع رحلات Slice 8B الفعلية. نتيجة GitHub Actions تُسجل بعد الدفع ولا تُفترض مسبقًا.

## مؤجل صراحة

Discounts، Refunds، payment reversals/chargebacks، Communications/messages، Bot، Ranking، SMS/provider/Apple Pay حقيقي، PDF، production deployment، وSlice 9. لا توجد دلالة refund من الإلغاء.

## نقطة التوقف

تتوقف المهمة عند Slice 8B بعد commit/push ونجاح CI. لا يُدمج أو يُعدل `main` ولا يبدأ Slice 9.
