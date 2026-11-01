import 'package:academy_mobile/core/theme/ui_text.dart';

import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/app_theme.dart';
import '../shared/experience_ui.dart';
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
                r['performanceMean'] is Map &&
                    obj(r['performanceMean'])['value'] is num
                ? 'متوسط الأداء ${performanceScoreText(obj(r['performanceMean'])['value'] as num)} · ${r['evaluator']}'
                : 'تقييم منشور · ${r['evaluator']}',
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
                          style: TextStyle(
                            color: ExperienceColors.positive(context),
                          ),
                        ),
                      UiText(
                        d['performanceMean'] is Map &&
                                obj(d['performanceMean'])['value'] is num
                            ? performanceScoreText(
                                obj(d['performanceMean'])['value'] as num,
                              )
                            : '—',
                        style: const TextStyle(
                          fontSize: 38,
                          fontWeight: FontWeight.bold,
                        ),
                      ),
                      UiText(
                        'متوسط الأداء',
                        style: TextStyle(
                          color: Theme.of(context).colorScheme.onSurfaceVariant,
                        ),
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
          if (d['performanceMean'] is Map)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 8),
              child: UiText(
                '${obj(d['performanceMean'])['scoredCriteria']} من ${obj(d['performanceMean'])['applicableCriteria']} معايير منشورة · غير المقيم لا يدخل بصفر',
                style: const TextStyle(fontSize: 11),
              ),
            ),
          if (d['overallScore'] is num)
            UiText(
              'التقييم المركب · ${performanceScoreText(d['overallScore'] as num)}',
              style: const TextStyle(fontSize: 12),
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
            style: TextStyle(
              fontSize: 12,
              color: Theme.of(context).colorScheme.onSurfaceVariant,
            ),
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
                          color: scoreColor(c['score'], context: context),
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
            style: TextStyle(
              color: Theme.of(context).colorScheme.onSurfaceVariant,
            ),
          ),
        ]);
      },
    ),
  );
}

Color scoreColor(Object? score, {BuildContext? context}) => context != null
    ? score == null
          ? Theme.of(context).colorScheme.onSurfaceVariant
          : (score as num) >= 90
          ? ExperienceColors.positive(context)
          : score >= 60
          ? ExperienceColors.warning(context)
          : ExperienceColors.negative(context)
    : score == null
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

String radarAxisName(Object? value) {
  final raw = textOf(value);
  return RegExp('[ء-ي]').hasMatch(raw) ? raw : label(raw);
}

class RadarChart extends StatelessWidget {
  final List<Json> axes;
  final bool compact;
  final Color? accentColor;
  const RadarChart(
    this.axes, {
    super.key,
    this.compact = false,
    this.accentColor,
  });
  @override
  Widget build(BuildContext context) {
    final assessed = axes.where((a) => validAssessment(a['value'])).toList();
    if (assessed.isEmpty) return const SizedBox.shrink();
    if (assessed.length < 3) {
      return Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          for (final a in assessed)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 6),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  UiText(
                    '${radarAxisName(a['axis'])} · ${a['value']}',
                    style: const TextStyle(fontSize: 12),
                  ),
                  LinearProgressIndicator(
                    value: (a['value'] as num).toDouble() / 100,
                  ),
                ],
              ),
            ),
        ],
      );
    }
    final colors = Theme.of(context).colorScheme;
    return Semantics(
      label: assessed
          .map((a) => '${radarAxisName(a['axis'])}: ${a['value']}')
          .join('، '),
      child: SizedBox(
        height: compact ? 140 : 210,
        width: double.infinity,
        child: CustomPaint(
          painter: _RadarPainter(
            assessed,
            compact,
            colors.onSurface,
            accentColor ?? colors.primary,
          ),
        ),
      ),
    );
  }
}

class _RadarPainter extends CustomPainter {
  final List<Json> axes;
  final bool compact;
  final Color textColor, accent;
  _RadarPainter(this.axes, this.compact, this.textColor, this.accent);
  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width / 2, size.height / 2),
        radius = math.max(
          1.0,
          math.min(size.width / 2 - (compact ? 28 : 52), size.height / 2 - 28),
        );
    Offset point(int i, double scale) =>
        center +
        Offset(
              math.cos(-math.pi / 2 + i * math.pi * 2 / axes.length),
              math.sin(-math.pi / 2 + i * math.pi * 2 / axes.length),
            ) *
            radius *
            scale;
    final grid = Paint()
      ..color = textColor.withValues(alpha: .18)
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1;
    for (final level in [.25, .5, .75, 1.0]) {
      canvas.drawPath(
        Path()..addPolygon(
          List.generate(axes.length, (i) => point(i, level)),
          true,
        ),
        grid,
      );
    }
    final values = List.generate(
      axes.length,
      (i) => point(i, (axes[i]['value'] as num).toDouble() / 100),
    );
    canvas.drawPath(
      Path()..addPolygon(values, true),
      Paint()..color = accent.withValues(alpha: .22),
    );
    canvas.drawPath(
      Path()..addPolygon(values, true),
      Paint()
        ..color = accent
        ..style = PaintingStyle.stroke
        ..strokeWidth = 2,
    );
    for (var i = 0; i < axes.length; i++) {
      canvas.drawCircle(values[i], 3, Paint()..color = accent);
      final name = radarAxisName(axes[i]['axis']);
      final text = TextPainter(
        text: TextSpan(
          text: '$name\n${axes[i]['value']}',
          style: TextStyle(
            color: textColor,
            fontFamily: 'AcademyArabic',
            fontSize: compact ? 8 : 10,
          ),
        ),
        textAlign: TextAlign.center,
        textDirection: TextDirection.rtl,
      )..layout(maxWidth: compact ? 48 : 80);
      text.paint(
        canvas,
        point(i, 1.24) - Offset(text.width / 2, text.height / 2),
      );
    }
  }

  @override
  bool shouldRepaint(covariant _RadarPainter old) =>
      old.axes != axes ||
      old.textColor != textColor ||
      old.accent != accent ||
      old.compact != compact;
}
