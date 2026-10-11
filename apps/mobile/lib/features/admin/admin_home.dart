import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../coach/coach_attendance.dart';
import '../guardian/guardian_ui.dart';
import '../shared/role_navigation.dart';

/// An operational home, deliberately not a copy of the owner's financial home.
/// Capability checks control UI; every API also enforces its own permission.
class AdminHome extends StatelessWidget {
  final AuthController auth;
  const AdminHome({super.key, required this.auth});

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'يوم الأكاديمية', 'Academy day'),
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/my-permissions',
      builder: (context, value) {
        final permissions = value is List ? value.cast<String>() : <String>[];
        return gList([
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(
                  Icons.waving_hand_outlined,
                  color: Theme.of(context).colorScheme.primary,
                ),
                const SizedBox(height: 8),
                UiText(
                  localized(
                    context,
                    'أهلًا، ${auth.session?.displayName ?? ""}',
                    'Welcome, ${auth.session?.displayName ?? ""}',
                  ),
                  style: Theme.of(context).textTheme.headlineSmall,
                ),
                UiText(
                  localized(
                    context,
                    'حضور اليوم وطلبات الأسر في مكان واحد',
                    'Today’s attendance and family requests in one place',
                  ),
                ),
              ],
            ),
          ),
          if (permissions.contains('attendance.read'))
            GLink(
              localized(context, 'حضور اللاعبين', 'Player attendance'),
              Icons.fact_check_outlined,
              () => openPage(context, NativeSessions(auth: auth)),
            ),
          if (permissions.contains('payment.read'))
            GLink(
              localized(context, 'طلبات الدفع', 'Payment requests'),
              Icons.receipt_long_outlined,
              () => openPage(context, NativePaymentRequests(auth: auth)),
            ),
          if (!permissions.contains('attendance.read') &&
              !permissions.contains('payment.read'))
            const EmptyMessage('لا توجد مهام متاحة بصلاحيات هذا الحساب.'),
        ]);
      },
    ),
  );
}

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
        final sessions = rows(value);
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
