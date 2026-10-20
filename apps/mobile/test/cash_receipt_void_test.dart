import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:academy_mobile/features/owner/owner_reports.dart';
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
          'paidAtUtc': '2026-09-28T06:00:00Z',
        },
      );
      await tester.pumpWidget(
        guardian.shell(ReceiptPage(auth: auth, id: 'receipt')),
      );
      await tester.pumpAndSettle();
      expect(find.textContaining('إيصال معكوس'), findsOneWidget);
      expect(find.textContaining('المدفوع: 800'), findsOneWidget);
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
      expect(
        find.textContaining('عكس إيصال نقدي · الفترة ملغاة'),
        findsOneWidget,
      );
      expect(find.textContaining('-800'), findsWidgets);
    },
  );
}
