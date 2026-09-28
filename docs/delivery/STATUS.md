# Status

تاريخ التحديث: 2026-09-28. الفرع: `codex/05-slice3-subscriptions-payments`.

## Slice 3 — منفذ فعليًا

- نموذج tenant-scoped للباقات `Duration/Sessions/Combined` مع تحقق تطبيق وقيد PostgreSQL، أسعار `decimal(18,2)` وEGP للديمو. لا hard delete للباقات المستخدمة.
- فترات اشتراك append-only مرتبطة بـ`SportEnrollment` والخطة والتحصيل. المدة شمولية؛ التجديد المبكر يبدأ بعد آخر نهاية، والمنتهي يبدأ من تاريخ تأكيد الدفع. أرصدة الحصص محفوظة ولا تُستهلك قبل Slice 4.
- `RenewalRequest` منفصل عن `PaymentRequest`. السعر والعملة مشتقان server-side من الخطة، و`Idempotency-Key` يمنع double-click. إنشاء الطلب لا ينشئ تحصيلًا أو فترة.
- `IPaymentGateway` مع `InternalTestPaymentGateway` لـDemo/Test فقط. event موقع HMAC ومطابق للمرجع والمبلغ والعملة؛ browser return لا يؤكد الدفع. نجاح موثق ينشئ ذريًا Collection واحدة وReceipt واحدة وSubscriptionPeriod واحدة. الفشل/الإلغاء لا ينشئ أيًا منها.
- قيود قاعدة البيانات: event reference فريد، Collection واحدة لكل PaymentRequest، Receipt وفترة واحدة لكل Collection، receipt number فريد داخل Academy، وعلاقات Academy/Sport المركبة تمنع الخلط بين tenants.
- API للإداري للباقات والفترات الحالية/القريبة/المنتهية وطلبات التجديد والدفع والتحصيلات المشتقة من السجلات المؤكدة. Guardian لا يرى إلا تسجيلات الأطفال المرتبطين وإيصالاتهم ومدفوعاتهم.
- Dashboard يحوي Module «الاشتراكات» وسبعة Sub-modules. ولي الأمر يختار التسجيل والبـاقة ويمر بشاشة Test Payment Gateway عربية واضحة ثم يرى الحالة الموثقة والإيصال.
- seed حتمي بتاريخ `2026-09-28` لأكاديميتين، وباقات كرة قدم وسباحة، وحالات active/expiring/expired/scheduled وconfirmed/failed/pending. إعادة seed idempotent ولا تمس Production.
- migration forward-only: `Slice3SubscriptionsPayments`، وتضيف `SubscriptionPlans`, `SubscriptionPeriods`, `RenewalRequests`, `PaymentRequests`, `PaymentProviderEvents`, `Collections`, `Receipts` فقط.

## مراجعة الأمان المركزة

- لا يقبل DTO المالي `AcademyId` أو amount/currency من العميل؛ `CurrentTenant` و`GuardianPlayerLink` يحددان النطاق، والموارد الخارجية تعيد 404 آمنًا.
- callback يتحقق HMAC وprovider/reference/amount/currency وevent replay قبل الأثر. transaction serializable والـunique indexes يوفران دفاعًا متعدد الطبقات ضد duplicate callback/double-click.
- simulation routes وcallback الداخلي لا تُسجل إلا في `Demo/Testing`، وبدء التطبيق يرفض تمكين البوابة في Production. CSRF مفروض على إنشاء التجديد والمحاكاة، ولا يوجد open redirect أو secret في الاستجابة.
- لا PAN/CVV أو Apple Pay credentials. لا Cash/InstaPay/Vodafone أو شعارات/ادعاءات Apple Pay/Geidea/Fawry. تفاصيل adapter المستقبلي في `docs/payments/PAYMENT_PROVIDER_ADAPTER.md`.

## دليل التحقق

- طُبقت migration على PostgreSQL 17 محليًا، وفُحص سجل migrations والجداول والقيود الجديدة.
- Release build: ناجح، 0 warnings / 0 errors. backend: 59/59 إجمالًا (`57 integration + 1 unit + 1 architecture`)؛ منها 18/18 حالة مالية Slice 3.
- frontend: typecheck وlint وproduction build ناجحة؛ 8/8 اختبارات UI. Mobile Chromium E2E: 7/7، منها 3 رحلات Slice 3 للنجاح والفشل والرؤية الإدارية.
- `npm audit --omit=dev`: صفر vulnerabilities. نتيجة remote CI تُسجل بعد الدفع ولا تُفترض مسبقًا.

## غير منفذ

لا Attendance أو session consumption أو Evaluations أو Nutrition أو Sport Products أو Medical أو Gallery أو advanced Reports. لا خصومات/تجميد/تعديل أيام/إلغاء كامل أو renewal-for-someone-else، ولا refund. لا Geidea/Fawry/Apple Pay أو card/wallet حقيقي، ولا SMS، نشر، أو اختبار Production. `main` لم يُمس.

## نقطة التوقف

Slice 3 فقط. المهمة التالية بعد المراجعة هي Slice 4 Attendance ضمن تفويض مستقل؛ لم تبدأ هنا.
