import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart';

void main() {
  testWidgets(
    'One theme supports Arabic RTL and English LTR without role-specific overrides',
    (tester) async {
      final prefs = Appearance();
      final auth = AuthController(
        FakeApi((a, b, c, d) async => null),
        MemoryStore(),
      )..restoring = false;
      await tester.pumpWidget(
        AcademyApp(auth: auth, appearance: prefs, environment: 'Demo'),
      );
      expect(
        Theme.of(tester.element(find.text('رقم الهاتف'))).brightness,
        Brightness.dark,
      );
      expect(
        Directionality.of(tester.element(find.text('رقم الهاتف'))),
        TextDirection.rtl,
      );
      await prefs.update(night: false, language: 'en');
      await tester.pumpAndSettle();
      expect(find.text('Phone number'), findsOneWidget);
      expect(
        Theme.of(tester.element(find.text('Phone number'))).brightness,
        Brightness.light,
      );
      expect(
        Directionality.of(tester.element(find.text('Phone number'))),
        TextDirection.ltr,
      );
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
      prefs.dispose();
    },
  );
  testWidgets(
    'No role onboarding is shown before an authenticated context even for a fresh installation',
    (tester) async {
      final prefs = Appearance()..onboarded = false;
      final api = FakeApi((a, b, c, d) async => null);
      final auth = AuthController(api, MemoryStore())..restoring = false;
      await tester.pumpWidget(
        AcademyApp(auth: auth, appearance: prefs, environment: 'Demo'),
      );
      expect(find.byType(AcademyOnboarding), findsNothing);
      expect(find.text('رقم الهاتف'), findsOneWidget);
      await prefs.update(completed: true);
      await tester.pumpAndSettle();
      expect(find.text('رقم الهاتف'), findsOneWidget);
      expect(api.paths, isEmpty);
      await prefs.update(night: false);
      await tester.pumpAndSettle();
      expect(find.text('كل رحلة تستحق المتابعة'), findsNothing);
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
      prefs.dispose();
    },
  );
}
