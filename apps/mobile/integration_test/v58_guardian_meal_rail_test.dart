import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/daily_nutrition.dart';
import 'package:academy_mobile/features/guardian/family_nutrition_rail.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'v57_role_footer_design_test.dart' show FooterReadTransport;

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'guardian approved meal rail reads real child meals and opens the same detail',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      const viewportTag = String.fromEnvironment(
        'MEAL_VIEWPORT_TAG',
        defaultValue: 'regular',
      );
      const expectedWidth = int.fromEnvironment(
        'MEAL_EXPECTED_WIDTH',
        defaultValue: 440,
      );
      expect(
        t.view.physicalSize.width / t.view.devicePixelRatio,
        expectedWidth.toDouble(),
      );
      expect(base, 'http://127.0.0.1:5278');
      final transport = FooterReadTransport(AppConfig('Demo', base));
      final auth = AuthController(
        transport,
        SecureRefreshStore('v45.5278.os-media-entry.01099900100'),
      );
      await auth.restore();
      if (auth.session == null) {
        final challenge = await auth.requestOtp('01099900100');
        await auth.verify('01099900100', challenge, '246810');
      }
      expect(auth.session!.selected!.role, 'Guardian');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final home = obj(await auth.request('GET', '/api/v1/guardian/home'));
      final children = rows(home['children']);
      expect(children, isNotEmpty);
      final today = textOf(home['trainingToday'] ?? home['today']);
      final source = obj(
        await auth.request(
          'GET',
          dailyNutritionPath(textOf(children.first['id']), today),
        ),
      );
      final expectedMeal = obj(
        dailyNutritionMeals(source).firstWhere((m) => m.$3 is Map).$3,
      );
      final expectedDetail = obj(
        await auth.request(
          'GET',
          '/api/v1/guardian/nutrition/${expectedMeal['id']}',
        ),
      );
      final appearance = Appearance();
      await appearance.completeRoleIntro('Guardian', 'football');
      await appearance.completeRoleIntro('Guardian', 'general');
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 200 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      for (final light in [true, false]) {
        await t.pumpWidget(const SizedBox());
        await t.pumpAndSettle();
        await appearance.update(night: !light);
        await t.pumpWidget(
          AcademyApp(
            key: ValueKey(light),
            auth: auth,
            environment: 'Demo',
            appearance: appearance,
          ),
        );
        await wait(find.text('أبنائي'));
        await t.scrollUntilVisible(
          find.byType(FamilyNutritionRail),
          250,
          scrollable: find.byType(Scrollable).first,
          maxScrolls: 30,
        );
        await wait(find.byKey(const ValueKey('family-meal-0')));
        final first = find.byKey(const ValueKey('family-meal-0'));
        await t.ensureVisible(first);
        await t.drag(find.byType(Scrollable).first, const Offset(0, 90));
        await t.pumpAndSettle();
        expect(
          find.descendant(
            of: first,
            matching: find.text(textOf(expectedMeal['arabicName'])),
          ),
          findsOneWidget,
        );
        expect(
          find.descendant(
            of: first,
            matching: find.text(textOf(children.first['arabicName'])),
          ),
          findsOneWidget,
        );
        final image = t.widget<DemoImage>(
          find.descendant(of: first, matching: find.byType(DemoImage)),
        );
        expect(image.reference, expectedMeal['imageReference']);
        expect(image.borderRadius, BorderRadius.zero);
        await binding.takeScreenshot(
          'phase-approved-guardian-meals-$viewportTag-${light ? 'light' : 'dark'}',
        );
        await t.tap(first);
        await wait(find.text('تفاصيل الوجبة'));
        await wait(find.text(textOf(expectedDetail['arabicDescription'])));
        expect(find.text(textOf(expectedMeal['arabicName'])), findsOneWidget);
        await binding.takeScreenshot(
          'phase-approved-guardian-meal-detail-$viewportTag-${light ? 'light' : 'dark'}',
        );
        await t.tap(find.byType(BackButton));
        await wait(find.byKey(const ValueKey('family-meal-0')));
        expect(t.takeException(), isNull);
      }
      expect(transport.writes, isEmpty);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
      appearance.dispose();
    },
  );
}
