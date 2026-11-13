import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/coach/coach_home.dart';
import 'package:academy_mobile/features/coach/monthly_stars.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'v45_single_renewal_saved_receipt_test.dart'
    show SavedRenewalReadTransport;

// This journey accepts the already-tested introduction without changing any
// normal completion key, and changes appearance in memory only.
class OverviewAppearance extends Appearance {
  @override
  Future<bool> hasRoleIntro(String role, String sportTheme) async => true;
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'actual coach home leads to assigned groups and keeps monthly stars in More',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      const width = int.fromEnvironment('COACH_ACCEPTANCE_WIDTH');
      expect([375, 440], contains(width));
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, width);
      final transport = SavedRenewalReadTransport(AppConfig('Demo', base));
      final auth = AuthController(
        transport,
        SecureRefreshStore('v73.5278.approved-intro.Coach'),
      );
      await auth.restore();
      if (auth.session == null) {
        final challenge = await auth.requestOtp('01099900020');
        await auth.verify('01099900020', challenge, '246810');
      }
      expect(auth.session!.selected!.role, 'Coach');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final budgets = obj(
        await auth.request('GET', '/api/v1/coach/monthly-stars'),
      );
      final teams = rows(budgets['teams']);
      final groups = rows(await auth.request('GET', '/api/v1/coach/groups'));
      expect(groups, isNotEmpty);
      Future<void> wait(Finder target) async {
        for (var i = 0; i < 240 && target.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(target, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> reveal(Finder target) async {
        if (target.evaluate().isEmpty) {
          await t.scrollUntilVisible(
            target,
            180,
            scrollable: find.byType(Scrollable).first,
            maxScrolls: 80,
          );
        }
        await t.ensureVisible(target);
        await t.pumpAndSettle();
      }

      Future<void> capture(String suffix, String mode) async {
        for (var i = 0; i < 200; i++) {
          final images = t.widgetList<RawImage>(find.byType(RawImage)).toList();
          if (images.isNotEmpty && images.every((im) => im.image != null)) {
            break;
          }
          await t.pump(const Duration(milliseconds: 100));
        }
        final images = t.widgetList<RawImage>(find.byType(RawImage)).toList();
        expect(images, isNotEmpty);
        expect(images.every((im) => im.image != null), true);
        await t.pump(const Duration(milliseconds: 100));
        expect(t.takeException(), isNull);
        await binding.takeScreenshot(
          'phase-approved-coach-overview-$width-$mode-$suffix',
        );
      }

      Future<void> back() async {
        await t.tap(find.byTooltip('رجوع').last);
        await t.pumpAndSettle();
      }

      const contrastOnly = bool.fromEnvironment('STARS_CONTRAST_ONLY');
      for (final dark in [
        false,
        true,
      ].where((dark) => !contrastOnly || !dark)) {
        final mode = dark ? 'dark' : 'light',
            prefs = OverviewAppearance()..dark = dark;
        await t.pumpWidget(
          AcademyApp(auth: auth, appearance: prefs, environment: 'Demo'),
        );
        await wait(find.byType(CoachGroupCard));
        expect(find.text('العمليات السريعة'), findsNothing);
        expect(find.text('نجوم فريقي الشهرية'), findsNothing);
        if (!contrastOnly) {
          final openGroup = find.descendant(
            of: find.byKey(ValueKey('coach-group-${groups.first['id']}')),
            matching: find.text('فتح المجموعة'),
          );
          await reveal(openGroup);
          await capture('groups', mode);
          await t.tap(openGroup);
          await wait(find.byType(CoachGroup));
          expect(
            t.widget<CoachGroup>(find.byType(CoachGroup)).group['id'],
            groups.first['id'],
          );
          await capture('group', mode);
          await back();
        }
        await t.tap(find.text('المزيد'));
        await t.pumpAndSettle();
        final openStars = find.text('نجوم فريقي الشهرية');
        await reveal(openStars);
        await t.tap(openStars);
        await wait(find.byType(CoachMonthlyStars));
        if (teams.isNotEmpty) {
          await wait(
            find.textContaining(
              '${teams.first['name']} · ${teams.first['remaining']} نجوم متبقية',
            ),
          );
        } else {
          await wait(find.text('لا توجد فرق مسندة إليك.'));
        }
        if (contrastOnly) {
          await capture('stars-contrast-ready', mode);
        } else {
          await capture('stars', mode);
        }
        await back();
        await wait(openStars);
        final after = obj(
          await auth.request('GET', '/api/v1/coach/monthly-stars'),
        );
        expect(
          rows(after['teams'])
              .map((x) => '${x['id']}:${x['remaining']}')
              .toList(),
          teams.map((x) => '${x['id']}:${x['remaining']}').toList(),
        );
        await t.pumpWidget(const SizedBox());
        prefs.dispose();
      }
      auth.dispose();
      transport.close();
    },
    timeout: const Timeout(Duration(minutes: 6)),
  );
}
