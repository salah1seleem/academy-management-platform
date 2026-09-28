# Payment Provider Adapter

الحالة: **Slice 3 implemented seam — لا يوجد provider إنتاجي متصل.**

## العقد الحالي

تعتمد وحدة التجديد على `IPaymentGateway` بدل اسم provider بعينه. العقد ينشئ checkout بمرجع opaque، ويعرض الوسائل التي يقررها provider، ويبني/يتحقق من provider event. `PaymentProcessor` مستقل عن تفاصيل checkout: يقبل event موثوقًا ومطابقًا للمرجع والمبلغ والعملة، ثم ينفذ داخل transaction واحدة: تأكيد `PaymentRequest`، دفع `RenewalRequest`، إنشاء `Collection` و`Receipt` وفترة اشتراك تاريخية.

`InternalTestPaymentGateway` هو sandbox داخلي لـDemo/Test فقط. يوقع event بـHMAC، ويدعم نجاحًا وفشلًا وإلغاءً. يرفض التشغيل إذا فُعّل خارج `Demo/Testing`، ولا تُسجّل routes المحاكاة هناك. لا أموال أو بطاقات أو wallets حقيقية، ولا تخزين PAN/CVV أو Apple Pay credentials.

## adapter الإنتاجي المستقبلي

الموصل الحقيقي يجب أن يطبق دون تغيير مجال الاشتراك:

- `CreateCheckout` عبر provider-hosted checkout أو SDK معتمد.
- `GetAvailablePaymentMethods` من استجابة provider/merchant؛ لا hardcode من الواجهة.
- `Verify/ParseWebhook` بالتوقيع والمفاتيح ودفاع replay.
- `QueryPaymentStatus` فقط إذا احتاج provider reconciliation موثقًا.
- `Refund` مؤجل لموافقة ونطاق مستقل، ولا ينفذ في Slice 3.

Apple Pay مطلب إنتاجي أساسي لمسار ولي الأمر، لكن ظهوره لاحقًا مشروط بدعم provider وأهلية merchant والجهاز والمتصفح. Geidea هو الهدف المفضل لأول adapter حقيقي لأن اتجاه المنتج يتطلب Apple Pay؛ هذا تفضيل تخطيطي لا ادعاء اتصال أو اعتماد. يلزم قبل التنفيذ التحقق من sandbox والعقد والبلد والعملات والوسائل المتاحة ومتطلبات domain verification، ثم threat model واختبارات webhook. لا يجوز عرض شعار Apple Pay أو Geidea قبل إثبات التكامل الحقيقي.

## حدود البيانات والأمان

المجال يحتفظ فقط بالمبلغ الدقيق والعملة وحالة عامة ومراجع provider الآمنة. الأسرار في secret store إنتاجي لا في قاعدة البيانات أو Git. callback لا يثق في browser redirect؛ صفحة الرجوع تستعلم عن الحالة المؤكدة من API. كل payment/collection/receipt tenant-scoped، ووصول Guardian مشروط بـ`GuardianPlayerLink` الصريح. unique indexes تمنع حدث provider، والتحصيل، والإيصال، والفترة المكررة.
