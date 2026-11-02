import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:academy_mobile/features/shared/vip_rewards.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

const acceptanceMessage = 'مكافأة قبول آلي اصطناعية محفوظة — VIP أكتوبر ٢٠٢٦';
const guardianId = String.fromEnvironment('VIP_GUARDIAN_ID');
const previewGuardianId = String.fromEnvironment('VIP_PREVIEW_GUARDIAN_ID');
const familyPhone = String.fromEnvironment('VIP_GUARDIAN_PHONE');

// This is a bounded real HTTP acceptance, not a fake source. A resumed run
// finds the same saved message before allowing issuance; no financial writes.
class VipAcceptanceTransport extends HttpApiTransport {
  VipAcceptanceTransport(super.config);
  int issues = 0, previews = 0;
  bool mayIssue = false;
  String? previewGuardian;
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    if (method != 'GET' && !path.startsWith('/api/v1/mobile/auth/')) {
      final b = obj(body);
      if (method != 'POST' ||
          ![
            '/api/v1/manage/vip-rewards/preview',
            '/api/v1/manage/vip-rewards/',
          ].contains(path) ||
          (path.endsWith('/preview') ? previewGuardian == null : !mayIssue) ||
          b['message'] != acceptanceMessage ||
          num.tryParse('${b['percentage']}') != 12 ||
          (b['guardianIds'] as List).single !=
              (path.endsWith('/preview') ? previewGuardian : guardianId)) {
        throw StateError(
          'Only the single bounded synthetic VIP issue is allowed.',
        );
      }
      if (path.endsWith('/preview')) {
        previews++;
      } else {
        if (issues != 0 || previews == 0) {
          throw StateError('No duplicate issuance.');
        }
        issues++;
      }
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

class VipAcceptanceAppearance extends Appearance {
  @override
  Future<bool> hasRoleIntro(String role, String sportTheme) async => true;
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'one saved VIP issuance, exact notice route and family privacy at both widths',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect(guardianId, isNotEmpty);
      expect(familyPhone, isNotEmpty);
      expect(familyPhone.endsWith('1099900100'), false);
      const width = int.fromEnvironment('VIP_ACCEPTANCE_WIDTH');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, width);
      t.platformDispatcher.accessibilityFeaturesTestValue =
          const FakeAccessibilityFeatures(disableAnimations: true);
      addTearDown(t.platformDispatcher.clearAccessibilityFeaturesTestValue);
      final transport = VipAcceptanceTransport(AppConfig('Demo', base));
      final appearance = VipAcceptanceAppearance();
      final controllers = <AuthController>[];
      Future<AuthController> login(
        String role,
        String phone, {
        String? key,
      }) async {
        final a = AuthController(
          transport,
          SecureRefreshStore(key ?? 'v73.5278.approved-intro.$role'),
        );
        controllers.add(a);
        await a.restore();
        if (a.session == null) {
          final c = await a.requestOtp(phone);
          await a.verify(phone, c, '246810');
        }
        expect(a.session!.selected!.role, role);
        expect(
          a.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return a;
      }

      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> reach(Finder f) async {
        FocusManager.instance.primaryFocus?.unfocus();
        await t.pumpAndSettle();
        if (f.evaluate().isEmpty) {
          final scroll = find.byType(Scrollable).first;
          t.state<ScrollableState>(scroll).position.jumpTo(0);
          await t.pumpAndSettle();
          await t.scrollUntilVisible(
            f,
            160,
            scrollable: scroll,
            maxScrolls: 100,
          );
        }
        await t.ensureVisible(f.first);
        await t.pumpAndSettle();
      }

      Future<void> capture(String name) async {
        expect(t.takeException(), isNull);
        await binding.takeScreenshot('phase-approved-vip-$width-$name');
      }

      Future<void> app(AuthController a, bool dark) async {
        appearance.dark = dark;
        await t.pumpWidget(const SizedBox.shrink());
        await t.pumpAndSettle();
        await t.pumpWidget(
          AcademyApp(auth: a, appearance: appearance, environment: 'Demo'),
        );
        await wait(find.byType(NativeRoleTabs));
      }

      Future<void> open(Widget page) async {
        openPage(t.element(find.byType(NativeRoleTabs)), page);
        await t.pump();
      }

      Future<List<Json>> saved(AuthController owner) async {
        final all = <Json>[];
        var page = 1;
        while (true) {
          final d = obj(
            await owner.request('GET', '/api/v1/manage/vip-rewards?page=$page'),
          );
          all.addAll(rows(d['items']));
          if (all.length >= (d['count'] as num)) break;
          page++;
          expect(page, lessThan(10));
        }
        return all.where((r) => r['message'] == acceptanceMessage).toList();
      }

      try {
        final owner = await login('AcademyOwner', '01099900010');
        final before = obj(
          await owner.request(
            'GET',
            '/api/v1/reports/financial?allDates=true&pageSize=1',
          ),
        );
        var records = await saved(owner);
        expect(records.length, lessThanOrEqualTo(1));
        if (records.isEmpty || width == 375) {
          final issueNow = records.isEmpty;
          final selectedGuardian = issueNow ? guardianId : previewGuardianId;
          expect(
            width,
            375,
            reason: 'Only the first bounded375 run may issue.',
          );
          final options = obj(
            await owner.request('GET', '/api/v1/manage/vip-rewards/options'),
          );
          final recipient = rows(options['items'])
              .singleWhere((r) => r['id'] == selectedGuardian);
          transport.mayIssue = issueNow;
          transport.previewGuardian = selectedGuardian;
          await app(owner, false);
          await open(NativeVipRewards(auth: owner, owner: true));
          await wait(find.text('إصدار مكافأة'));
          await t.tap(find.text('إصدار مكافأة'));
          await wait(find.byType(CheckboxListTile));
          final choice = find.widgetWithText(
            CheckboxListTile,
            textOf(recipient['name']),
          );
          await reach(choice);
          await t.tap(choice);
          final pct = find.byWidgetPredicate(
            (w) => w is TextField && w.decoration?.labelText == 'نسبة الخصم %',
          );
          final message = find.byWidgetPredicate(
            (w) => w is TextField && w.decoration?.labelText == 'رسالة الأسرة',
          );
          await reach(pct);
          await t.tap(pct);
          await t.enterText(pct, '12');
          expect(t.widget<TextField>(pct).controller!.text, '12');
          await reach(message);
          await t.tap(message);
          await t.enterText(message, acceptanceMessage);
          expect(
            t.widget<TextField>(message).controller!.text,
            acceptanceMessage,
          );
          await reach(find.text('صالحة حتى'));
          await t.tap(find.text('صالحة حتى'));
          await t.pumpAndSettle();
          await t.tap(
            find.text(
              MaterialLocalizations.of(t.element(find.byType(DatePickerDialog)))
                  .okButtonLabel,
            ),
          );
          await t.pumpAndSettle();
          await reach(find.text('معاينة المكافأة'));
          await t.tap(find.text('معاينة المكافأة'));
          await t.pumpAndSettle();
          await reach(find.text('تأكيد الإصدار وحفظ الإشعار الداخلي'));
          expect((await saved(owner)).length, records.length);
          await capture('preview-light');
          if (issueNow) {
            await t.tap(find.text('تأكيد الإصدار وحفظ الإشعار الداخلي'));
            for (
              var i = 0;
              i < 240 && find.byType(NativeVipIssue).evaluate().isNotEmpty;
              i++
            ) {
              await t.pump(const Duration(milliseconds: 150));
            }
            expect(find.byType(NativeVipIssue), findsNothing);
            records = await saved(owner);
            expect(records, hasLength(1));
            expect(transport.issues, 1);
          }
          transport.mayIssue = false;
        }
        final reward = records.single, id = textOf(reward['id']);
        expect(reward['guardianId'], guardianId);
        expect(reward['percentage'], 12);
        expect(reward['state'], 'Available');
        expect(reward['history'], isEmpty);
        final recovered = obj(
          await owner.request(
            'GET',
            '/api/v1/manage/vip-rewards/requests/${reward['requestId']}',
          ),
        );
        expect(recovered['ids'], [id]);
        for (final dark in [false, true]) {
          await app(owner, dark);
          await open(NativeVipRewards(auth: owner, owner: true, rewardId: id));
          await wait(find.text(acceptanceMessage));
          await reach(find.text(acceptanceMessage));
          expect(find.text('التالي'), findsNothing);
          await capture('owner-${dark ? 'dark' : 'light'}-saved');
        }
        final family = await login(
          'Guardian',
          familyPhone,
          key: 'vip.saved.acceptance.family.$guardianId',
        );
        final detail = obj(
          await family.request('GET', '/api/v1/guardian/vip-rewards/$id'),
        );
        final own = rows(detail['items']).single;
        expect(own['id'], id);
        expect(own['requestId'], isNull);
        expect(own['history'], isEmpty);
        final notices = obj(
          await family.request('GET', '/api/v1/notifications'),
        );
        final notice = rows(notices['items'])
            .singleWhere((n) => n['targetId'] == id);
        expect(notice['reward'], true);
        expect(notice['targetKind'], 'Rewards');
        for (final dark in [false, true]) {
          await app(family, dark);
          await open(NotificationInbox(auth: family));
          await wait(find.text('عرض المكافأة'));
          await reach(find.text('عرض المكافأة'));
          await t.tap(find.text('عرض المكافأة'));
          await wait(find.text(acceptanceMessage));
          expect(
            t.widget<NativeVipRewards>(find.byType(NativeVipRewards)).rewardId,
            id,
          );
          expect(find.text('التالي'), findsNothing);
          expect(find.text('إلغاء المكافأة'), findsNothing);
          await reach(find.text(acceptanceMessage));
          await capture('family-${dark ? 'dark' : 'light'}-notice');
        }
        final unrelated = await login(
          'Guardian',
          '01099900100',
          key: 'v73.5278.approved-intro.Guardian',
        );
        try {
          await unrelated.request('GET', '/api/v1/guardian/vip-rewards/$id');
          fail('Another family must not read the entitlement.');
        } on ApiFailure catch (e) {
          expect(e.status, 404);
        }
        final after = obj(
          await owner.request(
            'GET',
            '/api/v1/reports/financial?allDates=true&pageSize=1',
          ),
        );
        expect(after, before);
        expect(await saved(owner), hasLength(1));
        final finalNotices = obj(
          await family.request('GET', '/api/v1/notifications'),
        );
        expect(
          rows(finalNotices['items'])
              .singleWhere((n) => n['id'] == notice['id'])['isRead'],
          notice['isRead'],
        );
        binding.reportData = {
          ...?binding.reportData,
          'rewardId': id,
          'requestId': reward['requestId'],
          'issuePosts': transport.issues,
          'width': width,
          'financialReportUnchanged': true,
          'unrelatedFamilyStatus': 404,
        };
      } finally {
        await t.pumpWidget(const SizedBox.shrink());
        await t.pumpAndSettle();
        for (final a in controllers) {
          a.dispose();
        }
      }
    },
  );
}
