import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../shared/experience_ui.dart';
import 'guardian_report.dart';
import 'guardian_ui.dart';

/// Approved Guardian child-detail portrait, performance and identity composition.
class GuardianProfileHeader extends StatelessWidget {
  const GuardianProfileHeader({
    super.key,
    required this.auth,
    required this.player,
    required this.today,
    this.enrollment,
    required this.performance,
    this.evaluationCount,
    this.monthlyStars,
  });
  final AuthController auth;
  final Json player, performance;
  final Json? enrollment;
  final String today;
  final int? evaluationCount, monthlyStars;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final heroSecondary = colors.onSurfaceVariant;
    final score = performance['meanPerformance'];
    final axes = rows(performance['axes'] ?? <Json>[]);
    final criteria = rows(performance['criteria'] ?? <Json>[])
        .where((c) => validAssessment(c['value']))
        .toList();
    Widget performanceView() => Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        UiText(
          'مؤشر الأداء · ${enrollment?['sport'] ?? 'لا يوجد تسجيل نشط'}',
          style: TextStyle(
            fontSize: 10,
            fontWeight: FontWeight.w600,
            color: heroSecondary,
          ),
        ),
        if (performance['isFootballReport'] == true &&
            axes.any((a) => validAssessment(a['value'])))
          RadarChart(axes, accentColor: colors.primary)
        else if (criteria.isNotEmpty)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 12),
            child: Column(
              children: [
                for (final c in criteria.take(6))
                  Padding(
                    padding: const EdgeInsets.only(bottom: 10),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        UiText(
                          '${c['name']} · ${c['value']}',
                          style: const TextStyle(fontSize: 11),
                        ),
                        const SizedBox(height: 4),
                        LinearProgressIndicator(
                          value: (c['value'] as num).toDouble() / 100,
                          minHeight: 3,
                        ),
                      ],
                    ),
                  ),
              ],
            ),
          )
        else
          const SizedBox(
            height: 180,
            child: Center(
              child: UiText(
                'لا يوجد تقييم منشور لهذه الرياضة بعد.',
                style: TextStyle(fontSize: 12),
              ),
            ),
          ),
      ],
    );
    Widget portrait(double width) => ClipRRect(
      borderRadius: BorderRadius.circular(18),
      child: SizedBox(
        width: width,
        height: 210,
        child: Stack(
          fit: StackFit.expand,
          children: [
            PlayerAvatar(
              textOf(player['arabicName']),
              auth: auth,
              reference: player['photoReference'],
              gender: player['gender'],
              size: width,
              height: 210,
              alignment: const Alignment(0, -.56),
              square: true,
            ),
            const PositionedDirectional(
              top: 0,
              end: 0,
              child: AcademyCrest(size: 30),
            ),
          ],
        ),
      ),
    );
    Widget metric(
      String value,
      String title,
      String note, {
      bool accent = false,
    }) => Padding(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 10),
      child: Wrap(
        spacing: 8,
        runSpacing: 4,
        crossAxisAlignment: WrapCrossAlignment.center,
        children: [
          UiText(
            value,
            style: TextStyle(
              fontSize: 42,
              fontWeight: FontWeight.w600,
              height: 1,
              color: accent
                  ? ExperienceColors.information(context)
                  : colors.onSurface,
            ),
          ),
          Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              UiText(
                title,
                style: const TextStyle(
                  fontSize: 13,
                  fontWeight: FontWeight.w600,
                ),
              ),
              UiText(
                note,
                style: TextStyle(fontSize: 11, color: heroSecondary),
              ),
            ],
          ),
        ],
      ),
    );
    return Container(
      key: const ValueKey('guardian-profile-header'),
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(26),
        border: Border.all(color: colors.outlineVariant),
      ),
      clipBehavior: Clip.antiAlias,
      child: Container(
        key: const ValueKey('guardian-profile-hero-surface'),
        decoration: BoxDecoration(
          gradient: LinearGradient(
            colors: [
              Color.alphaBlend(
                colors.primary.withValues(alpha: .045),
                colors.surface,
              ),
              colors.surface,
            ],
          ),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(12, 12, 12, 0),
              child: LayoutBuilder(
                builder: (context, bounds) {
                  if (MediaQuery.textScalerOf(context).scale(14) > 20) {
                    return Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Center(child: portrait(bounds.maxWidth * .6)),
                        const SizedBox(height: 12),
                        performanceView(),
                      ],
                    );
                  }
                  return Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      portrait(bounds.maxWidth * .44),
                      const SizedBox(width: 8),
                      Expanded(child: performanceView()),
                    ],
                  );
                },
              ),
            ),
            Container(
              margin: const EdgeInsets.fromLTRB(12, 12, 12, 0),
              decoration: BoxDecoration(
                border: Border.symmetric(
                  horizontal: BorderSide(
                    color: colors.primary.withValues(alpha: .45),
                  ),
                ),
              ),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: metric(
                      validAssessment(score)
                          ? performanceScoreText(score as num)
                          : '—',
                      'متوسط الأداء',
                      performance['scoredCriteria'] is num &&
                              performance['applicableCriteria'] is num
                          ? "${performance['scoredCriteria']} من ${performance['applicableCriteria']} معايير منشورة"
                          : 'آخر تقييم منشور',
                      accent: true,
                    ),
                  ),
                  Expanded(
                    child: metric(
                      evaluationCount?.toString() ?? '—',
                      'تقييمات',
                      performance['evaluationDate'] == null
                          ? 'لا يوجد تقييم منشور'
                          : day(performance['evaluationDate']),
                    ),
                  ),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(14, 12, 14, 14),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(
                    textOf(player['arabicName']),
                    style: const TextStyle(
                      fontSize: 22,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  const SizedBox(height: 4),
                  UiText(
                    childContextLine(player, enrollment, today),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(fontSize: 12, color: heroSecondary),
                  ),
                  if (enrollment != null)
                    UiText(
                      textOf(enrollment!['group']),
                      style: TextStyle(fontSize: 12, color: heroSecondary),
                    ),
                  if ((monthlyStars ?? 0) > 0)
                    UiText(
                      List.filled(monthlyStars!, '★').join(' '),
                      semanticsLabel: '$monthlyStars نجوم هذا الشهر',
                      style: TextStyle(
                        fontSize: 18,
                        color: ExperienceColors.award(context),
                      ),
                    ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
