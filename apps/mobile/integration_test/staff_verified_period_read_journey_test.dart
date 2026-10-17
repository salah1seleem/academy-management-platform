import 'support/role_intro.dart';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

// Read the actual browser-signed source. This is not native passkey signing.
void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Native administrator reads the same browser-signed work period and source, without any attendance write',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      final transport = HttpApiTransport(
        AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
      );
      final auth = AuthController(
        transport,
        SecureRefreshStore('academy.v3.signed-period.admin-test'),
      );
      await auth.restore();
      if (auth.session != null) await auth.logout();
      final otp = await auth.requestOtp('01099900011');
      await auth.verify('01099900011', otp, '246810');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final state =
          await auth.request('GET', '/api/v1/staff/passkeys/attendance') as Map;
      final receipts = state['recent'] as List;
      final policy = (state['branches'] as List).singleWhere(
        (b) => (b as Map)['radiusMeters'] == 80,
      ) as Map;
      expect(policy['maxAccuracyMeters'], 10);
      expect(policy['maxAgeSeconds'], 15);
      expect(policy['challengeSeconds'], 60);
      expect(policy['attendanceRequired'], isTrue);
      expect(receipts, isNotEmpty);
      final receipt = receipts.first as Map;
      final own = await auth.request(
        'GET',
        '/api/v1/staff/work-periods?date=${receipt['workDate']}',
      ) as Map;
      final period = (own['items'] as List).singleWhere(
        (p) => (p as Map)['id'] == receipt['workPeriodId'],
      ) as Map;
      expect(period['trustSource'], 'ServerVerified');
      expect(period['endUtc'], isNotNull);
      expect(own['canEdit'], isFalse);
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 160 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      await t.pumpWidget(AcademyApp(auth: auth, environment: 'Demo'));
      await skipRoleIntroIfPresent(t);
      await wait(find.text('المزيد'));
      await t.tap(find.text('المزيد'));
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('فترات عملي'),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(find.text('فترات عملي'));
      await wait(find.text('اختيار يوم'));
      // A preserved browser receipt may be from yesterday; use the actual UI date filter.
      if (own['today'] != receipt['workDate']) {
        await t.tap(find.text('اختيار يوم'));
        await t.pumpAndSettle();
        await t.tap(find.byIcon(Icons.edit_outlined));
        await t.pumpAndSettle();
        final locale = MaterialLocalizations.of(
          t.element(find.byType(DatePickerDialog)),
        );
        await t.enterText(
          find.byType(TextField),
          locale.formatCompactDate(
            DateTime.parse(receipt['workDate'] as String),
          ),
        );
        await t.tap(find.text('عرض اليوم'));
        await t.pumpAndSettle();
      }
      await wait(
        find.text('بداية بتوقيع متحقق خادميًا — لا يضمن سلامة الموقع'),
      );
      expect(find.text('حفظ'), findsNothing);
      expect(find.text('إضافة فترة عمل'), findsNothing);
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await t.pumpAndSettle();
        await t.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 400)),
        );
        await binding.takeScreenshot('phase-signed-native-period');
      }
      await t.tap(find.byType(BackButton));
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('وسائل التحقق الخاصة بي'),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(find.text('وسائل التحقق الخاصة بي'));
      await wait(find.text('سياسة حضوري حسب الفرع'));
      await t.scrollUntilVisible(
        find.text('النطاق 80 مترًا · الدقة حتى 10 مترًا'),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      expect(
        find.text('تسجيل الحضور مطلوب لفئتك في هذا الفرع.'),
        findsOneWidget,
      );
      expect(find.text('تسجيل حضوري'), findsNothing);
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await t.pumpAndSettle();
        await t.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 400)),
        );
        await binding.takeScreenshot('phase-policy-native-own');
      }
      final after = await auth.request(
        'GET',
        '/api/v1/staff/work-periods?date=${receipt['workDate']}',
      ) as Map;
      expect(after['items'], own['items']);
      expect(after['totalWorkedSeconds'], own['totalWorkedSeconds']);
      await auth.logout();
      await t.pumpWidget(const SizedBox());
      auth.dispose();
      transport.close();
    },
  );
}
