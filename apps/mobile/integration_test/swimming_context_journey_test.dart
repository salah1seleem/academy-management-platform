import '../test/introduction_test_helpers.dart';
import 'startup_motion_journey_test.dart'
    show StartupJourneyStore, StartupReadTransport;

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/shared/academy_reveal.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'one verified identity switches from Owner A to Guardian in swimming B without another OTP or owner powers',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const main = '507a3fde-42f9-43fa-bbbb-5a6009365d35',
          second = '0a015eb4-9e07-4f6d-90c2-7cc626346f45';
      final auth = AuthController(
        StartupReadTransport(AppConfig('Demo', 'http://127.0.0.1:5278')),
        StartupJourneyStore(),
      );
      final prefs = Appearance();
      Future<void> tick(int ms) async {
        for (var n = 0; n < ms; n += 50) {
          await t.pump(const Duration(milliseconds: 50));
        }
      }

      Future<void> wait(Finder f) async {
        for (var n = 0; n < 300 && f.evaluate().isEmpty; n++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(f, findsWidgets);
      }

      await t.runAsync(() async {
        final challenge = await auth.requestOtp('01084720300');
        await auth.verify('01084720300', challenge, '246810');
        await auth.selectRole(
          auth.session!.memberships
              .firstWhere(
                (m) => m.academyId == main && m.role == 'AcademyOwner',
              )
              .id,
        );
      });
      auth.restoring = false;
      expect(
        auth.session!.memberships.where(
          (m) => m.academyId == second && m.role == 'AcademyOwner',
        ),
        isEmpty,
      );
      await t.pumpWidget(
        AcademyApp(auth: auth, environment: 'Demo', appearance: prefs),
      );
      await tick(600);
      await finishRoleIntroduction(t);
      await tick(2400);
      await wait(find.byType(OwnerDashboard));
      await tick(1300);
      await binding.takeScreenshot('phase-context-owner-a');
      final owner = await t.runAsync(
        () => auth.request('GET', '/api/v1/subscriptions/home'),
      ) as Map;
      expect(owner['total'], 305);
      await t.runAsync(
        () => auth.selectRole(
          auth.session!.memberships
              .firstWhere((m) => m.academyId == second && m.role == 'Guardian')
              .id,
        ),
      );
      await tick(500);
      await binding.takeScreenshot('phase-context-swimming-intro');
      await finishRoleIntroduction(t);
      await tick(250);
      expect(find.byType(AcademyReveal), findsOneWidget);
      await binding.takeScreenshot('phase-context-swimming-reveal');
      await tick(2300);
      await wait(find.byType(GuardianHome));
      await tick(1300);
      await binding.takeScreenshot('phase-context-swimming-guardian');
      final home = await t.runAsync(
        () => auth.request('GET', '/api/v1/guardian/home'),
      ) as Map;
      expect((home['children'] as List).length, 2);
      for (final child in home['children'] as List) {
        for (final sport in child['sports'] as List) {
          expect(sport['sport'], 'السباحة');
        }
      }
      final brand = await t.runAsync(
        () => auth.request('GET', '/api/v1/academy/experience'),
      ) as Map;
      expect((brand['onboarding'] as Map)['sportTheme'], 'swimming');
      await t.runAsync(() async {
        try {
          await auth.request('GET', '/api/v1/reports/owner-summary');
          fail('Guardian must not retain Owner access');
        } on ApiFailure catch (e) {
          expect(e.status, 403);
        }
      });
      binding.reportData = {
        ...?binding.reportData,
        'source':
            'actual native iOS simulator and two imported synthetic academies',
        'ownerAEnrollments': 305,
        'guardianBChildren': 2,
        'onlySwimming': true,
        'ownerFinancialAccessDenied': true,
        'secondOtpRequested': false,
      };
      await t.pumpWidget(const SizedBox());
      auth.dispose();
      prefs.dispose();
    },
  );
}
