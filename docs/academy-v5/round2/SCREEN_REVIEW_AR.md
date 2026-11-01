# V5-02 — مصفوفة مراجعة الشاشات

24 أكتوبر 2026. المرجع v5.1. الجرد **176 صيغة واجهة** مستخرجة من162ملف صفحة، بعد توسيع أنواع الهيكل واستبعاد التداخلات غير القابلة للوصول. ليس عدد162ملفًا دليلًا على162شاشة مختلفة مقبولة.

**154 صيغة فُتحت في المتصفح:**102 مدخل ثابت و52 صيغة تفاصيل بروابط من الواجهة. مرت150منها على1440و375، وأضيفت4تفاصيل على375؛ اختُبرت7عينات على768 وعينات بالمظهر الفاتح. ثم فُحص21عرضًا من البناء النهائي على3278 بحسابَي المالك والإداري. لا خروج أفقي في المشاهدات المسجلة. الصيغ الـ22 الباقية راجعت من مصدرها ومكوناتها المشتركة؛ لم تتوفر سجلات أو إتاحة مناسبة لفتح حالة أعمال فعلية، فلا تنسب لها موافقة بصرية فردية.

الزيارة تعني مراجعة التكوين الظاهر وحالة البيانات المتاحة، ولا تعني إتمام كل إجراءات الصفحة أو كل التبويبات أو كل حالات السجل. بعض القوائم فارغة أو تعرض عدم الإتاحة الحالية؛ لا تُنشأ سجلات مالية أو تمنح صلاحيات لإخفاء هذا الحد. بيانات العرض اصطناعية، ولم ينفذseed/reset/migration.

## قرارات الواجهة

| الرمز | قرار المراجعة والتنفيذ |
|---|---|
| A — مشترك في الجميع | عنوان مختصر ورجوع واضح ومساعدة اختيارية؛ خط ومسافات وأسطح من هوية الرئيسية، RTL ومقاسات مرنة |
| B | أعمدة مستقلة وبطاقات للهاتف وإجراء رئيسي؛ بحث وفلاتر أساسية، والفلاتر الإضافية قابلة للفتح |
| C | حقول بمحاذاة واضحة في عمودين على الكمبيوتر وعمود على الهاتف؛ عناوين أقسام وخطوات المجموعة وإجراءات متسقة |
| D | تبويبات للملفات الطويلة وروابط مرتبطة بالسجل؛ الملاحظة بأربعة تبويبات مع حفظ المسودة أثناء التنقل |
| E | كشف منظم واختيار واحد من حالات الحضور الخمس؛ بقاء المزامنة والتعارض وإعادة التحقق، وحالة الإعداد غير المتاح مفهومة |
| F | جداول للطلبات والتحصيلات، التفاصيل الأساسية ظاهرة والنماذج الثانوية قابلة للفتح؛ تحذيرات الأثر المالي ظاهرة عند القرار |
| G | اختيار اللاعب وتسجيله الرياضي قبل فتح أدوات التطور؛ معايير وتقرير بأعمدة، ومخطط مقروء، ومساعدة مختصرة للتقرير الشهري |
| H | المعرض أولًا والرفع منفصل، رابط مباشر للذكريات ثم اختيار اللاعب/المجموعة؛ ضوابط النشر والخصوصية محفوظة |
| I | إبراز حسابي والأمان الصحيح في التنقل؛ إرشادات اختيارية مع بقاء حالات التهيئة والخصوصية اللازمة |

A يسري على جميع الصفوف. قراراتD وF لا تحول البيانات المحاسبية إلى تعديل أو حذف؛ القواعد والخدمات والصلاحيات قائمة كما هي.

## السجل لكل صيغة

«فُتحت» أدناه تعني الحالة المتاحة فقط. «مصدر/مكون» ليست قبولًا بصريًا لسجل غير موجود. مثال الرابط محفوظ فيJSONالمرافق، ويمكن فتحه محليًا فقط أثناء تشغيل الديمو. الصور النهائية المختارة في مجلدscreenshots، والسجلات الكاملة المؤقتة فيtmp/v5-round2المستبعد منGit.

| صيغة المسار | قرار التكوين | الدليل | المقاسات | المظهر | حالة البيانات/الحد |
|---|---|---|---|---|---|
| `/dashboard/academy/branches/[id]/edit` | C — النماذج | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/academy/sports/[id]/edit` | C — النماذج | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/academy/categories/[id]/edit` | C — النماذج | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/academy/groups/[id]/edit` | C — النماذج | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/academy/branches/[id]` | D — الملفات والتبويبات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/academy/sports/[id]` | D — الملفات والتبويبات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/academy/categories/[id]` | D — الملفات والتبويبات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/academy/groups/[id]` | D — الملفات والتبويبات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/academy/coaches/[id]` | D — الملفات والتبويبات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/academy/branches/new` | C — النماذج | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/academy/sports/new` | C — النماذج | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/academy/categories/new` | C — النماذج | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/academy/groups/new` | C — النماذج | فُتحت | 375, 768, 1440 | dark, light | حالة العرض المتاحة |
| `/dashboard/academy/coaches/new` | C — النماذج | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/academy/branches` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/academy/sports` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/academy/categories` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/academy/groups` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/academy/coaches` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/academy/coaches/create` | C — النماذج | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/academy/groups/sessions` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/account/attendance` | E — الحضور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/account/verification` | I — حسابي والسياق | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/attendance/corrections/[id]` | E — الحضور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/attendance/corrections` | E — الحضور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/attendance/history` | E — الحضور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/attendance/sessions/[id]/lesson` | E — الحضور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/attendance/sessions/[id]/players` | E — الحضور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/attendance/sessions/[id]/staff` | E — الحضور | فُتحت | 375 | dark | حالة العرض المتاحة |
| `/dashboard/attendance/sessions` | E — الحضور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/attendance/staff/[id]` | E — الحضور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/attendance/staff/exceptions/[id]` | E — الحضور | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
| `/dashboard/attendance/staff/exceptions` | E — الحضور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/attendance/staff` | E — الحضور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/community-campaigns` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/content/catalog/[id]/edit` | C — النماذج | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/content/catalog/[id]/inventory` | D — الملفات والتبويبات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/content/catalog/[id]` | D — الملفات والتبويبات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/content/catalog/[id]/sell` | D — الملفات والتبويبات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/content/catalog/new` | C — النماذج | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/content/catalog` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/content/nutrition/[id]/edit` | C — النماذج | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/content/nutrition/[id]` | D — الملفات والتبويبات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/content/nutrition/new` | C — النماذج | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/content/nutrition` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/context` | I — حسابي والسياق | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/discounts` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/enrollment-requests/[id]` | D — الملفات والتبويبات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/enrollment-requests` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/evaluations/[id]/edit` | G — التقييم والتطور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/evaluations/[id]/report` | G — التقييم والتطور | فُتحت | 375, 768, 1440 | dark, light | حالة العرض المتاحة |
| `/dashboard/evaluations/badges` | G — التقييم والتطور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/evaluations/badges/rules` | G — التقييم والتطور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/evaluations/certificates/[id]` | G — التقييم والتطور | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
| `/dashboard/evaluations/certificates/sources/[id]` | G — التقييم والتطور | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
| `/dashboard/evaluations/criteria/[id]/edit` | G — التقييم والتطور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/evaluations/criteria/new` | G — التقييم والتطور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/evaluations/criteria` | G — التقييم والتطور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/evaluations/goals/[id]` | G — التقييم والتطور | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
| `/dashboard/evaluations/goals/new` | G — التقييم والتطور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/evaluations/goals` | G — التقييم والتطور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/evaluations/lessons/[id]` | G — التقييم والتطور | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
| `/dashboard/evaluations/lessons` | G — التقييم والتطور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/evaluations/monthly` | G — التقييم والتطور | فُتحت | 375, 1440 | dark, light | حالة العرض المتاحة |
| `/dashboard/evaluations/new` | G — التقييم والتطور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/evaluations` | G — التقييم والتطور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/evaluations/reports` | G — التقييم والتطور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/evaluations/skills/curricula/[id]` | G — التقييم والتطور | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
| `/dashboard/evaluations/skills/journey` | G — التقييم والتطور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/evaluations/skills/new` | G — التقييم والتطور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/evaluations/skills` | G — التقييم والتطور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/evaluations/skills/plans/[id]` | G — التقييم والتطور | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
| `/dashboard/evaluations/skills/plans/[id]/technical-review` | G — التقييم والتطور | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
| `/dashboard/evaluations/technical-reviews/[id]` | G — التقييم والتطور | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
| `/dashboard/evaluations/technical-reviews/[id]/promotion` | G — التقييم والتطور | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
| `/dashboard/evaluations/technical-reviews` | G — التقييم والتطور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/facilities` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | تنبيه قائم: المرافق غير متاحة حاليًا. راجع إتاحة الميزة وصلاحية الفرع. / تعذر تحميل طلبات الزيارة اليومية. |
| `/dashboard/family-renewals/[id]` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/family-renewals/quote` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/feedback/[id]` | D — ملف الملاحظة | فُتحت | 375, 768, 1440 | dark, light | حالة العرض المتاحة |
| `/dashboard/feedback` | D — ملف الملاحظة | فُتحت | 375, 1440 | dark, light | حالة العرض المتاحة |
| `/dashboard/finance/cash-registers` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/finance/cash-registers/sessions/[id]/family` | F — الماليات والاشتراكات | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
| `/dashboard/finance/cash-registers/sessions/[id]` | F — الماليات والاشتراكات | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
| `/dashboard/finance/decisions` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/finance/external-agreements/[id]` | F — الماليات والاشتراكات | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
| `/dashboard/finance/external-agreements/[id]/receipts/[receiptId]` | F — الماليات والاشتراكات | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
| `/dashboard/finance/external-agreements/new` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/finance/external-agreements` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/finance/free-training/[enrollmentId]` | F — الماليات والاشتراكات | فُتحت | 375 | dark | حالة العرض المتاحة |
| `/dashboard/finance/operating-expenses` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/finance/operating-statement` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/finance` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/finance/provider-quarantine/[id]` | F — الماليات والاشتراكات | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
| `/dashboard/finance/provider-quarantine` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/finance/receivables/[id]/credit` | F — الماليات والاشتراكات | فُتحت | 375 | dark | حالة العرض المتاحة |
| `/dashboard/finance/receivables/[id]` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/finance/receivables` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/finance/renewals` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/finance/settlement-comparisons/[id]` | F — الماليات والاشتراكات | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
| `/dashboard/finance/settlement-comparisons/import` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/finance/settlement-comparisons/new` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/finance/settlement-comparisons` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/guardians/[id]/edit` | C — النماذج | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/guardians/[id]` | D — الملفات والتبويبات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/guardians` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/guardians/rewards` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/help/[id]` | D — الملفات والتبويبات | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
| `/dashboard/help` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | تنبيه قائم: هذا الدليل غير متاح ضمن ميزات الأكاديمية وصلاحياتك الحالية.إعادة المحاولة |
| `/dashboard/import` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/matches` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/my-followups/[id]` | D — الملفات والتبويبات | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
| `/dashboard/my-followups/investigations/[id]` | D — الملفات والتبويبات | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
| `/dashboard/my-followups` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | تنبيه قائم: التكليف غير متاح ضمن إسنادك وصلاحياتك الحالية. / المتابعة غير متاحة ضمن إسنادك وصلاحياتك الحالية. |
| `/dashboard/news` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/notifications` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/notifications/reminders` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/operations/capacity` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/operations/decisions` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/operations/follow-ups/[id]` | D — الملفات والتبويبات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/operations/follow-ups/new` | C — النماذج | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/operations/follow-ups` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/payroll/[id]/account` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/payroll/[id]/advances` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/payroll/[id]/opening` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/payroll/[id]/statement` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | تنبيه قائم: مرجع الأساسي القديم غير مؤرخ؛ لا يعني أن تاريخ الرواتب السابق موثق. |
| `/dashboard/payroll/deductions` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/payroll` | F — الماليات والاشتراكات | فُتحت | 375, 768, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/payroll/requests` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/players/[id]/edit` | C — النماذج | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/players/[id]` | D — الملفات والتبويبات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/players/featured` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/players/media/[id]/edit` | H — المعرض والذكريات | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
| `/dashboard/players/media/[id]` | H — المعرض والذكريات | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
| `/dashboard/players/media/memories` | H — المعرض والذكريات | فُتحت | 375, 768, 1440 | dark, light | حالة العرض المتاحة |
| `/dashboard/players/media/new` | H — المعرض والذكريات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/players/media` | H — المعرض والذكريات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/players/medical/[id]/edit` | C — النماذج | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/players/medical/[id]` | D — الملفات والتبويبات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/players/medical/new` | C — النماذج | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/players/medical` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/players/new` | C — النماذج | فُتحت | 375, 768, 1440 | dark, light | حالة العرض المتاحة |
| `/dashboard/players` | B — القوائم والوصول | فُتحت | 375, 768, 1440 | dark, light | حالة العرض المتاحة |
| `/dashboard/players/subscriptions` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/product-sales/[id]` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/reports/attendance` | E — الحضور | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/reports/financial` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/reports/receipts` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/settings` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/settings/sports` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/staff/access-history` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/staff/employees/[id]/contract` | D — الملفات والتبويبات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/staff/employees/[id]/edit` | C — النماذج | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/staff/employees/[id]` | D — الملفات والتبويبات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/staff/employees/[id]/salary` | D — الملفات والتبويبات | فُتحت | 375, 1440 | dark | تنبيه قائم: الراتب القائم مرجع غير مؤرخ للماضي. أول تغيير يثبت مرجعه من الشهر الحالي فقط، ولا يخترع تاريخ رواتب سابقًا. |
| `/dashboard/staff/employees/new` | C — النماذج | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/staff/employees` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/staff` | B — القوائم والوصول | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/subscriptions/collections` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/subscriptions/current` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/subscriptions/expired` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/subscriptions/expiring` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/subscriptions/payments/[id]` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/subscriptions/payments` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/subscriptions/periods/[id]` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/subscriptions/plans/[id]/edit` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/subscriptions/plans/[id]` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/subscriptions/plans/new` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/subscriptions/plans` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/subscriptions/receipts/[receiptId]` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/subscriptions/receipts/[receiptId]/void` | F — الماليات والاشتراكات | فُتحت | 375 | dark | تنبيه قائم: هذا المسار لإيصال اشتراك نقدي مقيد بالصندوق فقط. لا يرحّل إيصالًا تاريخيًا تلقائيًا. |
| `/dashboard/subscriptions/renew/new` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/subscriptions/renewals/[id]` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/subscriptions/renewals` | F — الماليات والاشتراكات | فُتحت | 375, 1440 | dark | حالة العرض المتاحة |
| `/dashboard/tournament-payments/[id]` | D — الملفات والتبويبات | مصدر/مكون | — | — | لا سجل عرض/إتاحة مناسبة؛ لا قبول بصري فردي |
