import 'package:academy_mobile/core/theme/ui_text.dart';

import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/app_theme.dart';
import '../../core/widgets/arabic_number_input.dart';
import '../shared/experience_ui.dart';
import 'guardian_ui.dart';

const _footballAxes = [
  'Passing',
  'Dribbling',
  'Speed',
  'Defending',
  'Physical',
  'Shooting',
];

String reportDateText(BuildContext context, Object? value) {
  final date = DateTime.tryParse(day(value));
  return date == null
      ? 'تاريخ غير محدد'
      : MaterialLocalizations.of(context).formatShortDate(date);
}

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
      builder: (context, value) {
        final reports = rows(value);
        if (reports.isEmpty) {
          return gList([const EmptyMessage('لا توجد تقييمات منشورة بعد.')]);
        }
        return gList([
          for (final report in reports) _historyLink(context, report),
        ]);
      },
    ),
  );

  Widget _historyLink(BuildContext context, Json report) {
    final stats = report['performanceMean'];
    final mean = stats is Map ? stats['value'] : null;
    final evaluator = report['evaluator']?.toString().trim() ?? '';
    final details = [
      if (validAssessment(mean)) 'متوسط الأداء ${performanceScoreText(mean)}',
      if (evaluator.isNotEmpty) evaluator,
    ];
    return GLink(
      '${report['sport']} · ${reportDateText(context, report['evaluationDate'])}',
      Icons.insights,
      () => openPage(context, PlayerReport(auth: auth, id: report['id'])),
      subtitle: details.isEmpty ? null : details.join(' · '),
    );
  }
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
        final report = obj(value);
        if (report['id'] is String &&
            textOf(report['id']).toLowerCase() != id.toLowerCase()) {
          return gList([const EmptyMessage('هذا التقرير غير متاح.')]);
        }
        final football = report['isFootballReport'] == true;
        final position = report['footballPosition']?.toString().trim() ?? '';
        final sport = report['sport']?.toString().trim() ?? '';
        final evaluator = report['evaluator']?.toString().trim() ?? '';
        final period = report['reportingPeriod']?.toString().trim() ?? '';
        final date = DateTime.tryParse(day(report['evaluationDate']));
        final showPeriod =
            period.isNotEmpty &&
            (date == null ||
                canonicalNumericInput(period) !=
                    canonicalNumericInput(
                      MaterialLocalizations.of(context).formatMonthYear(date),
                    ));
        final note = report['generalNotes']?.toString().trim() ?? '';
        final stats = report['performanceMean'];
        final mean = stats is Map ? stats['value'] : null;
        final scored = stats is Map
            ? stats['scoredCriteria'] ?? report['scoredCriteria']
            : report['scoredCriteria'];
        final applicable = stats is Map
            ? stats['applicableCriteria'] ?? report['totalApplicableCriteria']
            : report['totalApplicableCriteria'];
        final overall = report['overallScore'];
        final recordedAxes = {
          if (football)
            for (final axis in rows(report['axes'] ?? []))
              if (validAssessment(axis['value'])) axis['axis']: axis,
        };
        final axes = [
          for (final name in _footballAxes)
            if (recordedAxes[name] case final Json axis) axis,
        ];
        final criteria = rows(report['criteria'] ?? [])
            .where((item) => validAssessment(item['score']))
            .toList();
        final colors = Theme.of(context).colorScheme;

        return gList([
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    PlayerAvatar(
                      textOf(report['player']),
                      auth: auth,
                      reference: report['photoReference'],
                      size: 54,
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          UiText(
                            textOf(report['player']),
                            style: const TextStyle(
                              fontSize: 17,
                              fontWeight: FontWeight.w800,
                            ),
                          ),
                          if (sport.isNotEmpty) UiText(sport),
                          if (football && position.isNotEmpty)
                            UiText(
                              position,
                              style: TextStyle(
                                color: ExperienceColors.positive(context),
                              ),
                            ),
                        ],
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 10),
                UiText(
                  [
                    reportDateText(context, report['evaluationDate']),
                    if (evaluator.isNotEmpty) evaluator,
                  ].join(' · '),
                  style: TextStyle(color: colors.onSurfaceVariant),
                ),
                if (showPeriod) UiText(period),
                const Divider(height: 20),
                UiText(
                  'متوسط الأداء',
                  style: TextStyle(color: colors.onSurfaceVariant),
                ),
                Wrap(
                  crossAxisAlignment: WrapCrossAlignment.center,
                  spacing: 6,
                  children: [
                    UiText(
                      validAssessment(mean)
                          ? performanceScoreText(mean)
                          : 'غير متاح',
                      style: TextStyle(
                        fontSize: validAssessment(mean) ? 30 : 17,
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                    if (validAssessment(mean)) const UiText('من 100'),
                  ],
                ),
                if (validAssessment(overall))
                  UiText('التقييم المركب · ${performanceScoreText(overall)}'),
                if (scored is num && applicable is num && applicable > 0)
                  UiText(
                    'الدرجات المسجّلة $scored من $applicable',
                    style: TextStyle(color: colors.onSurfaceVariant),
                  ),
              ],
            ),
          ),
          if (axes.isNotEmpty)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Heading('محاور الأداء'),
                  if (axes.length == _footballAxes.length) RadarChart(axes),
                  Wrap(
                    spacing: 14,
                    runSpacing: 6,
                    children: [
                      for (final axis in axes)
                        UiText(
                          '${radarAxisName(axis['axis'])}: ${performanceScoreText(axis['value'])}',
                        ),
                    ],
                  ),
                ],
              ),
            ),
          Facts([
            ('العمر', textOf(report['age'])),
            ('الطول · سم', textOf(report['heightCm'])),
            ('الوزن · كجم', textOf(report['weightKg'])),
            if (football) ('القدم المفضلة', label(report['preferredFoot'])),
          ]),
          const Heading('الدرجات حسب المعيار'),
          if (criteria.isEmpty)
            const EmptyMessage('لا توجد درجات منشورة في هذا التقرير.'),
          for (final item in criteria)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(child: UiText(textOf(item['name']))),
                      UiText(
                        performanceScoreText(item['score']),
                        style: TextStyle(
                          fontSize: 23,
                          fontWeight: FontWeight.bold,
                          color: scoreColor(item['score'], context: context),
                        ),
                      ),
                    ],
                  ),
                  if (item['notes'] case final String text
                      when text.trim().isNotEmpty)
                    UiText(text),
                ],
              ),
            ),
          if (note.isNotEmpty) ...[
            const Heading('ملاحظات المدرب'),
            UiText(note),
          ],
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
