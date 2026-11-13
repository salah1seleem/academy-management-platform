import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/admin_home.dart';
import 'package:academy_mobile/features/admin/admin_home_overview.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'v45_single_renewal_saved_receipt_test.dart'
    show SavedRenewalReadTransport;

class AdminOverviewAppearance extends Appearance {
  @override
  Future<bool> hasRoleIntro(String role, String sportTheme) async => true;
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'actual administrator home uses permitted session/payment sources and keeps read-only routes across both appearances',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      const width = int.fromEnvironment('ADMIN_ACCEPTANCE_WIDTH');
      expect([375, 440], contains(width));
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, width);
      final transport = SavedRenewalReadTransport(AppConfig('Demo', base));
      final auth = AuthController(
        transport,
        SecureRefreshStore('v73.5278.approved-intro.AcademyAdmin'),
      );
      await auth.restore();
      if (auth.session == null) {
        final challenge = await auth.requestOtp('01099900011');
        await auth.verify('01099900011', challenge, '246810');
      }
      expect(auth.session!.selected!.role, 'AcademyAdmin');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final permissions = (await auth.request(
        'GET',
        '/api/v1/my-permissions',
      ) as List).cast<String>();
      expect(permissions, contains('attendance.read'));
      expect(permissions, contains('payment.read'));
      final today = rows(
        await auth.request('GET', '/api/v1/attendance/sessions'),
      ).where((s) => s['status'] != 'Cancelled').toList();
      final recorded = today
          .where((s) => (s['recorded'] as num? ?? 0) > 0)
          .length;
      final pending = today
          .where(
            (s) =>
                (s['playerCount'] as num? ?? 0) > (s['recorded'] as num? ?? 0),
          )
          .length;
      final payment = obj(
        await auth.request(
          'GET',
          '/api/v1/manage/payment-requests?status=Pending&allDates=true',
        ),
      );
      final calendar = obj(
        await auth.request('GET', '/api/v1/academy/calendar'),
      );
      final date = textOf(calendar['today']);
      final history = rows(
        await auth.request(
          'GET',
          '/api/v1/attendance/sessions?from=${date.substring(0, 7)}-01&to=$date',
        ),
      );
      final staff = history.expand((s) => rows(s['staff'])).toList();
      expect(
        staff,
        isNotEmpty,
        reason: 'Verify additive role from actual recorded-month response, even when today has no sessions.',
      );
      expect(
        staff.every(
          (m) => ['Coach', 'AcademyAdmin', 'AcademyOwner'].contains(m['role']),
        ),
        true,
      );
      Future<void> wait(Finder target) async {
        for (var i = 0; i < 240 && target.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(target, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> reveal(Finder target, {bool up = false}) async {
        if (target.evaluate().isEmpty) {
          await t.scrollUntilVisible(
            target,
            up ? -180 : 180,
            scrollable: find.byType(Scrollable).first,
            maxScrolls: 80,
          );
        }
        await t.ensureVisible(target);
        await t.pumpAndSettle();
      }

      Future<void> capture(String name, String mode) async {
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
        await binding.takeScreenshot(
          'phase-approved-admin-overview-$width-$mode-$name',
        );
      }

      const directionOnly = bool.fromEnvironment('ADMIN_DIRECTION_ONLY');
      const linkOnly = bool.fromEnvironment('SHARED_LINK_ONLY');
      for (final dark in [false, true]) {
        final prefs = AdminOverviewAppearance()..dark = dark;
        final mode = dark ? 'dark' : 'light';
        await t.pumpWidget(
          AcademyApp(auth: auth, appearance: prefs, environment: 'Demo'),
        );
        await wait(find.byType(AdminAttendanceOverview));
        if (linkOnly) {
          final players = find.widgetWithText(GLink, 'اللاعبون');
          await reveal(players);
          await capture('shared-link', mode);
          await t.tap(players);
          await wait(find.byType(NativePlayerDirectory));
          await wait(find.byType(TextField));
          await capture('shared-link-destination', mode);
          await t.tap(find.byTooltip('رجوع').last);
          await wait(find.byType(AdminHome));
          await reveal(find.widgetWithText(GLink, 'اللاعبون'));
          await t.pumpWidget(const SizedBox());
          prefs.dispose();
          continue;
        }

        await reveal(find.byType(AdminAttendanceOverview));
        expect(
          t
              .widget<Text>(
                find.byKey(const ValueKey('admin-recorded-session-count')),
              )
              .data,
          '$recorded من ${today.length}',
        );
        if (!directionOnly) {
          await capture('attendance', mode);
        }
        final attendanceTask = find.byKey(
          const ValueKey('admin-home-attendance-task'),
        );
        await reveal(attendanceTask);
        expect(t.widget<AdminHomeTask>(attendanceTask).count, pending);
        final paymentTask = find.byKey(
          const ValueKey('admin-home-payments-task'),
        );
        await reveal(paymentTask);
        expect(
          t.widget<AdminHomeTask>(paymentTask).count,
          (payment['pending'] as num).toInt(),
        );
        expect(
          find.text('متابعاتي المسندة').evaluate().isNotEmpty,
          permissions.contains('followups.read'),
        );
        if (directionOnly) {
          await capture('tasks-direction-ready', mode);
          await t.pumpWidget(const SizedBox());
          prefs.dispose();
          continue;
        }
        await capture('tasks', mode);
        await reveal(find.widgetWithText(FilledButton, 'عرض الطلبات'));
        await t.tap(find.widgetWithText(FilledButton, 'عرض الطلبات'));
        await wait(find.byType(NativePaymentRequests));
        await wait(find.text('طلبات معلّقة (${payment['pending']})'));
        await capture('payments', mode);
        await t.tap(find.byTooltip('رجوع').last);
        await wait(find.byType(AdminHome));
        await reveal(find.widgetWithText(FilledButton, 'عرض الحصص'), up: true);
        await t.tap(find.widgetWithText(FilledButton, 'عرض الحصص'));
        await wait(find.byType(NativeSessions));
        if (today.isEmpty) {
          await wait(find.text('لا توجد حصص اليوم.'));
        } else {
          await wait(
            find.textContaining('${today.length} حصة مرتبة حسب الوقت'),
          );
        }
        await capture('sessions', mode);
        await t.tap(find.byTooltip('رجوع').last);
        await wait(find.byType(AdminHome));
        await t.pumpWidget(const SizedBox());
        prefs.dispose();
      }
      auth.dispose();
      transport.close();
    },
    timeout: const Timeout(Duration(minutes: 6)),
  );
}
