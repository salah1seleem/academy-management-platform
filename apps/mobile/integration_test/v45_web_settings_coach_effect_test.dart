import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/academy_brand.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/coach/coach_home.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/native_staff_attendance.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

// A coordinated browser/native acceptance test. Changes are made through the
// real web settings controls; this test only reads them and uses native screens.
void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'saved web attendance policies affect the real coach app and restore safely',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      final auth = AuthController(
        HttpApiTransport(AppConfig('Demo', base)),
        SecureRefreshStore('v45.5278.web-settings.coach20'),
      );
      await auth.restore();
      if (auth.session == null) {
        final c = await auth.requestOtp('01099900020');
        await auth.verify('01099900020', c, '246810');
      }
      expect(auth.session!.selected!.role, 'Coach');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final initial = obj(
        await auth.request('GET', NativeStaffAttendanceFlow.root),
      );
      expect(initial['hasOpenAttendance'], isFalse);
      expect(
        rows(initial['branches']).any((b) => b['enabled'] == true),
        isTrue,
      );
      final branchIds = rows(initial['branches']).map((b) => b['id']).toSet();
      debugPrint(
        'Synthetic coach staff branches: ${rows(initial['branches']).map((b) => '${b['id']} ${b['name']} ${b['enabled']}').join(' / ')}',
      );
      Future<void> waitFor(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> reach(Finder f) async {
        final scroll = find.byType(Scrollable).first;
        t.state<ScrollableState>(scroll).position.jumpTo(0);
        await t.pumpAndSettle();
        await t.scrollUntilVisible(f, 150, scrollable: scroll, maxScrolls: 100);
        await t.ensureVisible(f.last);
        await t.pumpAndSettle();
      }

      Future<void> show(String key, bool coachAttendance) async {
        final appearance = Appearance();
        for (final theme in ['football', 'general']) {
          await appearance.completeRoleIntro('Coach', theme);
        }
        await t.pumpWidget(
          AcademyApp(
            key: ValueKey(key),
            auth: auth,
            environment: 'Demo',
            appearance: appearance,
          ),
        );
        await waitFor(find.byType(NativeRoleTabs));
        await waitFor(find.text('تدريباتي'));
        await reach(find.text('تدريباتي'));
        final brand = BrandScope.maybeOf(t.element(find.byType(CoachHome)))!;
        for (var i = 0; i < 160 && brand.loading; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(brand.coachAttendance, coachAttendance);
      }

      Future<void> awaitPolicy(bool coach, bool anyStaff, String stage) async {
        debugPrint(
          'Awaiting web policy $stage: coach=$coach, anyStaff=$anyStaff',
        );
        final deadline = DateTime.now().add(const Duration(minutes: 4));
        while (DateTime.now().isBefore(deadline)) {
          final brand = obj(
            await auth.request('GET', '/api/v1/academy/experience'),
          );
          final staff = obj(
            await auth.request('GET', NativeStaffAttendanceFlow.root),
          );
          expect(
            rows(staff['branches']).map((b) => b['id']).toSet(),
            branchIds,
          );
          if (brand['coachAttendanceEnabled'] == coach &&
              rows(staff['branches']).any((b) => b['enabled'] == true) ==
                  anyStaff) {
            return;
          }
          await t.pump(const Duration(seconds: 1));
        }
        fail('The coordinated web policy stage did not complete: $stage');
      }

      await awaitPolicy(true, true, 'enabled');
      expect(
        await auth.request('GET', '/api/v1/my-permissions'),
        contains('attendance.manage'),
      );
      await show('enabled', true);
      await reach(find.text('حضور وانصراف'));
      await binding.takeScreenshot(
        'phase-approved-coach-web-policy-enabled-w375',
      );
      final groups = rows(await auth.request('GET', '/api/v1/coach/groups'));
      expect(groups, isNotEmpty);
      final assigned = find.descendant(
        of: find.byKey(ValueKey('coach-group-${groups.first['id']}')),
        matching: find.text('فتح المجموعة'),
      );
      await reach(assigned);
      await t.tap(assigned);
      await waitFor(find.byType(CoachGroup));
      await waitFor(find.textContaining('لاعب في المجموعة'));
      await binding.takeScreenshot(
        'phase-approved-coach-assigned-group-context-w375',
      );
      Navigator.of(t.element(find.byType(CoachGroup))).pop();
      await t.pumpAndSettle();

      await awaitPolicy(false, false, 'disabled');
      expect(
        await auth.request('GET', '/api/v1/my-permissions'),
        isNot(contains('attendance.manage')),
      );
      await show('disabled', false);
      final scroll = find.byType(Scrollable).first;
      t.state<ScrollableState>(scroll).position.jumpTo(0);
      await t.pumpAndSettle();
      expect(find.text('حضور وانصراف'), findsNothing);
      await binding.takeScreenshot(
        'phase-approved-coach-web-policy-disabled-w375',
      );
      debugPrint(
        'Native disabled stage accepted; restore original staff flags and keep original coach=false.',
      );
      await awaitPolicy(false, true, 'restored');
      await show('restored', false);
      await reach(find.text('حضور وانصراف'));
      await t.tap(find.text('حضور وانصراف'));
      await waitFor(find.byType(NativeStaffAttendancePage));
      await t.pump(const Duration(seconds: 1));
      await binding.takeScreenshot(
        'phase-approved-staff-check-in-honest-device-state-w375',
      );
      // No credential, location or biometric assertion is synthesized by this test.
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox.shrink());
      auth.dispose();
    },
  );
}
