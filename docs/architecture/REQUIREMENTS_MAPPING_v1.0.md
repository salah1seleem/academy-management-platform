# Requirements Mapping v1.0

هذه مصفوفة تغطية لا تغيّر حالة أي مطلب. مراحل التسليم المقترحة: `S0` foundation، `S1` tenant/auth، `S2` structure/people، `S3` subscription/finance، `S4` attendance، `S5` evaluation، `S6` guardian core، `S7` content، `S8` reports/preserved capabilities، `S9` production gate، و`Cross` مستمر. `Excluded` يعني تنفيذ الاستبعاد نفسه، لا إسقاط المعرف.

| ID | الحالة الأصلية | المالك | مسؤولية الشاشة/API | المرحلة | قبول/ديمو |
|---|---|---|---|---|---|
| AC-GOV-001 | ثابت بطلب المستخدم | Governance | حدود الاستقلال والمراجعة | S0 | AT-001 |
| AC-GOV-002 | ثابت بطلب المستخدم | Tenancy | Academy branding/isolation | S1 | AT-003, DM-10 |
| AC-GOV-003 | ثابت بطلب المستخدم | Delivery | بوابات النطاق والوقت | Cross | DM-01 |
| AC-GOV-004 | ثابت بطلب المستخدم | Platform | API واحد وواجهات الأدوار | S0 | AT-004, AT-032 |
| AC-GOV-005 | ثابت بطلب المستخدم | Web/Branding | RTL/localization/theme | S0 | AT-002 |
| AC-GOV-006 | ضابط تنفيذ مقترح | Governance | changelog/traceability | Cross | — |
| AC-ORG-001 | اقتراح سابق غير معتمد نهائياً | Structure | إعداد Academy API | S2 | AT-003 |
| AC-ORG-002 | ثابت بطلب المستخدم | Structure | فروع/رياضات/فئات | S2 | AT-015 |
| AC-ORG-003 | ثابت بطلب المستخدم | Structure | Group CRUD/assignment | S2 | AT-006, AT-015 |
| AC-ORG-004 | ثابت بطلب المستخدم | Structure | Staff profiles/assignments | S2 | AT-016, DM-07 |
| AC-ORG-005 | ضابط تنفيذ مقترح | Structure | schedule/session generation | S4 | AT-016 |
| AC-IAM-001 | ثابت بطلب المستخدم | Identity | role routes/policies | S1 | AT-002, DM-10 |
| AC-IAM-002 | ضابط تنفيذ مقترح | Identity | individual accounts/roles | S1 | AT-003 |
| AC-IAM-003 | ثابت بطلب المستخدم | Web/Admin | admin home actions | S2 | AT-004, AT-016 |
| AC-IAM-004 | ثابت بطلب المستخدم | Web/Coach | assigned groups/evaluate | S5 | AT-015, AT-020 |
| AC-IAM-005 | ثابت بطلب المستخدم | Web/Owner | owner dashboard/settings | S8 | AT-028 |
| AC-IAM-006 | ثابت بطلب المستخدم | Identity/People | guardian activation/link | S1 | AT-005, AT-014 |
| AC-IAM-007 | ضابط تنفيذ مقترح | Authorization | resource policies/exports/media | S1 | AT-003, AT-015, AT-027 |
| AC-IAM-008 | اقتراح سابق غير معتمد نهائياً | Identity | optional player account | S8 | — |
| AC-PLY-001 | ثابت بطلب المستخدم | People | player list/profile | S2 | AT-004 |
| AC-PLY-002 | ثابت بطلب المستخدم | People | GuardianPlayerLink | S2 | AT-005, DM-02 |
| AC-PLY-003 | ثابت بطلب المستخدم | Enrollment | SportEnrollment | S2 | AT-006, DM-03 |
| AC-PLY-004 | ثابت بطلب المستخدم | Enrollment | reuse player on renewal/sport | S2 | AT-010 |
| AC-PLY-005 | ثابت بطلب المستخدم | People | scoped search/filters | S2 | AT-015 |
| AC-PLY-006 | ثابت بطلب المستخدم | Enrollment | admin registration API | S2 | AT-004, DM-01 |
| AC-PLY-007 | ضابط تنفيذ مقترح | People | archive/audit rules | S2 | AT-010 |
| AC-SUB-001 | ثابت بطلب المستخدم | Subscriptions | plan management | S3 | AT-011 |
| AC-SUB-002 | وظيفة مرجعية محفوظة | Subscriptions | duration/session/combined | S3/S8 | AT-017, DM-09 |
| AC-SUB-003 | ثابت بطلب المستخدم | Subscriptions | periods/status/balance | S3 | AT-011, DM-01 |
| AC-SUB-004 | ثابت بطلب المستخدم | Subscriptions | admin renewal | S3 | AT-010, AT-013 |
| AC-SUB-005 | ثابت بطلب المستخدم | Subscriptions | guardian renewal request | S3/S6 | AT-012 |
| AC-SUB-006 | ثابت بطلب المستخدم | Subscriptions | renewal-other reference | S3/S6 | AT-014, DM-05 |
| AC-SUB-007 | ضابط تنفيذ مقترح | Subscriptions | period history/early renewal | S3 | AT-011 |
| AC-SUB-008 | وظيفة مرجعية محفوظة | Subscriptions | freeze adjustment | S8 | AT-029, DM-09 |
| AC-SUB-009 | وظيفة مرجعية محفوظة | Subscriptions | day adjustment | S8 | AT-029, DM-09 |
| AC-SUB-010 | وظيفة مرجعية محفوظة | Subscriptions | cancellation | S8 | AT-029, DM-09 |
| AC-SUB-011 | وظيفة مرجعية محفوظة | Subscriptions | discount calculation/audit | S8 | AT-029, DM-09 |
| AC-SUB-012 | اقتراح سابق غير معتمد نهائياً | Subscriptions | expiring/expired query | S3 | AT-031, DM-12 |
| AC-ATT-001 | ثابت بطلب المستخدم | Attendance | trainee/staff roster | S4 | AT-016, DM-07 |
| AC-ATT-002 | ضابط تنفيذ مقترح | Attendance | per-session unique record | S4 | AT-016 |
| AC-ATT-003 | وظيفة مرجعية محفوظة | Attendance | code lookup | S8 | AT-016 |
| AC-ATT-004 | وظيفة مرجعية محفوظة | Attendance | history/calendar | S4 | AT-016, DM-07 |
| AC-ATT-005 | ضابط تنفيذ مقترح | Attendance/Subscriptions | session balance transaction | S4 | AT-017 |
| AC-ATT-006 | وظيفة مرجعية محفوظة | Reporting | monthly XLSX export | S8 | AT-018 |
| AC-ATT-007 | اقتراح سابق غير معتمد نهائياً | Web/Admin | bulk attendance UX | S4 | AT-016 |
| AC-EVA-001 | ثابت بطلب المستخدم | Evaluations | criteria admin/coach scoring | S5 | AT-019, DM-06 |
| AC-EVA-002 | ثابت بطلب المستخدم | Evaluations | draft/publish | S5 | AT-020, DM-06 |
| AC-EVA-003 | ضابط تنفيذ مقترح | Evaluations | nullable score validation | S5 | AT-019 |
| AC-EVA-004 | ثابت بطلب المستخدم | Evaluations/Web | report header/position | S5 | AT-021 |
| AC-EVA-005 | ثابت بطلب المستخدم | Evaluations | football six axes | S5 | AT-022 |
| AC-EVA-006 | ثابت بطلب المستخدم | Evaluations/People | age/measurements/foot | S5 | AT-021 |
| AC-EVA-007 | ثابت بطلب المستخدم | Evaluations/Web | detailed criteria/no pitch | S5 | AT-021 |
| AC-EVA-008 | اقتراح سابق غير معتمد نهائياً | Evaluations | aggregate formula/mapping | S5 | AT-022 |
| AC-EVA-009 | وظيفة مرجعية محفوظة | Evaluations | period projection/trends | S5/S8 | AT-022 |
| AC-EVA-010 | ضابط تنفيذ مقترح | Evaluations | sport-specific report template | S5 | AT-023 |
| AC-PAR-001 | ثابت بطلب المستخدم | Web/Guardian | Arabic mobile visual system | S6 | AT-002 |
| AC-PAR-002 | ثابت بطلب المستخدم | Web/Guardian | child cards | S6 | AT-005, DM-02 |
| AC-PAR-003 | ثابت بطلب المستخدم | Web/Guardian | deduped sport sections | S6 | AT-007, DM-03, DM-04 |
| AC-PAR-004 | ثابت بطلب المستخدم | Web/Guardian | three persistent actions | S6 | AT-008 |
| AC-PAR-005 | ثابت بطلب المستخدم | Enrollment/Web | new enrollment request | S6 | AT-010 |
| AC-PAR-006 | ثابت بطلب المستخدم | Web/Guardian | enrollment context selector | S6 | AT-006 |
| AC-PAR-007 | ثابت بطلب المستخدم | Web/Guardian | profile navigation | S6 | AT-009 |
| AC-PAR-008 | ثابت بطلب المستخدم | Web/Guardian | schedule section | S6 | AT-009 |
| AC-PAR-009 | ثابت بطلب المستخدم | Web/Guardian | contextual renewal | S6 | AT-010 |
| AC-PAR-010 | مستبعد من النسخة الحالية بطلب المستخدم | Governance/Web | absence of intensive/AI UI | Excluded | AT-009 |
| AC-SPP-001 | ثابت بطلب المستخدم | Sport Products | catalog admin/read | S7 | AT-007 |
| AC-SPP-002 | وظيفة مرجعية محفوظة | Sport Products | product card projection | S7 | AT-007 |
| AC-SPP-003 | ضابط تنفيذ مقترح | Sport Products | tenant/sport visibility | S7 | AT-003, AT-007 |
| AC-SPP-004 | اقتراح سابق غير معتمد نهائياً | Sport Products | commerce boundary | S8 | — |
| AC-SPP-005 | ثابت بطلب المستخدم | Sport Products/Demo | seeded football/swimming catalog | S7 | DM-02, DM-04 |
| AC-NUT-001 | ثابت بطلب المستخدم | Nutrition | information-only library | S7 | AT-025, DM-08 |
| AC-NUT-002 | ثابت بطلب المستخدم | Nutrition/Web | three tabs | S7 | AT-024 |
| AC-NUT-003 | ثابت بطلب المستخدم | Nutrition/Web | info card without commerce | S7 | AT-025 |
| AC-NUT-004 | ثابت بطلب المستخدم | Nutrition/Web | detail/nutrients | S7 | AT-026 |
| AC-NUT-005 | ثابت بطلب المستخدم | Nutrition/Demo | preseeded content | S7 | AT-024, DM-08 |
| AC-NUT-006 | وظيفة مرجعية محفوظة | Nutrition/Demo | 18 placements/15 items | S7 | AT-024 |
| AC-NUT-007 | ثابت بطلب المستخدم | Nutrition | prohibit commerce model/API | S7 | AT-025, AT-028 |
| AC-NUT-008 | ضابط تنفيذ مقترح | Nutrition | serving/source/review status | S7 | AT-026 |
| AC-NUT-009 | ضابط تنفيذ مقترح | Nutrition | no personalized prescription | S7 | AT-026 |
| AC-NUT-010 | اقتراح سابق غير معتمد نهائياً | Nutrition | status/order/editing | S8 | AT-024 |
| AC-MED-001 | ثابت بطلب المستخدم | Medical | child record read source | S7 | AT-027, DM-11 |
| AC-MED-002 | اقتراح سابق غير معتمد نهائياً | Medical | record write/publish fields | S7 | AT-027 |
| AC-MED-003 | ضابط تنفيذ مقترح | Medical/Demo | restricted/synthetic display | S7 | AT-027, AT-033, DM-11 |
| AC-GAL-001 | ثابت بطلب المستخدم | Gallery | child media gallery | S7 | AT-027, DM-11 |
| AC-GAL-002 | اقتراح سابق غير معتمد نهائياً | Gallery | upload/metadata/publish | S7 | AT-027 |
| AC-GAL-003 | ضابط تنفيذ مقترح | Gallery/Authorization | private media delivery | S7 | AT-003, AT-027 |
| AC-FIN-001 | ثابت بطلب المستخدم | Collections | subscription collections only | S3 | AT-028 |
| AC-FIN-002 | ضابط تنفيذ مقترح | Collections | immutable operation ledger | S3 | AT-028 |
| AC-FIN-003 | ضابط تنفيذ مقترح | Collections | pending vs confirmation | S3 | AT-012 |
| AC-FIN-004 | وظيفة مرجعية محفوظة | Collections | receipt/print projection | S8 | AT-029, DM-09 |
| AC-FIN-005 | ضابط تنفيذ مقترح | Collections | atomic idempotent confirmation | S3 | AT-013 |
| AC-RPT-001 | وظيفة مرجعية محفوظة | Reporting | owner dashboard | S8 | AT-028 |
| AC-RPT-002 | وظيفة مرجعية محفوظة | Reporting | financial query/export | S8 | AT-018, AT-028 |
| AC-RPT-003 | وظيفة مرجعية محفوظة | Reporting | operational reports | S8 | AT-018, DM-12 |
| AC-RPT-004 | ضابط تنفيذ مقترح | Reporting | metric definitions | S8 | AT-028 |
| AC-COM-001 | وظيفة مرجعية محفوظة | Communications | guardian bot capability | S8 | DM-09 |
| AC-COM-002 | ضابط تنفيذ مقترح | Communications/IAM | secure bot linking | S8 | AT-003 |
| AC-COM-003 | وظيفة مرجعية محفوظة | Communications | scoped message dispatch | S8 | DM-09 |
| AC-COM-004 | وظيفة مرجعية محفوظة | Reporting/Communications | ranking projection | S8 | AT-022 |
| AC-COM-005 | وظيفة مرجعية محفوظة | Identity/Support | unspecified reference entries | S8 | — |
| AC-UX-001 | ثابت بطلب المستخدم | Web | mobile-first interaction | Cross | AT-002 |
| AC-UX-002 | ضابط تنفيذ مقترح | Web/API | loading/error/empty/persistence | Cross | AT-032, AT-033 |
| AC-UX-003 | ثابت بطلب المستخدم | Web/Reporting | Arabic dates/numbers/units | Cross | AT-002 |
| AC-DEMO-001 | ثابت بطلب المستخدم | Demo | per-slice fixtures/journey | Cross | DM-01..12 |
| AC-DEMO-002 | ثابت بطلب المستخدم | Demo | synthetic Egyptian names | Cross | DM-01, DM-02 |
| AC-DEMO-003 | ثابت بطلب المستخدم | Demo | linked edge scenarios | Cross | DM-02..07 |
| AC-DEMO-004 | ضابط تنفيذ مقترح | Demo | injected reference clock | Cross | AT-031, DM-12 |
| AC-DEMO-005 | ثابت بطلب المستخدم | Demo/Nutrition | ready information seed | S7 | AT-024..026, DM-08 |
| AC-DEMO-006 | ضابط تنفيذ مقترح | Demo | idempotent scoped reset | Cross | AT-030 |
| AC-DEMO-007 | ضابط تنفيذ مقترح | Demo/IAM | role accounts/second academy | S1 | AT-003, DM-10 |
| AC-DEMO-008 | ثابت بطلب المستخدم | Demo | end-to-end story | S6 | DM-01 |
| AC-DEL-001 | ثابت بطلب المستخدم | Delivery | docs-first/small slices | S0 | AT-001 |
| AC-DEL-002 | اقتراح سابق غير معتمد نهائياً | Architecture | approved technical candidate | S0 | AT-032 |
| AC-DEL-003 | اقتراح سابق غير معتمد نهائياً | Delivery/Web | PWA-first recommendation | S0 | AT-002 |
| AC-DEL-004 | اقتراح سابق غير معتمد نهائياً | Delivery | repository/branch/test layout | S0 | — |
| AC-DEL-005 | ضابط تنفيذ مقترح | Operations | production readiness gate | S9 | AT-034 |
| AC-DEL-006 | اقتراح سابق غير معتمد نهائياً | Governance | prohibit unapproved expansions | Excluded | AT-034 |

## نتيجة العد

مصدر العد هو العناوين `#### AC-*` في baseline، لا الرقم المكتوب في المقدمة. النتيجة المستهدفة للفحص الآلي: **117 occurrence، 117 unique، 0 duplicate، 0 missing، 0 extra** في هذه المصفوفة. سيناريوهات المرجع الموجودة: **12 DM** و**34 AT**؛ سجل القرارات يغطي **12 OD** كل واحد مرة.
