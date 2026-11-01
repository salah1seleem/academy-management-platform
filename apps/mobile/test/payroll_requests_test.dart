import 'package:academy_mobile/features/owner/payroll_requests.dart';
import 'package:academy_mobile/features/shared/staff_amounts.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'coach_test.dart' show coach, shell;

void main() {
  test('Arabic advance amount is exact and bounded', () {
    expect(payrollAmount('٢٥٠٫٥٠'), 250.5);
    expect(payrollAmount('12.345'), isNull);
    expect(payrollAmount('0'), isNull);
    expect(payrollAmount('100000001'), isNull);
    expect(
      payrollRequestStatus({'kind': 'Advance', 'status': 'Approved'}),
      contains('بانتظار الصرف'),
    );
  });
  testWidgets('Staff advance requires its amount and remains an own request', (
    tester,
  ) async {
    final calls = <dynamic>[];
    final auth = await coach((m, p, b, t) async {
      if (m == 'POST') {
        calls.add(b);
        return {'id': 'request'};
      }
      return {
        'configured': true,
        'employee': {'id': 'own', 'name': 'موظف عرض', 'currency': 'EGP'},
        'reasons': [
          {'id': 'reason', 'name': 'ظرف شخصي'},
        ],
        'requests': [],
      };
    });
    await tester.pumpWidget(shell(StaffAmounts(auth: auth)));
    await tester.pumpAndSettle();
    await tester.tap(find.text('سلفة'));
    await tester.pumpAndSettle();
    await tester.tap(find.byType(DropdownButtonFormField<String>));
    await tester.pumpAndSettle();
    await tester.tap(find.text('ظرف شخصي').last);
    await tester.pumpAndSettle();
    await tester.tap(find.text('إرسال للمراجعة'));
    await tester.pumpAndSettle();
    expect(calls, isEmpty);
    await tester.enterText(
      find.widgetWithText(TextFormField, 'قيمة السلفة (EGP)'),
      '٢٥٠',
    );
    await tester.tap(find.text('إرسال للمراجعة'));
    await tester.pumpAndSettle();
    expect(calls.single, {
      'reasonId': 'reason',
      'kind': 'Advance',
      'requestedAmount': 250,
    });
    expect(find.text('تم إرسال الطلب للمراجعة.'), findsOneWidget);
  });
  testWidgets(
    'Owner reviews request separately from recording advance payment',
    (tester) async {
      var state = 'Pending';
      final calls = <String>[];
      final auth = await coach((m, p, b, t) async {
        if (m == 'POST') {
          calls.add(p);
          state = p.endsWith('/review') ? 'Approved' : 'Paid';
          return {'id': 'request'};
        }
        if (p.contains('/calendar')) return {'today': '2026-10-29'};
        if (p.endsWith('/reasons')) return <dynamic>[];
        return [
          {
            'id': 'request',
            'name': 'موظف عرض',
            'kind': 'Advance',
            'requestedAmount': 250,
            'amount': state == 'Pending' ? null : 250,
            'currency': 'EGP',
            'reasonName': 'ظرف شخصي',
            'status': state,
            'dueDate': '2026-10-29',
          },
        ];
      });
      await tester.pumpWidget(shell(OwnerPayrollRequests(auth: auth)));
      await tester.pumpAndSettle();
      await tester.tap(find.text('مراجعة الطلب'));
      await tester.pumpAndSettle();
      expect(find.textContaining('لا يسجل صرفها'), findsOneWidget);
      await tester.tap(find.text('حفظ القرار'));
      await tester.pumpAndSettle();
      expect(calls.single, endsWith('/review'));
      await tester.tap(find.text('معتمدة'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('تسجيل صرف السلفة'));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.widgetWithText(TextFormField, 'مرجع الصرف'),
        'تحويل عرض ١',
      );
      await tester.tap(find.text('تسجيل الصرف'));
      await tester.pumpAndSettle();
      expect(calls.length, 2);
      expect(calls.last, endsWith('/record-advance'));
      expect(tester.takeException(), isNull);
    },
  );
}
