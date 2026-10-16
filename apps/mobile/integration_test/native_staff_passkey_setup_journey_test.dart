import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/auth/staff_passkey_provider.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

// Actual OS rejection of the deliberately unassociated reserved .test RP is
// not successful enrollment, Face ID verification, or physical-device coverage.
void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Native provider rejects missing association, preserves sources and offers the existing review path',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      final transport = HttpApiTransport(
            AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
          ),
          auth = AuthController(
            transport,
            SecureRefreshStore('academy.v3.native-passkey.setup-test'),
          );
      await auth.restore();
      if (auth.session != null) await auth.logout();
      final otp = await auth.requestOtp('01099900011');
      await auth.verify('01099900011', otp, '246810');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final before =
              await auth.request('GET', '/api/v1/staff/passkeys/') as Map,
          periods =
              await auth.request('GET', '/api/v1/staff/work-periods') as Map;
      expect(before['rpId'], 'academy.test');
      expect(before['nativeIosOriginConfigured'], isTrue);
      final options = await auth.request(
        'POST',
        '/api/v1/staff/passkeys/register/options',
        body: {'name': 'وسيلة اختبار نطاق غير مرتبط'},
      ) as Map;
      final provider = IosStaffPasskeyProvider(
        optedIn: true,
        rpId: 'academy.test',
      );
      final availability = await provider.authenticator.getAvailability().iOS();
      expect(availability.isNative, isTrue);
      expect(availability.hasPasskeySupport, isTrue);
      await expectLater(
        provider
            .register(Map<String, dynamic>.from(options['options'] as Map))
            .timeout(const Duration(seconds: 40)),
        throwsA(isA<StaffPasskeyProblem>()),
      );
      expect(
        (await auth.request('GET', '/api/v1/staff/passkeys/') as Map)['items'],
        before['items'],
      );
      expect(
        (await auth.request('GET', '/api/v1/staff/work-periods')
            as Map)['items'],
        periods['items'],
      );
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 180 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      await t.pumpWidget(AcademyApp(auth: auth, environment: 'Demo'));
      await wait(find.text('المزيد'));
      await t.tap(find.text('المزيد'));
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('حضوري وانصرافي'),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(find.text('حضوري وانصرافي'));
      await wait(find.textContaining('الإعداد المحلي الحالي لا يكفي'));
      expect(find.text('تسجيل حضوري'), findsNothing);
      await t.scrollUntilVisible(
        find.text('طلب إثبات حضور للمراجعة'),
        160,
        scrollable: find.byType(Scrollable).first,
      );
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await t.pumpAndSettle();
        await t.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 400)),
        );
        await binding.takeScreenshot('phase-native-passkey-setup');
      }
      await t.tap(find.text('طلب إثبات حضور للمراجعة'));
      await wait(find.text('طلب إثبات حضوري'));
      expect(find.text('إرسال للمراجعة'), findsOneWidget);
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await t.pumpAndSettle();
        await t.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 400)),
        );
        await binding.takeScreenshot('phase-native-passkey-review');
      }
      expect(
        (await auth.request('GET', '/api/v1/staff/work-periods')
            as Map)['items'],
        periods['items'],
      );
      await auth.logout();
      await t.pumpWidget(const SizedBox());
      auth.dispose();
      transport.close();
    },
  );
}
