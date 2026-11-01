import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:academy_mobile/features/coach/coach_home.dart';
import 'package:academy_mobile/features/admin/admin_home_overview.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/community_ads.dart';
import 'package:academy_mobile/features/shared/native_staff_attendance.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:academy_mobile/features/shared/staff_home_action.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'v45_single_renewal_saved_receipt_test.dart'
    show SavedRenewalReadTransport;
import 'v49_owner_compact_rails_test.dart' as owner_small;

class ForwardLinkAppearance extends Appearance {
  @override
  Future<bool> hasRoleIntro(String role, String sportTheme) async => true;
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  const width = int.fromEnvironment('FORWARD_ACCEPTANCE_WIDTH');
  if (width == 375) owner_small.main();
  testWidgets(
    'forward entries retain real role destinations and back across appearances',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect([375, 440], contains(width));
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, width);
      t.platformDispatcher.accessibilityFeaturesTestValue =
          const FakeAccessibilityFeatures(disableAnimations: true);
      addTearDown(t.platformDispatcher.clearAccessibilityFeaturesTestValue);
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
            170,
            scrollable: find.byType(Scrollable).first,
            maxScrolls: 80,
          );
        }
        await t.ensureVisible(target);
        await t.pumpAndSettle();
      }

      Future<void> capture(String name) async {
        for (var i = 0; i < 200; i++) {
          final images = t.widgetList<RawImage>(find.byType(RawImage)).toList();
          if (images.isNotEmpty && images.every((im) => im.image != null)) {
            break;
          }
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(
          t
              .widgetList<RawImage>(find.byType(RawImage))
              .every((im) => im.image != null),
          true,
        );
        expect(t.takeException(), isNull);
        await binding.takeScreenshot('phase-approved-forward-$width-$name');
      }

      for (final sample in [
        ('AcademyOwner', '01099900010', 'owner'),
        ('AcademyAdmin', '01099900011', 'admin'),
        ('Coach', '01099900020', 'coach'),
        ('Guardian', '01099900100', 'guardian'),
      ]) {
        final transport = SavedRenewalReadTransport(AppConfig('Demo', base));
        final auth = AuthController(
          transport,
          SecureRefreshStore('v73.5278.approved-intro.${sample.$1}'),
        );
        await auth.restore();
        if (auth.session == null) {
          final challenge = await auth.requestOtp(sample.$2);
          await auth.verify(sample.$2, challenge, '246810');
        }
        expect(auth.session!.selected!.role, sample.$1);
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        for (final dark in [false, true]) {
          final appearance = ForwardLinkAppearance()..dark = dark;
          final mode = dark ? 'dark' : 'light';
          await t.pumpWidget(
            AcademyApp(auth: auth, appearance: appearance, environment: 'Demo'),
          );
          await wait(find.byType(NativeRoleTabs));
          switch (sample.$1) {
            case 'AcademyOwner':
              await wait(find.byType(OwnerDashboard));
              await wait(find.text('ملخص الأكاديمية'));
            case 'AcademyAdmin':
              await wait(find.byType(AdminAttendanceOverview));
            case 'Coach':
              await wait(find.byType(CoachHome));
              await wait(find.widgetWithText(ChoiceChip, 'المجموعات'));
            case 'Guardian':
              await wait(find.byType(GuardianHome));
              await wait(find.byKey(const Key('guardian-urgent-summary')));
          }
          await capture('${sample.$3}-$mode-home');
          if (sample.$1 == 'AcademyAdmin' || sample.$1 == 'Coach') {
            final entry = find.descendant(
              of: find.byType(StaffHomeAttendanceAction),
              matching: find.byType(GCard),
            );
            await reveal(entry);
            await t.tap(entry);
            await wait(find.byType(NativeStaffAttendancePage));
            await wait(find.text('حضوري وانصرافي'));
            await t.tap(find.byType(BackButton));
            await wait(find.byType(NativeRoleTabs));
          }
          if (sample.$1 == 'Coach') {
            final groups = find.widgetWithText(ChoiceChip, 'المجموعات');
            await reveal(groups);
            await t.tap(groups);
            await t.pumpAndSettle();
            final group = find
                .ancestor(
                  of: find.textContaining('تسجيل لاعب'),
                  matching: find.byType(ListTile),
                )
                .first;
            await reveal(group);
            await capture('coach-$mode-groups');
            await t.tap(group);
            await wait(find.byType(CoachGroup));
            await t.tap(find.byType(BackButton));
            await wait(find.byType(CoachHome));
          }
          if (sample.$1 == 'Guardian') {
            await t.tap(find.byKey(const Key('guardian-urgent-summary')));
            await wait(find.byType(NotificationInbox));
            expect(
              t
                  .widget<NotificationInbox>(find.byType(NotificationInbox))
                  .urgentMatchesOnly,
              true,
            );
            await t.tap(find.byType(BackButton));
            await wait(find.byType(GuardianUrgentMatchAlerts));
          }
          await t.pumpWidget(const SizedBox());
          await t.pumpAndSettle();
          appearance.dispose();
        }
        auth.dispose();
        transport.close();
      }
    },
    timeout: const Timeout(Duration(minutes: 12)),
  );
}
