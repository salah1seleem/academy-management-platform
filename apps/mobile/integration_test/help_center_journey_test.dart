import 'dart:convert';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Four roles open live authorized written help through More without changing financial results',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5228',
      );
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 200 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      for (final entry in [
        ('01099900010', 'AcademyOwner', 'قراءة لوحة المالك', 'owner-dashboard'),
        (
          '01099900011',
          'AcademyAdmin',
          'تسجيل حضور اللاعبين',
          'player-attendance',
        ),
        ('01099900020', 'Coach', 'حصتي ومجموعتي', 'coach-sessions'),
        ('01099900101', 'Guardian', 'تجديد اشتراك أبنائك', 'family-renewal'),
      ]) {
        final auth = AuthController(
          HttpApiTransport(
            AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
          ),
          SecureRefreshStore('academy.v3.help.journey.${entry.$2}'),
        );
        await auth.restore();
        if (auth.session != null) {
          await auth.logout();
        }
        final challenge = await auth.requestOtp(entry.$1);
        await auth.verify(entry.$1, challenge, '246810');
        expect(auth.session!.selected!.role, entry.$2);
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        final before = entry.$2 == 'AcademyOwner'
            ? jsonEncode(
                await auth.request(
                  'GET',
                  '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
                ),
              )
            : null;
        final catalog = rows(
          obj(await auth.request('GET', '/api/v1/help/lessons'))['items'],
        );
        expect(catalog.any((l) => l['id'] == entry.$4), true);
        expect(catalog.any((l) => l['id'] == 'swimming-lanes'), false);
        if (entry.$2 == 'Coach') {
          expect(catalog.any((l) => l['id'] == 'player-attendance'), false);
        }
        if (entry.$2 != 'AcademyOwner') {
          expect(catalog.any((l) => l['id'] == 'owner-dashboard'), false);
        }
        await t.pumpWidget(
          MaterialApp(
            key: ValueKey(entry.$2),
            locale: const Locale('ar'),
            supportedLocales: const [Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            theme: guardianTheme(),
            home: NativeMore(
              auth: auth,
              role: entry.$2,
              environment: 'Demo',
              account: const SizedBox.shrink(),
            ),
          ),
        );
        await wait(find.text('دليل الاستخدام'));
        await t.tap(find.text('دليل الاستخدام'));
        await wait(find.text('كيف نساعدك اليوم؟'));
        final field = find.widgetWithText(TextField, 'البحث في الأدلة');
        await t.enterText(field, entry.$3);
        await t.testTextInput.receiveAction(TextInputAction.search);
        await t.pumpAndSettle();
        await wait(find.text(entry.$3));
        await t.scrollUntilVisible(
          find.text('اقرأ الخطوات'),
          150,
          scrollable: find.byType(Scrollable).first,
        );
        await t.tap(find.text('اقرأ الخطوات'));
        await wait(find.textContaining('دليل مكتوب · إصدار'));
        expect(find.text(entry.$3), findsOneWidget);
        expect(find.textContaining('لا يوجد فيديو منشور بعد'), findsOneWidget);
        expect(t.takeException(), isNull);
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot(
            'phase-help-${entry.$2.toLowerCase()}-ios',
          );
        }
        if (before != null) {
          expect(
            jsonEncode(
              await auth.request(
                'GET',
                '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
              ),
            ),
            before,
          );
        }
        await auth.logout();
        await t.pumpAndSettle();
        expect(find.text(entry.$3), findsNothing);
        await t.pumpWidget(const SizedBox.shrink());
        auth.dispose();
      }
    },
  );
}
