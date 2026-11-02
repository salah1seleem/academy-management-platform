import 'package:flutter/material.dart';

import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

int weekdayIndex(Object? value) {
  const names = [
    'Sunday',
    'Monday',
    'Tuesday',
    'Wednesday',
    'Thursday',
    'Friday',
    'Saturday',
  ];
  return value is num ? value.toInt() : names.indexOf('$value');
}

String arabicWeekday(Object? value) =>
    const [
      'الأحد',
      'الاثنين',
      'الثلاثاء',
      'الأربعاء',
      'الخميس',
      'الجمعة',
      'السبت',
    ].elementAtOrNull(weekdayIndex(value)) ??
    'غير محدد';

/// Recurring rules only. Actual attendance stays attached to dated sessions.
class WeeklyGroupSchedule extends StatelessWidget {
  const WeeklyGroupSchedule({super.key, required this.schedules});
  final List<Json> schedules;
  @override
  Widget build(BuildContext context) {
    final groups = <String, List<Json>>{};
    for (final rule in schedules) {
      final key =
          '${rule['startTime']}|${rule['endTime']}|${rule['validFrom']}|${rule['validTo']}';
      groups.putIfAbsent(key, () => []).add(rule);
    }
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const UiText(
            'مواعيد المجموعة',
            style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700),
          ),
          if (groups.isEmpty)
            const Padding(
              padding: EdgeInsets.only(top: 10),
              child: UiText('لم تُحدد مواعيد أسبوعية بعد.'),
            ),
          for (final rules in groups.values) ...[
            const SizedBox(height: 10),
            Wrap(
              spacing: 6,
              runSpacing: 6,
              children: [
                for (final d
                    in (rules
                        .map((r) => weekdayIndex(r['dayOfWeek']))
                        .where((d) => d >= 0)
                        .toSet()
                        .toList()
                      ..sort((a, b) => ((a + 1) % 7).compareTo((b + 1) % 7))))
                  Chip(
                    label: UiText(arabicWeekday(d)),
                    visualDensity: VisualDensity.compact,
                  ),
              ],
            ),
            const SizedBox(height: 6),
            UiText(
              'من ${trainingTimeOf(rules.first['startTime'])} إلى ${trainingTimeOf(rules.first['endTime'])}',
              style: const TextStyle(fontWeight: FontWeight.w700),
            ),
          ],
        ],
      ),
    );
  }
}
