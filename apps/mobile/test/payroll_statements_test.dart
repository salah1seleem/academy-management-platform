import 'package:academy_mobile/features/shared/payroll_statements.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'coach_test.dart' show coach, shell;

void main() {
  testWidgets(
    'Reviewed opening zero is visible and is not a fabricated monthly salary',
    (tester) async {
      final calls = <String>[];
      final auth = await coach((m, p, b, t) async {
        calls.add('$m $p');
        return {
          'configured': true,
          'asOf': '2026-10-15',
          'currency': 'EGP',
          'items': [],
          'totalDue': 0,
          'totalPaid': 0,
          'remaining': 0,
          'priorRemaining': 0,
          'currentRemaining': 0,
          'openingBalanceVerified': true,
          'opening': {
            'asOf': '2026-10-01',
            'amount': 0,
            'paid': 0,
            'remaining': 0,
            'payments': [],
          },
        };
      });
      await tester.pumpWidget(shell(StaffPayrollStatements(auth: auth)));
      await tester.pumpAndSettle();
      expect(find.text('افتتاح موثق'), findsOneWidget);
      expect(
        find.textContaining('داخل الإجمالي وليس راتب شهر'),
        findsOneWidget,
      );
      expect(find.text('الرصيد القديم: 0 EGP'), findsOneWidget);
      expect(find.textContaining('لا كشف معتمد بعد'), findsNothing);
      expect(find.byType(TextFormField), findsNothing);
      expect(find.byType(FilledButton), findsNothing);
      expect(calls, ['GET /api/v1/staff/payroll/account']);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'Own advance before any approved earnings is visible without a false salary or payout',
    (tester) async {
      final auth = await coach(
        (m, p, b, t) async => {
          'configured': true,
          'asOf': '2026-10-15',
          'currency': 'EGP',
          'items': [],
          'recordedAdvanceTotal': 750,
          'unallocatedAdvances': 750,
          'advanceApplied': 0,
        },
      );
      await tester.pumpWidget(shell(StaffPayrollStatements(auth: auth)));
      await tester.pumpAndSettle();
      expect(find.text('سبق صرفها: 750 EGP'), findsOneWidget);
      expect(find.text('غير المخصص: 750 EGP'), findsOneWidget);
      expect(find.textContaining('ليس صرفًا جديدًا'), findsOneWidget);
      expect(find.byType(TextFormField), findsNothing);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'Own approved monthly dues show 3600 earned, 2500 paid, 1100 remaining with no mutation',
    (tester) async {
      final calls = <String>[];
      final auth = await coach((m, p, b, t) async {
        calls.add('$m $p');
        return {
          'configured': true,
          'asOf': '2026-10-15',
          'currency': 'EGP',
          'totalDue': 6600,
          'totalPaid': 3500,
          'remaining': 3100,
          'priorRemaining': 2000,
          'currentRemaining': 1100,
          'currentMonthApproved': true,
          'items': [
            {
              'month': '2026-10-01',
              'currency': 'EGP',
              'netDue': 3600,
              'paid': 2500,
              'remaining': 1100,
              'baseDue': 3000,
              'deductions': 0,
              'additions': 600,
              'payments': [
                {'amount': 2500},
              ],
            },
            {
              'month': '2026-09-01',
              'netDue': 3000,
              'paid': 1000,
              'remaining': 2000,
              'baseDue': 3000,
              'deductions': 0,
              'additions': 0,
              'payments': [],
            },
          ],
        };
      });
      await tester.pumpWidget(shell(StaffPayrollStatements(auth: auth)));
      await tester.pumpAndSettle();
      expect(find.text('المستحق: 3600 EGP'), findsOneWidget);
      expect(find.text('المسدد: 2500 EGP'), findsOneWidget);
      expect(find.text('المتبقي: 1100 EGP'), findsOneWidget);
      expect(find.text('المتبقي المعروف: 3100 EGP'), findsOneWidget);
      expect(find.text('من أشهر سابقة: 2000 EGP'), findsOneWidget);
      expect(find.byType(TextFormField), findsNothing);
      expect(find.byType(FilledButton), findsNothing);
      expect(calls, ['GET /api/v1/staff/payroll/account']);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'No approved statement does not imply zero wages or a verified opening',
    (tester) async {
      final auth = await coach(
        (m, p, b, t) async => {'configured': true, 'items': []},
      );
      await tester.pumpWidget(shell(StaffPayrollStatements(auth: auth)));
      await tester.pumpAndSettle();
      expect(
        find.textContaining('هذا لا يعني أن مستحقاتك صفر'),
        findsOneWidget,
      );
      expect(find.textContaining('الأرصدة الافتتاحية والسلف'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );
}
