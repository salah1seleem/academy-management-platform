# Academy Management Platform

منصة مستقلة عربية أولاً لإدارة الأكاديميات. الحالة الحالية هي **Slice 6: تجربة ولي الأمر الأساسية وطلبات الاشتراك الجديدة**؛ وحدات محتوى Slice 7 ما زالت خارج النطاق.

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
- معايير تقييم خاصة بالرياضة، مسودات ودرجات 0–100 ونشر immutable. تقرير كرة القدم يحسب ستة محاور من snapshots الخادم ويعرض radar وقيمًا نصية وتاريخًا لولي الأمر؛ السباحة لها criteria مستقلة بلا radar كرة قدم.
- رئيسية ولي أمر عربية mobile-first تجمع الطفل مرة واحدة وتعرض رياضاته دون تكرار، مع الإجراءات الثلاثة: اشتراك جديد، تجديد، وتجديد للغير. ملف الطفل يعرض التقرير المنشور والجدول والحضور والاشتراكات من البيانات المحفوظة.
- `NewEnrollmentRequest` لطلب رياضة جديدة لطفل مرتبط أو طفل جديد؛ الطلب وحده لا ينشئ لاعبًا أو تسجيلًا أو دفعًا. Owner/Admin يراجع ويختار المجموعة ثم ينشئ الرابط والتسجيل ذريًا دون اشتراك مدفوع.

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

آخر migration هي `20260928221209_Slice6GuardianCore`. تضيف `NewEnrollmentRequests` فقط، مع علاقات tenant-scoped وحماية idempotency وحالات المراجعة؛ لا تضيف Nutrition أو Products أو Medical أو Gallery أو Matches أو Messages.

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

افتح `http://127.0.0.1:3000/login`. ادخل كولي الأمر «سارة محمود» بالهاتف التجريبي، وشاهد عمر مرة واحدة مع كرة القدم والسباحة، والإجراءات الثلاثة وحالات الاشتراكات. افتح ملف عمر لمراجعة التقرير المنشور والجدول والحضور والاشتراكات. من «اشتراك جديد» اختر طفلًا قائمًا أو أدخل طفلًا جديدًا، ثم الرياضة والفرع فقط. ادخل كإداري وافتح «اللاعبون ← طلبات الاشتراك الجديدة»، ابدأ المراجعة واختر مجموعة مطابقة ثم اعتمد؛ يعود ولي الأمر إلى الطلب المقبول ومنه إلى مسار الباقة والدفع الموجود. `OD-006` و`OD-011` ما زالا غير معتمدين؛ لا أموال أو SMS حقيقية. الجلسة `HttpOnly` ولا tokens في `localStorage`.

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

لا يوجد حتى الآن: Nutrition/Product/Medical/Gallery/Communications/AI Reports أو advanced BI، revision/supersede UI أو مقارنة فترات التقييم، تصدير حضور، rescheduling متقدم، خصومات/تجميد/إلغاء اشتراك مكتمل، provider دفع أو SMS إنتاجي، native auth، نشر أو إثبات production-readiness.
