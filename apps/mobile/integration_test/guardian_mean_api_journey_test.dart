import '../test/introduction_test_helpers.dart';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'startup_motion_journey_test.dart'
    show StartupJourneyStore, StartupReadTransport;

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'actual guardian home renders published means and synchronized child selection',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      final api = StartupReadTransport(
        AppConfig('Demo', 'http://127.0.0.1:5278'),
      );
      final auth = AuthController(api, StartupJourneyStore());
      await auth.restore();
      final challenge = await auth.requestOtp('01099900100');
      await auth.verify('01099900100', challenge, '246810');
      expect(auth.session?.selected?.role, 'Guardian');
      final data = await auth.request(
        'GET',
        '/api/v1/guardian/home',
      ) as Map<String, dynamic>;
      final children = (data['children'] as List).cast<Map<String, dynamic>>();
      expect(children, isNotEmpty);
      await t.pumpWidget(
        AcademyApp(auth: auth, environment: 'Demo', appearance: Appearance()),
      );
      Future<void> tick(int ms) async {
        for (var elapsed = 0; elapsed < ms; elapsed += 50) {
          await t.pump(const Duration(milliseconds: 50));
        }
      }

      for (
        var i = 0;
        i < 240 && find.byType(GuardianHome).evaluate().isEmpty;
        i++
      ) {
        await t.pump(const Duration(milliseconds: 100));
        if (find.byType(AcademyOnboarding).evaluate().isNotEmpty) {
          await finishRoleIntroduction(t);
          await tick(2500);
        }
      }
      expect(find.byType(GuardianHome), findsOneWidget);
      await tick(2500);
      expect(find.text('متوسط الأداء'), findsWidgets);
      await binding.takeScreenshot('phase-guardian-mean-real-home');
      if (children.length > 1) {
        await t.tap(find.byKey(ValueKey('child-tab-${children[1]['id']}')));
        await tick(600);
        expect(
          t
              .widget<ChoiceChip>(
                find.byKey(ValueKey('child-tab-${children[1]['id']}')),
              )
              .selected,
          isTrue,
        );
        await binding.takeScreenshot('phase-guardian-mean-real-sibling');
      }
      expect(api.reads, contains('/api/v1/guardian/home'));
      expect(t.takeException(), isNull);
    },
  );
}
