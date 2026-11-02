import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/coach/coach_attendance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/home_destinations.dart';
import 'package:academy_mobile/features/owner/refined_home_panels.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'v45_native_compensation_test.dart' show CompensationAcceptanceTransport;

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'owner opens the actual historical player attendance and its exact session without changing attendance or money',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      final transport = CompensationAcceptanceTransport(
        AppConfig('Demo', base),
      );
      final auth = AuthController(
        transport,
        SecureRefreshStore('v45.5278.os-media-entry.01099900010'),
      );
      await auth.restore();
      expect(auth.session, isNotNull);
      expect(auth.session!.selected!.role, 'AcademyOwner');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      const enrollment = '676f7a72-b5bc-3aff-7cee-2b48d877d80f';
      const source = '8354c7dc-3a13-0d71-db92-c18a0ded4a7f';
      const player = '526d6af0-a898-0ab4-2b5f-80aa4494436f';
      const historyPath =
          '/api/v1/attendance/history?playerId=$player&sportId=95dd7371-1a24-3530-7a50-596def0c20ac&branchId=dc5c1688-5add-b52a-bf9d-bcddfbb6a10a';
      final beforeHistory = rows(await auth.request('GET', historyPath));
      final sourceHistory = beforeHistory.singleWhere(
        (h) => h['trainingSessionId'] == source,
      );
      expect(sourceHistory['status'], 'Absent');
      final attendance = obj(
        await auth.request(
          'GET',
          '/api/v1/attendance/sessions/$source/players',
        ),
      );
      final sourcePlayer = rows(attendance['players'])
          .singleWhere((p) => p['sportEnrollmentId'] == enrollment);
      expect(sourcePlayer['attendanceStatus'], 'Absent');
      expect(sourcePlayer['sessionConsumed'], false);
      for (final p in [
        ('63359353-a864-b606-b49e-37eccdf5056c', '2026-09-20', '2026-10-19'),
        ('2c03dd3c-5d00-b5c8-0893-09890ab40597', '2026-08-21', '2026-09-20'),
      ]) {
        final period = obj(
          await auth.request('GET', '/api/v1/subscriptions/periods/${p.$1}'),
        );
        expect(period['startDate'], p.$2);
        expect(period['endDate'], p.$3);
        expect(period['priceSnapshot'], 900);
        expect(period['remainingSessions'], null);
        expect(period['status'], 'Expired');
        expect(period['adjustments'], isEmpty);
      }
      final before = obj(
        await auth.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      expect(before['totalCount'], 71);
      expect(before['totalAmount'], 60285);
      expect(before['totalRefunds'], 1);
      expect(before['netAmount'], 60284);
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
        await t.scrollUntilVisible(f, 150, scrollable: scroll, maxScrolls: 120);
        await t.ensureVisible(f.last);
        await t.pumpAndSettle();
      }

      Future<void> tap(Finder f) async {
        await reach(f);
        await t.tap(f.last);
        await t.pump(const Duration(milliseconds: 400));
      }

      final appearance = Appearance();
      for (final theme in ['football', 'general']) {
        await appearance.completeRoleIntro('AcademyOwner', theme);
      }
      await t.pumpWidget(
        AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
      );
      await wait(find.text('ملخص الأكاديمية'));
      await reach(find.byType(HomeSubscriptions));
      await tap(find.textContaining(RegExp(r'^\d+ إجمالي$')));
      await wait(find.byType(HomeSubscriptionPlayers));
      await tap(find.widgetWithText(ListTile, 'علي أحمد إبراهيم'));
      await wait(find.byType(NativePlayerWorkspace));
      await tap(find.widgetWithText(TextButton, 'التدريب والحضور'));
      await tap(find.text('الحصص والحضور'));
      await wait(find.byType(NativePlayerSessions));
      await wait(find.text('سجل حضور اللاعب'));
      final sourceLabel = find.text('2026-10-14 · ناشئين 2016 — مجموعة أ');
      final card = find.ancestor(of: sourceLabel, matching: find.byType(GCard));
      await reach(card);
      expect(
        find.descendant(of: card, matching: find.text('غائب')),
        findsOneWidget,
      );
      expect(
        find.descendant(
          of: card,
          matching: find.text('لم تُخصم حصة من الرصيد'),
        ),
        findsOneWidget,
      );
      await t.runAsync(
        () => Future<void>.delayed(const Duration(milliseconds: 500)),
      );
      await binding.takeScreenshot(
        'phase-approved-native-player-attendance-history-w375',
      );
      await t.tap(
        find.descendant(of: card, matching: find.text('فتح كشف هذه الحصة')),
      );
      await t.pump(const Duration(milliseconds: 400));
      await wait(find.byType(CoachAttendance));
      expect(
        t.widget<CoachAttendance>(find.byType(CoachAttendance)).sessionId,
        source,
      );
      await reach(find.text('علي أحمد إبراهيم'));
      expect(find.text('حفظ الحضور'), findsNothing);
      await t.runAsync(
        () => Future<void>.delayed(const Duration(milliseconds: 500)),
      );
      await binding.takeScreenshot(
        'phase-approved-native-player-exact-attendance-session-w375',
      );
      expect(transport.commands, isEmpty);
      expect(rows(await auth.request('GET', historyPath)), beforeHistory);
      expect(
        obj(
          await auth.request(
            'GET',
            '/api/v1/attendance/sessions/$source/players',
          ),
        )['players'],
        attendance['players'],
      );
      final after = obj(
        await auth.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      for (final k in [
        'totalCount',
        'totalAmount',
        'totalRefunds',
        'netAmount',
      ]) {
        expect(after[k], before[k]);
      }
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox.shrink());
      auth.dispose();
    },
  );
}
