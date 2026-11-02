import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/admin/native_player_placement.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/home_destinations.dart';
import 'package:academy_mobile/features/owner/refined_home_panels.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'support/placement_fixture.dart';

class CompensationAcceptanceTransport extends HttpApiTransport {
  CompensationAcceptanceTransport(super.config);
  final commands = <({String path, Json body, String? key, Json result})>[];
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    final value = await super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
    if (method == 'POST' &&
        (path.endsWith('/compensation') ||
            path.endsWith('/booking') ||
            path.endsWith('/cancel'))) {
      commands.add((
        path: path,
        body: obj(body),
        key: idempotencyKey,
        result: obj(value),
      ));
    }
    return value;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'owner grants compensation for existing absence, books and releases the real future occurrence natively without money or attendance fabrication',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      const enrollment = '676f7a72-b5bc-3aff-7cee-2b48d877d80f';
      const player = '526d6af0-a898-0ab4-2b5f-80aa4494436f';
      const original = '8354c7dc-3a13-0d71-db92-c18a0ded4a7f';

      const root = '/api/v1/people/enrollments/$enrollment/compensations';
      final transport = CompensationAcceptanceTransport(
        AppConfig('Demo', base),
      );
      final auth = AuthController(
        transport,
        SecureRefreshStore('v45.5278.os-media-entry.01099900010'),
      );
      await auth.restore();
      if (auth.session == null) {
        final c = await auth.requestOtp('01099900010');
        await auth.verify('01099900010', c, '246810');
      }
      expect(auth.session!.selected!.role, 'AcademyOwner');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final fixture = await preparePlacementFixture(auth);
      await reviewFixturePlayerLevel(
        auth,
        enrollment,
        textOf(fixture['levelId']),
      );
      final target = textOf(fixture['targetSessionId']);
      debugPrint('Synthetic placement fixture: $fixture');
      final initial = rows(await auth.request('GET', root));
      expect(
        initial.where((g) => g['sourceSessionId'] == original),
        isEmpty,
        reason: 'Do not grant the saved source again; resume its exact IDs.',
      );
      final beforeMoney = obj(
        await auth.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      final beforeAttendance = await auth.request(
        'GET',
        '/api/v1/attendance/sessions/$original/players',
      );
      expect(obj(beforeAttendance)['players'].toString(), contains(enrollment));
      final beforePlayer = obj(
        await auth.request('GET', '/api/v1/people/players/$player'),
      );
      final originalEnrollment = rows(beforePlayer['enrollments'])
          .singleWhere((e) => e['id'] == enrollment);
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

      Future<void> choose(String label, String value) async {
        await tap(find.widgetWithText(DropdownButtonFormField<String>, label));
        await t.pumpAndSettle();
        await t.tap(find.text(value).last);
        await t.pumpAndSettle();
      }

      Future<void> capture(String name) async {
        await t.pumpAndSettle();
        await t.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 500)),
        );
        await binding.takeScreenshot(name);
      }

      Future<void> confirmPlacement() async {
        await tap(find.text('معاينة الأثر والتعارضات'));
        await wait(find.text('راجعت الأثر والحصص وأؤكد اعتماد الإجراء.'));
        await tap(find.text('راجعت الأثر والحصص وأؤكد اعتماد الإجراء.'));
        await tap(find.text('تأكيد الإجراء'));
        await wait(find.text('حُفظ الإجراء وأُعيدت قراءة سجله الفعلي.'));
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
      await tap(find.widgetWithText(TextButton, 'المزيد'));
      await tap(find.text('التعويض والحصص البديلة'));
      await wait(find.byType(NativePlayerCompensations));
      await tap(find.text('اعتماد تعويض'));
      await wait(find.byType(NativePlayerPlacement));
      await choose('الحصة الأصلية', 'ناشئين 2016 — مجموعة أ · 2026-10-14');
      await tap(find.widgetWithText(TextFormField, 'نهاية صلاحية التعويض'));
      await wait(find.byType(DatePickerDialog));
      await t.tap(find.text('29').last);
      await t.tap(
        find.text(
          MaterialLocalizations.of(t.element(find.byType(DatePickerDialog)))
              .okButtonLabel,
        ),
      );
      await t.pumpAndSettle();
      await confirmPlacement();
      await capture('phase-approved-compensation-granted-w375');
      final grantCommand = transport.commands.singleWhere(
        (c) => c.path.endsWith('/compensation'),
      );
      final grantId = grantCommand.result['id'];
      debugPrint('Synthetic native compensation grant: $grantId / $original');
      var grants = rows(await auth.request('GET', root));
      var grant = grants.singleWhere((g) => g['id'] == grantId);
      expect(grant['sourceSessionId'], original);
      expect(grant['units'], 1);
      expect(grant['cause'], 'ApprovedAbsence');
      Navigator.of(t.element(find.byType(NativePlayerPlacement))).pop();
      await t.pumpAndSettle();
      await wait(find.text('حجز حصة بديلة'));
      await tap(find.text('حجز حصة بديلة'));
      await wait(find.byType(NativePlayerPlacement));
      await choose(
        'الحصة البديلة',
        'مجموعة قبول نقل وتعويض اصطناعية2016 · 2026-10-29 · 18:00:00 — 19:00:00',
      );
      await confirmPlacement();
      await capture('phase-approved-compensation-booked-w375');
      final bookingCommand = transport.commands.singleWhere(
        (c) => c.path.endsWith('/booking'),
      );
      final bookingId = bookingCommand.result['id'];
      debugPrint('Synthetic native compensation booking: $bookingId / $target');
      final replay = obj(
        await auth.request(
          'POST',
          bookingCommand.path,
          body: bookingCommand.body,
          idempotencyKey: bookingCommand.key,
        ),
      );
      expect(replay['id'], bookingId);
      grants = rows(await auth.request('GET', root));
      grant = grants.singleWhere((g) => g['id'] == grantId);
      expect(grant['allocated'], 1);
      expect(grant['consumed'], 0);
      expect(rows(grant['bookings']), hasLength(1));
      expect(rows(grant['bookings']).single['trainingSessionId'], target);
      Navigator.of(t.element(find.byType(NativePlayerPlacement))).pop();
      await t.pumpAndSettle();
      await wait(find.text('تحرير حجز التعويض'));
      await tap(find.text('تحرير حجز التعويض'));
      await wait(find.byType(AlertDialog));
      await t.tap(find.text('تأكيد تحرير الحجز'));
      await t.pump(const Duration(milliseconds: 400));
      await wait(find.text('حُرر الحجز وأُعيدت قراءة السجل.'));
      await reach(find.text('حجز محرر'));
      await capture('phase-approved-compensation-released-w375');
      grant = rows(await auth.request('GET', root))
          .singleWhere((g) => g['id'] == grantId);
      expect(grant['allocated'], 0);
      expect(grant['consumed'], 0);
      expect(rows(grant['bookings']).single['state'], 'Cancelled');
      expect(
        obj(
          await auth.request(
            'GET',
            '/api/v1/attendance/sessions/$original/players',
          ),
        )['players'],
        obj(beforeAttendance)['players'],
      );
      final afterPlayer = obj(
        await auth.request('GET', '/api/v1/people/players/$player'),
      );
      final afterEnrollment = rows(afterPlayer['enrollments'])
          .singleWhere((e) => e['id'] == enrollment);
      for (final k in [
        'subscription',
        'subscriptions',
        'periods',
        'trainingGroupId',
      ]) {
        expect(afterEnrollment[k], originalEnrollment[k]);
      }
      final money = obj(
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
        expect(money[k], beforeMoney[k]);
      }
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox.shrink());
      auth.dispose();
    },
  );
}
