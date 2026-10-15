import 'package:academy_mobile/features/shared/coach_agreements.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'coach_test.dart' show coach, shell;

void main() {
  testWidgets(
    'A disputed private delivery shows review instead of null or zero money',
    (tester) async {
      final auth = await coach(
        (m, p, b, t) async => {
          'items': [
            {
              'branch': 'الرحاب',
              'sport': 'كرة القدم',
              'serviceDate': '2026-10-01',
              'requiresReview': true,
              'earned': null,
              'contingent': null,
              'currency': 'EGP',
              'variableMethod': 'Percent',
              'variableValue': 40,
            },
          ],
        },
      );
      await tester.pumpWidget(shell(CoachPrivateServicesPage(auth: auth)));
      await tester.pumpAndSettle();
      expect(
        find.text('تعارض يتطلب مراجعة — العمولة غير محسوبة'),
        findsOneWidget,
      );
      expect(find.textContaining('null EGP'), findsNothing);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'Private assessment is self-only read, separated from posted salary and never editable',
    (tester) async {
      final calls = <String>[];
      final auth = await coach((m, p, b, t) async {
        calls.add('$m $p');
        return {
          'postedToPayroll': true,
          'items': [
            {
              'branch': 'الرحاب',
              'sport': 'كرة القدم',
              'serviceDate': '2026-10-01',
              'earned': 50,
              'postedAmount': 25,
              'pendingDelta': 25,
              'contingent': 50,
              'currency': 'EGP',
              'variableMethod': 'Percent',
              'variableValue': 40,
            },
          ],
        };
      });
      await tester.pumpWidget(shell(CoachPrivateServicesPage(auth: auth)));
      await tester.pumpAndSettle();
      expect(find.text('مستوفية للشروط: 50 EGP'), findsOneWidget);
      expect(find.text('معلقة على التحصيل: 50 EGP'), findsOneWidget);
      expect(
        find.text('المرحّل إلى الرواتب — ليس سدادًا: 25 EGP'),
        findsOneWidget,
      );
      expect(find.text('فرق التقييم: 25 EGP'), findsOneWidget);
      expect(find.byType(TextFormField), findsNothing);
      expect(find.byType(FilledButton), findsNothing);
      expect(calls, ['GET /api/v1/staff/payroll/private-services']);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'Coach reads approved own terms without salary, note, ID input or mutation controls',
    (tester) async {
      final calls = <String>[];
      final auth = await coach((m, p, b, t) async {
        calls.add('$m $p');
        return {
          'items': [
            {
              'branch': 'الرحاب',
              'sport': 'كرة القدم',
              'variableMethod': 'Percent',
              'variableValue': 40,
              'eligibility': 'DeliveredCollected',
              'baseCoverage': 'GroupOnly',
              'currency': 'EGP',
              'effectiveFrom': '2026-10-01',
              'version': 1,
            },
          ],
        };
      });
      await tester.pumpWidget(shell(CoachAgreementsPage(auth: auth)));
      await tester.pumpAndSettle();
      expect(find.text('40%'), findsOneWidget);
      expect(find.text('تنفيذ معتمد وتحصيل صافٍ'), findsOneWidget);
      expect(find.byType(TextFormField), findsNothing);
      expect(find.byType(FilledButton), findsNothing);
      expect(calls, ['GET /api/v1/staff/payroll/agreements']);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets('No terms is not presented as a zero default or an amount due', (
    tester,
  ) async {
    final auth = await coach((m, p, b, t) async => {'items': []});
    await tester.pumpWidget(shell(CoachAgreementsPage(auth: auth)));
    await tester.pumpAndSettle();
    expect(find.text('لا يوجد اتفاق خاص معتمد لحسابك.'), findsOneWidget);
    expect(find.text('0%'), findsNothing);
    expect(tester.takeException(), isNull);
  });
}
