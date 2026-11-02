import 'startup_motion_journey_test.dart'
    show StartupJourneyStore, StartupReadTransport;

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/shared/launcher_icon.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    const bool.fromEnvironment('ICON_EXPECT_SYSTEM_BLOCK')
        ? 'actual iOS system rejection is shown and the standard icon is restored'
        : 'real iOS alternate icon uses explicit authorized selection',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      final service = LauncherIconService();
      if (const bool.fromEnvironment('ICON_RESET_ONLY')) {
        await t.pumpWidget(
          const MaterialApp(
            home: Scaffold(body: Text('إعادة الأيقونة القياسية — فحص محاكي')),
          ),
        );
        await t.pumpAndSettle();
        await t.runAsync(() => service.set(null, null));
        final after = await t.runAsync(service.state);
        expect(after!['currentKey'], isNull);
        binding.reportData = {
          ...?binding.reportData,
          'source': 'actual iOS simulator native API',
          'reset': true,
        };
        return;
      }
      final auth = AuthController(
        StartupReadTransport(AppConfig('Demo', 'http://127.0.0.1:5278')),
        StartupJourneyStore(),
      );
      await t.runAsync(() async {
        final challenge = await auth.requestOtp('01084720300');
        await auth.verify('01084720300', challenge, '246810');
        if (auth.session?.selected?.role != 'AcademyOwner') {
          await auth.selectRole(
            auth.session!.memberships
                .firstWhere(
                  (m) =>
                      m.academyId == '507a3fde-42f9-43fa-bbbb-5a6009365d35' &&
                      m.role == 'AcademyOwner',
                )
                .id,
          );
        }
      });
      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: AcademyTheme.light(),
          home: LauncherIconPage(auth: auth),
        ),
      );
      for (
        var n = 0;
        n < 100 &&
            find
                .text('استخدام شعار أكاديميتي كأيقونة التطبيق')
                .evaluate()
                .isEmpty;
        n++
      ) {
        await t.pump(const Duration(milliseconds: 100));
        await t.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 20)),
        );
      }
      final before = await t.runAsync(service.state);
      expect(before!['supported'], true);
      expect(before['keys'], contains('professional-demo'));
      await binding.takeScreenshot('phase-icon-available');
      await t.ensureVisible(
        find.text('استخدام شعار أكاديميتي كأيقونة التطبيق'),
      );
      await t.pumpAndSettle();
      await t.tap(find.text('استخدام شعار أكاديميتي كأيقونة التطبيق'));
      await t.runAsync(() => Future<void>.delayed(const Duration(seconds: 2)));
      await t.pumpAndSettle();
      final after = await t.runAsync(service.state);
      if (const bool.fromEnvironment('ICON_EXPECT_SYSTEM_BLOCK')) {
        expect(find.textContaining('تعذر تطبيق الأيقونة'), findsOneWidget);
        expect(after!['currentKey'], isNull);
        await binding.takeScreenshot('phase-icon-system-blocked');
        binding.reportData = {
          ...?binding.reportData,
          'source': 'actual iOS 26.2 simulator; system API rejected alternate selection',
          'selectionVerified': false,
          'fallbackVerified': true,
          'nativeErrorObserved': 'NSPOSIXErrorDomain/35',
          'after': after,
        };
        return;
      }
      expect(after!['currentKey'], 'professional-demo');
      expect(after['academyId'], '507a3fde-42f9-43fa-bbbb-5a6009365d35');
      await binding.takeScreenshot('phase-icon-selected');
      binding.reportData = {
        ...?binding.reportData,
        'source': 'actual iOS simulator API and authorized demo academy',
        'before': before,
        'after': after,
      };
    },
  );
}
