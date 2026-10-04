import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Native OTP, secure restore and logout for Guardian Coach Owner',
    (tester) async {
      final config = AppConfig(
        'Demo',
        const String.fromEnvironment('API_BASE_URL'),
      );
      final transport = HttpApiTransport(config);
      // Dedicated test key; never clear the reviewer's normal installed app session.
      final store = SecureRefreshStore('${config.storageKey}.integration');
      await store.clear();
      for (final (phone, role) in [
        ('01099900100', 'Guardian'),
        ('01099900020', 'Coach'),
        ('01099900010', 'AcademyOwner'),
      ]) {
        final auth = AuthController(transport, store);
        await auth.restore();
        await tester.pumpWidget(AcademyApp(auth: auth, environment: 'Demo'));
        await tester.pumpAndSettle();
        await tester.enterText(find.byType(TextFormField).first, phone);
        await tester.tap(find.text('إرسال رمز التحقق'));
        await tester.pumpAndSettle();
        await tester.enterText(find.byType(TextFormField).last, '246810');
        await tester.tap(find.text('تأكيد الدخول'));
        await tester.pumpAndSettle();
        expect(auth.session?.selected?.role, role);
        expect(await store.read(), isNotNull);
        final restored = AuthController(transport, store);
        await restored.restore();
        expect(restored.session?.selected?.role, role);
        final me =
            await restored.request('GET', '/api/v1/me') as Map<String, dynamic>;
        expect(me['role'], role);
        await restored.logout();
        expect(await store.read(), isNull);
        await tester.pumpWidget(const SizedBox.shrink());
        auth.dispose();
        restored.dispose();
      }
      transport.close();
    },
  );
}
