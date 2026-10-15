import 'package:academy_mobile/features/shared/contract_history.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'coach_test.dart' show coach, shell;

void main() {
  testWidgets(
    'Staff reads only employment dates and future versions without financial mutation controls',
    (tester) async {
      final calls = <String>[];
      final auth = await coach((m, p, b, t) async {
        calls.add('$m $p');
        return {
          'configured': true,
          'contractStart': '2000-01-01',
          'contractEnd': null,
          'hasVerifiedHistory': true,
          'items': [
            {
              'effectiveFrom': '2026-11-01',
              'contractStart': '2000-01-01',
              'contractEnd': '2026-11-15',
            },
          ],
        };
      });
      await tester.pumpWidget(shell(StaffContractHistory(auth: auth)));
      await tester.pumpAndSettle();
      expect(find.text('2000-01-01 — دائم'), findsOneWidget);
      expect(find.text('2000-01-01 — 2026-11-15'), findsOneWidget);
      expect(find.byType(TextFormField), findsNothing);
      expect(find.byType(FilledButton), findsNothing);
      expect(calls, ['GET /api/v1/staff/payroll/contract-versions']);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'Legacy contract is explicitly unverified rather than fabricated history',
    (tester) async {
      final auth = await coach(
        (m, p, b, t) async => {
          'contractStart': '2025-01-01',
          'contractEnd': null,
          'items': [],
        },
      );
      await tester.pumpWidget(shell(StaffContractHistory(auth: auth)));
      await tester.pumpAndSettle();
      expect(
        find.text('التاريخ السابق غير مؤرخ؛ لم نفترض سجلًا سابقًا موثقًا.'),
        findsOneWidget,
      );
      expect(tester.takeException(), isNull);
    },
  );
}
