import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/daily_nutrition.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

// Reads only. Use an isolated Football Demo API, never a customer database.
void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Guardian real API siblings to scoped plans, published progress and daily nutrition',
    (tester) async {
      final transport = HttpApiTransport(
        AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
      );
      final store = SecureRefreshStore('academy.v3.guardian-home-integration');
      await store.clear();
      final auth = AuthController(transport, store);
      await auth.restore();
      const phone = String.fromEnvironment(
        'GUARDIAN_PHONE',
        defaultValue: '01099900101',
      );
      final challenge = await auth.requestOtp(phone);
      await auth.verify(phone, challenge, '246810');
      final home = await auth.request('GET', '/api/v1/guardian/home') as Map;
      final children = (home['children'] as List).cast<Map>();
      expect(children.length, greaterThanOrEqualTo(2));
      final child = children.reversed.firstWhere(
        (c) => (c['sports'] as List).any(
          (s) => ['Expired', 'Expiring'].contains(s['period']?['status']),
        ),
      );
      final sport = (child['sports'] as List).firstWhere(
        (s) => ['Expired', 'Expiring'].contains(s['period']?['status']),
      ) as Map;
      final latest = child['latestEvaluation'] as Map;
      expect(latest['id'], isNotNull);
      final report = await auth.request(
        'GET',
        '/api/v1/guardian/evaluations/${latest['id']}/report',
      ) as Map;
      expect(report['player'], child['arabicName']);
      await tester.pumpWidget(AcademyApp(auth: auth, environment: 'Demo'));
      Future<void> waitFor(Finder finder) async {
        for (var i = 0; i < 150 && finder.evaluate().isEmpty; i++) {
          await tester.pump(const Duration(milliseconds: 150));
        }
        expect(finder, findsWidgets);
        await tester.pumpAndSettle();
      }

      Future<void> scrollTo(Finder finder) async {
        await tester.scrollUntilVisible(
          finder,
          200,
          scrollable: find.byType(Scrollable).first,
        );
        await tester.pumpAndSettle();
      }

      Future<void> capture(String name) async {
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-guardian-$name');
        }
      }

      await waitFor(find.byType(DropdownButtonFormField<String>));
      await capture('home-family');
      await tester.tap(find.byType(DropdownButtonFormField<String>));
      await tester.pumpAndSettle();
      await tester.tap(find.text(child['arabicName'] as String).last);
      await tester.pumpAndSettle();
      expect(find.byKey(ValueKey('child-${child['id']}')), findsOneWidget);
      for (final sibling in children.where((c) => c['id'] != child['id'])) {
        expect(find.byKey(ValueKey('child-${sibling['id']}')), findsNothing);
      }
      await capture('home-selected');
      await scrollTo(find.byKey(ValueKey('home-renew-${sport['id']}')));
      await tester.tap(find.byKey(ValueKey('home-renew-${sport['id']}')));
      await waitFor(find.text('اختيار الباقة'));
      await waitFor(find.text(child['arabicName'] as String));
      await capture('scoped-plans');
      await tester.tap(find.byType(BackButton).last);
      await tester.pumpAndSettle();
      await scrollTo(find.text('تطور ${child['arabicName']}'));
      await tester.tap(find.text('تطور ${child['arabicName']}'));
      await waitFor(find.text('تقرير اللاعب'));
      await waitFor(find.text(child['arabicName'] as String));
      await capture('published-progress');
      await tester.tap(find.byType(BackButton).last);
      await tester.pumpAndSettle();
      await scrollTo(find.byType(DailyNutritionCard));
      await capture('daily-preview');
      await tester.tap(find.text('متابعة الغذاء اليومي لطفلك'));
      await waitFor(find.text('التغذية والصحة'));
      await waitFor(find.text('اقتراحات اليوم'));
      await capture('daily-details');
      expect(find.text('تعذر تحميل وجبات اليوم'), findsNothing);
      expect(tester.takeException(), isNull);
      await auth.logout();
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
      transport.close();
    },
  );
}
