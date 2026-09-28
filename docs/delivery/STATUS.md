# Status

تاريخ التحديث: 2026-09-29. الفرع: `codex/07-slice5-evaluations`.

## Slice 5 — منفذ فعليًا

- أضيفت `EvaluationCriteria`, `PlayerEvaluations`, و`EvaluationScores` tenant-scoped. العلاقات المركبة تثبت Academy/Sport/Enrollment/Group؛ unique يمنع criterion مكررًا في تقييم، وDB تقيد score إلى null أو 0–100 والوزن إلى أكبر من صفر.
- Owner/Admin يديران معايير الرياضة وتفعيلها. المحور السداسي اختياري ومسموح لكرة القدم فقط. إيقاف معيار مستخدم يحفظ التاريخ؛ لا hard delete.
- Owner/Admin يريان الأكاديمية، والمدرب يرى ويقيّم وينشر لاعبي مجموعاته المسندة فقط. Guardian يرى Published لطفله المرتبط فقط؛ Draft وموارد طفل/أكاديمية أخرى تعيد 404/403.
- المسودة تحمل criteria الفعالة وتقبل null وnotes، والحفظ المتكرر يحدث نفس السجل. `xmin`/Version يكشف stale edit. النشر صريح ويتطلب درجة واحدة، ويسجل الناشر والوقت، وبعده يمنع التعديل المباشر. revision→supersede مؤجل ولا overwrite للتاريخ.
- الاسم والوزن والمحور snapshots داخل الدرجة؛ تعديل criterion لاحقًا لا يغير التقرير المنشور. الحساب الخادمي فقط عبر `IEvaluationReportCalculator`: weighted average للدرجات المتاحة في كل محور، ومتوسط المحاور المتاحة للإجمالي، missing ≠ 0، وتقريب منزلة عشرية واحدة؛ completeness منفصلة. `OD-007` ما زال **PENDING / NOT APPROVED**.
- واجهة عربية RTL وموبايل أولًا تضيف «التقييمات»: معايير التقييم، تقييمات اللاعبين، والتقارير المنشورة. نموذج الدرجات بطاقات لمس 0–100 بلا stars. تقرير كرة القدم يعرض اللاعب/المركز/الإجمالي، radar سداسي responsive مع قيم نصية accessible، العمر والطول والوزن والقدم وكل المعايير والملاحظات. لا pitch diagram ولا AI. السباحة تعرض تقريرًا تفصيليًا بلا radar كرة قدم.
- migration forward-only: `20260928214148_Slice5PlayerEvaluations`. لم تتغير migrations السابقة.
- Demo حتمي بتاريخ `2026-09-28`: 18 معيار كرة قدم عبر المحاور الستة، 7 معايير سباحة مستقلة، تقييمان كرة قدم منشوران للتاريخ، مسودة مخفية عن Guardian، تقييم سباحة منشور، وسجل أكاديمية ثانية للعزل. إعادة seed idempotent ومقصورة على `Demo`.

## مراجعة الأمن والتاريخ

- AcademyId لا يؤخذ من العميل؛ tenant من الجلسة. IDOR على evaluation/enrollment/criterion/player محجوب، والمدرب مقيد بـ`StaffGroupAssignment` وGuardian بـ`GuardianPlayerLink`.
- الخادم يرفض cross-tenant/cross-sport criterion، score خارج الحدود، duplicate criterion، نشر بلا درجة، وتعديل Published أو stale Draft. العميل لا يرسل overall/axes/completeness كحقائق؛ الخادم يحسبها من snapshots.
- لا تغيير في الاشتراكات أو التحصيل أو الحضور أو روابط الوصاية. Nutrition/Product/Medical/Gallery/Communications/AI Reports لم تبدأ.

## دليل التحقق الحالي

- Release build: ناجح، 0 warnings / 0 errors. migration طُبقت على PostgreSQL 17 مؤقت، و`has-pending-model-changes` أكد تطابق النموذج.
- اختبارات Slice 5 الحرجة: 25/25 PostgreSQL. المجموعة الكاملة: 128/128 (`126 integration + 1 unit + 1 architecture`) مع بقاء الدفع والحضور والعزل خضراء.
- Frontend: typecheck وlint وproduction build ناجحة؛ UI tests هي 21/21، منها 11 لتجربة Slice 5 والقائمة؛ `npm audit` صفر vulnerabilities. Mobile Chromium E2E هي 15/15، منها ثلاث رحلات تقييم حقيقية للمدرب وولي الأمر والعزل. نتيجة remote CI تسجل بعد الدفع ولا تفترض مسبقًا.

## مؤجل صراحة

اعتماد معادلة `OD-007`، revision/supersede UI، مقارنة الفترات/الاتجاه/الألوان، template رسومي للسباحة، Nutrition، Sport Products، Medical، Gallery، Communications، advanced BI، attendance exports، provider دفع/SMS حقيقيان، والنشر الإنتاجي. لا production-readiness claim.

## نقطة التوقف

تتوقف المهمة عند Slice 5. لا يبدأ Slice 6، و`main` لا يُدمج أو يُعدل ضمن هذه المهمة.
