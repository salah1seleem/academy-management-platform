import '../shared/modern_home.dart';
import '../shared/subscription_home.dart';
import '../owner/owner_quick_operations.dart';
import '../../core/widgets/home_skeleton.dart';
import '../shared/staff_home_action.dart';
import '../shared/academy_news.dart';
import '../shared/assigned_followups.dart';
import '../shared/experience_ui.dart';
import '../owner/owner_followups.dart';
import 'admin_home_overview.dart';
import 'native_product_orders.dart';
import '../shared/native_match_management.dart';

import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/api/api_client.dart';
import '../../core/widgets/app_widgets.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../coach/coach_attendance.dart';
import '../coach/coach_home.dart';
import '../guardian/guardian_ui.dart';
import '../shared/role_navigation.dart';

/// Operational reads retain the existing server permissions and branch/sport scope.
class AdminHome extends StatefulWidget {
  final AuthController auth;
  final Widget? accountPage;
  const AdminHome({super.key, required this.auth, this.accountPage});
  @override
  State<AdminHome> createState() => _AdminHomeState();
}

class _AdminHomeState extends State<AdminHome> {
  String? identity;
  String? get currentIdentity =>
      widget.auth.session?.selected?.role != 'AcademyAdmin'
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late Future<Json> request;
  Json? partial;
  int generation = 0;
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
      request = identity == null ? Future.value(<String, dynamic>{}) : load();
    });
  }

  @override
  void didUpdateWidget(AdminHome old) {
    super.didUpdateWidget(old);
    if (old.auth != widget.auth) {
      old.auth.removeListener(contextChanged);
      widget.auth.addListener(contextChanged);
      identity = currentIdentity;
      request = identity == null ? Future.value(<String, dynamic>{}) : load();
    }
  }

  @override
  void dispose() {
    widget.auth.removeListener(contextChanged);
    super.dispose();
  }

  Future<Json> load() async {
    final scope = currentIdentity, attempt = ++generation;
    partial = null;
    if (scope == null) return {};
    final permissions = (await widget.auth.request(
      'GET',
      '/api/v1/my-permissions',
    ) as List).cast<String>();
    if (scope != currentIdentity || attempt != generation) return {};
    final result = <String, dynamic>{'permissions': permissions};
    final sources = [
      ('attendance.read', 'sessions', '/api/v1/attendance/sessions'),
      (
        'payment.read',
        'payments',
        '/api/v1/manage/payment-requests?status=Pending&allDates=true',
      ),
      (
        'followups.read',
        'followups',
        '/api/v1/my-followups?state=Open&allDates=true',
      ),
    ].where((s) => permissions.contains(s.$1)).toList();
    for (final source in sources) {
      result['${source.$2}Pending'] = true;
    }
    if (mounted) setState(() => partial = result);
    await Future.wait(
      sources.map((source) async {
        try {
          final value = await widget.auth.request('GET', source.$3);
          if (scope != currentIdentity || attempt != generation) return;
          if (source.$2 == 'followups' &&
              (value is! Map || value['items'] is! List)) {
            throw const FormatException('Invalid follow-up list');
          }
          result[source.$2] = value;
        } catch (e) {
          if (scope != currentIdentity || attempt != generation) return;
          result['${source.$2}Error'] = e is ApiFailure
              ? e.message
              : 'تعذر تحميل هذا القسم. أعد المحاولة.';
        }
        if (scope != currentIdentity || attempt != generation || !mounted) {
          return;
        }
        result['${source.$2}Pending'] = false;
        setState(() => partial = Map<String, dynamic>.from(result));
      }),
    );
    return result;
  }

  Future<void> refresh() async {
    final next = load();
    setState(() {
      request = next;
    });
    try {
      await next;
    } catch (_) {
      /* The failed read is rendered with retry. */
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
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'يوم الأكاديمية', 'Academy day'),
    showAppBar: false,
    child: FutureBuilder<Json>(
      future: request,
      builder: (context, snapshot) {
        if (currentIdentity == null) return const SizedBox.shrink();
        if (partial == null &&
            snapshot.connectionState != ConnectionState.done) {
          return const HomeSkeleton(role: 'AcademyAdmin');
        }
        if (snapshot.hasError && partial == null) {
          return Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                ErrorNotice(
                  snapshot.error is ApiFailure
                      ? (snapshot.error as ApiFailure).message
                      : localized(
                          context,
                          'تعذر تحميل يوم الأكاديمية.',
                          'Unable to load the academy day.',
                        ),
                ),
                OutlinedButton.icon(
                  onPressed: refresh,
                  icon: const Icon(Icons.refresh),
                  label: const UiText('إعادة المحاولة'),
                ),
              ],
            ),
          );
        }
        final d = partial ?? snapshot.data!,
            permissions = (d['permissions'] as List).cast<String>();
        final sessions = d['sessions'] is List
            ? todaySessions(d['sessions'])
            : <Json>[];
        final followups = d['followups'] is Map
            ? obj(d['followups'])
            : <String, dynamic>{};
        final assigned = followups['items'] is List
            ? rows(followups['items'])
            : <Json>[];
        final pendingSessions = sessions
            .where(
              (s) =>
                  (s['playerCount'] as num? ?? 0) >
                  (s['recorded'] as num? ?? 0),
            )
            .length;
        final payment = d['payments'] is Map
            ? obj(d['payments'])
            : <String, dynamic>{};
        return RefreshIndicator(
          onRefresh: refresh,
          child: gList([
            const RoleHomeIntro(
              title: 'يوم الأكاديمية',
              subtitle: 'المهام والحصص التي تحتاج متابعتك',
            ),

            NativeOperationActions(
              key: ValueKey('admin-primary-$identity'),
              auth: widget.auth,
              primaryOnly: true,
            ),
            NativeMatchEntry(
              key: ValueKey('admin-matches-$identity'),
              auth: widget.auth,
              asLink: true,
            ),
            if (permissions.contains('subscription.read'))
              SubscriptionHomePanel(
                key: ValueKey('admin-subscriptions-$identity'),
                auth: widget.auth,
              ),
            StaffHomeAttendanceAction(
              key: ObjectKey(request),
              auth: widget.auth,
            ),
            if (d['sessionsPending'] == true)
              const PendingHomeSection(label: 'جارٍ تحميل حصص اليوم'),
            if (permissions.contains('attendance.read') &&
                d['sessionsError'] == null &&
                d['sessionsPending'] != true &&
                sessions.isNotEmpty)
              AdminAttendanceOverview(sessions: sessions),
            const ExperienceSection('مهامي اليوم', Icons.task_alt_outlined),
            if (permissions.contains('attendance.read') &&
                d['sessionsPending'] != true)
              AdminHomeTask(
                key: const ValueKey('admin-home-attendance-task'),
                title: 'حضور اللاعبين',
                detail: 'حصص اليوم التي تحتاج استكمال تسجيل اللاعبين',
                count: d['sessionsError'] == null ? pendingSessions : null,
                action: 'عرض الحصص',
                onOpen: () =>
                    openPage(context, NativeSessions(auth: widget.auth)),
              ),
            if (d['paymentsPending'] == true)
              const PendingHomeSection(label: 'جارٍ تحميل طلبات الدفع'),
            if (permissions.contains('payment.read') &&
                d['paymentsPending'] != true)
              AdminHomeTask(
                key: const ValueKey('admin-home-payments-task'),
                title: 'طلبات الدفع',
                detail: 'الطلبات المعلقة من كل التواريخ',
                count: d['paymentsError'] == null && payment['pending'] is num
                    ? (payment['pending'] as num).toInt()
                    : null,
                action: 'عرض الطلبات',
                onOpen: () async {
                  final scope = currentIdentity;
                  await openPage(
                    context,
                    NativePaymentRequests(auth: widget.auth),
                  );
                  if (mounted && scope == currentIdentity) await refresh();
                },
              ),
            if (permissions.contains('content.catalog.manage'))
              NativeProductStockSection(
                key: ValueKey('admin-inventory-$identity'),
                auth: widget.auth,
              ),
            if (d['followupsPending'] == true)
              const PendingHomeSection(label: 'جارٍ تحميل المتابعات المسندة'),
            if (permissions.contains('followups.read') &&
                d['followupsPending'] != true) ...[
              const ExperienceSection(
                'متابعات مسندة إليّ',
                Icons.assignment_outlined,
              ),
              if (d['followupsError'] != null)
                const ErrorNotice('تعذر تحميل المتابعات.'),
              if (d['followupsError'] == null && assigned.isEmpty)
                const UiText('لا توجد متابعات مفتوحة.'),
              if (assigned.isNotEmpty) ...[
                for (final item in assigned.take(3))
                  GCard(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        UiText(
                          item['source'] is Map
                              ? textOf(
                                  obj(item['source'])['name'] ?? 'متابعة سجل',
                                )
                              : 'متابعة سجل',
                          style: const TextStyle(
                            fontSize: 15,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                        UiText(
                          followUpConditions[item['condition']] ??
                              'متابعة مسندة',
                          style: const TextStyle(fontSize: 12),
                        ),
                        UiText(
                          'الحالة: ${followUpStates[item['state']] ?? 'متابعة'} · الموعد: ${day(item['dueOn'])}',
                          style: const TextStyle(fontSize: 12),
                        ),
                        OutlinedButton(
                          key: ValueKey('admin-home-followup-${item['id']}'),
                          onPressed: () async {
                            await openPage(
                              context,
                              AssignedFollowUpsPage(
                                auth: widget.auth,
                                id: textOf(item['id']),
                              ),
                            );
                            if (mounted && currentIdentity == identity) {
                              await refresh();
                            }
                          },
                          child: const UiText('عرض المتابعة'),
                        ),
                      ],
                    ),
                  ),
              ],
              GLink(
                'متابعاتي المسندة',
                Icons.assignment_ind_outlined,
                () async {
                  await openPage(
                    context,
                    AssignedFollowUpsPage(
                      auth: widget.auth,
                      initialAllDates: true,
                    ),
                  );
                  if (mounted && currentIdentity == identity) await refresh();
                },
              ),
            ],
            if (permissions.contains('people.manage'))
              GLink(
                'اللاعبون',
                Icons.groups_outlined,
                () => openPage(
                  context,
                  CoachPlayers(auth: widget.auth, admin: true),
                ),
              ),
            if (permissions.contains('attendance.read')) ...[
              if (d['sessionsError'] != null)
                ErrorNotice(textOf(d['sessionsError']))
              else ...[
                Heading(
                  localized(
                    context,
                    'جدول اليوم · ${sessions.length} حصة',
                    'Today’s schedule · ${sessions.length} sessions',
                  ),
                ),
                const SizedBox(height: 8),
                for (final s in sessions.take(3))
                  CoachSessionCard(session: s, onOpen: () => openSession(s)),
                if (sessions.isEmpty) const EmptyMessage('لا توجد حصص اليوم.'),
                if (sessions.length > 3)
                  TextButton(
                    onPressed: () =>
                        openPage(context, NativeSessions(auth: widget.auth)),
                    child: UiText(
                      localized(
                        context,
                        'عرض كل حصص اليوم (${sessions.length})',
                        'View all today’s sessions (${sessions.length})',
                      ),
                    ),
                  ),
              ],
            ],
            if (permissions.contains('payment.read') &&
                d['paymentsError'] != null)
              ErrorNotice(textOf(d['paymentsError'])),
            if (!permissions.contains('attendance.read') &&
                !permissions.contains('payment.read') &&
                !permissions.contains('people.manage') &&
                !permissions.contains('followups.read'))
              const EmptyMessage('لا توجد مهام متاحة بصلاحيات هذا الحساب.'),
            NewsStrip(auth: widget.auth),
            if (d['sessionsError'] != null ||
                d['paymentsError'] != null ||
                d['followupsError'] != null)
              OutlinedButton.icon(
                onPressed: refresh,
                icon: const Icon(Icons.refresh),
                label: const UiText('إعادة المحاولة'),
              ),
          ]),
        );
      },
    ),
  );
}

List<Json> todaySessions(Object? value) =>
    rows(value).where((s) => s['status'] != 'Cancelled').toList()..sort(
      (a, b) => '${a['sessionDate']} ${a['startTime']}'.compareTo(
        '${b['sessionDate']} ${b['startTime']}',
      ),
    );

class NativeSessions extends StatelessWidget {
  final AuthController auth;
  const NativeSessions({super.key, required this.auth});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'حصص اليوم', 'Today’s sessions'),
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/attendance/sessions',
      builder: (context, value) {
        final sessions = todaySessions(value);
        return gList([
          UiText(
            localized(
              context,
              '${sessions.length} حصة مرتبة حسب الوقت',
              '${sessions.length} sessions in time order',
            ),
            style: Theme.of(context).textTheme.titleMedium,
          ),
          for (final s in sessions)
            Card(
              child: ListTile(
                leading: const Icon(Icons.schedule_outlined),
                title: UiText(textOf(s['group'])),
                subtitle: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    UiText(
                      '${sessionDateLabel(s['sessionDate'])} · ${trainingTimeRange(s['startTime'], s['endTime'])}',
                      textDirection: TextDirection.rtl,
                    ),
                    UiText('${s['branch']} · ${s['sport']}'),
                  ],
                ),
                isThreeLine: true,
                trailing: const Icon(Icons.chevron_right),
                onTap: () => openPage(
                  context,
                  CoachAttendance(auth: auth, sessionId: textOf(s['id'])),
                ),
              ),
            ),
          if (sessions.isEmpty) const EmptyMessage('لا توجد حصص اليوم.'),
        ]);
      },
    ),
  );
}
