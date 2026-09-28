# Status

تاريخ التحديث: 2026-09-28. الفرع: `codex/02-slice0-foundation`.

## Slice 0 — منفذ فعليًا

- Solution على .NET 10: `Academy.Api` و`Academy.Infrastructure` فقط؛ لا مشاريع مستقبلية فارغة لكل module.
- `/health/live` مستقل عن قاعدة البيانات، و`/health/ready` يفحص PostgreSQL عبر EF Core/Npgsql.
- JSON structured logging وProblemDetails foundation.
- `FoundationDbContext` بلا entities/DbSets، وmigration تقنية `FoundationInitialized`؛ التطبيق اليدوي أنشأ `__EFMigrationsHistory` فقط.
- PostgreSQL 17 Compose محلي، `.env.example` بلا أسرار حقيقية، ولا migrations تلقائية عند startup.
- Next.js 16 shell عربي من الجذر `lang=ar` و`dir=rtl`، responsive/mobile-first، أربع بطاقات role preview مع تنبيه واضح أنها غير مفعلة.
- PWA metadata/manifest وicon مستقل بلا أي هوية للأهلي؛ لا offline business/service worker.
- unit/integration/architecture/frontend render/E2E harness وGitHub Actions CI.
- سجل `OD-002` كمعتمد من مالك المنتج بتاريخ 2026-09-28؛ لم يتغير أي `OD` آخر.

## تحقق نُفذ ونجح

الإصدارات: .NET SDK `10.0.401`، EF Core `10.0.12`، Npgsql provider `10.0.3`، PostgreSQL `17.11`، Node `24.21.0`، Next.js `16.3.6`.

- `dotnet restore AcademyManagementPlatform.slnx` — نجح.
- `dotnet build AcademyManagementPlatform.slnx --configuration Release` — نجح، 0 warnings و0 errors.
- `dotnet ef migrations add FoundationInitialized ...` — نجح؛ migration بلا domain schema.
- `dotnet ef database update ...` على PostgreSQL 17 مؤقت — نجح؛ فحص `\dt` أظهر جدول EF التقني فقط.
- `dotnet test AcademyManagementPlatform.slnx --no-build --configuration Release` — نجح: architecture 1/1، unit/live 1/1، integration/readiness 2/2.
- `npm ci/install` وaudit — نجح، 0 vulnerabilities وقت الفحص.
- `npm run typecheck` — نجح.
- `npm run lint` — نجح باستخدام ESLint `9.39.5`، أحدث خط متوافق فعليًا مع Next plugins الحالية.
- `npm run test:web` — نجح، 1/1.
- `npm run build:web` — نجح؛ `/` و`/manifest.webmanifest` static.
- `npm run test:e2e` — نجح، 1/1 على mobile Chromium؛ تحقق العنوان العربي و`lang=ar` و`dir=rtl` والتنبيه وAPI live.
- تشغيل API و`curl`: مع PostgreSQL، `live=200` و`ready=200`. بعد إيقاف PostgreSQL، `live=200` و`ready=503`.

## قيد بيئي موثق

Docker/Compose لم يكونا مثبتين في بيئة التنفيذ، لذلك لم يُشغّل ملف Compose نفسه. جرى بدلًا منه تشغيل PostgreSQL `17.11` محليًا في cluster مؤقت معزول، وتحقق الاتصال والـmigration والجاهزية عمليًا. ملف Compose يستخدم image `postgres:17` لكنه ينتظر تشغيله على جهاز به Docker قبل الاعتماد عليه كدليل مستقل.

## لم يُنفذ

لا Academy business tables، ولا tenant isolation فعلي، ولا authentication/authorization/users/passwords، ولا Player/Guardian/Branch/Sport/Group، ولا subscriptions/attendance/evaluations، ولا parent business screens، ولا nutrition/products/medical/gallery/collections/reports، ولا seed/demo data، ولا SMS/OTP/payment، ولا service worker/offline business، ولا deployment أو production configuration/tests. لم تُمس ملفات baseline المعتمدة، ولم يُستخدم كود أو أصل أو بنية للأهلي.

## نقطة التوقف

Slice 0 فقط. الخطوة التالية المحتملة هي Slice 1 minimal secure tenant/auth path بعد تفويض مستقل وحسم `OD-003`; لم تبدأ.
