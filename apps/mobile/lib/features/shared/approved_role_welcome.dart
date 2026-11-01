import 'package:flutter/material.dart';

import 'experience_ui.dart';

/// Identity is supplied by the authenticated role gate, never a role picker.
class ApprovedRoleWelcome extends StatelessWidget {
  const ApprovedRoleWelcome({
    super.key,
    required this.role,
    required this.displayName,
    required this.academyName,
    required this.training,
    this.sportTheme = 'general',
    required this.onEnter,
  });
  final String role, displayName, academyName;
  final bool training;
  final String sportTheme;
  final VoidCallback onEnter;

  @override
  Widget build(BuildContext context) {
    final english = Localizations.localeOf(context).languageCode == 'en';
    final background = Theme.of(context).scaffoldBackgroundColor;
    final scheme = Theme.of(context).colorScheme;
    final (image, roleAr, roleEn, lineAr, lineEn) = switch (role) {
      'AcademyOwner' => (
        'assets/demo/portraits/child-01.jpg',
        'المالك',
        'Owner',
        'ابدأ من الرئيسية لمراجعة الأكاديمية، وافتح العمليات والتقارير حسب صلاحياتك.',
        'Start with your academy overview, then open your authorized operations and reports.',
      ),
      'AcademyAdmin' => (
        'assets/demo/portraits/coach-01.jpg',
        'الإداري',
        'Administrator',
        'تابع اللاعبين والحضور والطلبات المتاحة لحسابك داخل الأكاديمية.',
        'Follow the players, attendance and requests available to your academy account.',
      ),
      'Coach' => (
        'assets/demo/portraits/child-02.jpg',
        'المدرب',
        'Coach',
        'راجع مجموعاتك وحصصك، وسجّل الحضور والتقييم في نطاقك.',
        'Review your assigned groups and sessions, and record attendance and evaluations within your scope.',
      ),
      'Guardian' => (
        'assets/demo/portraits/guardian-01.jpg',
        'ولي الأمر',
        'Guardian',
        'تابع أبناءك المرتبطين بحسابك ومواعيدهم واشتراكاتهم.',
        'Follow the children linked to your account, their schedules and subscriptions.',
      ),
      _ => (
        'assets/demo/portraits/guardian-01.jpg',
        'حسابك',
        'Account',
        'تصفح الخدمات المتاحة لعضويتك الحالية.',
        'Open the services available to your current membership.',
      ),
    };
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 650),
            child: LayoutBuilder(
              builder: (context, bounds) => SingleChildScrollView(
                child: Stack(
                  children: [
                    PositionedDirectional(
                      top: 0,
                      start: 0,
                      end: 0,
                      // Square illustrative portraits must not be stretched to
                      // full screen height. Finish their fade before the first
                      // possible heading: spacer + padding + check + gap.
                      height: bounds.maxHeight * .38 + 24 + 56 + 14,
                      child: ExcludeSemantics(
                        key: const ValueKey('welcome-artwork'),
                        child: training && sportTheme != 'swimming'
                            ? Image.asset(
                                image,
                                fit: BoxFit.cover,
                                alignment: const Alignment(0, -.4),
                              )
                            : Center(
                                child: Icon(
                                  sportTheme == 'swimming'
                                      ? Icons.pool_rounded
                                      : Icons.school_outlined,
                                  size: 160,
                                  color: scheme.primary.withValues(alpha: .2),
                                ),
                              ),
                      ),
                    ),
                    PositionedDirectional(
                      top: 0,
                      start: 0,
                      end: 0,
                      height: bounds.maxHeight * .38 + 24 + 56 + 14,
                      child: DecoratedBox(
                        decoration: BoxDecoration(
                          gradient: LinearGradient(
                            begin: Alignment.topCenter,
                            end: Alignment.bottomCenter,
                            colors: [
                              background.withValues(alpha: .15),
                              background.withValues(alpha: .25),
                              background,
                            ],
                            stops: const [0, .5, 1],
                          ),
                        ),
                      ),
                    ),
                    ConstrainedBox(
                      constraints: BoxConstraints(minHeight: bounds.maxHeight),
                      child: Column(
                        mainAxisSize: MainAxisSize.min,
                        mainAxisAlignment: MainAxisAlignment.end,
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          SizedBox(height: bounds.maxHeight * .38),
                          ColoredBox(
                            key: const ValueKey('welcome-readable-panel'),
                            color: Colors.transparent,
                            child: Padding(
                              padding: const EdgeInsets.fromLTRB(
                                24,
                                24,
                                24,
                                48,
                              ),
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.stretch,
                                children: [
                                  Align(
                                    alignment: AlignmentDirectional.centerStart,
                                    child: Container(
                                      width: 56,
                                      height: 56,
                                      decoration: BoxDecoration(
                                        border: Border.all(
                                          color: ExperienceColors.positive(
                                            context,
                                          ),
                                          width: 2,
                                        ),
                                      ),
                                      child: Icon(
                                        Icons.check_rounded,
                                        size: 26,
                                        color: ExperienceColors.positive(
                                          context,
                                        ),
                                        semanticLabel: english
                                            ? 'Account verified'
                                            : 'تم التحقق من الحساب',
                                      ),
                                    ),
                                  ),
                                  const SizedBox(height: 14),
                                  Text(
                                    '${english ? 'Welcome, ' : 'أهلًا، '}$displayName',
                                    key: const ValueKey('welcome-identity'),
                                    style: TextStyle(
                                      fontSize: 28,
                                      height: 1.3,
                                      fontWeight: FontWeight.w600,
                                      color: scheme.onSurface,
                                    ),
                                  ),
                                  const SizedBox(height: 14),
                                  Text(
                                    training
                                        ? (english ? lineEn : lineAr)
                                        : (english
                                              ? 'Open the services available to your academy account.'
                                              : 'تصفح الخدمات المتاحة لحسابك داخل الأكاديمية.'),
                                    style: TextStyle(
                                      fontSize: 15,
                                      height: 1.7,
                                      color: scheme.onSurfaceVariant,
                                    ),
                                  ),
                                  const SizedBox(height: 14),
                                  Wrap(
                                    spacing: 6,
                                    runSpacing: 6,
                                    children: [
                                      for (final label in [
                                        english ? roleEn : roleAr,
                                        academyName,
                                      ])
                                        Container(
                                          padding: const EdgeInsets.symmetric(
                                            horizontal: 10,
                                            vertical: 6,
                                          ),
                                          decoration: BoxDecoration(
                                            border: Border.all(
                                              color: scheme.outline,
                                            ),
                                          ),
                                          child: Text(
                                            label,
                                            style: TextStyle(
                                              fontSize: 12,
                                              color: scheme.onSurface,
                                            ),
                                          ),
                                        ),
                                    ],
                                  ),
                                  const SizedBox(height: 20),
                                  FilledButton(
                                    key: const ValueKey('role-welcome-enter'),
                                    onPressed: onEnter,
                                    style: FilledButton.styleFrom(
                                      minimumSize: const Size.fromHeight(54),
                                      padding: const EdgeInsets.symmetric(
                                        horizontal: 18,
                                        vertical: 14,
                                      ),
                                    ),
                                    child: Row(
                                      children: [
                                        Expanded(
                                          child: Text(
                                            english
                                                ? 'Open your home'
                                                : 'الدخول إلى الرئيسية',
                                            style: const TextStyle(
                                              fontSize: 15,
                                              fontWeight: FontWeight.w700,
                                            ),
                                          ),
                                        ),
                                        const SizedBox(width: 12),
                                        const Icon(
                                          Icons.arrow_forward_rounded,
                                          size: 18,
                                        ),
                                      ],
                                    ),
                                  ),
                                ],
                              ),
                            ),
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
      ),
    );
  }
}
