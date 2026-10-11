import 'package:academy_mobile/features/shared/staff_amounts.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'coach_test.dart' show coach, shell;

void main() {
  testWidgets(
    'Additional amount needs a configured reason and submits only own request',
    (tester) async {
      final calls = <Object?>[];
      final auth = await coach((m, p, b, t) async {
        if (m == 'POST') {
          calls.add(b);
          return {'id': 'requested'};
        }
        return {
          'configured': true,
          'employee': {'id': 'own', 'name': 'مدرب تجريبي'},
          'reasons': [
            {'id': 'overtime', 'name': 'ساعة إضافية'},
          ],
          'requests': [],
        };
      });
      await tester.pumpWidget(shell(StaffAmounts(auth: auth)));
      await tester.pumpAndSettle();
      await tester.tap(find.text('إرسال للمراجعة'));
      await tester.pumpAndSettle();
      expect(calls, isEmpty);
      await tester.enterText(find.byType(TextFormField), '150');
      await tester.tap(find.byType(DropdownButtonFormField<String>));
      await tester.pumpAndSettle();
      await tester.tap(find.text('ساعة إضافية').last);
      await tester.pumpAndSettle();
      await tester.tap(find.text('إرسال للمراجعة'));
      await tester.pumpAndSettle();
      expect(calls.single, {'reasonId': 'overtime', 'amount': 150.0});
      expect(find.text('تم إرسال الطلب للمراجعة.'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets('Staff without linked employee cannot create payroll requests', (
    tester,
  ) async {
    final auth = await coach((m, p, b, t) async => {'configured': false});
    await tester.pumpWidget(shell(StaffAmounts(auth: auth)));
    await tester.pumpAndSettle();
    expect(find.textContaining('لم يتم ربط سجل الموظف'), findsOneWidget);
    expect(find.text('إرسال للمراجعة'), findsNothing);
  });
}
