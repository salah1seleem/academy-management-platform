import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/shared/matches.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Real Football brand, small meals and role-scoped matches across three native roles',
    (tester) async {
      final config = AppConfig(
        'Demo',
        const String.fromEnvironment('API_BASE_URL'),
      );
      final transport = HttpApiTransport(config),
          store = SecureRefreshStore('${config.storageKey}.brand-visual-only');
      await store.clear();
      final auth = AuthController(transport, store);
      await auth.restore();
      Future<void> waitFor(Finder finder) async {
        for (var i = 0; i < 150 && finder.evaluate().isEmpty; i++) {
          await tester.pump(const Duration(milliseconds: 150));
        }
        expect(finder, findsWidgets);
        await tester.pumpAndSettle();
      }

      Future<void> capture(String name) async {
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-brand-$name');
        }
      }

      Future<void> login(String phone) async {
        final challenge = await auth.requestOtp(phone);
        await auth.verify(phone, challenge, '246810');
        await tester.pumpWidget(AcademyApp(auth: auth, environment: 'Demo'));
        await tester.pumpAndSettle();
      }

      await login('01099900100');
      await waitFor(find.text('أبنائي'));
      await waitFor(find.text('متابعة الغذاء اليومي لطفلك'));
      expect(find.byType(AcademyCrest), findsWidgets);
      expect(find.text('تعذر تحميل وجبات اليوم'), findsNothing);
      await capture('guardian-home');
      await tester.scrollUntilVisible(
        find.byType(NextMatchStrip),
        160,
        scrollable: find.byType(Scrollable).first,
      );
      await waitFor(find.textContaining('فريق الأصدقاء'));
      await capture('guardian-next-match');
      await auth.logout();
      await tester.pumpAndSettle();
      await login('01099900020');
      await waitFor(find.text('المجموعات المسندة'));
      expect(find.byType(AcademyCrest), findsWidgets);
      await capture('coach-home');
      await auth.logout();
      await tester.pumpAndSettle();
      await login('01099900010');
      await waitFor(find.text('تحصيل اليوم'));
      expect(find.byType(AcademyCrest), findsWidgets);
      await capture('owner-home');
      expect(tester.takeException(), isNull);
      await auth.logout();
      auth.dispose();
      transport.close();
    },
  );
}
