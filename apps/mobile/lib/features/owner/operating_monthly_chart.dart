import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show DateFormat;

import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/experience_ui.dart';

/// Geometry only: both signed cash series and date intersections are server values.
class OperatingMonthlyChart extends StatelessWidget {
  const OperatingMonthlyChart({
    super.key,
    required this.items,
    required this.currency,
    this.onOpen,
    this.showRange = true,
  });
  final bool showRange;
  final List<Json> items;
  final String currency;
  final ValueChanged<Json>? onOpen;
  @override
  Widget build(BuildContext context) {
    if (items.isEmpty) return const SizedBox.shrink();
    final income = ExperienceColors.positive(context),
        expenses = ExperienceColors.negative(context);
    return Semantics(
      container: true,
      label: 'مقارنة المقبوض والمدفوع شهريًا',
      child: Container(
        padding: const EdgeInsets.fromLTRB(14, 12, 14, 8),
        decoration: BoxDecoration(
          border: Border.all(
            color: Theme.of(context).colorScheme.outlineVariant,
          ),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Wrap(
              spacing: 14,
              runSpacing: 6,
              children: [
                for (final item in [
                  ('الإيرادات المحصلة', income),
                  ('المصروفات المدفوعة', expenses),
                ])
                  Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      ColoredBox(
                        color: item.$2,
                        child: const SizedBox.square(dimension: 10),
                      ),
                      const SizedBox(width: 6),
                      Flexible(
                        child: UiText(
                          item.$1,
                          style: const TextStyle(fontSize: 11),
                        ),
                      ),
                    ],
                  ),
              ],
            ),
            const SizedBox(height: 6),
            if (showRange)
              UiText(
                '${items.first['from']} — ${items.last['to']}',
                textDirection: TextDirection.ltr,
                style: TextStyle(
                  fontSize: 10,
                  color: Theme.of(context).colorScheme.onSurfaceVariant,
                ),
              ),
            SizedBox(
              height: 100,
              child: CustomPaint(
                painter: OperatingMonthlyPainter(
                  items,
                  income,
                  expenses,
                  Theme.of(context).colorScheme.outlineVariant,
                ),
              ),
            ),
            Row(
              textDirection: TextDirection.ltr,
              children: [
                for (final month in items)
                  Expanded(
                    child: Semantics(
                      button: onOpen != null,
                      label:
                          '${month['from']} — ${month['to']}، المقبوض ${money(month['income'], currency)}، المدفوع ${money(month['expenses'], currency)}',
                      child: Tooltip(
                        message:
                            '${month['from']} — ${month['to']}\nالمقبوض ${money(month['income'], currency)}\nالمدفوع ${money(month['expenses'], currency)}',
                        child: InkWell(
                          key: ValueKey('operating-month-${month['from']}'),
                          onTap: onOpen == null ? null : () => onOpen!(month),
                          child: Padding(
                            padding: const EdgeInsets.symmetric(vertical: 10),
                            child: UiText(
                              DateFormat.yMMM(
                                'ar',
                              ).format(DateTime.parse(month['from'] as String)),
                              textAlign: TextAlign.center,
                              style: const TextStyle(fontSize: 11),
                            ),
                          ),
                        ),
                      ),
                    ),
                  ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class OperatingMonthlyPainter extends CustomPainter {
  const OperatingMonthlyPainter(
    this.items,
    this.income,
    this.expenses,
    this.line,
  );
  final List<Json> items;
  final Color income, expenses, line;
  @override
  void paint(Canvas canvas, Size size) {
    final values = items
        .expand(
          (p) => [
            (p['income'] as num).toDouble(),
            (p['expenses'] as num).toDouble(),
          ],
        )
        .toList();
    final low = math.min(0.0, values.reduce(math.min)),
        high = math.max(0.0, values.reduce(math.max));
    final span = math.max(1.0, high - low);
    double y(double value) => 6 + (high - value) * (size.height - 14) / span;
    final zero = y(0);
    canvas.drawLine(
      Offset(0, zero),
      Offset(size.width, zero),
      Paint()..color = line,
    );
    final slot = size.width / items.length, width = math.min(34.0, slot * .22);
    for (var i = 0; i < items.length; i++) {
      for (final series in [
        ('income', income, -1),
        ('expenses', expenses, 1),
      ]) {
        final value = (items[i][series.$1] as num).toDouble();
        if (value == 0) continue;
        final center = slot * (i + .5) + series.$3 * (width + 4) / 2;
        final end = y(value);
        canvas.drawRect(
          Rect.fromLTRB(
            center - width / 2,
            math.min(end, zero),
            center + width / 2,
            math.max(end, zero),
          ),
          Paint()..color = series.$2,
        );
      }
    }
  }

  @override
  bool shouldRepaint(covariant OperatingMonthlyPainter old) =>
      old.items != items ||
      old.income != income ||
      old.expenses != expenses ||
      old.line != line;
}
