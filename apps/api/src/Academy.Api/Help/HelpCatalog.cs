using Academy.Api.Auth;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;

namespace Academy.Api.Help;

// Versioned product guidance, not a claim that a tutorial video has been uploaded.
// These texts contain no tenant data, URLs supplied by clients, executable HTML or credentials.
internal sealed record HelpText(string Ar, string En);
internal sealed record HelpGuide(string Id, string Section, HelpText Title, HelpText Summary, HelpText[] Steps, AcademyRole[] Roles, string[] Permissions, string? Feature = null, SportKind? Sport = null, bool CashOnly = false, bool AwaitingContent = false);
internal static class HelpCatalog
{
    private static readonly AcademyRole[] Staff = [AcademyRole.AcademyOwner, AcademyRole.AcademyAdmin];
    private static readonly AcademyRole[] Owner = [AcademyRole.AcademyOwner];
    private static readonly AcademyRole[] Coach = [AcademyRole.Coach];
    private static readonly AcademyRole[] Family = [AcademyRole.Guardian];
    private static HelpText T(string ar, string en) => new(ar, en);
    internal static readonly HelpGuide[] All = [
        new("academy-setup","setup",T("تجهيز الأكاديمية","Set up the academy"),T("راجع الفرع والرياضة والفئة والمجموعة قبل بدء التسجيل.","Check the branch, sport, age category and group before registration."),[
            T("ابدأ من بيانات الأكاديمية، ثم أنشئ الفرع والرياضة والفئات العمرية المناسبة.","Start with academy details, then create the branch, sport and age categories."),
            T("من المجموعة راجع السعة والمدرب وأيام التدريب والتوقيت، واحفظ الجدول قبل إضافة اللاعبين.","In the group, review capacity, coach, weekdays and times; save its schedule before placing players."),
            T("راجع قائمة الحصص الناتجة وتواريخها. إنشاء المجموعة وحده لا يسجل حضورًا أو تحصيلًا.","Check the resulting sessions and dates. Creating a group does not record attendance or collect money.")],Owner,[AcademyPermissions.StructureManage],"training"),
        new("import","setup",T("استيراد البيانات","Import existing records"),T("استخدم القالب والمعاينة لمنع التكرار والربط الخاطئ.","Use the template and preview to prevent duplicates and incorrect links."),[
            T("نزّل القالب الحالي من الاستيراد، واقرأ الحقول والمراجع قبل ملء بيانات اصطناعية للتجربة.","Download the current import template and read its fields and references; practice with synthetic data."),
            T("ارفع الملف للمعاينة، وراجع أخطاء كل صف والروابط بين اللاعب وولي الأمر والمجموعة.","Upload for preview and review row errors and the links between player, guardian and group."),
            T("لا تعتمد ملفًا به تعارضات غير محلولة. بعد الاعتماد افتح عينات من الملفات للتحقق، ولا تعِد إدخال اللاعبين يدويًا.","Do not commit unresolved conflicts. After committing, inspect sample profiles instead of recreating players manually.")],Owner,[AcademyPermissions.PeopleManage],"training"),
        new("packages","subscriptions",T("إنشاء باقة","Create a training package"),T("فرّق بين باقة التدريب والمنتج الرياضي.","Keep training packages separate from sports products."),[
            T("افتح الباقات، وحدد الرياضة ونوع المدة أو الحصص والسعر والفروع المؤهلة.","Open packages and set the sport, duration or session format, price and eligible branches."),
            T("راجع السريان والنشر قبل إتاحتها للتسجيل، ثم اختبر ظهورها في سياق الفرع والرياضة المناسبين.","Review validity and publication before registration; check availability for the correct branch and sport."),
            T("تغيير سعر الباقة لا يعيد تسعير اشتراك أو مستند سابق تلقائيًا.","A new package price does not automatically reprice an existing subscription or document.")],Staff,[AcademyPermissions.SubscriptionPlanManage],"training"),
        new("registration-renewal","subscriptions",T("تسجيل وتجديد اشتراك","Register and renew"),T("استخدم ملف اللاعب والأسرة القائمين بدل إنشاء نسخ أخرى.","Reuse the existing player and family profiles."),[
            T("ابدأ بالبحث عن اللاعب أو هاتف ولي الأمر حتى لا تكرر الأسرة أو الطفل.","Search for the player or guardian phone first to avoid duplicate records."),
            T("راجع الباقة والفرع والمجموعة والخصم والإجمالي في المعاينة قبل التأكيد.","Review package, branch, group, discount and total in the preview before confirming."),
            T("التجديد لا ينشئ لاعبًا جديدًا. الطلب المرفوع بإيصال يحتاج مراجعة مخولة قبل تفعيل الاشتراك.","Renewal does not create another player. An uploaded payment proof requires authorized review before activation.")],Staff,[AcademyPermissions.PeopleManage,AcademyPermissions.SubscriptionRenewManage],"training"),
        new("player-attendance","operations",T("تسجيل حضور اللاعبين","Record player attendance"),T("الحضور لحصة فعلية، وغير المسجل ليس غائبًا.","Attendance belongs to an actual session; unrecorded does not mean absent."),[
            T("افتح حضور اللاعبين واختر تاريخ اليوم ثم الحصة والمجموعة المطلوبة.","Open player attendance, choose today, then the required session and group."),
            T("راجع الأسماء قبل اختيار الحالة واحفظ التغيير وانتظر رسالة النجاح.","Review names before choosing a status; save and wait for confirmation."),
            T("لا يمكن تسجيل حضور مستقبلي. عدم تسجيل الحالة لا يخصم جلسة ولا يثبت غيابًا، والمدرب لا يملك حضور الأطفال افتراضيًا.","Future attendance is not permitted. Unrecorded status neither consumes a session nor proves absence; coaches do not receive player-attendance permission by default.")],Staff,[AcademyPermissions.AttendanceRead],"training"),
        new("transfers-makeups","operations",T("النقل والتعويض","Transfers and make-up sessions"),T("راجع التاريخ والسعة والجدول قبل نقل التسجيل.","Check date, capacity and schedule before moving an enrollment."),[
            T("افتح تسجيل اللاعب وحدد المجموعة المطلوبة والتاريخ الفعلي للتغيير.","Open the player's enrollment and select the target group and effective date."),
            T("راجع المعاينة والتعارضات والسعة والأثر على الحصص قبل الاعتماد.","Review preview, conflicts, capacity and affected sessions before approval."),
            T("لا تستخدم تعديل اسم المجموعة بدل قرار نقل، ولا تعتبر الحصة التعويضية حضورًا مسجلًا بمجرد إنشائها.","Do not rename a group to simulate a transfer; creating a make-up session does not record attendance.")],Staff,[AcademyPermissions.SubscriptionAdjustmentManage],"training"),
        new("evaluations","development",T("التقييم ونشر التقرير","Evaluate and publish"),T("المسودة تختلف عن التقرير الذي يراه ولي الأمر.","A draft differs from the report a guardian can read."),[
            T("افتح اللاعب في سياق مجموعتك أو الحصة، وراجع معايير التقييم المتاحة.","Open the player in the assigned group or session and review available criteria."),
            T("احفظ التقييم كمسودة ثم راجعه قبل النشر إذا كنت مخولًا. لا تملأ درجات تخمينية لإكمال الشكل.","Save a draft and review before publishing if authorized. Do not invent scores to complete a chart."),
            T("ولي الأمر يقرأ التقرير المنشور المرتبط بطفله فقط، ولا يمنحه رابط التقرير صلاحية بيانات طفل آخر.","A guardian can read only a linked child's published report; a report link does not grant access to another child.")],[..Staff,..Coach],[AcademyPermissions.EvaluationRead],"training"),
        new("expenses-allocation","finance",T("المصروفات وتوزيعها","Expenses and allocation"),T("راجع الفترة والمستند وجهة التكلفة ولا تخلط الاستحقاق بالسداد.","Review period, evidence and cost allocation; distinguish accrual from payment."),[
            T("سجل المصروف ومستنده والفترة والفرع أو الجهة المستفيدة، ثم راجع المعاينة قبل الاعتماد.","Record expense, evidence, period and benefiting branch or unit; review before approval."),
            T("عند التوزيع راجع القاعدة والإجمالي، واحتفظ ببيان الأجزاء غير الموزعة أو ناقصة البيانات.","For allocation, check the rule and total; retain visibility of unallocated or incomplete amounts."),
            T("اعتماد مصروف لا يثبت دفعه، ونتيجة التكاليف الناقصة ليست ربحًا مكتملًا.","Approving an expense does not prove payment; incomplete costs do not establish a complete profit result.")],Owner,[AcademyPermissions.ReportsFinancialRead],"expenses"),
        new("payroll-substitutes","finance",T("الرواتب والبدائل","Payroll and substitute coaches"),T("راجع العقد والعمل الفعلي ومكونات الاستحقاق قبل الاعتماد.","Review contract, actual work and statement components before approval."),[
            T("افتح ملف الموظف أو المدرب وراجع العقد وفترته والحساب قبل اعتماد المستحقات.","Open the employee or coach profile and review contract, dates and account before approval."),
            T("راجع الأساسي والخصومات والإضافات والسلف والسداد كلًا على حدة، ولا تساوِ الاعتماد بالصرف.","Review base pay, deductions, additions, advances and payments separately; approval is not disbursement."),
            T("البديل يخص الحصة المحددة، ولا ينقل إسناد المجموعة أو يصرف أجرًا تلقائيًا. الدين الخارجي لا يُخصم من الراتب تلقائيًا.","A substitute applies to that session; it does not reassign the group or pay wages automatically. External debt is not automatically netted against salary.")],Owner,[AcademyPermissions.PayrollRead],"payroll"),
        new("external-partners","finance",T("الشركاء الخارجيون","External partners"),T("بانتظار استكمال المسار ومراجعة المحتوى؛ لا توجد تعليمات تشغيل معتمدة في هذا الدليل بعد.","Awaiting the integrated workflow and content review; this guide does not yet provide operating instructions."),[],Owner,[AcademyPermissions.ReportsFinancialRead],"external_partners",AwaitingContent:true),
        new("owner-dashboard","overview",T("قراءة لوحة المالك","Read the owner dashboard"),T("افهم تعريف الرقم والفترة ثم افتح دليله.","Understand a metric's definition and period, then open its evidence."),[
            T("راجع من تاريخ وإلى تاريخ والفرع قبل مقارنة أي رقم، ومسح الفلتر يزيل حتى التواريخ الافتراضية.","Check date range and branch before comparing metrics; clearing filters also removes default dates."),
            T("افتح تفاصيل البطاقة بدل جمع أعداد متداخلة: قرب الانتهاء جزء من النشط، وفرصة التجديد ليست دينًا.","Open card details instead of summing overlapping counts: expiring is part of active, and renewal opportunity is not debt."),
            T("المتابعة البشرية لا تغير المصدر أو تسدد مالًا عند إغلاقها. راجع البيانات الناقصة قبل تفسير نتيجة مالية.","Closing a human follow-up does not change its source or settle money. Review missing data before interpreting financial results.")],Owner,[AcademyPermissions.OwnerDashboardRead]),
        new("day-close","finance",T("إغلاق اليوم","Close the day"),T("راجع النقد الفعلي والمستندات والفروق قبل تأكيد الإغلاق.","Review counted cash, documents and differences before confirming closure."),[
            T("افتح جلسة التحصيل الصحيحة وراجع كل الحركات ووسائل الدفع قبل الإغلاق.","Open the correct collection session and review movements and payment methods before closing."),
            T("أدخل النقد المعدود فعلًا وراجع الفرق والمعاينة، ولا تنشئ تحصيلًا وهميًا لمساواة الرصيد.","Enter actual counted cash and review differences and preview; do not invent collections to balance the cash."),
            T("بعد التأكيد احتفظ بالمستند المعتمد، وراجع التصحيح من مساره المخول بدل تعديل الدليل التاريخي.","After confirmation, retain the approved document and use authorized correction rather than rewriting historical evidence.")],Owner,[AcademyPermissions.ReportsFinancialRead],CashOnly:true),
        new("coach-sessions","operations",T("حصتي ومجموعتي","My sessions and groups"),T("الحصة موعد محدد والمجموعة قائمة مستمرة.","A session is a dated occurrence; a group is an ongoing roster."),[
            T("من الرئيسية افتح الحصة القادمة، ثم راجع التاريخ والوقت والمكان واللاعبين المسندين.","From home open the next session and check date, time, place and assigned players."),
            T("المجموعات مستقلة عن جدول الحصص. راجع اللاعب والتقييم من سياقه المصرح، والبديل قد يكون لحصة واحدة فقط.","Groups are separate from the session schedule. Open player and evaluation in the authorized context; a substitute may apply to only one session."),
            T("حضورك الوظيفي لا يمنح حضور الأطفال أو تعديل الرواتب، وغير المسجل لا يثبت غيابًا.","Your workplace attendance does not grant player attendance or salary editing; unrecorded status does not prove absence.")],Coach,[AcademyPermissions.CoachGroupsRead],"training"),
        new("family-renewal","subscriptions",T("تجديد اشتراك أبنائك","Renew your children's subscriptions"),T("راجع الباقة والأبناء والإجمالي مرة واحدة قبل رفع الإيصال.","Review package, children and total before uploading proof."),[
            T("من التجديد اختر الباقة المؤهلة والأبناء المطلوبين، ثم راجع الإجمالي والخصم المعروض.","In renewal choose an eligible package and children, then review the displayed total and discount."),
            T("إذا عرضت الأكاديمية بيانات تحويل، ادفع من قناتك ثم ارفع إثبات الدفع في الطلب الصحيح.","If the academy provides transfer details, pay using your channel and upload proof to the correct request."),
            T("رفع الإيصال ليس قبولًا نهائيًا. راجع نتيجة الطلب والإيصال بعد المراجعة، ولا تكرر الطلب بسبب تأخر الرد.","Uploading proof is not final approval. Review the decision and receipt; do not duplicate a request because the response is delayed.")],Family,[AcademyPermissions.GuardianOwnRenewal],"training"),
        new("family-progress","development",T("مواعيد طفلك وتقدمه","Your child's schedule and progress"),T("اختر الطفل واقرأ الموعد الفعلي والتقرير المنشور.","Select the child and read actual schedules and published reports."),[
            T("اختر الطفل عند التعدد ثم راجع التقويم والتحديثات العاجلة قبل الحضور للمكان.","For multiple children select the child, then review calendar and urgent updates before travelling."),
            T("التقرير المنشور يعرض التقييم المحفوظ؛ غياب تقرير ليس درجة صفر أو دليل تأخر الطفل.","A published report contains saved evaluations; a missing report is not a zero score or proof of poor progress."),
            T("التغذية معلومات عامة وليست علاجًا، والمعرض يعرض فقط الوسائط المصرح بها لأبنائك.","Nutrition is general information, not treatment; the gallery shows only media authorized for your children.")],Family,[AcademyPermissions.GuardianEvaluationRead],"training"),
        new("swimming-lanes","operations",T("مراجعة حارات السباحة","Review swimming lanes"),T("راجع الحارة والسعة والمجموعة والمدرب والحجز الفعلي.","Review lane, capacity, group, coach and actual booking."),[
            T("افتح لوحة المرافق واختر الفرع والحارة والفترة المطلوبة.","Open the facility board and select branch, lane and period."),
            T("راجع الحجوزات والتعارضات وسياسة السعة قبل أي تغيير؛ تعريف الحارة وحده لا ينشئ حصة أو حجزًا.","Review bookings, conflicts and capacity policy before changing anything; defining a lane does not create a session or booking."),
            T("إعدادات الحارات تخص السباحة فقط، ولا تضيف قيود سباحة إلى مجموعات كرة القدم.","Lane settings apply to swimming and do not impose swimming constraints on football groups.")],Owner,[AcademyPermissions.FacilitiesRead],"facilities",SportKind.Swimming)
    ];
}
