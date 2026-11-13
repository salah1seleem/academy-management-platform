import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../../core/widgets/arabic_number_input.dart';
import '../guardian/guardian_ui.dart';

String evaluationStatusText(Object? status) => switch (status) {
  'Published' => 'منشور لولي الأمر',
  'PublishedInternal' => 'للاستخدام الداخلي',
  _ => 'مسودة',
};

String? evaluationDateText(BuildContext context, Object? value) {
  final date = value is String ? DateTime.tryParse(value) : null;
  if (date == null) return null;
  final strings = MaterialLocalizations.of(context);
  return canonicalNumericInput('${date.day} ${strings.formatMonthYear(date)}');
}

class CoachEvaluationRecord extends StatelessWidget {
  const CoachEvaluationRecord({
    super.key,
    required this.auth,
    required this.enrollment,
    required this.evaluation,
    this.message,
  });

  final AuthController auth;
  final Json enrollment;
  final Json evaluation;
  final String? message;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = theme.colorScheme;
    final player = textOf(evaluation['player']);
    final sport = evaluation['sport'] as String?;
    final group = evaluation['group'] as String?;
    final date = evaluationDateText(context, evaluation['evaluationDate']);
    final period = (evaluation['reportingPeriod'] as String?)?.trim();
    final notes = (evaluation['generalNotes'] as String?)?.trim();
    final criteria = rows(evaluation['scores']);
    final scored = criteria.where((item) => item['score'] != null).length;
    final internal = evaluation['status'] == 'PublishedInternal';
    final metadata = [
      ?date,
      if (period != null && period.isNotEmpty && period != 'تقييم دوري') period,
    ];

    return gList([
      GCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                PlayerAvatar(
                  player,
                  auth: auth,
                  reference: enrollment['photoReference'],
                  gender: enrollment['gender'],
                  size: 48,
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      UiText(
                        player,
                        style: theme.textTheme.titleLarge?.copyWith(
                          fontSize: 18,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      if (sport != null && sport.isNotEmpty) ...[
                        const SizedBox(height: 4),
                        UiText(sport),
                      ],
                    ],
                  ),
                ),
              ],
            ),
            if (group != null && group.isNotEmpty) ...[
              const SizedBox(height: 8),
              UiText(group, style: theme.textTheme.bodyMedium),
            ],
            if (metadata.isNotEmpty)
              UiText(metadata.join(' · '), style: theme.textTheme.bodySmall),
            const SizedBox(height: 12),
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(
                  internal ? Icons.lock_outline : Icons.check_circle_outline,
                  size: 19,
                  color: colors.primary,
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: Semantics(
                    liveRegion: message != null,
                    child: UiText(
                      message ?? evaluationStatusText(evaluation['status']),
                      style: theme.textTheme.bodyMedium?.copyWith(
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
      const SizedBox(height: 12),
      Semantics(
        header: true,
        child: UiText('الدرجات حسب المعيار', style: theme.textTheme.titleLarge),
      ),
      const SizedBox(height: 6),
      if (criteria.isEmpty)
        const EmptyMessage('لا توجد معايير في هذا التقييم.')
      else ...[
        UiText(
          'الدرجات المسجّلة $scored من ${criteria.length}',
          style: theme.textTheme.bodyMedium,
        ),
        const SizedBox(height: 8),
        for (final item in criteria) _CriterionResult(item: item),
      ],
      if (notes != null && notes.isNotEmpty)
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Semantics(
                header: true,
                child: UiText(
                  'ملاحظات التقييم',
                  style: theme.textTheme.titleLarge,
                ),
              ),
              const SizedBox(height: 6),
              UiText(notes),
            ],
          ),
        ),
    ]);
  }
}

class _CriterionResult extends StatelessWidget {
  const _CriterionResult({required this.item});

  final Json item;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final score = item['score'] as num?;
    final notes = (item['notes'] as String?)?.trim();
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          UiText(
            textOf(item['name']),
            style: theme.textTheme.titleSmall?.copyWith(
              fontWeight: FontWeight.w700,
            ),
          ),
          const SizedBox(height: 4),
          UiText(
            score == null
                ? 'لم يُقيّم'
                : '${performanceScoreText(score)} من 100',
            style: theme.textTheme.bodyMedium?.copyWith(
              color: score == null
                  ? theme.colorScheme.onSurfaceVariant
                  : theme.colorScheme.primary,
              fontWeight: score == null ? FontWeight.normal : FontWeight.w700,
            ),
          ),
          if (notes != null && notes.isNotEmpty) ...[
            const SizedBox(height: 6),
            UiText(notes, style: theme.textTheme.bodyMedium),
          ],
        ],
      ),
    );
  }
}
