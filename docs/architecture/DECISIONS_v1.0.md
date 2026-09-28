# Decisions v1.0

**الحالة: توصيات معمارية للمراجعة وليست تعديلاً للمتطلبات.** لا يعني اعتماد baseline أن البدائل المفتوحة حُسمت.

| القرار | default موصى به وسببه | المتطلبات المتأثرة | آخر موعد للحسم |
|---|---|---|---|
| OD-001 | demo cut: الأساس الآمن، التسجيل، مدة شهرية، تحصيل يدوي، حضور، تقييم، guardian core، nutrition؛ تحفظ الحصص/combined والتجميد/الأيام/الإلغاء/الخصم/التصدير والإيصال والاتصالات كبنود لاحقة ظاهرة بالخطة لا أزرار وهمية. يقلل المخاطر دون حذف. | AC-GOV-003، AC-SUB-002/008..011، AC-ATT-006، AC-FIN-004، AC-COM-001..004 | قبل تثبيت نطاق demo بعد foundation |
| OD-002 | PWA أولًا من Next.js واحد؛ native client لاحقًا على API نفسه. أسرع للديمو ومتسق مع الهاتف. | AC-DEL-003، AC-IAM-001 | مع اعتماد هذه المعمارية وقبل frontend scaffold |
| OD-003 | admin creates guardian/link؛ OTP موثوق للرقم للتفعيل والاستعادة؛ عدة guardians مسموحة بعلاقات منفصلة، بلا ربط من الهاتف وحده. لا production activation حتى اختيار provider. | AC-IAM-006/007, AC-PLY-002 | قبل Slice 1 auth |
| OD-004 | duration بالأيام التقويمية المنتهية شاملًا؛ التجديد المبكر يبدأ بعد النهاية، والمنتهي يبدأ من تاريخ التأكيد؛ combined ينتهي عند أول حد يصل إليه؛ freeze يوقف الحضور ويمدد المدة بالأيام المعتمدة، والحصة تخصم على Present فقط. default موثق يحتاج أمثلة قبول. | AC-SUB-002/007/008, AC-ATT-005 | قبل Slice 3 |
| OD-005 | تحصيل يدوي يؤكده موظف مخول؛ خصم واحد amount أو percent بسبب إلزامي؛ reversal بقيد عكسي. تجديد الغير عبر reference opaque single-use منتهي، بلا بحث أسماء عام؛ لا gateway الآن. | AC-SUB-006/011, AC-FIN-003/005 | قبل Slice 3 |
| OD-006 | ولي الأمر يرسل طلب enrollment ويختار الرياضة/الفرع لا المجموعة؛ الإداري يطابق الطفل عبر guardian link ويعتمد المجموعة. يمنع duplicate/self-assignment الخاطئ. | AC-PAR-005, AC-PLY-004/006 | قبل Slice 6 |
| OD-007 | default مؤقت: متوسط موزون للدرجات المنشورة غير الناقصة، criterion يرتبط بمحور ووزن؛ إظهار completeness، ولا report summary غير football قبل template معتمد. | AC-EVA-003/005/008/009/010 | قبل Slice 5 calculation |
| OD-008 | أول إصدار يعرض catalog فقط؛ item فردي، الأكاديمية ناشر المحتوى لا بائعًا إلكترونيًا، ولا cart/favourites/ratings/order. تظهر الرياضة من enrollment active أو expiring فقط. | AC-SPP-002/004, AC-PAR-003 | قبل Slice 7 products |
| OD-009 | seed مواد demo بحقوق استخدام واضحة؛ كل قيمة غذائية تحمل serving/source/review status، وغير المراجع موسوم "بيانات تجريبية غير مراجعة"؛ شاشة تحرير بسيطة تؤجل بعد العرض الأول. | AC-NUT-005/008/010 | قبل نشر Slice 7 |
| OD-010 | medical write لمالك/إداري بpermission طبي منفصل، publish للGuardian؛ coach بلا وصول افتراضي. gallery للاعب فقط أولًا، upload للإداري مع مراجعة. | AC-MED-001/002, AC-GAL-001/002/003 | قبل Slice 7 |
| OD-011 | القسم يعرض training schedule فقط في الإصدار الأول؛ event/match entity لا يضاف حتى اعتماد حقوله. | AC-PAR-008 | قبل توسيع Slice 6 |
| OD-012 | لا نستنتج وظائف من أسماء القوائم؛ receipt HTML/PDF browser-print أولًا، وكل users/contacts/support/bot/message/ranking slice يحتاج specification وقبول مستقل. | AC-FIN-004, AC-COM-001..005 | قبل كل وظيفة مرجعية |

لا يوجد قرار بيزنس يمنع **Slice 0 foundation**. يلزم صلاح اعتماد `OD-002` قبل تثبيت PWA، و`OD-003` قبل مسار الهوية الكامل. بقية القرارات مؤجلة إلى البوابة المبينة ولا تمنع الأساس.
