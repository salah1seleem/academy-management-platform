import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/guardian/guardian_profile.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_test/flutter_test.dart';

import 'guardian_test.dart' as guardian;

void main() {
  testWidgets('Guardian waived period is service, not a receipt or a payment', (
    tester,
  ) async {
    final auth = await guardian.logged(
      (m, p, b, t) async => {
        'plan': 'شهر تدريب اصطناعي',
        'status': 'Active',
        'origin': 'ApprovedWaiver',
        'startDate': '2026-09-28',
        'endDate': '2026-10-27',
        'adjustments': <Map<String, dynamic>>[],
      },
    );
    await tester.pumpWidget(
      guardian.shell(PeriodPage(auth: auth, id: 'waiver')),
    );
    await tester.pumpAndSettle();
    expect(find.text('تدريب مجاني بإعفاء معتمد'), findsOneWidget);
    expect(find.text('شهر تدريب اصطناعي'), findsOneWidget);
    expect(find.text('بداية الاشتراك 28 سبتمبر 2026'), findsOneWidget);
    expect(find.text('نهاية الاشتراك 27 أكتوبر 2026'), findsOneWidget);
    expect(find.text('لا توجد تعديلات.'), findsOneWidget);
    expect(find.textContaining('دفع'), findsNothing);
    expect(find.textContaining('إيصال'), findsNothing);
  });

  testWidgets('Guardian sees only recorded dates and session adjustments', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(320, 844);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final auth = await guardian.logged(
      (method, path, body, token) async => {
        'plan': 'باقة حصص اصطناعية',
        'status': 'Frozen',
        'startDate': '2026-11-12',
        'frozenFromDate': '2026-11-18',
        'remainingSessions': 7,
        'adjustments': [
          {'type': 'FreezeStarted', 'effectiveDate': '2026-11-18'},
          {
            'type': 'SessionsAdded',
            'effectiveDate': '2026-11-17',
            'newEndDate': '2026-12-11',
            'sessionsDelta': 2,
            'oldRemainingSessions': 5,
            'newRemainingSessions': 7,
          },
        ],
      },
    );
    await tester.pumpWidget(
      guardian.shell(PeriodPage(auth: auth, id: 'sessions')),
    );
    await tester.pumpAndSettle();
    expect(find.text('بداية الاشتراك 12 نوفمبر 2026'), findsOneWidget);
    expect(find.text('مجمد من 18 نوفمبر 2026'), findsOneWidget);
    expect(find.text('الحصص المتبقية 7'), findsOneWidget);
    expect(find.text('تجميد'), findsOneWidget);
    expect(find.text('الرصيد من 5 إلى 7 حصة'), findsOneWidget);
    expect(find.textContaining('نهاية الاشتراك'), findsOneWidget);
    expect(find.text('نهاية الاشتراك 11 ديسمبر 2026'), findsOneWidget);
    expect(find.textContaining('غير محدد'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('Guardian retries a temporary period failure', (tester) async {
    var reads = 0;
    final auth = await guardian.logged((method, path, body, token) async {
      expect(path, '/api/v1/guardian/subscriptions/periods/waiver/adjustments');
      if (++reads == 1) {
        throw const ApiFailure(503, 'تعذر تحميل الاشتراك الآن.');
      }
      return {
        'plan': 'شهر تدريب اصطناعي',
        'status': 'Active',
        'startDate': '2026-11-12',
        'endDate': '2026-12-11',
        'adjustments': <Map<String, dynamic>>[],
      };
    });
    await tester.pumpWidget(
      guardian.shell(PeriodPage(auth: auth, id: 'waiver')),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('إعادة المحاولة'));
    await tester.pumpAndSettle();
    expect(reads, 2);
    expect(find.text('بداية الاشتراك 12 نوفمبر 2026'), findsOneWidget);
    expect(find.text('فعال'), findsOneWidget);
    expect(find.text('إعادة المحاولة'), findsNothing);
  });
}
