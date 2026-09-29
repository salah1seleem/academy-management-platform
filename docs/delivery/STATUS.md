# Status

تاريخ التحديث: 2026-09-29. الفرع: `codex/14-slice8d-demo-closure`، والأساس المعتمد: `9fc885d9c8a10e4832443b060c1ba227de92e099`.

## Slice 8D — Demo Closure منفذ فعليًا

- أصبح Owner/Admin قادرًا على البحث عن لاعب قائم واختيار `SportEnrollment` قائم وباقة متوافقة ثم إنشاء `RenewalRequest` و`PaymentRequest` فقط. الرحلة لا تنشئ Player أو Guardian link أو Enrollment جديدًا، ولا تنشئ Collection/Receipt/Period قبل نجاح الدفع.
- جُمعت قواعد الإنشاء في `RenewalCreationService` المشترك بين Guardian وAdmin، مع تحقق tenant/sport، وsnapshot مالي، و`Idempotency-Key`، وقفل PostgreSQL transaction-scoped يمنع طلبين مفتوحين متنافسين للتسجيل نفسه. نجاح `InternalTestPaymentGateway` يعيد استخدام معالج الدفع القائم لإنشاء Collection وReceipt وSubscriptionPeriod ذريًا.
- أضيفت مداخل التجديد من الاشتراكات الحالية وتفاصيل اللاعب/الفترة والإجراء السريع، وصفحات اختيار/مراجعة ودفع تجريبي للإداري. `AcademyOwner` و`AcademyAdmin` فقط؛ Coach وGuardian ممنوعان، والبحث/التفاصيل tenant-scoped.
- خصومات Slice 8C متوافقة: ينشئ الإداري الطلب أولًا، ثم يطبق الخصم من شاشة الطلب؛ يُستبدل PaymentRequest المعلق ويظل `FinalAmount` هو مبلغ الدفع والتحصيل والإيصال والتقرير. قواعد التجديد المبكر بعد `EndDate` المعدل والمنتهي/الملغي لم تتغير.
- أصبحت العملة والتواريخ وأنواع الباقات وحالات الدفع/التجديد/الفترات تعرض عبر formatters عربية مشتركة. أزيلت القيم الإنجليزية الخام من رحلات Guardian الأساسية، وأُصلح input التقييم ومداخل خطة الاشتراك لمنع انتقال uncontrolled/controlled.
- `/` أصبح مدخلًا واعيًا بالجلسة يوجّه غير المسجل إلى `/login` وGuardian إلى `/guardian` والموظف إلى `/dashboard`. أصبح `manifest.start_url=/login`. لا يوجد Service Worker أو offline support ولا ادعاء بذلك.
- `AT-002`: **PASS** بعد اختبارات التحويلات العربية، الجذر/manifest، ورحلات Mobile Chromium. `AC-SUB-004`: **IMPLEMENTED**. عوائق الديمو P0 المتبقية: **0**.
- الحكم الحالي: **READY FOR FINAL DEMO REVIEW**، وليس Production Ready. `DM-12` يبقى **PARTIAL** لأن أمر reset آمن ومقيد بسجلات Demo لم يُنفذ وهو خارج Slice 8D؛ لذلك يظل جرد السيناريوهات `11 READY / 1 PARTIAL`. عناصر P1 المفتوحة من التدقيق: **7**.

## Full MVP Completion Audit — سجل تاريخي سابق لـSlice 8D

- اكتمل تدقيق الأدلة للمتطلبات `117/117` ومعايير القبول `34/34` وسيناريوهات الديمو `12/12`، شاملًا معالجة Premium Dashboard UI، من دون تنفيذ application code أو تغيير baseline.
- الحكم الحالي: **NOT DEMO READY** حتى تنفيذ admin renewal وإغلاق اتساق العربية/PWA المتبقي؛ والحكم الإنتاجي **NOT PRODUCTION READY**.
- التقرير الكامل: [MVP_COMPLETION_AUDIT_2026-09-29.md](MVP_COMPLETION_AUDIT_2026-09-29.md).
- المهمة التالية الموصى بها فقط: `Slice 8D — Demo Closure: Admin Renewal + Guardian Arabic/PWA Cleanup`؛ لم تبدأ.

## Dashboard UI remediation — منفذ فعليًا

- أعيد تصميم واجهات الموظفين `Owner` و`Admin` و`Coach` فقط بنظام عربي موحد: خط Cairo، شريط جانبي متجاوب، رؤوس صفحات وأزرار وشارات وفلاتر وجداول/بطاقات وإجراءات صف وحوارات وحالات تحميل/فراغ/خطأ قابلة لإعادة الاستخدام.
- أعيد تنظيم لوحة المالك من البيانات الحقيقية القائمة، والقوائم والتقارير والنماذج وصفحات التفاصيل، دون تغيير API أو الحسابات أو الصلاحيات أو قواعد الاشتراكات والحضور والتقييم.
- أضيف `lucide-react` كاعتماد واجهة خفيف ومثبت، ووثقت القواعد في `docs/ui/DASHBOARD_UI_SYSTEM.md`. بقيت Guardian PWA مستقرة عدا الخط المشترك.
- روجعت الواجهات بصريًا للأدوار الثلاثة عند 1440 و1280 و1024 و768 و390، مع منع overflow للصفحة واستخدام drawer وبطاقات الهاتف عند المقاسات المناسبة. نجح typecheck وlint وproduction build و92/92 اختبار واجهة و35/35 E2E و295/295 اختبار backend، و`npm audit --omit=dev` بلا ثغرات؛ تُثبت نتيجة CI في handoff بعد push.

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
