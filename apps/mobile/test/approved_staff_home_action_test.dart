import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/shared/staff_home_action.dart';
import 'package:academy_mobile/features/shared/staff_work_periods.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as fixtures;

void main() {
  for (final role in ['Coach', 'AcademyAdmin']) {
    for (final enabled in [true, false]) {
      testWidgets(
        '$role self attendance action follows branch setting $enabled',
        (t) async {
          final paths = <String>[];
          final auth = await fixtures.authenticate(role, (
            m,
            p,
            b,
            token,
          ) async {
            paths.add(p);
            return {
              'configured': false,
              'branches': [
                {'id': 'branch', 'enabled': enabled, 'configured': false},
              ],
              'hasOpenAttendance': false,
            };
          });
          await t.pumpWidget(
            fixtures.app(Scaffold(body: StaffHomeAttendanceAction(auth: auth))),
          );
          await t.pumpAndSettle();
          expect(
            find.text('حضور وانصراف'),
            enabled ? findsOneWidget : findsNothing,
          );
          expect(
            paths.every((p) => p == '/api/v1/staff/passkeys/attendance'),
            isTrue,
          );
          await auth.logout();
          await t.pumpAndSettle();
          expect(find.text('حضور وانصراف'), findsNothing);
          await t.pumpWidget(const SizedBox());
          auth.dispose();
        },
      );
    }
    testWidgets('$role refreshes the attendance entry after resuming', (
      t,
    ) async {
      var next = Completer<Object?>();
      final auth = await fixtures.authenticate(
        role,
        (m, p, b, token) => next.future,
      );
      final card = find.text('حضور وانصراف');
      Map<String, dynamic> status({required bool enabled, bool open = false}) =>
          {
            'branches': [
              {'id': 'branch', 'enabled': enabled},
            ],
            'hasOpenAttendance': open,
          };
      Future<void> resume() async {
        t.binding.handleAppLifecycleStateChanged(AppLifecycleState.paused);
        t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
        await t.pump();
      }

      await t.pumpWidget(
        fixtures.app(Scaffold(body: StaffHomeAttendanceAction(auth: auth))),
      );
      next.complete(status(enabled: true));
      await t.pumpAndSettle();
      expect(card, findsOneWidget);

      next = Completer<Object?>();
      await resume();
      expect(card, findsNothing);
      next.complete(status(enabled: false));
      await t.pumpAndSettle();
      expect(card, findsNothing);

      next = Completer<Object?>();
      await resume();
      next.complete(status(enabled: false, open: true));
      await t.pumpAndSettle();
      expect(card, findsOneWidget);
      expect(find.text('لديك تسجيل حضور مفتوح'), findsOneWidget);

      next = Completer<Object?>();
      await resume();
      next.completeError(const ApiFailure(503, 'تعذر تحميل حالة الدوام.'));
      await t.pumpAndSettle();
      expect(card, findsNothing);

      next = Completer<Object?>();
      await resume();
      next.complete(status(enabled: true));
      await t.pumpAndSettle();
      expect(card, findsOneWidget);

      next = Completer<Object?>();
      await resume();
      await auth.logout();
      next.complete(status(enabled: true));
      await t.pumpAndSettle();
      expect(card, findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    });
  }
  testWidgets(
    'attendance history shows saved present and absent days and does not infer absence from an empty day',
    (t) async {
      final auth = await fixtures.authenticate(
        'Coach',
        (m, p, b, token) async => {
          'configured': true,
          'today': '2026-10-21',
          'workDate': '2026-10-21',
          'items': <Object>[],
          'attendanceStatus': null,
          'attendanceSummary': {
            'from': '2026-10-01',
            'to': '2026-10-31',
            'presentDays': 2,
            'absentDays': 1,
            'recordedDays': 3,
          },
          'totalWorkedSeconds': null,
        },
      );
      await t.pumpWidget(fixtures.app(StaffWorkPeriodsPage(auth: auth)));
      await t.pumpAndSettle();
      expect(find.text('أيام حضور مسجلة'), findsOneWidget);
      expect(find.text('أيام غياب مسجلة'), findsOneWidget);
      expect(find.text('لم تسجل حالة حضور لهذا اليوم.'), findsOneWidget);
      expect(find.text('اليوم المختار: غياب مسجل'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
