import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_home_settings.dart';
import 'package:academy_mobile/features/owner/collection_target.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:academy_mobile/features/owner/refined_home_panels.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'owner verifies home placement and scoped target, reopens and restores both',
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

      final options = obj(
        await auth.request('GET', '/api/v1/manage/structure/options'),
      );
      final branch =
          rows(options['branches'])
                  .singleWhere((b) => b['arabicName'] == 'فرع الشروق')['id']
              as String;
      final originalTargets = rows(originalHome['targets']);
      final prior = originalTargets
          .where(
            (x) =>
                x['from'] == '2026-09-01' &&
                x['to'] == '2026-09-30' &&
                x['branchId'] == branch,
          )
          .firstOrNull;
      final oldAmount = prior?['amount'];
      final nextAmount = oldAmount == 16000 ? 17000 : 16000;
      Future<void> target(Object? amount) async {
        bool? closed;
        await show(
          Builder(
            builder: (context) => Scaffold(
              body: TextButton(
                onPressed: () async {
                  closed = await Navigator.push<bool>(
                    context,
                    MaterialPageRoute(
                      builder: (_) => CollectionTargetPage(
                        auth: auth,
                        from: '2026-09-01',
                        to: '2026-09-30',
                        branchId: branch,
                        branchName: 'فرع الشروق',
                      ),
                    ),
                  );
                },
                child: const Text('فتح هدف الفترة'),
              ),
            ),
          ),
        );
        await t.tap(find.text('فتح هدف الفترة'));
        await wait(find.byType(TextFormField));
        await t.enterText(find.byType(TextFormField), amount?.toString() ?? '');
        await binding.takeScreenshot(
          amount == nextAmount
              ? 'phase-approved-target-readback-form'
              : 'phase-approved-target-restored-form',
        );
        await t.ensureVisible(find.text('حفظ الهدف'));
        await t.tap(find.text('حفظ الهدف'));
        await wait(find.text('فتح هدف الفترة'));
        expect(closed, true);
        final saved = obj(obj((await active())['settings'])['ownerHome']);
        final exact = rows(saved['targets'])
            .where(
              (x) =>
                  x['from'] == '2026-09-01' &&
                  x['to'] == '2026-09-30' &&
                  x['branchId'] == branch,
            )
            .toList();
        if (amount == null) {
          expect(exact, isEmpty);
        } else {
          expect(exact.single['amount'], amount);
        }
        final cockpit = obj(
          await auth.request(
            'GET',
            '/api/v1/reports/owner-cockpit?from=2026-09-01&to=2026-09-30&branchId=$branch',
          ),
        );
        if (amount != null) expect(obj(cockpit['target'])['amount'], amount);
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
        await target(nextAmount);
        final cockpit = obj(
          await auth.request(
            'GET',
            '/api/v1/reports/owner-cockpit?from=2026-09-01&to=2026-09-30&branchId=$branch',
          ),
        );
        await show(
          GuardianPage(
            title: 'ملخص فرع الشروق',
            child: SingleChildScrollView(
              child: AcademySummaryPanel(
                data: cockpit,
                onReport: (_) {},
                onCapacity: () {},
                onTarget: () {},
                onWeek: (_) {},
              ),
            ),
          ),
        );
        await wait(find.text('هدف الفترة'));
        await binding.takeScreenshot('phase-approved-target-readback-applied');
      } finally {
        await target(oldAmount);
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
