# حسابات Demo — Slice 1

هذه بيانات صناعية للاستخدام المحلي/العرض فقط. تتطلب `ASPNETCORE_ENVIRONMENT=Demo` والإعدادات الصريحة في `README.md`. لا تستخدم القيم نفسها أو seed في Production.

| الأكاديمية | الاسم | الدور | المعرّف |
|---|---|---|---|
| أكاديمية النجوم الرياضية | أحمد محمد | AcademyOwner | `owner.nogoom@example.test` |
| أكاديمية النجوم الرياضية | منى السيد | AcademyAdmin | `admin.nogoom@example.test` |
| أكاديمية النجوم الرياضية | كريم حسن | Coach | `coach.nogoom@example.test` |
| أكاديمية النجوم الرياضية | سارة محمود | Guardian | `+201000000001` أو `01000000001` |
| أكاديمية المستقبل الرياضية | محمود علي | AcademyOwner | `owner.future@example.test` |

- كلمة مرور staff المحلية: `Demo-Only-123!` عبر `Demo__StaffPassword`.
- OTP ولي الأمر المحلي: `246810` عبر `Demo__FixedOtp`.
- seed idempotent ولا ينشئ Player أو طفلًا أو علاقة ولي أمر بطفل.
- الحسابات منفصلة افتراضيًا بين الأكاديميتين، وتُستخدم أكاديمية المستقبل لاختبارات العزل السلبية.
