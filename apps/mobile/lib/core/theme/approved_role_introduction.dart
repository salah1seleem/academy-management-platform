import 'dart:math' as math;

import 'package:flutter/material.dart';

// Reference geometry and topics; no prototype identities, metrics or access rules.
class _Slide {
  const _Slide(
    this.image,
    this.kickerAr,
    this.kickerEn,
    this.titleAr,
    this.titleEn,
    this.bodyAr,
    this.bodyEn,
    this.icon,
  );
  final String image, kickerAr, kickerEn, titleAr, titleEn, bodyAr, bodyEn;
  final IconData icon;
}

const _child1 = 'assets/demo/portraits/child-01.jpg';
const _child2 = 'assets/demo/portraits/child-02.jpg';
const _coach = 'assets/demo/portraits/coach-01.jpg';
const _guardian = 'assets/demo/portraits/guardian-01.jpg';
const _slides = <String, List<_Slide>>{
  'AcademyOwner': [
    _Slide(
      _child1,
      'لوحة المالك',
      'Owner dashboard',
      'الاشتراكات والتحصيلات',
      'Subscriptions and collections',
      'اختر الفرع والفترة لعرض الاشتراكات والتحصيلات والمصروفات.',
      'Choose a branch and date range to view subscriptions, collections and expenses.',
      Icons.insights_outlined,
    ),
    _Slide(
      _coach,
      'مركز القرارات',
      'Decisions',
      'التجديد والغياب',
      'Renewals and absences',
      'راجع الاشتراكات التي قاربت على الانتهاء والغياب المتكرر. يمكنك إسناد المتابعة لموظف.',
      'Review expiring subscriptions and repeated absences. You can assign a follow-up to a staff member.',
      Icons.task_alt_outlined,
    ),
    _Slide(
      _child2,
      'العمليات',
      'Operations',
      'إدارة الأكاديمية',
      'Academy management',
      'افتح «العمليات» لإدارة الفروع والمجموعات وباقات التدريب.',
      'Open Operations to manage branches, groups and training plans.',
      Icons.bolt_outlined,
    ),
  ],
  'AcademyAdmin': [
    _Slide(
      _coach,
      'الرئيسية',
      'Home',
      'التسجيل والتجديد',
      'Registration and renewal',
      'سجّل لاعبًا جديدًا أو ابحث عن لاعب لتجديد اشتراكه.',
      'Register a new player or find a player to renew their subscription.',
      Icons.checklist_outlined,
    ),
    _Slide(
      _child1,
      'الحضور',
      'Attendance',
      'تسجيل حضور الحصة',
      'Record session attendance',
      'اختر الحصة من جدول اليوم وسجّل حضور اللاعبين.',
      'Choose a session from today’s schedule and record player attendance.',
      Icons.event_available_outlined,
    ),
    _Slide(
      _child2,
      'ملف اللاعب',
      'Player profile',
      'اشتراكات اللاعبين',
      'Player subscriptions',
      'راجع المجموعة ومواعيد التدريب ونهاية الاشتراك من ملف اللاعب.',
      'Check the group, training times and subscription end date in the player’s profile.',
      Icons.person_outline,
    ),
  ],
  'Coach': [
    _Slide(
      _child2,
      'المجموعات',
      'Groups',
      'جدول مجموعاتك',
      'Your group schedule',
      'افتح المجموعة لمعرفة مواعيد حصصها واللاعبين المسجلين فيها.',
      'Open a group to see its session times and enrolled players.',
      Icons.calendar_month_outlined,
    ),
    _Slide(
      _coach,
      'ملف اللاعب',
      'Player profile',
      'تقييم اللاعب',
      'Player evaluation',
      'سجّل الدرجات والملاحظات، ثم انشر التقييم ليظهر لولي الأمر.',
      'Record scores and notes, then publish the evaluation for the guardian to see.',
      Icons.star_outline,
    ),
    _Slide(
      _child1,
      'الحضور',
      'Attendance',
      'حضور اللاعبين',
      'Player attendance',
      'إذا أسند إليك الإداري تسجيل الحضور، افتح الحصة وسجّل حضور لاعبي مجموعتك.',
      'If an administrator assigns attendance to you, open the session to mark attendance for your group.',
      Icons.fact_check_outlined,
    ),
  ],
  'Guardian': [
    _Slide(
      _child1,
      'أبنائي',
      'My children',
      'تقييمات المدرب',
      'Coach evaluations',
      'افتح ملف ابنك للاطلاع على التقييمات التي نشرها المدرب.',
      'Open your child’s profile to see evaluations the coach has published.',
      Icons.groups_outlined,
    ),
    _Slide(
      _child2,
      'المواعيد',
      'Schedule',
      'الحصص والحضور',
      'Sessions and attendance',
      'راجع موعد الحصة القادمة وسجل حضور ابنك وغيابه.',
      'Check your child’s next session and attendance record.',
      Icons.calendar_month_outlined,
    ),
    _Slide(
      'assets/onboarding/m1.jpg',
      'الاشتراك والمتابعة',
      'Subscriptions and follow-up',
      'تجديد الاشتراك',
      'Renew a subscription',
      'راجع تاريخ انتهاء الاشتراك وقدّم طلب تجديد أو اشتراك جديد.',
      'Check the end date and request a renewal or a new subscription.',
      Icons.receipt_long_outlined,
    ),
  ],
};
const _general = [
  _Slide(
    _guardian,
    'حسابي',
    'My account',
    'الأكاديمية المختارة',
    'Selected academy',
    'إذا كنت عضوًا في أكثر من أكاديمية، يمكنك التبديل بينها من حسابك.',
    'If you belong to more than one academy, switch between them from your account.',
    Icons.school_outlined,
  ),
  _Slide(
    _coach,
    'العضويات',
    'Memberships',
    'الدور النشط',
    'Active role',
    'إذا كان لك أكثر من دور، اختر الدور الذي تريد استخدامه. تتغيّر الصفحات المتاحة حسب الدور.',
    'If you have more than one role, choose the one you want to use. Available pages depend on the role.',
    Icons.verified_user_outlined,
  ),
  _Slide(
    _guardian,
    'التفضيلات',
    'Preferences',
    'اللغة والمظهر',
    'Language and appearance',
    'غيّر اللغة من «حسابي»، والمظهر من الزر أعلى الشاشة.',
    'Change the language from My account and the appearance from the button at the top of the screen.',
    Icons.explore_outlined,
  ),
];

class ApprovedRoleIntroduction extends StatefulWidget {
  const ApprovedRoleIntroduction({
    super.key,
    required this.role,
    required this.sportTheme,
    required this.training,
    required this.academyName,
    required this.crest,
    required this.onComplete,
    this.busy = false,
    this.error,
    this.onContinueOnce,
    this.canPop = false,
    this.initialPage = 0,
    this.onPageChanged,
  });
  final String role, sportTheme, academyName;
  final int initialPage;
  final ValueChanged<int>? onPageChanged;
  final bool training, busy, canPop;
  final Widget crest;
  final String? error;
  final VoidCallback? onContinueOnce;
  final Future<void> Function() onComplete;
  @override
  State<ApprovedRoleIntroduction> createState() =>
      _ApprovedRoleIntroductionState();
}

class _ApprovedRoleIntroductionState extends State<ApprovedRoleIntroduction> {
  late int index = widget.initialPage.clamp(0, pages.length - 1);
  double drag = 0;
  bool completing = false;
  final scroll = ScrollController();
  bool get locked => widget.busy || completing;
  String tr(String ar, String en) =>
      Localizations.localeOf(context).languageCode == 'en' ? en : ar;
  List<_Slide> get pages =>
      widget.training ? (_slides[widget.role] ?? _general) : _general;
  void move(int next) {
    if (locked || next < 0 || next >= pages.length) return;
    setState(() => index = next);
    widget.onPageChanged?.call(next);
    if (scroll.hasClients) scroll.jumpTo(0);
  }

  Future<void> complete() async {
    if (locked) return;
    setState(() => completing = true);
    try {
      await widget.onComplete();
    } finally {
      if (mounted) setState(() => completing = false);
    }
  }

  @override
  void didUpdateWidget(ApprovedRoleIntroduction oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.role != widget.role ||
        oldWidget.sportTheme != widget.sportTheme ||
        oldWidget.training != widget.training) {
      index = 0;
    }
  }

  @override
  void dispose() {
    scroll.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final slide = pages[index], c = Theme.of(context).colorScheme;
    final large = MediaQuery.textScalerOf(context).scale(1) > 1.4;
    final role = tr(
      switch (widget.role) {
        'AcademyOwner' => 'المالك',
        'AcademyAdmin' => 'الإداري',
        'Coach' => 'المدرب',
        _ => 'ولي الأمر',
      },
      switch (widget.role) {
        'AcademyOwner' => 'Owner',
        'AcademyAdmin' => 'Administrator',
        'Coach' => 'Coach',
        _ => 'Guardian',
      },
    );
    return PopScope(
      canPop: widget.canPop,
      child: Scaffold(
        body: SafeArea(
          child: Center(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 650),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Padding(
                    key: const ValueKey('intro-brand-header'),
                    padding: const EdgeInsets.fromLTRB(24, 14, 24, 8),
                    child: Row(
                      children: [
                        widget.crest,
                        const SizedBox(width: 12),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                widget.academyName,
                                maxLines: 2,
                                overflow: TextOverflow.ellipsis,
                                style: const TextStyle(
                                  fontSize: 15,
                                  fontWeight: FontWeight.w800,
                                ),
                              ),
                              Text(
                                role,
                                style: TextStyle(
                                  fontSize: 12,
                                  color: c.onSurfaceVariant,
                                ),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),
                  ),
                  Expanded(
                    child: SingleChildScrollView(
                      controller: scroll,
                      padding: const EdgeInsets.fromLTRB(24, 18, 24, 12),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          GestureDetector(
                            key: const ValueKey('intro-photo'),
                            onHorizontalDragStart: (_) => drag = 0,
                            onHorizontalDragUpdate: (d) => drag += d.delta.dx,
                            onHorizontalDragEnd: (_) {
                              if (drag.abs() > 40) {
                                move(
                                  index +
                                      ((Directionality.of(context) ==
                                                  TextDirection.rtl
                                              ? drag > 0
                                              : drag < 0)
                                          ? 1
                                          : -1),
                                );
                              }
                            },
                            child: ClipRRect(
                              borderRadius: BorderRadius.circular(28),
                              child: SizedBox(
                                height: math.min(
                                  310,
                                  math.max(
                                    220,
                                    MediaQuery.sizeOf(context).height * .34,
                                  ),
                                ),
                                child: Stack(
                                  fit: StackFit.expand,
                                  children: [
                                    if (widget.training &&
                                        widget.sportTheme != 'swimming')
                                      Image.asset(
                                        slide.image,
                                        fit: BoxFit.cover,
                                        alignment: const Alignment(0, -.4),
                                        excludeFromSemantics: true,
                                      )
                                    else
                                      ColoredBox(
                                        color: c.primaryContainer,
                                        child: Icon(
                                          widget.sportTheme == 'swimming'
                                              ? Icons.pool_outlined
                                              : slide.icon,
                                          size: 110,
                                          color: c.primary,
                                        ),
                                      ),
                                    PositionedDirectional(
                                      bottom: 16,
                                      start: 16,
                                      child: Container(
                                        padding: const EdgeInsets.all(12),
                                        decoration: BoxDecoration(
                                          color: c.surface,
                                          borderRadius: BorderRadius.circular(
                                            16,
                                          ),
                                        ),
                                        child: Icon(
                                          slide.icon,
                                          size: 25,
                                          color: c.primary,
                                        ),
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                            ),
                          ),
                          const SizedBox(height: 24),
                          Text(
                            tr(slide.kickerAr, slide.kickerEn),
                            style: TextStyle(
                              fontSize: 12,
                              fontWeight: FontWeight.w700,
                              color: c.primary,
                            ),
                          ),
                          const SizedBox(height: 8),
                          Semantics(
                            header: true,
                            child: Text(
                              tr(slide.titleAr, slide.titleEn),
                              style: const TextStyle(
                                fontSize: 28,
                                fontWeight: FontWeight.w800,
                                height: 1.35,
                              ),
                            ),
                          ),
                          const SizedBox(height: 12),
                          Text(
                            tr(slide.bodyAr, slide.bodyEn),
                            style: TextStyle(
                              fontSize: 15,
                              height: 1.6,
                              color: c.onSurfaceVariant,
                            ),
                          ),
                          if (widget.error != null) ...[
                            const SizedBox(height: 12),
                            Text(
                              widget.error!,
                              style: TextStyle(color: c.error),
                            ),
                          ],
                          if (widget.onContinueOnce != null &&
                              index == pages.length - 1)
                            TextButton(
                              onPressed: widget.onContinueOnce,
                              child: Text(
                                tr(
                                  'متابعة لهذه الجلسة فقط',
                                  'Continue for this session only',
                                ),
                              ),
                            ),
                        ],
                      ),
                    ),
                  ),
                  Padding(
                    padding: const EdgeInsets.fromLTRB(24, 8, 24, 16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Semantics(
                          excludeSemantics: true,
                          label: tr(
                            'الخطوة ${index + 1} من 3',
                            'Step ${index + 1} of 3',
                          ),
                          child: Row(
                            children: [
                              for (var n = 0; n < 3; n++)
                                Expanded(
                                  child: Padding(
                                    padding: const EdgeInsets.symmetric(
                                      horizontal: 3,
                                    ),
                                    child: ClipRRect(
                                      borderRadius: BorderRadius.circular(4),
                                      child: LinearProgressIndicator(
                                        value: n <= index ? 1 : 0,
                                        minHeight: 4,
                                        color: c.primary,
                                        backgroundColor: c.outlineVariant,
                                      ),
                                    ),
                                  ),
                                ),
                            ],
                          ),
                        ),
                        const SizedBox(height: 18),
                        Flex(
                          direction: large ? Axis.vertical : Axis.horizontal,
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            if (index > 0)
                              Padding(
                                padding: const EdgeInsetsDirectional.only(
                                  end: 12,
                                ),
                                child: TextButton.icon(
                                  onPressed: locked
                                      ? null
                                      : () => move(index - 1),
                                  icon: const Icon(Icons.arrow_back_rounded),
                                  label: Text(tr('السابق', 'Back')),
                                ),
                              ),
                            Flexible(
                              fit: large ? FlexFit.loose : FlexFit.tight,
                              child: FilledButton(
                                key: const ValueKey('role-intro-next'),
                                onPressed: locked
                                    ? null
                                    : index < 2
                                    ? () => move(index + 1)
                                    : complete,
                                child: Row(
                                  mainAxisAlignment: MainAxisAlignment.center,
                                  mainAxisSize: MainAxisSize.min,
                                  children: [
                                    Flexible(
                                      child: Text(
                                        tr(
                                          locked
                                              ? 'جارٍ الحفظ…'
                                              : index < 2
                                              ? 'التالي'
                                              : 'ابدأ',
                                          locked
                                              ? 'Saving…'
                                              : index < 2
                                              ? 'Next'
                                              : 'Start now',
                                        ),
                                      ),
                                    ),
                                    const SizedBox(width: 12),
                                    const Icon(
                                      Icons.arrow_forward_rounded,
                                      size: 19,
                                    ),
                                  ],
                                ),
                              ),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
