import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../coach/coach_attendance.dart';
import '../guardian/guardian_profile.dart';
import '../guardian/family_child_filter.dart';
import '../guardian/guardian_ui.dart';
import 'matches.dart';

// A read-only projection of existing authorized sources, not another calendar.
class RoleAgenda extends StatefulWidget {
  final AuthController auth;
  final bool guardian;
  const RoleAgenda({super.key, required this.auth, required this.guardian});
  @override
  State<RoleAgenda> createState() => _RoleAgendaState();
}

class _RoleAgendaState extends State<RoleAgenda> {
  String? identity() => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final openedIdentity = identity();
  List<Json> children = [], sessions = [], matches = [];
  String? playerId = '';
  String filter = 'all';
  bool loading = true,
      homeError = false,
      trainingError = false,
      matchError = false;
  int generation = 0;
  bool current(int attempt) =>
      mounted &&
      generation == attempt &&
      openedIdentity != null &&
      openedIdentity == identity();

  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load({String? child}) async {
    final attempt = ++generation;
    setState(() {
      loading = true;
      homeError = false;
      sessions = [];
      matches = [];
    });
    if (widget.guardian && child == null) {
      try {
        final data = obj(
          await widget.auth.request('GET', '/api/v1/guardian/home'),
        );
        if (!current(attempt)) return;
        children = rows(data['children']);
        if (playerId != '' && !children.any((c) => c['id'] == playerId)) {
          playerId = children.isEmpty ? null : children.first['id'] as String;
        }
      } catch (_) {
        if (current(attempt)) {
          setState(() {
            loading = false;
            homeError = true;
          });
        }
        return;
      }
    } else if (child != null) {
      playerId = child;
    }
    if (widget.guardian && playerId == null) {
      if (current(attempt)) {
        setState(() {
          loading = false;
          trainingError = false;
          matchError = false;
        });
      }
      return;
    }
    final selected = playerId;
    Future<(List<Json>, bool)> training() async {
      try {
        if (!widget.guardian) {
          return (
            rows(
              await widget.auth.request(
                'GET',
                '/api/v1/attendance/sessions?upcomingOnly=true',
              ),
            ),
            false,
          );
        }
        final unique = <String, Json>{};
        final eligible = children.where(
          (c) => selected == '' || c['id'] == selected,
        );
        for (final child in eligible) {
          final id = textOf(child['id']);
          final data = obj(
            await widget.auth.request(
              'GET',
              '/api/v1/guardian/children/${Uri.encodeComponent(id)}/profile',
            ),
          );
          if (obj(data['player'])['id'] != id) throw StateError('scope');
          for (final e in rows(data['enrollments'])) {
            for (final s in rows(e['upcoming'])) {
              final key = textOf(s['id']), previous = unique[key];
              unique[key] = {
                ...s,
                'group': s['group'] ?? e['group'],
                'sport': e['sport'],
                'branch': e['branch'],
                'childrenNames': <String>{
                  ...((previous?['childrenNames'] as List?)?.cast<String>() ??
                      []),
                  textOf(child['arabicName']),
                }.toList(),
                'childIds': <String>{
                  ...((previous?['childIds'] as List?)?.cast<String>() ?? []),
                  id,
                }.toList(),
              };
            }
          }
        }
        return (unique.values.toList(), false);
      } catch (_) {
        return (<Json>[], true);
      }
    }

    Future<(List<Json>, bool)> games() async {
      try {
        final data = rows(
          await widget.auth.request(
            'GET',
            '/api/v1/my-matches?upcomingOnly=true${widget.guardian && selected != '' ? '&playerId=${Uri.encodeComponent(selected!)}' : ''}',
          ),
        );
        if (data.any((m) => m['scheduleWindow'] != 'Upcoming')) {
          throw StateError('window');
        }
        if (widget.guardian &&
            data.any(
              (m) =>
                  m['children'] is! List ||
                  !(m['children'] as List).any(
                    (id) => selected == ''
                        ? children.any((c) => c['id'] == id)
                        : id == selected,
                  ),
            )) {
          throw StateError('scope');
        }
        return (
          data
              .where(
                (m) => m['isUpcoming'] == true && m['status'] != 'Cancelled',
              )
              .toList(),
          false,
        );
      } catch (_) {
        return (<Json>[], true);
      }
    }

    final result = await Future.wait([training(), games()]);
    if (!current(attempt)) return;
    setState(() {
      sessions = result[0].$1;
      trainingError = result[0].$2;
      matches = result[1].$1;
      matchError = result[1].$2;
      loading = false;
    });
  }

  Future<void> open(Widget page) async {
    await openPage(context, page);
    if (mounted && openedIdentity == identity()) await load();
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'المواعيد القادمة', 'Upcoming schedule'),
    child: ListenableBuilder(
      listenable: widget.auth,
      builder: (context, _) {
        if (openedIdentity == null || openedIdentity != identity()) {
          return const SizedBox.shrink();
        }
        final events =
            <({bool match, Json data})>[
              if (filter != 'matches')
                for (final s in sessions) (match: false, data: s),
              if (filter != 'training')
                for (final m in matches) (match: true, data: m),
            ]..sort(
              (
                a,
                b,
              ) => '${a.data[a.match ? 'matchDate' : 'sessionDate']} ${a.data['startTime']}'
                  .compareTo(
                    '${b.data[b.match ? 'matchDate' : 'sessionDate']} ${b.data['startTime']}',
                  ),
            );
        return gList([
          Row(
            children: [
              Expanded(
                child: UiText(
                  localized(
                    context,
                    'حصصك ومبارياتك في مكان واحد',
                    'Training and matches in one place',
                  ),
                ),
              ),
              IconButton(
                onPressed: loading ? null : () => load(),
                tooltip: localized(
                  context,
                  'تحديث المواعيد',
                  'Refresh schedule',
                ),
                icon: const Icon(Icons.refresh),
              ),
            ],
          ),
          if (widget.guardian && children.isNotEmpty)
            FamilyChildFilter(
              children: children,
              selected: playerId ?? '',
              enabled: !loading,
              onSelected: (id) => load(child: id),
            ),
          const SizedBox(height: 12),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              for (final f in [
                ('all', 'الكل', 'All'),
                ('training', 'الحصص', 'Training'),
                ('matches', 'المباريات', 'Matches'),
              ])
                ChoiceChip(
                  label: UiText(localized(context, f.$2, f.$3)),
                  selected: filter == f.$1,
                  onSelected: (_) => setState(() => filter = f.$1),
                ),
            ],
          ),
          if (loading)
            const Padding(
              padding: EdgeInsets.all(24),
              child: Center(child: CircularProgressIndicator()),
            )
          else if (homeError)
            GLink(
              localized(
                context,
                'تعذر تحميل الأبناء — أعد المحاولة',
                'Could not load children — retry',
              ),
              Icons.refresh,
              () => load(),
            )
          else ...[
            if (trainingError)
              GLink(
                localized(
                  context,
                  'تعذر تحميل الحصص — أعد المحاولة',
                  'Could not load training — retry',
                ),
                Icons.refresh,
                () => load(),
              ),
            if (matchError)
              GLink(
                localized(
                  context,
                  'تعذر تحميل المباريات — أعد المحاولة',
                  'Could not load matches — retry',
                ),
                Icons.refresh,
                () => load(),
              ),
            if (widget.guardian && children.isEmpty)
              const EmptyMessage('لا يوجد أبناء مرتبطون بهذا الحساب.'),
            for (var i = 0; i < events.length; i++) ...[
              if (i == 0 || eventDate(events[i]) != eventDate(events[i - 1]))
                widget.guardian
                    ? GuardianAgendaDay(date: eventDate(events[i]))
                    : Heading(eventDate(events[i])),
              eventCard(context, events[i]),
            ],
            if (events.isEmpty &&
                !trainingError &&
                !matchError &&
                (!widget.guardian || children.isNotEmpty))
              const EmptyMessage('لا توجد مواعيد قادمة في القائمة المعروضة.'),
            UiText(
              localized(
                context,
                widget.guardian
                    ? 'تُعرض أقرب 3 حصص لكل تسجيل والمباريات المنشورة للابن المختار. راجع المواعيد الدورية من تفاصيل التدريب.'
                    : 'تُعرض الحصص القادمة خلال 14 يومًا ومبارياتك المنشورة القادمة.',
                widget.guardian
                    ? 'Nearest 3 sessions per enrollment and published matches for the selected child. Recurring times are in training details.'
                    : 'Training in the next 14 days and your upcoming published matches.',
              ),
              style: Theme.of(context).textTheme.bodySmall,
            ),
            if (!widget.guardian || playerId != null)
              GLink(
                localized(
                  context,
                  'سجل المباريات والإلغاءات',
                  'Match history & cancellations',
                ),
                Icons.history,
                () => open(
                  MatchSchedule(
                    auth: widget.auth,
                    playerId: widget.guardian && playerId != ''
                        ? playerId
                        : null,
                  ),
                ),
              ),
            if (widget.guardian && playerId != null && playerId != '')
              GLink(
                localized(
                  context,
                  'تفاصيل التدريب والمواعيد الدورية',
                  'Training & recurring times',
                ),
                Icons.event_repeat,
                () => open(
                  ProfileRecords(
                    auth: widget.auth,
                    playerId: playerId!,
                    section: 'schedule',
                  ),
                ),
              ),
          ],
        ]);
      },
    ),
  );

  String eventDate(({bool match, Json data}) event) =>
      day(event.data[event.match ? 'matchDate' : 'sessionDate']);

  Widget eventCard(BuildContext context, ({bool match, Json data}) event) {
    final d = event.data;
    Future<void> onTap() => open(
      event.match
          ? MatchSchedule(
              auth: widget.auth,
              playerId: widget.guardian && playerId != '' ? playerId : null,
              matchId: textOf(d['id']),
            )
          : widget.guardian
          ? ProfileRecords(
              auth: widget.auth,
              playerId: playerId == ''
                  ? (d['childIds'] as List).first as String
                  : playerId!,
              section: 'schedule',
            )
          : CoachAttendance(auth: widget.auth, sessionId: textOf(d['id'])),
    );
    if (widget.guardian) {
      final names = event.match
          ? children
                .where((c) => (d['children'] as List).contains(c['id']))
                .map((c) => textOf(c['arabicName']))
                .toList()
          : (d['childrenNames'] as List?)?.cast<String>() ?? <String>[];
      return GuardianAgendaCard(
        key: ValueKey('guardian-agenda-${d['id']}'),
        data: d,
        isMatch: event.match,
        childNames: names,
        onTap: onTap,
      );
    }
    return GCard(
      onTap: onTap,
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(
            event.match ? Icons.emoji_events_outlined : Icons.sports_outlined,
            color: Theme.of(context).colorScheme.primary,
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                UiText(
                  localized(
                    context,
                    event.match ? 'مباراة' : 'حصة تدريب',
                    event.match ? 'Match' : 'Training',
                  ),
                  style: Theme.of(context).textTheme.labelLarge,
                ),
                UiText(
                  event.match
                      ? '${d['team']} × ${d['opponent']}'
                      : textOf(d['group']),
                  style: Theme.of(context).textTheme.titleMedium,
                ),
                Directionality(
                  textDirection: TextDirection.rtl,
                  child: UiText(
                    event.match
                        ? trainingTimeOf(d['startTime'])
                        : trainingTimeRange(d['startTime'], d['endTime']),
                  ),
                ),
                UiText(
                  event.match
                      ? '${d['tournament']} · ${d['venue']}'
                      : '${d['sport']} · ${d['branch']}',
                ),
                if (!event.match && d['childrenNames'] is List)
                  UiText(
                    (d['childrenNames'] as List).join('، '),
                    style: const TextStyle(fontSize: 12),
                  ),
                if (event.match && d['coachName'] != null)
                  UiText('مدرب المباراة: ${d['coachName']}'),
                if (event.match &&
                    d['coachAssignmentSource'] == 'LegacyCurrentTeam')
                  const UiText('إسناد قديم غير موثق؛ يلزم مراجعة الإدارة.'),
              ],
            ),
          ),
          const Icon(Icons.chevron_right, size: 18),
        ],
      ),
    );
  }
}

class GuardianAgendaDay extends StatelessWidget {
  const GuardianAgendaDay({super.key, required this.date});
  final String date;
  @override
  Widget build(BuildContext context) {
    final parsed = DateTime.tryParse(date);
    final colors = Theme.of(context).colorScheme;
    return Container(
      key: ValueKey('agenda-date-$date'),
      margin: const EdgeInsets.only(top: 10, bottom: 6),
      padding: const EdgeInsets.only(bottom: 6),
      decoration: BoxDecoration(
        border: Border(bottom: BorderSide(color: colors.outlineVariant)),
      ),
      child: UiText(
        parsed == null
            ? 'تاريخ غير محدد'
            : MaterialLocalizations.of(context).formatFullDate(parsed),
        style: TextStyle(
          fontSize: 13,
          fontWeight: FontWeight.w600,
          color: colors.primary,
        ),
      ),
    );
  }
}

class GuardianAgendaCard extends StatelessWidget {
  const GuardianAgendaCard({
    super.key,
    required this.data,
    required this.isMatch,
    required this.childNames,
    required this.onTap,
  });
  final Json data;
  final bool isMatch;
  final List<String> childNames;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.only(bottom: 6),
      child: Semantics(
        button: true,
        child: Material(
          color: colors.surface,
          child: InkWell(
            onTap: onTap,
            child: Padding(
              padding: const EdgeInsets.all(10),
              child: IntrinsicHeight(
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    SizedBox(
                      width: 4,
                      child: ColoredBox(color: colors.primary),
                    ),
                    const SizedBox(width: 10),
                    SizedBox(
                      width: MediaQuery.textScalerOf(context).scale(54),
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          UiText(
                            trainingTimeOf(data['startTime']),
                            textDirection: TextDirection.ltr,
                            style: const TextStyle(
                              fontSize: 13,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                          if (!isMatch)
                            UiText(
                              trainingTimeOf(data['endTime']),
                              textDirection: TextDirection.ltr,
                              style: TextStyle(
                                fontSize: 11,
                                color: colors.onSurfaceVariant,
                              ),
                            ),
                        ],
                      ),
                    ),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          UiText(
                            isMatch
                                ? '${data['team']} × ${data['opponent']}'
                                : textOf(data['group']),
                            style: const TextStyle(
                              fontSize: 14,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                          UiText(
                            childNames.join('، '),
                            style: TextStyle(
                              fontSize: 11,
                              color: colors.onSurfaceVariant,
                            ),
                          ),
                          UiText(
                            isMatch
                                ? '${data['tournament']} · ${data['venue']}'
                                : '${data['sport']} · ${data['branch']}',
                            style: TextStyle(
                              fontSize: 11,
                              color: colors.onSurfaceVariant,
                            ),
                          ),
                          if (isMatch && data['coachName'] != null)
                            UiText(
                              'مدرب المباراة: ${data['coachName']}',
                              style: TextStyle(
                                fontSize: 11,
                                color: colors.onSurfaceVariant,
                              ),
                            ),
                          if (isMatch &&
                              data['coachAssignmentSource'] ==
                                  'LegacyCurrentTeam')
                            const UiText(
                              'إسناد قديم غير موثق؛ يلزم مراجعة الإدارة.',
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
      ),
    );
  }
}
