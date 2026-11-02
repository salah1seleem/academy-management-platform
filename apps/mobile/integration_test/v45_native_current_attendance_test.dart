import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/admin_home.dart';
import 'package:academy_mobile/features/coach/coach_attendance.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/guardian/guardian_profile.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class CurrentAttendanceTransport extends HttpApiTransport {
  CurrentAttendanceTransport(super.config);
  final writes = <({String path, Json body})>[];
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    final result = await super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
    if (method == 'PUT' &&
        path.endsWith('/players') &&
        path.contains('/attendance/sessions/')) {
      writes.add((path: path, body: obj(body)));
    }
    return result;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'authorized admin records one current synthetic attendance, replay consumes once and linked guardian reads the actual record',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      const date = '2026-10-22';
      expect(
        day(DateTime.now().toIso8601String()),
        date,
        reason: 'Do not change attendance on another day or bypass past correction.',
      );
      const player = '14204d10-9fcd-42d2-8452-72a94178976b';
      const enrollment = '04588866-660a-4898-bd4a-2dbddf36efd4';
      const period = '02f8772b-4afe-4446-9f52-4af1f96d11c6';
      const group = '9801a246-fb66-e21d-8524-03d5937b1caf';
      final transport = CurrentAttendanceTransport(AppConfig('Demo', base));
      Future<AuthController> login(String phone, String key) async {
        final auth = AuthController(transport, SecureRefreshStore(key));
        await auth.restore();
        if (auth.session == null) {
          final challenge = await auth.requestOtp(phone);
          await auth.verify(phone, challenge, '246810');
        }
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return auth;
      }

      final owner = await login(
        '01099900010',
        'v45.5278.os-media-entry.01099900010',
      );
      final admin = await login(
        '01099900011',
        'v45.5278.os-media-entry.01099900011',
      );
      final guardian = await login(
        '01200000007',
        'v45.5278.current-attendance.guardian7',
      );
      final foreign = await login(
        '01099900100',
        'v45.5278.guardian-workspace.01099900100',
      );
      expect(owner.session!.selected!.role, 'AcademyOwner');
      expect(admin.session!.selected!.role, 'AcademyAdmin');
      expect(guardian.session!.selected!.role, 'Guardian');
      final beforeMoney = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      expect(beforeMoney['totalCount'], 71);
      expect(beforeMoney['totalAmount'], 60285);
      expect(beforeMoney['totalRefunds'], 1);
      final beforePeriod = obj(
        await owner.request('GET', '/api/v1/subscriptions/periods/$period'),
      );
      expect(
        beforePeriod['remainingSessions'],
        7,
        reason: 'Do not consume again if this acceptance already saved; resume the stored record.',
      );
      final currentSessions = rows(
        await owner.request(
          'GET',
          '/api/v1/attendance/sessions?groupId=$group',
        ),
      );
      final matches = currentSessions
          .where(
            (s) => s['sessionDate'] == date && s['startTime'] == '12:00:00',
          )
          .toList();
      expect(matches.length, lessThanOrEqualTo(1));
      final session = matches.isEmpty
          ? textOf(
              obj(
                await owner.request(
                  'POST',
                  '/api/v1/attendance/sessions',
                  body: {
                    'trainingGroupId': group,
                    'sessionDate': date,
                    'startTime': '12:00:00',
                    'endTime': '13:00:00',
                    'notes': 'جلسة قبول حضور اصطناعية22أكتوبر — لا بيانات عميل',
                  },
                ),
              )['id'],
            )
          : textOf(matches.single['id']);
      debugPrint(
        'Synthetic current attendance session: $session; enrollment: $enrollment; period: $period',
      );
      final rosterPath = '/api/v1/attendance/sessions/$session/players';
      final beforeRoster = obj(await admin.request('GET', rosterPath));
      expect(beforeRoster['canManageAttendance'], true);
      final target = rows(beforeRoster['players'])
          .singleWhere((p) => p['sportEnrollmentId'] == enrollment);
      expect(target['playerId'], player);
      expect(target['attendanceStatus'], 'NotRecorded');
      expect(target['remainingSessions'], 7);
      expect(target['sessionConsumed'], false);
      await expectLater(
        foreign.request('GET', '/api/v1/guardian/children/$player/profile'),
        throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 404)),
      );
      final guardianProfile = obj(
        await guardian.request(
          'GET',
          '/api/v1/guardian/children/$player/profile',
        ),
      );
      expect(obj(guardianProfile['player'])['id'], player);
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
        await t.ensureVisible(f.first);
        await t.pumpAndSettle();
      }

      Future<void> capture(String name) async {
        await t.pumpAndSettle();
        await t.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 500)),
        );
        await binding.takeScreenshot(name);
      }

      Future<void> openApp(AuthController auth, String role) async {
        final appearance = Appearance();
        for (final theme in ['football', 'general']) {
          await appearance.completeRoleIntro(role, theme);
        }
        await t.pumpWidget(
          AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
        );
      }

      await openApp(admin, 'AcademyAdmin');
      await wait(find.byType(AdminHome));
      await t.tap(find.widgetWithText(NavigationDestination, 'الحضور'));
      await t.pump(const Duration(milliseconds: 400));
      await wait(find.byType(NativeSessions));
      final sessionTile = find.widgetWithText(
        ListTile,
        'مجموعة النجوم التجريبية',
      );
      await reach(sessionTile);
      expect(sessionTile, findsOneWidget);
      await t.tap(sessionTile);
      await t.pump(const Duration(milliseconds: 400));
      await wait(find.byType(CoachAttendance));
      expect(
        t.widget<CoachAttendance>(find.byType(CoachAttendance)).sessionId,
        session,
      );
      await wait(find.byType(TextField));
      await reach(find.byType(TextField));
      await t.enterText(find.byType(TextField), textOf(target['playerCode']));
      await t.testTextInput.receiveAction(TextInputAction.done);
      await t.pumpAndSettle();
      await reach(find.widgetWithText(FilterChip, 'حاضر'));
      await capture(
        'phase-approved-admin-current-player-attendance-review-w375',
      );
      await t.tap(find.widgetWithText(FilterChip, 'حاضر'));
      await t.pumpAndSettle();
      await t.tap(find.widgetWithText(FilledButton, 'حفظ الحضور'));
      await t.pump(const Duration(milliseconds: 500));
      t
          .state<ScrollableState>(find.byType(Scrollable).first)
          .position
          .jumpTo(0);
      await wait(find.text('تم حفظ الحضور ويمكن تصحيحه بأمان.'));
      expect(transport.writes, hasLength(1));
      expect(rows(transport.writes.single.body['items']), hasLength(1));
      final afterRoster = obj(await admin.request('GET', rosterPath));
      final saved = rows(afterRoster['players'])
          .singleWhere((p) => p['sportEnrollmentId'] == enrollment);
      expect(saved['attendanceStatus'], 'Present');
      expect(saved['remainingSessions'], 6);
      expect(saved['sessionConsumed'], true);
      expect(
        rows(afterRoster['players'])
            .where((p) => p['sportEnrollmentId'] != enrollment)
            .toList(),
        rows(beforeRoster['players'])
            .where((p) => p['sportEnrollmentId'] != enrollment)
            .toList(),
      );
      await admin.request(
        'PUT',
        rosterPath,
        body: transport.writes.single.body,
      );
      final replay = obj(await admin.request('GET', rosterPath));
      expect(replay['players'], afterRoster['players']);
      final afterPeriod = obj(
        await owner.request('GET', '/api/v1/subscriptions/periods/$period'),
      );
      expect(afterPeriod['remainingSessions'], 6);
      for (final key in [
        'startDate',
        'endDate',
        'priceSnapshot',
        'initialSessions',
        'collectionId',
        'adjustments',
        'planId',
      ]) {
        expect(afterPeriod[key], beforePeriod[key]);
      }
      await reach(find.text('لاعب خيالي 7'));
      await capture(
        'phase-approved-admin-current-player-attendance-saved-w375',
      );
      final family = obj(
        await guardian.request(
          'GET',
          '/api/v1/guardian/children/$player/profile',
        ),
      );
      expect(
        rows(family['attendance']).singleWhere(
          (a) =>
              a['sessionDate'] == date &&
              a['group'] == 'مجموعة النجوم التجريبية',
        )['status'],
        'Present',
      );
      final familyPeriod = obj(
        await guardian.request(
          'GET',
          '/api/v1/guardian/subscriptions/periods/$period/adjustments',
        ),
      );
      expect(familyPeriod['remainingSessions'], 6);
      await t.pumpWidget(const SizedBox.shrink());
      await openApp(guardian, 'Guardian');
      await wait(find.byType(GuardianHome));
      final card = find.byKey(const ValueKey('large-child-$player'));
      await reach(card);
      await t.tap(find.descendant(of: card, matching: find.text('عرض الملف')));
      await t.pump(const Duration(milliseconds: 400));
      await wait(find.byType(ChildProfile));
      await reach(find.text('الحضور'));
      await t.tap(find.text('الحضور'));
      await t.pump(const Duration(milliseconds: 400));
      await wait(find.byType(ProfileRecords));
      expect(
        t.widget<ProfileRecords>(find.byType(ProfileRecords)).playerId,
        player,
      );
      await wait(find.text('حاضر'));
      await capture(
        'phase-approved-guardian-current-player-attendance-saved-w375',
      );
      final afterMoney = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      for (final key in [
        'totalCount',
        'totalAmount',
        'totalRefunds',
        'netAmount',
      ]) {
        expect(afterMoney[key], beforeMoney[key]);
      }
      expect(transport.writes, hasLength(2));
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox.shrink());
      for (final auth in [owner, admin, guardian, foreign]) {
        auth.dispose();
      }
    },
  );
}
