import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/admin/native_structure_operations.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/home_destinations.dart';
import 'package:academy_mobile/features/owner/refined_home_panels.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'support/placement_fixture.dart';

class PlacementCancelTransport extends HttpApiTransport {
  PlacementCancelTransport(super.config);
  ({String path, Json body, String key, Json result})? placement, cancellation;
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
        (path.endsWith('/placement') || path.endsWith('/cancel'))) {
      final command = (
        path: path,
        body: obj(body),
        key: idempotencyKey!,
        result: obj(value),
      );
      if (path.endsWith('/placement')) {
        placement = command;
      } else {
        cancellation = command;
      }
    }
    return value;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'native owner places one unassigned second sport then cancels its approved waiver without changing original child enrollment or money',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      final transport = PlacementCancelTransport(AppConfig('Demo', base));
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
      const player = '14204d10-9fcd-42d2-8452-72a94178976b';
      final beforePlayer = obj(
        await auth.request('GET', '/api/v1/people/players/$player'),
      );
      final originals = rows(beforePlayer['enrollments']);
      final beforeMoney = obj(
        await auth.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      final fixture = await preparePlacementFixture(auth);
      final groupId = textOf(fixture['id']);
      final group = obj(
        obj(
          await auth.request('GET', '/api/v1/manage/groups/$groupId/details'),
        )['group'],
      );
      final preexisting = originals
          .where(
            (e) =>
                e['branchId'] == group['branchId'] &&
                e['sportId'] == group['sportId'],
          )
          .firstOrNull;
      if (preexisting != null) {
        expect(
          preexisting['trainingGroupId'],
          null,
          reason: 'The acceptance placement already exists. Resume its stored revision; do not place or approve another period.',
        );
      }
      final unassigned =
          preexisting ??
          obj(
            await auth.request(
              'POST',
              '/api/v1/people/players/$player/unassigned-enrollments',
              body: {
                'branchId': group['branchId'],
                'sportId': group['sportId'],
                'registrationFormat': 'Group',
              },
            ),
          );
      final enrollment = textOf(unassigned['id']);
      await reviewFixturePlayerLevel(
        auth,
        enrollment,
        textOf(fixture['levelId']),
      );
      var current = obj(
        await auth.request('GET', '/api/v1/people/players/$player'),
      );
      var e = rows(current['enrollments'])
          .singleWhere((e) => e['id'] == enrollment);
      String periodId;
      if (e['subscription'] is Map) {
        periodId = textOf(obj(e['subscription'])['id']);
      } else {
        final options = obj(
          await auth.request(
            'GET',
            '/api/v1/finance/free-training/$enrollment/options',
          ),
        );
        final plan = rows(
          options['plans'],
        ).singleWhere((p) => p['id'] == '3b2705c6-1f5e-b1c0-8196-4b742a17c876');
        final body = {
          'sportEnrollmentId': enrollment,
          'subscriptionPlanId': plan['id'],
          'reason': 'استحقاق قبول تسكين اصطناعي لرياضة ثانية — بلا تحصيل',
        };
        final quote = obj(
          await auth.request(
            'POST',
            '/api/v1/finance/free-training/quote',
            body: body,
          ),
        );
        final approved = obj(
          await auth.request(
            'POST',
            '/api/v1/finance/free-training/approve',
            body: {...body, 'quoteHash': quote['quoteHash']},
            idempotencyKey: 'v45-native-placement-player-seven',
          ),
        );
        periodId = textOf(approved['periodId']);
        debugPrint(
          'Synthetic placement waiver entitlement: $approved / enrollment $enrollment',
        );
      }
      final beforePeriod = obj(
        await auth.request('GET', '/api/v1/subscriptions/periods/$periodId'),
      );
      expect(beforePeriod['origin'], 'ApprovedWaiver');
      expect(beforePeriod['priceSnapshot'], 0);
      expect(beforePeriod['receiptId'], null);
      expect(beforePeriod['status'], 'Active');
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

      final appearance = Appearance();
      for (final theme in ['football', 'general']) {
        await appearance.completeRoleIntro('AcademyOwner', theme);
      }
      await t.pumpWidget(
        AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
      );
      await wait(find.byType(NavigationBar));
      await t.tap(
        find.descendant(
          of: find.byType(NavigationBar),
          matching: find.text('العمليات'),
        ),
      );
      await wait(find.text('اشتراك جديد'));
      await tap(find.text('تسكين لاعب'));
      await wait(find.byType(NativeGroupPicker));
      await tap(find.text(textOf(fixture['arabicName'])));
      await wait(find.byType(NativeGroupPlacement));
      final search = find.widgetWithText(TextFormField, 'اسم اللاعب أو الكود');
      await reach(search);
      await t.enterText(search, textOf(beforePlayer['playerCode']));
      await tap(find.text('بحث'));
      await wait(find.textContaining(textOf(beforePlayer['playerCode'])));
      await tap(
        find.widgetWithText(
          CheckboxListTile,
          '${beforePlayer['arabicName']} · ${beforePlayer['playerCode']}',
        ),
      );
      await tap(find.text('معاينة التسكين'));
      await reach(find.text('تأكيد التسكين'));
      await capture('phase-approved-native-player-placement-reviewed-w375');
      await t.tap(find.text('تأكيد التسكين'));
      await t.pump(const Duration(milliseconds: 400));
      await wait(find.text('تم تسكين 1 لاعب.'));
      await capture('phase-approved-native-player-placement-saved-w375');
      final placed = transport.placement!;
      debugPrint(
        'Synthetic native placement: ${placed.result} / enrollment $enrollment / group $groupId',
      );
      final record = obj(
        await auth.request(
          'GET',
          '/api/v1/manage/groups/$groupId/placement-revisions/${placed.result['id']}',
        ),
      );
      expect(record['assignedEnrollmentIds'], [enrollment]);
      expect(record['placed'], 1);
      final replay = obj(
        await auth.request(
          'POST',
          placed.path,
          body: placed.body,
          idempotencyKey: placed.key,
        ),
      );
      expect(replay['id'], placed.result['id']);
      current = obj(
        await auth.request('GET', '/api/v1/people/players/$player'),
      );
      e = rows(current['enrollments'])
          .singleWhere((e) => e['id'] == enrollment);
      expect(e['trainingGroupId'], groupId);
      Navigator.of(t.element(find.byType(NativeGroupPlacement))).pop();
      await t.pumpAndSettle();
      Navigator.of(t.element(find.byType(NativeGroupPicker))).pop();
      await t.pumpAndSettle();
      await t.tap(
        find.descendant(
          of: find.byType(NavigationBar),
          matching: find.text('الرئيسية'),
        ),
      );
      await wait(find.text('ملخص الأكاديمية'));
      await reach(find.byType(HomeSubscriptions));
      await tap(find.textContaining(RegExp(r'^\d+ إجمالي$')));
      await wait(find.byType(HomeSubscriptionPlayers));
      final playerTile = find.byWidgetPredicate(
        (w) =>
            w is ListTile &&
            w.title is UiText &&
            (w.title as UiText).data == beforePlayer['arabicName'] &&
            w.subtitle is UiText &&
            (w.subtitle as UiText).data.contains('فرع مدينتي'),
      );
      await tap(playerTile);
      await wait(find.byType(NativePlayerWorkspace));
      await wait(find.text('الرياضة والتسجيل'));
      await choose(
        'الرياضة والتسجيل',
        '${e['sportName']} · ${e['branchName']}',
      );
      await tap(find.widgetWithText(TextButton, 'الاشتراكات والماليات'));
      await tap(find.text('تفاصيل الاشتراك وسجل التعديلات'));
      await wait(find.byType(NativeSubscriptionPeriod));
      expect(
        t
            .widget<NativeSubscriptionPeriod>(
              find.byType(NativeSubscriptionPeriod),
            )
            .id,
        periodId,
      );
      await choose('الإجراء', 'إلغاء الاشتراك');
      final reason = find.widgetWithText(TextFormField, 'سبب التعديل');
      await reach(reason);
      await t.enterText(
        reason,
        'إلغاء اشتراك قبول تسكين اصطناعي — بلا استرداد',
      );
      await tap(find.text('راجعت أثر التعديل على الاشتراك وأؤكد تنفيذه.'));
      await reach(find.text('تأكيد تعديل الاشتراك'));
      await capture('phase-approved-native-waiver-cancel-reviewed-w375');
      await t.tap(find.text('تأكيد تعديل الاشتراك'));
      await t.pump(const Duration(milliseconds: 400));
      for (var i = 0; i < 240 && transport.cancellation == null; i++) {
        await t.pump(const Duration(milliseconds: 150));
      }
      expect(transport.cancellation, isNotNull);
      await t.pumpAndSettle();
      await reach(find.text('حُفظ التعديل وأُعيدت قراءة سجل الاشتراك.'));
      await capture('phase-approved-native-waiver-cancel-saved-w375');
      final cancelled = transport.cancellation!;
      debugPrint(
        'Synthetic native waiver cancellation: ${cancelled.result} / period $periodId',
      );
      final period = obj(
        await auth.request('GET', '/api/v1/subscriptions/periods/$periodId'),
      );
      expect(period['status'], 'Cancelled');
      expect(period['origin'], 'ApprovedWaiver');
      expect(period['priceSnapshot'], 0);
      expect(period['receiptId'], null);
      expect(period['startDate'], beforePeriod['startDate']);
      expect(period['endDate'], beforePeriod['endDate']);
      expect(period['initialSessions'], beforePeriod['initialSessions']);
      expect(period['remainingSessions'], beforePeriod['remainingSessions']);
      expect(obj(period['waiver'])['id'], obj(beforePeriod['waiver'])['id']);
      final cancellationReplay = obj(
        await auth.request(
          'POST',
          cancelled.path,
          body: cancelled.body,
          idempotencyKey: cancelled.key,
        ),
      );
      expect(
        cancellationReplay['adjustmentId'],
        cancelled.result['adjustmentId'],
      );
      expect(
        rows(
          obj(
            await auth.request(
              'GET',
              '/api/v1/subscriptions/periods/$periodId',
            ),
          )['adjustments'],
        ),
        hasLength(1),
      );
      final afterPlayer = obj(
        await auth.request('GET', '/api/v1/people/players/$player'),
      );
      for (final old in originals.where((old) => old['id'] != enrollment)) {
        expect(
          rows(afterPlayer['enrollments'])
              .singleWhere((e) => e['id'] == old['id']),
          old,
        );
      }
      final afterMoney = obj(
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
        expect(afterMoney[k], beforeMoney[k]);
      }
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox.shrink());
      auth.dispose();
    },
  );
}
