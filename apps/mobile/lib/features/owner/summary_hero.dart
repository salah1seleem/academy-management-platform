import 'package:flutter/material.dart';

import '../../core/theme/academy_home_skin.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/experience_ui.dart';

class PeriodTrend extends StatelessWidget {
  const PeriodTrend({super.key, required this.change});

  final num change;

  @override
  Widget build(BuildContext context) {
    final color = change < 0
        ? ExperienceColors.negative(context)
        : change > 0
        ? ExperienceColors.positive(context)
        : Theme.of(context).colorScheme.onSurfaceVariant;
    return Row(
      children: [
        Directionality(
          textDirection: TextDirection.ltr,
          child: Icon(
            change < 0
                ? Icons.trending_down_rounded
                : change > 0
                ? Icons.trending_up_rounded
                : Icons.trending_flat_rounded,
            key: const ValueKey('collection-trend-icon'),
            size: 19,
            color: color,
          ),
        ),
        const SizedBox(width: 4),
        Expanded(
          child: UiText(
            change == 0
                ? 'مثل الفترة السابقة'
                : '${performanceScoreText(change.abs())}% ${change < 0 ? 'أقل' : 'أعلى'} من الفترة السابقة',
            style: TextStyle(
              fontSize: 11,
              color: color,
              fontWeight: FontWeight.w600,
            ),
          ),
        ),
      ],
    );
  }
}

class SummaryHero extends StatelessWidget {
  const SummaryHero({
    super.key,
    required this.data,
    required this.onReport,
    this.onTarget,
  });

  final Json data;
  final VoidCallback onReport;
  final VoidCallback? onTarget;

  @override
  Widget build(BuildContext context) {
    final collections = obj(data['collections']), target = obj(data['target']);
    final change = collections['changePercentage'] as num?;
    final progress = target['achievement'] as num?;
    final goal = target['amount'] as num?;
    final colors = Theme.of(context).colorScheme;
    final accent = academyAccent(context);
    final achieved = progress == null ? null : performanceScoreText(progress);
    final collected = Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        TextButton(
          onPressed: onReport,
          style: TextButton.styleFrom(
            alignment: AlignmentDirectional.centerStart,
            foregroundColor: colors.onSurface,
            padding: EdgeInsets.zero,
          ),
          child: FittedBox(
            fit: BoxFit.scaleDown,
            alignment: AlignmentDirectional.centerStart,
            child: MoneyText(collections['net'], data['currency'], size: 28),
          ),
        ),
        if (change != null) PeriodTrend(change: change),
      ],
    );
    final targetTile = goal == null
        ? UiText(
            'لم يُحدد هدف للفترة',
            style: TextStyle(fontSize: 12, color: colors.onSurfaceVariant),
          )
        : Container(
            key: const ValueKey('summary-target-inline'),
            padding: const EdgeInsets.all(9),
            decoration: BoxDecoration(
              color: accent.withValues(alpha: .07),
              borderRadius: BorderRadius.circular(12),
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                UiText(
                  'هدف الفترة',
                  style: TextStyle(
                    fontSize: 11,
                    color: colors.onSurfaceVariant,
                  ),
                ),
                const SizedBox(height: 4),
                FittedBox(
                  fit: BoxFit.scaleDown,
                  alignment: AlignmentDirectional.centerStart,
                  child: MoneyText(goal, data['currency'], size: 18),
                ),
                if (progress != null) ...[
                  const SizedBox(height: 6),
                  LinearProgressIndicator(
                    value: (progress / 100).clamp(0, 1).toDouble(),
                    color: accent,
                    borderRadius: BorderRadius.circular(4),
                    minHeight: 5,
                    semanticsLabel: progress < 0 || progress > 100
                        ? 'تحقيق هدف التحصيل، $achieved% من الهدف'
                        : 'تحقيق هدف التحصيل',
                    semanticsValue:
                        '${performanceScoreText(progress.clamp(0, 100))}%',
                  ),
                  const SizedBox(height: 3),
                  ExcludeSemantics(
                    child: UiText(
                      '$achieved%',
                      style: const TextStyle(
                        fontSize: 11,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                  ),
                ],
              ],
            ),
          );
    const heading = UiText(
      'التحصيل خلال الفترة',
      style: TextStyle(fontSize: 12, fontWeight: FontWeight.w700),
    );
    return LayoutBuilder(
      builder: (context, bounds) {
        final compact =
            bounds.maxWidth >= 238 * MediaQuery.textScalerOf(context).scale(1);
        final action = onTarget == null
            ? null
            : OutlinedButton(
                key: const ValueKey('collection-target-button'),
                onPressed: onTarget,
                style: OutlinedButton.styleFrom(
                  padding: const EdgeInsets.symmetric(horizontal: 8),
                  minimumSize: const Size(44, 44),
                ),
                child: UiText(
                  'ضبط هدف التحصيل',
                  maxLines: compact ? 1 : null,
                  softWrap: !compact,
                  style: const TextStyle(
                    fontSize: 11,
                    fontWeight: FontWeight.w800,
                  ),
                ),
              );
        final showTarget = goal != null || onTarget != null;
        return Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (action == null)
              heading
            else if (compact)
              Row(
                children: [
                  const Expanded(child: heading),
                  const SizedBox(width: 8),
                  action,
                ],
              )
            else
              Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [heading, const SizedBox(height: 4), action],
              ),
            const SizedBox(height: 4),
            if (compact && showTarget)
              Row(
                key: const ValueKey('summary-halves'),
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(flex: 6, child: collected),
                  const SizedBox(width: 9),
                  Expanded(flex: 4, child: targetTile),
                ],
              )
            else
              Column(
                key: const ValueKey('summary-halves'),
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  collected,
                  if (showTarget) ...[const SizedBox(height: 8), targetTile],
                ],
              ),
          ],
        );
      },
    );
  }
}
