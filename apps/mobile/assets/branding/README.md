# هوية iAcademy One — 25 أكتوبر 2026

المصدر: ملفات SVG وPNG المسطحة من `Design package complete.zip` الذي قدمه المستخدم. المراجع إلهام غير معتمد تلقائيًا؛ اختيرت نسخة المسار المسطح بعد فحص المرجعين واللقطات، ولم تستخدم النسخ المعدنية ثلاثية الأبعاد. أزيلت بيانات XML الوصفية المضمّنة فقط؛ حافظت المصادر على الهندسة والألوان.

- `iacademy-symbol-on-dark.svg` و`iacademy-symbol-on-light.svg`: الرمز المستقل.
- `iacademy-app-icon-master-1024.svg` و`iacademy-icon.png`: مصدر الأيقونة الكاملة. صُدّرت أحجام iOS وAndroid من نفس PNG. لا تغيير في bundle/application IDs.
- `iacademy-wordmark-horizontal-dark.svg`: أصل قابل لإعادة الاستخدام. واجهة الدخول تستخدم نصًا حيًا `iAcademy One` إلى جوار الرمز لضمان وضوح حجم الهاتف وإمكانية الوصول.
- `professional-academy.png`: الأصل التجريبي السابق محفوظ؛ لم يعد هوية المنصة قبل الدخول. شعار الأكاديمية الفعلي يأتي من المسار المصرح به `/api/v1/academy/branding/logo`.

الحركة: `lib/core/widgets/brand_motion.dart` و`lib/features/shared/academy_reveal.dart`. لا فيديو لكل عميل، ولا حرق للشعار أو الاسم في الخلفية. سطح الشعار يُختار من بكسلاته المرئية دون تغيير ألوانه؛ الاحتواء يحفظ الشعارات العريضة.

الشاشة الأصلية للنظام ثابتة بالكحلي والرمز، والحركة داخل Flutter. إضافة أيقونات العملاء المدفوعة وإتاحتها بند R15 مستقل لم يُنفذ في هذه الدفعة.

WhatsApp glyph: Simple Icons CC0 source https://github.com/simple-icons/simple-icons/blob/develop/icons/whatsapp.svg (retrieved25 October2026). Used only to identify the user-initiated contact action. Click to Chat syntax verified against https://faq.whatsapp.com/5913398998672934. No message is sent automatically.
