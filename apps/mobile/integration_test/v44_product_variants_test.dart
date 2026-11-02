import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/guardian/sport_store.dart';
import 'package:academy_mobile/features/shared/transfer_payment.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'real guardian numbered uniform preserves chosen child, size and number in pending order',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final auth = AuthController(
        HttpApiTransport(AppConfig('Demo', base)),
        SecureRefreshStore('v44.5278.variants.guardian'),
      )..restoring = false;
      final challenge = await auth.requestOtp('01099900100');
      await auth.verify('01099900100', challenge, '246810');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      expect(auth.session!.selected!.role, 'Guardian');
      final sections = rows(
        await auth.request('GET', '/api/v1/guardian/catalog'),
      );
      final section = sections.firstWhere(
        (s) =>
            rows(s['items'])
                .any((p) => p['arabicName'] == 'زي تدريب مرقم تجريبي'),
      );
      final product = {
        ...rows(section['items'])
            .firstWhere((p) => p['arabicName'] == 'زي تدريب مرقم تجريبي'),
        'sportId': section['sportId'],
      };
      expect(product['usesVariants'], true);
      final contexts = rows(
        await auth.request('GET', '/api/v1/guardian/subscriptions/enrollments'),
      ).where((e) => e['sportId'] == product['sportId']).toList();
      Map<String, dynamic>? child;
      for (final e in contexts) {
        final inv = obj(
          await auth.request(
            'GET',
            '/api/v1/guardian/catalog/${product['id']}/variants?enrollmentId=${e['id']}',
          ),
        );
        if (inv['assignedShirtNumber'] == 7) {
          child = e;
          break;
        }
      }
      expect(child, isNotNull);
      Future<void> waitFor(Finder f) async {
        for (var i = 0; i < 200 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pump(const Duration(milliseconds: 250));
      }

      Future<void> tap(String label) async {
        final f = find.text(label).last;
        await waitFor(f);
        await t.ensureVisible(f);
        await t.pump(const Duration(milliseconds: 250));
        await t.ensureVisible(f);
        await t.tap(f);
        await t.pump(const Duration(milliseconds: 350));
      }

      Future<void> choose(String label, String option) async {
        final f = find.widgetWithText(DropdownButtonFormField<String>, label);
        await waitFor(f);
        await t.ensureVisible(f);
        await t.pump(const Duration(milliseconds: 250));
        await t.tap(f);
        await t.pump(const Duration(milliseconds: 350));
        await tap(option);
      }

      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: AcademyTheme.dark(),
          home: ProductPurchase(auth: auth, product: product),
        ),
      );
      await waitFor(find.text('الطفل والفرع'));
      await choose('الطفل والفرع', '${child!['player']} · ${child['branch']}');
      await t.pumpAndSettle();
      await choose('المقاس', 'M');
      await tap('أؤكد رقم القميص المختار: 7');
      await t.scrollUntilVisible(
        find.text('متابعة إلى الدفع'),
        140,
        scrollable: find.byType(Scrollable).first,
      );
      await t.pumpAndSettle();
      expect(t.takeException(), isNull);
      await binding.takeScreenshot(
        'phase-approved-numbered-product-confirmation',
      );
      await tap('متابعة إلى الدفع');
      await waitFor(find.byType(TransferPayment));
      await t.pumpAndSettle();
      final path = t
          .widget<TransferPayment>(find.byType(TransferPayment))
          .proofPath;
      final parts = path.split('/');
      final id = parts[parts.length - 2];
      final order = obj(
        await auth.request('GET', '/api/v1/guardian/product-sales/$id'),
      );
      expect(order['status'], 'Pending');
      expect(order['sizeSnapshot'], 'M');
      expect(order['shirtNumberSnapshot'], 7);
      expect(order['sportEnrollmentId'], child['id']);
      expect(order['playerId'], child['playerId']);
      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: AcademyTheme.dark(),
          home: GuardianProductOrder(auth: auth, id: id),
        ),
      );
      await waitFor(find.text('رقم القميص'));
      await t.pumpAndSettle();
      expect(t.takeException(), isNull);
      await binding.takeScreenshot(
        'phase-approved-numbered-product-saved-order',
      );
      final remaining = obj(
        await auth.request(
          'GET',
          '/api/v1/guardian/catalog/${product['id']}/variants?enrollmentId=${child['id']}',
        ),
      );
      expect(
        rows(remaining['variants']).firstWhere(
          (v) => v['size'] == 'M' && v['shirtNumber'] == 7,
        )['quantity'],
        1,
      ); // Existing pending-order policy: deduction only on staff confirmation.
      await t.pumpWidget(const SizedBox());
      await auth.logout();
      auth.dispose();
    },
  );
}
