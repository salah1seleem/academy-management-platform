import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/coach/coach_attendance.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

void main() {
  testWidgets(
    'historical attendance opens the recorded occurrence rather than today or the current group',
    (t) async {
      final calls = <String>[];
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        calls.add('$m $p');
        if (p.contains('/attendance/history?')) {
          return [
            {
              'trainingSessionId': 'historical-session',
              'sessionDate': '2026-10-14',
              'group': 'المجموعة السابقة',
              'status': 'Absent',
              'sessionConsumed': false,
            },
          ];
        }
        if (p.endsWith('/historical-session/players')) {
          return {
            'session': {
              'id': 'historical-session',
              'sessionDate': '2026-10-14',
              'group': 'المجموعة السابقة',
            },
            'players': [],
            'canManageAttendance': false,
          };
        }
        return [];
      });
      await t.pumpWidget(
        app(
          NativePlayerSessions(
            auth: auth,
            playerId: 'player',
            groupId: 'current-group',
            sportId: 'sport',
            branchId: 'branch',
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(
        find.text('الأربعاء 14 أكتوبر 2026 · المجموعة السابقة'),
        findsOneWidget,
      );
      expect(find.text('غائب'), findsOneWidget);
      expect(find.text('لم تُخصم حصة من الرصيد'), findsOneWidget);
      await t.tap(find.text('فتح كشف هذه الحصة'));
      await t.pumpAndSettle();
      expect(
        t.widget<CoachAttendance>(find.byType(CoachAttendance)).sessionId,
        'historical-session',
      );
      expect(
        calls,
        contains('GET /api/v1/attendance/sessions/historical-session/players'),
      );
      expect(calls.any((p) => !p.startsWith('GET ')), false);
      expect(calls.any((p) => p.contains('groupId=current-group')), false);
      expect(find.text('حفظ الحضور'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
