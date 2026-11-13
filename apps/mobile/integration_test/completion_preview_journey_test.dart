import '../test/introduction_test_helpers.dart';
import 'startup_motion_journey_test.dart' show StartupReadTransport;

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/guardian/guardian_content.dart';
import 'package:academy_mobile/features/guardian/daily_nutrition.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'isolated real guardian sport sync, private import and populated nutrition',
    (t) async {
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5378');
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      final api = StartupReadTransport(AppConfig('Demo', base));
      final prefs = Appearance();
      Future<void> tick(int ms) async {
        for (var n = 0; n < ms; n += 100) {
          await t.pump(const Duration(milliseconds: 100));
        }
      }

      Future<void> wait(Finder f) async {
        for (var n = 0; n < 400 && f.evaluate().isEmpty; n++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(f, findsWidgets);
      }

      Future<void> reveal(Finder f, {bool up = false}) async {
        await t.scrollUntilVisible(
          f,
          up ? -180 : 180,
          scrollable: find
              .descendant(
                of: find.byKey(const ValueKey('guardian-home-scroll')),
                matching: find.byType(Scrollable),
              )
              .first,
        );
        await tick(500);
      }

      Future<AuthController> mount(
        String phone,
        String academy,
        String role,
      ) async {
        final auth = AuthController(
          api,
          SecureRefreshStore('completion-preview-5378-$phone'),
        );
        await auth.restore();
        if (auth.session == null) {
          final challenge = await auth.requestOtp(phone);
          await auth.verify(phone, challenge, '246810');
        }
        if (auth.session?.selected?.academyId != academy ||
            auth.session?.selected?.role != role) {
          await auth.selectRole(
            auth.session!.memberships
                .firstWhere((m) => m.academyId == academy && m.role == role)
                .id,
          );
        }
        auth.restoring = false;
        await t.pumpWidget(
          AcademyApp(auth: auth, environment: 'Demo', appearance: prefs),
        );
        await wait(find.byType(AcademyOnboarding));
        await finishRoleIntroduction(t);
        await wait(find.byType(NativeRoleTabs));
        await tick(2500);
        return auth;
      }

      const scenario = String.fromEnvironment(
        'PREVIEW_SCENARIO',
        defaultValue: 'all',
      );
      late AuthController auth;
      if (scenario != 'private') {
        auth = await mount(
          '01526189023',
          '507a3fde-42f9-43fa-bbbb-5a6009365d35',
          'Guardian',
        );
        await wait(find.byType(DropdownButtonFormField<String>));
        await t.tap(find.byType(DropdownButtonFormField<String>).first);
        await tick(300);
        await t.tap(find.textContaining('كرة القدم').last);
        await tick(600);
        await binding.takeScreenshot('phase-completion-sport-selected');
        final child = t.widget<PremiumChildCard>(
          find.byType(PremiumChildCard).first,
        );
        final selected = (child.childData['sports'] as List)
            .cast<Map<String, dynamic>>()
            .singleWhere(
              (enrollment) => enrollment['id'] == child.selectedEnrollmentId,
            );
        expect(selected['sport'], 'كرة القدم');
        expect((selected['performance'] as Map)['overallScore'], 74);
        await binding.takeScreenshot('phase-completion-subscription-synced');
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      }
      if (scenario != 'nutrition') {
        auth = await mount(
          '01084720253',
          '6fd31a1c-4c74-4726-81fe-a9fce59d0c73',
          'Guardian',
        );
        await wait(find.text('لاعب الخاص الخيالي 001'));
        await binding.takeScreenshot('phase-completion-private-guardian');
        final privateChild = t
            .widget<PremiumChildCard>(find.byType(PremiumChildCard).first)
            .childData;
        expect(privateChild['arabicName'], 'لاعب الخاص الخيالي 001');
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      }
      if (scenario != 'private') {
        auth = await mount(
          '01099900100',
          '30000000-0000-0000-0000-000000000003',
          'Guardian',
        );
        final nutrition = find.descendant(
          of: find.byType(PremiumChildCard).first,
          matching: find.text('مكتبة التغذية'),
        );
        await reveal(nutrition);
        await binding.takeScreenshot('phase-completion-nutrition-home');
        await t.tap(nutrition);
        await wait(find.byType(NutritionLibrary));
        await tick(1800);
        final card = t.widget<NutritionLibrary>(find.byType(NutritionLibrary));
        final meals = await auth.request(
          'GET',
          dailyNutritionPath(card.playerId, card.today),
        ) as Map;
        for (final key in ['breakfast', 'lunch', 'dinner']) {
          expect(meals[key], isA<Map>());
        }
        await binding.takeScreenshot('phase-completion-nutrition-library');
        final first = meals['breakfast'] as Map;
        await t.tap(
          find.widgetWithText(ActionChip, 'الإفطار · ${first['arabicName']}'),
        );
        await wait(find.byType(MealDetails));
        await tick(1800);
        expect(find.text('القيم الغذائية'), findsOneWidget);
        await binding.takeScreenshot('phase-completion-nutrition-details');
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      }
      binding.reportData ??= <String, dynamic>{};
      binding.reportData!['selectionVerified'] = scenario != 'private';
      binding.reportData!['source'] = 'Real isolated API 5378; UI imported private enrollment; existing synthetic populated nutrition; no business writes';
      prefs.dispose();
    },
  );
}
