import 'package:academy_mobile/core/theme/ui_text.dart';

import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/app_theme.dart';
import 'guardian_ui.dart';

class ReportHistory extends StatelessWidget {
  final AuthController auth;
  final String playerId;
  const ReportHistory({super.key, required this.auth, required this.playerId});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'تقارير اللاعب',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/children/$playerId/evaluations',
      builder: (context, value) => gList([
        const UiText('التقييمات المنشورة · اختر تاريخ التقرير'),
        if (rows(value).isEmpty)
          const EmptyMessage('لا توجد تقييمات منشورة بعد.'),
        for (final r in rows(value))
          GLink(
            '${r['sport']} · ${day(r['evaluationDate'])}',
            Icons.insights,
            () => openPage(context, PlayerReport(auth: auth, id: r['id'])),
            subtitle:
                'التقييم ${textOf(r['overallScore'])} · ${r['evaluator']}',
          ),
      ]),
    ),
  );
}

class PlayerReport extends StatelessWidget {
  final AuthController auth;
  final String id;
  const PlayerReport({super.key, required this.auth, required this.id});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'تقرير اللاعب',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/evaluations/$id/report',
      builder: (context, value) {
        final d = obj(value);
        return gList([
          Wrap(
            spacing: 10,
            runSpacing: 8,
            children: [
              Chip(label: UiText(textOf(d['reportingPeriod']))),
              Chip(label: UiText(day(d['evaluationDate']))),
            ],
          ),
          GCard(
            child: Row(
              children: [
                PlayerAvatar(
                  textOf(d['player']),
                  auth: auth,
                  reference: d['photoReference'],
                  size: 54,
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      UiText(
                        textOf(d['player']),
                        style: const TextStyle(
                          fontSize: 14,
                          fontWeight: FontWeight.bold,
                        ),
                      ),
                      if (d['isFootballReport'] == true)
                        UiText(
                          textOf(d['footballPosition']),
                          style: const TextStyle(color: Colors.greenAccent),
                        ),
                      UiText(
                        textOf(d['overallScore']),
                        style: const TextStyle(
                          fontSize: 38,
                          fontWeight: FontWeight.bold,
                        ),
                      ),
                      const UiText(
                        'التقييم العام',
                        style: TextStyle(color: Colors.white60),
                      ),
                    ],
                  ),
                ),
                if (d['isFootballReport'] == true)
                  SizedBox(
                    width: 122,
                    child: RadarChart(rows(d['axes']), compact: true),
                  ),
              ],
            ),
          ),
          if (d['isFootballReport'] == true) ...[
            Wrap(
              spacing: 12,
              runSpacing: 8,
              children: [
                for (final a in rows(
                  d['axes'],
                ).where((a) => validAssessment(a['value'])))
                  UiText('${label(a['axis'])}: ${textOf(a['value'])}'),
              ],
            ),
          ],
          const SizedBox(height: 18),
          Facts([
            ('العمر', textOf(d['age'])),
            ('الطول · سم', textOf(d['heightCm'])),
            ('الوزن · كجم', textOf(d['weightKg'])),
            ('القدم المفضلة', label(d['preferredFoot'])),
          ]),
          UiText(
            'اكتمال التقييم ${textOf(d['completenessPercentage'])}% · القيم غير المسجلة ليست صفرًا',
            style: const TextStyle(fontSize: 12, color: Colors.white60),
          ),
          const Heading('المعايير التفصيلية'),
          for (final c in rows(
            d['criteria'],
          ).where((c) => validAssessment(c['score'])))
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(child: UiText(textOf(c['name']))),
                      UiText(
                        textOf(c['score']),
                        style: TextStyle(
                          fontSize: 23,
                          fontWeight: FontWeight.bold,
                          color: scoreColor(c['score']),
                        ),
                      ),
                    ],
                  ),
                  if (c['notes'] != null) UiText(c['notes']),
                ],
              ),
            ),
          const Heading('ملاحظات المدرب'),
          UiText(d['generalNotes'] ?? 'لا توجد ملاحظات منشورة.'),
          UiText(
            'المدرب: ${d['evaluator']}',
            style: const TextStyle(color: Colors.white60),
          ),
        ]);
      },
    ),
  );
}

Color scoreColor(Object? score) => score == null
    ? Colors.white54
    : (score as num) >= 90
    ? Colors.greenAccent
    : score >= 70
    ? AcademyTheme.gold
    : score >= 60
    ? Colors.orangeAccent
    : Colors.redAccent;

bool validAssessment(dynamic value) =>
    value is num && value.isFinite && value >= 0 && value <= 100;

class RadarChart extends StatelessWidget {
  final List<Json> axes;
  final bool compact;
  const RadarChart(this.axes, {super.key, this.compact = false});
  @override
  Widget build(BuildContext context) => Semantics(
    label: 'رسم المحاور الستة؛ القيم موضحة نصيًا أسفله',
    child: SizedBox(
      height: compact ? 140 : 210,
      child: CustomPaint(
        painter: _RadarPainter(
          axes,
          compact,
          Theme.of(context).colorScheme.onSurface,
        ),
      ),
    ),
  );
}

class _RadarPainter extends CustomPainter {
  final List<Json> axes;
  final bool compact;
  final Color textColor;
  _RadarPainter(this.axes, this.compact, this.textColor);
  static const names = [
    'Passing',
    'Shooting',
    'Physical',
    'Defending',
    'Speed',
    'Dribbling',
  ];
  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width / 2, size.height / 2),
        radius = math.min(
          size.width / 2 - (compact ? 25 : 55),
          size.height / 2 - 28,
        );
    Offset point(int i, double scale) =>
        center +
        Offset(
              math.cos(-math.pi / 2 + i * math.pi / 3),
              math.sin(-math.pi / 2 + i * math.pi / 3),
            ) *
            radius *
            scale;
    final grid = Paint()
      ..color = Colors.white24
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1;
    canvas.drawCircle(center, radius, grid);
    final values = names
        .map(
          (n) =>
              axes.where((a) => a['axis'] == n).firstOrNull?['value'] as num?,
        )
        .toList();
    // Missing axes stay missing: no zero-filling or alternative scoring formula.
    if (values.every((x) => x != null)) {
      canvas.drawPath(
        Path()..addPolygon(
          List.generate(6, (i) => point(i, values[i]!.toDouble() / 100)),
          true,
        ),
        Paint()..color = Colors.cyan.withValues(alpha: .24),
      );
      canvas.drawPath(
        Path()..addPolygon(
          List.generate(6, (i) => point(i, values[i]!.toDouble() / 100)),
          true,
        ),
        Paint()
          ..color = Colors.cyan
          ..style = PaintingStyle.stroke
          ..strokeWidth = 2,
      );
    }
    for (var i = 0; i < 6; i++) {
      if (values[i] != null) {
        canvas.drawCircle(
          point(i, values[i]!.toDouble() / 100),
          3,
          Paint()..color = Colors.cyanAccent,
        );
      }
      final t = TextPainter(
        text: TextSpan(
          text: compact
              ? const ['PAS', 'SHT', 'PHY', 'DEF', 'SPD', 'DRI'][i]
              : label(names[i]),
          style: TextStyle(color: textColor, fontFamily: 'Cairo', fontSize: 11),
        ),
        textDirection: TextDirection.rtl,
      )..layout();
      canvas.save();
      canvas.translate(
        point(i, 1.19).dx - t.width / 2,
        point(i, 1.19).dy - t.height / 2,
      );
      t.paint(canvas, Offset.zero);
      canvas.restore();
    }
  }

  @override
  bool shouldRepaint(covariant _RadarPainter old) =>
      old.axes != axes || old.textColor != textColor || old.compact != compact;
}
