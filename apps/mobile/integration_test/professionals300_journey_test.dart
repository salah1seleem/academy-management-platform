import '../test/introduction_test_helpers.dart';
import 'startup_motion_journey_test.dart'
    show StartupJourneyStore, StartupReadTransport;

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/shared/academy_reveal.dart';
import 'package:academy_mobile/features/auth/login_page.dart';
import 'package:academy_mobile/features/coach/coach_attendance.dart';
import 'package:academy_mobile/features/guardian/guardian_profile.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'four native Homes read the actual UI-imported 300-player tenant',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const academy = "507a3fde-42f9-43fa-bbbb-5a6009365d35";
      final api = StartupReadTransport(
        AppConfig('Demo', 'http://127.0.0.1:5278'),
      );
      final prefs = Appearance();
      Future<void> tick(int milliseconds) async {
        for (var n = 0; n < milliseconds; n += 50) {
          await t.pump(const Duration(milliseconds: 50));
        }
      }

      Future<void> wait(Finder target) async {
        for (var n = 0; n < 400 && target.evaluate().isEmpty; n++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(target, findsWidgets);
      }

      final reports = <String, Object?>{};
      for (final item in [
        ('AcademyOwner', '01084720300', 'owner'),
        ('AcademyAdmin', '01084720301', 'admin'),
        ('Coach', "01526189023", 'coach'),
        ('Guardian', "01526189023", 'guardian'),
      ]) {
        final auth = AuthController(api, StartupJourneyStore());
        if (item.$3 == 'owner') {
          await t.pumpWidget(
            AcademyApp(auth: auth, environment: 'Demo', appearance: prefs),
          );
          await auth.restore();
          await wait(find.byType(LoginPage));
          await t.enterText(find.byType(TextFormField), item.$2);
          await binding.takeScreenshot('phase-import-owner-login');
          await t.ensureVisible(find.byKey(const ValueKey('login-submit')));
          await t.tap(find.byKey(const ValueKey('login-submit')));
          await wait(find.byKey(const ValueKey('login-six-code-boxes')));
          await binding.takeScreenshot('phase-import-owner-otp');
          await t.enterText(find.byType(TextFormField), '246810');
          await t.ensureVisible(find.byKey(const ValueKey('login-submit')));
          await t.tap(find.byKey(const ValueKey('login-submit')));
        } else {
          final challenge = await auth.requestOtp(item.$2);
          await auth.verify(item.$2, challenge, '246810');
          if (auth.session?.selected?.academyId != academy ||
              auth.session?.selected?.role != item.$1) {
            await auth.selectRole(
              auth.session!.memberships
                  .firstWhere(
                    (m) => m.academyId == academy && m.role == item.$1,
                  )
                  .id,
            );
          }
          auth.restoring = false;
          await t.pumpWidget(
            AcademyApp(auth: auth, environment: 'Demo', appearance: prefs),
          );
        }
        await wait(find.byType(AcademyOnboarding));
        expect(auth.session!.selected!.academyId, academy);
        expect(auth.session!.selected!.role, item.$1);
        await binding.takeScreenshot('phase-import-${item.$3}-onboarding');
        await finishRoleIntroduction(t);
        await wait(find.byType(AcademyReveal));
        await tick(650);
        await binding.takeScreenshot('phase-import-${item.$3}-reveal');
        await wait(find.byType(NativeRoleTabs));
        await wait(
          item.$3 == 'owner'
              ? find.text('305')
              : item.$3 == 'admin'
              ? find.text('100')
              : item.$3 == 'coach'
              ? find.textContaining('مجموعة الكرة 01')
              : find.text('لاعب خيالي 001'),
        );
        await tick(1000);
        await binding.takeScreenshot('phase-import-${item.$3}-home');
        expect(t.takeException(), isNull);
        final data = await auth.request(
          'GET',
          item.$1 == 'Guardian'
              ? '/api/v1/guardian/home'
              : item.$1 == 'Coach'
              ? '/api/v1/attendance/sessions?nearestOnly=true'
              : '/api/v1/subscriptions/home',
        );
        reports[item.$3] = data;
        if (item.$1 == 'AcademyOwner') {
          expect((data as Map)['total'], 305);
          expect(data['active'], 255);
          expect(data['expired'], 50);
          expect(data['players'], 300);
        }
        if (item.$1 == 'AcademyAdmin') {
          expect((data as Map)['total'], 100);
          expect(data['players'], 100);
        }
        if (item.$3 == 'coach') {
          await t.tap(find.text('فتح الحصة'));
          await wait(find.byType(CoachAttendance));
          await wait(find.text('لاعب خيالي 001'));
          await tick(500);
          await binding.takeScreenshot('phase-import-coach-roster');
        }
        if (item.$3 == 'guardian') {
          await t.tap(find.byType(DropdownButtonFormField<String>).first);
          await tick(400);
          await t.tap(find.textContaining('كرة القدم').last);
          await tick(700);
          await binding.takeScreenshot('phase-import-guardian-football-home');
          await t.ensureVisible(find.text('عرض الملف').first);
          await t.tap(find.text('عرض الملف').first);
          await wait(find.byType(ChildProfile));
          await tick(1500);
          await binding.takeScreenshot('phase-import-guardian-child-profile');
        }
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      }
      binding.reportData ??= <String, dynamic>{};
      binding.reportData!['source'] = 'Actual platform UI-imported synthetic workbook; real mobile auth/API; no business fixture writes';
      // Only bounded counts are included in driver metadata; screenshots retain the live UI.
      binding.reportData!['verifiedRoles'] = reports.keys.toList();
      prefs.dispose();
    },
  );
}
