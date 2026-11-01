# I Academy One — Linked Design, Connected Import & Enrollment Discount

Updated consolidated handoff — retain Points 01–23; expand Point 24; add Points 00 and 25.
## Implementation prompt for Codex

Continue the existing `academy-management-platform` implementation. This is an update to the existing application, not a request for a replacement app, a standalone HTML demo, or a new architecture.

The actual mobile application is the primary target for «المالك»، «الإداري»، «المدرب»، and «ولي الأمر». Also implement the specified web Owner Home changes, plus the explicitly requested web settings, notification management, product management, and import screens. Adapt shared features to each surface; do not redesign unrelated web modules.

Inspect the current branch, implementation, and relevant handoff/progress documents before changing anything. Preserve completed functionality and unrelated work. These instructions supersede conflicting earlier presentation decisions, but do not authorize resuming unrelated paused scope. Reuse existing APIs, permissions, financial calculations, import processing, and data models where suitable.

Use English for implementation notes if needed. ALL visible screen names, headings, labels, buttons, validation, and progress/final reports must be natural Arabic RTL. Use the approved product name `I Academy One`; retain each academy’s own configured name and branding in its workspace.

Visual quality is a primary acceptance criterion: substantially improve layout, density, typography, hierarchy, photos, and interaction, not just functionality. Use the attached exported design files as mapped in Point 00. Do not pretend to have inspected inaccessible screens or treat prototype success states as production results.

## 00 — ربط التصميم بالتنفيذ مع الحفاظ على البيزنس

Use the attached `Academy Mobile App Design.zip` together with this prompt. The ZIP is the visual/interaction reference; this prompt and the existing authorized business rules define what the real system must do. Do not build a replacement application.

| Surface | Source file inside `Academy Mobile App Design.zip` |
|---|---|
| «المالك» — mobile | `Premium Owner App.dc.html` |
| «الإداري» — mobile | `Premium Admin App.dc.html` |
| «المدرب / الكابتن» — mobile | `Premium Coach App.dc.html` |
| «ولي الأمر» — mobile | `Premium Guardian App.dc.html` |
| «لوحة الويب» | `Premium Web.dc.html` |
| Shared forms / evaluation screens | `PremiumForm.dc.html`, `premium-forms.js`, `PremiumEval.dc.html` |
| Handoff / assets / runtime | `Premium Handoff.dc.html`, `assets/`, `_ds/`, `support.js`, `ios-frame.jsx` |

The archive also contains older/alternative files, including `Presentation.dc.html`, `Academy Mobile App.dc.html`, and Modern-vs-Premium presentations. The original Presentation alone does not contain the complete five-surface experience. Use the five linked `Premium` files above as the complete cross-role reference for this handoff; do not silently mix their appearance with older alternatives. A later explicit user selection of a different visual variant overrides appearance, not business logic.

Open all five surfaces and navigate their internal tabs, bottom/side navigation, lists, forms, child details, dialogs and small-phone states. Record a compact screen-to-existing-route/component mapping. Confirm the actual files you could render; do not claim to have inspected a screen only because its name exists in source. The accompanying review is a prototype-navigation check, NOT production verification or full coverage of every control. Verify in your own runtime too.

Match the supplied appearance closely: section composition, card sizing, spacing, typography, colors, icons, portrait crops, horizontal carousels, header, navigation and RTL. Transfer these patterns into the EXISTING native mobile stack and the specified web surfaces. Do not embed the exported HTML in a WebView or replace the real app with this prototype. The preview's role selector, quick-jump toolbar, phone frame and design-review controls are NOT application features to copy.

**Preserve business behavior.** Keep existing permissions, academy/branch boundaries, player–guardian links, enrollment/renewal rules, attendance/session calculations, stars/evaluations, financial engines and integrations. A feature absent from the prototype must not disappear from the real application; give its existing destination the same visual language. New functional changes are limited to this approved brief, including the connected import in Point 24 and the exceptional discount in Point 25.

`premium-data.js` and `academy-data.js` contain example data. `premium-forms.js` contains prototype form behavior. They are NOT authoritative domain rules or financial calculations. In particular, do not copy a demo minimum collection target, a country-only phone check, fixed offer percentages, illustrative permissions, fixed dates, or demo ranking as new policy. Bind all displayed information to the current real sources. Preserve the configured currency/locale.


Known gaps in this exact export: the Home branch/date controls are presentation-only in the Owner/Web files, and the web sidebar's «وحدات أخرى» entries («المباريات»، «التقارير»، «الأعضاء والاشتراكات»، «المدربون») are static labels, not implemented destinations. This must be recorded honestly. Bind the real Home filters to existing scope/date calculations; preserve and connect existing production routes for the other entries without treating their missing prototype pages as permission to delete modules or expand into a wholesale unrelated web redesign.

Some prototype actions simulate results using local state. The Excel screen does not read a real uploaded workbook and its displayed download filenames are not actual XLSX files. Simulated save/send/import success is not proof of persistence or delivery. Implement real operations with the existing backend. Test save/reload, role scope, data refresh, actual notification recipients and actual imported records.

Use supplied images only where their usage is authorized; retain existing customer images and correct fallbacks. If an external font or asset cannot load, report it and use an explicit temporary fallback; do not certify exact visual matching with missing typography/assets. Compare actual mobile/desktop screenshots with matching reference screens at the same size before final delivery.

## 01 — التصميم العام والرأس والتنقل

Create a consistent premium visual system across mobile and the affected web screens. Remove accidental white strips between the sidebar and content; use coherent page surfaces, compact cards, readable Arabic typography, and predictable spacing. Keep labels near their values. Currency is smaller and visually secondary to the amount throughout the app.

Remove repetitive explanatory paragraphs beneath titles. Put genuinely necessary accounting definitions or sensitive-action warnings in concise contextual help/confirmation; do not change the underlying meaning just to shorten the copy. Management pages show the list first, with a clear add button opening its form. Back navigation uses an arrow.

Keep the Owner header’s profile, «الإشعارات», and contextual video icons present across «الرئيسية»، «العمليات»، «المباريات»، «التقارير»، and «المزيد». Fix their disappearance outside Home. Apply the shared shell appropriately to the other roles. Video help opens the configured video for the current page/role, never onboarding. Missing video content must have an honest state, not a fake link.

Profile contains «المظهر»، «اللغة»، and «تسجيل الخروج». Notifications remain in their own bell/inbox, not a combined news-and-notifications page. Preserve necessary account/security functions behind appropriate access rather than deleting their backend capabilities.

Preserve player/coach/guardian photos and consistent silhouette fallbacks. Use safe, fictional portraits for isolated demo data; never replace customer photos.

## 02 — ملخص الأكاديمية وهدف التحصيل

Keep «التحصيل خلال الفترة» and comparison with the previous comparable period. Make «ضبط هدف التحصيل» an actual, easy-to-save flow, preferably a compact contextual dialog/sheet instead of sending the user into an unrelated settings page. Persist the target and its applicable academy/branch/period scope; refresh and reload must show the same value and correct progress.

In isolated demo data, add legitimate linked historical test transactions so a realistic comparison such as approximately 12–14% can be demonstrated. Do not hardcode the displayed percentage, fabricate production income, or alter real transactions to achieve an attractive number. Calculate comparison/target progress from the same applicable scope. Hide unavailable comparison/target rows instead of adding long missing-data messages.

Move «مستحقات متأخرة»، «أعضاء يحتاجون متابعة»، «أماكن تدريب متاحة»، and «متوسط الحضور» INSIDE the «ملخص الأكاديمية» card as compact indicators. Keep their drill-down actions. Avoid repeating them as separate oversized cards. Implement the web arrangement and a readable mobile equivalent.

## 03 — الاشتراكات وشارت الفروع

Keep subscription totals and meaningful states, including active, ending soon, and expired, with working filtered-list links.

Inside the SAME «الاشتراكات» card, show a small branch comparison chart for SUBSCRIPTION COUNTS, with clear branch labels and count/percentage as appropriate. Remove the redundant standalone branch pie chart and standalone active-subscription chart.

The latest decision explicitly overrides the earlier request for two charts here: DO NOT add subscription monetary-value charts to this card. Monetary collection breakdowns belong to «التحصيلات». Respect the selected scope without confusing subscriptions with distinct players or double-counting near-expiry subscriptions inside totals.

## 04 — التحصيلات ومصادرها

Merge «التحصيل اليومي» and «التحصيل الشهري» into one compact «التحصيلات» card with two clear internal sections. Each retains «نقدي» and «إلكتروني» breakdowns. Preserve clear actual day/month labels when exploring historical data.

Within this same card, add compact branch collection charts and «مصادر التحصيل»: «اشتراكات جديدة»، «تجديد الاشتراكات»، «المنتجات الرياضية»، «البطولات», and other genuinely configured sources when applicable. Do not count a parent subscription total again alongside its new/renewal components.

Use close label/value alignment, small but readable charts, and minimal empty space. Chart captions must identify their period; do not mix daily and monthly figures without explanation. Use the same sources/calculations on mobile and web.

## 05 — تقرير الأرباح والخسائر

Populate Home and the detailed report only from relevant configured financial categories and actual applicable records. A football academy must not see unused swimming/pool/lane/external-program categories. Do not invent financial categories or meaningless zero rows merely to fill the UI.

Display revenue/receipt and expense/payment categories in compact value tiles: three per row where readable, two on narrow phones. Use clear Arabic labels and small currency text. Remove repeated technical qualifiers such as «دون عمولة الخاص المفصلة» from visible category names.

Retain totals and «الفائض التشغيلي» / «العجز التشغيلي». If the existing report is cash-based, identify that briefly, for example «على أساس التحصيل والصرف», with detail behind an information action. Do not silently relabel cash flow as earned profit. Keep outstanding customer balances, unpaid expenses, and payroll liabilities separate from actual receipt/payment totals.

Hide unused categories, not genuine transactions: existing amounts must remain accurately represented even if a module is later disabled. Reuse the canonical calculation engine.

## 06 — ترتيب الرئيسية والإجراءات السريعة وطلبات الدفع

Place «الإجراءات السريعة» at the END of Home by default. Add a working academy setting to move them to the beginning. Save and apply that preference consistently on the applicable mobile/web Owner Home.

Make «طلبات الدفع» one compact card with readable, smaller counts and working type-specific links. On desktop, use half-width placement beside another appropriate section rather than stretching three numbers across an excessively wide area. Do not squeeze phone content into an unreadable desktop arrangement.

Keep financial/subscription statistics before «مركز القرارات الذكية» and follow-up sections. Consolidate duplicate charts and follow-up blocks; preserve useful information and actions without repeating them.

## 07 — المدربون ومركز القرارات الذكية

Within «المدربون», add a small sorting switch: «حسب التقييم» and «حسب المجموعات المسندة». Keep highest rating first as the default; the second option ranks by assigned group count. Use the same choice in the full list. Remove any separate duplicate coach-workload section.

Retain coach photo, name, branch, applicable sport, assigned groups, sessions, rating, and review count in readable cards/rows.

Add a decision when an active coach is below the configured assignment target. Three groups is an example, not a universal hardcoded requirement. Show a concise message such as «مدرب لديه مجموعتان من أصل 3», with «إسناد إلى مجموعة» opening the real authorized assignment flow. Respect availability, branch/sport scope, and existing constraints.

Preserve the center’s top-three summary, «كل القرارات», assignment/follow-up flows, and actionable recipient lists. Do not invent a financial loss from a low assignment count.

## 08 — العمليات الأصلية داخل الموبايل

Owner bottom navigation is now FIVE destinations: «الرئيسية»، «العمليات»، «المباريات»، «التقارير»، «المزيد». This overrides the older four-item arrangement.

Core operations must run inside the actual mobile application using its authenticated session: «اشتراك جديد»، «تجديد اشتراك»، «إضافة مدرب»، «إنشاء تقييم»، «تسجيل الحضور»، «إسناد إلى مجموعة» for assigning a coach, «تسكين لاعب», and «تعديل مجموعة». Reuse existing business logic. Embedded web forms or browser redirects do not count as native completion for these actions. Include the authorized exceptional discount in the new-enrollment form as defined in Point 25.

Remove descriptions saying the action uses the web dashboard or requires another login. Do not open incognito windows or repeatedly request credentials. Make the same operations available to authorized administrators, not automatically to every administrator.

Remove duplicated «التقرير المالي»، «طلبات الدفع», and decision-follow-up shortcuts from Operations; keep these functions in their proper existing destinations. Product-catalog management has the specific web exception in Point 23.

## 09 — المباريات للأدوار المختلفة

Redesign match cards around the TWO teams and score, with the academy crest beside its team. Use green for a win, red for a loss, amber/orange for a draw, and a distinct neutral treatment for upcoming/unplayed matches. Interpret outcomes relative to the relevant academy/selected team; missing results are not draws.

Use a compact calendar tile with a large day, month beneath, and time below. Tournament, venue, branch, and coach are concise secondary details, not a dense paragraph. Add text/icons so color is not the only status cue.

Owner retains branch/team/date filtering and actual results/statistics. Coach sees assigned teams/groups only, with no unnecessary branch filter. Guardian sees only matches relevant to linked eligible children, with «كل الأبناء» and child filters. Add the nearest upcoming eligible match to Guardian Home, alongside the next training session.

Authorized administrators can create matches, enter details, and record/update results inside mobile. Changes must appear in the appropriate Owner/Coach/Guardian views. Preserve existing tournament functionality; do not add a new fixture-generation system.

## 10 — التقارير والمستحقات المالية

Merge the current revenue breakdown and collection ledger into ONE «الإيرادات والتحصيلات» page: source-summary tiles first, «سجل التحصيلات» below, with shared branch/date filters and record drill-downs. Describe collected revenue accurately rather than treating all financial concepts as interchangeable.

Simplify «المستحقات المالية» labels according to their actual meaning, for example outstanding total, overdue, not yet due, and credit in the customer’s favor where supported. Provide isolated, reconciled demo records so each displayed amount can be traced to its source document.

Remove long technical paragraphs from the initial view; retain important distinctions in concise help. Expired subscriptions or pending payment requests must not automatically become booked debt. Keep payroll, expenses, and attendance reports organized and accessible without introducing duplicate financial engines.

## 11 — الموظفون والصلاحيات المبسطة

Add native «الموظفون والصلاحيات» under Owner «المزيد». Support the same relevant employee fields as web, including name, phone, job title, salary where applicable, and role/access assignment.

Keep the normal role experiences stable: Owner has academy-wide authority, Guardian sees linked children, and Coach sees assigned work. Fine-grained customization primarily concerns administrators. «مدير النشاط الرياضي» and «المدير الفني» are job/access arrangements within the administrator experience, not reasons to create a confusing new permissions architecture.

Provide a simple grouped action checklist/toggle screen, not a mandatory template wizard. Cover the requested capabilities: player view/add/edit; enroll/renew/cancel/refund; player attendance; staff attendance and attendance-request review; coach/group assignment and player placement; session transfers, makeups, alternative sessions and day adjustments; match management/results; news/events/ads; product management/orders; relevant financial views and delegated follow-ups.

Preserve existing branch/sport scope controls. Separate sensitive actions such as refunds and «منح خصم استثنائي» (Point 25) from ordinary editing. Apply permissions to server actions and data as well as buttons. Attendance-only staff must not gain financial access. Do not redesign fixed roles unnecessarily or weaken existing security.

## 12 — إعدادات الأكاديمية

Rebuild the affected web settings screen into clear tabs, grouping existing fields sensibly, such as «بيانات الأكاديمية»، «التشغيل والحضور»، «المالية والتحصيل», and «المظهر والرئيسية». Keep existing necessary settings; remove clutter, not functionality.

Make save/validation behavior reliable and obvious. Include the collection-target access, quick-action placement, coach player-attendance switch, staff self-check-in switch, and relevant current configuration. Avoid duplicating permission management or notification editors in several places.

Test every affected visible setting/button: save, reload, then verify its actual effect in mobile/web. This is a functional repair as well as a visual redesign.

## 13 — الأخبار والفعاليات والإعلانات

Provide native mobile creation/publishing for «الأخبار»، «الفعاليات», and «الإعلانات» for Owner and explicitly authorized administrators. Forms include relevant image/content, publishing period, target branches, and target audience: «المدربون»، «الإداريون»، «أولياء الأمور», or «الكل».

Lists come first. Show the title on the right, a distinct status badge on the left, and clear edit/delete actions. Use «نشر» as the primary publishing action; remove redundant visible refresh controls and confusing duplicate draft controls while preserving any necessary internal workflow.

Show targeted news/events on Coach, Administrator, and Guardian Home in horizontal sections. Owner manages content through «المزيد». Respect audience, branch, publication dates, and permissions. A scheduled item is not «يعرض الآن» before its start date. The same content must not leak to non-targeted users.

## 14 — رئيسية المدرب

Make «المجموعات» and «الحصص» prominent and distinct. Use a horizontal DAY selector defaulted to today, not a from/to date range, for the day’s relevant schedule.

Show the next session and next match even when they fall after today, clearly distinguished from the selected day’s list. Include assigned groups/player counts, targeted swipeable news/events, and direct access to group/player work.

Do not repeat news inside «المزيد». Remove «فترات العمل» and the redundant tutorial-library entry from Coach More. Preserve other existing useful authorized actions, including «طلب مبلغ إضافي», without adding new payroll scope.

## 15 — تقييم اللاعبين والنجوم

Coach evaluation uses the criteria actually configured for the relevant sport. Use clear 0–100 sliders initially positioned at 50 as requested, BUT an untouched default is not automatically a submitted assessment. Require deliberate adjustment or explicit confirmation to treat a criterion as evaluated. Preserve a genuine zero score.

Guardian views and performance charts show only actually evaluated, published criteria; never fill missing criteria with 50 or «غير متاح». Reuse current evaluation logic.

Keep the existing monthly star rule: THREE stars per coach per assigned team per calendar month, not three per player. The month runs from its first day to its actual last day, including February. Show remaining allowance, prevent unauthorized/duplicate/over-budget grants, and preserve existing Owner featured-player ranking and badges. Do not invent a second points system.

## 16 — حضور الموظفين وسجل الحضور

When academy settings enable in-app staff check-in/out, show a clear «حضور وانصراف» action on Coach and Administrator Home, including administrator-type technical managers. Retain existing verification/location policies; do not bypass them to simplify the UI.

Rename the historical self-attendance destination to «سجل الحضور», with dates and attendance/absence summaries. It is not the same thing as the Home check-in/out action.

Keep the existing attendance-proof/exception request for now: the latest instruction is to retain it and route review to an administrator with that explicit permission, rather than making it an Owner-only daily task.

Remove redundant «وسائل التحقق الخاصة بي» and «الحساب والإعدادات» shortcuts from More. Profile owns appearance/language/logout. Preserve security and recovery functionality where needed.

## 17 — تجربة الإداري وملف اللاعب

Use a focused administrator structure: «الرئيسية»، «العمليات»، «الحضور»، «المزيد», showing only permitted tasks. Home highlights the administrator’s real daily actions and assigned follow-ups, plus targeted content.

Provide the mobile player list/profile with the relevant existing tabs: «نظرة عامة»، «الاشتراكات والماليات»، «التدريب والحضور»، «المباريات»، «التطور», and «المزيد». Hide unauthorized data/tabs, not merely write buttons.

Make the authorized native actions actually work: add/edit player, new enrollment, renewal, attendance, cancellation, refund, group changes, selected-session transfer, compensation/alternative sessions, adding/deducting/carrying days, and related histories. Reuse their current business definitions rather than inventing new financial behavior.

Administrators with permission can manage content, matches, product orders, and delegated decisions, and assign appropriate tasks to a coach within allowed scope. Preserve player photos and relevant subscription/remaining-session notes in rosters; show sensitive notes only to authorized users.

## 18 — رئيسية ولي الأمر وبطاقات الأبناء

Replace vertically repeated child dashboards with a horizontal RTL carousel of LARGE child cards. Show one main card with a visible portion of the next to signal swiping.

Each card presents photo, name, age, branch, sport/enrollment context, subscription dates, actual published evaluation average, and stars. Show a prominent sport-appropriate performance visualization using only available assessed criteria. Keep it inside the card/selected-child area; do not squeeze an unreadable chart beside a narrow phone card. Avoid applying football axes to every sport.

Switching the active child updates all child-specific details without stale data from the previous child. Preserve multiple-sport enrollments rather than collapsing their dates and statuses into one misleading subscription.

Keep clear actions for «اشتراك جديد»، «تجديد اشتراك», and the existing «تجديد للغير». New enrollment still supports a new child or a linked child choosing another sport/branch/eligible plan.

## 19 — تجديد اشتراك أكثر من ابن

For compatible children in the SAME sport, provide checkboxes, eligible common plan selection, per-child prices/discounts, a combined total, and ONE payment-proof upload for the family renewal request.

Use the existing family/payment model where available. Persist the child-level allocations so approval updates the correct subscriptions and financial records exactly once. Confirming/retrying must not duplicate payment or renewal. Show accurate statuses; never claim all children renewed if only some were processed.

Same sport alone does not make every plan eligible: also validate the actual branch, age, registration, plan and currency rules. For children in DIFFERENT sports, retain separate per-child renewal as requested; do not expand this into a universal multi-sport cart. Explain that distinction briefly in the UI.

## 20 — تنقل ولي الأمر والتغذية والمواعيد

Use Guardian bottom navigation «الرئيسية»، «الحصص»، «المباريات»، «التقييمات», avoiding a redundant «المزيد». Preserve access to existing purchases, rewards, complaints, and other relevant functions through appropriate Home/profile/child destinations; do not silently remove them.

Home includes a single horizontally swipeable nutrition row with each child’s name, meal imagery, and their configured daily content. Also show targeted news/events, relevant tournaments, each child’s nearest session, and the nearest match only when eligible.

Full session/match destinations support «كل الأبناء» and child selection. A combined monthly calendar with child-specific colors and session/match filters is an OPTIONAL later enhancement, not a blocker for the core scope; reuse an existing calendar if suitable.

Child detail preserves attendance history, player card/report, progress, badges/stars, nutrition/health, injuries/consultations, and media. Keep these organized without duplicate attendance destinations.

## 21 — صور وفيديوهات التمرين

Allow authorized Coach/Administrator users to upload training photos/videos from the relevant session/group, using an action such as «صور وفيديوهات التمرين».

Add a media section near the end of Guardian Home. Deliver each item ONLY to authorized guardians of children eligible for that session/group under existing membership/publication/privacy rules. Do not distribute it to the whole academy or use public unprotected links for private media.

Preserve existing publication/consent policies. Verify both positive access and a guardian from another group being denied access.

## 22 — التنبيهات التلقائية وقوالب الرسائل

Add/complete a web «التنبيهات» management section with event/condition, editable Arabic template, recipients, enablement, relevant timing, and delivery history/status. Reuse the shared notification module; do not build an unrelated second inbox.

Provide working templates and automatic triggers for at least: new enrollment; single/family renewal; approaching expiry; expired subscription; recorded absence; repeated absence threshold; newly published evaluation; added/deducted days; and relevant existing star, match, payment and reward events.

Use dynamic names and correct context, for example «تم نشر تقييم جديد لـ{اسم_اللاعب}», «تم تجديد اشتراك {أسماء_الأبناء}», and «غاب {اسم_اللاعب} عن حصة اليوم». Repeated-absence thresholds must use a defined policy/period, not trigger repeatedly on every background scan.

Only trigger from actual committed business events or eligible scheduled conditions. Avoid duplicates after retries, edits, or repeated scheduler runs. Distinguish per-session absence from a threshold alert. Notification links open the correct authorized record.

Verify actual recipient inbox delivery for Owner/Administrator/Coach/Guardian as applicable, including variable substitution, unread state, and navigation. A successful create API or «تم الإرسال» label alone is insufficient evidence. Verify external push only if it is configured and in scope; do not claim SMS/push delivery merely because an in-app record exists. Use isolated test recipients, not real customer broadcasts.

## 23 — المنتجات الرياضية والمقاسات والأرقام

Enhance existing products with size variants and inventory for each size. For pre-numbered uniforms, track available SHIRT NUMBER plus quantity within the size, rather than an unexplained size total. Unnumbered products retain ordinary size/quantity inventory.

Use this simple number-selection flow: an optional player shirt number belongs to the relevant team/enrollment context; if assigned and available in the chosen size, preselect it. Otherwise show the actual available numbers and let the guardian explicitly confirm the number, or explain unavailability. Never silently substitute another shirt number. Do not require a player to have one global number across all sports.

Show the selected child, size, shirt number and quantity before purchase and in the order. Preserve existing stock reservation/confirmation/cancellation behavior and test competing purchases for the last available item. Do not create a separate complex warehouse system or serialize every garment unnecessarily.

Owner «المزيد» may include «إدارة المنتجات الرياضية» opening the WEB management screen; this is an explicit exception to native editing. Authorized administrators may also manage products/orders. Use a safe authenticated handoff where supported, never credentials/tokens in ordinary shareable URLs or forced incognito. Guardian purchase and relevant order handling remain properly integrated.

## 24 — استيراد أكاديمية مترابطة من ملف Excel واحد

### الهدف والبساطة

This is HIGH PRIORITY: import the operational starting data of ONE selected, authorized academy from ONE `.xlsx` workbook, not merely a list of player names. The intended result is connected branches, sports, coaches, groups, weekly schedules, plans, guardians, players, opening subscriptions, applicable offers, tournaments and participation links. Existing compatible records may be reused after validation. Do not create/link unrelated academies or cross tenant boundaries.

Keep the workbook easy for a nontechnical administrator. Use short human-readable codes such as `B01`, `S01`, `C01`, `G01`, `P01`, `T01`, not database GUIDs. Mark required versus optional columns clearly. Optional modules may have empty sheets; an academy without tournaments/offers must not be forced to invent them. Explain references in a short «دليل الاستخدام», with practical examples and dropdowns where suitable. Retain supported CSV imports, but the requested multi-sheet template is XLSX.

Provide real working «تنزيل قالب فارغ» and «تنزيل نموذج مكتمل» actions. Both must generate/download the actual format accepted by the importer, not decorative filenames copied from the prototype.

### الصفحات والربط

Keep the existing planned sheets, and add the missing «المدربون» and «البطولات». Do not expand this into a complicated ERP migration workbook.

| الصفحة | الحد الأدنى للمحتوى والربط |
|---|---|
| «دليل الاستخدام» | Short instructions, required/optional fields, code relationships, date/phone format and one connected example. |
| «الفروع» | Branch code/name and only the fields genuinely required by the existing model. |
| «الرياضات» | Sport code/name. |
| «الفئات العمرية» | Category code/name and supported birth-year/age boundaries. |
| «مستويات التدريب» | Codes/names when used; optional when not applicable. |
| «أنماط التدريب» | Prefilled supported values such as «جماعي»، «خاص»، «شبه خاص»; do not invent unsupported modes. |
| «المدربون» — added | Coach code, name, phone, applicable sport/branch references and only required existing staff fields. Reuse the current identity/membership rules; do not grant administrative privileges. Salary or advanced employment fields stay optional unless the existing model genuinely requires them. |
| «المجموعات» | Code/name, capacity, branch, sport, age category, applicable level/mode and «كود المدرب» / «أكواد المدربين» to create the actual group assignments. Accept multiple coaches only where the existing model supports them; document the simple delimiter. |
| «الحصص» | Group code, one row per weekly day/time pattern, start/end time and supported validity dates/duration. Generate dated sessions using the existing scheduler, without duplicating sessions. |
| «الباقات» | Code/name, applicable sport/scope, price and the existing Duration/Sessions/Combined configuration and required fields. |
| «العروض» | Optional supported persistent offers/discounts and their codes; reuse the current engine. This is not a new exceptional-discount catalog. |
| «البطولات» — added | «كود البطولة», «اسم البطولة», «كود الرياضة», applicable branch/scope, start/end dates when required, plus only genuinely mandatory existing tournament fields. Preserve existing fee rules without inventing payments. |
| «الأعضاء» | Stable player code, name, date of birth, gender, guardian name/phone, branch/sport/category/group/plan codes, optional offer code, opening subscription state and optional «أكواد البطولات». |

In «الأعضاء», blank «أكواد البطولات» means no tournament membership. A single value such as `T01` links the player to that tournament. Where multiple memberships are supported, allow a simple documented value such as `T01,T02`, normalize whitespace/separators, validate every reference and create each participation once. Tournament membership is NOT another training subscription and must not duplicate a player or charge them again.

**Groups and teams are not interchangeable.** Inspect the current tournament model. If participation/match eligibility requires a team, use the actual team entity and explicit membership. Add an OPTIONAL minimal «الفرق» sheet and «كود الفريق» on the relevant member/participation row only when needed to represent that existing model. Do not automatically turn every training group into a team or assign a player to an arbitrary team. Explain and block unresolved/ambiguous team mappings before confirmation. Keep this addition conditional; do not force extra team/participation sheets on academies that do not need them.

Keep guardian details within «الأعضاء» as requested; a separate mandatory guardians sheet is not needed. Resolve siblings through the existing normalized identity/linking rules, not by guessing from names. An optional stable guardian code is acceptable when necessary to disambiguate. Multiple sports must reuse the same player and guardian while creating distinct valid sport enrollments. Use stable player/enrollment identifiers so repeated rows/imports cannot create duplicate subscriptions.

### حالة الاشتراك والبيانات الافتتاحية

For an existing subscription, include only the necessary opening-state fields: applicable start/end dates or remaining sessions, valid plan/group links, and explicitly supported agreed price/opening balances where required. Unknown financial values must be reported, not guessed. Preserve actual dates, currency and tenant rules. Importing an old subscription is NOT cash collected today; do not create fake receipts, current revenue, duplicate payments, attendance history or match results.

The goal is to create the connected system represented by the workbook, not fabricate records that were not supplied. A player's group enrollment determines access to appropriate generated sessions through the existing roster rules. Tournament participation makes the relevant existing tournament/matches accessible under current rules; it does not invent fixtures or results.

### الرفع والتحقق والتأكيد

Use the existing background import/staging machinery where suitable. Flow: upload → validate workbook and cross-sheet references → preview counts and relationships → explicit confirmation → safe publication → clear result.

Preserve leading zeros and `+` in phone/code cells and make dates unambiguous. Handle a realistic workbook of 200–300 players without per-player manual setup. Resolve reference sheets before dependent memberships/records regardless of sheet order. Show sheet name, row, field, bad code and a short correction message for each error. Validate duplicates, scope, plan suitability, group capacity, schedule conflicts, guardian ambiguity and unknown coach/tournament/team codes using existing rules.

Never silently publish a partially broken academy. Use validated staging and the existing safe commit strategy; if any segment cannot publish, keep the failed job uncommitted/isolated with a truthful recoverable result, not a success banner. Do not reset existing customer data. Preview create/update/reuse effects and do not overwrite unrelated existing records. Re-uploading the same successful workbook must not duplicate players, guardians, assignments, sessions, subscriptions, tournament links or financial effects. Retrying after interruption must be safe.

After import, verify the actual linked records on web AND mobile: coach → assigned groups → sessions/roster; player → sport/group/plan → correct opening subscription; guardian → correct children only; tournament → eligible team/player → relevant match access. Include an explicit total for tournament participation and coach assignments in the result, not just player count. No real welcome/OTP/broadcast notification is sent silently during migration/tests.

### المخرجات والاختبارات المطلوبة

Deliver the ACTUAL blank workbook and TEN-distinct-player fictional sample, both downloadable from the app and accepted by its importer. Include siblings, at least two coaches/groups, two tournaments, a player with no tournament, a player with one tournament and (when supported) a player with two; include a multi-sport player without duplicated identity. Add the minimum optional team data if the existing model requires it.

Separately test a generated 200–300-player workbook in an isolated environment to verify the intended onboarding scale. Test a bad coach/tournament/group reference, correcting and re-uploading it, repeated successful upload, interruption/retry and tenant isolation. Verify no fake current-day collection and no duplicate financial/subscription effects. Supply the XLSX files, actual reconciliation counts and test evidence. A hand-built workbook that is unrelated to the live importer or a prototype progress animation is not completion.

## 25 — خصم استثنائي يدوي عند الاشتراك الجديد

Add a simple optional «إضافة خصم استثنائي» action inside the EXISTING «اشتراك جديد» flow for the Owner and an explicitly authorized Administrator. This is a one-off exception entered manually during that enrollment, not a requirement to first create a permanent offer and not a change to the general package price.

Use a compact area with «نوع الخصم»: «مبلغ ثابت» / «نسبة مئوية», «قيمة الخصم», and a short «سبب الخصم». Show «السعر قبل الخصم»، «قيمة الخصم», and «الإجمالي بعد الخصم» immediately and on the final review. The same capability and meaning must apply in native mobile and the corresponding existing web new-enrollment form.

Add one understandable permission «منح خصم استثنائي» to the existing Owner-managed administrator action checklist. Administrators need permission for both enrollment and this sensitive action, within their current academy/branch scope. Ordinary enrollment or attendance permission alone must not authorize it. Enforce this at the API, not just by hiding controls. Do not add a mandatory approval workflow unless an existing configured policy already requires one.

Reuse the existing pricing/discount/audit/payment machinery. The server recalculates the final amount; the client cannot choose an arbitrary payable total. Percentage must be positive and no more than 100; a fixed amount cannot exceed the eligible price and the result cannot be negative. Use existing currency rounding and the supported zero-price flow when a full discount is allowed. Reject invalid values clearly.

Preserve the current offer/discount-combination rule: do not silently stack a manual exception on top of sibling/VIP/persistent offers. Display the current offer and the actual applicable rule; where the current model allows one discount, explain that an explicit replacement is required rather than secretly applying both. Do not invent a second pricing engine or an automatic permanent family discount.

Persist the original price, discount type/value, applied discount amount, final price, reason, actor and timestamp against the actual enrollment/financial source. Show the real adjustment in the receipt/subscription and authorized relevant views, including the guardian's price breakdown. Keep internal audit/reason visibility within existing permissions. The exception affects this enrollment only, not other players or later renewals unless separately authorized under an existing rule.

Verify native/web consistency, unauthorized API rejection, fixed and percentage cases, limits/rounding, existing-offer conflicts, save/reload and retry/idempotency. Example test cases with no competing offer: price 1,000 with a fixed 100 discount → 900; price 1,000 with 15% → 850. A double tap/network retry must not apply the discount twice or duplicate payment/enrollment.

## التنفيذ والتحديثات والتسليم

Maintain a persistent checklist for Points 00–25 with existing/changed/new/not-applicable status. Resolve implementation sequencing sensibly; Point 24 is not optional just because it is last in this document. Preserve the established stack and avoid unnecessary rewrites or unrelated features.

Before starting a point, send a short Arabic update: «بدأت النقطة X — [الاسم]». After finishing it, send «اكتملت النقطة X» only when accurate, stating what works and what was tested. Report meaningful intermediate progress without private chain-of-thought. Continue automatically without routine approval requests; record genuine blockers and continue independent work.

Validate complete user flows, permissions, branch/tenant isolation, numerical reconciliation, responsive RTL layout, and settings after reload. Use actual small and typical phone screens plus desktop. Native workflows must be verified in the real mobile app; web mockups do not count. Test notifications at the recipient and imports through the real download/upload path.

Use isolated demo/test data and recipients. Do not reset existing customer data, send real broadcasts, or deploy to production without authorization. Keep unresolved external assets/integrations explicit rather than claiming completion.

Final report must be in Arabic, with one row for EVERY point:

| النقطة | ما تم | حالة الموبايل | حالة الويب عند الانطباق | دليل الاختبار/الصورة | المتبقي أو العائق |
|---|---|---|---|---|---|

Distinguish «مكتمل ومختبر», «منفذ ولم يتم التحقق», and «غير مكتمل». Include actual screenshots compared with the mapped design, tests executed, changed branch/commit reference, safe demo instructions, both Excel files, the connected-import reconciliation and exceptional-discount evidence, and any remaining blocker. Finish the complete agreed goal, not only the first successful screen or API.
