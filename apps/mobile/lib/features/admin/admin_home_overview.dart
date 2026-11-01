import 'package:flutter/material.dart';

import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/experience_ui.dart';

/// The existing session endpoint owns scope and attendance classification.
/// Ratios describe saved records in actual sessions, never workday absence.
class AdminAttendanceOverview extends StatelessWidget {
  const AdminAttendanceOverview({super.key, required this.sessions});
  final List<Json> sessions;
  @override
  Widget build(BuildContext context) {
    final actual = sessions.where((s) => s['status'] != 'Cancelled').toList();
    final withRecords = actual
        .where((s) => (s['recorded'] as num? ?? 0) > 0)
        .length;
    final playerRecorded = actual.fold<num>(
      0,
      (sum, s) => sum + (s['recorded'] as num? ?? 0),
    );
    final playerPresent = actual.fold<num>(
      0,
      (sum, s) => sum + (s['present'] as num? ?? 0),
    );
    final ratios = <(String, String, double?)>[
      (
        'players',
        'اللاعبون',
        playerRecorded > 0 ? playerPresent / playerRecorded : null,
      ),
      for (final role in [('Coach', 'المدربون'), ('AcademyAdmin', 'الإداريون')])
        (() {
          final recorded = actual
              .expand((s) => s['staff'] is List ? rows(s['staff']) : <Json>[])
              .where(
                (m) =>
                    m['role'] == role.$1 &&
                    [
                      'Present',
                      'Late',
                      'Absent',
                      'Excused',
                    ].contains(m['attendanceStatus']),
              )
              .toList();
          final present = recorded
              .where((m) => ['Present', 'Late'].contains(m['attendanceStatus']))
              .length;
          return (
            role.$1,
            role.$2,
            recorded.isEmpty ? null : present / recorded.length,
          );
        })(),
    ];
    final color = ExperienceColors.positive(context);
    final ring = SizedBox.square(
      dimension: 84 * (MediaQuery.textScalerOf(context).scale(19) / 19),
      child: Semantics(
        label:
            '$withRecords من ${actual.length} حصص بها سجلات حضور، وقد تكون جزئية',
        child: ExcludeSemantics(
          child: Stack(
            alignment: Alignment.center,
            children: [
              Positioned.fill(
                child: CircularProgressIndicator(
                  value: actual.isEmpty ? 0 : withRecords / actual.length,
                  strokeWidth: 7,
                  color: color,
                  backgroundColor: Theme.of(context).dividerColor,
                ),
              ),
              FittedBox(
                fit: BoxFit.scaleDown,
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(
                      '$withRecords من ${actual.length}',
                      key: const ValueKey('admin-recorded-session-count'),
                      style: const TextStyle(
                        fontSize: 19,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    const UiText(
                      'بها حضور مسجل',
                      style: TextStyle(fontSize: 9),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
    final details = Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const UiText(
          'حضور حصص اليوم',
          style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600),
        ),
        const SizedBox(height: 10),
        for (final ratio in ratios) ...[
          Row(
            children: [
              Expanded(
                child: UiText(
                  ratio.$2,
                  style: const TextStyle(
                    fontSize: 12,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ),
              Text(
                ratio.$3 == null ? '—' : '${(ratio.$3! * 100).round()}%',
                key: ValueKey('admin-attendance-${ratio.$1}'),
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
            value: (ratio.$3 ?? 0).clamp(0, 1),
            color: color,
          ),
          const SizedBox(height: 10),
        ],
      ],
    );
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          LayoutBuilder(
            builder: (context, bounds) =>
                MediaQuery.textScalerOf(context).scale(14) > 21 ||
                    bounds.maxWidth < 280
                ? Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Align(
                        alignment: AlignmentDirectional.centerStart,
                        child: ring,
                      ),
                      const SizedBox(height: 14),
                      details,
                    ],
                  )
                : Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      ring,
                      const SizedBox(width: 14),
                      Expanded(child: details),
                    ],
                  ),
          ),
          const SizedBox(height: 8),
          const UiText(
            'النسب من السجلات المحفوظة داخل الحصص، وليست حضور يوم العمل. الحصة ذات السجلات الجزئية لا تعني اكتمال التسجيل؛ غير المسجل لا يُحسب غيابًا.',
            style: TextStyle(fontSize: 11),
          ),
        ],
      ),
    );
  }
}

class AdminHomeTask extends StatelessWidget {
  const AdminHomeTask({
    super.key,
    required this.title,
    required this.detail,
    required this.count,
    required this.action,
    required this.onOpen,
  });
  final String title, detail, action;
  final int? count;
  final VoidCallback onOpen;
  @override
  Widget build(BuildContext context) => GCard(
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SizedBox(
              width: 50,
              child: Text(
                count?.toString() ?? '—',
                style: TextStyle(
                  fontSize: 26,
                  fontWeight: FontWeight.w600,
                  color: Theme.of(context).colorScheme.primary,
                ),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  UiText(
                    title,
                    style: const TextStyle(
                      fontSize: 15,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  const SizedBox(height: 4),
                  UiText(
                    detail,
                    style: TextStyle(
                      fontSize: 12,
                      color: Theme.of(context).colorScheme.onSurfaceVariant,
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
        const SizedBox(height: 10),
        FilledButton(
          onPressed: onOpen,
          style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(44)),
          child: Row(
            children: [
              Expanded(child: UiText(action)),
              const SizedBox(width: 10),
              const Icon(Icons.chevron_right, size: 18),
            ],
          ),
        ),
      ],
    ),
  );
}
