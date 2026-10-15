import 'package:academy_mobile/features/shared/salary_history.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'coach_test.dart' show coach, shell;

void main() {
  testWidgets(
    'Own salary history is read only and distinguishes rate from payout',
    (tester) async {
      final calls = <String>[];
      final auth = await coach((m, p, b, t) async {
        calls.add('$m $p');
        return {
          'currentSalary': 3000,
          'currency': 'EGP',
          'hasVerifiedHistory': true,
          'items': [
            {
              'monthlySalary': 3500,
              'currency': 'EGP',
              'effectiveFrom': '2026-11-01',
              'revision': 2,
            },
          ],
        };
      });
      await tester.pumpWidget(shell(StaffSalaryHistory(auth: auth)));
      await tester.pumpAndSettle();
      expect(find.text('الراتب الشهري الحالي — ليس صرفًا'), findsOneWidget);
      expect(find.text('3000 EGP'), findsOneWidget);
      expect(find.text('3500 EGP'), findsOneWidget);
      expect(find.byType(TextFormField), findsNothing);
      expect(find.byType(FilledButton), findsNothing);
      expect(calls, ['GET /api/v1/staff/payroll/salary-versions']);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'An undated legacy rate is disclosed rather than fabricated history',
    (tester) async {
      final auth = await coach(
        (m, p, b, t) async => {
          'currentSalary': 3000,
          'currency': 'EGP',
          'hasVerifiedHistory': false,
          'items': [],
        },
      );
      await tester.pumpWidget(shell(StaffSalaryHistory(auth: auth)));
      await tester.pumpAndSettle();
      expect(find.textContaining('المرجع القديم غير مؤرخ'), findsOneWidget);
      expect(find.textContaining('نسخة'), findsNothing);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets('No employee linkage never shows a made up salary', (
    tester,
  ) async {
    final auth = await coach((m, p, b, t) async => {'configured': false});
    await tester.pumpWidget(shell(StaffSalaryHistory(auth: auth)));
    await tester.pumpAndSettle();
    expect(find.text('لم يربط سجل الموظف بحسابك بعد.'), findsOneWidget);
    expect(find.textContaining('null'), findsNothing);
    expect(tester.takeException(), isNull);
  });
}
