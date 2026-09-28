# Domain Model v1.0

**الحالة: PROPOSED — مفاهيمي، مع تحقق foundation المحدود أدناه.**

**ملاحظة تنفيذ Slice 1:** تحقق فعليًا فقط `Academy`, `ApplicationUser`, `AcademyMembership`, `UserSession`, و`GuardianOtpChallenge`/Identity claims. كل الكيانات التجارية الظاهرة أدناه ما زالت مفاهيم مستقبلية ولم تُنشأ جداولها.

## العلاقات الأساسية

```mermaid
erDiagram
  Academy ||--o{ AcademyMembership : scopes
  User ||--o{ AcademyMembership : has
  Academy ||--o{ Branch : owns
  Academy ||--o{ Sport : offers
  Branch ||--o{ TrainingGroup : hosts
  Sport ||--o{ TrainingGroup : classifies
  TrainingGroup ||--o{ RecurringSchedule : plans
  TrainingGroup ||--o{ TrainingSession : occurs_as
  User ||--o| Guardian : profile
  Academy ||--o{ Player : owns
  Guardian ||--o{ GuardianPlayerLink : linked
  Player ||--o{ GuardianPlayerLink : linked
  Player ||--o{ SportEnrollment : enrolls
  Sport ||--o{ SportEnrollment : for
  TrainingGroup ||--o{ SportEnrollment : assigns
  SportEnrollment ||--o{ SubscriptionPeriod : has
  SubscriptionPlan ||--o{ SubscriptionPeriod : defines
  SportEnrollment ||--o{ RenewalRequest : requests
  RenewalRequest ||--o| Collection : confirms
  SubscriptionPeriod ||--o{ SubscriptionAdjustment : changes
  TrainingSession ||--o{ Attendance : records
  SportEnrollment ||--o{ Attendance : trainee
  AcademyMembership ||--o{ Attendance : staff
  Sport ||--o{ EvaluationCriterion : defines
  SportEnrollment ||--o{ PlayerEvaluation : receives
  PlayerEvaluation ||--o{ EvaluationScore : contains
  EvaluationCriterion ||--o{ EvaluationScore : scores
  Sport ||--o{ SportProduct : categorizes
  NutritionItem ||--o{ NutritionCategoryLink : appears_in
  Player ||--o{ PlayerMedicalRecord : has
  Player ||--o{ PlayerMedia : has
```

كل كيان tenant-owned يحمل `AcademyId` ضمن المفتاح المنطقي والعلاقات. المعرفات UUID/UUIDv7 opaque؛ الأكواد القابلة للعرض منفصلة ولا تُستخدم وحدها كصلاحية.

## الملكية والثوابت

- `Player` هوية الطفل داخل الأكاديمية؛ `SportEnrollment` هو ارتباطه برياضة/فرع/مجموعة. uniqueness يمنع تسجيلين active متطابقين وفق قاعدة تعتمد لاحقًا، ولا يمنع رياضتين. `GuardianPlayerLink` علاقة صريحة بحالة وصلاحيات، وليست استنتاجًا من الهاتف أو الدفع.
- `TrainingGroup` يجمع الرياضة/الفرع/الفئة؛ `RecurringSchedule` قالب أسبوعي، و`TrainingSession` واقعة مؤرخة بحالة Scheduled/Held/Cancelled. `Attendance` unique على academy+session+subject-type+subject-id وحالته `NotRecorded|Present|Absent`؛ عدم السجل لا يتحول تلقائيًا إلى غياب.
- `SubscriptionPlan` نوعه `Duration|Sessions|Combined` ويحمل العملة/السعر والمدة أو الحصص المنطبقة. `SubscriptionPeriod` تاريخ محفوظ لا يُستبدل بالتجديد. `RenewalRequest` منفصل عن `Collection`; `Receipt` يعكس Collection مؤكدة فقط. `SubscriptionAdjustment` append-only للتجميد/الأيام/الإلغاء/التصحيح مع السبب والمنفذ.
- `PlayerEvaluation` مرتبط بالتسجيل والمدرب وreporting period وحالته `Draft|Published|Superseded`. score nullable من 0–100؛ null ليست صفرًا. criteria رياضية، ويمكن ربط criterion بمحور report اختياري ووزن لاحقًا.
- `SportProduct` تجارة رياضية محتملة مستقلة. `NutritionItem` معلومات وصورة وحصة وقيم/source status وتصنيفات Breakfast/Lunch/Dinner؛ لا Money أو Rating أو Cart أو Order. `PlayerMedicalRecord` و`PlayerMedia` لهما visibility/publication policy منفصلة.

## دورات الحالة

```mermaid
stateDiagram-v2
  [*] --> Pending: RenewalRequest
  Pending --> Confirmed: authorized collection
  Pending --> Rejected
  Pending --> Expired
  Confirmed --> [*]
```

```mermaid
stateDiagram-v2
  [*] --> Scheduled
  Scheduled --> Active
  Active --> Frozen
  Frozen --> Active
  Active --> Expired
  Scheduled --> Cancelled
  Active --> Cancelled
```

القواعد الدقيقة للمدة والحصص والتجميد والإلغاء تخضع لـ`OD-004/005`. الانتقال المالي `Pending -> Confirmed` وحده ينشئ Collection/Receipt ويمدد الفترة atomically وبـ idempotency key. التصحيح لا يحذف السجل الأصلي.

## التقييم والتقارير

تقرير كرة القدم يعرض الاسم/الصورة/المركز/الإجمالي، ثم ستة محاور (`passing, dribbling, speed, defending, physical, shooting`)، ثم العمر والطول والوزن والقدم، ثم كل criteria. default المقترح للمراجعة: متوسط موزون للدرجات المنشورة غير الناقصة داخل الفترة؛ قيمة المحور من criteria المرتبطة به، والإجمالي من المحاور المتاحة فقط مع إظهار completeness. لا يعتمد هذا قبل `OD-007` ولا يملأ محورًا ناقصًا بصفر. للرياضات الأخرى قالب report خاص بالرياضة؛ إلى حين اعتماده تعرض criteria التفصيلية فقط ورسالة أن الملخص غير معرف.

## الوقت والمال والقياسات

- التخزين الزمني `timestamptz` UTC؛ العرض بمنطقة الأكاديمية (demo: Africa/Cairo). تاريخ الميلاد وstart/end كـ`date` عندما لا يلزم وقت. clock حقن قابل للاختبار وDemoReferenceDate صريح.
- الأموال `decimal(18,2)` مع ISO currency (EGP للديمو)، بلا float. السعر الأصلي والخصم والصافي محفوظة، والتقارير تجمع Collections confirmed فقط.
- الطول cm والوزن kg بقيم decimal وحدود validation؛ العمر يحسب من الميلاد في تاريخ العرض ولا يخزن كحقيقة مستقلة.
- السجلات التاريخية audit fields (`CreatedAt`, `CreatedBy`, version/concurrency token) وأرشفة بدل الحذف عندما توجد مراجع مالية/حضور/تقييم.
