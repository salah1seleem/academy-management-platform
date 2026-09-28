# Status

تاريخ التحديث: 2026-09-29. الفرع: `codex/09-slice7-content`.

## Slice 7 — منفذ فعليًا

- أضيف كتالوج رياضي **للعرض فقط** ومربوط بالرياضات ذات التسجيل النشط لأطفال ولي الأمر. تُجمع الرياضة مرة واحدة ولو تكررت بين الأطفال، ولا توجد سلة أو طلب أو checkout أو دفع أو مخزون أو مفضلة أو تقييمات.
- أضيفت مكتبة تغذية معلوماتية عامة من قاعدة البيانات، بتبويبات الإفطار والغداء والعشاء وتفاصيل الحصة والسعرات والبروتين والكربوهيدرات والدهون وحالة المصدر. بيانات Demo موسومة بوضوح «بيانات تجريبية غير مراجعة» وليست وصفة شخصية أو حقيقة غذائية معتمدة.
- أضيفت سجلات `Injury|Consultation` للاعب، مع فصل `StaffNotes` عن الملاحظات المرئية، وحالات `Open|Monitoring|Resolved` ونشر صريح. Owner/Admin يديرانها، وGuardian يرى المنشور فقط لطفله المرتبط، وCoach بلا وصول طبي.
- أضيف معرض `PlayerMedia` كـmetadata للاعب ونوع `Image|Video` ونشر صريح. Guardian يرى المنشور فقط لطفله المرتبط. الأصول الحالية SVG محلية اصطناعية تحت `/demo-assets/`؛ لا upload أو object storage إنتاجي.
- أضيفت شاشات إدارة عربية للكتالوج والتغذية والسجلات الطبية والمعرض، مع الإنشاء والعرض والتعديل والتفعيل/النشر والفلاتر الملائمة. لا حذف تاريخي ولا حقول تجارة تغذية.
- دمجت الأقسام في رئيسية ولي الأمر وملف الطفل بترتيب التقرير، التدريب، التغذية، الطب، المعرض، ثم التجديد، مع حالات فارغة صريحة.
- migration forward-only: `20260928225824_Slice7GuardianContent`، وتضيف فقط `SportCatalogItems`, `NutritionItems`, `NutritionCategoryLinks`, `PlayerMedicalRecords`, و`PlayerMedia`.
- Demo حتمي ومحدد النطاق: 8 منتجات فعالة لرياضتين، 15 مادة تغذية فريدة في 18 موضع تصنيف، سجل طبي منشور وآخر غير منشور، صورتان منشورتان وعنصر غير منشور، وبيانات Academy B لاختبارات العزل. إعادة seed idempotent ولا تمس بيانات Production.

## مراجعة الأمن والاتساق

- tenant يُستمد من جلسة الخادم ولا يؤخذ من العميل. كل العلاقات والقراءات والكتابات academy-scoped، وكل قراءة Guardian للاعب تتطلب `GuardianPlayerLink` نشطًا؛ اختبارات Academy B وchild IDOR خضراء.
- صلاحيات مستقلة للقراءة والإدارة لكل من catalog/nutrition/medical/media؛ DTOs محددة تمنع overposting. `StaffNotes` لا يدخل DTO الخاص بولي الأمر، وCoach محجوب عن الطب وإدارة المحتوى.
- مراجع الوسائط المقبولة في Slice 7 مقيدة بأصول المشروع المحلية؛ تمنع traversal وURLs الخارجية. هذا ليس بديلًا عن بوابة تخزين Production الخاصة وفحص الملفات والاحتفاظ والروابط المؤقتة.
- نموذج `Player` ما زال منفصلًا عن `SportEnrollment`؛ أهلية الكتالوج تأتي من التسجيل النشط ولا تكرر الطفل أو الرياضة. وحدات التغذية والكتالوج والطب والمعرض مستقلة ولا تُنشئ أثرًا ماليًا.
- `OD-008` و`OD-009` و`OD-010` ما زالت **PENDING / NOT APPROVED**؛ ما نُفذ defaults تقنية قابلة للعكس فقط. لم تُخترع موافقة على بائع/شراء أو مصدر غذائي أو سياسة ملفات إنتاجية.

## دليل التحقق الحالي

- `dotnet restore` وRelease build ناجحان: 0 warnings / 0 errors. migration مطبقة محليًا على PostgreSQL، وEF pending-model check أكد عدم وجود تغييرات غير مهاجرة.
- Backend كامل: **187/187** (`185 integration + 1 unit + 1 architecture`)؛ منها **31/31** اختبار PostgreSQL مخصصًا لـSlice 7، مع بقاء auth/tenancy/subscriptions/payments/attendance/evaluations/guardian خضراء.
- Frontend: typecheck وlint وproduction build (38 صفحة) ناجحة؛ **50/50** UI tests، و`npm audit --omit=dev` = 0 vulnerabilities.
- Mobile Chromium E2E: **23/23** في التحقق النهائي، وتشمل كل الرحلات السابقة وأربع رحلات Guardian فعلية لـSlice 7. نتيجة GitHub Actions تُسجل بعد الدفع ولا تُفترض مسبقًا.

## مؤجل صراحة

تجارة المنتجات، seller responsibility، favourites/ratings، أي تجارة أو تخصيص للتغذية، مصادر غذائية معتمدة، production media upload/storage، communications/messages، AI/advanced reports، مزود دفع أو SMS حقيقي، Apple Pay، والنشر الإنتاجي. لا production-readiness claim.

## نقطة التوقف

تتوقف المهمة عند Slice 7. لا يبدأ Slice 8، و`main` لا يُدمج أو يُعدل ضمن هذه المهمة.
