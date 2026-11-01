import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets('approved design uses real authenticated native role shells', (
    t,
  ) async {
    expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
    const api = String.fromEnvironment('API_BASE_URL');
    expect(api, 'http://127.0.0.1:5278');
    const size = String.fromEnvironment(
      'CAPTURE_SIZE',
      defaultValue: 'typical',
    );
    Future<void> waitFor(Finder f) async {
      for (var i = 0; i < 200 && f.evaluate().isEmpty; i++) {
        await t.pump(const Duration(milliseconds: 150));
      }
      expect(f, findsWidgets);
      await t.pump(const Duration(milliseconds: 400));
    }

    for (final sample in [
      ('AcademyOwner', '01099900010', 'owner', 'ملخص الأكاديمية'),
      ('AcademyAdmin', '01099900011', 'admin', 'حضور اللاعبين'),
      ('Coach', '01099900020', 'coach', 'المجموعات المسندة'),
      ('Guardian', '01099900101', 'guardian', 'أبنائي'),
    ]) {
      final auth = AuthController(
        HttpApiTransport(AppConfig('Demo', api)),
        SecureRefreshStore('v44.5278.approved.${sample.$2}'),
      );
      await auth.restore();
      var restored = false;
      if (auth.session?.selected != null) {
        try {
          await auth.request('GET', '/api/v1/me');
          restored = true;
        } catch (_) {
          /* ordinary OTP replaces an expired test session */
        }
      }
      if (!restored) {
        final challenge = await auth.requestOtp(sample.$2);
        await auth.verify(sample.$2, challenge, '246810');
      }
      expect(auth.session!.selected!.role, sample.$1);
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final appearance = Appearance();
      for (final theme in ['football', 'general']) {
        await appearance.completeRoleIntro(sample.$1, theme);
      }
      await t.pumpWidget(
        AcademyApp(
          key: ValueKey(sample.$1),
          auth: auth,
          environment: 'Demo',
          appearance: appearance,
        ),
      );
      await waitFor(find.byType(NativeRoleTabs));
      await waitFor(find.text(sample.$4));
      expect(find.byTooltip('حسابي'), findsOneWidget);
      expect(find.byTooltip('التنبيهات'), findsOneWidget);
      expect(find.byTooltip('شرح الصفحة بالفيديو'), findsOneWidget);
      expect(t.takeException(), isNull);
      if (sample.$1 == 'AcademyOwner' &&
          const bool.fromEnvironment('EXPECTED_DEMO_COMPARISON')) {
        final calendar = obj(
          await auth.request('GET', '/api/v1/academy/calendar'),
        );
        final q = Uri(
          queryParameters: {
            'from': textOf(calendar['trailingMonthFrom']),
            'to': textOf(calendar['today']),
          },
        ).query;
        final cockpit = obj(
          await auth.request('GET', '/api/v1/reports/owner-cockpit?$q'),
        );
        final collections = obj(cockpit['collections']);
        expect(collections['net'], 27300);
        expect(collections['previous'], 24300);
        expect(collections['changePercentage'], inInclusiveRange(12, 14));
      }
      await t.pumpAndSettle();
      await binding.takeScreenshot(
        'phase-approved-${sample.$3}-home-$size-isolated',
      );
      if (sample.$1 == 'AcademyOwner') {
        for (final tab in ['العمليات', 'المباريات', 'التقارير', 'المزيد']) {
          await t.tap(
            find.descendant(
              of: find.byType(NavigationBar),
              matching: find.text(tab),
            ),
          );
          await t.pump(const Duration(seconds: 1));
          expect(find.byTooltip('حسابي'), findsOneWidget);
          expect(find.byTooltip('التنبيهات'), findsOneWidget);
          expect(find.byTooltip('شرح الصفحة بالفيديو'), findsOneWidget);
          expect(t.takeException(), isNull);
        }
        await t.tap(
          find.descendant(
            of: find.byType(NavigationBar),
            matching: find.text('الرئيسية'),
          ),
        );
        await waitFor(find.text('ملخص الأكاديمية'));
        if (const bool.fromEnvironment('HOME_SETTINGS_ACCEPTANCE')) {
          await t.ensureVisible(find.text('ضبط هدف التحصيل'));
          await t.tap(find.text('ضبط هدف التحصيل'));
          await waitFor(find.text('حفظ الهدف'));
          await t.enterText(find.byType(TextField), '50000');
          await t.tap(find.text('حفظ الهدف'));
          await waitFor(find.text('هدف الفترة'));
          final calendar = obj(
            await auth.request('GET', '/api/v1/academy/calendar'),
          );
          final query = Uri(
            queryParameters: {
              'from': textOf(calendar['trailingMonthFrom']),
              'to': textOf(calendar['today']),
            },
          ).query;
          final saved = obj(
            await auth.request('GET', '/api/v1/reports/owner-cockpit?$query'),
          );
          expect(obj(saved['target'])['amount'], 50000);
          expect(obj(saved['target'])['branchId'], isNull);
          await t.tap(
            find.descendant(
              of: find.byType(NavigationBar),
              matching: find.text('المزيد'),
            ),
          );
          await t.pump(const Duration(milliseconds: 300));
          await t.tap(
            find.descendant(
              of: find.byType(NavigationBar),
              matching: find.text('الرئيسية'),
            ),
          );
          await waitFor(find.text('هدف الفترة'));
          await binding.takeScreenshot(
            'phase-approved-owner-target-$size-isolated',
          );
        }
      }
      // Read-only role boundaries are still enforced by the same backend.
      final me = obj(await auth.request('GET', '/api/v1/me'));
      expect(me.isNotEmpty, isTrue);
      await t.pumpWidget(const SizedBox());
      await t.pump(const Duration(milliseconds: 300));
      auth.dispose();
      appearance.dispose();
    }
  });
}
