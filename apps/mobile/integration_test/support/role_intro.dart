import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/shared/approved_role_welcome.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

// Normal UI action after authenticated context selection. Never pre-mark a role
// or weaken the journey's business assertions when a fresh installation tours.
Future<void> skipRoleIntroIfPresent(WidgetTester tester) async {
  await tester.pumpAndSettle();
  if (find.byType(AcademyOnboarding).evaluate().isNotEmpty) {
    await tester.tap(find.text('تخطي'));
    await tester.pumpAndSettle();
  }
  if (find.byType(ApprovedRoleWelcome).evaluate().isNotEmpty) {
    final enter = find.byKey(const ValueKey('role-welcome-enter'));
    await tester.ensureVisible(enter);
    await tester.tap(enter);
    // Owner home has intentionally continuous motion after V4.9.
    await tester.pump(const Duration(milliseconds: 400));
  }
  // Automatic reveal mounts Home immediately; allow its bounded animation to
  // finish without assuming that a continuously animated Home will settle.
  if (find.byKey(const ValueKey('academy-reveal')).evaluate().isNotEmpty) {
    await tester.pump(const Duration(milliseconds: 2050));
    await tester.pump();
  }
  expect(find.byType(AcademyOnboarding), findsNothing);
  expect(find.byType(ApprovedRoleWelcome), findsNothing);
}
