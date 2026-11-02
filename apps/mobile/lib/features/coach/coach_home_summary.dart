import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/experience_ui.dart';
import 'monthly_stars.dart';

/// Counts only recorded attendance returned by the existing scoped sessions API.
class CoachAttendanceSummary extends StatelessWidget {
  const CoachAttendanceSummary({
    super.key,
    required this.groups,
    required this.sessions,
    required this.today,
  });
  final List<Json> groups, sessions;
  final String today;

  @override
  Widget build(BuildContext context) {
    final actual = sessions.where((s) => s['status'] != 'Cancelled').toList();
    num total(String field, Iterable<Json> rows) =>
        rows.fold<num>(0, (sum, row) => sum + (row[field] as num? ?? 0));
    final recorded = total('recorded', actual);
    final present = total('present', actual);
    final percentage = recorded > 0 ? (present * 100 / recorded).round() : null;
    final period = today.substring(0, 7);
    final rowsByGroup = <String, Json>{
      for (final group in groups) textOf(group['id']): group,
    };
    // Substitute sessions may be authorized even when the group is not in the
    // coach's regular assignments. Preserve those actual scoped records too.
    for (final session in actual) {
      final id = textOf(session['trainingGroupId']);
      rowsByGroup.putIfAbsent(
        id,
        () => {'id': id, 'arabicName': session['group']},
      );
    }
    final color = ExperienceColors.positive(context);
    final ring = Semantics(
      label: percentage == null
          ? 'لا يوجد حضور مسجل خلال الفترة'
          : 'الحضور المسجل $percentage%، $present حاضرًا من $recorded سجلًا',
      child: ExcludeSemantics(
        child: SizedBox.square(
          dimension: 84 * (MediaQuery.textScalerOf(context).scale(19) / 19),
          child: Stack(
            alignment: Alignment.center,
            children: [
              Positioned.fill(
                child: CircularProgressIndicator(
                  value: percentage == null
                      ? 0
                      : (percentage / 100).clamp(0, 1),
                  strokeWidth: 7,
                  color: color,
                  backgroundColor: Theme.of(context).dividerColor,
                ),
              ),
              Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    percentage == null ? '—' : '$percentage%',
                    key: const ValueKey('coach-recorded-percentage'),
                    style: const TextStyle(
                      fontSize: 19,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  const UiText('حضور مسجل', style: TextStyle(fontSize: 9)),
                ],
              ),
            ],
          ),
        ),
      ),
    );
    final details = Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        UiText('الحضور المسجل · $period', style: const TextStyle(fontSize: 12)),
        Text(
          '$period-01 — $today',
          textDirection: TextDirection.ltr,
          style: TextStyle(
            fontSize: 11,
            color: Theme.of(context).colorScheme.onSurfaceVariant,
          ),
        ),
        const SizedBox(height: 10),
        if (recorded == 0) const UiText('لا يوجد حضور مسجل خلال الفترة.'),
        for (final group in rowsByGroup.values) ...[
          Builder(
            builder: (context) {
              final groupSessions = actual.where(
                (s) => textOf(s['trainingGroupId']) == textOf(group['id']),
              );
              final count = total('recorded', groupSessions);
              final ratio = count > 0
                  ? total('present', groupSessions) / count
                  : null;
              return Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Expanded(
                        child: UiText(
                          textOf(group['arabicName']),
                          style: const TextStyle(
                            fontSize: 12,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ),
                      const SizedBox(width: 8),
                      Text(
                        ratio == null ? '—' : '${(ratio * 100).round()}%',
                        key: ValueKey('coach-group-attendance-${group['id']}'),
                        textDirection: TextDirection.ltr,
                        style: const TextStyle(
                          fontSize: 12,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 5),
                  LinearProgressIndicator(
                    value: (ratio ?? 0).clamp(0, 1),
                    color: color,
                  ),
                ],
              );
            },
          ),
          const SizedBox(height: 10),
        ],
        const UiText(
          'من سجلات الحضور المسجلة فقط؛ الحصص غير المسجلة لا تُحسب غيابًا.',
          style: TextStyle(fontSize: 11),
        ),
      ],
    );
    return GCard(
      child: LayoutBuilder(
        builder: (context, constraints) {
          if (MediaQuery.textScalerOf(context).scale(14) > 21 ||
              constraints.maxWidth < 280) {
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Align(alignment: AlignmentDirectional.centerStart, child: ring),
                const SizedBox(height: 16),
                details,
              ],
            );
          }
          return Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              ring,
              const SizedBox(width: 14),
              Expanded(child: details),
            ],
          );
        },
      ),
    );
  }
}

class CoachHomeStarBudgets extends StatefulWidget {
  const CoachHomeStarBudgets({super.key, required this.auth});
  final AuthController auth;
  @override
  State<CoachHomeStarBudgets> createState() => _CoachHomeStarBudgetsState();
}

class _CoachHomeStarBudgetsState extends State<CoachHomeStarBudgets> {
  int revision = 0;
  Future<void> openStars() async {
    await openPage(context, CoachMonthlyStars(auth: widget.auth));
    if (mounted) setState(() => revision++);
  }

  String? get identity {
    final membership = widget.auth.session?.selected;
    return membership?.role == 'Coach'
        ? '${membership!.academyId}:${membership.id}:${membership.contextRevision}'
        : null;
  }

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: widget.auth,
    builder: (context, _) =>
        identity == null ? const SizedBox.shrink() : content(context),
  );

  Widget content(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      const ExperienceSection('نجوم الشهر', Icons.star_border_rounded),
      RemoteBody(
        key: ValueKey('$identity:$revision'),
        auth: widget.auth,
        path: '/api/v1/coach/monthly-stars',
        builder: (context, value) {
          final data = obj(value), teams = rows(obj(value)['teams']);
          return GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (data['month'] != null)
                  Text(
                    textOf(data['month']).substring(0, 7),
                    textDirection: TextDirection.ltr,
                    style: const TextStyle(fontSize: 12),
                  ),
                if (teams.isEmpty) const UiText('لا توجد فرق مسندة إليك.'),
                for (final team in teams)
                  Padding(
                    padding: const EdgeInsets.symmetric(vertical: 10),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        UiText(
                          textOf(team['name']),
                          style: const TextStyle(
                            fontSize: 14,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                        Wrap(
                          spacing: 10,
                          crossAxisAlignment: WrapCrossAlignment.center,
                          children: [
                            ExcludeSemantics(
                              child: Row(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  for (var i = 0; i < 3; i++)
                                    Icon(
                                      i < (team['remaining'] as num? ?? 0)
                                          ? Icons.star_rounded
                                          : Icons.star_outline_rounded,
                                      color:
                                          Theme.of(context).brightness ==
                                              Brightness.light
                                          ? ExperienceColors.warning(context)
                                          : ExperienceColors.gold,
                                      size: 20,
                                    ),
                                ],
                              ),
                            ),
                            UiText(
                              '${team['remaining'] ?? '—'} من 3 متبقية',
                              key: ValueKey(
                                'coach-star-remaining-${team['id']}',
                              ),
                              style: const TextStyle(fontSize: 12),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                OutlinedButton.icon(
                  onPressed: openStars,
                  icon: const Icon(Icons.star_border_rounded),
                  label: const UiText('منح نجمة'),
                ),
              ],
            ),
          );
        },
      ),
    ],
  );
}
