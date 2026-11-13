import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:academy_mobile/features/owner/owner_reports.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'guardian_test.dart' as guardian;
import 'owner_test.dart' as owner;

void main() {
  testWidgets(
    'Guardian original receipt clearly identifies a void without changing the original amount',
    (tester) async {
      final auth = await guardian.logged(
        (m, p, b, t) async => {
          'academyName': 'أكاديمية الاختبار',
          'receiptNumber': 'R-V1',
          'playerNameSnapshot': 'لاعب اصطناعي',
          'sportNameSnapshot': 'كرة القدم',
          'planNameSnapshot': 'شهري',
          'originalAmount': 800,
          'amount': 800,
          'discountAmount': 0,
          'currency': 'EGP',
          'isVoided': true,
          'enrollmentBranch': 'فرع التدريب',
          'collectionBranch': 'فرع الصندوق',
          'paidAtUtc': '2026-11-11T21:59:00Z',
          'paidAtAcademy': '2026-11-11T23:59:00+02:00',
          'voidedAtUtc': '2026-11-11T22:01:00Z',
          'voidedAtAcademy': '2026-11-12T00:01:00+02:00',
        },
      );
      await tester.pumpWidget(
        guardian.shell(ReceiptPage(auth: auth, id: 'receipt')),
      );
      await tester.pumpAndSettle();
      expect(find.textContaining('إيصال معكوس'), findsOneWidget);
      expect(find.text('المبلغ المدفوع'), findsOneWidget);
      expect(find.textContaining('800'), findsOneWidget);
      final locale = MaterialLocalizations.of(
        tester.element(find.byType(ReceiptPage)),
      );
      final paidDay = locale.formatShortDate(DateTime.utc(2026, 11, 11));
      final reversedDay = locale.formatShortDate(DateTime.utc(2026, 11, 12));
      final paidTime = locale.formatTimeOfDay(
        const TimeOfDay(hour: 23, minute: 59),
      );
      final reversedTime = locale.formatTimeOfDay(
        const TimeOfDay(hour: 0, minute: 1),
      );
      expect(find.textContaining('$paidDay · $paidTime'), findsOneWidget);
      expect(
        find.textContaining('$reversedDay · $reversedTime'),
        findsOneWidget,
      );
      expect(find.textContaining('فرع التسجيل: فرع التدريب'), findsOneWidget);
      expect(find.textContaining('فرع التحصيل: فرع الصندوق'), findsOneWidget);
    },
  );
  testWidgets(
    'Owner financial list distinguishes a void and displays its negative net',
    (tester) async {
      final auth = await owner.owner(
        (m, p, b, t) async => p.contains('structure/options')
            ? {'branches': []}
            : {
                'totalAmount': 0,
                'totalRefunds': 800,
                'netAmount': -800,
                'totalCount': 1,
                'currency': 'EGP',
                'items': [
                  {
                    'player': 'لاعب اصطناعي',
                    'receiptNumber': 'R-V1',
                    'branch': 'مدينة نصر',
                    'collectionType': 'Void',
                    'amount': 0,
                    'netAmount': -800,
                    'currency': 'EGP',
                  },
                ],
              },
      );
      await tester.pumpWidget(
        owner.shell(OwnerReports(auth: auth, initialReport: 'financial')),
      );
      await tester.pumpAndSettle();
      expect(find.textContaining('عكس إيصال نقدي'), findsOneWidget);
      expect(find.textContaining('-800'), findsWidgets);
      expect(
        find.textContaining('الفترة ملغاة'),
        findsNothing,
      ); // A void row alone does not prove period cancellation.
    },
  );
}
