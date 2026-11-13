import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart' show AcademyCrest;
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'experience_ui.dart';
import 'operation_design.dart';

bool _cancelled(Json match) =>
    match['isActive'] == false || match['status'] == 'Cancelled';

bool _finalResult(Json match) =>
    !_cancelled(match) &&
    match['status'] != 'Draft' &&
    match['status'] != 'ResultDraft' &&
    (match['resultFinalized'] == true ||
        (match['resultFinalized'] == null && match['status'] == 'Completed')) &&
    match['ourScore'] is num &&
    match['opponentScore'] is num;

bool isUpcomingMatch(Json match) =>
    match['isUpcoming'] == true && !_cancelled(match) && !_finalResult(match);

class MatchExperience extends StatefulWidget {
  const MatchExperience({super.key, required this.auth, required this.items});
  final AuthController auth;
  final List<Json> items;
  @override
  State<MatchExperience> createState() => _MatchExperienceState();
}

class _MatchExperienceState extends State<MatchExperience> {
  String branch = '', team = '', from = '', to = '';
  bool showPrevious = false;
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
    final completed = matches.where(_finalResult).toList();
    String sortKey(Json match) => "${match['matchDate']} ${match['startTime']}";
    final upcoming = matches.where(isUpcomingMatch).toList()
      ..sort((a, b) => sortKey(a).compareTo(sortKey(b)));
    final previous = matches.where((match) => !isUpcomingMatch(match)).toList()
      ..sort((a, b) => sortKey(b).compareTo(sortKey(a)));
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
      if (upcoming.isNotEmpty && previous.isNotEmpty)
        SegmentedButton<bool>(
          segments: [
            ButtonSegment(
              value: false,
              label: UiText('${ui(context, 'القادمة')} · ${upcoming.length}'),
            ),
            ButtonSegment(
              value: true,
              label: UiText('${ui(context, 'السجل')} · ${previous.length}'),
            ),
          ],
          showSelectedIcon: false,
          selected: {showPrevious},
          onSelectionChanged: (value) =>
              setState(() => showPrevious = value.single),
        ),
      if (upcoming.isNotEmpty && previous.isEmpty)
        const Heading('المباريات القادمة'),
      if (previous.isNotEmpty && upcoming.isEmpty)
        const Heading('سجل المباريات'),
      const SizedBox(height: 8),
      if (upcoming.isEmpty && previous.isEmpty)
        const EmptyMessage('لا توجد مباريات في النطاق المختار.'),
      for (final match
          in upcoming.isNotEmpty && (!showPrevious || previous.isEmpty)
              ? upcoming
              : previous)
        MatchResultCard(data: match),
    ]);
  }
}

class MatchResultCard extends StatelessWidget {
  const MatchResultCard({super.key, required this.data, this.onEdit});
  final Json data;
  final VoidCallback? onEdit;

  @override
  Widget build(BuildContext context) {
    final date = DateTime.tryParse(day(data['matchDate']));
    final our = data['ourScore'] is num ? data['ourScore'] as num : null;
    final opponent = data['opponentScore'] is num
        ? data['opponentScore'] as num
        : null;
    final completed = _finalResult(data);
    final cancelled = _cancelled(data);
    final state = switch (data['status']) {
      _ when cancelled => 'ملغاة',
      _ when completed && our! > opponent! => 'فوز',
      _ when completed && our! < opponent! => 'خسارة',
      _ when completed => 'تعادل',
      'Draft' => 'مسودة',
      'ResultDraft' || 'Completed' => 'النتيجة قيد المراجعة',
      _ when our != null && opponent != null => 'النتيجة قيد المراجعة',
      _ => data['isUpcoming'] == false ? 'بانتظار النتيجة' : 'مجدولة',
    };
    final colors = Theme.of(context).colorScheme;
    final accent = switch (state) {
      'فوز' => ExperienceColors.positive(context),
      'خسارة' || 'ملغاة' => ExperienceColors.negative(context),
      'تعادل' => ExperienceColors.warning(context),
      _ => colors.onSurfaceVariant,
    };
    final kickoff = trainingTimeOf(data['startTime']);
    final dateLabel = date == null
        ? ''
        : MaterialLocalizations.of(context).formatMediumDate(date);
    final place = [data['venue'], data['branch']]
        .whereType<String>()
        .map((value) => value.trim())
        .where((value) => value.isNotEmpty)
        .toSet()
        .join(' · ');
    final note = data['notes']?.toString().trim() ?? '';
    final hasScore =
        !cancelled &&
        (completed || (onEdit != null && our != null && opponent != null));
    final scoreColor = completed ? accent : colors.onSurface;
    final academyTeam = _MatchTeam(
      name: textOf(data['team']),
      score: hasScore ? our : null,
      scoreColor: scoreColor,
      ours: true,
    );
    final opposingTeam = _MatchTeam(
      name: textOf(data['opponent']),
      score: hasScore ? opponent : null,
      scoreColor: scoreColor,
    );
    return Theme(
      data: operationTheme(context),
      child: GCard(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Wrap(
              alignment: WrapAlignment.spaceBetween,
              crossAxisAlignment: WrapCrossAlignment.center,
              spacing: 10,
              runSpacing: 6,
              children: [
                UiText(
                  textOf(data['tournament']),
                  style: const TextStyle(fontWeight: FontWeight.w700),
                ),
                Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 10,
                    vertical: 5,
                  ),
                  decoration: BoxDecoration(
                    color: accent.withValues(alpha: .10),
                    borderRadius: BorderRadius.circular(12),
                  ),
                  child: UiText(
                    state,
                    style: TextStyle(
                      fontWeight: FontWeight.w700,
                      color: accent,
                    ),
                  ),
                ),
              ],
            ),
            const Divider(height: 22),
            LayoutBuilder(
              builder: (context, constraints) {
                final pairedWidth =
                    MediaQuery.textScalerOf(context).scale(240) + 12;
                return constraints.maxWidth < pairedWidth
                    ? Column(
                        children: [
                          academyTeam,
                          const SizedBox(height: 12),
                          opposingTeam,
                        ],
                      )
                    : Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Expanded(child: academyTeam),
                          const SizedBox(width: 12),
                          Expanded(child: opposingTeam),
                        ],
                      );
              },
            ),
            const SizedBox(height: 12),
            Wrap(
              alignment: WrapAlignment.center,
              spacing: 10,
              runSpacing: 4,
              children: [
                if (dateLabel.isNotEmpty)
                  UiText(
                    dateLabel,
                    style: Theme.of(context).textTheme.labelMedium,
                  ),
                if (!hasScore && !cancelled && kickoff != 'غير محدد')
                  UiText(
                    kickoff,
                    style: Theme.of(context).textTheme.labelMedium,
                  ),
              ],
            ),
            if (place.isNotEmpty)
              UiText(
                place,
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.labelMedium,
              ),
            if (data['participation'] != null)
              UiText(
                data['participation'] == 'Bench'
                    ? 'المشاركة: بديل'
                    : 'المشاركة: لاعب',
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.labelMedium,
              ),
            if (data['coachAssignmentSource'] == 'Confirmed' &&
                data['coachName'] != null)
              UiText(
                'مدرب المباراة: ${data['coachName']}',
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.labelMedium,
              )
            else if (data['coachAssignmentSource'] == 'LegacyCurrentTeam' &&
                onEdit != null)
              const UiText('إسناد قديم غير موثق؛ يلزم مراجعة الإدارة.'),
            if (note.isNotEmpty)
              if (isUpcomingMatch(data))
                Padding(
                  padding: const EdgeInsets.only(top: 8),
                  child: Text(note),
                )
              else
                ExpansionTile(
                  tilePadding: EdgeInsets.zero,
                  title: const UiText('ملاحظات المباراة'),
                  children: [
                    Align(
                      alignment: AlignmentDirectional.centerStart,
                      child: Text(note),
                    ),
                  ],
                ),
            if (onEdit != null) ...[
              const SizedBox(height: 8),
              OutlinedButton.icon(
                key: ValueKey('match-edit-${data['id']}'),
                onPressed: onEdit,
                icon: const Icon(Icons.edit_outlined, size: 16),
                label: const UiText('تفاصيل المباراة والنتيجة'),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _MatchTeam extends StatelessWidget {
  const _MatchTeam({
    required this.name,
    required this.scoreColor,
    this.score,
    this.ours = false,
  });
  final String name;
  final Color scoreColor;
  final num? score;
  final bool ours;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final content = Column(
      children: [
        if (score != null)
          UiText(
            '$score',
            style: TextStyle(
              fontSize: 28,
              fontWeight: FontWeight.w900,
              color: scoreColor,
            ),
          )
        else if (ours)
          const AcademyCrest(size: 36)
        else
          SizedBox(
            width: 36,
            height: 36,
            child: Icon(Icons.shield_outlined, color: colors.onSurfaceVariant),
          ),
        const SizedBox(height: 4),
        Tooltip(
          message: name,
          excludeFromSemantics: true,
          child: UiText(
            name,
            semanticsLabel: name,
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            textAlign: TextAlign.center,
            style: const TextStyle(fontWeight: FontWeight.w700),
          ),
        ),
      ],
    );
    return score == null
        ? content
        : Semantics(
            container: true,
            excludeSemantics: true,
            label: '$name: $score',
            child: content,
          );
  }
}
