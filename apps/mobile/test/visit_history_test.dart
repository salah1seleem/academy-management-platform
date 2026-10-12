import 'package:academy_mobile/features/shared/visit_history.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter/material.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

void main() {
  testWidgets(
    'Authorized staff history is read-only and distinguishes stale companion visit at 200% text',
    (tester) async {
      tester.view.physicalSize = const Size(390, 1300);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final calls = <String>[];
      final auth = await authenticate('AcademyAdmin', (m, p, b, t) async {
        calls.add('$m $p');
        return {
          'visits': [
            {
              'name': 'مرافق اصطناعي',
              'personKind': 'Companion',
              'enteredAtUtc': '2026-10-12T08:00:00Z',
              'staleOpen': true,
            },
          ],
        };
      });
      await tester.pumpWidget(
        app(VisitHistoryPage(auth: auth, bookingId: 'b'), scale: 2),
      );
      await tester.pumpAndSettle();
      expect(find.text('مرافق اصطناعي'), findsOneWidget);
      expect(find.text('مرافق غير متدرب'), findsOneWidget);
      expect(find.textContaining('يحتاج مراجعة'), findsOneWidget);
      expect(find.text('تسجيل الدخول'), findsNothing);
      expect(calls, ['GET /api/v1/facilities/bookings/b/visits']);
      expect(tester.takeException(), isNull);
      auth.dispose();
    },
  );
}
