import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'experience_ui.dart';
import 'operation_design.dart';

class MatchExperience extends StatefulWidget {
  const MatchExperience({super.key, required this.auth, required this.items});
  final AuthController auth;
  final List<Json> items;
  @override
  State<MatchExperience> createState() => _MatchExperienceState();
}

class _MatchExperienceState extends State<MatchExperience> {
  String branch = '', team = '', from = '', to = '';
  @override
  Widget build(BuildContext context) {
    final owner = widget.auth.session?.selected?.role == 'AcademyOwner';
    final guardian = widget.auth.session?.selected?.role == 'Guardian';
    final branches = <String, Json>{};
    for (final m in widget.items) {
      if (m['branchId'] != null) {
        branches[textOf(m['branchId'])] = {
          'id': m['branchId'],
          'arabicName': m['branch'],
        };
      }
    }
    final scoped = widget.items
        .where((m) => branch.isEmpty || m['branchId'] == branch)
        .toList();
    final teams = <String, Json>{};
    for (final m in scoped) {
      teams[textOf(m['teamId'] ?? m['team'])] = {
        'id': m['teamId'] ?? m['team'],
        'arabicName': m['team'],
      };
    }
    final matches = scoped
        .where(
          (m) =>
              (team.isEmpty || (m['teamId'] ?? m['team']) == team) &&
              (from.isEmpty || day(m['matchDate']).compareTo(from) >= 0) &&
              (to.isEmpty || day(m['matchDate']).compareTo(to) <= 0),
        )
        .toList();
    final completed = matches
        .where(
          (m) =>
              m['status'] == 'Completed' &&
              m['ourScore'] is num &&
              m['opponentScore'] is num,
        )
        .toList();
    final upcoming =
        matches
            .where((m) => m['isUpcoming'] == true && m['status'] != 'Cancelled')
            .toList()
          ..sort(
            (a, b) => '${a['matchDate']} ${a['startTime']}'.compareTo(
              '${b['matchDate']} ${b['startTime']}',
            ),
          );
    final previous = matches.where((m) => m['isUpcoming'] != true).toList()
      ..sort(
        (a, b) => textOf(b['matchDate']).compareTo(textOf(a['matchDate'])),
      );
    int sum(String key) =>
        completed.fold(0, (v, m) => v + (m[key] as num).toInt());
    if (widget.items.isEmpty) {
      return gList([const EmptyMessage('لا توجد مباريات منشورة تخصك حاليًا.')]);
    }
    return gList([
      if (owner)
        ScopeBranchTabs(
          items: branches.values.toList(),
          selected: branch,
          onSelected: (v) => setState(() {
            branch = v;
            team = '';
          }),
        ),
      if (guardian)
        ExpansionTile(
          title: const UiText('تصفية المباريات'),
          leading: const Icon(Icons.tune_rounded),
          children: [
            ScopeBranchTabs(
              items: teams.values.toList(),
              selected: team,
              allLabel: 'كل الفرق',
              onSelected: (v) => setState(() => team = v),
            ),
            ScopeDateTiles(
              from: from,
              to: to,
              onChanged: (a, b) => setState(() {
                from = a;
                to = b;
              }),
              onClear: () => setState(() {
                from = '';
                to = '';
                team = '';
              }),
            ),
          ],
        ),
      if (!guardian)
        ScopeBranchTabs(
          items: teams.values.toList(),
          selected: team,
          allLabel: 'كل الفرق',
          onSelected: (v) => setState(() => team = v),
        ),
      if (!guardian)
        ScopeDateTiles(
          from: from,
          to: to,
          onChanged: (a, b) => setState(() {
            from = a;
            to = b;
          }),
          onClear: () => setState(() {
            from = '';
            to = '';
            branch = '';
            team = '';
          }),
        ),
      if (team.isNotEmpty) ...[
        ExperienceSection(
          textOf(teams[team]?['arabicName']),
          Icons.shield_outlined,
          color: ExperienceColors.positive(context),
        ),
        GCard(
          child: Wrap(
            spacing: 18,
            runSpacing: 12,
            children: [
              for (final s in [
                ('مباريات مكتملة', completed.length),
                (
                  'فوز',
                  completed
                      .where(
                        (m) =>
                            (m['ourScore'] as num) >
                            (m['opponentScore'] as num),
                      )
                      .length,
                ),
                (
                  'تعادل',
                  completed
                      .where((m) => m['ourScore'] == m['opponentScore'])
                      .length,
                ),
                (
                  'خسارة',
                  completed
                      .where(
                        (m) =>
                            (m['ourScore'] as num) <
                            (m['opponentScore'] as num),
                      )
                      .length,
                ),
                ('أهداف لنا', sum('ourScore')),
                ('أهداف علينا', sum('opponentScore')),
              ])
                SizedBox(
                  width: 82,
                  child: Column(
                    children: [
                      UiText(
                        '${s.$2}',
                        style: TextStyle(
                          fontSize: 24,
                          fontWeight: FontWeight.w900,
                          color: ExperienceColors.positive(context),
                        ),
                      ),
                      UiText(s.$1, style: const TextStyle(fontSize: 10)),
                    ],
                  ),
                ),
            ],
          ),
        ),
      ],
      ExperienceSection(
        'المباريات القادمة',
        Icons.event_available_rounded,
        color: ExperienceColors.positive(context),
      ),
      if (upcoming.isEmpty)
        const EmptyMessage('لا توجد مباريات قادمة في النطاق المختار.'),
      for (final m in upcoming) MatchResultCard(data: m),
      const ExperienceSection(
        'النتائج والمباريات السابقة',
        Icons.scoreboard_outlined,
      ),
      if (previous.isEmpty)
        const EmptyMessage('لا توجد نتائج سابقة في النطاق المختار.'),
      for (final m in previous) MatchResultCard(data: m),
    ]);
  }
}

class MatchResultCard extends StatelessWidget {
  const MatchResultCard({super.key, required this.data});
  final Json data;
  @override
  Widget build(BuildContext context) {
    final date = DateTime.tryParse(day(data['matchDate']));
    final our = data['ourScore'], opponent = data['opponentScore'];
    final completed =
        data['status'] == 'Completed' && our is num && opponent is num;
    final cancelled = data['status'] == 'Cancelled';
    final state = cancelled
        ? 'ملغاة'
        : completed
        ? (our > opponent
              ? 'فوز'
              : our < opponent
              ? 'خسارة'
              : 'تعادل')
        : data['status'] == 'ResultDraft'
        ? 'النتيجة قيد المراجعة'
        : 'قادمة';
    final icon = cancelled
        ? Icons.cancel_outlined
        : completed
        ? (our > opponent
              ? Icons.check_circle_outline
              : our < opponent
              ? Icons.arrow_downward
              : Icons.remove)
        : Icons.schedule;
    final colors = Theme.of(context).colorScheme;
    final accent = cancelled || completed && our < opponent
        ? colors.error
        : completed && our > opponent
        ? ExperienceColors.positive(context)
        : completed
        ? ExperienceColors.warning(context)
        : colors.onSurfaceVariant;
    final time = textOf(data['startTime']);
    const months = [
      'يناير',
      'فبراير',
      'مارس',
      'أبريل',
      'مايو',
      'يونيو',
      'يوليو',
      'أغسطس',
      'سبتمبر',
      'أكتوبر',
      'نوفمبر',
      'ديسمبر',
    ];
    Widget team(String name, {bool ours = false}) => Expanded(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Row(
            children: [
              if (ours)
                const AcademyCrest(size: 40)
              else
                Container(
                  width: 38,
                  height: 38,
                  alignment: Alignment.center,
                  decoration: BoxDecoration(
                    border: Border.all(color: colors.outlineVariant),
                  ),
                  child: UiText(
                    name.isEmpty ? 'ف' : name.characters.take(2).toString(),
                    style: const TextStyle(fontSize: 9),
                  ),
                ),
              const SizedBox(width: 6),
              Expanded(
                child: Tooltip(
                  message: name,
                  child: UiText(
                    name,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    semanticsLabel: name,
                    style: const TextStyle(
                      fontSize: 14,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          FittedBox(
            fit: BoxFit.scaleDown,
            child: UiText(
              '${(ours ? our : opponent) ?? '—'}',
              textDirection: TextDirection.ltr,
              semanticsLabel:
                  '$name: ${(ours ? our : opponent) ?? 'لم تسجل نتيجة'}',
              style: TextStyle(
                fontSize: 28,
                fontWeight: FontWeight.w900,
                color: accent,
              ),
              maxLines: 1,
              softWrap: false,
            ),
          ),
        ],
      ),
    );
    return Theme(
      data: operationTheme(context),
      child: GCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Container(
                  width: 60,
                  padding: const EdgeInsets.symmetric(vertical: 5),
                  color: colors.primary.withValues(alpha: .12),
                  child: Column(
                    children: [
                      UiText(
                        date == null ? '—' : '${date.day}',
                        style: const TextStyle(
                          fontSize: 23,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      UiText(
                        date == null ? '' : months[date.month - 1],
                        style: const TextStyle(fontSize: 10),
                      ),
                      UiText(
                        time.length >= 5 ? time.substring(0, 5) : time,
                        textDirection: TextDirection.ltr,
                        style: const TextStyle(fontSize: 10),
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      UiText(
                        textOf(data['tournament']),
                        style: const TextStyle(
                          fontSize: 11,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                      UiText(
                        '${data['venue']} · ${data['branch'] ?? ''}',
                        style: const TextStyle(fontSize: 10),
                      ),
                      if (data['coachAssignmentSource'] == 'Confirmed' &&
                          data['coachName'] != null)
                        UiText(
                          'مدرب المباراة: ${data['coachName']}',
                          style: const TextStyle(fontSize: 10),
                        ),
                    ],
                  ),
                ),
                const SizedBox(width: 6),
                Container(
                  padding: const EdgeInsets.all(5),
                  decoration: BoxDecoration(
                    border: Border.all(color: accent.withValues(alpha: .5)),
                  ),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Icon(icon, color: accent, size: 12),
                      const SizedBox(width: 3),
                      UiText(
                        state,
                        style: TextStyle(fontSize: 10, color: accent),
                      ),
                    ],
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12),
            IntrinsicHeight(
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  team(textOf(data['team']), ours: true),
                  const SizedBox(width: 24, child: Center(child: UiText('×'))),
                  team(textOf(data['opponent'])),
                ],
              ),
            ),
            if (data['participation'] != null)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: UiText(
                  data['participation'] == 'Bench'
                      ? 'المشاركة: بديل'
                      : 'المشاركة: لاعب',
                  style: const TextStyle(fontSize: 11),
                ),
              ),
            if (data['coachAssignmentSource'] == 'LegacyCurrentTeam')
              const UiText(
                'إسناد قديم غير موثق؛ يلزم مراجعة الإدارة.',
                style: TextStyle(fontSize: 10),
              ),
            if (data['notes'] != null)
              UiText(
                textOf(data['notes']),
                style: const TextStyle(fontSize: 11),
              ),
          ],
        ),
      ),
    );
  }
}
