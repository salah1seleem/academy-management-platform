import 'package:academy_mobile/features/shared/facility_board.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

void main() {
  for (final allowed in [true, false]) {
    testWidgets('Facilities link respects actual permission $allowed', (
      tester,
    ) async {
      final auth = await authenticate(
        'AcademyAdmin',
        (m, p, b, t) async => allowed ? ['facilities.read'] : [],
      );
      await tester.pumpWidget(
        app(Scaffold(body: FacilityBoardLink(auth: auth))),
      );
      await tester.pumpAndSettle();
      expect(
        find.text('المرافق والحارات'),
        allowed ? findsOneWidget : findsNothing,
      );
      auth.dispose();
    });
  }
  testWidgets(
    'Lane board displays actual booking not payment or physical count, at 200% text',
    (tester) async {
      tester.view.physicalSize = const Size(390, 1300);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final auth = await authenticate(
        'AcademyOwner',
        (m, p, b, t) async => {
          'date': '2026-10-11',
          'bookings': [
            {
              'id': 'b',
              'resource': 'A',
              'facility': 'مرفق اختبار',
              'space': 'مسبح اختبار',
              'groupName': 'مجموعة سباحة',
              'coach': 'مدرب بديل الحصة',
              'plannedCoach': 'المدرب الأصلي',
              'startTime': '17:00:00',
              'endTime': '18:00:00',
              'reservedSeats': 6,
              'capacity': 8,
            },
          ],
          'closures': [],
        },
      );
      await tester.pumpWidget(app(FacilityBoardPage(auth: auth), scale: 2));
      await tester.pumpAndSettle();
      expect(find.text('مجموعة سباحة'), findsOneWidget);
      expect(find.text('المقاعد: 6 / 8'), findsOneWidget);
      expect(find.text('لم تربط حصة مؤرخة'), findsOneWidget);
      expect(find.text('المدرب المخطط: المدرب الأصلي'), findsOneWidget);
      expect(find.textContaining('مدرب بديل الحصة'), findsOneWidget);
      expect(tester.takeException(), isNull);
      auth.dispose();
    },
  );
}
