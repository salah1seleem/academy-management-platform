import 'package:academy_mobile/features/guardian/guardian_profile.dart';
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
    expect(find.textContaining('دفع'), findsNothing);
    expect(find.textContaining('إيصال'), findsNothing);
  });
}
