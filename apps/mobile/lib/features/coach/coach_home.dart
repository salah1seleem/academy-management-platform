import '../shared/group_schedule.dart';
import '../shared/modern_home.dart';
import '../shared/directory_cards.dart';
import '../shared/subscription_home.dart' show PendingHomeSection;
import '../shared/audience_communication.dart';
import '../../core/widgets/home_skeleton.dart';

import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../../core/widgets/app_widgets.dart';
import '../guardian/guardian_ui.dart';
import '../shared/matches.dart';
import '../shared/private_media.dart';
import '../shared/academy_news.dart';
import '../shared/staff_amounts.dart';
import '../shared/staff_home_action.dart';
import '../shared/experience_ui.dart';
import 'coach_attendance.dart';
import 'coach_home_summary.dart';
import 'coach_evaluation.dart';
import 'coach_player.dart';
import 'session_context.dart';
import '../owner/owner_action_rail.dart';
import '../owner/owner_quick_operations.dart' show NativeEvaluationPicker;
import '../shared/role_navigation.dart' show CoachGroupsPage;
import '../shared/staff_work_periods.dart';
import 'monthly_stars.dart';

class CoachHome extends StatefulWidget {
  const CoachHome({super.key, required this.auth, required this.accountPage});
  final AuthController auth;
  final Widget accountPage;
  @override
  State<CoachHome> createState() => _CoachHomeState();
}

class _CoachHomeState extends State<CoachHome> {
  late Future<List<Object?>> request;
  String? identity, selectedDay;
  List<Object?>? partial;
  List<String> paths = [];
  int generation = 0;
  bool showGroups = false;
  String? get currentIdentity => widget.auth.session?.selected?.role != 'Coach'
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  Future<List<Object?>> load() async {
    final expected = currentIdentity, attempt = ++generation;
    partial = null;
    if (expected == null) return <Object?>[];
    final calendar = obj(
      await widget.auth.request('GET', '/api/v1/academy/calendar'),
    );
    if (!mounted || expected != currentIdentity || attempt != generation) {
      return <Object?>[];
    }
    selectedDay ??= textOf(calendar['today']);
    final query = Uri(
      queryParameters: {'from': selectedDay!, 'to': selectedDay!},
    ).query;
    final monthly = Uri(
      queryParameters: {
        'from': '${textOf(calendar['today']).substring(0, 7)}-01',
        'to': textOf(calendar['today']),
      },
    ).query;
    paths = [
      '/api/v1/coach/groups',
      '/api/v1/attendance/sessions?nearestOnly=true',
      '/api/v1/evaluations/options',
      '/api/v1/attendance/sessions?$query',
      '/api/v1/attendance/sessions?$monthly',
    ];
    final sources = List<String>.from(paths),
        values = <Object?>[null, null, null, null, null, calendar];
    setState(() => partial = values);
    await Future.wait([
      for (var i = 0; i < sources.length; i++)
        () async {
          Object? value;
          try {
            value = await widget.auth.request('GET', sources[i]);
          } catch (e) {
            value = e is ApiFailure
                ? e
                : const ApiFailure(500, 'تعذر تحميل هذا القسم.');
          }
          if (!mounted ||
              expected != currentIdentity ||
              attempt != generation) {
            return;
          }
          values[i] = value;
          setState(() {
            final next = List<Object?>.from(partial ?? values);
            next[i] = value;
            partial = next;
          });
        }(),
    ]);
    return values;
  }

  Future<void> retrySource(int index) async {
    if (currentIdentity == null || partial == null || index >= paths.length) {
      return;
    }
    final expected = currentIdentity, attempt = generation, path = paths[index];
    setState(() => partial![index] = null);
    Object? value;
    try {
      value = await widget.auth.request('GET', path);
    } catch (e) {
      value = e is ApiFailure
          ? e
          : const ApiFailure(500, 'تعذر تحميل هذا القسم.');
    }
    if (mounted && expected == currentIdentity && attempt == generation) {
      setState(() => partial![index] = value);
    }
  }

  @override
  void initState() {
    super.initState();
    identity = currentIdentity;
    request = load();
    widget.auth.addListener(contextChanged);
  }

  void contextChanged() {
    if (!mounted || identity == currentIdentity) return;
    setState(() {
      identity = currentIdentity;
      selectedDay = null;
      request = load();
    });
  }

  @override
  void didUpdateWidget(covariant CoachHome old) {
    super.didUpdateWidget(old);
    if (old.auth != widget.auth) {
      old.auth.removeListener(contextChanged);
      widget.auth.addListener(contextChanged);
      identity = currentIdentity;
      selectedDay = null;
      request = load();
    }
  }

  @override
  void dispose() {
    widget.auth.removeListener(contextChanged);
    super.dispose();
  }

  Future<void> refresh() async {
    final next = load();
    setState(() {
      request = next;
    });
    try {
      await next;
    } catch (_) {
      /* the failed source stays visible with retry */
    }
  }

  void openSession(Json session, {bool evaluations = false}) => openPage(
    context,
    CoachAttendance(
      auth: widget.auth,
      sessionId: textOf(session['id']),
      title: evaluations ? 'تقييم لاعبي الحصة' : 'تفاصيل الحصة',
    ),
  );
  @override
  Widget build(BuildContext context) => GuardianPage(
    showAppBar: false,
    title: 'الرئيسية · المدرب',
    child: FutureBuilder<List<Object?>>(
      future: request,
      builder: (context, snapshot) {
        if (currentIdentity == null) return const SizedBox.shrink();
        if (partial == null &&
            snapshot.connectionState != ConnectionState.done) {
          return const HomeSkeleton(role: 'Coach');
        }
        if (partial == null && snapshot.hasError) {
          return Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                ErrorNotice(
                  snapshot.error is ApiFailure
                      ? (snapshot.error as ApiFailure).message
                      : 'تعذر تحميل بيانات المدرب.',
                ),
                OutlinedButton(
                  onPressed: refresh,
                  child: const UiText('إعادة المحاولة'),
                ),
              ],
            ),
          );
        }
        final values = partial ?? snapshot.data!;
        bool ready(int index) =>
            values.length > index &&
            (index == 2
                ? values[index] is Map<String, dynamic>
                : values[index] is List);
        Widget sourceState(int index, String name, {double height = 180}) =>
            values[index] == null
            ? PendingHomeSection(label: 'جارٍ تحميل $name', height: height)
            : GCard(
                child: Column(
                  children: [
                    UiText(
                      values[index] is ApiFailure
                          ? (values[index] as ApiFailure).message
                          : 'تعذر تحميل $name.',
                    ),
                    TextButton(
                      onPressed: () => retrySource(index),
                      child: UiText('إعادة تحميل $name'),
                    ),
                  ],
                ),
              );
        final groups = ready(0) ? rows(values[0]) : <Json>[],
            nearest = ready(1) ? rows(values[1]) : <Json>[],
            enrollments = ready(2)
                ? rows(obj(values[2])['enrollments'])
                : <Json>[],
            sessions = ready(3)
                ? rows(values[3])
                      .where((s) => s['status'] != 'Cancelled')
                      .toList()
                : <Json>[],
            monthly = ready(4)
                ? rows(values[4])
                      .where((s) => s['status'] != 'Cancelled')
                      .toList()
                : <Json>[],
            calendar = obj(values[5]);
        final today = textOf(calendar['today']), active = selectedDay ?? today;
        return RefreshIndicator(
          onRefresh: refresh,
          child: gList([
            const RoleHomeIntro(
              title: 'ملعبك اليوم',
              subtitle: 'حصصك ولاعبوك وخطوتهم التالية',
            ),

            StaffHomeAttendanceAction(auth: widget.auth),
            if (!ready(1))
              sourceState(1, 'الحصة القادمة', height: 220)
            else if (nearest.isNotEmpty)
              CoachSessionCard(
                session: nearest.first,
                onOpen: () => openSession(nearest.first),
                featured: true,
              )
            else
              const GCard(child: UiText('لا توجد حصة قادمة مجدولة.')),

            OwnerActionRail(
              key: ValueKey('coach-home-actions-$identity'),
              actions: [
                if (nearest.isNotEmpty)
                  OwnerRailAction(
                    'فتح الحصة',
                    Icons.fact_check_outlined,
                    const Color(0xFF248EBA),
                    () => openSession(nearest.first),
                  ),
                OwnerRailAction(
                  'مجموعاتي',
                  Icons.groups_outlined,
                  const Color(0xFF6755C7),
                  () => openPage(context, CoachGroupsPage(auth: widget.auth)),
                ),
                if (enrollments.isNotEmpty &&
                    ready(2) &&
                    obj(values[2])['canEvaluate'] == true)
                  OwnerRailAction(
                    'تقييم اللاعبين',
                    Icons.assignment_outlined,
                    const Color(0xFF159B82),
                    () => openPage(
                      context,
                      NativeEvaluationPicker(auth: widget.auth),
                    ),
                  ),
                OwnerRailAction(
                  'نجوم فريقي',
                  Icons.star_outline_rounded,
                  const Color(0xFFE99A23),
                  () => openPage(context, CoachMonthlyStars(auth: widget.auth)),
                ),
                OwnerRailAction(
                  'سجل حضوري',
                  Icons.access_time_outlined,
                  const Color(0xFF377CC4),
                  () => openPage(
                    context,
                    StaffWorkPeriodsPage(auth: widget.auth),
                  ),
                ),
              ],
            ),
            Row(
              children: [
                Expanded(
                  child: !ready(0)
                      ? sourceState(0, 'المجموعات', height: 120)
                      : GCard(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              UiText(
                                '${groups.length}',
                                style: const TextStyle(
                                  fontSize: 30,
                                  fontWeight: FontWeight.w700,
                                ),
                              ),
                              const UiText(
                                'المجموعات المسندة',
                                style: TextStyle(fontSize: 12),
                              ),
                            ],
                          ),
                        ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: !ready(2)
                      ? sourceState(2, 'اللاعبين', height: 120)
                      : GCard(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              UiText(
                                '${enrollments.map((e) => e['playerId'] ?? e['id']).toSet().length}',
                                style: const TextStyle(
                                  fontSize: 30,
                                  fontWeight: FontWeight.w700,
                                ),
                              ),
                              const UiText(
                                'اللاعبون في مجموعاتي',
                                style: TextStyle(fontSize: 12),
                              ),
                            ],
                          ),
                        ),
                ),
              ],
            ),
            if (!ready(0) || !ready(4))
              sourceState(!ready(0) ? 0 : 4, 'الحضور المسجل')
            else
              CoachAttendanceSummary(
                groups: groups,
                sessions: monthly,
                today: today,
              ),
            const ExperienceSection('جدولي', Icons.calendar_month_outlined),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 16),
              child: Wrap(
                spacing: 8,
                children: [
                  for (final mode in [false, true])
                    ChoiceChip(
                      label: UiText(mode ? 'المجموعات' : 'الحصص'),
                      selected: showGroups == mode,
                      onSelected: (_) => setState(() => showGroups = mode),
                    ),
                ],
              ),
            ),
            if (!showGroups) ...[
              CoachDaySelector(
                today: today,
                selected: active,
                onChange: (day) {
                  selectedDay = day;
                  refresh();
                },
              ),
              Padding(
                padding: const EdgeInsets.symmetric(
                  horizontal: 16,
                  vertical: 10,
                ),
                child: UiText(
                  'حصص ${active == today ? 'اليوم' : active} · ${sessions.length}',
                  style: const TextStyle(
                    fontSize: 12,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ),
              if (!ready(3))
                sourceState(3, 'حصص اليوم')
              else if (sessions.isEmpty)
                const GCard(child: UiText('لا توجد حصص في اليوم المختار.')),
              for (final session in sessions)
                GCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      UiText(
                        '${CoachSessionCard.time(session['startTime'])} · ${session['group']}',
                        style: const TextStyle(fontWeight: FontWeight.w700),
                      ),
                      const SizedBox(height: 6),
                      UiText(
                        '${session['resource'] ?? session['branch']} · ${session['playerCount'] ?? '—'} لاعبًا',
                        style: const TextStyle(fontSize: 12),
                      ),
                      const SizedBox(height: 10),
                      Wrap(
                        spacing: 8,
                        children: [
                          OutlinedButton(
                            onPressed: () => openSession(session),
                            child: const UiText('تسجيل الحضور'),
                          ),
                          if (ready(2) && obj(values[2])['canEvaluate'] == true)
                            TextButton(
                              onPressed: () =>
                                  openSession(session, evaluations: true),
                              child: const UiText('التقييم'),
                            ),
                        ],
                      ),
                    ],
                  ),
                ),
            ] else ...[
              if (!ready(0))
                sourceState(0, 'مجموعاتي')
              else if (groups.isEmpty)
                const EmptyMessage('لا توجد مجموعات مسندة حاليًا.'),
              for (final group in groups)
                GroupDirectoryCard(
                  group: group,
                  onTap: () => openPage(
                    context,
                    CoachGroup(auth: widget.auth, group: group),
                  ),
                ),
            ],
            NextMatchStrip(auth: widget.auth),
            CoachHomeStarBudgets(key: ValueKey(request), auth: widget.auth),
            NewsStrip(auth: widget.auth),
            GLink(
              'السلف والمبالغ الإضافية',
              Icons.add_card_outlined,
              () => openPage(context, StaffAmounts(auth: widget.auth)),
            ),
          ]),
        );
      },
    ),
  );
}

class CoachDaySelector extends StatelessWidget {
  const CoachDaySelector({
    super.key,
    required this.today,
    required this.selected,
    required this.onChange,
  });
  final String today, selected;
  final ValueChanged<String> onChange;
  static const weekdays = [
    'الإثنين',
    'الثلاثاء',
    'الأربعاء',
    'الخميس',
    'الجمعة',
    'السبت',
    'الأحد',
  ];
  @override
  Widget build(BuildContext context) {
    final base = DateTime.parse('${today}T12:00:00Z');
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 12),
      child: Row(
        children: [
          Expanded(
            child: SizedBox(
              height: 30 + MediaQuery.textScalerOf(context).scale(62),
              child: ListView.separated(
                padding: const EdgeInsets.symmetric(horizontal: 16),
                scrollDirection: Axis.horizontal,
                itemCount: 7,
                separatorBuilder: (_, _) => const SizedBox(width: 6),
                itemBuilder: (context, i) {
                  final date = base.add(Duration(days: i)),
                      value = isoDay(date),
                      active = selected == value;
                  return InkWell(
                    onTap: () => onChange(value),
                    child: Container(
                      width: 24 + MediaQuery.textScalerOf(context).scale(38),
                      padding: const EdgeInsets.symmetric(vertical: 8),
                      decoration: BoxDecoration(
                        color: active
                            ? Theme.of(context).colorScheme.primary
                            : Theme.of(context).colorScheme.surface,
                        border: Border.all(
                          color: Theme.of(context).dividerColor,
                        ),
                      ),
                      child: Column(
                        children: [
                          UiText(
                            i == 0 ? 'اليوم' : weekdays[date.weekday - 1],
                            maxLines: 1,
                            style: TextStyle(
                              fontSize: 10,
                              color: active
                                  ? Theme.of(context).colorScheme.onPrimary
                                  : null,
                            ),
                          ),
                          UiText(
                            '${date.day}',
                            style: TextStyle(
                              fontSize: 23,
                              fontWeight: FontWeight.w700,
                              color: active
                                  ? Theme.of(context).colorScheme.onPrimary
                                  : null,
                            ),
                          ),
                        ],
                      ),
                    ),
                  );
                },
              ),
            ),
          ),
          IconButton(
            tooltip: 'اختيار يوم',
            icon: const Icon(Icons.calendar_month_outlined),
            onPressed: () async {
              final chosen = await showDatePicker(
                context: context,
                initialDate: DateTime.parse(selected),
                firstDate: DateTime(
                  base.year,
                  base.month,
                  base.day,
                ).subtract(const Duration(days: 366)),
                lastDate: DateTime(
                  base.year,
                  base.month,
                  base.day,
                ).add(const Duration(days: 366)),
              );
              if (chosen != null) onChange(isoDay(chosen));
            },
          ),
        ],
      ),
    );
  }
}

class CoachSessionCard extends StatelessWidget {
  final Json session;
  final bool featured;
  final VoidCallback onOpen;
  final String? openLabel;
  const CoachSessionCard({
    super.key,
    required this.session,
    required this.onOpen,
    this.featured = false,
    this.openLabel,
  });
  static String time(Object? v) => trainingTimeOf(v);

  @override
  Widget build(BuildContext context) {
    final c = Theme.of(context).colorScheme;
    final date = DateTime.tryParse(textOf(session['sessionDate']));
    final large = MediaQuery.textScalerOf(context).scale(1) > 1.4;
    return GCard(
      key: ValueKey('coach-session-${session['id']}'),
      onTap: onOpen,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (featured) ...[
            Row(
              children: [
                Icon(Icons.near_me_outlined, size: 18, color: c.primary),
                const SizedBox(width: 8),
                Expanded(
                  child: UiText(
                    'الحصة القادمة',
                    style: TextStyle(
                      fontSize: 12,
                      color: c.primary,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 16),
          ],
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Container(
                width: large ? 72 : 58,
                padding: const EdgeInsets.symmetric(
                  vertical: 10,
                  horizontal: 5,
                ),
                decoration: BoxDecoration(
                  color: c.primary.withValues(alpha: .08),
                  borderRadius: BorderRadius.circular(16),
                ),
                child: Column(
                  children: [
                    if (date != null) ...[
                      UiText(
                        arabicWeekday(date.weekday % 7),
                        style: TextStyle(fontSize: 10, color: c.primary),
                        textAlign: TextAlign.center,
                      ),
                      const SizedBox(height: 4),
                    ],
                    UiText(
                      date?.day.toString() ?? '—',
                      style: TextStyle(
                        fontSize: 28,
                        fontWeight: FontWeight.w800,
                        height: 1.2,
                        color: c.primary,
                      ),
                    ),
                    const SizedBox(height: 5),
                    UiText(
                      date == null ? '—' : '${date.month} / ${date.year}',
                      style: TextStyle(fontSize: 9, color: c.onSurfaceVariant),
                      textAlign: TextAlign.center,
                    ),
                  ],
                ),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    UiText(
                      textOf(session['group']),
                      style: const TextStyle(
                        fontSize: 17,
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                    const SizedBox(height: 4),
                    UiText(
                      trainingTimeRange(
                        session['startTime'],
                        session['endTime'],
                      ),
                      textDirection: TextDirection.rtl,
                      style: const TextStyle(
                        fontSize: 16,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    const SizedBox(height: 4),
                    UiText(
                      '${session['branch'] ?? ''} · ${session['sport'] ?? ''}',
                      style: TextStyle(fontSize: 12, color: c.onSurfaceVariant),
                    ),
                    if (session['resource'] != null)
                      UiText(
                        textOf(session['resource']),
                        style: TextStyle(
                          fontSize: 12,
                          color: c.onSurfaceVariant,
                        ),
                      ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          SessionContextBadges(session: session),
          const SizedBox(height: 4),
          UiText(
            localized(
              context,
              '${session['playerCount'] ?? '—'} لاعب في الحصة',
              '${session['playerCount'] ?? '—'} players in this session',
            ),
            style: TextStyle(fontSize: 12, color: c.onSurfaceVariant),
          ),
          const SizedBox(height: 12),
          if (featured)
            FilledButton.icon(
              onPressed: onOpen,
              icon: const Icon(Icons.arrow_forward_rounded, size: 18),
              label: UiText(localized(context, 'فتح الحصة', 'Open session')),
            )
          else
            TextButton.icon(
              onPressed: onOpen,
              icon: const Icon(Icons.arrow_forward_rounded, size: 18),
              label: UiText(
                openLabel ??
                    localized(context, 'تفاصيل الحصة', 'Session details'),
              ),
            ),
        ],
      ),
    );
  }
}

class CoachGroup extends StatefulWidget {
  final AuthController auth;
  final Json group;
  const CoachGroup({super.key, required this.auth, required this.group});
  @override
  State<CoachGroup> createState() => _CoachGroupState();
}

class _CoachGroupState extends State<CoachGroup> {
  final search = TextEditingController();
  late final identity = coachContext(widget.auth);
  late Future<List<Object?>> request = load();
  Future<List<Object?>> load() => Future.wait([
    widget.auth.request('GET', '/api/v1/coach/groups'),
    widget.auth.request('GET', '/api/v1/evaluations/options'),
    widget.auth.request('GET', '/api/v1/attendance/sessions?upcomingOnly=true'),
  ]);
  @override
  void dispose() {
    search.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AppScaffold(
    title: textOf(widget.group['arabicName']),
    child: ListenableBuilder(
      listenable: widget.auth,
      builder: (context, _) {
        if (identity == null || identity != coachContext(widget.auth)) {
          return const SizedBox.shrink();
        }
        return FutureBuilder<List<Object?>>(
          future: request,
          builder: (context, snapshot) {
            if (snapshot.connectionState != ConnectionState.done) {
              return const Center(child: CircularProgressIndicator());
            }
            if (snapshot.hasError) {
              return Center(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    ErrorNotice(
                      snapshot.error is ApiFailure
                          ? (snapshot.error as ApiFailure).message
                          : localized(
                              context,
                              'تعذر تحميل المجموعة. أعد فتحها.',
                              'Unable to load the group. Reopen it.',
                            ),
                    ),
                    OutlinedButton.icon(
                      onPressed: () => setState(() {
                        request = load();
                      }),
                      icon: const Icon(Icons.refresh),
                      label: const UiText('إعادة المحاولة'),
                    ),
                  ],
                ),
              );
            }
            final active = rows(snapshot.data![0])
                .where((g) => g['id'] == widget.group['id']);
            if (active.isEmpty) {
              return Center(
                child: UiText(
                  localized(
                    context,
                    'لم تعد هذه المجموعة مسندة إليك.',
                    'This group is no longer assigned to you.',
                  ),
                ),
              );
            }
            final options = obj(snapshot.data![1]);
            final players = rows(options['enrollments'])
                .where((p) => p['groupId'] == widget.group['id'])
                .toList();
            final filtered = players
                .where((p) => playerMatches(p, search.text))
                .toList();
            final sessions =
                rows(snapshot.data![2])
                    .where(
                      (s) =>
                          s['trainingGroupId'] == widget.group['id'] &&
                          s['status'] != 'Cancelled' &&
                          s['status'] != 'Held',
                    )
                    .toList()
                  ..sort(
                    (a, b) => '${a['sessionDate']} ${a['startTime']}'.compareTo(
                      '${b['sessionDate']} ${b['startTime']}',
                    ),
                  );
            return ListView(
              padding: const EdgeInsets.all(18),
              children: [
                AppCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const Icon(Icons.groups_2_outlined, size: 32),
                      const SizedBox(height: 10),
                      UiText(
                        textOf(active.first['arabicName']),
                        style: Theme.of(context).textTheme.titleLarge,
                      ),
                      UiText(
                        localized(
                          context,
                          '${players.length} لاعب في المجموعة',
                          '${players.length} players in the group',
                        ),
                      ),
                    ],
                  ),
                ),
                WeeklyGroupSchedule(
                  schedules: rows(active.first['recurring'] ?? <Json>[]),
                ),
                AudienceCommunicationLink(
                  auth: widget.auth,
                  id: textOf(widget.group['id']),
                ),
                if (sessions.isNotEmpty) ...[
                  UiText(
                    localized(context, 'الموعد القادم', 'Next session'),
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  CoachSessionCard(
                    session: sessions.first,
                    openLabel: localized(
                      context,
                      'فتح الحصة الأقرب',
                      'Open nearest session',
                    ),
                    onOpen: () => openPage(
                      context,
                      CoachAttendance(
                        auth: widget.auth,
                        sessionId: textOf(sessions.first['id']),
                        title: localized(
                          context,
                          'تفاصيل الحصة',
                          'Session details',
                        ),
                      ),
                    ),
                  ),
                ] else
                  UiText(
                    localized(
                      context,
                      'لا توجد حصة قادمة خلال 14 يومًا',
                      'No upcoming session within 14 days',
                    ),
                  ),
                const SizedBox(height: 16),
                TextField(
                  controller: search,
                  onChanged: (_) => setState(() {}),
                  decoration: InputDecoration(
                    labelText: localized(
                      context,
                      'بحث بالاسم أو كود اللاعب',
                      'Search name or player code',
                    ),
                    prefixIcon: const Icon(Icons.search),
                    suffixIcon: search.text.isEmpty
                        ? null
                        : IconButton(
                            tooltip: localized(
                              context,
                              'مسح البحث',
                              'Clear search',
                            ),
                            icon: const Icon(Icons.close),
                            onPressed: () => setState(search.clear),
                          ),
                  ),
                ),
                const SizedBox(height: 12),
                UiText(
                  localized(
                    context,
                    '${filtered.length} من ${players.length} لاعب',
                    '${filtered.length} of ${players.length} players',
                  ),
                ),
                if (filtered.isEmpty)
                  UiText(
                    localized(
                      context,
                      players.isEmpty
                          ? 'لا يوجد لاعبون في هذه المجموعة.'
                          : 'لا توجد نتائج لهذا البحث.',
                      players.isEmpty
                          ? 'No players in this group.'
                          : 'No matching players.',
                    ),
                  ),
                for (final player in filtered)
                  PlayerDirectoryCard(
                    auth: widget.auth,
                    player: {...player, 'arabicName': player['player']},
                    enrollments: [player],
                    openLabel: 'تفاصيل اللاعب',
                    onOpen: () => openPage(
                      context,
                      CoachPlayerPage(
                        auth: widget.auth,
                        enrollmentId: textOf(player['id']),
                        groupId: textOf(widget.group['id']),
                      ),
                    ),
                    onEvaluate: options['canEvaluate'] == true
                        ? () => openPage(
                            context,
                            CoachEvaluation(
                              auth: widget.auth,
                              enrollment: player,
                            ),
                          )
                        : null,
                  ),
                const SizedBox(height: 12),
                OutlinedButton.icon(
                  onPressed: () => openPage(
                    context,
                    UploadSessionMedia(
                      auth: widget.auth,
                      groupId: textOf(widget.group['id']),
                    ),
                  ),
                  icon: const Icon(Icons.add_photo_alternate_outlined),
                  label: const UiText('رفع صور وفيديوهات للحصة'),
                ),
              ],
            );
          },
        );
      },
    ),
  );
}
