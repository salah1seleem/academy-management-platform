# مراجع V3 وطبيعة الاستناد

مراجعة بتاريخ 14 أكتوبر 2026. هذه مراجع تقنية/محاسبية للمبادئ المذكورة، وليست دليلًا على تنفيذ التطبيق أو امتثال منشأة. السياسات الافتراضية والأرقام والـUI وقرارات المنتج مقترحات تصميم من متطلبات صلاح، وليست قواعد تنقل حرفيًا من هذه المراجع.

- **R1 — W3C WebAuthn Level 3.** التحقق بمفتاح عام وUser Verification، واختلاف البصمة/PIN، وعدم استنتاج بصمة بعينها أو جهاز مادي واحد من passkey. `https://www.w3.org/TR/webauthn-3/`
- **R2 — W3C Geolocation.** آلية الموقع والأذونات والخصوصية وعدم التعامل مع قراءة العميل كضمان مادي. `https://www.w3.org/TR/geolocation/`
- **R3 — MDN GeolocationCoordinates.accuracy.** دقة الإحداثيات بالمتر وكونها تقديرًا، ومتطلبات secure context. `https://developer.mozilla.org/en-US/docs/Web/API/GeolocationCoordinates/accuracy`
- **R4 — OWASP Authorization Cheat Sheet.** المنع الافتراضي، التحقق لكل طلب، وعدم الاكتفاء بالواجهة. `https://cheatsheetseries.owasp.org/cheatsheets/Authorization_Cheat_Sheet.html`
- **R5 — OWASP Session Management Cheat Sheet.** دورة الجلسة وتغيير الامتيازات وتجديدها/إبطالها المناسب. `https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html`
- **R6 — Apple Local Authentication.** بيانات البصمة تبقى لدى المنصة، والتطبيق يتلقى نتيجة تحقق محلي؛ لا تكفي نتيجة محلية كدليل سيرفري مستقل. `https://developer.apple.com/documentation/localauthentication`
- **R7 — Flutter local_auth، الناشر flutter.dev، وAndroid Biometric Authentication.** الدعم المحلي وحدوده، واستخدام التحقق لحماية العمليات/المفاتيح. افحص الإصدار المتوافق مع المشروع عند التنفيذ، ولا تفرض نسخة لمجرد ورودها في نتيجة البحث. `https://pub.dev/packages/local_auth` ؛ `https://developer.android.com/identity/sign-in/biometric-auth`
- **R8 — W3C WCAG 2.2 Understanding Target Size Minimum.** حجم/تباعد أهداف اللمس؛ هدف المشروع44–48px اختيار تصميمي أكبر من الحد العام24px مع استثناءاته. `https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html`
- **R9 — W3C WCAG 2.2 Understanding Contrast Minimum.** تباين النص العادي4.5:1 وغيره وفق السياق. `https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html`
- **R10 — IFRS Foundation, IFRS 15 overview.** الاعتراف بالإيراد مرتبط بتنفيذ التزامات الخدمة واختيار مقياس تقدم ملائم، لا مجرد قبض المال. `https://www.ifrs.org/issued-standards/list-of-standards/ifrs-15-revenue-from-contracts-with-customers/`
- **R11 — IFRS Foundation, Clarifications to IFRS 15.** التمييز بين مقدم الخدمة والأصيل/الوكيل يحتاج تحديد العلاقة والالتزام، لا التحصيل وحده. `https://www.ifrs.org/projects/completed-projects/2016/clarifications-to-ifrs-15-revenue-from-contracts-with-customers/`

- **R12 — OpenAI Cookbook, Using PLANS.md for multi-hour problem solving.** خطط تنفيذ مستمرة مع سجل تقدم وقرارات وأدلة وتوثيق يسمح بالاستئناف؛ لا ضمان لانتهاء مهمة مهما كانت حدود البيئة. `https://developers.openai.com/cookbook/articles/codex_exec_plans`

## المصدر الداخلي

`reference-v2/Academity_Master_Plan_V2_AR.md`: نسخة Library الأصلية المؤرخة11 أكتوبر2026، محفوظة للمقارنة دون تعديل. تتضمن18 شريحة V2-00..V2-17 ورواتب وعقودًا خارجية وإعلانات وتقارير ومبادئ تصميم. لا تثبت هذه الوثيقة حالة اكتمال الكود. V3 تحدد التعديلات الصريحة ولا تسقط بقية V2. مراجع V2 نفسها محفوظة كما كانت وليست جميعها إعادة بحث في هذه الحزمة.
