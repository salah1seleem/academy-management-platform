import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class SavedRenewalReadTransport extends HttpApiTransport {
  SavedRenewalReadTransport(super.config);
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    if (method != 'GET' && !path.startsWith('/api/v1/mobile/auth/')) {
      throw StateError(
        'Saved acceptance permits only reads and authentication: $method $path',
      );
    }
    return super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'guardian opens the previously confirmed single renewal and exact receipt without new money',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final auth = AuthController(
        SavedRenewalReadTransport(AppConfig('Demo', base)),
        SecureRefreshStore('v45.5278.current-attendance.guardian7'),
      );
      await auth.restore();
      if (auth.session == null) {
        final challenge = await auth.requestOtp('01200000007');
        await auth.verify('01200000007', challenge, '246810');
      }
      expect(auth.session?.selected?.role, 'Guardian');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      const payment = '9720e2d6-798a-45e1-b12d-c4fc974c2f97';
      const receipt = '5c849242-33ee-49cc-8bb4-524b5a69dab1';
      final saved = obj(
        await auth.request(
          'GET',
          '/api/v1/guardian/subscriptions/payments/$payment',
        ),
      );
      expect(saved['status'], 'Confirmed');
      expect(saved['receiptId'], receipt);
      final receiptData = obj(
        await auth.request(
          'GET',
          '/api/v1/guardian/subscriptions/receipts/$receipt',
        ),
      );
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: AcademyTheme.dark(),
          home: CheckoutPage(auth: auth, environment: 'Demo', id: payment),
        ),
      );
      await wait(find.text('عرض الإيصال'));
      expect(find.text('رفع إيصال الدفع'), findsNothing);
      expect(find.text('الدفع التجريبي'), findsNothing);
      await binding.takeScreenshot(
        'phase-approved-single-renewal-saved-guardian',
      );
      await t.ensureVisible(find.text('عرض الإيصال'));
      await t.tap(find.text('عرض الإيصال'));
      await t.pump(const Duration(milliseconds: 350));
      await wait(find.text(textOf(receiptData['receiptNumber'])));
      expect(t.widget<ReceiptPage>(find.byType(ReceiptPage)).id, receipt);
      await binding.takeScreenshot(
        'phase-approved-single-renewal-saved-receipt',
      );
      final after = obj(
        await auth.request(
          'GET',
          '/api/v1/guardian/subscriptions/payments/$payment',
        ),
      );
      expect(after, saved);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
