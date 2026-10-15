# أدلة قبول V2 + V3

## C2C3D — مدة عقد العمل مصدر مؤرخ — 2026-10-15

المعتمد الحالي3000 محفوظ، وعقد الشهر التالي المنتهي يوم15 يحسب3000×15/أيام الشهر؛1500 فينوفمبر. المسودة وتنبيه انتهاء العقد لدى المالك وEmployeeDetail وquote ينعكسون من العقد الساري؛ المصدر الخام والكشف والصرف لا يكتبون ثانية. Initial مع إنشاء الموظف، LegacyBaseline من المراجعة، Change متسلسل، مع noPast/finalized وexpectedRevision/replay/CSRF/tenant/livegrant/self، وحراسة SQL/EF وثبات reset المعروف. Web سجل ومراجعة وحفظ، وFlutter «عقدي وتغييراته» ذاتي read-only دون ملاحظات داخلية. المصدر نفسه لاتفاق/تنفيذ الخاص وCorrection.

التكامل19/19 ثم6/6 ثم12/12 متداخل، وحفظ بيانات الملف1/1؛ Web25/25 وFlutter10/10 وanalyze، وحدة12/12 ومعمارية1/1؛ typecheck/lint/build وEF بلا pending model. E2E6/7 (محدد status ملتبس) ثم1/1 ثم7/7 في39.9s على قاعدة جديدة بعد91migration، ثم1/1 بعد تنسيق الزر؛ صور390/1440 والمحاولات/SHA/CI في [PROGRESS](PROGRESS.md). الجديدة `20261015104526_V3DatedEmploymentContracts` فقط على اختبار؛ Demo اختياري يحفظ الموجود ويرفضProduction، بلا تغيير.env أوmain أو بيانات عرض أو إرسال/دفع حقيقي. ليست الجولة الموحدة/F10 أو جهازFlutter/PG17Production؛ الافتتاح ونهاية الخدمة والدفتر والخارجي وبقية الخريطة باقية. F04/AC14/22 جزئية.

## C2C3C — تصحيح عمولة مراجع لا يعيد كتابة الكشف — 2026-10-15

Code `a52623c6835d7bbd142017b607ac94b386a9601e` مرفوع، [CI38712738840](https://github.com/salah1seleem/academy-management-platform/actions/runs/38712738840) **in_progress**؛ لا قبول شامل قبل انتهاء البوابات. main مؤكد محفوظ؛ مصفوفة18/48 بلا missing/duplicate.

بعد3100معتمدًا/مسددًا، Credit400 لحصة من8/2000/40% ينشئ Review0 ثمCorrection−20 في شهر راتب غير معتمد يختاره المالك صراحة. الأصل والراتب الأساسي والتحصيل والصرف تبقى؛ الشهر التالي2980، subtotal الخاص−20 محتسب مرة واحدة. قيد مصدر ثابت بنفس review/snapshot/actor وسلسلة متزامنة، quote قابل للإبطال، replay/CSRF/tenant/Owner-only وSQL/EF؛ لا راتب أو خصم تلقائي خارج العقد، ولا مقاصة دين خارجي أو دفتر مالي جديد. Web يحفظ فعليًا وFlutter يقرأ شهره وقيمته فقط.

تكامل3/3 ثم17/17 ثم النهائي4/4 متداخلة؛ Web24/24 وFlutter8/8 وanalyze؛ وحدة12/12 ومعمارية1/1 وtypecheck/lint/build وEF بلا pending model. E2E5/6 (تاريخ اتفاق fixture رجعي رفضه الخادم)، ثم1/1، ثم بعد إصلاح تنسيق الحقول النهائي6/6 في43.2s على قاعدة معزولة جديدة بكل90migration. صور Web390/1440 وأسماء القواعد والمحاولات والسجلات وSHA/CI في [PROGRESS](PROGRESS.md). الجديدة `20261015085300_V3ReviewedPrivateCorrections` فقط، بلا تغيير migrations القديمة/.env/متطلبات/main أو قاعدة العرض. ليست قبولV2/V3 كاملًا أوF10 أو جهازFlutter/بصمة/PG17Production. الافتتاح والعقد المؤرخ ونهاية الخدمة والتصحيح الموجب وF07/F10 وبقية الخريطة باقية؛ F04/AC14/22 جزئية.

## C2C3B — السلفة لا تصرف أو تخصم مرتين — 2026-10-15

Code `637eeb2e133978c5c56551fcf5344807caaf062a` مرفوع؛ [CI38705926897](https://github.com/salah1seleem/academy-management-platform/actions/runs/38705926897) in_progress عند الفحص، لا قبول شامل مفترض. main محفوظ وخريطة18/48 بلا missing/duplicate.

PayrollAdvance خروج مال مسجل، وتخصيصه فيPayrollDisbursement القائم تسوية لا صرف جديد. تستعمل الرواتب/المالك/الحساب/CSV نفس المصادر؛ فصلDirectPaid وAdvanceApplied وRecordedAdvanceTotal/UnallocatedAdvances وCashOutRecorded. لاPayrollAdjustment أو مصروف/قبض/PayrollSettlement مكرر، ولا مقاصة تلقائية أو بنك فعلي. Grant مستقل معPay/CRUD، منعAdminself عبرCoach، وعزل/سحب/مرجع اختياري وقيودSQL/EF. F04/AC14/22 **جزئية**: التصحيح بعد الاعتماد/الصرف والافتتاح/العقد المؤرخ والدفتر باقية.

Backend21/21 للمستهلكين، النهائي خمس سلف+reset6/6، ثمSQL/تزامن2/2 بعد89migrations من الصفر؛ متداخلة. وحدة12/12 ومعمارية1/1؛ Web32/32 وFlutter8/8 وanalyze؛ typecheck/lint/build وEF بلا pending model. E2E أولًا4/5 (محددlabel)، ثم1/1، ثم النهائي5/5 في38.5s على `academy_v3_advances_final_20261015` الجديدة. مرجع3000: سلفة750→تخصيص500→صرف1000→تخصيص250؛ المسدد1750، المتبقي1250، الخصومات0، وخروج المال1750 لا2500. مرجعشهرين6000→1750→4250، وتكرار/تزامن لم ينفقا الرصيد مرتين. CSVcutoff قبلالتخصيص يحتفظبالسلفةغيرالمخصصة750 والمسدد0. [390](evidence/c2c3b/payroll-advances-390.png)/[1440](evidence/c2c3b/payroll-advances-1440.png) Web فعليتان مراجعتان؛ لا اختبارجهازFlutter أوFullRegression/F10 موحد مدعى.

Migrationالجديدة `20261015074148_V3PayrollAdvances` فقط؛ opt-in `Demo__PayrollAdvanceSeedEnabled=true` معلومالهوية، سلفةحتى500 للمدربالرابع ومصدرالتخصيص مرةواحدة، مع حفظالموجود ورفضProduction/resetالمعزول. .env والمتطلبات/main وقاعدةالمراجعة محفوظة. السجلات والمحاولات وCodeSHA/CI والاستئناف في [PROGRESS](PROGRESS.md).

## C2C3A — حساب معتمد متعدد الأشهر وCSV بنفس cutoff — 2026-10-15

Code مرفوع `7968116085aa89e3c71890dfe0340381286481b6`؛ [CI38699435887](https://github.com/salah1seleem/academy-management-platform/actions/runs/38699435887) **in_progress** وقت الفحص، لا قبول نهائي مفترض. main محفوظ وخريطة18/48 بلا omission/duplicate.

تجميع نفس مصادر الكشوف والدفعات، لا كتابة مالية من قراءة الحساب. 25 شهرًا:75000−2500=72500 (72000سابق+500حالي)، مع مسددLegacy مستبعد لا معاد ترحيله؛ توقيت ثابت يمنع احتساب دفع لاحق فيCSV سابق. صاحب القراءة لا يكتسب الصرف/الاعتماد، وCoach/Admin يريان حسابهما المرتبط فقط. افتتاح وسلف غير موثقين، لا صفرًا. F04/V2-08/V3-AC14/22 **جزئية**؛ لا دفتر عام أوF10 كامل أو تسوية شهر مغلق.

تكامل17/17 ثم النهائي خمس الحساب+reset6/6 في33s (متداخلة)، وحدة12/12 ومعمارية1/1، Web20/20، Flutter7/7 وanalyze؛ typecheck/lint/build وEF بلا pending model، دون migration جديدة. E2E متأثر4/4 في25.7s، ثم1/1 في12.9s لأحدث الواجهة:7200→2500→4700→صرف3600 من الشهر السابق→1100. CSV المقروء قبل الدفعة يظل4700؛ OwnMobile API مطابق ويحجب الملاحظات/المراجع. تاريخ مستقبلي مرفوض، اعتماد/دفع لاحق مستبعد، DST والعزل والـIDOR/scope/revocation/CSV injection وتهيئةDemo المحروسة مثبتة. [390](evidence/c2c3a/payroll-account-390.png) و[1440](evidence/c2c3a/payroll-account-1440.png) Web حقيقيتان مراجعتان، ليستا صور جهازFlutter. الرحلة ليست قبولV2/V3 الموحد.

قاعدة اختبار فقط `academy_v3_c2c2_migration_20261015`؛ opt-in `Demo__PayrollAccountSeedEnabled=true` يجهز كشفي المدرب الرابع الآن دون دفع مفبرك ويحفظ المصادر القائمة. تصحيحات fixtures/seed والملاحظات/السجلات والـSHA/CI في [PROGRESS](PROGRESS.md). التالية سلفة وتخصيص صريح وتصحيح فترة مفتوحة، لا إعادة بناء الحساب أو نسخ متبقيه.

## C2C2 — اعتماد المستحق وصرفه جزئيًا — 2026-10-15

Code SHA `6c8a64edcfad3521c92e9fc2f9238c4f36feaf10` مرفوع؛ [CI38692947752](https://github.com/salah1seleem/academy-management-platform/actions/runs/38692947752) in_progress وقت التوثيق، لا passed مفترض. main محفوظ وخريطة18/48 لا تعني اكتمال التنفيذ أو القبول الموحد.

V3-AC09/14/22 وF04 تظل **جزئية**: مصدر كشف شهري معتمد بصلاحية مستقلة عن الصرف/الراتب/الخاص، ودليل بنوده/hash، وتسجيل دفعات/مدفوع/متبقٍ متسلسل. 3000+600=3600، صرف2500→1100، ثم1100→0؛ لا PayrollSettlement/Receipt/مصروف مكرر. المصدر القديم لا يمحى أو يعاد ترحيله. منع مستقبل/مصدر stale/تجاوز/تزامن/replay/عزل أكاديمية وفرع وself عبر دور ثانٍ وCRUD وحده وسحب الإذن، وثباتSQL/EF والكشف بعد refundReview مثبتة.

تكامل المستهلكين26/26 ثم المتأثر7/7 ثم الخمس الجديدة5/5؛ مجموعات متداخلة. وحدة12/12 ومعمارية1/1، Web27/27، Flutter7/7 وanalyze؛ typecheck/lint/build وEF بلا pending model. Football E2E3/3 في25.1s ثم الصورة1/1 في12.5s، وصور [390](evidence/c2c2/payroll-statement-390.png)/[1440](evidence/c2c2/payroll-statement-1440.png) فعلية مراجعة. المالك/Payroll/Native API ذاتي يقرؤون المدفوع والمتبقي نفسيهما؛ لا بيانات source/note/reference مالية داخلية في تطبيق الموظف، ولا أدوات اعتماد/صرف له. ليس اختبار Flutter جهاز فعلي أو الجولة الموحدة.

النهائي أضاف حالة SQL لنقل/تصفير خصم بعد الاعتماد: الستة الجديدة+reset7/7 في35s، وحارس المصدر1/1 على قاعدةmigrations جديدة بالكامل في12s. القديم والجديد مقفولان، والمصدر2950 لا يعاد كتابته. هذه إعادة المتأثر بعد حراسة داخلية، لا جولةE2E/Full Regression أخرى؛ لا تجمع الأعداد المتداخلة.

Migration `20261015054545_V3ApprovedPayrollDisbursements` على اختبار فقط ومن الصفر في قاعدةfinal. seed اختياري محروس لشهر موظف مستحق معروف، بلا صرف/reset تلقائي. تصحيحCI locator317083a أعاد الرحلة1/1؛ CI إصلاحه Web/Flutter/E2E success وBackend جارٍ وقت الفحص. عودة التفويض الحساس بعد toggle اكتشفتها assertions وأصلح المنتج؛ OTP429 بقيت حمايته واستخدمت قاعدة جديدة دون حذف القديمة. السجلات والمحاولات والحدود في [PROGRESS](PROGRESS.md). أرصدة وسلف/تسوية بعد الصرف/دفتر وF10 باقية، لا قبول محاسبي كامل.

## C2C1 — أساسي مؤرخ وصلاحية اعتماد مستقلة — 2026-10-15

Code SHA `26b98c3fe13070c41b295dfbca79cb1384c08e03` مرفوع على الفرع نفسه؛ [CI38681653081](https://github.com/salah1seleem/academy-management-platform/actions/runs/38681653081) بدأ **in_progress**، وليس passed. main محفوظ وخريطة18/48 مكتملة معرفيًا فقط، لا اكتمال تنفيذ أو قبول موحد.

V3-AC09/14/22 وF04 **جزئية**: مصدر SalaryVersion ثابت وموعد شهر وموافق/نسخة/hash، PaidSnapshot محفوظ، LegacyBaseline من شهر المراجعة فقط؛ لا مصروف أو صرف من الاعتماد. قائم على Payroll لا موديول مالي مكرر. الراتب الحالي وقائمة الموظفين والكشف واتفاق الخاص يقرأون المصدر الساري؛ Web تعديل الملف read-only ومسار اعتماد مستقل؛ Flutter قراءة ذاتية دون actor/note. سياق Admin+Coach لنفس الشخص لا يعتمد أجره، ولا تكفي CRUD أو صلاحية صرف/خاص.

تكامل **27/27 في1m49s** (`V3SalaryHistoryTests` + Payroll + C1/C2 + التفويض والسحب وreset المحدد)، وحدة **12/12**، Web أربعة ملفات **28/28**، Flutter ثلاثة ملفات **9/9**؛ typecheck/lint/build/analyze وEF بلا pending model. E2E **2/2 في24.2s** ثم رحلة الصورة **1/1 في15.7s**: زيادة مستقبلية500 مع بقاء الحالي وقراءة شهرها وMobile API ذاتي، وإعادة ترحيل الخاص المتأثر. [390](evidence/c2c1/salary-history-390.png) و[1440](evidence/c2c1/salary-history-1440.png) Web فعلية، عنوان داخل viewport وشبكة1/2 دون overflow. لا Flutter Simulator/device integration أو Full Regression في هذه الشريحة.

Migration `20261015033834_V3DatedSalaryHistory` على قواعد اختبار معلومة؛ all migrations من الصفر في قاعدةE2E جديدة، ولا reset للمراجعة/العميل. حارس Initial صار مصدر إنشاء خادميًا داخل المعاملة بعد فشل ربط ساعة الاختبار/SQL وتجربةxmin/savepoints؛ أصلح projection تحميل actor وredirect308، وفصل fixture الحسابات وحفظ seed اليدوي. المجموعة قبل إصلاح استقلالseed26/27 ثم الفاشل/السحب/reset3/3 ثم27/27 النهائي؛ لا تخفيف assertions أو حذف مصادر. التفاصيل والسجلات الفعلية في [PROGRESS](PROGRESS.md).

المتبقي: كشف شهري معتمد/دفعات جزئية/رصيد وسلفة وتصحيح ما بعد الصرف وتأريخ عقد، F07/F10، باقي18V2/48V3 والقبول الموحد. SHA وCI يسجلان بعد الرفع؛ لا green مفترض أو release-ready.

## C2B — فرق عمولة الخاص إلى Payroll القائم — 2026-10-15

**متابعة CI الأساس:** checkpointC2A2610c6a انتهى **820/822 Backend**، وWeb/Flutter/E2E success. أصلح تكامل fixture الإيصال الناقص، ولقطة الرياضة/اللاعب/الباقة من المصدر بدل نص كرة القدم الثابت. لا تغيير مالي/تقرير أو تخفيف assertions. إعادة الفاشلين ومستهلكي الإصلاح **4/4 في23s** على `academy_v3_c2b_regression_20261015` الجديدة بعد migrations كاملة، ويختبر الخاص receipt/reportلاعبين اصطناعيين لا يخلط الرياضة، ومطابقة Owner/Payroll. سجلci-fixture-regression؛ CI C2B38672602556 بدأ ولم يكتمل عند هذه الكتابة.

**النسخة النهائية:** E2E **5/5 في29.5s** بعد تنسيق المراجعة، جميع migrations من الصفر على قاعدةC2B الجديدة. مراجعة [390](evidence/c2b/private-posting-390.png)/[1440](evidence/c2b/private-posting-1440.png)، computedgrid/flex وعدمoverflow مثبتة. Web27/27، Flutter11/11، typecheck بعدnext-env وlint/build/analyze وEF بلاpendingmodel. سجل `/tmp/academy-v3-c2b-e2e-fresh-final.log`؛ لا تخفيف OTP أو إعادة Full Regression. الآتي سجل المحاولات لا نتيجة معلقة جديدة.

مطابقة Payroll/Owner summary لنفس الفرع والشهر: **1/1 في9s** بعد عزل فرع اصطناعي وإكمال Receipt لكل Collection جديد في fixture، دون حذف سجلات سابقة أو تعديل تقرير لتجاوز بيانات اختبار ناقصة؛ الأولى500 محفوظة في سجلowner-match والنهائيowner-match-final. المثال الخاص بقي100 ثم200 وصافي3200، وإضافات وصافي لوحة المالك يساويان تقرير الرواتب.

إعادة مصادر الخاص كاملة بعد إصلاح fixture: **8/8 في33s**، تشمل C2A وC2B؛ `private-final.log`. ليست8 اختبارات جديدة إضافة إلى15؛ المجموعتان متداخلتان.

بعد شرط AcademyId الصريح الأخير في subtotal: Backendمطابقة/عزل **2/2 في13s**، وإعادة E2Eللرحلة المتأثرة **1/1 في16.7s** (`scope-final`/`e2e-postscope`).5/5 قبل هذه الحراسة الدفاعية؛ الواجهة لم تتغير.

- الوحدة `PrivateCompensationCalculatorTests`: **6/6**. التكامل المحدد `V3_private_` + `PayrollIntegrationTests` + `V3_payroll_`: **15/15 في54s**، ثم حراسة null وتوافق reset مصدر Payroll الجديد **2/2 في21s** (حراسة null متداخلة). لا Full Regression. قاعدة `academy_v3_fixture_20261014` فقط.
- `V3PrivatePostingTests`:8حصص/2000/تنفيذ2/1000تحصيل/40%=ترحيل100، ثم200 عند اكتمال التحصيل بفرق100 فقط. راتب3000 →3100 →3200؛ لا تغيير الأساسي ولا عدد قبض/إيصالات بسبب الترحيل. الضغط المتزامن بالمفتاح نفسه201+200، والقديم المتغير409، وتفاصيل المصدر/الاتفاق40/cutoff ثابتة.
- credit400 بعد ترحيل100 للحصة: فرق−20 لا يخصم تلقائيًا؛ Review0 بلا PayrollAdjustment ثم تأكيد Reversal−20، أصل100 محفوظ وصافي الراتب3080. بعد صرف3100 يبقى snapshotالصرف3100 والاستثناء0 دون خصم أو ترحيل مستقبل. null من تعارض حضور يبقى null، لا استحقاق0. SQL يرفض مراجعة null غير حقيقية وتسلسلًا مزورًا وحذف المصدر/تعديل حركة راتبه؛ EF يمنع الكتابة على المصدر.
- CSRF400، مفتاح قصير422، quoteمتغير409، أكاديمية ثانية404، الإداري رغم تفويض الاتفاق/الصرف403، المدرب403. قراءة Flutter الذاتية تعكس المرحّل200/فرق0، دون طفل أو فاتورة/رصيد أسرة. لا منح تلقائية من Coach أو CRUD. اختبار reset لا يمس إلا tenantFootball المحجوز: التشغيل العادي يحفظ المصدر، resetالصريح المحروس يزيله ويحفظ مصادر الأكاديمية الأخرى وcounts30/6؛ لا reset للعرض/عميل.
- Web4ملفات **27/27**، تشمل معاينة/تأكيد وbodyبلا مبلغ أو تاريخ، منع الكتابة للحالات المغلقة/null، رفض quoteقديم، ومصدر subtotalلا يكرر الصافي. typecheck/lint/productionbuild ناجحة. Flutter **11/11** للاتفاقات/الخاص/المدرب وanalyze بلا ملاحظات؛ لا اختبار جهاز فعلي أو Flutter integration جديد.
- Football E2E: أول4/5 لترتيب toast/modal، ثم **5/5 في23.9s**. بعد ضبط CSS الشبكة،3/5 لأن OTPالمدرب وصل429 من إعادة اختبار متقاربة في القاعدة نفسها؛ لم تخفف حماية OTP أو assertions. أنشئت قاعدة منفصلة جديدة `academy_v3_c2b_e2e_20261015` دون حذف القديمة/تعديل بياناتها، وطُبقت كل migrations من الصفر ثم **5/5 في29.5s** كما صدر هذا السجل؛ الصور فعلية وليست اجتيازًا مفترضًا.

Migration `20261015014116_V3PrivateCompensationPosting` تضيف سجل مصدر لا دفترًا عامًا. طُبقت على قواعد الاختبار الثلاث فقط؛ تعديل حراسة SQL أثناء تطوير migration الجديدة طابق دالتها في القاعدتين الأوليين، ثم تحقق النشر من الصفر في الثالثة. EF بلا pending model. لا نشر أو رسالة/دفعة حقيقية أو تغيير متطلبات/main/.env. AC16 نفذ مثاله المحدد واختبر، **implemented_pending_verification** للجولة الموحدة. عدم اكتمال كشف الرصيد/الدفعات الجزئية/تسوية ما بعد الصرف/تأريخ الأساسي/الدفتر وF10 والتفويض المستقل يبقي AC13–15 وF03/F04 **partial**؛ اقرأ [D3-017](DECISIONS_AND_CONFLICTS.md).

## C2A — تنفيذ خاص معتمد وتقييم عمولة مرتبط بالمصدر — 2026-10-15

**إعادة النسخة النهائية:** Backend3/3 للمتأثر بعد حراسة تعارض الحضور اللاحق (null+requiresReview، لا صفر)، Web28/28، Flutter19/19 وanalyze/typecheck/lint/build. Football E2E **4/4 في19.7s** بعد مرجع المطالبة القصير وحراسة التعارض، وصور390/1440 فعلية مراجعة بلا overflow.115/115 أدناه مجموعة المستهلكين قبل الحراسة، لا ادعاء إعادتها كاملة بعد آخر تعديل. التفاصيل والمحاولات والاستئناف فيPROGRESS.

- `PrivateCompensationCalculatorTests`: **6/6**؛ minor units والتناسب و0 وPerSession وDelivered. expected مستقلة:8/2000/2/1000/40%=100+100، ثم200 وفرق100.
- `dotnet test tests/integration/Academy.IntegrationTests --no-restore --filter 'FullyQualifiedName~V3_private_|FullyQualifiedName~V3_agreement_|FullyQualifiedName~PayrollIntegrationTests|FullyQualifiedName~V2ReceivablesTests'`: **115/115 في7m43s** على academy_v3_fixture_20261014، دون skipped. ليست Full Regression. تشمل التكرار/العزل/الغياب/المستقبل/CSRF/المدرب غير المسند/Owner-only،40→30 وcredit400 وثبات SQL/EF؛ راتب3000 وبلا سداد/PayrollAdjustment محفوظان. إعادة توافق seed وحدود reset المحروس مع Football القديم: **3/3 في27s**، دون تخفيف counts30/6 القديمة؛ قاعدة اختبار فقط.
- Web coach-private-services/coach-agreements/structure-record/payroll/staff-access: **27/27**؛ typecheck/lint وproduction build ناجحة. next-env يعاد إلى مسارات dev الأصلية بعد البناء.
- Flutter coach_agreements/staff_amounts/v2_role_workspaces: **18/18** وanalyze بلا ملاحظات. API ذاتي يزيل الطفل/المطالبة/صافي الأسرة؛ لا إدخال نسبة أو اعتماد في الموبايل. لا اختبار Simulator/جهاز فعلي لهذه الإضافة.
- Football E2E:4/4 في18.4s قبل إضافة مرجع المطالبة القصير للفصل بين باقتين لنفس الطفل. إعادة النسخة النهائية وصورها تسجل فيPROGRESS. الاختبار يثبت ربط مطالبة → تأكيد → اعتماد حصة → replay بنفسId → قراءة مدرب عبرBearerOTP Demo دون بيانات الأسرة؛ يحتوي إنشاء المدرب390/1440 والمتابعة القديمة.
- Migration `20261015001813_V3PrivateServiceEvidence` على قواعد اختبار فقط؛ EF بلا pending changes. السجلات `/tmp/academy-v3-c2a-{consumers,seed-compatibility,web,flutter,analyze,build,e2e-final,typecheck-restored,lint}.log`.

المحاولات:1/3 تكامل ثم إصلاح طول PlayerCode وxmin ومفتاح طلب تخصيص fixture، ثم3/3 و115/115. E2E3/4 كشف مصدر seed بتوقيت لاحق لساعة الديمو؛ صار مصدر اليوم السابق، وقاعدة اختبار جديدة بدل تعديل الصف الثابت.3/4 أخرى مهلة قراءة body بعد نجاح الحفظ؛ صار التحقق من HTTP201 والحالة المحفوظة وreplayAPI بنفسId.1/1 ثم3/4 لاختيار أول صف بدل fixture مع زيادة الصفوف عند إعادة التشغيل؛ المحدد صار تاريخ/رقم المصدر، مع إبقاء assertions50/50/250، ثم4/4. لا خفض assertion أو ادعاء عطل شبكة.

**الحد:** AC13–16/F03 ما زالت partial. تقييم الاستحقاق ومصدره محفوظان، لكن فرق100 لم يرحل إلى Payroll/دفتر ولم يُصرف. ربط الاشتراك المحصل مباشرة والحجز المالي المسبق/العكس والتفويض المستقل وبقية V2/V3 باقية كماPROGRESS.

## B3b2 — السياق الإداري المستقل

Code SHA `9fdb815af29d70b9b10ccf17cbabfee891eabd7b` مرفوع. [CI38637100984](https://github.com/salah1seleem/academy-management-platform/actions/runs/38637100984) بدأ وهوin_progress عند checkpoint؛ نتائج التشغيل البعيد مستقلة عن الاختبارات المحلية أدناه. مراجعة Git/check/المتطلبات: لا تعديلات خارج الشريحة، لا تغيير baseline، وmain المحلي/البعيد محفوظ.

Migration `20261014202341_V3CorrelatedAdministrativeContexts` على fixture/E2E المعزولتين، وEF has-pending-model-changes ناجح. تحفظ فهرس تفرد العضويات الأساسية وتضيف ارتباط user/academy مركبًا وحارس هوية السياق؛ لا ترحيل آلي لصلاحيات أو بيانات عملاء ولا downgrade فعلي.

`dotnet test tests/integration/Academy.IntegrationTests --filter 'FullyQualifiedName~V2StaffScopeTests|FullyQualifiedName~MobileAuthenticationTests|FullyQualifiedName~TenantAuthenticationTests|FullyQualifiedName~Group_filter_returns_only_group_players' --no-restore --nologo -v minimal`، باتصال `academy_v3_fixture_20261014`: **69/69 في221s**. ست حالات جديدة تغطي Owner/CSRF/tenant/حالة افتراضية/منع إنشاء موظف مكرر/رفض worker وحارسSQL23514؛ قويA/رياضة1 وضعيفB/رياضة2، قراءة وCSV/منع كتابة وIDOR وسحب فوري؛ دخول Web pending قبل الاختيار؛ حفظ معلق بعد الإيقاف/all-sports وبعد إيقاف الأساس؛ Mobile token rotation واختيار صريح وإصدار3→4 عند تعديل المنح ورفض refresh بعد الإيقاف؛ seed متكرر وحفظ السحب ورفض Production. كان68/68 قبل إضافة بوابة pending الخادمية، ثم أعيدت المجموعة المتأثرة69/69 بعد آخر تغيير، فلا تجمع العددين.

Web **38/38** في11 ملفًا، typecheck/lint ناجحان؛ الاختيار الصريح والرفض يحفظان المدخلات، الإنشاء لا يفعّل، التفعيل/الإيقاف مؤكدان بالنسخة، ودخول متعدد السياقات يفتح الاختيار. Flutter **19/19** فيaccess_context/auth_controller/widget معanalyze؛ ثلاثة اختبارات سياق جديدة تشمل الاسم والنطاق والإصدار وعرض دورين إداريين مميزين. لا تشغيل simulator أو اختبار جهاز خادمي بهذه الشريحة.

E2E `v3-access-contexts.spec.ts v3-custom-role-templates.spec.ts v2-staff-access.spec.ts` على5186/3106 وقاعدةE2E: **3/3 في36s** مع production build. إنشاء ومراجعة وتفعيل من UI، دخول بلا بيانات قبل الاختيار، نطاق الضعيف ومنع كتابة، ثم نطاق القوي وكتابة صحيحة؛ القوالب وتفويض V2 السابقان بقيا يعملان. صور390/1440 تحتevidence/b3b2، مراجعة فعلية وأبعاد بلا overflow. المحاولة الأولى2/3: عنوان الاختبار «اللاعبون» لا يطابق العنوان الفعلي «قائمة اللاعبين»؛ صُحح locator إلى الاسم الفعلي دون حذف assertion. أخطاء إعداد الاختبار الأولى كانت تغيير مفتاح BranchId بدل إنشاء مجموعة مستقلة، وLIKE علىjsonb، ومسارoptions غير موجود؛ صححت fixtures/قراءةJSON/المسار، لا منطق المنتج أو assertions الأمن. أمر EF الأول افتقدConnectionStrings، وأمر npm نُفذ منmobile بدلالجذر؛ صحح السياق فقط، بلا تثبيت أو تغيير إعدادات.

الدليل يخص السياقات الإدارية التشغيلية. الأفعال المالية الحساسة المستقلة وrelation_scope المخصص وبقية48قبول والانحدار النهائي باقية؛ لا CI أخضر لهذا الكود قبل تشغيله عن بُعد.

## B3b1 — القوالب المخصصة

Migration `20261014185336_V3ReusableStaffPermissionTemplates` مطبقة على قاعدتي الاختبار V3 فقط؛ has-pending-model-changes نجح. `dotnet test ... --filter 'FullyQualifiedName~V2StaffScopeTests|FullyQualifiedName~V3_mixed_sport_family' --no-restore` **18/18 في63s**. الاختبارات الأربع الجديدة في V3StaffRoleTemplateTests تشمل owner/tenant/CSRF، رفض مصفوفة ناقصة أو write بلاread، اسم مكرر، stale revision، مصدر أجنبي، الاسم لا يغير Role، حفظ مصدر/تخصيص/نطاق، عدم تغيير الجلسة بتعديل/أرشفة تعريف ثم التغيير الفعلي عند اعتماد نسخة جديدة، immutability عبرEF وSQL23514، وseed متكرر يحفظ الأرشفة ولا يمنح تلقائيًا.

Web staff-access/staff-role-templates **10/10** مع typecheck/lint. E2E v2-staff-access/v3-staff-templates/v3-custom-role-templates **3/3 في34.9s** مع production build؛ صور390/1440 محفوظة. الرحلة الجديدة فشلت أولًا لأن اختبارGET سبق اكتمالPUT؛ أضيف انتظار response204 وعودة القائمة قبل فحص المصدر، دون حذف assertion أو تغيير منطق الحفظ. إعادة1/1 نجحت، ثم كشفت مراجعة الصورة قص أعمدة المصفوفة على الموبايل، فعولج العرض لا المنح، وأعيد3/3 مع assertion يمنع overflow داخل المصفوفة أيضًا. مراجعة390 النهائية تمت.

بعد مراجعة السجل أضيفت حالة المصدر/الإصدار/التخصيص الفعلية من الخادم إلى `AppliedTemplate` داخل audit After مع إبقاء TemplateKey والجسم القديم للتوافق. أضيف assertion على التخصيص والمصدر، وأعيدت القوالب ومستهلك القالب السابق **5/5 في17s**؛ لا جمعها مع18 كعدد فريد. EF بلا pending model بعد التغيير.

Code SHA `fe52bfe6bab7eb8962c136db0c15a5143f7715f0`؛ CI [38623096545](https://github.com/salah1seleem/academy-management-platform/actions/runs/38623096545): Frontend/Flutter/E2E success وBackend in_progress عند آخر فحص. CI السابق B1/B2/c0d339e أصبحsuccess؛ B3a فشل كما التحديث أدناه. لا Full Regression محلي أو Flutter/device جديد أو اكتمال V2/V3. قوالب قابلة لإعادة الاستخدام فقط؛ المنح المتعددة حسب الدور/العلاقة والأفعال الحساسة باقية كما PROGRESS.

### نتيجة CI وإصلاح تداخل بيانات الاختبارات

CI B3a [38609005372](https://github.com/salah1seleem/academy-management-platform/actions/runs/38609005372): المعمارية1/1 والوحدة190/190؛ التكامل799/800؛ بقية الوظائفFrontend/Flutter/E2E success. الفاشل Group_filter_returns_only_group_players رأى طفل الأسرة الاصطناعية الإضافي في مجموعة السباحة المشتركة، كما أثبت log. أصلح `5aad08c616d8f8ec82fd0c08d060e9365b64a6a1` مصدر التداخل، لا الاختبار القديم: مجموعة سباحة مستقلة للأسرة مع assertions تمنع تغيير المجموعة المشتركة أو خلط الفلتر.

إعادة على قاعدة V3 السابقة18/19 احتوت خمسة أعضاء سبق أن تركتهم التشغيلات القديمة؛ لم يُحذف أحد. قاعدة جديدة `academy_v3_fixture_20261014` مع migrations القائمة: الاختبار القديم1/1، ثم الأسرة1/1، ثم `dotnet test tests/integration/Academy.IntegrationTests --filter 'FullyQualifiedName~StructurePeopleEnrollmentTests|FullyQualifiedName~V2StaffScopeTests|FullyQualifiedName~V3_mixed_sport_family' --no-build --no-restore --nologo -v minimal` **36/36 في98s** بعد رحلة الأسرة. EF has-pending-model-changes ناجح؛ لا migration أخرى. نجاح الاختبارات المستهدفة لا يساوي اجتياز CI الكامل على الإصلاح؛ يلزم فحص تشغيل HEAD الجديد في الاستئناف.

## B3a — نطاق الرياضة التشغيلي، 14 أكتوبر 2026

Code SHA `70f217fa57e58844b58a7dad35f02aa956fee9e4` مرفوع. CI [38609005372](https://github.com/salah1seleem/academy-management-platform/actions/runs/38609005372) **in_progress** عند الفحص، ولا تعادل النتائج المحلية نجاح CI. بعد مراجعة نطاق HR أضيف منع الحضور الوظيفي اليومي المشترك من التفويض الرياضي الضيق وأعيد الاختبار المتأثر **1/1**؛ لم تمس بقية نتائج الاختبارات أو assertions.

نفس قاعدتي الاختبار المعزولتين B2؛ لا reset أو تغيير لقاعدة المراجعة. Migrations `20261014165922_V3MembershipSportScope` و`20261014170814_V3PlanSportScopeLookup` مطبقتان على الاثنتين؛ `has-pending-model-changes --no-build` نجح.

الأمر النهائي المستهدف `dotnet test tests/integration/Academy.IntegrationTests --filter 'FullyQualifiedName~V2StaffScopeTests|FullyQualifiedName~V3_mixed_sport_family|FullyQualifiedName~Family_request_and_proof_require_every_branch|FullyQualifiedName~Branch_admin_cannot_publish_outside_scope|FullyQualifiedName~IMP_06_branch_admin_can_publish|FullyQualifiedName~Scoped_admin_collects_linked_family_across_branches' --no-restore` باستخدام اتصال قاعدة V3: **18/18 في61s**. يشمل 8 اختبارات V3 جديدة: نطاق/IDOR/CSV/كتابة صحيحة ومرفوضة، مدخلات أجنبية وتوافق عميل قديم، منع المجالات المشتركة وworker، كائن tracked وهوية طفل متعدد الرياضات، دور ثان لا يوسع الدور المختار وتعارض مدرب عبر الرياضات، سحب scope/action قبل حفظ أمر، seed آمن متكرر، طلب أسرة وإثبات متعدد الرياضات. الستة القديمة وأربعة مستهلكين اجتازوا دون خفض assertions.

مجموعة إضافية محددة للحصص والحجز/إنشاء المجموعة **7/7**، تضم 3 حالات V3 متداخلة أعيدت بعد توسيع التحقق؛ لا تجمع الأرقام كاختبارات فريدة. Web: staff-access / sport-operations-settings / facilities **12/12**؛ typecheck/lint ناجحان. متصفح mobile-chromium: v2-staff-access / v3-staff-templates **2/2 في25.9s** مع production build على5186/3106. تحقق حفظ ثم reload ثم دخول إداري مستقل ثم سحب الرياضة من المالك وانعكاسه في نفس الجلسة. صور فعلية [390](evidence/b3a/staff-scope-390.png) و[1440](evidence/b3a/staff-scope-1440.png)، assertions تمنع overflow. مراجعة لقطة390 الكاملة؛ ليست صور كل أدوار V3.

أول محاولة خادم كشفت دورة EF بين الباقة ونشرها بسبب فلاتر الرياضة؛ أصلحت بـlookup SQL tenant-parameterized، لا تعطيل حماية الأكاديمية. اختبار tracked أولي حاول تغيير مفتاح EF ثابت، فعدل السيناريو لاختبار تغيير حالة كائن مخفي وهوية مشتركة باستخدام عضوية فعلية مفوضة، مع بقاء رفض الكتابة وعدم الحفظ. أخطاء compilation في سيناريوهات الاختبار صُححت قبل النتائج النهائية. لا إخفاء فشل أو وصف تشغيل aborted بأنه ناجح.

القيود: تفويض رياضي ضيق ليس كل وظائف الإدارة أو دورًا مخصصًا متعدد المنح. توجد عمليات مشتركة ممنوعة صراحة حتى تطبيق مساراتها، والمالك والتفويض السابق الواسع لم يتغيرا. لا Full Regression موحد أو Flutter/device جديد أو ledger/F10 أو إعلانات أو بيانات V3 الشاملة. CI اللاحق نتيجة مستقلة؛ التشغيلات السابقة كانت Backend جاريًا ولا توصف خضراء بالكامل.

## خط أساس 2026-10-14

- Starting HEAD: `3d5a60d5026a5b8cd54bcf3f1f43abcf1ea6934e` على `codex/15-native-mobile-app`، شجرة نظيفة ومطابقةorigin. main لم يُغيّر.
- سلامة ZIP: حسابSHA-256 لجميع8 ملفات فيmanifest وطولbytes؛ **8/8** مطابقة. مرجعV2 مطابق لنسخة المستودع. المعرفات:V2-00..17=18، مصفوفةV2=85، V3-AC01..48=48. المرجع الحسابي يحوي29قيمة expected ولم ينفذ على المنتج بعد.
- CI للأساس: [run38570922252](https://github.com/salah1seleem/academy-management-platform/actions/runs/38570922252): backend/frontend/flutter success؛ e2e failure. logs تؤكد timeout دخولowner.football ضمنprofileLegacyRegression؛ النقل إلىtests/e2e/football يبقي نفس الرحلة والتوقعات. لا Passed للإعادة قبل تشغيلها.
- V3-AC01: قراءة ومطابقة المصادر والجرد مثبتة فيPROGRESS؛ لا تعني تنفيذ بقية48حالة.

## تنفيذ B1

B1 commit: `ee102e8844af289d5d7002c21503d99873ef3378`، مرفوع. CI38588910233: frontend/flutter/e2e success؛ backend كانin_progress عند الفحص، لا ادعاءCIكامل.

### B1 — تحقق فعلي على قاعدة academy_v3_dev_20261014

قاعدةPG16 جديدة معزولة، طبقت عليهاmigrationsالقائمة فقط، دونresetأو بيانات عميل. الأوامر: `dotnet test tests/integration/Academy.IntegrationTests/Academy.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~V2StaffScopeTests` **6/6**؛ `npm test -- src/features/staff-access.test.tsx` **4/4**؛ `npm run typecheck` و`npm run lint` وproductionbuild ناجحة. `npx playwright test ... v2-feedback-followup.spec.ts` علىfootballconfig **1/1**؛ `npm test -- v3-staff-templates.spec.ts v2-staff-access.spec.ts` علىports5186/3106 **2/2** معproductionbuild.

الصور390/1440 في`tests/e2e/test-results/v3-staff-templates-Owner-a-2bbbd-nd-reopens-the-saved-grants-mobile-chromium/`؛ روجعت390 فعليًا وفحصoverflowبالاختبار. محاولةBackendالأولى5/6 بسببContainsعلىjsonbفيtest، صُحح بقراءةJSONوالتأكد منTemplateKey، ثم6/6. محاولةtypecheck/buildالأولى كشفتrenderقديمًا unreachable فحذفناه؛ E2Eالأولى0/2 بسبب عنوانين متطابقين وتوقع201لعقدإنشاء200؛ صحح العنوان والتوقع وفقAPIثم2/2. لم تُحذفassertionsحفظ/عزل/أثر. القوالب ليستroleengineV3كاملًا. CIبعدpushبوابة مستقلة.

F10 / الدفتر / الإعلانات / إثبات الجهاز / بيانات3أكاديميات / النهائي الموحد / صورV3 / الأجهزة الحقيقية: **لم تنفذ**. لا تكامل خارجي مهيأ يعلن ناجحًا. نتائجV2 القديمة موثقة فيdocs/delivery/V2_EXECUTION.md وليست نتائجV3.

الإعادة النهائية للمتصفح **2/2 في32.3s** على قاعدة ثانية نظيفة `academy_v3_e2e_20261014` حتى لا تلوث فروع اختبارات التكامل صور العرض. البحث عن فرع والقائمة محدودة الارتفاع؛ أعدناWeb4/4 وtypecheck/lint بعد التغيير. روجعت لقطة390 النهائية. الصور تتضمن شاشةصلاحياتB1فقط؛ بقيةصورV3لمتنفذ.

## B2 — تشغيل سباحة معزول في المرافق

Code commit: `13c500331040684ba6fd3f33f639e79d5f921e4c` مرفوع؛ migrationالوحيدة لهذه الدفعة`20261014152943_V3SportScopedSwimmingPolicies`. النسخ المرئية الدائمة: [360](evidence/b2/sport-settings-360.png)، [390](evidence/b2/sport-settings-390.png)، [430](evidence/b2/sport-settings-430.png)، [Desktop](evidence/b2/sport-settings-1440.png)، [صلاحيات390](evidence/b2/staff-template-390.png)، [صلاحياتDesktop](evidence/b2/staff-template-1440.png). نسخ من لقطات التطبيق الفعلية، لا mockups أو صور مرجع، ولا تستبدل أدلة الصلاحيات.

القاعدة `academy_v3_dev_20261014` للاختبارات و`academy_v3_e2e_20261014` للمتصفح فقط؛ PG16 محلي، لا قاعدة مراجعة/عملاء. migration `20261014152943_V3SportScopedSwimmingPolicies` طبقت على الاثنتين، بما فيها backfill المسبح القديم فيE2E. `dotnet ef migrations has-pending-model-changes ... --no-build` نجح: لا تغييرات معلقة.

Backend: فلتر `V3_swimming|Resource_capacity_requires|Last_capacity_is_atomic|Shared_adjacent_bookings|Coach_conflicts_are_checked|Facility_tenant_branch_role|Day_pass_` **10/10**؛ بعد حراسةseed واختبارها، `V3_swimming|Day_pass_` **6/6**؛ المستهلكون `Resource_timetable_preview|Generated_sessions_reserve|Visit_entry_retry|Visit_demo_seed|Observation_expected6|Observation_demo_seed` **6/6**. ليست أعدادًا تجمع كاختبارات فريدة. تشمل رفضtenant/football/coach/guardian/CSRF، version409، تعطيلAوبقاءB، quoteوعروضالولي بعدالإيقاف، حفظ التاريخ وعدمإنشاءمال، وseedلايعيدالتفعيل.

Web: `npm test -- src/features/sport-operations-settings.test.tsx src/features/facilities.test.tsx src/features/staff-access.test.tsx` **11/11**، typecheck/lint ناجحان. E2E: `npm test -- v3-sport-settings.spec.ts v3-staff-templates.spec.ts` **2/2 في24s** معproductionbuild على5186/3106. الرحلة: رياضةGeneral→Swimming؛ حفظ فرع؛ reload؛ فرع آخرoff؛ إنشاءPoolوحارةواعتماد؛ حجزOpenSwim؛ إيقاف؛ POSTآخر403؛ الحجزالأولباقدونحذف؛ رابطالمرافقيحفظالفرع.

صور360/390/430/1440 تحت `tests/e2e/test-results/v3-sport-settings-Owner-co-24939-y-and-disables-new-bookings-mobile-chromium/`، مع overflow assertions؛ فحص390/1440فعليًا، وإصلاحselectخامفيالواجهةبمقاس48pxوتباعدمقروء، ثم إعادة2/2. محاولةE2Eالأولى تعثرتgetByLabelلـselect؛ الثانيةstatusغيرفريد بسببnoticeعام؛ استُخدمrolecomboboxوstatusخاصبنصالحفظ، ثم1/1و2/2 دونحذفassertions. lint كشفeffectloadوعولجباستدعاءfetchداخلeffectبـAbortController؛ typecheckكشفmockfetchوعولجبـstubGlobal. أمرmigrationالأولافتقدمتغيرconnectionوتصححعلىقاعدةالاختبار؛ لا مشكلةشبكة أو retryبلاسبب.

لا اختبار جهاز/Flutter جديد فيB2 ولا دور رياضة مترابط أو ledger أو3أكاديمياتDemo كاملة؛ هذه فجوات محفوظة. CIالجديد بعدpush مستقل عن النتائج المحلية.

## B3b3A — تفويضات الرواتب الحساسة، 2026-10-15

الدليل البرمجي: PayrollSensitiveAccess وStaffAccessEndpoints وPayrollEndpoints؛ الاختبارات الجديدة الأربعة في V3PayrollSensitiveTests. الأوامر:

- `ACADEMY_TEST_CONNECTION_STRING=...academy_v3_fixture_20261014... dotnet test tests/integration/Academy.IntegrationTests --no-restore --filter 'FullyQualifiedName~V2StaffScopeTests|FullyQualifiedName~PayrollIntegrationTests'`: **33/33**.
- بعد تكامل سجل التدقيق: فلتر `V3_payroll_review|V3_sensitive_grants|V3_pending_sensitive|V3_inflight_payment|PayrollIntegrationTests|OwnerReviewOperationsTests` (FullyQualifiedName لكل جزء): **27/27**. لا تعتبر المجموعتين اختبارات فريدة مجتمعة.
- `npm run test --workspace apps/web -- src/features/payroll.test.tsx src/features/staff-access.test.tsx src/features/staff-access-contexts.test.tsx src/features/staff-role-templates.test.tsx src/features/staff-access-audit.test.tsx`: **25/25**؛ typecheck/lint ناجحان.
- `flutter test test/staff_amounts_test.dart`: **2/2**؛ لا تغيير Dart أو اختبار بصمة/جهاز فعلي.
- `npm run test:e2e -- v3-payroll-sensitive.spec.ts v3-access-contexts.spec.ts` بقاعدة academy_v3_e2e_20261014 ومنافذ5186/3106: **2/2 في53.4s**، مع production build. [390](evidence/b3b3a/payroll-grants-390.png) و[1440](evidence/b3b3a/payroll-grants-1440.png) روجعتا فعليًا.
- `dotnet ef migrations has-pending-model-changes ... --no-build`: لا تغييرات؛ لا migration لهذه الشريحة.

يثبت السيناريو رفض CRUD وحده، فصل الاعتماد/الصرف، منع الطلب الذاتي وIDOR وفرع آخر وأكاديمية ثانية، رفض المنح المجهولة/المكررة والنطاق غير المدعوم، replay متزامن بلا مضاعفة، وسحب الإذن/الفرع أثناء طلب صرف HTTP منتظر قفل المنح بلا كتابة. القيمة الفعلية 3100+150=3250 محفوظة في PayrollSettlement، وليست دفعًا مصرفيًا حقيقيًا. تقرير التدقيق يميز تغيير التفويض واسم منفذه.

محاولات الفشل والإصلاح والاستئناف وحدود B3b3A في PROGRESS. هذه أدلة مستهدفة فقط؛ لم تنفذ البوابة الموحدة أو F10 أو اعتماد إصدار V2/V3. CI البعيد بوابة منفصلة عن النتائج المحلية.

## C1 — اتفاقات أجر الخاص، 2026-10-15

المنفذ: CoachAgreementEndpoints وCoachCompensationAgreement/Approval ومحرر CoachAgreements في ملف المدرب وCoachAgreementsPage داخل Flutter. Migration `20261014231750_V3CoachPrivateAgreements` على قاعدتي fixture/e2e المعزولتين فقط؛ EF بلا pending model.

أوامر ونتائج فعلية:
- `dotnet test tests/integration/Academy.IntegrationTests --no-restore --filter 'FullyQualifiedName~V3_agreement|FullyQualifiedName~PayrollIntegrationTests'` مع academy_v3_fixture_20261014: **11/11**. مجموعة13/13 السابقة تشمل مستهلكي التفويض كذلك ولا تجمع كاختبارات فريدة.
- `npm run test --workspace apps/web -- src/features/coach-agreements.test.tsx src/features/structure-record.test.tsx src/features/staff-access.test.tsx src/features/payroll.test.tsx`: **23/23**، typecheck/lint ناجحان.
- `flutter test test/coach_agreements_test.dart test/staff_amounts_test.dart test/v2_role_workspaces_test.dart`: **17/17**؛ flutter analyze بلا ملاحظات.
- `npm run test:e2e -- v3-coach-agreements.spec.ts v3-payroll-sensitive.spec.ts` بقاعدة academy_v3_e2e_20261014 ومنافذ5186/3106: **2/2 في30.8s** معproduction build. يتضمن OTP Demo202 ثمverify200 وقراءة المدرب عبرBearer؛ لا اختبار جهاز/Flutter حي.
- [390](evidence/c1/coach-agreements-390.png) و[1440](evidence/c1/coach-agreements-1440.png) Web فعلية مراجعة؛ لا overflow. next-env أُعيد إلى المسارات الأصلية بعدbuild ثمtypecheck.

الخمسة الجديدة تثبت المصدر/العزل/الصفر الصريح/التكرار/نسخ40→30/عدم الرجعية/انتهاء الخليفة/cutoff/ثباتEFوSQL/سحب الربط/دورAdmin+Coach/seed والمحافظة على البيانات وProduction rejection. الراتب3000 لم يزد ولم يسدد بسبب الاتفاق. لا استحقاق خاص أو دفتر أوF10 ناجح في هذه الدفعة؛ مصدر النسبة أصبح جاهزًا للربط فيC2 وليس المنتج المالي كاملًا. المحاولات المصححة وحدود/استئناف الدفعة فيPROGRESS.
