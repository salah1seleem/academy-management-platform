import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/coach/coach_attendance.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

void main() {
  testWidgets(
    'Admin requests a versioned correction, retains failed draft, and never writes closed-day attendance directly',
    (tester) async {
      tester.view.physicalSize = const Size(390, 1100);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      Object? sent;
      var attempts = 0;
      final methods = <String>[];
      final auth = await authenticate('AcademyAdmin', (m, p, b, t) async {
        methods.add(m);
        if (m == 'POST') {
          sent = b;
          if (attempts++ == 0) {
            throw const ApiFailure(503, 'تعذر الاتصال مؤقتًا؛ الطلب لم يؤكد.');
          }
          return {'id': 'correction'};
        }
        return {
          'canManageAttendance': false,
          'canRequestCorrection': true,
          'canEvaluate': false,
          'session': {
            'group': 'مجموعة اصطناعية',
            'sessionDate': '2026-09-28',
            'startTime': '10:00',
          },
          'players': [
            {
              'sportEnrollmentId': 'e',
              'arabicName': 'طفل اصطناعي',
              'attendanceStatus': 'NotRecorded',
              'version': 41,
              'playerCode': 'DEMO-1',
            },
          ],
        };
      });
      await tester.pumpWidget(
        app(CoachAttendance(auth: auth, sessionId: 's'), scale: 2),
      );
      await tester.pumpAndSettle();
      expect(find.text('حفظ الحضور'), findsNothing);
      await tester.ensureVisible(find.text('طلب تصحيح يوم سابق'));
      await tester.tap(find.text('طلب تصحيح يوم سابق'));
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.byType(DropdownButtonFormField<String>));
      await tester.tap(find.byType(DropdownButtonFormField<String>));
      await tester.pumpAndSettle();
      await tester.tap(find.text('غائب').last);
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.byType(TextField));
      await tester.enterText(find.byType(TextField), 'مراجعة ورقية اصطناعية');
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.text('إرسال الطلب للمالك'),
        200,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.tap(find.text('إرسال الطلب للمالك'));
      await tester.pumpAndSettle();
      expect(find.text('تعذر الاتصال مؤقتًا؛ الطلب لم يؤكد.'), findsOneWidget);
      expect(find.text('مراجعة ورقية اصطناعية'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.scrollUntilVisible(
        find.text('إرسال الطلب للمالك'),
        200,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.tap(find.text('إرسال الطلب للمالك'));
      await tester.pumpAndSettle();
      expect(find.textContaining('لم يتغير الحضور بعد'), findsOneWidget);
      expect(sent, {
        'kind': 'Players',
        'reason': 'مراجعة ورقية اصطناعية',
        'items': [
          {'subjectId': 'e', 'status': 'Absent', 'expectedVersion': 41},
        ],
      });
      expect(methods.contains('PUT'), isFalse);
      expect(tester.takeException(), isNull);
      auth.dispose();
    },
  );
}
