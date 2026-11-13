import '../shared/group_schedule.dart';
import '../shared/modern_home.dart';
import '../shared/directory_cards.dart';
import '../shared/subscription_home.dart' show PendingHomeSection;
import '../shared/audience_communication.dart';

import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show Intl;

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/academy_brand.dart';
import '../../core/theme/ui_text.dart';
import '../../core/widgets/app_widgets.dart';
import '../guardian/guardian_ui.dart';
import '../shared/matches.dart';
import '../shared/private_media.dart';
import '../shared/academy_news.dart';
import '../shared/staff_home_action.dart';
import '../shared/experience_ui.dart';
import 'coach_attendance.dart';
import 'coach_evaluation.dart';
import 'coach_player.dart';
import 'session_context.dart';

String _playerCountLabel(BuildContext context, int count) => localized(
  context,
  Intl.plural(
    count,
    locale: 'ar',
    zero: 'لا يوجد لاعبون',
    one: 'لاعب واحد',
    two: 'لاعبان',
    few: '$count لاعبين',
    many: '$count لاعبًا',
    other: '$count لاعب',
  ),
  count == 1 ? '1 player' : '$count players',
);

class CoachHome extends StatefulWidget {
  const CoachHome({super.key, required this.auth, required this.accountPage});
  final AuthController auth;
  final Widget accountPage;

  @override
  State<CoachHome> createState() => _CoachHomeState();
}

class _CoachHomeState extends State<CoachHome> {
  String? get currentIdentity => widget.auth.session?.selected?.role == 'Coach'
      ? coachContext(widget.auth)
      : null;
  late String? identity = currentIdentity;
  late Future<Object?> groups;
  late Future<Object?> nextSession;

  @override
  void initState() {
    super.initState();
    read();
    widget.auth.addListener(contextChanged);
  }

  void read() {
    groups = identity == null
        ? Future.value()
        : widget.auth.request('GET', '/api/v1/coach/groups');
    nextSession = identity == null
        ? Future.value()
        : widget.auth
              .request('GET', '/api/v1/attendance/sessions?nearestOnly=true')
              .onError((Object error, StackTrace _) => error);
  }

  void contextChanged() {
    final current = currentIdentity;
    if (!mounted || current == identity) return;
    setState(() {
      identity = current;
      read();
    });
  }

  @override
  void didUpdateWidget(covariant CoachHome oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.auth == widget.auth) return;
    oldWidget.auth.removeListener(contextChanged);
    identity = currentIdentity;
    read();
    widget.auth.addListener(contextChanged);
  }

  @override
  void dispose() {
    widget.auth.removeListener(contextChanged);
    super.dispose();
  }

  Future<void> refresh() async {
    setState(read);
    await Future.wait([groups, nextSession])
        .then<void>((_) {}, onError: (Object _) {});
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    showAppBar: false,
    title: 'الرئيسية · المدرب',
    child: identity == null
        ? const SizedBox.shrink()
        : RefreshIndicator(
            onRefresh: refresh,
            child: FutureBuilder<Object?>(
              key: ValueKey(identity),
              future: groups,
              builder: (context, snapshot) {
                final assigned = rows(snapshot.data ?? const []);
                return gList([
                  RoleHomeIntro(
                    title: localized(context, 'تدريباتي', 'Training'),
                  ),
                  StaffHomeAttendanceAction(
                    key: ObjectKey(groups),
                    auth: widget.auth,
                  ),
                  if (snapshot.connectionState != ConnectionState.done)
                    const PendingHomeSection(label: 'جارٍ تحميل مجموعاتك')
                  else if (snapshot.hasError)
                    GCard(
                      child: Column(
                        children: [
                          const ErrorNotice('تعذر تحميل مجموعاتك.'),
                          TextButton(
                            onPressed: refresh,
                            child: const UiText('إعادة المحاولة'),
                          ),
                        ],
                      ),
                    )
                  else ...[
                    if (assigned.isEmpty)
                      const EmptyMessage('لا توجد مجموعات مسندة إليك حاليًا.')
                    else ...[
                      GCard(
                        child: Row(
                          children: [
                            Expanded(
                              child: coachMetric(
                                '${assigned.length}',
                                'مجموعات مسندة',
                              ),
                            ),
                            const SizedBox(width: 12),
                            Expanded(
                              child: coachMetric(
                                '${assigned.fold<int>(0, (total, group) => total + ((group['playerCount'] as num?)?.toInt() ?? 0))}',
                                'تسجيلات اللاعبين',
                              ),
                            ),
                          ],
                        ),
                      ),
                      const ExperienceSection(
                        'مجموعاتي',
                        Icons.groups_outlined,
                      ),
                      for (final group in assigned)
                        CoachGroupCard(auth: widget.auth, group: group),
                      FutureBuilder<Object?>(
                        future: nextSession,
                        builder: (context, snapshot) {
                          final result = snapshot.data;
                          if (result is ApiFailure) {
                            return GCard(
                              child: Column(
                                children: [
                                  ErrorNotice(result.message),
                                  TextButton(
                                    onPressed: refresh,
                                    child: const UiText(
                                      'إعادة تحميل الحصة القادمة',
                                    ),
                                  ),
                                ],
                              ),
                            );
                          }
                          final next = result is List
                              ? rows(result).firstOrNull
                              : null;
                          if (next == null) return const SizedBox.shrink();
                          final group = assigned
                              .where(
                                (item) => item['id'] == next['trainingGroupId'],
                              )
                              .firstOrNull;
                          return CoachSessionCard(
                            session: next,
                            featured: true,
                            compact: true,
                            showAction: group != null,
                            onOpen: () {
                              if (group != null) {
                                openPage(
                                  context,
                                  CoachGroup(auth: widget.auth, group: group),
                                );
                              }
                            },
                          );
                        },
                      ),
                    ],
                  ],
                  NextMatchStrip(auth: widget.auth),
                  NewsStrip(auth: widget.auth),
                ]);
              },
            ),
          ),
  );

  Widget coachMetric(String value, String label) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      UiText(
        value,
        style: const TextStyle(fontSize: 25, fontWeight: FontWeight.w800),
      ),
      UiText(label, style: const TextStyle(fontSize: 12)),
    ],
  );
}

class CoachGroupCard extends StatelessWidget {
  const CoachGroupCard({super.key, required this.auth, required this.group});
  final AuthController auth;
  final Json group;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final stackCount = MediaQuery.textScalerOf(context).scale(1) > 1.4;
    final countTag = switch (group['playerCount']) {
      final int count => DirectoryTag(
        _playerCountLabel(context, count),
        icon: Icons.person_outline,
      ),
      _ => null,
    };
    final schedules = <(Object?, Object?), Set<String>>{};
    for (final rule in rows(group['recurring'] ?? const [])) {
      schedules
          .putIfAbsent((rule['startTime'], rule['endTime']), () => <String>{})
          .add(arabicWeekday(rule['dayOfWeek']));
    }
    void openGroup() => openPage(context, CoachGroup(auth: auth, group: group));
    return GCard(
      key: ValueKey('coach-group-${group['id']}'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            crossAxisAlignment: stackCount
                ? CrossAxisAlignment.start
                : CrossAxisAlignment.center,
            children: [
              Container(
                padding: const EdgeInsets.all(10),
                decoration: BoxDecoration(
                  color: colors.primary.withValues(alpha: .12),
                  borderRadius: BorderRadius.circular(12),
                ),
                child: Icon(Icons.groups_outlined, color: colors.primary),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    UiText(
                      textOf(group['arabicName']),
                      style: const TextStyle(
                        fontSize: 16,
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                    UiText(
                      [
                        group['branchName'],
                        group['sportName'],
                      ].whereType<String>().join(' · '),
                      style: TextStyle(
                        fontSize: 12,
                        color: colors.onSurfaceVariant,
                      ),
                    ),
                    if (stackCount && countTag != null) ...[
                      const SizedBox(height: 6),
                      countTag,
                    ],
                  ],
                ),
              ),
              if (!stackCount && countTag != null) countTag,
            ],
          ),
          const SizedBox(height: 12),
          for (final entry in schedules.entries)
            Padding(
              padding: const EdgeInsets.only(bottom: 6),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Icon(
                    Icons.calendar_month_outlined,
                    size: 17,
                    color: colors.primary,
                  ),
                  const SizedBox(width: 7),
                  Expanded(
                    child: UiText(
                      '${entry.value.join('، ')} · ${trainingTimeOf(entry.key.$1)} إلى ${trainingTimeOf(entry.key.$2)}',
                      style: TextStyle(
                        fontSize: 12,
                        color: colors.onSurfaceVariant,
                      ),
                    ),
                  ),
                ],
              ),
            ),
          if (schedules.isEmpty)
            UiText(
              'لم تُحدد مواعيد أسبوعية بعد.',
              style: TextStyle(fontSize: 12, color: colors.onSurfaceVariant),
            ),
          const SizedBox(height: 6),
          FilledButton.tonalIcon(
            onPressed: openGroup,
            icon: const Icon(Icons.arrow_forward_rounded, size: 18),
            label: const UiText('فتح المجموعة'),
          ),
        ],
      ),
    );
  }
}

class CoachSessionCard extends StatelessWidget {
  final Json session;
  final bool featured, compact;
  final VoidCallback onOpen;
  final String? openLabel;
  final bool showAction;
  const CoachSessionCard({
    super.key,
    required this.session,
    required this.onOpen,
    this.featured = false,
    this.compact = false,
    this.openLabel,
    this.showAction = true,
  });
  static String time(Object? v) => trainingTimeOf(v);

  @override
  Widget build(BuildContext context) {
    final c = Theme.of(context).colorScheme;
    final date = DateTime.tryParse(textOf(session['sessionDate']));
    if (compact) {
      return GCard(
        key: ValueKey('coach-session-${session['id']}'),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Icon(Icons.event_available_outlined, color: c.primary),
                const SizedBox(width: 8),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const UiText(
                        'الحصة القادمة',
                        style: TextStyle(fontWeight: FontWeight.w700),
                      ),
                      UiText(
                        '${session['group']} · ${sessionDateLabel(session['sessionDate'])} · ${trainingTimeOf(session['startTime'])}',
                        style: TextStyle(
                          fontSize: 12,
                          color: c.onSurfaceVariant,
                        ),
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
            if (showAction)
              FilledButton.tonal(
                onPressed: onOpen,
                child: UiText(openLabel ?? 'عرض المجموعة ولاعبيها'),
              ),
          ],
        ),
      );
    }
    final large = MediaQuery.textScalerOf(context).scale(1) > 1.4;
    return GCard(
      key: ValueKey('coach-session-${session['id']}'),
      onTap: showAction ? onOpen : null,
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
          UiText(switch (session['playerCount']) {
            final int count =>
              '${_playerCountLabel(context, count)} ${localized(context, 'في الحصة', 'in this session')}',
            _ => localized(
              context,
              'عدد اللاعبين غير متاح',
              'Player count unavailable',
            ),
          }, style: TextStyle(fontSize: 12, color: c.onSurfaceVariant)),
          const SizedBox(height: 12),
          if (showAction && featured)
            FilledButton.icon(
              onPressed: onOpen,
              icon: const Icon(Icons.arrow_forward_rounded, size: 18),
              label: UiText(openLabel ?? 'عرض المجموعة ولاعبيها'),
            )
          else if (showAction)
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
  late Future<
    ({
      Json? group,
      Json options,
      List<Json> sessions,
      Json? roster,
      String? attendanceError,
    })
  >
  request = load();

  Future<
    ({
      Json? group,
      Json options,
      List<Json> sessions,
      Json? roster,
      String? attendanceError,
    })
  >
  load() async {
    final groupId = textOf(widget.group['id']);
    final values = await Future.wait([
      widget.auth.request('GET', '/api/v1/coach/groups'),
      widget.auth.request('GET', '/api/v1/evaluations/options'),
    ]);
    final group = rows(values[0])
        .where((row) => row['id'] == groupId)
        .firstOrNull;
    final options = obj(values[1]);
    if (group == null ||
        identity != coachContext(widget.auth) ||
        options['canManageAttendance'] != true) {
      return (
        group: group,
        options: options,
        sessions: <Json>[],
        roster: null,
        attendanceError: null,
      );
    }
    try {
      final sessions =
          rows(
                await widget.auth.request(
                  'GET',
                  '/api/v1/attendance/sessions?groupId=${Uri.encodeQueryComponent(groupId)}',
                ),
              )
              .where(
                (session) =>
                    session['trainingGroupId'] == groupId &&
                    session['status'] != 'Cancelled',
              )
              .toList()
            ..sort(
              (a, b) =>
                  textOf(a['startTime']).compareTo(textOf(b['startTime'])),
            );
      final session = sessions.firstOrNull;
      final roster = session == null || identity != coachContext(widget.auth)
          ? null
          : obj(
              await widget.auth.request(
                'GET',
                '/api/v1/attendance/sessions/${session['id']}/players',
              ),
            );
      return (
        group: group,
        options: options,
        sessions: sessions,
        roster: roster,
        attendanceError: null,
      );
    } on ApiFailure catch (error) {
      return (
        group: group,
        options: options,
        sessions: <Json>[],
        roster: null,
        attendanceError: error.message,
      );
    }
  }

  @override
  void dispose() {
    search.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (identity == null || identity != coachContext(widget.auth)) {
        return const AppScaffold(title: 'المجموعة', child: SizedBox.shrink());
      }
      return FutureBuilder(
        future: request,
        builder: (context, snapshot) {
          final data = snapshot.data;
          final group = data?.group;
          final title = textOf(
            group?['arabicName'] ?? widget.group['arabicName'],
          );
          Widget page(Widget child) => AppScaffold(title: title, child: child);
          if (snapshot.connectionState != ConnectionState.done) {
            return page(const Center(child: CircularProgressIndicator()));
          }
          if (snapshot.hasError) {
            return page(
              Center(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    ErrorNotice(
                      snapshot.error is ApiFailure
                          ? (snapshot.error as ApiFailure).message
                          : 'تعذر تحميل المجموعة.',
                    ),
                    OutlinedButton(
                      onPressed: () => setState(() => request = load()),
                      child: const UiText('إعادة المحاولة'),
                    ),
                  ],
                ),
              ),
            );
          }
          if (data == null || group == null) {
            return page(
              const Center(child: UiText('لم تعد هذه المجموعة مسندة إليك.')),
            );
          }
          final players = rows(data.options['enrollments'])
              .where((player) => player['groupId'] == group['id'])
              .toList();
          final header = groupHeader(group, players.length);
          if (data.roster?['canManageAttendance'] == true) {
            return CoachAttendance(
              auth: widget.auth,
              sessionId: textOf(data.sessions.first['id']),
              title: title,
              groupHeader: [
                ...header,
                for (final session in data.sessions.skip(1))
                  GLink(
                    'كشف حصة ${trainingTimeOf(session['startTime'])}',
                    Icons.fact_check_outlined,
                    () => openPage(
                      context,
                      CoachAttendance(
                        auth: widget.auth,
                        sessionId: textOf(session['id']),
                      ),
                    ),
                  ),
              ],
              initialRoster: data.roster,
              groupEnrollmentIds: players
                  .map((player) => textOf(player['id']))
                  .toSet(),
            );
          }
          final filtered = players.where(
            (player) => playerMatches(player, search.text),
          );
          return page(
            gList([
              ...header,
              if (data.attendanceError != null)
                GCard(
                  child: Column(
                    children: [
                      ErrorNotice(data.attendanceError!),
                      TextButton(
                        onPressed: () => setState(() => request = load()),
                        child: const UiText('إعادة تحميل كشف الحضور'),
                      ),
                    ],
                  ),
                ),
              const ExperienceSection('لاعبو المجموعة', Icons.people_outline),
              TextField(
                controller: search,
                onChanged: (_) => setState(() {}),
                decoration: const InputDecoration(
                  labelText: 'بحث بالاسم أو كود اللاعب',
                  prefixIcon: Icon(Icons.search),
                ),
              ),
              if (filtered.isEmpty)
                EmptyMessage(
                  players.isEmpty
                      ? 'لا يوجد لاعبون في هذه المجموعة.'
                      : 'لا توجد نتائج لهذا البحث.',
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
                      groupId: textOf(group['id']),
                    ),
                  ),
                  onEvaluate: data.options['canEvaluate'] == true
                      ? () => openPage(
                          context,
                          CoachEvaluation(
                            auth: widget.auth,
                            enrollment: player,
                          ),
                        )
                      : null,
                ),
            ]),
          );
        },
      );
    },
  );

  List<Widget> groupHeader(Json group, int playerCount) {
    final brand = BrandScope.maybeOf(context);
    return [
      GCard(
        child: Row(
          children: [
            Icon(
              Icons.groups_outlined,
              color: Theme.of(context).colorScheme.primary,
            ),
            const SizedBox(width: 10),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(
                    '${_playerCountLabel(context, playerCount)} ${localized(context, 'في المجموعة', 'in this group')}',
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  UiText(
                    [
                      group['branchName'],
                      group['sportName'],
                    ].whereType<String>().join(' · '),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
      if (brand == null || brand.features.contains('media'))
        OutlinedButton.icon(
          onPressed: () => openPage(
            context,
            UploadSessionMedia(auth: widget.auth, groupId: textOf(group['id'])),
          ),
          icon: const Icon(Icons.add_photo_alternate_outlined),
          label: const UiText('رفع صور وفيديوهات المجموعة'),
        ),
      ExpansionTile(
        tilePadding: EdgeInsets.zero,
        leading: const Icon(Icons.calendar_month_outlined),
        title: const UiText('المواعيد والتواصل'),
        children: [
          WeeklyGroupSchedule(schedules: rows(group['recurring'] ?? const [])),
          AudienceCommunicationLink(auth: widget.auth, id: textOf(group['id'])),
        ],
      ),
    ];
  }
}
