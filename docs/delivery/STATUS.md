# Status

تاريخ التحديث: 2026-09-29. الفرع: `codex/12-slice8c-discounts`.

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
