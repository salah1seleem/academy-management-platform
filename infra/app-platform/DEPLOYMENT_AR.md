# App Platform — تجهيز فقط، دون نشر

الفرع من main عند fd811e58 بعد دمج PR1. لا موارد منشأة، لا أسرار، لا DNS ولا نقل بيانات عملاء. ملفات infra/digitalocean المحلية القديمة محفوظة ولم تُستخدم بدل Dockerfiles المراجعة.

## الإعداد

`app.template.yaml` قالب غير صالح للإرسال كما هو: جميع REPLACE_* تُستبدل في نسخة خاصة مستبعدة من Git. لا تنفذ apps create/update في هذه المهمة. region fra مقترح يتطلب تأكيد توافر الخدمات جميعها قبل الشراء.

- API: Dockerfile القائم infra/vps/api.Dockerfile، port8080، نسخة واحدة 2GB مبدئيًا. Imports__WorkerEnabled=true داخل API؛ لا worker component إضافي ولا توسع أفقي قبل مراجعة عامل الاستيراد القائم.
- Web: Dockerfile القائم infra/vps/web.Dockerfile، port3000، 1GB. ACADEMY_EDGE_API=true وقت البناء يعطل rewrites فقط في هذه النسخة. ingress يحفظ /api كاملًا ويوجهه مباشرة إلى API، و/ إلى Next؛ الويب يستخدم /api من نفس أصل HTTPS. لا اعتماد على اسم api في Compose أو عنوان ثابت أو متغير private غير متاح للبناء.
- API readiness=/health/ready، liveness=/health/live. Web health=/. readiness الحالي يتحقق من قاعدة البيانات؛ لا يثبت صلاحية Spaces أو مزود OTP.
- App Platform يتولى TLS عند ingress. Hosting__AppPlatform=true يثبت Scheme=https دون الثقة في X-Forwarded-* المرسلة من العميل. لا تضبط ReverseProxy__Address الخاص بـCompose. AllowedHosts يقتصر على اسم التطبيق/الدومين الفعلي. يلزم اختبار عنوان العميل ومعدلات IP عند staging؛ لا تثق في كل proxy لمعالجة ذلك.
- Managed PostgreSQL17 إنتاجي منفصل، وليس development DB ذات7دولارات. cluster_name يشير إلى عنقود سيتم اختياره لاحقًا، لا ينشئ القالب عنقودًا بالحجم المقترح. اختر نفس المنطقة وأضف التطبيق إلى trusted sources، دون0.0.0.0/0. اتصال Npgsql صريح بــSSL Mode=VerifyFull ومستخدم runtime، وشهادة CA بالـbase64 من المزود.
- مهمة PRE_DEPLOY تستعمل صورة API نفسها و--migrate-only بحساب DDL منفصل. لا seed أو خادم أو عامل داخلها. فشلها يمنع النشر؛ migrations إضافية متوافقة مع النسخة القديمة لأن الأخيرة قد تبقى تعمل أثناء المهمة. خذ backup وراجع SQL قبل أي تطبيق على بيانات قائمة. طبّق runtime-grants.sql بحساب مالك schema بعد المهاجرات، واضبط default privileges من الحساب نفسه. لا تعيد تنفيذ grants العامة دون إعادة تضييق جدول المفاتيح.

## Spaces والخصوصية

أنشئ لاحقًا bucket خاصًا دون public ACL أو public listing أو CDN عام أو سياسة anonymous read. مفاتيح API تقتصر على القراءة والكتابة والحذف في هذا bucket. Endpoint أصل Spaces HTTPS، وليس hostname CDN. لا مفاتيح في الويب أو Docker build args.

المفاتيح: AcademyId/StorageName؛ مسار help/ لفيديوهات المساعدة الخاصة بالمنصة. لا يقبل المخزن أسماء عشوائية أو traversal. التنزيل يبقى API موثقًا: صلاحيات الأكاديمية والمستخدم والطفل والموافقات والسحب وفترة الاحتفاظ تُفحص قبل S3، ولا redirect لرابط Spaces قابل للمشاركة. الملف يُجلب إلى spool طلب مؤقت يحذف عند إغلاقه، لدعم Range للفيديو؛ خصص مساحة مؤقتة مناسبة وراقب التنزيلات والمهل. الرفع المحول وفك الصور يبقيان في scratch محلي فقط، والنسخة الدائمة في Spaces.

لا fallback إلى local عند فشل Spaces. إعداد Local محفوظ لاختبارات التطوير وCompose. لم تُنقل ملفات قائمة؛ قبل أي انتقال حقيقي يلزم manifest من DB، ونسخة احتياطية، وتحويل الاسم إلى namespace الصحيح، ومطابقة SHA256/الحجم، واختبار رفض أكاديمية ثانية ثم التحويل. لا تخزن قاعدة البيانات روابط signed منتهية الصلاحية.

## مفاتيح الجلسات والاستعادة

migration161 يضيف app_data_protection_keys. XML يُشفّر بشهادة PFX قبل حفظه في PostgreSQL. الشهادة وكلمة مرورها أسرار RUN_TIME؛ التحميل EphemeralKeySet دون ملف مفتاح خاص في الحاوية. ApplicationName=iAcademyOne ثابت. runtime يقرأ ويضيف مفاتيح ولا يعدلها أو يحذفها.

احتفظ بالـPFX وكلمة مروره في خزنة أسرار منفصلة مع DB backup. فقدان الشهادة يمنع فك جلسات ومفاتيح قديمة؛ لا تستبدلها أو تمسح الجدول عند redeploy. دوران الشهادة يحتاج إبقاء شهادة الفك القديمة وإجراء مستقل موثق؛ لم يُنفذ دوران تلقائي. لا تنسخ شهادة الديمو إلى الإنتاج.

Managed backups/PITR تخص DB ولا تحفظ Spaces أو شهادة الفك. يلزم نسخ Spaces خارجي مشفر وسياسة احتفاظ، ونسخة مفاتيح مستقلة، ومطابقة DB/object manifest عند استعادة إلى هدف معزول. لا تسترجع فوق قاعدة عملاء. لا تحسب rollback للصورة rollback للمهاجرات؛ migration المفاتيح يرفض Down حفاظًا على الجلسات.

## OTP والإشعارات

OTP الإنتاجي غير موصول ويعيد501؛ fixed OTP محظور خارج Demo/Testing. لم يبدأ تكامل SMS جديد، ولا يصح إطلاق تسجيل العملاء قبل استكماله وضوابط الإساءة والتسليم.

موجود: inbox وإشعارات داخلية، تسجيل device tokens، worker وقوائم delivery ومحاولات/تراجع، وكود إرسال APNs وFCM عبرHTTPv1 وbridge بالموبايل. غير متحقق: شهادات/مفاتيح وحسابات APNs/FCM الإنتاجية، entitlements وإعداد Firebase وتسجيل الجهاز وتسليم فعلي وفتح التنبيه على جهاز حقيقي. الكود الحالي يقرأ ملفات credentials؛ حقنها بأمان من secrets يحتاج قبول تجهيز منفصل، ولا تُضاف مفاتيح وهمية أو تكاملات جديدة هنا. بدونها لا يدعي worker نجاح إرسال؛ inbox يبقى المرجع.

## التكلفة التقريبية بتاريخ2نوفمبر2026

- API shared1vCPU/2GB:25$/شهر؛ Web shared1vCPU/1GB:12$.
- Managed PG عقدة1GB:15.15$ أساس معلن، والتخزين حسب الاختيار (0.215$/GiB/شهر في الصفحة الحالية؛ راجع ملخص الطلب لتجنب جمع ما يشمله السعر مرتين).
- Spaces:5$/شهر يشمل250GiB و1TiB خروج وفق صفحة السعر.
- المجموع الأساسي57.15$؛ احتط بنحو60–70$ قبل OTP والضرائب والنسخ الخارجي والنقل الزائد ومهام النشر. ليست فاتورة مقاسة أو ضمان كفاية الموارد؛ 300 مشترك لا يعني300متصل متزامن. عقدة DB واحدة ليست HA. لا داعي لشراء dedicated egress IP لمجرد إضافة التطبيق إلى trusted sources إذا دعم الحساب ذلك.

مصادر رسمية روجعت أثناء التجهيز:
- https://docs.digitalocean.com/products/app-platform/reference/app-spec/
- https://www.digitalocean.com/pricing/app-platform
- https://www.digitalocean.com/pricing/managed-databases
- https://www.digitalocean.com/pricing/spaces-object-storage
- https://docs.digitalocean.com/products/spaces/how-to/manage-access/

## القبول المتبقي

فحوص PR، وتحقق schema الرسمي للقالب بعد اختيار المنطقة والحجوم وأسماء الموارد، وتجربة staging لاحقة مصرح بها: رفع صور وفيديو وإثبات تجريبي؛ دخول أدوار؛ رفض أكاديمية ثانية؛ سحب الموافقة؛ recreation لحاويات التطبيق دون حذف DB/Spaces؛ استمرار الصورة وجلسة المستخدم؛ Range؛ الاستعادة المعزولة. لا يُعد إنشاء store/provider جديد محليًا وحده إثباتًا لتشغيل DigitalOcean الفعلي.
