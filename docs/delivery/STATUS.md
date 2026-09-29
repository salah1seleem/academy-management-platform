# Status

تاريخ التحديث: 2026-09-29. الفرع: `codex/10-slice8a-reports-exports`.

## Slice 8A — منفذ فعليًا

- لوحة مالك عربية RTL مشتقة من السجلات المخزنة: التحصيل الكلي/اليومي/الشهري، حالات طلبات الدفع، الاشتراكات الفعالة/القريبة/المنتهية، اللاعبين النشطين، حضور اليوم، breakdown حسب الرياضة والفرع، وأحدث التحصيلات. لا constants مالية ولا trends مختلقة.
- تقرير مالي Owner/Admin من `Collections` المؤكدة فقط، بفلاتر التاريخ والرياضة والفرع والباقة والمزود/الطريقة والبحث، pagination، ومجموع مطابق للنتيجة. حالات الدفع المعلقة/الفاشلة/الملغاة/المنتهية لا تُحسب إيرادًا.
- تقرير حضور Owner/Admin، وCoach داخل المجموعات المسندة فقط، مع فصل اللاعبين عن الجهاز الفني وفلاتر الشهر/السنة أو الفترة والفرع والرياضة والمجموعة والحالة والبحث وسنة الميلاد. التغطية `StoredRecordsOnly` ولا يُستنتج الغياب من سجل مفقود.
- تصدير CSV فعلي مطابق للفلاتر، UTF-8 BOM، أسماء ملفات متوقعة وامتداد صحيح، tenant/resource authorization، حد 5000 صف، وحماية spreadsheet formula injection. لا تُصدّر هواتف أو بيانات طبية أو أسرار دفع.
- قائمة إيصالات Owner/Admin وقائمة دفع خاصة بولي الأمر، وصفحة «إيصال دفع» عربية تحفظ snapshots التاريخية وتدعم browser print. ليست PDF ولا فاتورة ضريبية.
- صلاحيات مستقلة: `OwnerDashboardRead`, `ReportsFinancialRead`, `ReportsAttendanceRead`, `ReportsExport`. Guardian بلا تقارير أكاديمية، وCoach بلا مالية/لوحة مالك.
- migration forward-only `20260928234906_Slice8ReportingIndexes`: ثلاثة فهارس فقط على التحصيل/حالة الدفع/تاريخ الميلاد؛ لا reporting warehouse أو totals table.
- Demo حتمي أضاف لاعب 2019 وسجل حضور واحد idempotent فقط. لم يكرر تحصيلات، وظل `DemoReferenceDate=2026-09-28` وAcademy B لاختبارات العزل.

## الأمن والاتساق

- tenant يأتي من جلسة الخادم؛ كل report/export/receipt academy-scoped. اختبارات IDOR والعزل والصلاحيات وتلاعب الفلاتر وحدود التاريخ وCSV safety ضمن بوابة Slice 8A.
- `Dashboard collections total = sum confirmed Collections = receipts sum`، ومجموع الفترة يطابق التقرير والتصدير لنطاقه. Nutrition/catalog/medical/media لا تنشئ أثرًا ماليًا.
- `OD-001` و`OD-012` ما زالا **PENDING / NOT APPROVED**. ما نُفذ هو نطاق Slice 8A فقط وdefault HTML browser-print قابل للعكس.

## دليل التحقق

- `dotnet restore` وRelease build ناجحان: **0 warnings / 0 errors**. migration طُبقت على PostgreSQL، وEF pending-model check أكد عدم وجود تغييرات غير مهاجرة.
- Backend كامل: **219/219** (`217 integration + 1 unit + 1 architecture`)؛ منها **32/32** اختبار PostgreSQL مخصصًا للتقارير/التصدير والأمن والاتساق.
- Frontend: typecheck وlint وproduction build (**42 صفحة**) ناجحة؛ **64/64** UI tests، و`npm audit --omit=dev` = **0 vulnerabilities**.
- Mobile Chromium E2E: **27/27** من قاعدة محلية معزولة ونظيفة، وتشمل كل الرحلات السابقة وأربع رحلات Slice 8A الفعلية. نتيجة GitHub Actions تُسجل بعد الدفع ولا تُفترض مسبقًا.

## مؤجل صراحة

التجميد وتعديل الأيام والإلغاء والخصومات، messages/bot/ranking، PDF generation، تقارير AI/advanced BI، تجارة المنتجات/التغذية، production media، provider/SMS حقيقي، والنشر/production readiness. لا يبدأ Slice 8B أو نطاق آخر هنا.

## نقطة التوقف

تتوقف المهمة عند Slice 8A بعد الدفع ونجاح CI. لا يُدمج أو يُعدل `main`.
