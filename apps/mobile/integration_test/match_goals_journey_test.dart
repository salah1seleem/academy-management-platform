import '../test/introduction_test_helpers.dart';
import 'startup_motion_journey_test.dart'
    show StartupJourneyStore, StartupReadTransport;

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/shared/native_match_management.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'actual imported academy opens its finalized match and four live scorer slots through native navigation',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      final auth = AuthController(
        StartupReadTransport(AppConfig('Demo', 'http://127.0.0.1:5278')),
        StartupJourneyStore(),
      );
      final appearance = Appearance();
      Future<void> tick(int ms) async {
        for (var n = 0; n < ms; n += 50) {
          await t.pump(const Duration(milliseconds: 50));
        }
      }

      Future<void> wait(Finder target) async {
        for (var n = 0; n < 300 && target.evaluate().isEmpty; n++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(target, findsWidgets);
      }

      await t.runAsync(() async {
        final challenge = await auth.requestOtp('01084720253');
        await auth.verify('01084720253', challenge, '246810');
        final member = auth.session!.memberships.firstWhere(
          (m) =>
              m.academyId == '6fd31a1c-4c74-4726-81fe-a9fce59d0c73' &&
              m.role == 'AcademyOwner',
        );
        await auth.selectRole(member.id);
      });
      auth.restoring = false;
      await t.pumpWidget(
        AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
      );
      await tick(600);
      await finishRoleIntroduction(t);
      await tick(2500);
      await wait(find.byType(OwnerDashboard));
      await t.tap(find.byKey(const ValueKey('owner-nav-2')));
      await tick(1300);
      await t.tap(find.byTooltip('إدارة المباريات والنتائج'));
      await wait(find.byType(NativeMatchManagement));
      await tick(900);
      const match = '11bbf735-836d-4e18-82be-a6e517c3cffd';
      final edit = find.byKey(const ValueKey('match-edit-$match'));
      await wait(edit);
      await t.ensureVisible(edit);
      await tick(300);
      await binding.takeScreenshot('phase-matches-native-result');
      await t.tap(edit);
      await wait(find.byType(NativeMatchEditor));
      await tick(600);
      final first = find.byWidgetPredicate(
        (w) => w.key is ValueKey && w.key.toString().contains('goal-1-'),
      );
      await wait(first);
      await t.ensureVisible(first);
      await tick(500);
      await binding.takeScreenshot('phase-matches-native-goals');
      expect(find.text('الهدف 4'), findsOneWidget);
      expect(find.text('هدف عكسي للمنافس'), findsWidgets);
      binding.reportData = {
        ...?binding.reportData,
        'source':
            'actual iOS simulator; live API; synthetic match created by web UI',
        'matchId': match,
        'goalSlots': 4,
        'ourScore': 4,
        'opponentScore': 2,
        'writesFromNativeJourney': false,
      };
      await t.pumpWidget(const SizedBox());
      auth.dispose();
      appearance.dispose();
    },
  );
}
