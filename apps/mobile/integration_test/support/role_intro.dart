import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:flutter_test/flutter_test.dart';

// Normal UI action after authenticated context selection. Never pre-mark a role
// or weaken the journey's business assertions when a fresh installation tours.
Future<void> skipRoleIntroIfPresent(WidgetTester tester) async {
  await tester.pumpAndSettle();
  if (find.byType(AcademyOnboarding).evaluate().isNotEmpty) {
    await tester.tap(find.text('تخطي'));
    await tester.pumpAndSettle();
  }
  expect(find.byType(AcademyOnboarding), findsNothing);
}
