# Status

تاريخ التحديث: 2026-09-28. الفرع: `codex/03-slice1-tenant-auth`.

## Slice 1 — منفذ فعليًا

- `Academy` tenant root و`ApplicationUser` عبر ASP.NET Core Identity و`AcademyMembership` بأدوار `AcademyOwner`, `AcademyAdmin`, `Coach`, `Guardian`.
- shared PostgreSQL schema مع `AcademyId` على العضوية، واختيار active Academy من عضويات المستخدم الفعالة فقط. يعيد الخادم فحص Academy والعضوية عند كل tenant request؛ لا يمثل header/query/body سلطة tenant.
- cookie جلسة `HttpOnly`, `SameSite=Lax`, و`Secure` خارج Development/Demo. الـcookie يحمل key مشفّرًا فقط؛ التذكرة في `UserSessions`، بمهلة idle 30 دقيقة وabsolute 12 ساعة. logout يلغي سجل الجلسة، والحساب المعطل يُرفض فورًا، وsecurity stamp يُفحص كل 5 دقائق.
- CSRF double-token على كل POST، ولا JWT منزلي أو token في URL/`localStorage`.
- staff password login مع hashing/lockout من Identity، وAPI إنشاء staff محدود للأكاديمية الحالية: Owner ينشئ Admin/Coach وAdmin ينشئ Coach فقط.
- Guardian phone normalization إلى E.164 لمصر، وDemo OTP بخمس محاولات/خمس دقائق/single-use. fixed OTP والseed يرفضان startup خارج بيئة `Demo`؛ لا SMS إنتاجي.
- Demo seed idempotent لأكاديميتين وخمسة memberships صناعية، بلا Player أو طفل أو `GuardianPlayerLink`.
- login عربي RTL، authenticated shell، Academy/role display، server-authorized Academy selector عند تعدد العضويات، وlogout. لا dashboard أعمال.
- proof APIs فقط: `/api/v1/me`, `/my-academies`, `/session/academy`, `/tenant/probe/{academyId}`، مع health endpoints السابقة.
- migrations forward-only: `TenantIdentityFoundation` ثم `IdentityUserClaims` و`MembershipRoleInvariant`. الجداول الجديدة: `Academies`, `Users`, `AspNetUserClaims`, `AcademyMemberships`, `UserSessions`, `GuardianOtpChallenges`.
- `OD-003` سُجل `APPROVED` كما قرر مالك المنتج.

## دليل التحقق المحلي

الإصدارات: .NET SDK `10.0.401` / runtime `10.0.12`، EF Core/Identity `10.0.12`، Npgsql provider `10.0.3`، PostgreSQL `17.11`، Node `24.21.0`، Next.js `16.3.6`.

- `dotnet restore` وRelease build: نجحا، 0 warnings / 0 errors.
- تطبيق migrations على PostgreSQL `academy_test` و`academy`: نجح، وفُحصت migrations دون أي جدول business لاحق.
- backend solution tests: architecture 1، unit 1، integration 14؛ الإجمالي 16/16 ناجح. اختبارات integration تشمل السيناريوهات الأمنية الـ12 المطلوبة، seed idempotency وDemo guard على PostgreSQL حقيقي.
- `npm run typecheck`, `npm run lint`, `npm run test:web`: نجحت؛ frontend test 1/1.
- `npm run build:web`: نجح، والصفحتان `/` و`/login` بُنيتا production build.
- `npm run test:e2e`: نجح 1/1 على mobile Chromium؛ login عربي، Academy/role الصحيحان، رفض Academy B، logout ثم منع الرجوع للجلسة.
- `npm audit --audit-level=high`: نجح، 0 vulnerabilities.
- CI عُدّل لتشغيل backend PostgreSQL tests وfrontend checks وE2E مع PostgreSQL؛ نتيجة remote تنتظر push.

## مراجعة الأمان المركزة

- tenant tampering/IDOR: المورد التجريبي يقارن route ID مع tenant المحلول من server-side ticket ثم membership فعالة؛ اختبارات URL/body/header وAcademy B تمر بالرفض `403`.
- CSRF/session fixation: token/header مطلوب لكل mutation، وsign-out يسبق كل sign-in/switch. logout يلغي التذكرة المخزنة في DB.
- cookie/account state: key مشفّر فقط في cookie؛ لا بيانات اعتماد في URL أو logs. `ITicketStore` يرفض revoked/expired/disabled في كل request؛ security-stamp window أقصاه 5 دقائق.
- password/OTP: Identity hashing وlockout؛ OTP لا يُسجّل، منتهي، single-use، وحده الأقصى خمس محاولات. طلب OTP يعطي ردًا عامًا، ولا ينشئ child access.
- staff scope: request لا يحتوي `AcademyId`؛ العضوية الجديدة تُكتب داخل tenant الحالي فقط وفي transaction. role policy يرفض Coach/Guardian.
- Demo leakage: guard يفشل startup إذا فعّل seed/fixed OTP خارج `Demo`. لا real SMS/provider credential.
- إصلاحات المراجعة: حُوّل seed clock إلى UTC المتوافق مع PostgreSQL، حُفظ tenant claim في server ticket، وأضيف lockout وحد محاولات OTP. لا توجد findings حرجة متبقية ضمن Slice 1.

## قيود وما لم يُنفذ

لا `Branch`, `Sport`, `Group`, coach profile، `Player`, `GuardianPlayerLink`, subscriptions, attendance, evaluations, finance, nutrition, products, medical, gallery أو reporting. لا SMS/OTP production provider، native auth، deployment أو production tests. staff UI غير موجود؛ المسار API محدود فقط. لم تتغير ملفات requirements baseline ولم تُستخدم أي بنية أو بيانات للأهلي.

## نقطة التوقف

Slice 1 فقط. أي Slice 2/Structure/People يحتاج تفويضًا جديدًا؛ لم يبدأ.
