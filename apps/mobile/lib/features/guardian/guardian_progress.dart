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
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    return Theme(
      data: operationTheme(context),
      child: gList([
        FamilyChildFilter(
          children: widget.children,
          selected: selected,
          onSelected: (id) => setState(() => selected = id),
        ),
        const SizedBox(height: 16),
        for (final child in widget.children.where(
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
                for (final sport in rows(child['sports'] ?? [])) ...[
                  const SizedBox(height: 16),
                  UiText(
                    '${sport['sport']} · ${sport['group'] ?? ''}',
                    style: TextStyle(
                      color: colors.onSurfaceVariant,
                      fontSize: 12,
                    ),
                  ),
                  if (sport['performance'] is Map) ...[
                    const SizedBox(height: 10),
                    Row(
                      children: [
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              const UiText(
                                'متوسط الأداء',
                                style: TextStyle(fontWeight: FontWeight.w700),
                              ),
                              UiText(
                                day(sport['performance']['evaluationDate']),
                                style: TextStyle(
                                  fontSize: 11,
                                  color: colors.onSurfaceVariant,
                                ),
                              ),
                            ],
                          ),
                        ),
                        UiText(
                          sport['performance']['meanPerformance'] is num
                              ? performanceScoreText(
                                  sport['performance']['meanPerformance']
                                      as num,
                                )
                              : '—',
                          style: TextStyle(
                            fontSize: 36,
                            fontWeight: FontWeight.w900,
                            color: colors.primary,
                          ),
                        ),
                        const SizedBox(width: 4),
                        const UiText('/100', style: TextStyle(fontSize: 11)),
                      ],
                    ),
                    const SizedBox(height: 8),
                    Wrap(
                      spacing: 8,
                      runSpacing: 8,
                      children: [
                        for (final criterion in rows(
                          sport['performance']['criteria'] ?? [],
                        ).take(6))
                          Chip(
                            label: UiText(
                              '${criterion['name']} · ${criterion['value']}',
                              style: const TextStyle(fontSize: 11),
                            ),
                          ),
                      ],
                    ),
                    const SizedBox(height: 12),
                    FilledButton.icon(
                      onPressed: () => openPage(
                        context,
                        PlayerReport(
                          auth: widget.auth,
                          id: textOf(sport['performance']['evaluationId']),
                        ),
                      ),
                      icon: const Icon(Icons.insights_rounded),
                      label: const UiText('التقرير الكامل'),
                    ),
                  ] else
                    const Padding(
                      padding: EdgeInsets.symmetric(vertical: 12),
                      child: UiText('لا يوجد تقييم منشور لهذه الرياضة بعد.'),
                    ),
                ],
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
        if (widget.children.isEmpty) const EmptyMessage(),
      ]),
    );
  }
}
