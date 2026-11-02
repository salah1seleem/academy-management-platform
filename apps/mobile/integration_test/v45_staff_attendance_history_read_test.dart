import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/native_staff_attendance.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:academy_mobile/features/shared/staff_attendance_exceptions.dart';
import 'package:academy_mobile/features/shared/staff_work_periods.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class StaffReadAcceptanceTransport extends HttpApiTransport {
  StaffReadAcceptanceTransport(super.config);
  final writes = <String>[];
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    if (method != 'GET' && path.startsWith('/api/v1/staff/')) {
      writes.add('$method $path');
    }
    return super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'real staff attendance help, history and exception destinations remain Arabic, usable and read only at375',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      final transport = StaffReadAcceptanceTransport(AppConfig('Demo', base));
      final auth = AuthController(
        transport,
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
      final before = obj(
        await auth.request('GET', NativeStaffAttendanceFlow.root),
      );
      Future<void> wait(Finder f) async {
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

      Future<void> tap(String title) async {
        final f = find.text(title);
        await reach(f);
        await t.tap(f.last);
        await t.pump(const Duration(milliseconds: 400));
      }

      Future<void> capture(String name) async {
        await t.pumpAndSettle();
        await t.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 500)),
        );
        await binding.takeScreenshot(name);
      }

      final appearance = Appearance();
      for (final theme in ['football', 'general']) {
        await appearance.completeRoleIntro('Coach', theme);
      }
      await t.pumpWidget(
        AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
      );
      await wait(find.byType(NativeRoleTabs));
      await wait(find.text('المجموعات المسندة'));
      await tap('حضور وانصراف');
      await wait(find.byType(NativeStaffAttendancePage));
      await wait(find.text('كيف يُثبت حضورك؟'));
      expect(find.textContaining('لا نخزن بصمات أو صور وجوه'), findsNothing);
      expect(
        find.textContaining('إعداد الحضور الآمن غير مكتمل'),
        findsOneWidget,
      );
      await capture('phase-approved-staff-attendance-compact-help-w375');
      await tap('كيف يُثبت حضورك؟');
      await t.pumpAndSettle();
      expect(find.textContaining('لا نخزن بصمات أو صور وجوه'), findsOneWidget);
      await tap('كيف يُثبت حضورك؟');
      await t.pumpAndSettle();
      await tap('سجل الحضور');
      await wait(find.byType(StaffWorkPeriodsPage));
      await t.pump(const Duration(seconds: 1));
      expect(find.text('سجل الحضور'), findsOneWidget);
      expect(find.text('فترات عملي'), findsNothing);
      await capture('phase-approved-staff-attendance-history-native-w375');
      Navigator.of(t.element(find.byType(StaffWorkPeriodsPage))).pop();
      await t.pumpAndSettle();
      await tap('طلب إثبات حضور للمراجعة');
      await wait(find.byType(StaffAttendanceExceptionPage));
      await t.pump(const Duration(seconds: 1));
      expect(find.text('طلب إثبات حضوري'), findsOneWidget);
      await capture('phase-approved-staff-attendance-claim-read-native-w375');
      expect(transport.writes, isEmpty);
      final after = obj(
        await auth.request('GET', NativeStaffAttendanceFlow.root),
      );
      expect(after['hasOpenAttendance'], before['hasOpenAttendance']);
      expect(after['recent'], before['recent']);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox.shrink());
      auth.dispose();
    },
  );
}
