# iAcademy One — Additional Implementation Requirements

## Full startup rehearsal, 300-player import, WhatsApp, group notifications and Guardian Home

This ADDENDUM extends the existing R01–R19 implementation handoff and your current execution plan. Read it completely, merge A01–A12 into the same tracker, and finish BOTH the previous scope and these additions. Do not restart the project, replace the existing plan or stop after a design proposal. Deliver working mobile flows, required web/platform changes, connected data and observed evidence.

The immediate recording priority remains the FOUR real Home experiences: Owner, Administrator, Coach and Guardian. Multi-tenant setup and first-use data import are equally critical: they must work as the actual onboarding process for a new academy.

## A01. Reference priority, branding and actual assets

Keep the previous engineering, isolation, financial and delivery requirements. Latest explicit corrections override conflicting older presentation instructions.

The PLAYER CARD is now a specific exception to the earlier unapproved-design rule: the user explicitly requests the FIFA-style sports card to match the attached card design supplied to you. Inspect that actual attachment and reproduce its visual structure, silhouette, portrait treatment, hierarchy, performance indicators, skill numbers and prominent overall value. Adapt academy branding, Arabic RTL and responsive sizing without replacing the requested card with a generic statistics tile. Compare your running implementation against the reference.

Other Claude designs remain inspiration rather than automatically approved layouts. Improve weak Home compositions when needed while preserving working features and academy identity.

Use the supplied Professionals academy logo when available. If either logo or card reference is absent from YOUR task context, state the exact missing asset and continue all unaffected work; never claim to have inspected it or matched it.

Naming discrepancy: the previously confirmed platform name is iAcademy One; the latest spoken message says AI Academy Online. Resolve against confirmed product configuration and explicitly named artwork. Do not silently introduce two platform names or rename bundle/package IDs. Keep one canonical name and flag an unresolved naming discrepancy without blocking unrelated implementation.

## A02. Run the experience from the beginning

Actually launch the mobile app from a closed state in the available native environment. Verify the platform logo/native launch, rich in-app moving background, phone login, OTP, academy/role resolution, academy-branded role onboarding, automatic academy reveal and the correct populated Home.

Use an isolated first-use account/session to observe onboarding; do not reset real customers. Also verify returning launch with a valid session: no routine OTP or repeated onboarding, and no intro replay on ordinary background resume or tab navigation. Keep the approximately two-second academy reveal, concurrent data loading, truthful shimmer, reduced-motion behavior and error handling from R07.

Create/setup Professionals through the REAL platform console. Upload its logo once, configure its real named branches/sports and owner identity, import the workbook below, check readiness and activate. Sign in using the configured Owner phone, then the other role accounts. Verify that the logo, colors and appropriate backgrounds propagate to onboarding, all four Homes, and the in-app bottom-navigation Home icon. Preserve a neutral logo surface and readable contrast.

Test another academy with a contrasting identity to demonstrate that changing or switching an academy does not change another academy's data or branding. Inspect the platform console itself for clear Arabic instructions, sensible page order, usable lists, import feedback and understandable next steps. Do not treat successful APIs alone as a passing UI/UX review.

## A03. Make the workbook easy for academy staff

Extend the EXISTING .xlsx format and import pipeline. Deliver an academy-friendly blank workbook with an Arabic “ابدأ هنا” sheet explaining filling order, required fields, simple examples and upload steps. Keep optional historical sheets clearly optional for a basic first import.

Use clearly named entity tabs and consistent headers. Cover the existing model's branches, sports, age categories, coaches/staff, groups, guardians, players, enrollments/packages/subscriptions, schedules and supported historical records. Include attendance/evaluations/payment history where supported and needed for this rehearsal.

Each entity should have a readable name and a stable external reference code, for example BR01, CO01, GR01 and PL0001. These are workbook linking codes, NOT internal database IDs that customers must discover. Explain codes simply and provide dropdown/reference validation where practical. Keep phone values as text and normalize country codes and Arabic/Western digits correctly.

Make the Players tab straightforward: one player identity per row, with clear guardian and enrollment links. Use explicit extra enrollment/relationship rows for multiple sports, groups or guardians; do not rely on comma-separated names or guess relationships. Coach assignment should follow the selected group/assignment; if coach information is repeated for convenience, validate consistency instead of producing contradictory links.

Support siblings sharing a guardian, multiple authorized guardians for one child, and legitimate multi-role phones. Children do not need their own phone numbers. Do not silently merge two people because their names match.

## A04. Prepare a connected 300-player rehearsal

Prepare a FILLED workbook for a clearly identified Professionals DEMO/STAGING tenant. Use synthetic people and historical transactions, with the supplied academy identity where available. Do not present synthetic history as an actual customer's records.

Use an explicit reference date in the academy time zone, close to the recording date. Include SIX completed months plus the current month, so charts, renewals, attendance and evaluations can be inspected over time rather than just one or two days.

Required population at that reference date:

- Exactly 300 DISTINCT players.
- 250 players with at least one currently active subscription.
- 50 players with expired subscriptions and no currently active subscription.
- A documented near-expiry subset of the active population.
- Varied valid packages: duration, session-count and combined plans, and private/group plans where the academy supports them.
- Connected guardians, coaches, groups, weekly schedules, dated sessions, attendance and published/draft evaluations, with realistic historical renewal periods and registration dates.

Use the academy's confirmed sports. If Professionals is a swimming-only academy, do not invent football or force multi-sport branding into it. Use a second demo tenant for additional sport/capability cases where needed. Include a small valid multi-enrollment sample within the supported structure.

Do not equate 250 active PLAYERS with 250 active SUBSCRIPTION RECORDS. Prepare an expected-results summary for both units, all historical periods and overlapping near-expiry categories. Historical monthly charts must derive from dated source records, not today's status copied into every month.

Where financial history is supported, import explicit synthetic historical payments/collections through the existing accounting path. A paid subscription must not generate duplicate collections. Reconcile financial charts with the original synthetic source totals. Never invent revenue merely to fill a chart.

Use isolated authentication/test delivery settings and authorized test devices. No real SMS, WhatsApp invitations or push messages to fabricated numbers. Never change the production clock or real records to make a demo look current.

## A05. Upload through the UI, reconcile, and test failures

The required acceptance exercise is an ACTUAL upload of the completed 300-player workbook through the platform console: select file → validate → preview → confirm → import → review results. Direct database fixtures or a generated spreadsheet alone do not satisfy this requirement.

Check every major relationship and expected aggregate: 300 distinct players; the 250/50 player-status split; separately counted subscriptions; guardian-child, player-enrollment-group, coach-group and schedule-session links; historical monthly totals; draft versus published evaluations.

Inspect representative linked records in all four role experiences. Include siblings, multiple guardians, private/session-based subscriptions and differing dates. Each role must see only its permitted scope. Capture Home screenshots after the actual imported data is loaded.

Re-upload the same workbook through the OTHER supported entry point, Academy Owner versus platform admin. Verify no duplicate people, subscriptions, attendance, evaluations, balances or collections. Uploader identity must not change duplicate detection.

Use small invalid-data workbooks/rows for these meaningful negative scenarios:

1. Missing or duplicate reference codes; duplicate rows and conflicting identity updates.
2. Unknown guardian/group/coach/package references and inconsistent branch/sport/group assignments.
3. Invalid phones versus valid normalized equivalents, shared guardians and legitimate multi-role phones.
4. Missing required values, impossible/reversed dates, negative amounts and invalid session counts.
5. An unauthorized target academy or a reference belonging to another tenant, including direct API tampering.
6. Interrupted import/retry, following the declared atomic or staged strategy without half-linked or duplicated results.
7. Invalid evaluation criteria, draft/publication errors and unsupported historical data, with no silent data loss.

Every error identifies sheet, row, column, reason and an understandable Arabic correction. Fix the negative sample and demonstrate successful corrected import. Measure and report actual import duration and responsiveness; do not invent a speed claim.

Deliver the blank template, completed 300-player workbook, compact negative samples, expected-results summary and actual reconciliation report. Do not stop after creating the files.

## A06. Individual WhatsApp contact from player and guardian profiles

Add a recognizable green WhatsApp action labeled “التواصل مع ولي الأمر عبر واتساب” in authorized staff views of EACH player profile. The recipient is the linked guardian, not the child's phone.

- One eligible guardian: open that guardian's conversation.
- Several eligible guardians: show a clear chooser with guardian name, relationship and authorized phone; respect any existing primary-contact designation without silently selecting the wrong person.
- No valid linked contact: show a helpful state and the permitted path to complete the guardian record, rather than opening a wrong/empty number.
- Guardian profile: provide a direct “التواصل عبر واتساب” action for that guardian.

Enforce tenant, role and contact-view permissions with the correct scope: Owner within their academy, Administrator within permitted branches, and Coach for assigned players. A Guardian user must not receive a directory of other parents, and their own child page does not need a redundant button for messaging themselves.

Use official WhatsApp Click to Chat with the normalized international number and safely encoded optional draft. Keep draft content minimal, for example: “مرحبًا، نتواصل معكم من أكاديمية {academyName} بخصوص {playerName}.” Do not embed private evaluations, medical details or financial records in a URL.

Opening the conversation must not automatically send a message. Handle supported native/web opening and unavailable-app/error states. This individual contact action must work independently of Business API credentials or the group-integration feature. Test correct recipient selection, multiple guardians, invalid/missing numbers, Arabic text and phone normalization.

## A07. WhatsApp at team/training-group level: implement supported behavior

Add a green WhatsApp action within the selected team/training group. Inspect current OFFICIAL capabilities and actual business-account eligibility before offering automatic creation. Do not falsely claim that WhatsApp has no Groups API, and do not promise unsupported bulk creation/addition.

As checked on 25 October 2026, Meta's official Groups API documentation specifies Official Business Account eligibility, invite-only participation and a maximum of eight participants. This does not fulfill automatic 25–45-person academy groups. Recheck official limits before implementation; do not rely on an obsolete 100,000-conversation eligibility requirement.

For ordinary academy groups, complete a practical “إعداد / ربط مجموعة واتساب” workflow:

1. Prepare the group name and invitation text from the selected academy/team/group.
2. Guide the authorized administrator to create/configure the group in WhatsApp or link an existing one.
3. Store its validated invite link against the correct tenant and group/team.
4. Make the invitation available only to the intended authorized guardian audience, and allow sending it through the in-app notification composer after recipient preview.
5. Deduplicate guardians with multiple children in the same audience.

Use truthful states such as linked group/invitation available/invitation sent. A clicked link is not verified membership, and sending an invitation is not adding every person to WhatsApp.

Separate “المشرفون فقط / جميع الأعضاء” POSTING permissions from joining approval/privacy. When the posting setting is managed in WhatsApp, say so and guide the user; do not show a fake toggle implying an API operation succeeded. An admin-only group does not mean member phone numbers are hidden. Removing a player from the academy app does not automatically remove an existing external WhatsApp membership; show the responsible administrator the required external action.

Use an eligible official integration only where its capabilities genuinely fit. Do not use WhatsApp Web scraping, unofficial account emulation, automatic address-book population, or a wa.me link falsely presented as a populated group creator. WhatsApp setup must not block the rest of the app or the independent notification feature.

## A08. Send targeted in-app notifications from a group/team

Add “إرسال إشعار للمجموعة” / “إرسال إشعار للفريق” within the corresponding details page for permitted staff. Support a simple free-text message and useful structured prefill for a coming match/event: date/time, meeting time, venue and applicable fee when that information exists. Preserve existing match/goal notifications from R13.

Determine recipients SERVER-SIDE from the selected authorized group/team or actual event roster. For children, default to linked eligible guardian accounts, not a requirement that all players own phones/accounts. Where existing player accounts support notifications, expose that as a distinct permitted audience.

Show a preview with the selected group, player count and DISTINCT recipient count. One guardian with two children in the group should not receive duplicate identical announcements. Do not guess 25 versus 45 recipients; derive the actual audience. Revalidate sender scope and membership when sending, and exclude unrelated groups, branches and academies.

Use the actual in-app inbox plus push where configured. Store the announcement and delivery/audit status; deduplicate retries/double taps. Do not equate queued/sent with delivered/read without evidence. If push is unavailable, still complete and test the in-app path and label the missing external setup precisely.

Keep group-wide information separate from child-specific details. A generic participation fee may be announced to the group; individualized balances, evaluations, diets and private records must not be broadcast to other families. Deep links must open the correct permitted context, and access must be rechecked after membership changes. Use the same audience isolation discipline as group photos/videos.

## A09. Guardian Home: reference-matched cards and synchronized child tabs

Implement a manually swipeable RTL horizontal carousel with one sports card per linked child, following the attached PLAYER CARD design as requested in A01. Show the actual photo/shared fallback, name, sport context, skill-performance numbers and indicators, plus the prominent overall performance value/gauge.

All performance values come from the child's PUBLISHED evaluations, using a consistent assessment/sport context. The prominent number MUST represent the requested overall performance MEAN: reuse an existing documented mean, or derive a clearly documented mean from comparable, appropriately normalized published criteria. Do not relabel a different legacy “power” value “متوسط الأداء” or silently change unrelated power formulas. Label any separate power metric accurately. Missing criteria are not zero; explain incomplete assessment coverage and show an honest insufficient-data state when needed. No invented ratings to complete the reference layout. Card, gauge, number and evaluation details must agree.

Directly BELOW EACH child's card, add a full-width “ملف اللاعب” button. It opens that exact child's mobile profile, organized into applicable existing web-profile tabs with Guardian-safe fields and permissions. Do not expose staff-only notes, other families or administrative finance through this reuse.

Below the card area, provide the feature/action bar: nutrition, attendance, evaluations, subscriptions, schedule and matches when enabled. Selecting a feature reveals its content with child-name tabs for the linked children. Child tabs and the card carousel share ONE selection state: changing either updates the other; changing features retains the selected child. Preserve sport context for multi-sport children and never combine unrelated skill scales.

With one child, avoid redundant child-tab clutter. With several children, keep RTL selection readable and usable. Prevent stale child data while switching or loading. Keep new subscription, renewal, Guardian requests and existing “renew for another child” actions accessible with their existing disclosure rules.

## A10. Nutrition and academy-wide match visibility

For nutrition: selected feature → child tabs → selected child's current-day content in the academy time zone. Preserve the existing compact “اقتراح نظام غذائي ليوم …” presentation, followed by breakfast/lunch/dinner tabs. A meal shows its existing item list; selecting an item shows its available image, calories and existing calculations/details.

This is navigation/presentation integration of the existing feature, not a new AI diet-generation or medical-advice project. Preserve the existing data source and calculations. Missing plans/images/calorie values get honest fallback states, not invented information. Other feature tabs similarly display only the selected child's actual attendance, evaluations, subscriptions or schedule.

The academy-level matches/competitions switch applies consistently to ALL FOUR roles, now explicitly including Guardian. When enabled, each role still sees only its authorized teams/children/records. When disabled, remove entry points and enforce server-side access without deleting history. An enabled feature with zero matches remains enabled and has an appropriate empty state.

## A11. Shared cartoon avatars and a premium Home pass

Deliver a SMALL consistent family of attractive illustrated fallback avatars, reused throughout lists, profiles, cards and all Homes:

- Child boy, child girl and neutral child.
- Adult man, adult woman and neutral adult.

Use recorded profile attributes, not guesses from names. Coaches, administrators and other adults may share the adult set; do not generate 200 unique avatars. Real uploaded photos take precedence; missing/broken photos use the same suitable fallback. Produce clean large-card and small-list rendering, consistent framing and reusable cached assets. Do not bake names or ratings into avatar images.

Review all four Homes as a senior UI/UX and frontend practitioner. Improve hierarchy, imagery, typography, spacing, micro-interactions and academy branding wherever needed for an excellent real recording. Preserve the latest Owner/Admin top actions → subscription hero → requested analytics ordering and the existing financial rules.

Use restrained entrance, card-transition and interaction motion where it improves clarity. Keep buttons stable during tapping, support reduced motion, and avoid constant carousel motion, heavy loading or large artwork hiding daily tasks. Verify small phones, larger text, readable logo contrast and sport-appropriate pictures. Do not present decorative screens whose controls or data are disconnected.

## A12. Delivery, evidence and continued execution

Update the SAME plan/tracker with A01–A12 and continue through the remaining R01–R19 work. Prioritize four recording-ready Homes plus successful first-use/tenant/import flow, without dropping the communication and Guardian requirements.

Deliver:

- Actual app/build/accessible preview and platform-console access, with tested role sign-in instructions.
- Blank workbook, filled 300-player workbook, compact negative examples, expected totals and observed reconciliation results.
- Evidence from full first launch and returning launch; Professionals identity propagation; all four populated running Homes; multi-tenant switching/isolation.
- Evidence that individual WhatsApp actions select the correct guardian and that group setup labels accurately reflect what is supported.
- Evidence that one selected group's announcement reaches only authorized recipients, avoids duplicate sibling/retry notifications and opens the correct context.
- Reference-versus-implementation card comparison; correct child switching/profile tabs/nutrition; reusable fallback-avatar examples.
- A short Arabic operating guide and per-requirementment completed/blocked/unverified status with concrete evidence and next steps.

Do not claim native testing from a web screenshot, push delivery without a device check, WhatsApp creation/membership without evidence, or successful import from workbook generation alone. State real missing credentials/assets/tooling precisely and complete unaffected work first.

Keep checkpoints, resolve routine implementation choices yourself, and continue until all authorized work is complete. Do not stop after one part, ask routine permission to continue, or replace a working deliverable with a plan. Preserve the previous limits on production changes, purchases, store publication and unsolicited real messaging.

Official WhatsApp references to verify during implementation:

- [Groups API](https://developers.facebook.com/documentation/business-messaging/whatsapp/groups)
- [Groups API eligibility and setup](https://developers.facebook.com/documentation/business-messaging/whatsapp/groups/get-started)
- [WhatsApp Click to Chat](https://faq.whatsapp.com/5913398998672934)
- [WhatsApp group permissions](https://faq.whatsapp.com/526742385997912)