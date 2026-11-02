import 'support/role_intro.dart';

import 'dart:convert';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

// Consumes a real virtual-authenticator registration created by the separate
// browser journey, in the same isolated Football Demo. No fake native biometrics.
void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Native own credential read and explicit revoke persist across clients without recording attendance',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      final transport = HttpApiTransport(
        AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
      );
      final auth = AuthController(
        transport,
        SecureRefreshStore('academy.v3.passkeys.owner-test'),
      );
      await auth.restore();
      if (auth.session != null) {
        await auth.logout();
      }
      final otp = await auth.requestOtp('01099900010');
      await auth.verify('01099900010', otp, '246810');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final keys = await auth.request('GET', '/api/v1/staff/passkeys/') as Map;
      final selected =
          (keys['items'] as List)
                  .where(
                    (k) =>
                        (k as Map)['name'] == 'وسيلة تحقق للمحاكي' &&
                        k['isActive'] == true,
                  )
                  .single
              as Map;
      final before =
              await auth.request('GET', '/api/v1/reports/owner-summary') as Map,
          daily = await auth.request(
            'GET',
            '/api/v1/reports/staff-daily?allDates=true',
          ) as Map;
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 160 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> reach(Finder f) async {
        await t.scrollUntilVisible(
          f,
          180,
          scrollable: find.byType(Scrollable).first,
        );
        await t.ensureVisible(f);
        await t.pumpAndSettle();
      }

      Future<void> capture(String n) async {
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await t.pumpAndSettle();
          await t.runAsync(
            () => Future<void>.delayed(const Duration(milliseconds: 400)),
          );
          await binding.takeScreenshot('phase-passkeys-$n');
        }
      }

      await t.pumpWidget(AcademyApp(auth: auth, environment: 'Demo'));
      await skipRoleIntroIfPresent(t);
      await wait(find.text('المزيد'));
      await t.tap(find.text('المزيد'));
      await t.pumpAndSettle();
      await reach(find.text('وسائل التحقق الخاصة بي'));
      await t.tap(find.text('وسائل التحقق الخاصة بي'));
      await wait(find.text('وسيلة تحقق للمحاكي'));
      await reach(find.text('وسيلة تحقق للمحاكي'));
      await capture('native-own');
      expect(find.text('إضافة وسيلة تحقق'), findsNothing);
      await reach(find.text('إيقاف الوسيلة'));
      await t.tap(find.text('إيقاف الوسيلة'));
      await wait(find.text('تأكيد الإيقاف'));
      await t.tap(find.text('تأكيد الإيقاف'));
      await wait(find.text('تم إيقاف وسيلة التحقق.'));
      await reach(find.text('وسيلة تحقق للمحاكي'));
      await capture('native-revoked');
      final saved = await auth.request('GET', '/api/v1/staff/passkeys/') as Map;
      expect(
        ((saved['items'] as List).singleWhere(
          (k) => (k as Map)['id'] == selected['id'],
        ) as Map)['isActive'],
        isFalse,
      );
      final after =
              await auth.request('GET', '/api/v1/reports/owner-summary') as Map,
          afterDaily = await auth.request(
            'GET',
            '/api/v1/reports/staff-daily?allDates=true',
          ) as Map;
      expect(
        jsonEncode(after['collections']),
        jsonEncode(before['collections']),
      );
      expect(
        jsonEncode(after['paymentRequests']),
        jsonEncode(before['paymentRequests']),
      );
      expect(afterDaily['totalCount'], daily['totalCount']);
      expect(t.takeException(), isNull);
      await auth.logout();
      await t.pumpWidget(const SizedBox());
      auth.dispose();
      transport.close();
    },
  );
}
