# Academy Management Platform

منصة مستقلة عربية أولاً لإدارة الأكاديميات. الحالة الحالية هي **Slice 8B: تعديلات الاشتراك الموثقة**.

## ما الموجود الآن؟

- ASP.NET Core Web API مع `/health/live` و`/health/ready`.
- EF Core وNpgsql مع ASP.NET Core Identity وعضويات Academy معزولة وجلسات browser محفوظة على الخادم.
- Next.js login وauthenticated shell عربيان RTL، mobile-first، مع تبديل Academy مخوّل من الخادم وPWA manifest/icon.
- PostgreSQL 17 محلي عبر Docker Compose.
- Demo seed حتمي لأكاديميتين، وOTP ثابت محروس ببيئة `Demo` فقط، واختبارات عزل backend وE2E.
- فروع ورياضات وفئات ومجموعات وجداول أسبوعية وإسناد مدرب، مع Player مستقل عن `SportEnrollment` وروابط وصاية صريحة.
- Dashboard عربي RTL وموبايل أولًا: sidebar ثابت، قوائم، بحث وفلاتر، وتسجيل لاعب/ولي أمر/تسجيل رياضي في transaction واحدة.
- باقات `Duration/Sessions/Combined` وفترات تاريخية، وتجديد ولي الأمر لنفس أبنائه أو لمستفيد آخر عبر كود آمن وOnline-first، مع `PaymentRequest` وevents وتحصيل وإيصال ذريين idempotent.
- `TrainingSession` فعلية مولّدة دون تكرار من الجدول الأسبوعي أو منشأة يدويًا، وحضور لاعبين وجهاز فني منفصلان. `Present` يخصم حصة واحدة فقط من `Sessions/Combined` المؤهل، والتصحيح يعيدها بحركات audit append-only.
- تعديلات فترة الاشتراك لـOwner/Admin: تجميد/استئناف `Duration` و`Combined`، إضافة/خصم الأيام، وإلغاء فترة واحدة، مع سجل `SubscriptionAdjustment` append-only وidempotency وتعارض متفائل. لا تعديل لتحصيل أو إيصال ولا refund ضمن هذه الإجراءات، وولي الأمر يرى الحالة المبسطة لطفله المرتبط.
- معايير تقييم خاصة بالرياضة، مسودات ودرجات 0–100 ونشر immutable. تقرير كرة القدم يحسب ستة محاور من snapshots الخادم ويعرض radar وقيمًا نصية وتاريخًا لولي الأمر؛ السباحة لها criteria مستقلة بلا radar كرة قدم.
- رئيسية ولي أمر عربية mobile-first تجمع الطفل مرة واحدة وتعرض رياضاته دون تكرار، مع الإجراءات الثلاثة: اشتراك جديد، تجديد، وتجديد للغير. ملف الطفل يعرض التقرير المنشور والجدول والحضور والاشتراكات من البيانات المحفوظة.
- `NewEnrollmentRequest` لطلب رياضة جديدة لطفل مرتبط أو طفل جديد؛ الطلب وحده لا ينشئ لاعبًا أو تسجيلًا أو دفعًا. Owner/Admin يراجع ويختار المجموعة ثم ينشئ الرابط والتسجيل ذريًا دون اشتراك مدفوع.
- كتالوج رياضي للعرض فقط مرتبط برياضات الأبناء دون تكرار؛ السعر والخصم الاختياريان metadata معلوماتية ولا توجد سلة أو طلب أو checkout أو دفع أو تقييمات.
- مكتبة تغذية عامة من قاعدة البيانات: 15 مادة و18 موضعًا عبر الإفطار والغداء والعشاء، مع الحصة والقيم وحالة المصدر. كل القيم التجريبية موسومة «بيانات تجريبية غير مراجعة»، ولا توجد أي حقول أو endpoints تجارة غذائية.
- سجلات إصابة/استشارة factual ينشئها Owner/Admin وينشرها صراحة؛ Guardian يرى المنشور فقط لطفله المرتبط ولا يرى `StaffNotes`، وCoach بلا وصول طبي. معرض الطفل يتبع قاعدة النشر والربط نفسها.

المتطلبات الحاكمة في [`docs/requirements/CURRENT_REQUIREMENTS.md`](docs/requirements/CURRENT_REQUIREMENTS.md)، وحالة التنفيذ الدقيقة في [`docs/delivery/STATUS.md`](docs/delivery/STATUS.md).

## 1. المتطلبات على الجهاز

- Git.
- .NET 10 SDK؛ تم التحقق باستخدام `10.0.401`.
- Node.js 24 LTS وnpm؛ تم التحقق باستخدام Node `24.21.0` وnpm `11.19.0`.
- Docker Desktop أو Docker Engine مع Compose لتشغيل PostgreSQL 17.

افتح Terminal ونفّذ كل الأوامر التالية من جذر المستودع:

```bash
cd "/Users/omar/Downloads/Academy Management Platform "
```

## 2. الإعداد لأول مرة

أنشئ ملف الإعداد المحلي. الملف `.env` مستبعد من Git:

```bash
cp .env.example .env
set -a
source .env
set +a
```

استعد الأدوات والحزم المقفلة:

```bash
dotnet tool restore
dotnet restore AcademyManagementPlatform.slnx
npm ci
```

## 3. تشغيل PostgreSQL

تأكد أن Docker يعمل، ثم:

```bash
docker compose --env-file .env -f infra/local/compose.yaml up -d postgres
docker compose --env-file .env -f infra/local/compose.yaml ps
```

لا تستخدم `down -v` إلا إذا كنت تقصد حذف بيانات قاعدة التطوير المحلية.

## 4. تطبيق migrations يدويًا

لا يطبق API migrations تلقائيًا عند التشغيل. بعد تحميل `.env` نفّذ:

```bash
dotnet ef database update \
  --project apps/api/src/Academy.Infrastructure \
  --startup-project apps/api/src/Academy.Api
```

آخر migration هي `20260929005032_Slice8BSubscriptionAdjustments`. تضيف `SubscriptionAdjustments` و`SubscriptionPeriods.FrozenFromDate` بقيود tenant/audit/idempotency، ولا تغير جداول التحصيل أو الإيصالات أو المبالغ المدفوعة.

## 5. تشغيل Demo آمن محليًا

بيانات العرض الصناعية موثقة في [`docs/demo/SLICE1_DEMO_ACCOUNTS.md`](docs/demo/SLICE1_DEMO_ACCOUNTS.md). لا تنسخها إلى Production. عدّل `.env` محليًا فقط:

```bash
ASPNETCORE_ENVIRONMENT=Demo
Demo__SeedEnabled=true
Demo__FixedOtpEnabled=true
Demo__FixedOtp=246810
Demo__StaffPassword='Demo-Only-123!'
Demo__ReferenceDate=2026-09-28
Payments__InternalTest__Enabled=true
Payments__InternalTest__SigningKey='Demo-Test-Signing-Key-Only-123456'
```

يرفض API البدء إذا فُعّل seed/fixed OTP أو بوابة الدفع الداخلية خارج بيئة مسموحة. بوابة الاختبار لا تحرك أموالًا حقيقية، ولا تعرض أو تحاكي Apple Pay أو provider إنتاجيًا. لا توجد خدمة SMS أو payment provider فعلية في هذه الشريحة.

## 6. تشغيل API

في Terminal أول، من جذر المستودع:

```bash
set -a
source .env
set +a
dotnet run --project apps/api/src/Academy.Api --no-launch-profile
```

تحقق من الصحة في Terminal آخر:

```bash
curl -i http://127.0.0.1:5080/health/live
curl -i http://127.0.0.1:5080/health/ready
```

`live` يجب أن ينجح ما دامت عملية API تعمل. `ready` ينجح فقط عندما يستطيع API الاتصال بـPostgreSQL.

## 7. تشغيل Web/PWA

في Terminal مستقل، من جذر المستودع:

```bash
export PATH="/opt/homebrew/opt/node@24/bin:$PATH"
npm run dev:web -- --hostname 127.0.0.1
```

افتح `http://127.0.0.1:3000/login`. ادخل كولي الأمر «سارة محمود» بالهاتف التجريبي، وشاهد عمر مرة واحدة مع كرة القدم والسباحة، ثم كتالوج الرياضتين مرة واحدة بلا أزرار شراء. افتح ملف عمر ثم «التغذية والصحة» لعرض التبويبات والشكشوكة وحالة البيانات، و«الإصابات والاستشارات الطبية» لعرض السجل المنشور فقط، و«الصور والفيديوهات» لعرض الرسومات المحلية المنشورة. مريم تعرض الحالات الفارغة الطبية/المعرض. ادخل كإداري وافتح «المحتوى» لإدارة الكتالوج والتغذية، و«اللاعبون» لإدارة السجلات الطبية وmetadata المعرض. `OD-008` و`OD-009` و`OD-010` ما زالت غير معتمدة.

لتجربة Slice 8A، ادخل كمالك وافتح «لوحة المالك»، ثم «التقرير المالي» لتصفية التحصيلات المؤكدة حسب الفترة/الرياضة/الفرع/الباقة وتنزيل CSV عربي حقيقي. كإداري افتح «التقارير ← تقارير الحضور»، اختر سبتمبر 2026 وسنة الميلاد 2019 ومجموعة كرة القدم، ثم صدّر CSV؛ يعرض التقرير سجلات الحضور المحفوظة فقط ولا يستنتج الغياب من سجل مفقود. كولي أمر افتح «الإيصالات»، اختر إيصالًا واضغط «طباعة الإيصال» لاستخدام طباعة المتصفح العربية؛ لا يتم إنشاء PDF أو فاتورة ضريبية.

لتجربة Slice 8B، ادخل كإداري وافتح «الاشتراكات ← الاشتراكات الحالية» ثم تفاصيل فترة عمر. جرّب التجميد بتاريخ `2026-09-27` ثم سجل حضورًا أثناء التجميد لمشاهدة التحذير بلا خصم، وبعدها الاستئناف بتاريخ `2026-09-28` لمشاهدة تمديد النهاية يومًا. من التفاصيل نفسها جرّب إضافة 5 أيام ثم خصم يومين، وراجع «سجل التعديلات». يمكن إلغاء فترة مريم التاريخية مع بقاء رابط الإيصال وإجمالي التقرير المالي كما هو. كولي أمر افتح ملف الطفل ثم رابط حالة الاشتراك لرؤية الحالة والتاريخ المبسطين دون أسباب أو بيانات الموظف.

صور الكتالوج والتغذية والمعرض الحالية رسوم SVG محلية اصطناعية مملوكة للمشروع لأغراض Demo. لا يوجد upload أو تخزين وسائط production-grade؛ object storage الخاص، الفحص، الاحتفاظ، والروابط المؤقتة جزء من Production gate لاحق.

## 8. تشغيل الاختبارات

مع PostgreSQL يعمل وبعد تحميل `.env`:

```bash
export ACADEMY_TEST_CONNECTION_STRING="$ConnectionStrings__Default"
dotnet build AcademyManagementPlatform.slnx --configuration Release
dotnet test AcademyManagementPlatform.slnx --no-build --configuration Release
npm run typecheck
npm run lint
npm run test:web
npm run build:web
```

لتشغيل E2E أول مرة، نزّل Chromium الاختباري ثم شغّل الاختبار. يشغّل Playwright الـAPI والويب تلقائيًا إذا لم يكونا يعملان:

```bash
npm exec --workspace @academy/e2e playwright install chromium
npm run test:e2e
```

## 9. إيقاف البيئة المحلية

أوقف API والويب بـ`Ctrl+C` في نافذتيهما، ثم:

```bash
docker compose --env-file .env -f infra/local/compose.yaml down
```

## هيكل المستودع

```text
apps/api/          API وPersistence foundation
apps/web/          Next.js Arabic RTL PWA shell
tests/             unit, integration, architecture, e2e
infra/local/       PostgreSQL Docker Compose
docs/              requirements, architecture, delivery
```

لا يوجد حتى الآن: خصومات أو refunds/payment reversals، تجارة منتجات أو تغذية، production media upload/storage، Communications/bot/ranking/AI Reports أو advanced BI، revision/supersede UI أو مقارنة فترات التقييم، rescheduling متقدم، PDF receipts، provider دفع أو SMS إنتاجي، native auth، نشر أو إثبات production-readiness.
