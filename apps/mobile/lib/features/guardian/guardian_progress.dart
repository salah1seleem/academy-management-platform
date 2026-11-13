import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../shared/operation_design.dart';
import 'family_child_filter.dart';
import 'guardian_report.dart';
import 'guardian_ui.dart';

class GuardianProgress extends StatefulWidget {
  const GuardianProgress({
    super.key,
    required this.auth,
    required this.children,
  });
  final AuthController auth;
  final List<Json> children;
  @override
  State<GuardianProgress> createState() => _GuardianProgressState();
}

class _GuardianProgressState extends State<GuardianProgress> {
  String selected = '';

  @override
  void didUpdateWidget(covariant GuardianProgress oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!widget.children.any((child) => child['id'] == selected)) selected = '';
  }

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final children = widget.children;
    return Theme(
      data: operationTheme(context),
      child: gList([
        if (children.length > 1) ...[
          FamilyChildFilter(
            children: children,
            selected: selected,
            onSelected: (id) => setState(() => selected = id),
            wrap: true,
          ),
          const SizedBox(height: 16),
        ],
        for (final child in children.where(
          (c) => selected.isEmpty || c['id'] == selected,
        ))
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Row(
                  children: [
                    PlayerAvatar(
                      textOf(child['arabicName']),
                      auth: widget.auth,
                      reference: child['photoReference'],
                      size: 54,
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: UiText(
                        textOf(child['arabicName']),
                        style: const TextStyle(
                          fontSize: 18,
                          fontWeight: FontWeight.w800,
                        ),
                      ),
                    ),
                  ],
                ),
                if (rows(child['sports'] ?? []).isEmpty)
                  const Padding(
                    padding: EdgeInsets.only(top: 12),
                    child: UiText('لا توجد تسجيلات رياضية لهذا اللاعب.'),
                  ),
                for (final sport in rows(child['sports'] ?? []))
                  _sport(context, sport, colors),
                TextButton.icon(
                  onPressed: () => openPage(
                    context,
                    ReportHistory(
                      auth: widget.auth,
                      playerId: textOf(child['id']),
                    ),
                  ),
                  icon: const Icon(Icons.history_rounded),
                  label: const UiText('سجل التقييمات'),
                ),
              ],
            ),
          ),
        if (children.isEmpty)
          const EmptyMessage('لا يوجد أبناء مرتبطون بهذا الحساب.'),
      ]),
    );
  }

  Widget _sport(BuildContext context, Json sport, ColorScheme colors) {
    final group = sport['group']?.toString().trim() ?? '';
    final performance = sport['performance'];
    final mean = performance is Map ? performance['meanPerformance'] : null;
    final id = performance is Map ? performance['evaluationId'] : null;
    final criteria = performance is Map
        ? rows(performance['criteria'] ?? [])
              .where((criterion) => validAssessment(criterion['value']))
              .take(6)
        : const <Json>[];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const SizedBox(height: 16),
        UiText(
          [textOf(sport['sport']), if (group.isNotEmpty) group].join(' · '),
          style: TextStyle(color: colors.onSurfaceVariant, fontSize: 12),
        ),
        if (performance is Map) ...[
          const SizedBox(height: 10),
          const UiText(
            'متوسط الأداء',
            style: TextStyle(fontWeight: FontWeight.w700),
          ),
          if (validAssessment(mean))
            Text.rich(
              TextSpan(
                children: [
                  TextSpan(
                    text: performanceScoreText(mean),
                    style: TextStyle(
                      fontSize: 28,
                      fontWeight: FontWeight.w800,
                      color: colors.primary,
                    ),
                  ),
                  TextSpan(
                    text: ' ${ui(context, 'من 100')}',
                    style: TextStyle(color: colors.onSurfaceVariant),
                  ),
                ],
              ),
              semanticsLabel:
                  '${performanceScoreText(mean)} ${ui(context, 'من 100')}',
            )
          else
            const UiText('غير متاح'),
          UiText(
            reportDateText(context, performance['evaluationDate']),
            style: TextStyle(color: colors.onSurfaceVariant, fontSize: 12),
          ),
          if (criteria.isNotEmpty) ...[
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              runSpacing: 4,
              children: [
                for (final criterion in criteria)
                  Chip(
                    label: UiText(
                      '${criterion['name']} · ${criterion['value']}',
                      style: const TextStyle(fontSize: 11),
                    ),
                  ),
              ],
            ),
          ],
          if (id is String && id.isNotEmpty) ...[
            const SizedBox(height: 12),
            SizedBox(
              width: double.infinity,
              child: FilledButton.icon(
                onPressed: () =>
                    openPage(context, PlayerReport(auth: widget.auth, id: id)),
                icon: const Icon(Icons.insights_rounded),
                label: const UiText('التقرير الكامل'),
              ),
            ),
          ],
        ] else
          const Padding(
            padding: EdgeInsets.symmetric(vertical: 12),
            child: UiText('لا يوجد تقييم منشور لهذه الرياضة بعد.'),
          ),
      ],
    );
  }
}
