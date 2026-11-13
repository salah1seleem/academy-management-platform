import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show DateFormat, NumberFormat;

import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

/// Plotting only. Values and local date windows come from the scoped server.
class HomeTrend extends StatelessWidget {
  const HomeTrend({
    super.key,
    required this.items,
    required this.monthly,
    this.currency,
    this.onOpen,
  });
  final List<Json> items;
  final bool monthly;
  final Object? currency;
  final ValueChanged<Json>? onOpen;
  @override
  Widget build(BuildContext context) {
    if (items.isEmpty) return const SizedBox.shrink();
    final key = monthly ? 'count' : 'amount';
    final values = items.map((v) => (v[key] as num).toDouble()).toList();
    final theme = Theme.of(context);
    final color = monthly
        ? theme.brightness == Brightness.dark
              ? const Color(0xFF22D3EE)
              : theme.colorScheme.onSurface
        : theme.colorScheme.primary;
    final first = monthly ? items.first['month'] : items.first['from'];
    final last = monthly ? items.last['asOf'] : items.last['to'];
    final local = MaterialLocalizations.of(context);
    final end = DateTime.tryParse(textOf(last));
    final weeklyWindow = switch (items.length) {
      1 => 'أسبوع واحد',
      2 => 'آخر أسبوعين',
      _ => 'آخر ${local.formatDecimal(items.length)} أسابيع',
    };
    String date(Object? value) {
      final parsed = DateTime.tryParse(textOf(value));
      return parsed == null ? 'تاريخ غير متاح' : local.formatShortDate(parsed);
    }

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          UiText(
            monthly ? 'نمو الاشتراكات' : 'التحصيل الأسبوعي',
            style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600),
          ),
          const SizedBox(height: 4),
          Wrap(
            spacing: 6,
            runSpacing: 2,
            children: [
              if (monthly)
                UiText(
                  '$first — $last',
                  key: ValueKey('home-trend-range-$monthly'),
                  textDirection: TextDirection.ltr,
                  style: TextStyle(
                    fontSize: 10,
                    color: theme.colorScheme.onSurfaceVariant,
                  ),
                ),
              if (!monthly && end != null)
                UiText(
                  '$weeklyWindow حتى ${local.formatShortDate(end)}',
                  style: TextStyle(
                    fontSize: 10,
                    color: theme.colorScheme.onSurfaceVariant,
                  ),
                ),
              UiText(
                monthly
                    ? 'تسجيلات لها اشتراك'
                    : currency == 'EGP'
                    ? '· ج.م'
                    : '· ${textOf(currency)}',
                style: TextStyle(
                  fontSize: 10,
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),
            ],
          ),
          const SizedBox(height: 10),
          LayoutBuilder(
            builder: (context, bounds) => Directionality(
              textDirection: TextDirection.ltr,
              child: SingleChildScrollView(
                scrollDirection: Axis.horizontal,
                reverse: true,
                child: SizedBox(
                  width: math.max(
                    bounds.maxWidth,
                    items.length * MediaQuery.textScalerOf(context).scale(52),
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      SizedBox(
                        height: monthly ? 52 : 64,
                        child: CustomPaint(
                          painter: _TrendPainter(
                            values,
                            color,
                            Theme.of(context).colorScheme.outlineVariant,
                            monthly,
                          ),
                        ),
                      ),
                      const SizedBox(height: 6),
                      Directionality(
                        textDirection: TextDirection.ltr,
                        child: Row(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            for (var i = 0; i < items.length; i++)
                              Expanded(
                                child: Tooltip(
                                  message: monthly
                                      ? '${items[i]['asOf']} · ${items[i][key]} اشتراكًا'
                                      : '${money(items[i][key], currency)} · من ${date(items[i]['from'])} إلى ${date(items[i]['to'])}',
                                  child: InkWell(
                                    onTap: onOpen == null
                                        ? null
                                        : () => onOpen!(items[i]),
                                    child: Padding(
                                      padding: const EdgeInsets.symmetric(
                                        vertical: 8,
                                      ),
                                      child: Column(
                                        children: [
                                          UiText(
                                            NumberFormat(
                                              '#,##0.##',
                                              'en',
                                            ).format(values[i]),
                                            textDirection: TextDirection.ltr,
                                            style: TextStyle(
                                              fontSize: 10,
                                              fontWeight: FontWeight.w600,
                                              color: values[i] < 0
                                                  ? Theme.of(context)
                                                        .colorScheme
                                                        .error
                                                  : null,
                                            ),
                                          ),
                                          UiText(
                                            monthly
                                                ? DateFormat(
                                                    'MMM',
                                                    'ar_EG',
                                                  ).format(
                                                    DateTime.parse(
                                                      textOf(items[i]['month']),
                                                    ),
                                                  )
                                                : DateFormat(
                                                    'd/M',
                                                    'en',
                                                  ).format(
                                                    DateTime.parse(
                                                      textOf(items[i]['to']),
                                                    ),
                                                  ),
                                            textDirection: monthly
                                                ? TextDirection.rtl
                                                : TextDirection.ltr,
                                            style: const TextStyle(fontSize: 9),
                                          ),
                                        ],
                                      ),
                                    ),
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
        ],
      ),
    );
  }
}

class _TrendPainter extends CustomPainter {
  _TrendPainter(this.values, this.color, this.grid, this.monthly);
  final List<double> values;
  final Color color, grid;
  final bool monthly;
  @override
  void paint(Canvas canvas, Size size) {
    final low = math.min(0.0, values.reduce(math.min)),
        high = math.max(0.0, values.reduce(math.max));
    final span = math.max(1.0, high - low);
    Offset point(int i, double v) => Offset(
      size.width * (i + .5) / values.length,
      4 + (high - v) / span * (size.height - 8),
    );
    final zero = point(0, 0).dy;
    canvas.drawLine(
      Offset(0, zero),
      Offset(size.width, zero),
      Paint()
        ..color = grid
        ..strokeWidth = 1,
    );
    final line = Path()..moveTo(point(0, values[0]).dx, point(0, values[0]).dy);
    for (var i = 1; i < values.length; i++) {
      final p = point(i, values[i]);
      line.lineTo(p.dx, p.dy);
    }
    final area = Path.from(line)
      ..lineTo(point(values.length - 1, 0).dx, zero)
      ..lineTo(point(0, 0).dx, zero)
      ..close();
    canvas.drawPath(
      area,
      Paint()..color = color.withValues(alpha: monthly ? .15 : .16),
    );
    canvas.drawPath(
      line,
      Paint()
        ..color = color
        ..style = PaintingStyle.stroke
        ..strokeWidth = 2.5,
    );
    if (!monthly) {
      final last = point(values.length - 1, values.last);
      canvas.drawCircle(last, 4.5, Paint()..color = color);
      canvas.drawCircle(
        last,
        4.5,
        Paint()
          ..color = grid
          ..style = PaintingStyle.stroke
          ..strokeWidth = 2,
      );
    }
  }

  @override
  bool shouldRepaint(covariant _TrendPainter old) =>
      old.values != values ||
      old.color != color ||
      old.grid != grid ||
      old.monthly != monthly;
}
