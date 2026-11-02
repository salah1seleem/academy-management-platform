# iAcademy One — Complete Codex Implementation Handoff

Version: 25 October 2026. This consolidates and supersedes the earlier implementation prompt wherever requirements conflict.

You are working in the EXISTING academy-management-platform / iAcademy One project. Deliver the actual mobile app, required backend/database changes, platform console, relevant web screens, prepared data, and verified access. This is an implementation assignment, not a proposal or a static redesign.

The marketing recording is planned for 26 October. Prioritize four excellent, working Home screens—Owner, Administrator, Coach and Guardian—especially Administrator and Coach. They represent four role experiences inside ONE app. Continue through the complete scope; never fabricate completion or weaken isolation to meet the recording date.

## R01. Authority, inspection and execution

Read repository instructions, actual code, existing progress records and Academity_Master_Plan_V2_AR.md or subsequent V3/V4 handoffs if present. Preserve the existing stack, working features and calculations. Extend existing auth, tenant, import and component systems; do not build parallel replacements or substitute a web mockup for the mobile app.

Inspect every supplied Claude Design reference, card concept, iAcademy One logo and existing screen that is actually available. ALL such visual references are UNAPPROVED inspiration. Adopt useful ideas, improve weak colors, images and hierarchy, and produce a stronger coherent design. Do not claim to have inspected an absent attachment. Missing references must not halt available work. Maintain a short adopted/adapted/rejected-and-why comparison.

Latest binding corrections:

- Owner and Administrator quick actions belong at the TOP, below the compact header. Remove the previous default placing Owner actions at the end.
- Academy introductions are generated automatically by the platform from the academy identity. Do not implement an academy-owner splash-video upload/editor.
- Central onboarding remains role-based, with sport-aware imagery and vocabulary; no academy-authored onboarding pages.
- One academy logo supplies all in-app identity surfaces. Paid OS launcher icons are a separate, explicitly constrained feature.

Maintain one requirement tracker using R01–R19, with implementation locations, evidence and a resumable checkpoint. Progress and operating instructions must be clear Arabic. Product UI is Arabic RTL; technical notes may be English. Public brand: iAcademy One. Do not rename existing package/bundle identifiers unnecessarily.

## R02. Real platform-management console

Provide a distinct privileged platform role and route for Salah, separate from Academy Owner. Use the existing dashboard design language and improve its clarity: responsive lists, concise page guidance and understandable states. Include “كيف أستخدم هذه الصفحة؟” explaining purpose, important fields, action result and next step.

Provide a tested development/staging platform sign-in using the supported mechanism. Deliver the actual username/email and password or exact supported login instructions securely; never invent credentials, embed production passwords or commit secrets.

Overview: actual academy totals, setup tasks and applicable trial/subscription reminders.

Academy list: logo/name; owner name and separate phone; location; operational status; actual branches, sports and DISTINCT players; platform subscription price, currency and billing period. Add search, filters, pagination and ascending/descending sorting by players, branches, sports and comparable platform fees. Do not compare different currencies or monthly/yearly prices without a declared normalization/filter rule. Platform fees are what academies pay iAcademy One, not player revenue.

Academy detail tabs: “نظرة عامة”، “الهوية”، “الفروع والرياضات”، “المستخدمون والصلاحيات”، “استيراد البيانات”، “اشتراك المنصة”، “سجل التغييرات”. Put sports imagery, enabled modules and premium-icon eligibility in the relevant existing tabs. Keep commercial fields simple; do not start a new billing/payment-gateway project.

## R03. Academy setup, readiness and activation

Build a resumable sequence:

1. Academy name, one canonical logo, accessible brand accents, contact/location/time zone.
2. Owner name and normalized phone; actual named branches and sports.
3. Enabled capabilities; import people, structure and supported historical records.
4. Resolve import errors, review readiness and preview each role with actual linked data.
5. Explicit activation.

Create real branch/sport records and derive counts; entering “three branches” alone is insufficient. Saving setup must not activate the academy or automatically invite everyone. Separate preparation/active/suspended access states from commercial subscription/trial status.

Missing mandatory identity/relationship data blocks activation with a clear fix. Optional photos or historical evaluations do not block an otherwise usable academy and must never be invented. Unready academies show a helpful access message; authorized setup and scoped previews remain available.

Show a real academy route/QR only if supported by the actual deployment. Shared multi-tenancy does not require one server or server-URL field per academy. Do not invent domains or require new infrastructure for this scope.

## R04. Phone identity, sessions and multiple roles

Use one authenticated identity per normalized phone plus explicit academy memberships, roles and branch/group/team scopes. Normalize Arabic/Western digits and country codes; store phones as text. Safely migrate existing relationships.

One phone may be Owner in A and Guardian in B, or Coach and Guardian in A. Do not reject legitimate memberships or overwrite conflicting personal details. A phone number alone never authorizes access. Verify through the real authentication flow before returning private membership choices.

No session: platform entry → phone → OTP → authorized context. One allowed context proceeds directly; multiple contexts show academy logo/name and role, remembering the last still-authorized choice. Show “تبديل الواجهة” in “المزيد” only when more than one context exists. Custom director roles reuse an appropriate experience with explicit permissions.

Keep valid sessions signed in with secure renewal. Do not require OTP at every launch or role switch. Reverify when trusted credentials are absent/revoked or required on a new device. Handle unknown accounts, unready/suspended academies, expired/wrong OTP, resend limits and connectivity clearly. Indexed membership resolution must not become a slow global scan on navigation. Revoked membership must stop access despite an old session.

Any test OTP belongs only to an isolated test environment, with honest documented instructions.

## R05. Server-enforced isolation and scoped permissions

Validate academy context, membership, role and resource ownership on EVERY applicable server read/write, import/export, media access and notification. Client-supplied tenant/role IDs are never sufficient authority.

- Owner: authorized data in their academy.
- Administrator: permitted branches and operations.
- Coach: assigned groups, sessions, players and teams.
- Guardian: explicitly linked children in the selected academy.
- Financial summaries, profit/loss and academy-wide collection reports: Owner unless specifically delegated.

Preserve authorized registration, renewal and branch-scoped payment/receipt operations for administrators; withholding financial reports must not prevent legitimate operational collections. A “technical director” title does not itself grant financial access.

Switch API context, permissions, logo/name, queries and local state together. Cancel or ignore old responses and prevent flashes of the previous academy’s data. Cache keys include authorized context and relevant scope/filter inputs. Owner-to-Guardian switching must remove Owner powers. Protect child media, deep links and exports; do not expose a shared cross-academy phone directory.

Platform role previews must be clearly labelled and audited, using supported preview/test-account mechanisms rather than an unaudited impersonation bypass. Audit important role, branding, setup, import, activation and result changes.

## R06. Shared identity and exceptional visual assets

Inspect the available iAcademy One logo, icon and wordmark. Refine or replace weak unapproved artwork with a coherent, usable brand; deliver source/export assets and integrate them. Preserve recognizable existing approved product language wherever applicable.

One academy logo upload must update: onboarding; automatic academy intro; all four Home headers/heroes; and the in-app bottom-navigation Home icon. Keep “الرئيسية” under that icon; use contain sizing, a neutral container and an active indicator without recoloring/distorting the logo. No repeated logo uploads per screen.

Use shared components and versioned brand assets. Derive initial accent suggestions from the logo, then enforce contrast and allow platform overrides. Do not blindly use a logo color against a similar background. Protect logos with a suitable light/dark neutral plate and text with a controlled scrim/solid surface. Semantic success/error/warning colors retain their meaning.

Create genuinely finished sport-appropriate hero/onboarding visuals using licensed available imagery or available image-generation tools. Avoid generic portraits, broken anatomy, irrelevant football photos and decorative filler. Stock/generated people must never be represented as the actual child or coach. Keep logos, copy and statistics as live overlays—not baked into images—so branding remains dynamic and accessible.

Sport rules:

- Before context resolution: inclusive multi-sport or refined neutral iAcademy One imagery.
- Single-sport academy: that sport; swimming means swimming imagery.
- Multi-sport Owner/Admin: deliberate multi-sport/neutral composition, or selected sport when filtered.
- Coach: assigned/selected relevant sport; Guardian: selected child’s sport context.

Maintain a small reusable sports-asset collection with a neutral fallback and platform-controlled selection. Do not create a separate asset upload for every surface or require runtime AI generation. Keep one visual system: quality Arabic typography, consistent icons, generous but efficient spacing and readable controls. Real task content must remain visible on the first screen.

## R07. Native launch, automatic academy intro and honest loading

Keep the OS-native launch screen compatible with iOS/Android. Rich motion belongs inside the running app; do not promise a downloaded per-tenant video as the native launch screen.

Platform entry/login: iAcademy One identity, polished restrained moving background, readable phone form. Suggested line: “أكاديميتك في إيدك”. Instructions: “ادخل برقم هاتفك المسجل لدى الأكاديمية.” CTA: “متابعة”. OTP includes masked phone, “تغيير الرقم”, verification/resend feedback, readable digits in RTL and keyboard-safe actions.

Generate the academy intro automatically from logo/name/accessibility-adjusted accents using a reusable code/native animation template in the existing stack. No separate video generation per customer, owner video editor or unnecessary rendering service.

Target approximately TWO SECONDS TOTAL:

- 0–250 ms: background appears.
- 150–850 ms: logo grows from roughly 65–70% to a restrained 103–104%, settling at 100% with elegant ease-out.
- Until approximately 1600 ms: brief clear hold with academy name.
- Final approximately 400 ms: transition into Home or its actual loading skeleton.

Use subtle light/contours/sport-appropriate background movement. No flashing, loud particles, autoplay sound or repeated bouncing. Keep the logo visually separated even when its colors resemble the background. Academy name and logo are enough here; do not repeat multiple slogans. Reduced-motion mode uses a static frame/brief fade.

First entry: native/platform entry → phone → OTP → authorized context → role onboarding → academy intro → Home. Returning cold launch with valid known context: native launch → academy intro → Home, without repeated OTP/onboarding. Normal background resume and tab changes must not replay the introduction. Do not block time-sensitive deep links with unnecessary branding.

Prefetch Home data during the intro. Use layout-matched subtle shimmer ONLY for genuinely pending sections; replace each when ready. No fake progress, artificial delay, second loading show or endless logo loop. Reduced motion uses static skeletons. Handle errors with retry. Cache assets safely and replace versions coherently; offline/missing assets fall back cleanly without showing another academy’s brand.

## R08. Central role onboarding, personalized by identity and sport

Replace tenant-authored onboarding with ONE centrally maintained template system. Safely retire the old editor without deleting unrelated academy branding. Three concise slides per role; sport-specific visuals/terms are controlled variants, not separate tenant-authored flows. Context is resolved AFTER verified login, before choosing personalized slides.

Show the academy logo prominently in a protected position; use bounded academy accents without losing readability. Parent slides may show the linked child’s actual photo/published card data when available, otherwise an honest illustration—not invented ratings.

Use this Arabic baseline, improving wording only while preserving meaning and actual capabilities:

### OWNER

1. “صورة واضحة لأكاديميتك” — “تابع الاشتراكات والتحصيلات والمصروفات، واعرف ما يحتاج إلى انتباهك اليوم.”
2. “تابع التفاصيل التي تصنع الفرق” — “راجع التجديدات القريبة، وتابع اللاعبين والمدربين والمهام من مكان واحد.”
3. “تجربة تليق باسم أكاديميتك” — “اجمع فريقك وأولياء الأمور في تجربة تحمل هويتك وتسهّل متابعة التدريب.”

### ADMINISTRATOR

1. “يومك أوضح، وشغلك أسهل” — “ابدأ بالتسجيل والتجديد، ووصل إلى حصص اليوم ومهامك بسرعة.”
2. “كل اشتراك في وقته” — “تابع الاشتراكات الفعالة والقريبة من الانتهاء والمنتهية ضمن الفروع المتاحة لك.”
3. “من التسجيل إلى التدريب” — “راجع بيانات اللاعبين ومجموعاتهم ومواعيدهم، وأكمل إجراءاتك من الصفحات المتاحة لك.”

### COACH

1. “حصصك جاهزة أمامك” — “اعرف مواعيد حصصك ومجموعاتك ولاعبيك، وابدأ يومك بخطة واضحة.”
2. “تابع تطور كل لاعب” — “سجّل التقييمات والملاحظات، وانشر لولي الأمر ما يساعده على متابعة تطور ابنه.”
3. “حضور واضح، ومتابعة مستمرة” — “سجّل حضور لاعبيك، وراجع مشاركتهم وملاحظاتهم لتستعد للحصة التالية.”

### GUARDIAN

1. “شوف تطور ابنك بوضوح” — “اكتشف بطاقته الرياضية وتقييمات المدرب، وتعرّف على نقاط قوته وما يحتاج إلى تطويره.”
2. “ابقَ قريبًا من رحلته” — “تابع مواعيد التدريب والحضور والغياب والتحديثات الخاصة بابنك.”
3. “اشتراكه ومتابعته بسهولة” — “اعرف موعد انتهاء الاشتراك، وجدّده أو قدّم طلب اشتراك جديد من الخيارات المتاحة لك.”

Only highlight nutrition, media, competitions or other features when genuinely operational and permitted. Do not claim guaranteed profit or market-first status. Track agreed missing functionality rather than hiding it behind changed copy.

Controls: “التالي”, back, progress, final “ابدأ”; NO Skip and no forced timers. Asset loading must not lock Next. Persist/resume progress by user/role and managed content version. New roles get their introduction once. Logo/color edits and switching academies within the same role must not force repeats. Offer voluntary replay through Help.

## R09. Four real Homes and common interaction rules

Implement polished, distinct role experiences in the actual app. Preserve established Owner/Guardian functionality while substantially improving Admin/Coach. Every KPI must reconcile with its destination; every action must open an authorized working flow.

Use role-appropriate horizontal RTL quick-action rows, matching current icon/color language, with subtle entrance motion. Controls stay still during tapping and support manual swipe. Support small phones, larger text, reduced motion, populated/loading/empty/error states and accessible contrast. Do not make every card enormous or push essential work below a decorative hero.

Academy-branded helpful banners may use existing content facilities; no fabricated news or new CMS project. Preserve role-specific navigation and working renewal/new-subscription/other existing actions. Apply corresponding Owner changes to web using the same data calculations.

## R10. Owner and Administrator: subscription hero and on-demand analytics

Exact hierarchy: compact branded header → TOP quick actions → branded subscription hero → optional selected breakdown panel → subsequent operational content. For Owner, the existing revenue/financial section follows the subscription area, retaining its established content and rules; then preserve payment requests, three Smart Decisions and existing coach/VIP/featured-player sections and ranking logic.

Hero: sport-appropriate image, academy logo/name, clear subscription heading, total and readable tiles for active, near-expiry and expired subscriptions. Use restrained translucent/solid tile backgrounds and meaningful distinct colors. Owner copy can be “اشتراكات الأكاديمية”; Admin wording reflects the actual allowed branch scope. Each tile opens exactly its corresponding filtered list.

Audit and reuse status/time-zone/date definitions. DISTINCT players are not subscription/enrollment records. One person in two sports may be one player and two subscriptions. Near-expiry may overlap active: never sum overlapping states as exclusive categories. Clarify count units/denominators and avoid misleading percentages.

Below the hero, compact dimension tabs reveal one relevant chart/list ONLY after selection; allow collapse. Dimensions, where meaningful in the real data model: branches, sports, packages (including session-count-based subscriptions), groups, weekly training-day patterns and age groups. A separate scheduled-session/roster breakdown is optional where meaningful; it must not replace the requested subscription/package analysis by number of sessions. Hide branch/sport tabs when only one option is available WITHIN the user’s authorized scope. Never widen access to populate a tab.

Examples: monthly/quarterly/private/group packages; Saturday–Monday–Wednesday groups versus Sunday–Tuesday–Thursday groups. Private/general belongs inside packages: do NOT add a separate subscription-type tab. Normalize day combinations; do not count each dated occurrence as another member. Distinguish session-based packages from actual scheduled sessions/rosters. Attendance totals must not masquerade as subscription counts.

Keep shared branch/sport/date filters consistent across card, selected chart and destination. Fetch breakdowns lazily, not all at startup. Use actual recorded relations, not guesses from package names. Explain overlapping membership breakdowns where totals are non-additive.

Administrator sees permitted-branch counts, lists, exports, today’s sessions and follow-ups only. Quick actions include permitted registration, renewal, attendance, allocation and sessions. Do not show Owner financial summaries unless explicitly delegated; legitimate scoped payment actions remain available.

## R11. Coach and Guardian Homes

COACH: academy identity, relevant coaching/sport image and a strong next-session card: “جاهز للحصة القادمة؟”, actual group/time/branch and “عرض الحصة”. Present clear real summaries for sessions, groups, players and evaluations with their time scope. Provide assigned sessions/groups/players/attendance/evaluations, plus enabled competitions. Show useful follow-ups. Distinguish groups from dated sessions and private notes from published feedback. Empty days get truthful helpful states, not invented sessions.

GUARDIAN: the actual child’s sports card remains the visual centerpiece. Preserve child/sport selection, real photo/fallback, published evaluations and existing power indicators/formula. Improve composition without inventing ratings or implying official FIFA endorsement. Show next training, attendance, subscription/renewal and authorized child-specific notes/media/nutrition where implemented. No other child’s private data or generic promotional hero replacing the child card.

## R12. Optional competitions, permissions and match cards

Add/reuse academy-level capability settings for matches/competitions and supported navigation placement: appropriate quick action/Home destination or permitted bottom-nav slot. Respect the existing compact navigation. Disabled features disappear from relevant entry points and are enforced server-side; existing records are preserved. Zero matches is NOT the same as disabled—authorized users must still create the first match.

Use configured sport/capability terminology. Swimming can have competitions; do not blanket disable all competition features for it or force football match terminology everywhere. Do not build a new universal sports-results engine in this task.

Enforce granted permissions with the appropriate scope: Owner within their academy; Administrator within permitted branches/teams; Coach within assigned teams. A Coach permitted to CREATE matches must also be able to ENTER their results within that scope. Do not accidentally require Owners to be assigned as coaches or add a separate Coach restriction contradicting this requirement. Coach does not gain general academy configuration or unrestricted financial access. Preserve enabled self-service attendance-correction, expense/disbursement and salary-adjustment requests through approval flows, never self-approved payroll edits.

Redesign matches as individual readable cards. Show teams and score clearly; location, date and time are compact labeled chips. Separate secondary detail from the card’s primary hierarchy. Final win: subtle green; loss: subtle red; draw: orange; always include a text state. Upcoming/unfinalized matches stay neutral. Missing score is not a final 0–0 draw. Determine outcome from OUR team’s score, independent of RTL positioning.

## R13. Goal scorers and dependable guardian notifications

For goal-based sports, clearly label our team score and opponent score with team names. Our score N creates N goal slots: “الهدف الأول”, “الهدف الثاني”, etc. Each selects from the actual MATCH ROSTER; the same player can occupy multiple slots. Opponent goals do not create our scorer slots; zero goals creates none.

Validate nonnegative integers and roster/team/tenant membership server-side. Changing the score must reconcile removed/added events without hidden leftover goals. Save score and goal events transactionally. Incomplete information may remain a draft or explicitly unknown/own-goal where appropriate; never assign a fictional scorer. Do not model basketball points or swimming results as football goal selectors.

After the result is successfully finalized/published, aggregate each child’s credited goals and send ONE summary to each authorized linked guardian. Example: “سجّل صلاح هدفين في مباراة اليوم. النتيجة: فريق الأكاديمية 4 – المنافس 2.” Use correct Arabic singular/dual/plural wording and unambiguous team labels.

No notifications while typing or saving drafts. Use the existing reliable post-commit delivery mechanism; server-side deduplication identity is stable across retries: match + child + recipient + event kind. Do NOT make every result revision a fresh congratulatory key. Double taps/retries must not duplicate delivery.

Corrections reconcile goal totals and the in-app summary without repeatedly broadcasting congratulations. If a correction alert is necessary, make it clearly labeled and limited. Do not promise already delivered remote pushes can be silently retracted. Do not alter evaluation/power formulas merely because a player scored unless an existing defined rule requires it.

Implement the actual in-app notification inbox and mobile push integration, using appropriate permission handling and scoped deep links. Verify push on a test device when credentials/tooling exist; otherwise complete in-app delivery and identify remote delivery as unverified. Goal messages are not SMS. Never notify unrelated guardians or leak across academy contexts.

## R14. One Excel import pipeline, two authorized entry points

Expose the existing .xlsx workflow in BOTH the platform console and Academy Owner console, using the SAME template, validation, preview, import logic and re-import rules. Platform admin explicitly selects an authorized academy; Academy Owner cannot override their tenant in a request. Record the uploader, but uploader identity must not defeat duplicate detection.

Deliver a clear Arabic template and separate filled synthetic example. Use stable reference codes, not name guesses or internal database IDs. Support the actual modules’ relationships: branches/sports/age groups/groups/coaches; weekly schedules/dated sessions; guardians/children; enrollments/plans/subscriptions; published/draft evaluations; supported attendance/matches/history. Players need no phone; siblings and shared guardians need explicit links.

Provide row/sheet/column errors with fixes; preview additions/updates/conflicts/rejections; safe atomic or declared staged commit; batch history and downloadable errors; reconciliation against resulting links/counts. Re-upload from platform then Owner must not multiply people, subscriptions, evaluations, balances or money.

Historical paid subscriptions must not create fresh collections or duplicate revenue. Preserve historical dates and draft/publication states. Import must not send invitations/SMS or publish private media as a side effect. Refresh relevant Home aggregates after completion. Track unsupported requested data types explicitly and complete necessary integration rather than silently discarding rows.

## R15. Optional paid academy icon on the device Home screen

This is separate from the freely dynamic in-app “الرئيسية” logo. Keep one iAcademy One app and store identity; do not create one separately published app per customer.

Implement simple platform-managed eligibility and mapping: academy → premium-icon entitlement → allowed bundled icon key → supported platform/minimum app version. Use actual declared iOS alternate icons and Android manifest/launcher resources supported by the existing native stack. Ordinary remote logo upload does not instantly create a new OS icon. New academy artwork normally needs inclusion in an app build/update before selection.

Show understandable states: standard icon; academy artwork awaiting inclusion; academy icon available in this installed build. Do not invent a hard platform limit on academy count or promise every Android launcher behaves identically. Verify actual build/device support.

After authenticated context selection, offer “استخدام شعار أكاديميتي كأيقونة التطبيق” when eligible and supported. Show only authorized choices, not a catalog of other customers. Respect system confirmation behavior and use supported APIs.

The selected icon changes THIS installed app on THIS device, not every academy’s users. App name remains iAcademy One. A multi-academy user has one selected installed icon at a time: persist their choice; changing roles must not switch it. Offer an explicit choice when changing academies instead of silently changing the device icon repeatedly.

Fall back to the platform icon when unavailable/ineligible, applying changes through the supported lifecycle; do not promise instantaneous offline revocation. Finish entitlement UI and supported native integration. If signing, actual premium artwork or device execution is unavailable, state exactly what is implemented and what requires a release/device check. Use an authorized sample icon for a test demonstration if appropriate; do not sell unverified availability as ready.

## R16. Connected demo data and recording preparation

Use genuine customer data only with its actual relationships. Prepare a separate clearly identified demo academy with coherent synthetic data and authorized imagery through real services/imports—not hardcoded UI statistics. Provide enough linked people, sports, groups, plans, sessions, attendance and published/draft evaluations to populate all four Homes convincingly.

Prepare actual session dates relative to the recording date/time zone. Any date reset belongs only to demo data. Include active/near-expiry/expired subscriptions and illustrative match outcomes/scorers where enabled. Demo paid states use supported demo/accounting flows, not fake live payments.

Use a second academy with a different sport and contrasting logo to verify identity and isolation. Include Owner-in-A/Guardian-in-B and Coach-plus-Guardian-in-A cases. Never send real SMS/push to fabricated accounts or expose real children’s data in marketing captures.

Capture the RUNNING four mobile Homes and a short usable recording route. Keep seed/demo labeling clear in the environment and handoff. Do not call a design render or static screenshot a working implementation.

## R17. Focused acceptance checks with evidence

Use existing test tools and meaningful integration/API/visual checks. Do not add a broad unrelated framework. Verify:

1. Actual platform login; Academy Owner denied platform UI/APIs.
2. Setup, named records, derived list counts, activation and safe suspension.
3. Import/re-import through both entry points; relationship reconciliation; no duplicate money.
4. One-context and multi-context login; session continuity and revoked access.
5. Server tenant/branch/coach/child isolation including tampered IDs, exports and media.
6. Onboarding by role/sport/brand, completion/resume, no forced repetition after logo edits.
7. One logo updates every required surface and only the intended academy; no stale context flashes.
8. Owner/Admin top actions, subscription-state/list reconciliation, distinct count units, lazy tabs, singleton-tab hiding and branch restrictions.
9. Coach/Guardian correct linked records and working destinations; retained Owner finances/rankings and responsive web parity.
10. Competition toggles and permitted Coach creation/result entry; win/loss/draw/upcoming cards.
11. Our score 4 versus opponent 2 gives four roster slots; repeated scorer aggregates correctly; score reduction leaves no stale events; draft/retry/correction does not spam guardians; unrelated guardians receive nothing.
12. In-app notifications and actual push where possible; scoped notification deep links.
13. Native launch/intro timing, valid-session return, no replay on ordinary resume, real loading/error states and reduced motion.
14. Native premium icon selection on supported builds/devices, unchanged app name, per-device behavior and fallback.
15. Actual mobile RTL, small screens, larger text, keyboard, readability, image crops and four real screenshot captures.

Inspect the running result and improve weak compositions; do not stop at compilation. Record exactly what ran and passed. Web previews are not evidence of native iOS/Android behavior. Clearly label any unavailable native, SMS, push, signing or hosting test as unverified.

## R18. Concrete delivery and plain-Arabic guide

Deliver the actual app/build/usable preview and platform-console URL/route; tested platform and four-role accounts with context and exact sign-in/test-OTP instructions; usable Excel template and filled example; brand/motion assets; four running Home captures and recording route.

State whether links are local, temporary preview or published. Do not present cloud-workspace localhost as reachable on Salah’s Mac. Supply a supported accessible preview/build where available. Keep real secrets out of source control, screenshots and public documentation.

Write a short Arabic operating guide: add academy → upload logo once → owner/branches/sports/capabilities → import/fix errors → role preview → activate → edit branding → suspend/reactivate → switch roles. Also explain subscription filters, publish/correct match results, guardian notifications and premium-icon readiness versus uploaded logo.

Finish with an Arabic R01–R19 status table: completed with evidence, or blocked/unverified with exact dependency and next action. Include the concise design-reference comparison. Never invent deployments, tests or accounts.

## R19. Continue through completion

Start with a short Arabic progress message, inspect and execute. Prioritize the data/permission dependencies that unblock the four recording-ready Homes; then finish remaining integrations and verification. Work in coherent increments with persistent checkpoints. A plan, partial backend or screenshot-only prototype is not completion.

Resolve routine choices autonomously and continue all authorized reversible work. Do not repeatedly ask whether to continue or stop just because one optional asset is missing. Complete unaffected work before reporting a concrete credential, destructive production action or necessary external dependency.

Do not purchase hosting, publish to app stores, send marketing messages or make unapproved production changes. Actual user-triggered authentication follows the configured authorized flow. Infrastructure/developer-account purchases are a separate step.

Useful official implementation references, to verify against the actual stack:

- [Apple onboarding](https://developer.apple.com/design/human-interface-guidelines/onboarding)
- [Apple launching](https://developer.apple.com/design/human-interface-guidelines/launching)
- [Apple alternate app icons](https://developer.apple.com/documentation/xcode/configuring-your-app-to-use-alternate-app-icons)
- [Android activity aliases](https://developer.android.com/guide/topics/manifest/activity-alias-element)
- [Android splash screens](https://developer.android.com/develop/ui/views/launch/splash-screen)