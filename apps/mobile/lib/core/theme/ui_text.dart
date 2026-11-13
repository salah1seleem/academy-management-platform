import 'package:flutter/material.dart';

String trainingRegistrationLabel(Object? value) => switch (value) {
  'Private' => 'تدريب خاص',
  'SemiPrivate' => 'تدريب شبه خاص',
  _ => 'تدريب جماعي',
};

// Translate interface vocabulary only. Names, notes and other academy content
// remain exactly as entered; they are not sent to an external translation service.
String ui(BuildContext context, String value) {
  if (Localizations.localeOf(context).languageCode != 'en') return value;
  if (_english.containsKey(value)) return _english[value]!;
  var result = value;
  for (final entry in _phrases.entries) {
    result = result.replaceAll(entry.key, entry.value);
  }
  return result;
}

class UiText extends StatelessWidget {
  final String data;
  final TextStyle? style;
  final TextAlign? textAlign;
  final TextDirection? textDirection;
  final TextOverflow? overflow;
  final int? maxLines;
  final bool? softWrap;
  final String? semanticsLabel;
  const UiText(
    this.data, {
    super.key,
    this.style,
    this.textAlign,
    this.textDirection,
    this.overflow,
    this.maxLines,
    this.softWrap,
    this.semanticsLabel,
  });
  @override
  Widget build(BuildContext context) {
    final muted =
        style?.color == Colors.white60 ||
        style?.color == Colors.white54 ||
        style?.color == Colors.white70;
    final adaptiveStyle =
        muted && Theme.of(context).brightness == Brightness.light
        ? style?.copyWith(color: Theme.of(context).colorScheme.onSurfaceVariant)
        : style;
    final translated = ui(context, data);
    final currency = RegExp(
      r'(-?[0-9][0-9,]*(?:\.[0-9]+)?) ([A-Z]{3}|ج\.م|ر\.س|د\.إ|جنيه)(?=\s|$|[،؛.,·)])',
    ).allMatches(translated).toList();
    if (currency.isNotEmpty) {
      final base =
          adaptiveStyle?.fontSize ??
          DefaultTextStyle.of(context).style.fontSize ??
          14;
      final spans = <InlineSpan>[];
      var end = 0;
      for (final match in currency) {
        if (match.start > end) {
          spans.add(TextSpan(text: translated.substring(end, match.start)));
        }
        spans.add(TextSpan(text: match[1]));
        spans.add(
          TextSpan(
            text: ' ${match[2]}',
            style: TextStyle(
              fontSize: (base * .55).clamp(9, 12).toDouble(),
              fontWeight: FontWeight.w400,
            ),
          ),
        );
        end = match.end;
      }
      if (end < translated.length) {
        spans.add(TextSpan(text: translated.substring(end)));
      }
      return Text.rich(
        TextSpan(style: adaptiveStyle, children: spans),
        textAlign: textAlign,
        textDirection: textDirection,
        overflow: overflow,
        maxLines: maxLines,
        softWrap: softWrap,
        semanticsLabel: semanticsLabel == null
            ? translated
            : ui(context, semanticsLabel!),
      );
    }
    return Text(
      translated,
      style: adaptiveStyle,
      textAlign: textAlign,
      textDirection: textDirection,
      overflow: overflow,
      maxLines: maxLines,
      softWrap: softWrap,
      semanticsLabel: semanticsLabel == null
          ? null
          : ui(context, semanticsLabel!),
    );
  }
}

const _english = <String, String>{
  'إيصال دفع': 'Payment receipt',
  'إيصال رسم البطولة': 'Tournament fee receipt',
  'الإيصال غير متاح.': 'This receipt is unavailable.',
  'إيصال معكوس': 'Reversed receipt',
  'رُدت القيمة كاملة وأُلغيت فترة الاشتراك المرتبطة.': 'The full amount was refunded and the linked subscription period was cancelled.',
  'فرع التسجيل والتحصيل': 'Enrollment and collection branch',
  'تاريخ ووقت الدفع': 'Payment date and time',
  'تاريخ العكس': 'Reversal date',
  'المبلغ المدفوع': 'Amount paid',
  'طلبات المنتجات': 'Product orders',
  'طلب المنتج': 'Product order',
  'رقم الطلب': 'Order number',
  'بحث': 'Search',
  'مسح البحث': 'Clear search',
  'أدخل رقم الطلب كاملًا كما يظهر في تفاصيله.':
      'Enter the full number shown in the request details.',
  'لا توجد طلبات معلقة بهذا الرقم ضمن النطاق المختار.':
      'No pending requests with this number in the selected scope.',
  'عرض طلب الاشتراك': 'View enrollment request',
  'الحجم الأصلي': 'Original size',
  'انتهت جلسة المراجعة.': 'The review session ended.',
  'أعد فتح صورة الإثبات قبل إرسال القرار.':
      'Open the proof image again before submitting the decision.',
  'تعذر فتح صورة الإثبات.': 'Could not open the payment proof image.',
  'إعادة تحميل الصورة': 'Reload image',
  'لم يصل إثبات دفع.': 'No payment proof received.',
  'المنتج أو اللاعب أو رقم الطلب': 'Product, player or order number',
  'الصفحة': 'Page',
  'الصفحة السابقة': 'Previous page',
  'الصفحة التالية': 'Next page',
  'لا توجد طلبات في هذه الصفحة.': 'There are no orders on this page.',
  'تحديث الطلبات': 'Refresh orders',
  'متابعة الطلب': 'Track order',
  'عرض الطلب': 'View order',
  'تم تأكيد الدفع': 'Payment confirmed',
  'الطلب مرفوض': 'Order rejected',
  'مبلغ الطلب': 'Order amount',
  'طلب المنتج غير متاح لحسابك.': 'This product order is unavailable.',
  'تعذر قراءة طلب المنتج.': 'Could not load this product order.',
  'تواصل مع الأكاديمية لترتيب استلام المنتج.':
      'Contact the academy to arrange pickup.',
  'الاسم وقت الدفع': 'Name at payment',
  'اللاعب المرتبط الآن': 'Currently linked player',
  'تجديد لغير الأبناء': 'Renewal paid for another player',
  'المباريات القادمة': 'Upcoming matches',
  'القادمة': 'Upcoming',
  'سجل المباريات': 'Match history',
  'السجل': 'History',
  'بانتظار النتيجة': 'Awaiting result',
  'ملاحظات المباراة': 'Match notes',
  'خطوات تعلم اللاعب': 'Player learning journey',
  'أهداف التطور': 'Development goals',
  'أهداف تطور اللاعب': 'Player development goals',
  'هدف جديد': 'New goal',
  'عنوان الهدف': 'Goal title',
  'ما الذي نعمل عليه؟': 'What are we working on?',
  'حفظ المسودة': 'Save draft',
  'مراجعة النشر': 'Review before publishing',
  'نشر الهدف للأسرة؟': 'Publish this goal to the family?',
  'راجع النص وتأكد من خلوه من البيانات الطبية أو بيانات لاعب آخر.': 'Check that the text contains no medical details or information about another player.',
  'احفظ التعديلات لمراجعة النص قبل النشر.':
      'Save your edits before reviewing the goal for publication.',
  'موعد المراجعة المحفوظ فات. اختر موعدًا جديدًا واحفظه قبل النشر.': 'The saved review date has passed. Choose and save a new date before publishing.',
  'تظهر نتيجة المراجعة للأسرة. اعتماد تحقق الهدف لا يغيّر المستوى أو المجموعة أو الاشتراك.': 'The family sees the outcome. Marking the goal achieved does not change the level, group, or subscription.',
  'يُخفى الهدف عن الأسرة ويظل سجله متاحًا لفريق التدريب.': 'This hides the goal from the family and keeps its history available to coaches.',
  'لم ينشر فريق التدريب أهدافًا لهذا اللاعب بعد.':
      'The coaches have not published any goals for this player yet.',
  'لا توجد أهداف مسجلة لهذا الاشتراك الرياضي.':
      'There are no goals for this sport enrollment.',
  'إضافة هدف تحتاج صلاحية على هذا الاشتراك.':
      'You need permission for this enrollment to add a goal.',
  'قراءة أحدث نسخة': 'Read latest version',
  'قيد العمل': 'In progress',
  'نشر الهدف للأسرة': 'Publish goal to family',
  'نتيجة المراجعة': 'Review outcome',
  'ملاحظة التقدم للأسرة (اختياري)': 'Progress note for family (optional)',
  'ما زال قيد العمل': 'Still in progress',
  'حفظ المراجعة': 'Save review',
  'سجل فني محفوظ': 'Saved development history',
  'سجل المراجعات المنشورة': 'Published review history',
  'فتح الهدف وسجل المراجعة': 'Open goal and review history',
  'سحب الهدف مع حفظ السجل': 'Withdraw goal and keep history',
  'سحب الهدف؟': 'Withdraw this goal?',
  'سحب الهدف': 'Withdraw goal',
  'مسودة خاصة بفريق التدريب': 'Private coaching draft',
  'قيد العمل · منشور للأسرة': 'In progress · shared with family',
  'تحقق الهدف بمراجعة فنية': 'Achieved after a technical review',
  'هدف مسحوب': 'Withdrawn goal',
  'إنشاء مسودة': 'Draft created',
  'تعديل المسودة': 'Draft edited',
  'نشر للأسرة': 'Published to family',
  'مراجعة التقدم': 'Progress review',
  'تحقق الهدف': 'Goal achieved',
  'تم حفظ تحديث الهدف وسجل المراجعة.': 'Goal update and review history saved.',
  'هدف فني نراجعه معًا، وليس ضمانًا زمنيًا أو ترقية تلقائية. لا يغيّر المجموعة أو الاشتراك.': 'A technical goal to review together, not a time guarantee or automatic promotion. It does not change the group or subscription.',
  'المسودة خاصة بفريق التدريب. انشر الهدف للأسرة بعد مراجعته؛ بلا معلومات طبية أو طفل آخر.': 'The draft is private to the coaching team. Review it before sharing; do not include medical details or information about another child.',
  'يختفي الهدف عن الأسرة ويظل سجله محفوظًا. لا تتغير المجموعة أو المستوى.': 'The goal is hidden from the family and its history is kept. The group and level do not change.',
  'تظهر أحدث20 مراجعة منشورة؛ يمكن طلب سجل أقدم من الإدارة.': 'Showing the latest 20 published reviews. Contact the academy for older history.',
  'تظهر أحدث100 هدف.': 'Showing the latest 100 goals.',
  'تظهر أحدث100 مراجعة.': 'Showing the latest 100 reviews.',
  'لا توجد أهداف منشورة بعد. يمكنك متابعة التقييمات الحالية من ملف اللاعب.': 'No goals have been published yet. Existing evaluations are available in the player profile.',
  'بطاقة اللاعب': 'Player card',
  'تدريب خاص': 'Private training',
  'تدريب شبه خاص': 'Semi-private training',
  'تدريب جماعي': 'Group training',
  'أكاديميتي': 'My Academy',
  'الأكاديمية': 'Academy',
  'الرئيسية': 'Home',
  'الرئيسية · المدرب': 'Coach home',
  'الحساب': 'Account',
  'حسابي': 'My account',
  'اللغة': 'Language',
  'المظهر الداكن': 'Dark appearance',
  'نفس الهوية لجميع الأدوار': 'One visual identity for every role',
  'التالي': 'Next',
  'تخطي': 'Skip',
  'ابدأ رحلتك': 'Get started',
  'مالك': 'Owner',
  'مدرب': 'Coach',
  'ولي أمر': 'Guardian',
  'دور غير مدعوم': 'Unsupported role',
  'اختر الأكاديمية والدور': 'Choose academy and role',
  'اختيار الدور': 'Choose a role',
  'الأدوار المتاحة': 'Available roles',
  'تبديل الدور': 'Switch role',
  'الدور الحالي': 'Current role',
  'متابعة بالدور المختار': 'Continue with selected role',
  'تحديث الأدوار المتاحة': 'Refresh available roles',
  'لا توجد أدوار متاحة لهذا الحساب.':
      'No roles are available for this account.',
  'الفروع': 'Branches',
  'الرياضات': 'Sports',
  'كل الرياضات': 'All sports',
  'لا توجد فروع مسندة': 'No assigned branches',
  'لا توجد رياضات مسندة': 'No assigned sports',
  'تأكيد الجلسة': 'Confirm your session',
  'تحقّق من الجلسة': 'Check session',
  'جارٍ تحديث الجلسة…': 'Updating session…',
  'جارٍ فتح الدور…': 'Opening role…',
  'جارٍ تسجيل الخروج…': 'Signing out…',
  'تعذر تأكيد تبديل الدور. تحقّق من الجلسة للمتابعة.':
      'The role switch could not be confirmed. Check your session to continue.',
  'تعذر التحقق من الجلسة. تحقّق من الاتصال وأعد المحاولة.':
      'Could not check your session. Check your connection and try again.',
  'انتهت الجلسة. سجّل الدخول لاختيار الدور.':
      'Your session ended. Sign in to choose a role.',
  'هذا الدور غير متاح حاليًا. حدّث الأدوار المتاحة.':
      'This role is unavailable. Refresh your available roles.',
  'تعذر فتح الدور المختار. أعد المحاولة.':
      'Could not open the selected role. Try again.',
  'تعذر تسجيل الخروج. حاول مجددًا بعد فتح قفل الجهاز.':
      'Could not sign out. Unlock your device and try again.',
  'الأكاديميات والأدوار المتاحة': 'Available academies and roles',
  'اختر عضوية واحدة صراحةً. لا يتم جمع صلاحيات الأدوار.':
      'Select one membership. Role permissions are never combined.',
  'رقم الهاتف': 'Phone number',
  'رحلتك في الأكاديمية': 'Your academy journey',
  'تسجيل الدخول': 'Sign in',
  'أدخل رمز التحقق': 'Enter your verification code',
  'أدخل رمز التحقق المكوّن من ٦ أرقام.':
      'Enter the six-digit verification code.',
  'الرمز مكوّن من ٦ أرقام.': 'The code has six digits.',
  'ادخل برقم هاتفك المسجل لدى الأكاديمية.':
      'Use the phone number registered with your academy.',
  'بنفس الرقم المسجّل لدى الأكاديمية.':
      'Use the phone number registered with your academy.',
  'رمز التحقق': 'Verification code',
  'إرسال رمز التحقق': 'Send verification code',
  'تأكيد الدخول': 'Sign in',
  'تسجيل الخروج': 'Sign out',
  'اسم الوسيلة': 'Method name',
  'هاتف العمل': 'Work phone',
  'اكتب اسمًا من حرفين إلى ٨٠ حرفًا.': 'Enter a name of 2 to 80 characters.',
  'إضافة وسيلة تحقق': 'Add verification method',
  'تجربة وسيلة التحقق': 'Test verification method',
  'الوسائل المحفوظة': 'Saved methods',
  'لا توجد وسائل تحقق محفوظة لهذا الحساب.':
      'There are no saved verification methods.',
  'التحقق والخصوصية': 'Verification and privacy',
  'سياسة الحضور حسب الفرع': 'Attendance policy by branch',
  'قد تستخدم بصمة الإصبع أو الوجه أو رمز الجهاز. يمكن أن تتزامن الوسيلة بين أجهزتك. لا نخزّن بصمات أو صور وجوه.': 'Your device may use a fingerprint, face, or passcode. The method can sync across your devices. We do not store fingerprints or face images.',
  'إضافة الوسيلة أو تجربتها لا تسجّل حضورًا.':
      'Adding or testing a method does not record attendance.',
  'إضافة وسيلة تحقق غير متاحة على هذا الجهاز. يمكنك طلب إثبات حضور للمراجعة.': 'Adding a verification method is unavailable on this device. You can request an attendance review.',
  'إضافة وسيلة تحقق غير متاحة حاليًا. تواصل مع المسؤول عن إعدادها.': 'Adding a verification method is unavailable. Contact the person who manages it.',
  'تاريخ الإضافة': 'Added',
  'آخر تحقق': 'Last verified',
  'تاريخ الإيقاف': 'Stopped',
  'قد تتزامن بين أجهزتك.': 'May sync across your devices.',
  'إيقاف الوسيلة': 'Stop method',
  'تأكيد الإيقاف': 'Confirm stop',
  'ستبقى الوسيلة في السجل. لا يمكن إعادة تفعيلها بعد الإيقاف.': 'The method stays in your history. It cannot be reactivated after it is stopped.',
  'جارٍ تحميل وسائل التحقق…': 'Loading verification methods…',
  'جارٍ التحقق من الحالة…': 'Checking the status…',
  'جارٍ إضافة الوسيلة…': 'Adding the method…',
  'جارٍ التحقق من الوسيلة…': 'Verifying the method…',
  'جارٍ إيقاف الوسيلة…': 'Stopping the method…',
  'تعذر عرض وسائل التحقق الخاصة بحسابك.':
      'Could not load your verification methods.',
  'تعذر تأكيد الإيقاف. تحقّق من حالة الوسيلة.':
      'The stop could not be confirmed. Check the method status.',
  'تعذر التحقق من الحالة. أعد قراءة القائمة.':
      'Could not check the status. Refresh the list and try again.',
  'تعذر تأكيد نتيجة المحاولة. تحقّق من الوسائل المحفوظة.':
      'The result could not be confirmed. Check your saved methods.',
  'تحقّق من الوسائل المحفوظة': 'Check saved methods',
  'الوسيلة غير ظاهرة في القائمة. أعد التحقق من حالتها.':
      'The method is missing from the list. Check its status again.',
  'تمت إضافة وسيلة التحقق.': 'Verification method added.',
  'تم التحقق من الوسيلة.': 'Method verified.',
  'تم إيقاف وسيلة التحقق.': 'Verification method stopped.',
  'وسيلة التحقق ما زالت فعّالة.': 'The method is still active.',
  'تم تحديث القائمة. راجع الوسائل المحفوظة قبل المحاولة مرة أخرى.':
      'List updated. Review the saved methods before trying again.',
  'تعذر بدء التحقق. حاول مرة أخرى.': 'Could not start verification. Try again.',
  'تم إلغاء التحقق على الجهاز.': 'Verification was cancelled on the device.',
  'لديك خمس وسائل فعّالة. أوقف إحدى الوسائل قبل إضافة وسيلة أخرى.':
      'You have five active methods. Stop one before adding another.',
  'تعديل الرقم': 'Edit phone number',
  'طلب رمز جديد': 'Request a new code',
  'يمكنك إدخال الرمز الجديد.': 'You can enter the new code.',
  'جارٍ طلب الرمز': 'Requesting the code',
  'جارٍ التحقق': 'Verifying the code',
  'ادخل برقم هاتف حسابك لمتابعة رحلتك في الأكاديمية.':
      'Sign in with your account phone number to follow your academy journey.',
  'بيئة عرض تجريبي فقط — لا تُرسل رسالة SMS حقيقية.':
      'Demo only — no real SMS is sent.',
  'بيئة عرض تجريبي. لا تُرسل رسائل تحقق فعلية.':
      'Demo only. No verification text is sent.',
  'أدخل رقم الهاتف المصري.': 'Enter an Egyptian phone number.',
  'مصر (+20)': 'Egypt (+20)',
  'أدخل رمز التحقق من ٦ أرقام.': 'Enter the six-digit verification code.',
  'تعذر إرسال الرمز. راجع رقم الهاتف المسجل لدى الأكاديمية.': 'Could not send a code. Check the phone number registered with your academy.',
  'الرمز غير صحيح أو انتهت صلاحيته. يمكنك طلب رمز جديد.':
      'The code is incorrect or expired. You can request a new code.',
  'استعادة الجلسة': 'Restoring session',
  'إعادة المحاولة': 'Retry',
  'تسجيل الحضور': 'Record attendance',
  'جارٍ التحميل': 'Loading',
  'جارٍ التنفيذ': 'Working',
  'جارٍ الحفظ…': 'Saving…',
  'جارٍ إنشاء الطلب…': 'Creating request…',
  'جارٍ الإرسال': 'Sending',
  'جارٍ الإرسال…': 'Sending…',
  'جارٍ البحث…': 'Searching…',
  'جارٍ الرفع': 'Uploading',
  'غير متاح': 'Unavailable',
  'غير محدد': 'Not specified',
  'ج.م': 'EGP',
  'أبنائي': 'My children',
  'تقدم أبنائي': 'My children’s progress',
  'ملف اللاعب': 'Player profile',
  'عرض الملف': 'View profile',
  'اشتراك جديد': 'New enrollment',
  'تجديد الاشتراك': 'Renew subscription',
  'تجديد اشتراك لغيره': 'Renew for someone',
  'الباقات': 'Plans',
  'الباقة': 'Plan',
  'المتجر': 'Store',
  'منتجات الرياضة': 'Sports products',
  'المنتجات الرياضية': 'Sports products',
  'طلبات الاشتراك': 'Enrollment requests',
  'طلبات الدفع': 'Payment requests',
  'إيصالاتي': 'My receipts',
  'إيصال التحصيل': 'Payment receipt',
  'عرض الإيصال': 'View receipt',
  'عرض التفاصيل': 'View details',
  'الإيصال صادر من سجل التحصيل على الخادم.':
      'This receipt comes from the stored collection record.',
  'اختيار الباقة': 'Choose plan',
  'اختيار باقة والاشتراك': 'Choose a plan and enroll',
  'اختر الطفل والرياضة': 'Choose child and sport',
  'مراجعة التجديد': 'Review renewal',
  'متابعة إلى الدفع': 'Continue to payment',
  'تحديث حالة الدفع': 'Refresh payment status',
  'إعادة محاولة الدفع': 'Retry payment',
  'عرض محاولة الدفع الحالية': 'View current payment attempt',
  'طلب دفع التجديد': 'Renewal payment',
  'استُبدل طلب الدفع': 'This payment request was replaced',
  'مبلغ الطلب السابق': 'Previous request amount',
  'المبلغ المطلوب': 'Amount due',
  'السعر الأصلي': 'Original price',
  'الخصم': 'Discount',
  'فتح طلب الدفع الحالي': 'Open current payment request',
  'التحقق من حالة الدفع': 'Check payment status',
  'إعادة إرسال طلب الدفع نفسه': 'Retry the same payment request',
  'جارٍ تنفيذ الإجراء…': 'Processing…',
  'طلب الدفع غير متاح لهذا الحساب.':
      'This payment is unavailable to this account.',
  'تعذر قراءة طلب الدفع.': 'Could not read the payment request.',
  'تعذر إكمال إجراء الدفع.': 'Could not complete the payment action.',
  'لم يصل تأكيد إنشاء طلب الدفع. أعد إرسال الطلب نفسه.': 'Creation of the payment request was not confirmed. Retry the same request.',
  'لم تتأكد نتيجة الدفع. تحقّق من الحالة قبل المحاولة مجددًا.': 'The payment result could not be confirmed. Check its status before trying again.',
  'طريقة الدفع غير متاحة لهذا الطلب. تواصل مع الأكاديمية.': 'The payment method is unavailable for this request. Contact your academy.',
  'لم تعتمد الأكاديمية إثبات الدفع. راجعها بشأن التحويل السابق قبل إرسال مبلغ جديد.': 'The academy did not accept the proof of payment. Ask about the previous transfer before paying again.',
  'الدفع لا يمنحك الوصول إلى ملف اللاعب أو ربطه بحسابك.': 'Paying does not grant access to the child’s profile or link them to your account.',
  'هذا الطلب لا ينشئ علاقة ولي أمر باللاعب.':
      'This request does not create a guardian relationship.',
  'لن يتم تحصيل المبلغ أو تفعيل الاشتراك قبل تأكيد الدفع. تاريخ التفعيل والحصص يحددهما الخادم.': 'No collection or activation occurs before payment confirmation. The server determines subscription dates and session balance.',
  'الدفع': 'Payment',
  'دفع الاشتراك الجديد': 'New enrollment payment',
  'التحويل للأكاديمية': 'Academy transfer',
  'إنستا باي': 'InstaPay',
  'المحفظة': 'Wallet',
  'نسخ': 'Copy',
  'تم النسخ': 'Copied',
  'رفع إيصال الدفع': 'Upload payment receipt',
  'وصل إثبات الدفع': 'Proof received',
  'لم يصل إثبات دفع': 'No proof yet',
  'تم رفع إثبات دفع لهذا الطلب.': 'Payment proof uploaded for this request.',
  'لم تضبط الأكاديمية حسابات التحويل بعد. تواصل مع الإدارة قبل الدفع.': 'The academy has not configured transfer accounts. Contact staff before paying.',
  'وصل إثبات الدفع. تنتظر العملية مراجعة الإدارة؛ لا تحتاج إلى الدفع مرة أخرى.':
      'Proof received. Staff review is pending; do not pay again.',
  'حوّل المبلغ الموضح ثم ارفع صورة إيصالك هنا. يبدأ الاشتراك بعد موافقة الإدارة.': 'Transfer the displayed amount and upload your receipt here. Activation follows staff approval.',
  'دفع تجريبي فقط · لن تُخصم أموال حقيقية':
      'Test payment only · no real money is charged',
  'محاكاة دفع ناجح': 'Simulate successful payment',
  'محاكاة فشل الدفع': 'Simulate failed payment',
  'إلغاء الدفع التجريبي': 'Cancel test payment',
  'تحديث الحالة': 'Refresh status',
  'شراء المنتج': 'Buy product',
  'تُراجع الإدارة الدفع وتوافر المخزون قبل التأكيد. الاستلام من الأكاديمية.': 'Staff review payment and stock before confirmation. Collect from the academy.',
  'طفل جديد': 'New child',
  'طفل مرتبط': 'Linked child',
  'اسم الطفل': 'Child name',
  'اسم اللاعب': 'Player name',
  'الطفل': 'Child',
  'الرياضة': 'Sport',
  'الفرع': 'Branch',
  'الفرع المفضل': 'Preferred branch',
  'تاريخ الميلاد': 'Date of birth',
  'ملاحظات — اختياري': 'Notes — optional',
  'إرسال طلب الاشتراك': 'Submit enrollment request',
  'اختر الباقة ثم أرسل إثبات الدفع. يُفعّل الاشتراك بعد موافقة الأكاديمية وإسناد المجموعة.': 'Choose a plan and upload payment proof. Staff approval and group assignment activate the subscription.',
  'أدخل اسم الطفل.': 'Enter the child’s name.',
  'اختر تاريخ الميلاد.': 'Choose a date of birth.',
  'اختر باقة الاشتراك.': 'Choose a subscription plan.',
  'لمنع الطلب المكرر، أعد إرسال نفس البيانات من هنا عند عودة الاتصال.':
      'To avoid duplicates, retry the same request here after reconnecting.',
  'هدية تدعم رحلة لاعب': 'Support another player’s journey',
  'بحث عن لاعب': 'Find a player',
  'اكتب ثلاثة أحرف على الأقل من اسم اللاعب.':
      'Enter at least three characters of the player’s name.',
  'ابحث داخل أكاديميتك واختر اللاعب والرياضة. الدفع لا يمنحك الوصول إلى ملفه أو ربطه بحسابك.': 'Search within your academy and choose the player and sport. Payment does not grant profile access or a guardian link.',
  'لا توجد تسجيلات مطابقة للاسم.': 'No matching enrollments.',
  'انتهى الاختيار. أعد البحث.': 'Selection expired. Search again.',
  'تقرير اللاعب': 'Player report',
  'تقارير اللاعب': 'Player reports',
  'التقييمات': 'Evaluations',
  'التقييم العام': 'Overall rating',
  'التقييمات المنشورة · اختر تاريخ التقرير':
      'Published evaluations · choose a report date',
  'آخر تقييم منشور': 'Latest published evaluation',
  'قوة اللاعب': 'Player power',
  'المعايير التفصيلية': 'Detailed criteria',
  'ملاحظات المدرب': 'Coach notes',
  'تقييم اللاعب': 'Evaluate player',
  'بدء تقييم جديد': 'New evaluation',
  'تقييم دوري': 'Periodic evaluation',
  'حفظ كمسودة': 'Save draft',
  'نشر التقييم': 'Publish evaluation',
  'تم حفظ المسودة.': 'Draft saved.',
  'تم نشر التقييم.': 'Evaluation published.',
  'درجات كرة القدم من 0 إلى 100. يحسب الخادم التقرير والنتيجة.': 'Football scores range from 0 to 100. The server calculates the report and result.',
  'العمر': 'Age',
  'الطول · سم': 'Height · cm',
  'الوزن · كجم': 'Weight · kg',
  'القدم المفضلة': 'Preferred foot',
  'التمرير': 'Passing',
  'التسديد': 'Shooting',
  'المراوغة': 'Dribbling',
  'السرعة': 'Speed',
  'الدفاع': 'Defending',
  'القوة البدنية': 'Physical',
  'اليمنى': 'Right',
  'اليسرى': 'Left',
  'كلتاهما': 'Both',
  'رسم المحاور الستة؛ القيم موضحة نصيًا أسفله':
      'Six rating axes, with textual values below',
  'التدريب والمواعيد': 'Training schedule',
  'المواعيد': 'Schedule',
  'المواعيد والحضور': 'Schedule and attendance',
  'الحضور': 'Attendance',
  'حضور اللاعب': 'Player attendance',
  'المواعيد الدورية': 'Recurring schedule',
  'الجلسات القادمة': 'Upcoming sessions',
  'المجموعات المسندة': 'Assigned groups',
  'نُشر في': 'Published on',
  'تفاصيل النشاط': 'Activity details',
  'هذا النشاط غير متاح.': 'This activity is unavailable.',
  'خبر': 'News',
  'فعالية': 'Event',
  'إعلان': 'Announcement',
  'متابعات مسندة إليّ': 'Follow-ups assigned to me',
  'لا توجد متابعات مفتوحة.': 'No open follow-ups.',
  'لا توجد تنبيهات.': 'No notifications.',
  'مقروء': 'Read',
  'غير مقروء': 'Unread',
  'تحديد كمقروء': 'Mark as read',
  'تحديد كغير مقروء': 'Mark as unread',
  'جارٍ تحديث الحالة…': 'Updating status…',
  'تعذر تأكيد تحديث الحالة. حدّث القائمة للتحقق.':
      'Could not confirm the change. Refresh the list to check.',
  'تحديث القائمة': 'Refresh list',
  'تعذر تحميل المتابعات.': 'Could not load follow-ups.',
  'تعذر تحميل المتابعات. أعد المحاولة.':
      'Could not load follow-ups. Try again.',
  'لا توجد تقييمات من الأسر': 'No family ratings',
  'مجموعاتي': 'My groups',
  'حصص اليوم والقادمة': 'Today and upcoming sessions',
  'حصصك ومجموعاتك فقط — لا تظهر بيانات مالية.':
      'Only your sessions and groups. No financial data is shown.',
  'حفظ الحضور': 'Save attendance',
  'تم حفظ الحضور.': 'Attendance saved.',
  'جارٍ التحقق…': 'Checking…',
  'تحقق من الحفظ': 'Check saved attendance',
  'الحفظ غير مؤكد': 'Save unconfirmed',
  'تغييرات غير مسجلة': 'Unsaved changes',
  'إعادة إرسال التغييرات': 'Resend changes',
  'الحضور محفوظ بالفعل دون إعادة الإرسال.': 'Attendance was already saved.',
  'تم تحديث الكشف بحالات الخادم.': 'Roster updated with saved attendance.',
  'تجاهل التغييرات وتحديث الكشف': 'Discard changes and refresh roster',
  'تبقى التغييرات في هذه الصفحة لمدة 15 دقيقة كحد أقصى.':
      'Changes remain on this page for up to 15 minutes.',
  'الإصابات والاستشارات الطبية': 'Injuries and medical consultations',
  'السجلات المنشورة لهذا الطفل فقط': 'Only records published for this child',
  'إصابة': 'Injury',
  'استشارة': 'Consultation',
  'مفتوح': 'Open',
  'مغلق': 'Closed',
  'متابعة': 'Monitoring',
  'الصور والفيديوهات': 'Photos and videos',
  'رفع صور وفيديوهات للحصة': 'Upload session photos and videos',
  'صور وفيديوهات المجموعة': 'Group media',
  'رفع صورة أو فيديو': 'Upload photo or video',
  'وصف مختصر (اختياري)': 'Caption (optional)',
  'فيديو التدريب': 'Training video',
  'تم الرفع. ظهرت الوسائط لأولياء الأمور المرتبطين بالمجموعة.':
      'Uploaded. Media is visible to linked guardians in the group.',
  'التغذية': 'Nutrition',
  'التغذية والصحة': 'Nutrition and health',
  'التغذية والوجبات': 'Nutrition and meals',
  'تفاصيل الوجبة': 'Meal details',
  'اقتراحات اليوم': 'Today’s meal ideas',
  'الإفطار': 'Breakfast',
  'الغداء': 'Lunch',
  'العشاء': 'Dinner',
  'قبل التدريب': 'Before training',
  'بعد التدريب': 'After training',
  'القيم الغذائية': 'Nutrition facts',
  'سعرات حرارية': 'Calories',
  'بروتين · جرام': 'Protein · g',
  'كربوهيدرات · جرام': 'Carbohydrates · g',
  'دهون · جرام': 'Fat · g',
  'صغيرة': 'Small',
  'متوسطة': 'Medium',
  'كبيرة': 'Large',
  'القيم الغذائية تقديرية حسب الحصة الموضحة':
      'Estimated nutritional values for the listed serving',
  'بيانات تجريبية غير مراجعة': 'Unreviewed demo values',
  'بيانات مراجعة وفق المصدر المسجل': 'Reviewed against the recorded source',
  'المصادر: USDA FoodData Central · HealthyChildren/AAP · Sports Dietitians Australia': 'Sources: USDA FoodData Central · HealthyChildren/AAP · Sports Dietitians Australia',
  'يوم تدريب · اقتراحات متنوعة تتغير يوميًا':
      'Training day · varied daily meal ideas',
  'يوم راحة · اقتراحات متنوعة تتغير يوميًا':
      'Rest day · varied daily meal ideas',
  'الاشتراكات': 'Subscriptions',
  'حالة الاشتراك': 'Subscription status',
  'تفاصيل حالة الاشتراك': 'Subscription details',
  'سجل التعديلات': 'Adjustment history',
  'فعال': 'Active',
  'فعالة': 'Active',
  'ينتهي قريبًا': 'Expiring soon',
  'تنتهي قريبًا': 'Expiring soon',
  'منتهي': 'Expired',
  'منتهية': 'Expired',
  'مجمد': 'Frozen',
  'ملغي': 'Cancelled',
  'قادم': 'Upcoming',
  'قيد الانتظار': 'Pending',
  'قيد المراجعة': 'Under review',
  'مقبول': 'Approved',
  'مرفوض': 'Rejected',
  'تم الدفع': 'Paid',
  'مؤكد': 'Confirmed',
  'فشل الدفع': 'Payment failed',
  'حاضر': 'Present',
  'متأخر': 'Late',
  'معتذر': 'Excused',
  'غائب': 'Absent',
  'لم يُسجل': 'Not recorded',
  'بدون اشتراك': 'No subscription',
  'لا يوجد اشتراك فعال': 'No active subscription',
  'تجميد': 'Freeze',
  'استئناف': 'Resume',
  'إضافة أيام': 'Add days',
  'خصم أيام': 'Deduct days',
  'إلغاء': 'Cancel',
  'الأحد': 'Sunday',
  'الاثنين': 'Monday',
  'الثلاثاء': 'Tuesday',
  'الأربعاء': 'Wednesday',
  'الخميس': 'Thursday',
  'الجمعة': 'Friday',
  'السبت': 'Saturday',
  'لوحة المالك': 'Owner dashboard',
  'الصورة الكاملة لأكاديميتك': 'Your academy at a glance',
  'من الأكاديمية': 'From your academy',
  'أخبار الأكاديمية': 'Academy news',
  'الأخبار': 'News',
  'الأخبار والفعاليات': 'News and events',
  'كل الأنشطة': 'All activities',
  'الفعاليات': 'Events',
  'الأخبار والإشعارات': 'News and notifications',
  'إشعاراتي': 'My notifications',
  'الطلبات': 'Requests',
  'اشتراكات جديدة': 'New enrollments',
  'تجديد': 'Renewals',
  'منتجات': 'Products',
  'تحصيل اليوم': 'Today’s collections',
  'تحصيل الشهر': 'Period collections',
  'نقدي': 'Cash',
  'إلكتروني': 'Electronic',
  'الاشتراكات الفعالة': 'Active subscriptions',
  'اللاعبون النشطون': 'Active players',
  'المدربون': 'Coaches',
  'صافي التحصيل بعد الاسترداد': 'Net collections after refunds',
  'أحدث التحصيلات': 'Recent collections',
  'التقارير': 'Reports',
  'التقارير التنفيذية': 'Executive reports',
  'المال': 'Finance',
  'ملخص التحصيلات': 'Collection summary',
  'ملخص الحضور المخزن': 'Stored attendance summary',
  'ملخص قراءة فقط من فترات الاشتراك المخزنة.':
      'Read-only summary from stored subscription periods.',
  'كل الفروع': 'All branches',
  'مسح': 'Clear',
  'اعتماد الطلبات وإسناد المجموعة متاحان من لوحة الويب.':
      'Approve requests and assign groups from the web dashboard.',
  'لا يوجد أبناء مرتبطون بهذا الحساب.':
      'No children are linked to this account.',
  'لا توجد باقات متاحة.': 'No plans available.',
  'لا توجد تسجيلات متاحة للتجديد.': 'No enrollments available for renewal.',
  'لا توجد طلبات حتى الآن.': 'No requests yet.',
  'لا توجد إيصالات لحسابك بعد.': 'No receipts for your account yet.',
  'لا توجد تقييمات منشورة بعد.': 'No published evaluations yet.',
  'لا توجد ملاحظات منشورة.': 'No published notes.',
  'لا توجد إصابات أو استشارات منشورة.':
      'No published injuries or consultations.',
  'لا توجد منتجات منشورة لرياضات الأبناء.':
      'No published products for your children’s sports.',
  'لا توجد جلسات قادمة منشورة.': 'No published upcoming sessions.',
  'لا توجد حصص ضمن النطاق الحالي.': 'No sessions in this range.',
  'لا توجد سجلات متاحة حتى الآن.': 'No records yet.',
  'لا توجد تعديلات.': 'No adjustments.',
  'تعذر تحميل الكتالوج · إعادة المحاولة': 'Unable to load the catalog · retry',
  'تعذر تحميل المجموعات.': 'Unable to load groups.',
  'تعذر تحميل بيانات المدرب.': 'Unable to load coach details.',
  'تعذر قراءة بيانات الشاشة. حدّثها وحاول مجددًا.':
      'Unable to read screen data. Refresh and retry.',
  'تعذر عرض البيانات.': 'Unable to display data.',
  'ليس لديك صلاحية لهذا الإجراء.':
      'You do not have permission for this action.',
  'سجل الدخول أولًا.': 'Sign in first.',
  'سجل الدخول مجددًا.': 'Sign in again.',
  'راجع البيانات المدخلة وحاول مجددًا.':
      'Check the entered information and retry.',
  'محاولات كثيرة. انتظر قليلًا قبل المحاولة.':
      'Too many attempts. Wait before retrying.',
  'تعذر الاتصال بالخادم.': 'Unable to connect to the server.',
  'تعذر إكمال الاتصال بالخادم.': 'Unable to complete the connection.',
  'تعذر الاتصال. تحقق من الشبكة وعنوان الخادم.':
      'Check your network and server address.',
  'انتهت مهلة الاتصال. لم يُعَد إرسال الطلب تلقائيًا.':
      'Connection timed out. The request was not automatically resent.',
  'تغيرت البيانات. حدّث الصفحة قبل المحاولة.':
      'Data changed. Refresh before retrying.',
  'تغيرت الجلسة.': 'Session changed.',
  'تغيرت الجلسة. أعد فتح الصفحة.': 'Session changed. Reopen the page.',
  'انتهت الجلسة أو تعذر التحقق من بيانات الدخول.':
      'Session expired or sign-in could not be verified.',
  'تعذر استعادة الجلسة.': 'Unable to restore session.',
  'تعذر إكمال الدخول أو حفظ الجلسة بأمان. أعد المحاولة.':
      'Unable to sign in or securely save your session. Retry.',
  'تعذر تبديل الدور بأمان. حاول مجددًا.':
      'Unable to switch roles securely. Retry.',
  'تعذر فتح التخزين الآمن. أعد المحاولة بعد فتح قفل الجهاز.':
      'Unable to open secure storage. Unlock the device and retry.',
  'تعذر مسح التخزين الآمن. افتح قفل الجهاز وحاول الخروج مجددًا.':
      'Unable to clear secure storage. Unlock your device and sign out again.',
  'تعذر مسح التخزين الآمن. افتح قفل الجهاز وحاول مجددًا.':
      'Unable to clear secure storage. Unlock your device and retry.',
  'تم الخروج محليًا، لكن تعذر تأكيد إلغاء الجلسة على الخادم.':
      'Signed out locally, but server revocation could not be confirmed.',
  'انتظر اكتمال تحديث الجلسة.': 'Wait for the session refresh to finish.',
  'تعذر تنفيذ الطلب الآن. حاول لاحقًا.':
      'Unable to perform this request now. Try later.',
  'تعذر إرسال إثبات الدفع.': 'Unable to submit payment proof.',
  'تعذر إرسال طلب المنتج.': 'Unable to submit product order.',
  'تعذر البحث عن اللاعب.': 'Unable to search for the player.',
  'الصورة حتى 8 ميجابايت والفيديو حتى 50 ميجابايت.':
      'Images up to 8 MB; videos up to 50 MB.',
  'هذا النوع غير مدعوم.': 'Unsupported file type.',
  'تعذر رفع الوسائط.': 'Unable to upload media.',
  'تعذر تشغيل الفيديو.': 'Unable to play video.',
  'تعذر عرض الوسائط.': 'Unable to display media.',
  'تعذر رفع الملف. راجع النوع والحجم والصلاحية.':
      'Check file type, size and upload permission.',
  'تعذر الاتصال. لم يُعَد رفع الملف تلقائيًا.':
      'Connection failed. The file was not automatically uploaded again.',
  'انتهت مهلة رفع الملف. حدّث الطلب قبل المحاولة.':
      'Upload timed out. Refresh the request before retrying.',
  'انتهت مهلة تحميل الوسائط.': 'Media download timed out.',
  'حجم الوسائط أكبر من المسموح.': 'Media exceeds the size limit.',
  'ملف غير مدعوم أو أكبر من المسموح.': 'Unsupported or oversized file.',
  'خدمة الوسائط غير متاحة.': 'Media service unavailable.',
  'مرجع الوسائط غير صالح.': 'Invalid media reference.',
  'مسار غير صالح.': 'Invalid path.',
  'استجابة الخادم غير صالحة.': 'Invalid server response.',
  'خدمة رسائل التحقق الحقيقية غير مفعلة بعد.':
      'Real verification messaging is not enabled yet.',
  'كرة القدم': 'Football',
  'السباحة': 'Swimming',
  'كرة السلة': 'Basketball',
};

const _phrases = <String, String>{
  'غير المقروءة ': 'Unread ',
  'المجموعات المسندة ': 'Assigned groups ',
  'حصص المجموعات ': 'Group sessions ',
  'عدد التقييمات ': 'Number of ratings ',
  'اكتمال التقييم ': 'Evaluation completeness ',
  ' · القيم غير المسجلة ليست صفرًا': ' · missing values are not zero',
  'المدرب: ': 'Coach: ',
  'التقييم ': 'Rating ',
  'التمرير: ': 'Passing: ',
  'التسديد: ': 'Shooting: ',
  'المراوغة: ': 'Dribbling: ',
  'السرعة: ': 'Speed: ',
  'الدفاع: ': 'Defending: ',
  'القوة البدنية: ': 'Physical: ',
  'غير متاح': 'Unavailable',
  'الحصة المتبقية': 'Remaining session',
  'حصة متبقية': 'sessions remaining',
  ' يومًا': ' days',
  ' حصة': ' sessions',
  ' جم': ' g',
  'ج.م': 'EGP',
  ' لاعبين': ' players',
  ' سنوات': ' years',
  ' طلب بانتظار المراجعة': ' pending requests',
};
