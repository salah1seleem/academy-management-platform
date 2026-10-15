import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/theme/appearance.dart';
import '../shared/matches.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/widgets/app_widgets.dart';
import '../guardian/guardian_ui.dart';
import 'coach_attendance.dart';
import 'coach_evaluation.dart';
import '../shared/private_media.dart';
import '../shared/academy_news.dart';
import '../shared/staff_amounts.dart';
import 'device_check_in.dart';

class CoachHome extends StatefulWidget {
  final AuthController auth;
  final Widget accountPage;
  const CoachHome({super.key, required this.auth, required this.accountPage});
  @override
  State<CoachHome> createState() => _CoachHomeState();
}

class _CoachHomeState extends State<CoachHome> {
  late Future<List<Object?>> request;
  String? identity;
  String? get currentIdentity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}';
  Future<List<Object?>> load() {
    if (currentIdentity == null) {
      return Future.value([
        <Json>[],
        <Json>[],
        <String, Object?>{'enrollments': <Json>[]},
      ]);
    }
    return Future.wait([
      widget.auth.request('GET', '/api/v1/coach/groups'),
      widget.auth.request(
        'GET',
        '/api/v1/attendance/sessions?upcomingOnly=true',
      ),
      widget.auth.request('GET', '/api/v1/evaluations/options'),
    ]);
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
      // The FutureBuilder shows the failed request and its retry action.
    }
  }

  void openSession(Json s) => openPage(
    context,
    CoachAttendance(
      auth: widget.auth,
      sessionId: textOf(s['id']),
      title: localized(context, 'تفاصيل الحصة', 'Session details'),
    ),
  );
  @override
  Widget build(BuildContext context) => AppScaffold(
    title: localized(context, 'الرئيسية · المدرب', 'Coach home'),
    actions: [
      IconButton(
        tooltip: ui(context, 'الحساب'),
        onPressed: () => openPage(context, widget.accountPage),
        icon: const Icon(Icons.person_outline),
      ),
    ],
    child: FutureBuilder<List<Object?>>(
      future: request,
      builder: (context, snapshot) {
        if (currentIdentity == null) return const SizedBox.shrink();
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
                          'تعذر تحميل بيانات المدرب.',
                          'Unable to load your work.',
                        ),
                ),
                OutlinedButton.icon(
                  onPressed: () => setState(() {
                    request = load();
                  }),
                  icon: const Icon(Icons.refresh),
                  label: UiText(
                    localized(context, 'إعادة المحاولة', 'Try again'),
                  ),
                ),
              ],
            ),
          );
        }
        final groups = rows(snapshot.data![0]),
            sessions =
                rows(snapshot.data![1])
                    .where(
                      (s) =>
                          s['status'] != 'Cancelled' && s['status'] != 'Held',
                    )
                    .toList()
                  ..sort(
                    (a, b) => '${a['sessionDate']} ${a['startTime']}'.compareTo(
                      '${b['sessionDate']} ${b['startTime']}',
                    ),
                  );
        final enrollments = rows(obj(snapshot.data![2])['enrollments']);
        return RefreshIndicator(
          onRefresh: refresh,
          child: ListView(
            padding: const EdgeInsets.all(18),
            physics: const AlwaysScrollableScrollPhysics(),
            children: [
              Row(
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        UiText(
                          localized(
                            context,
                            'أهلًا، ${widget.auth.session?.displayName ?? ''}',
                            'Welcome, ${widget.auth.session?.displayName ?? ''}',
                          ),
                          style: Theme.of(context).textTheme.titleLarge,
                        ),
                        const SizedBox(height: 6),
                        UiText(
                          localized(
                            context,
                            'حصص واضحة، وتطور تتابعه خطوة بخطوة',
                            'Your sessions and player development, in focus.',
                          ),
                          style: Theme.of(context).textTheme.bodyMedium,
                        ),
                      ],
                    ),
                  ),
                  Icon(
                    Icons.sports_outlined,
                    color: Theme.of(context).colorScheme.primary,
                    size: 32,
                  ),
                ],
              ),
              const SizedBox(height: 18),
              if (sessions.isNotEmpty)
                CoachSessionCard(
                  session: sessions.first,
                  featured: true,
                  onOpen: () => openSession(sessions.first),
                )
              else
                AppCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const Icon(Icons.event_available_outlined, size: 32),
                      UiText(
                        localized(
                          context,
                          'لا توجد حصة قادمة خلال 14 يومًا',
                          'No upcoming session in the next 14 days',
                        ),
                        style: Theme.of(context).textTheme.titleMedium,
                      ),
                      UiText(
                        localized(
                          context,
                          'مجموعاتك وملفات اللاعبين ما زالت متاحة أدناه.',
                          'Your groups and player profiles remain available below.',
                        ),
                      ),
                    ],
                  ),
                ),
              const SizedBox(height: 18),
              Row(
                children: [
                  Expanded(
                    child: UiText(
                      localized(
                        context,
                        'المجموعات المسندة',
                        'Assigned groups',
                      ),
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                  ),
                  Chip(
                    avatar: const Icon(Icons.groups_outlined, size: 18),
                    label: UiText('${groups.length}'),
                  ),
                ],
              ),
              if (groups.isEmpty)
                UiText(
                  localized(
                    context,
                    'لا توجد مجموعات مسندة حاليًا.',
                    'No groups assigned yet.',
                  ),
                ),
              for (final group in groups)
                Card(
                  child: ListTile(
                    contentPadding: const EdgeInsets.all(14),
                    leading: CircleAvatar(
                      backgroundColor: Theme.of(context)
                          .colorScheme
                          .primaryContainer,
                      child: Icon(
                        Icons.groups_outlined,
                        color: Theme.of(context).colorScheme.onPrimaryContainer,
                      ),
                    ),
                    title: UiText(
                      textOf(group['arabicName']),
                      style: const TextStyle(
                        fontSize: 16,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    subtitle: UiText(
                      localized(
                        context,
                        '${enrollments.where((e) => e['groupId'] == group['id']).length} تسجيل لاعب',
                        '${enrollments.where((e) => e['groupId'] == group['id']).length} player enrollments',
                      ),
                    ),
                    trailing: const Icon(Icons.chevron_left),
                    onTap: () => openPage(
                      context,
                      CoachGroup(
                        auth: widget.auth,
                        group: group,
                        sessions: sessions
                            .where((s) => s['trainingGroupId'] == group['id'])
                            .toList(),
                        enrollments: enrollments
                            .where((e) => e['groupId'] == group['id'])
                            .toList(),
                      ),
                    ),
                  ),
                ),
              const SizedBox(height: 18),
              if (sessions.length > 1) ...[
                UiText(
                  localized(
                    context,
                    'بعد الحصة الأقرب',
                    'After your nearest session',
                  ),
                  style: Theme.of(context).textTheme.titleLarge,
                ),
                const SizedBox(height: 8),
                for (final session in sessions.skip(1).take(5))
                  CoachSessionCard(
                    session: session,
                    onOpen: () => openSession(session),
                  ),
              ],
              NewsStrip(auth: widget.auth),
              NextMatchStrip(auth: widget.auth),
              const SizedBox(height: 18),
              UiText(
                localized(context, 'خدماتي', 'My tools'),
                style: Theme.of(context).textTheme.titleLarge,
              ),
              OutlinedButton.icon(
                onPressed: () =>
                    openPage(context, DeviceCheckIn(auth: widget.auth)),
                icon: const Icon(Icons.fingerprint),
                label: const UiText('تسجيل حضوري بالبصمة والموقع'),
              ),
              OutlinedButton.icon(
                onPressed: () =>
                    openPage(context, StaffAmounts(auth: widget.auth)),
                icon: const Icon(Icons.add_card_outlined),
                label: const UiText('طلب مبلغ إضافي'),
              ),
            ],
          ),
        );
      },
    ),
  );
}

class CoachSessionCard extends StatelessWidget {
  final Json session;
  final bool featured;
  final VoidCallback onOpen;
  const CoachSessionCard({
    super.key,
    required this.session,
    required this.onOpen,
    this.featured = false,
  });
  static String time(Object? v) {
    final value = textOf(v);
    return value.length >= 5 ? value.substring(0, 5) : value;
  }

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final date = DateTime.tryParse(textOf(session['sessionDate']));
    return Container(
      key: ValueKey('coach-session-${session['id']}'),
      margin: const EdgeInsets.only(bottom: 12),
      padding: EdgeInsets.all(featured ? 20 : 14),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(featured ? 24 : 16),
        border: Border.all(color: colors.primary.withValues(alpha: .22)),
        gradient: LinearGradient(
          colors: featured
              ? [
                  Color.alphaBlend(
                    colors.primary.withValues(alpha: .18),
                    colors.surface,
                  ),
                  colors.surface,
                ]
              : [colors.surface, colors.surface],
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (featured) ...[
            UiText(
              localized(context, 'حصتك الأقرب', 'Your nearest session'),
              style: TextStyle(
                fontSize: 14,
                fontWeight: FontWeight.w700,
                color: colors.onSurface,
              ),
            ),
            const SizedBox(height: 14),
          ],
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Container(
                width: 60,
                padding: const EdgeInsets.symmetric(
                  vertical: 10,
                  horizontal: 6,
                ),
                decoration: BoxDecoration(
                  color: colors.surface,
                  borderRadius: BorderRadius.circular(14),
                ),
                child: Column(
                  children: [
                    const Icon(Icons.calendar_today_outlined, size: 19),
                    UiText(
                      date?.day.toString() ?? '—',
                      style: TextStyle(
                        fontSize: 28,
                        fontWeight: FontWeight.w800,
                        color: colors.primary,
                      ),
                    ),
                    UiText(
                      date == null ? '—' : '${date.month}',
                      style: const TextStyle(fontSize: 14),
                    ),
                    if (date != null)
                      UiText(
                        '${date.year}',
                        style: const TextStyle(fontSize: 14),
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
                      style: TextStyle(
                        fontSize: featured ? 20 : 16,
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                    const SizedBox(height: 6),
                    UiText(
                      '${time(session['startTime'])} — ${time(session['endTime'])}',
                      textDirection: TextDirection.ltr,
                      style: TextStyle(
                        fontSize: featured ? 24 : 16,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    const SizedBox(height: 6),
                    UiText(
                      '${session['branch'] ?? ''} · ${session['sport'] ?? ''}',
                      style: const TextStyle(fontSize: 14),
                    ),
                    if (session['resource'] != null)
                      UiText(
                        textOf(session['resource']),
                        style: const TextStyle(fontSize: 14),
                      ),
                    UiText(
                      localized(
                        context,
                        '${session['playerCount'] ?? '—'} لاعب في الحصة',
                        '${session['playerCount'] ?? '—'} players in this session',
                      ),
                      style: const TextStyle(fontSize: 14),
                    ),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          if (featured)
            FilledButton.icon(
              onPressed: onOpen,
              icon: const Icon(Icons.arrow_forward),
              label: UiText(localized(context, 'فتح الحصة', 'Open session')),
            )
          else
            TextButton.icon(
              onPressed: onOpen,
              icon: const Icon(Icons.chevron_left),
              label: UiText(
                localized(context, 'تفاصيل الحصة', 'Session details'),
              ),
            ),
        ],
      ),
    );
  }
}

class CoachGroup extends StatelessWidget {
  final AuthController auth;
  final Json group;
  final List<Json> sessions, enrollments;
  const CoachGroup({
    super.key,
    required this.auth,
    required this.group,
    required this.sessions,
    required this.enrollments,
  });
  @override
  Widget build(BuildContext context) => AppScaffold(
    title: textOf(group['arabicName']),
    child: ListView(
      padding: const EdgeInsets.all(18),
      children: [
        if (sessions.isNotEmpty)
          AppCard(
            child: Column(
              children: [
                UiText(
                  '${sessions.first['branch']} · ${sessions.first['sport']}',
                ),
                UiText('${sessions.first['sessionDate']}'),
                UiText(
                  '${CoachSessionCard.time(sessions.first['startTime'])} — ${CoachSessionCard.time(sessions.first['endTime'])}',
                  textDirection: TextDirection.ltr,
                ),
              ],
            ),
          ),
        UiText(
          'اللاعبون (${enrollments.length})',
          style: Theme.of(context).textTheme.titleLarge,
        ),
        for (final player in enrollments)
          ListTile(
            leading: const CircleAvatar(child: Icon(Icons.person_outline)),
            title: UiText(textOf(player['player'])),
            subtitle: UiText(textOf(player['playerCode'])),
            trailing: IconButton(
              tooltip: ui(context, 'تقييم اللاعب'),
              icon: const Icon(Icons.assessment_outlined),
              onPressed: () => openPage(
                context,
                CoachEvaluation(auth: auth, enrollment: player),
              ),
            ),
          ),
        const SizedBox(height: 12),
        OutlinedButton.icon(
          onPressed: () => openPage(
            context,
            UploadSessionMedia(auth: auth, groupId: textOf(group['id'])),
          ),
          icon: const Icon(Icons.add_photo_alternate_outlined),
          label: const UiText('رفع صور وفيديوهات للحصة'),
        ),
        if (sessions.isNotEmpty)
          FilledButton.icon(
            onPressed: sessions.isEmpty
                ? null
                : () => openPage(
                    context,
                    CoachAttendance(
                      auth: auth,
                      sessionId: textOf(sessions.first['id']),
                      title: localized(
                        context,
                        'تفاصيل الحصة',
                        'Session details',
                      ),
                    ),
                  ),
            icon: const Icon(Icons.fact_check_outlined),
            label: UiText(
              localized(context, 'فتح الحصة الأقرب', 'Open nearest session'),
            ),
          ),
      ],
    ),
  );
}
