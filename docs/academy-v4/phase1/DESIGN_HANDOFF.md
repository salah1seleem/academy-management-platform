# تجهيز الربط بالتصميم النهائي — I Academy One

21 أكتوبر 2026. لا تعديل Flutter أو Next/CSS في هذه المرحلة. مصدر طلب المرحلة هو المحادثة الحالية؛ تعليمات الشكل والتنقل داخل المرفق مؤجلة. قائمة 00–25 في CHECKLIST.md هي نقطة الاستئناف.

فُحص فهرس ZIP ومصدر الملفات الخمسة Premium وملفا المراجعة/التكليف. لم تُعرض شاشات ZIP في متصفح بهذه الجولة، ولم تُختبر تفاعلاته أو تُنسب نتائج مراجعة الـ62 انتقالًا إلى المنتج الحقيقي. الأرقام والأسماء والصور ونجاحات JavaScript في المرجع ليست بيانات التطبيق ولا سياسة الأعمال. أُبقي الأرشيف خارج المستودع.

| المرجع المبدئي / النقاط | التنفيذ القائم الذي يُحتفظ به | المكونات والخدمة القابلة لإعادة الاستخدام | الربط/الإجراء المتبقي بعد التصميم |
|---|---|---|---|
| Premium Owner / 01–07 | `apps/mobile/lib/features/owner/owner_dashboard.dart`، `owner_cockpit.dart`، `refined_home_panels.dart`؛ ويب `/dashboard` | collection_target، home_destinations، coach_rating_leaders، member_values؛ API `Slice8/OwnerCockpit.cs` وoperating-statement | تنسيق الملخص والرسوم والبطاقات؛ إعداد موضع الإجراءات وهدف الإسناد يحتاجان تنفيذًا وظيفيًا مستقلًا، لا تغيير الأرقام |
| Owner/Admin Operations / 08،25 | `owner_operations.dart`، `owner_quick_operations.dart`، ويب `/dashboard/players/new` | API `POST /people/registrations` و`POST /people/registration-quote`؛ نفس PlanCatalog/DiscountRules/CollectionService | نماذج أصلية داخل الموبايل للعمليات التي تفتح الويب حاليًا؛ اختيار الخصم ومراجعة السعر من الخادم وإرسال key ثابت |
| Matches / 09 | `features/shared/match_experience.dart`، ويب `/dashboard/matches` | MatchEndpoints، roster، MatchParticipant وجمهوره المجمد | بطاقات الفريقين والنتيجة وأقرب مباراة؛ نماذج الإداري الأصلية، دون حذف فرق/بطولات أو إنشاء نتائج بالاستيراد |
| Reports / 05،10 | `owner_reports.dart`؛ `/dashboard/reports/financial`، `/dashboard/subscriptions/collections` و`/dashboard/finance` | مصادر التحصيل والتكاليف والمطالبات الحالية؛ لا محرك مالية ثانٍ | دمج العرض والفلاتر مع الحفاظ على الإيصال والتصدير والمراجعات |
| Staff / 11،16 | ويب `/dashboard/staff` و`/dashboard/staff/employees`؛ `native_staff_attendance.dart`، staff_work_periods، salary_history | StaffWorkflowAccess/StaffGrant/StaffAccess audit؛ attendance proof/exception | موظفون وصلاحيات أصلية بالموبايل، تسمية سجل الحضور، نقل وظائف الحساب للأماكن المعتمدة دون إزالتها |
| Web Settings / 12 | `/dashboard/settings` و`/dashboard/settings/sports` | Academy configuration versions وexperience؛ لا مخزن إعدادات بديل | تبويبات وتحقيق save/reload/effect لكل إعداد؛ فيديوهات غير المقدمة تبقى حالة صادقة |
| News/Ads / 13 | `/dashboard/news`؛ `shared/community_ads.dart` | News targeting/publication وCommunityCampaigns | نماذج نشر أصلية واستهداف مطابق؛ لا إعادة استخدام بيانات prototype |
| Premium Coach / 14–16 | `coach_home.dart`، coach_evaluation، session_context، monthly_stars | APIs التقييم المنشور وميزانية النجوم والحضور المقيد | اختيار اليوم، أقرب جلسة/مباراة، تأكيد المعايير غير المعدلة؛ لا منح حضور الأطفال تلقائيًا |
| Premium Admin / 17 | `features/admin/admin_home.dart`، ملف اللاعب والإجراءات القائمة | نفس people/registration/attendance/matches APIs والنطاق الموثوق | تفاصيل/تنقل ونماذج أصلية حسب أفعال الإداري الفعلية |
| Premium Guardian / 18–21 | guardian_home، guardian_profile، family_renewal_request/quote، guardian_content، daily_nutrition؛ shared/media_memories | ارتباط الأطفال وSportEnrollment منفصل، family approval ذرية، وسائط خاصة | Carousel واختيار الطفل والرياضة، ترتيب العناصر، رفع وسائط من سياق الحصة؛ جميع وجهات المكافآت والشراء والتواصل محفوظة |
| Notifications / 22 | `shared/notification_inbox.dart`؛ ويب `/dashboard/notifications` | NoticeTemplate/NoticeDispatch/AcademyNotice؛ API `/notifications` و`/manage/notifications` | تغطية الإنشاء/التجديد/إضافة وخصم الأيام/غياب الحصة منفردة غير مثبتة بالكامل؛ قوالب/تمكين/توقيت/تاريخ. لا تدعي تسليم push/SMS |
| Products / 23 | `guardian/sport_store.dart`؛ `/dashboard/content/catalog` | ProductSale والحجز والمخزون والتحويل الحالي | لم نجد مخزونًا مفصلًا للمقاس ورقم القميص؛ يحتاج امتدادًا للمنتجات والطلب تحت slice محددة، لا إعادة تصميم مخزن |
| Connected Import / 24 | ويب `/dashboard/import` القديم محفوظ؛ API `/people/import` | نفس ImportXlsx/Preview/Job/Worker/Commit؛ تاريخ الاستيراد القديم مدعوم | وصل زرّي connected-template/connected-sample وعرض effects/coachAssignments/tournamentParticipations؛ النموذج الجديد لم يربط بالواجهة في هذه المرحلة؛ معاينة roster للمباراة تدعم tournamentId لعضوية البطولة المصرح بها |

## عقد الربط للخصم

داخل RegistrationRequest/RegistrationQuote حقل اختياري `exceptionalDiscount` يحتوي `type` (1 نسبة،2 قيمة حسب RenewalDiscountType)، `value`، `reason`، `replaceExistingDiscount`. تُعرض الأسماء العربية فقط في الواجهة. لا يرسل العميل مبلغًا نهائيًا بوصفه سلطة؛ ExpectedAmount للمراجعة ويعيد الخادم الحساب. يعرض `my-access.workflowActions` الفعل `workflow.subscriptions.exceptional-discount` بعنوان «منح خصم استثنائي»؛ الإداري مرفوض دون منح صريح وصلاحيات الوحدة والتسجيل والتحصيل. الإعفاء الكامل للمالك فقط وفق السياسة الحالية. حفظ key للعملية يمنع تطبيق الخصم/التحصيل مرتين. السبب والتدقيق داخليان حسب صلاحيات السجل، والأسرة ترى تفصيل السعر المخول.

## عقد الربط للاستيراد

`GET /api/v1/people/import/connected-template` و`connected-sample` يعيدان ملفات XLSX الفعلية. الرفع إلى `preview` والتأكيد إلى `commit` بنفس عقد multipart الحالي؛ الملف الأصلي محفوظ مؤقتًا في Job حتى النشر أو انتهاء المدة. `ExpectedFileHash` و`ExpectedReferenceStamp` من المعاينة، واعتماد روابط الأسرة/الأرصدة/المالية المجهولة حسب الحالة، وIdempotency-Key ثابت.

الأخطاء: الورقة والصف والعمود والقيمة المعطوبة ورسالة عربية. الرد يعرض `effects` إنشاء/إعادة استخدام لكل ورقة، وعدد إسنادات المدربين ومشاركات البطولات. نتيجة Job تحفظ هذه الأعداد دون ملف الرفع أو هواتف الأسر في polling. لا زر نجاح قبل Completed.

التنفيذ المتصل الكامل متاح للمالك أو إداري بكل فروع/رياضات الأكاديمية وصلاحيات القراءة والإضافة للهيكل والموظفين واللاعبين والاشتراكات والمباريات. الإداري محدود الفروع يحتفظ باستيراد التسجيلات التاريخي القديم ضمن نطاقه؛ لا يُوسع الاستيراد سياقه لإنشاء هيكل مشترك. الخاص/شبه الخاص يتطلب مجموعة قائمة بمستوى وسعة ومورد معتمد؛ لا يخترع الملف حجز مورد غير معرّف.

## عند وصول التصميم النهائي

1. ثبّت نسخة التصميم الجديدة ومصدرها؛ طابقها مع كل صف 00–25 والحالة والأدلة بهذه المرحلة.
2. راجع تغيرات الأعمال فقط وسجلها بإصدار متطلبات جديد؛ غياب عنصر بصري لا يلغي وظيفة صحيحة.
3. نفذ الموبايل الأصلي أولًا ثم الويب المطلوب، مع إعادة استخدام الخدمات أعلاه.
4. اختبر دورًا بدور على هاتف صغير واعتيادي وويب؛ حفظ/إعادة تحميل/أخطاء وعزل، ولقطات مقارنة فعلية. اختبارات API الحالية لا تعني اجتياز رحلة Flutter أو التطابق البصري.
