import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'startup_motion_journey_test.dart'
    show StartupJourneyStore, StartupReadTransport;

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'actual imported player profile has a scoped guardian contact action',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const phone = String.fromEnvironment('TEST_OWNER_PHONE'),
          academy = String.fromEnvironment('TEST_ACADEMY_ID'),
          player = String.fromEnvironment('TEST_PLAYER_ID');
      expect(phone, isNotEmpty);
      expect(player, isNotEmpty);
      final api = StartupReadTransport(
        AppConfig('Demo', 'http://127.0.0.1:5278'),
      );
      final auth = AuthController(api, StartupJourneyStore());
      final challenge = await auth.requestOtp(phone);
      await auth.verify(phone, challenge, '246810');
      expect(auth.session?.selected?.academyId, academy);
      expect(auth.session?.selected?.role, 'AcademyOwner');
      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: AcademyTheme.dark(),
          home: NativePlayerWorkspace(auth: auth, id: player),
        ),
      );
      Future<void> tick(int ms) async {
        for (var elapsed = 0; elapsed < ms; elapsed += 50) {
          await t.pump(const Duration(milliseconds: 50));
        }
      }

      for (
        var i = 0;
        i < 240 &&
            find.text('التواصل مع ولي الأمر عبر واتساب').evaluate().isEmpty;
        i++
      ) {
        await t.pump(const Duration(milliseconds: 100));
      }
      expect(find.text('التواصل مع ولي الأمر عبر واتساب'), findsOneWidget);
      await tick(600);
      await binding.takeScreenshot('phase-contact-imported-player-profile');
      final contacts = await auth.request(
        'GET',
        '/api/v1/people/players/$player/guardian-contacts',
      ) as Map<String, dynamic>;
      expect((contacts['contacts'] as List), isNotEmpty);
      expect(
        api.reads,
        contains('/api/v1/people/players/$player/guardian-contacts'),
      );
      // Do not tap the external conversation button for synthetic phone numbers.
      expect(t.takeException(), isNull);
    },
  );
}
