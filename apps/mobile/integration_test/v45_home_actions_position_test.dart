import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_home_settings.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'owner saves native quick action placement, reopens and restores it',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final auth = AuthController(
        HttpApiTransport(AppConfig('Demo', base)),
        SecureRefreshStore('v45.5278.os-media-entry.01099900010'),
      );
      await auth.restore();
      if (auth.session == null) {
        final challenge = await auth.requestOtp('01099900010');
        await auth.verify('01099900010', challenge, '246810');
      }
      expect(auth.session!.selected!.role, 'AcademyOwner');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      Future<Map<String, dynamic>> active() async => rows(
        obj(
          await auth.request('GET', '/api/v1/manage/configuration'),
        )['versions'],
      ).singleWhere((r) => r['state'] == 'Active');
      final initial = await active();
      final originalHome = obj(obj(initial['settings'])['ownerHome'] ?? {});
      final original = originalHome['quickActionsAtTop'] == true;
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> show(Widget page) async {
        await t.pumpWidget(const SizedBox());
        await t.pumpAndSettle();
        await t.pumpWidget(
          MaterialApp(
            locale: const Locale('ar'),
            supportedLocales: const [Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            theme: AcademyTheme.dark(),
            home: page,
          ),
        );
      }

      Future<void> save(bool value) async {
        await show(OwnerHomeSettingsPage(auth: auth));
        await wait(find.byType(SwitchListTile));
        if (t.widget<SwitchListTile>(find.byType(SwitchListTile)).value !=
            value) {
          await t.tap(find.byType(SwitchListTile));
          await t.pumpAndSettle();
        }
        await t.ensureVisible(find.text('حفظ الإعدادات'));
        await t.tap(find.text('حفظ الإعدادات'));
        await t.pump(const Duration(milliseconds: 350));
        await wait(find.text('تم حفظ إعدادات الرئيسية.'));
        final read = await active();
        final home = obj(obj(read['settings'])['ownerHome']);
        expect(home['quickActionsAtTop'], value);
        expect(
          home['coachAssignmentTarget'],
          originalHome['coachAssignmentTarget'],
        );
        await show(OwnerHomeSettingsPage(auth: auth));
        await wait(find.byType(SwitchListTile));
        expect(
          t.widget<SwitchListTile>(find.byType(SwitchListTile)).value,
          value,
        );
      }

      try {
        await save(!original);
        await binding.takeScreenshot('phase-approved-home-position-reopened');
        await show(OwnerDashboard(auth: auth, accountPage: const SizedBox()));
        await wait(find.text('ملخص الأكاديمية'));
        if (!original) {
          expect(find.text('الإجراءات السريعة'), findsWidgets);
          expect(
            t.getTopLeft(find.text('الإجراءات السريعة').first).dy,
            lessThan(t.getTopLeft(find.text('ملخص الأكاديمية').first).dy),
          );
        } else {
          final scroll = find.byType(Scrollable).first;
          await t.scrollUntilVisible(
            find.text('الإجراءات السريعة'),
            350,
            scrollable: scroll,
            maxScrolls: 120,
          );
        }
        await binding.takeScreenshot('phase-approved-home-position-applied');
      } finally {
        await save(original);
        final restored = obj(obj((await active())['settings'])['ownerHome']);
        expect(restored, originalHome);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      }
      expect(t.takeException(), isNull);
    },
  );
}
