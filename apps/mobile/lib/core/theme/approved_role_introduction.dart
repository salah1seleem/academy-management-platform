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
      'نظرة شاملة',
      'Overview',
      'صورة واضحة لأكاديميتك',
      'A clear view of your academy',
      'تابع الاشتراكات والتحصيلات والمصروفات، واعرف ما يحتاج إلى انتباهك اليوم.',
      'Follow subscriptions, collections and expenses, and see what needs your attention today.',
      Icons.insights_outlined,
    ),
    _Slide(
      _coach,
      'مركز القرارات',
      'Decisions',
      'تابع التفاصيل التي تصنع الفرق',
      'Follow the details that make a difference',
      'راجع التجديدات القريبة، وتابع اللاعبين والمدربين والمهام من مكان واحد.',
      'Review upcoming renewals, players, coaches and tasks in one place.',
      Icons.task_alt_outlined,
    ),
    _Slide(
      _child2,
      'العمليات',
      'Operations',
      'تجربة تليق باسم أكاديميتك',
      'An experience with your academy identity',
      'اجمع فريقك وأولياء الأمور في تجربة تحمل هويتك وتسهّل متابعة التدريب.',
      'Bring your team and guardians together in a branded experience that makes training easier to follow.',
      Icons.bolt_outlined,
    ),
  ],
  'AcademyAdmin': [
    _Slide(
      _coach,
      'مهامي',
      'My tasks',
      'يومك أوضح، وشغلك أسهل',
      'A clearer day, simpler work',
      'ابدأ بالتسجيل والتجديد، ووصل إلى حصص اليوم ومهامك بسرعة.',
      'Start with registration and renewal, then reach today’s sessions and tasks quickly.',
      Icons.checklist_outlined,
    ),
    _Slide(
      _child1,
      'الحضور',
      'Attendance',
      'كل اشتراك في وقته',
      'Every subscription on time',
      'تابع الاشتراكات الفعالة والقريبة من الانتهاء والمنتهية ضمن الفروع المتاحة لك.',
      'Follow active, expiring and expired subscriptions within your authorized branches.',
      Icons.event_available_outlined,
    ),
    _Slide(
      _child2,
      'ملف اللاعب',
      'Player profile',
      'من التسجيل إلى التدريب',
      'From registration to training',
      'راجع بيانات اللاعبين ومجموعاتهم ومواعيدهم، وأكمل إجراءاتك من الصفحات المتاحة لك.',
      'Review players, groups and schedules, and complete workflows through your permitted pages.',
      Icons.person_outline,
    ),
  ],
  'Coach': [
    _Slide(
      _child2,
      'جدولي',
      'My schedule',
      'حصصك جاهزة أمامك',
      'Your sessions are ready',
      'اعرف مواعيد حصصك ومجموعاتك ولاعبيك، وابدأ يومك بخطة واضحة.',
      'See your sessions, groups and players, and start with a clear plan.',
      Icons.calendar_month_outlined,
    ),
    _Slide(
      _coach,
      'التقييم والنجوم',
      'Evaluations and stars',
      'تابع تطور كل لاعب',
      'Follow every player’s progress',
      'سجّل التقييمات والملاحظات، وانشر لولي الأمر ما يساعده على متابعة تطور ابنه.',
      'Record evaluations and notes, and publish feedback that helps guardians follow their child’s progress.',
      Icons.star_outline,
    ),
    _Slide(
      _child1,
      'الحضور',
      'Attendance',
      'حضور واضح، ومتابعة مستمرة',
      'Clear attendance and continued follow-up',
      'سجّل حضور لاعبيك، وراجع مشاركتهم وملاحظاتهم لتستعد للحصة التالية.',
      'Record attendance and review participation and notes to prepare for the next session.',
      Icons.camera_alt_outlined,
    ),
  ],
  'Guardian': [
    _Slide(
      _child1,
      'أبنائي',
      'My children',
      'شوف تطور ابنك بوضوح',
      'See your child’s progress clearly',
      'اكتشف بطاقته الرياضية وتقييمات المدرب، وتعرّف على نقاط قوته وما يحتاج إلى تطويره.',
      'Explore their sports card and coach evaluations, strengths and areas to develop.',
      Icons.groups_outlined,
    ),
    _Slide(
      _child2,
      'المواعيد',
      'Schedule',
      'ابقَ قريبًا من رحلته',
      'Stay close to their journey',
      'تابع مواعيد التدريب والحضور والغياب والتحديثات الخاصة بابنك.',
      'Follow training schedules, attendance, absences and updates for your child.',
      Icons.calendar_month_outlined,
    ),
    _Slide(
      'assets/onboarding/m1.jpg',
      'الاشتراك والمتابعة',
      'Subscriptions and follow-up',
      'اشتراكه ومتابعته بسهولة',
      'Subscriptions and follow-up made easy',
      'اعرف موعد انتهاء الاشتراك، وجدّده أو قدّم طلب اشتراك جديد من الخيارات المتاحة لك.',
      'Check the subscription end date, renew it or request a new subscription from your available options.',
      Icons.favorite_outline,
    ),
  ],
};
const _general = [
  _Slide(
    _guardian,
    'خدماتك',
    'Your services',
    'أكاديميتك في مكان واحد',
    'Your academy, together',
    'افتح الخدمات المتاحة لحسابك من داخل الأكاديمية المختارة.',
    'Explore the services available in your selected academy.',
    Icons.school_outlined,
  ),
  _Slide(
    _coach,
    'سياقك',
    'Your context',
    'سياق واضح وآمن',
    'A clear, secure context',
    'تظهر لك فقط البيانات المسموح بها لدورك. تبديل الدور لا يجمع الصلاحيات.',
    'Only authorized records are shown. Switching roles never combines access.',
    Icons.verified_user_outlined,
  ),
  _Slide(
    _guardian,
    'ابدأ',
    'Get started',
    'ابدأ بثقة',
    'Start with confidence',
    'اختر اللغة والمظهر، وأعد مشاهدة هذا التعريف من حسابي في أي وقت.',
    'Choose language and appearance, and replay this introduction from your account.',
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
                          Text(
                            tr(slide.titleAr, slide.titleEn),
                            style: const TextStyle(
                              fontSize: 28,
                              fontWeight: FontWeight.w800,
                              height: 1.35,
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
