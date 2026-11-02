import 'package:academy_mobile/features/guardian/guardian_visits.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

void main() {
  testWidgets(
    'Guardian own child visit history remains separate and read-only at 200% text',
    (tester) async {
      tester.view.physicalSize = const Size(390, 1300);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final calls = <String>[];
      final auth = await authenticate('Guardian', (m, p, b, t) async {
        calls.add('$m $p');
        return {
          'visits': [
            {
              'facility': 'مرفق اصطناعي',
              'resource': 'A',
              'enteredAtUtc': '2026-10-12T08:00:00Z',
              'staleOpen': true,
            },
          ],
        };
      });
      await tester.pumpWidget(
        app(GuardianVisits(auth: auth, playerId: 'p'), scale: 2),
      );
      await tester.pumpAndSettle();
      expect(find.text('مرفق اصطناعي · A'), findsOneWidget);
      expect(find.textContaining('بانتظار مراجعة الأكاديمية'), findsOneWidget);
      expect(find.text('تسجيل الدخول'), findsNothing);
      expect(calls, ['GET /api/v1/guardian/children/p/visits']);
      expect(tester.takeException(), isNull);
      auth.dispose();
    },
  );
}
