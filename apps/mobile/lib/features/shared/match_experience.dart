import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'experience_ui.dart';

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
          branch = '';
          team = '';
        }),
      ),
      if (team.isNotEmpty) ...[
        ExperienceSection(
          textOf(teams[team]?['arabicName']),
          Icons.shield_outlined,
          color: ExperienceColors.green,
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
                        style: const TextStyle(
                          fontSize: 24,
                          fontWeight: FontWeight.w900,
                          color: ExperienceColors.green,
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
      const ExperienceSection(
        'المباريات القادمة',
        Icons.event_available_rounded,
        color: ExperienceColors.green,
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
  Widget build(BuildContext context) => GCard(
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        UiText(
          textOf(data['tournament']),
          style: const TextStyle(fontSize: 10, color: ExperienceColors.blue),
        ),
        const SizedBox(height: 10),
        Row(
          children: [
            Expanded(
              child: UiText(
                textOf(data['team']),
                style: const TextStyle(
                  fontSize: 14,
                  fontWeight: FontWeight.w800,
                ),
              ),
            ),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 7),
              decoration: BoxDecoration(
                color: ExperienceColors.green.withValues(alpha: .1),
                borderRadius: BorderRadius.circular(10),
              ),
              child: UiText(
                data['status'] == 'Completed'
                    ? '${data['ourScore']} — ${data['opponentScore']}'
                    : '×',
                style: const TextStyle(
                  fontSize: 22,
                  fontWeight: FontWeight.w900,
                  color: ExperienceColors.green,
                ),
              ),
            ),
            Expanded(
              child: UiText(
                textOf(data['opponent']),
                textAlign: TextAlign.end,
                style: const TextStyle(
                  fontSize: 14,
                  fontWeight: FontWeight.w800,
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: 12),
        UiText(
          '${day(data['matchDate'])} · ${textOf(data['startTime']).substring(0, 5)}',
          style: const TextStyle(fontSize: 11),
        ),
        UiText(
          '${data['venue']} · ${data['branch'] ?? ''}',
          style: const TextStyle(fontSize: 10),
        ),
        if (data['status'] == 'Cancelled')
          const UiText('ملغاة', style: TextStyle(color: ExperienceColors.red)),
        if (data['participation'] != null)
          UiText(
            data['participation'] == 'Bench'
                ? 'المشاركة: بديل'
                : 'المشاركة: لاعب',
          ),
        if (data['coachAssignmentSource'] == 'LegacyCurrentTeam')
          const UiText('إسناد قديم غير موثق؛ يلزم مراجعة الإدارة.'),
        if (data['coachAssignmentSource'] == 'Confirmed' &&
            data['coachName'] != null)
          UiText('مدرب المباراة: ${data['coachName']}'),
        if (data['notes'] != null) UiText(textOf(data['notes'])),
      ],
    ),
  );
}
