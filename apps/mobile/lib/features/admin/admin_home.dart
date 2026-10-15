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
  const AdminHome({super.key, required this.auth});
  @override
  State<AdminHome> createState() => _AdminHomeState();
}

class _AdminHomeState extends State<AdminHome> {
  String? identity;
  String? get currentIdentity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late Future<Json> request;
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
  void didUpdateWidget(AdminHome old) {
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

  Future<Json> load() async {
    if (currentIdentity == null) return {};
    final permissions = (await widget.auth.request(
      'GET',
      '/api/v1/my-permissions',
    ) as List).cast<String>();
    final result = <String, dynamic>{'permissions': permissions};
    for (final item in [
      ('attendance.read', 'sessions', '/api/v1/attendance/sessions'),
      (
        'payment.read',
        'payments',
        '/api/v1/manage/payment-requests?status=Pending&allDates=true',
      ),
    ]) {
      if (!permissions.contains(item.$1)) continue;
      try {
        result[item.$2] = await widget.auth.request('GET', item.$3);
      } on ApiFailure catch (e) {
        result['${item.$2}Error'] = e.message;
      }
    }
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
    child: FutureBuilder<Json>(
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
        final d = snapshot.data!,
            permissions = (d['permissions'] as List).cast<String>();
        final sessions = d['sessions'] is List
            ? todaySessions(d['sessions'])
            : <Json>[];
        final payment = d['payments'] is Map
            ? obj(d['payments'])
            : <String, dynamic>{};
        return RefreshIndicator(
          onRefresh: refresh,
          child: gList([
            Row(
              children: [
                const AcademyCrest(size: 44),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      UiText(
                        widget.auth.session!.selected!.academyName,
                        style: Theme.of(context).textTheme.titleLarge,
                      ),
                      UiText(
                        localized(
                          context,
                          'أهلًا، ${widget.auth.session!.displayName}',
                          'Welcome, ${widget.auth.session!.displayName}',
                        ),
                        style: const TextStyle(fontSize: 14),
                      ),
                    ],
                  ),
                ),
              ],
            ),
            const SizedBox(height: 16),
            if (permissions.contains('attendance.read')) ...[
              GLink(
                localized(context, 'حضور اللاعبين', 'Player attendance'),
                Icons.fact_check_outlined,
                () => openPage(context, NativeSessions(auth: widget.auth)),
              ),
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
                UiText(
                  localized(
                    context,
                    'بتوقيت الأكاديمية، مرتبة حسب البداية',
                    'Academy day, ordered by start time',
                  ),
                  style: Theme.of(context).textTheme.bodySmall,
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
            if (permissions.contains('payment.read')) ...[
              const SizedBox(height: 12),
              GLink(
                localized(context, 'طلبات الدفع', 'Payment requests'),
                Icons.receipt_long_outlined,
                () =>
                    openPage(context, NativePaymentRequests(auth: widget.auth)),
              ),
              if (d['paymentsError'] != null)
                ErrorNotice(textOf(d['paymentsError']))
              else
                GCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      UiText(
                        localized(
                          context,
                          '${payment['pending'] ?? '—'} طلب بانتظار المراجعة',
                          '${payment['pending'] ?? '—'} requests awaiting review',
                        ),
                        style: Theme.of(context).textTheme.titleMedium,
                      ),
                      UiText(
                        localized(
                          context,
                          'كل الطلبات المعلقة، دون إخفاء الأقدم بفلتر اليوم',
                          'All pending requests, including older ones',
                        ),
                        style: const TextStyle(fontSize: 14),
                      ),
                    ],
                  ),
                ),
            ],
            if (!permissions.contains('attendance.read') &&
                !permissions.contains('payment.read'))
              const EmptyMessage('لا توجد مهام متاحة بصلاحيات هذا الحساب.'),
            if (d['sessionsError'] != null || d['paymentsError'] != null)
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
                subtitle: UiText(
                  '${day(s['sessionDate'])} · ${s['startTime']} — ${s['endTime']}\n${s['branch']} · ${s['sport']}',
                ),
                isThreeLine: true,
                trailing: const Icon(Icons.chevron_left),
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
