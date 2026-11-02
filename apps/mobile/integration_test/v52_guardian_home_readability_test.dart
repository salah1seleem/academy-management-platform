import '../test/introduction_test_helpers.dart';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'v45_single_renewal_saved_receipt_test.dart'
    show SavedRenewalReadTransport;

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Real guardian home keeps stars, score and sessions across both approved palettes without writes',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final auth = AuthController(
        SavedRenewalReadTransport(AppConfig('Demo', base)),
        SecureRefreshStore('v46.urgent-summary.guardian0'),
      );
      try {
        await auth.restore();
        expect(auth.session!.selected!.role, 'Guardian');
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        final home = obj(await auth.request('GET', '/api/v1/guardian/home'));
        final child = rows(home['children']).first;
        final before = await auth.request('GET', '/api/v1/notifications');
        for (final light in [false, true]) {
          await t.pumpWidget(
            AcademyApp(
              key: ValueKey(light),
              auth: auth,
              environment: 'Demo',
              appearance: Appearance()..dark = !light,
            ),
          );
          final card = find.byKey(ValueKey('child-${child['id']}'));
          for (var i = 0; i < 240 && card.evaluate().isEmpty; i++) {
            if (find.text('تخطي').evaluate().isNotEmpty) {
              await finishRoleIntroduction(t);
            }
            await t.pump(const Duration(milliseconds: 150));
          }
          expect(find.byType(PremiumChildCard), findsWidgets);
          expect(card, findsOneWidget);
          await t.pumpAndSettle();
          expect(
            find.descendant(
              of: card,
              matching: find.text(textOf(child['arabicName'])),
            ),
            findsOneWidget,
          );
          expect(
            find.descendant(
              of: card,
              matching: find.text('${child['monthlyStars']} ★ نجوم هذا الشهر'),
            ),
            findsOneWidget,
          );
          final sport = rows(child['sports']).first;
          final score = obj(sport['performance'])['overallScore'] as num;
          expect(
            find.descendant(of: card, matching: find.text('${score.round()}')),
            findsOneWidget,
          );
          final period = obj(sport['period']);
          expect(
            find.descendant(
              of: card,
              matching: find.text('${period['remainingSessions']} حصص متبقية'),
            ),
            findsOneWidget,
          );
          await binding.takeScreenshot(
            'phase-approved-guardian-home-readable-${light ? 'light' : 'dark'}',
          );
        }
        expect(await auth.request('GET', '/api/v1/notifications'), before);
      } finally {
        auth.dispose();
      }
    },
  );
}
