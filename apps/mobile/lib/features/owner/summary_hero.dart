import 'package:flutter/material.dart';

import '../../core/theme/academy_home_skin.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/experience_ui.dart';

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
    final change = collections['changePercentage'] as num?,
        progress = target['achievement'] as num?;
    final accent = academyAccent(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Expanded(
              child: Row(
                children: [
                  const Flexible(
                    child: UiText(
                      'التحصيل خلال الفترة',
                      style: TextStyle(
                        fontSize: 12,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                  IconButton(
                    tooltip: 'تعريف التحصيل',
                    icon: const Icon(Icons.info_outline, size: 16),
                    onPressed: () => showDialog<void>(
                      context: context,
                      builder: (c) => AlertDialog(
                        title: const UiText('التحصيل خلال الفترة'),
                        content: const UiText(
                          'التحصيل الفعلي بعد الاسترداد، بنفس الفرع والفترة المختارين. المقارنة بفترة سابقة مساوية في عدد الأيام.',
                        ),
                        actions: [
                          TextButton(
                            onPressed: () => Navigator.pop(c),
                            child: const UiText('حسنًا'),
                          ),
                        ],
                      ),
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(width: 12),
            if (onTarget != null)
              SizedBox(
                width: 154,
                child: OutlinedButton(
                  key: const ValueKey('collection-target-button'),
                  onPressed: onTarget,
                  style: OutlinedButton.styleFrom(
                    minimumSize: const Size(0, 44),
                    foregroundColor: academyInk(context),
                    backgroundColor: accent.withValues(alpha: .09),
                    padding: const EdgeInsets.symmetric(horizontal: 8),
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(12),
                    ),
                  ),
                  child: const FittedBox(
                    fit: BoxFit.scaleDown,
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(Icons.flag_outlined, size: 15),
                        SizedBox(width: 5),
                        UiText(
                          'ضبط هدف التحصيل',
                          maxLines: 1,
                          softWrap: false,
                          style: TextStyle(
                            fontSize: 11,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
          ],
        ),

        Row(
          key: const ValueKey('summary-halves'),
          crossAxisAlignment: CrossAxisAlignment.center,
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  InkWell(
                    onTap: onReport,
                    child: Padding(
                      padding: const EdgeInsets.symmetric(vertical: 2),
                      child: FittedBox(
                        fit: BoxFit.scaleDown,
                        alignment: AlignmentDirectional.centerStart,
                        child: MoneyText(
                          collections['net'],
                          data['currency'],
                          size: 32,
                        ),
                      ),
                    ),
                  ),
                  if (change != null)
                    Row(
                      children: [
                        Icon(
                          change < 0
                              ? Icons.trending_down_rounded
                              : Icons.trending_up_rounded,
                          key: const ValueKey('collection-trend-icon'),
                          size: 24,
                          color: change < 0
                              ? ExperienceColors.negative(context)
                              : ExperienceColors.positive(context),
                        ),
                        const SizedBox(width: 4),
                        Expanded(
                          child: UiText(
                            '${change.abs().toStringAsFixed(1)}% ${change < 0 ? 'أقل' : 'أعلى'} من الفترة السابقة',
                            style: TextStyle(
                              fontSize: 10,
                              color: change < 0
                                  ? ExperienceColors.negative(context)
                                  : ExperienceColors.positive(context),
                            ),
                          ),
                        ),
                      ],
                    ),
                  if (target['amount'] != null && progress != null) ...[
                    const SizedBox(height: 12),
                    Row(
                      key: const ValueKey('summary-target-inline'),
                      crossAxisAlignment: CrossAxisAlignment.center,
                      children: [
                        SizedBox(
                          width: 52,
                          height: 52,
                          child: Stack(
                            alignment: Alignment.center,
                            children: [
                              SizedBox.expand(
                                child: CircularProgressIndicator(
                                  value: (progress / 100)
                                      .clamp(0, 1)
                                      .toDouble(),
                                  color: accent,
                                  strokeWidth: 5,
                                  strokeCap: StrokeCap.round,
                                  backgroundColor: accent.withValues(alpha: .1),
                                  semanticsLabel: 'تحقيق هدف التحصيل',
                                  semanticsValue:
                                      '${progress.toStringAsFixed(0)}%',
                                ),
                              ),
                              UiText(
                                '${progress.toStringAsFixed(0)}%',
                                style: const TextStyle(
                                  fontSize: 14,
                                  fontWeight: FontWeight.w700,
                                ),
                              ),
                            ],
                          ),
                        ),
                        const SizedBox(width: 8),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              const UiText(
                                'هدف الفترة',
                                style: TextStyle(fontSize: 11),
                              ),
                              FittedBox(
                                fit: BoxFit.scaleDown,
                                alignment: AlignmentDirectional.centerStart,
                                child: MoneyText(
                                  target['amount'],
                                  data['currency'],
                                  size: 21,
                                ),
                              ),
                              UiText(
                                'المتبقي ${money(((target['amount'] as num) - (collections['net'] as num)).clamp(0, double.infinity), data['currency'])}',
                                style: TextStyle(
                                  fontSize: 10,
                                  color: Theme.of(context)
                                      .colorScheme
                                      .onSurfaceVariant,
                                ),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),
                  ],
                ],
              ),
            ),
          ],
        ),
      ],
    );
  }
}
