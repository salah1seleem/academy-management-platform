import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/admin/native_player_placement.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/role_agenda.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'support/placement_fixture.dart';

class TransferAcceptanceTransport extends HttpApiTransport {
  TransferAcceptanceTransport(super.config);
  Json? preview;
  final commands = <({String path, Json body, String key, Json result})>[];
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
        path.endsWith('/preview') &&
        (path.contains('/session-transfer') ||
            path.contains('/group-transfer'))) {
      preview = obj(value);
    }
    if (method == 'POST' &&
        (path.endsWith('/session-transfer') ||
            path.endsWith('/group-transfer'))) {
      commands.add((
        path: path,
        body: obj(body),
        key: idempotencyKey!,
        result: obj(value),
      ));
    }
    return value;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'explicit branch scoped admin moves one occurrence and a future group natively; parent reads exact schedule; money stays unchanged',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      Future<AuthController> login(
        String phone,
        String store,
        HttpApiTransport transport,
      ) async {
        final auth = AuthController(transport, SecureRefreshStore(store));
        await auth.restore();
        if (auth.session == null) {
          final c = await auth.requestOtp(phone);
          await auth.verify(phone, c, '246810');
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
        HttpApiTransport(AppConfig('Demo', base)),
      );
      final transport = TransferAcceptanceTransport(AppConfig('Demo', base));
      final admin = await login(
        '01099900993',
        'v45.5278.enrollment-no-discount.01099900993',
        transport,
      );
      expect(owner.session!.selected!.role, 'AcademyOwner');
      expect(admin.session!.selected!.role, 'AcademyAdmin');
      const player = '46c43bbe-b1b4-43bd-a10f-ce6fa2e34efd';
      const enrollment = 'b80a6306-394c-4f3d-bbec-4ed63c01b2fa';
      const period = '26beb9c0-eaee-4fe8-8739-aa303cd4b766';
      const receipt = '204edd10-3892-4d67-9288-02b2cf04e572';
      const source = '15d0d3e4-a646-e395-c204-300b256339d5';
      final beforePlayer = obj(
        await owner.request('GET', '/api/v1/people/players/$player'),
      );
      final beforeEnrollment = rows(beforePlayer['enrollments'])
          .singleWhere((e) => e['id'] == enrollment);
      expect(
        rows(beforeEnrollment['sessionMoves']),
        isEmpty,
        reason: 'Do not move the saved occurrence again. Resume its batch ID.',
      );
      expect(
        rows(beforeEnrollment['transfers']),
        isEmpty,
        reason: 'Do not create a second dated transfer.',
      );
      final fixture = await preparePlacementFixture(owner);
      await reviewFixturePlayerLevel(
        owner,
        enrollment,
        textOf(fixture['levelId']),
      );
      final target = textOf(fixture['targetSessionId']);
      final beforePeriod = await owner.request(
        'GET',
        '/api/v1/subscriptions/periods/$period',
      );
      final beforeReceipt = await owner.request(
        'GET',
        '/api/v1/subscriptions/receipts/$receipt',
      );
      final beforeMoney = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      final accessPath =
          '/api/v1/manage/staff/${admin.session!.selected!.id}/access';
      Json accessBody(Json v) => {
        'allBranches': v['allBranches'],
        'branches': v['selectedBranches'],
        'allSports': v['allSports'],
        'sports': v['selectedSports'],
        'actions': rows(v['actions'])
          ..sort((a, b) => textOf(a['module']).compareTo(textOf(b['module']))),
        'sensitivePermissions': v['selectedSensitivePermissions'],
        'workflowActions':
            rows(v['workflowActions'])
                .where((a) => a['allowed'] == true)
                .map((a) => a['key'])
                .toList()
              ..sort((a, b) => '$a'.compareTo('$b')),
      };
      final originalAccess = accessBody(
        obj(await owner.request('GET', accessPath)),
      );
      const leaves = [
        'workflow.players.place',
        'workflow.players.session-transfer',
      ];
      expect(
        (originalAccess['workflowActions'] as List).where(leaves.contains),
        isEmpty,
      );
      expect(originalAccess['allBranches'], false);
      expect(originalAccess['branches'], hasLength(1));
      final granted = {
        ...originalAccess,
        'workflowActions': [
          ...originalAccess['workflowActions'] as List,
          ...leaves,
        ],
        'actions': rows(originalAccess['actions'])
            .map(
              (a) => {
                ...a,
                if (['players', 'academy'].contains(a['module'])) 'edit': true,
              },
            )
            .toList(),
      };
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

      Future<void> confirm(String image, {bool group = false}) async {
        transport.preview = null;
        await tap(
          find.text(group ? 'معاينة نقل المجموعة' : 'معاينة نقل الحصص'),
        );
        for (var i = 0; i < 240 && transport.preview == null; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(transport.preview, isNotNull);
        expect(transport.preview!['conflicts'], isEmpty);
        final confirm = find.text(
          group ? 'تأكيد نقل المجموعة' : 'تأكيد نقل الحصص',
        );
        await reach(confirm);
        await capture(image);
        await t.tap(confirm);
        await t.pump(const Duration(milliseconds: 400));
        await wait(
          find.textContaining(
            group ? 'تم تسجيل نقل المجموعة' : 'تم تسجيل نقل الحصص المختارة.',
          ),
        );
      }

      AuthController? parent;
      try {
        await owner.request('PUT', accessPath, body: granted);
        final access = obj(await admin.request('GET', '/api/v1/my-access'));
        for (final leaf in leaves) {
          expect(
            rows(access['workflowActions'])
                .singleWhere((a) => a['key'] == leaf)['allowed'],
            true,
          );
        }
        final appearance = Appearance();
        for (final theme in ['football', 'general']) {
          await appearance.completeRoleIntro('AcademyAdmin', theme);
        }
        await t.pumpWidget(
          AcademyApp(auth: admin, environment: 'Demo', appearance: appearance),
        );
        await wait(find.text('اللاعبون'));
        await tap(find.text('اللاعبون'));
        await wait(find.byType(NativePlayerDirectory));
        final search = find.widgetWithText(
          TextFormField,
          'بحث بالاسم أو الكود أو هاتف ولي الأمر',
        );
        await reach(search);
        await t.enterText(search, '01099900995');
        await tap(find.text('بحث اللاعبين'));
        await wait(find.text('لاعب خصم إداري اصطناعي995'));
        await tap(find.text('لاعب خصم إداري اصطناعي995'));
        await wait(find.byType(NativePlayerWorkspace));
        await wait(find.text('نظرة عامة'));
        await tap(find.widgetWithText(TextButton, 'المزيد'));
        await tap(find.text('نقل حصص مختارة'));
        await wait(find.byType(NativePlayerPlacement));
        await choose('المجموعة البديلة', textOf(fixture['arabicName']));
        await choose(
          'الحصة الأصلية',
          'ناشئين 2016 — مجموعة أ · 2026-10-28 · 16:00:00 — 17:00:00',
        );
        await choose(
          'الحصة البديلة',
          'مجموعة قبول نقل وتعويض اصطناعية2016 · 2026-10-29 · 18:00:00 — 19:00:00',
        );
        await tap(find.text('إضافة إلى الحصص المختارة'));
        await confirm('phase-approved-admin-selected-transfer-review-w375');
        final occurrence = transport.commands.singleWhere(
          (c) => c.path.endsWith('/session-transfer'),
        );
        debugPrint(
          'Synthetic admin occurrence batch: ${occurrence.result} / $source / $target',
        );
        final replay = obj(
          await admin.request(
            'POST',
            occurrence.path,
            body: occurrence.body,
            idempotencyKey: occurrence.key,
          ),
        );
        expect(replay['id'], occurrence.result['id']);
        var profile = obj(
          await owner.request('GET', '/api/v1/people/players/$player'),
        );
        var e = rows(profile['enrollments'])
            .singleWhere((e) => e['id'] == enrollment);
        expect(rows(e['sessionMoves']), hasLength(1));
        expect(
          rows(e['sessionMoves']).single['batchId'],
          occurrence.result['id'],
        );
        expect(rows(e['sessionMoves']).single['sourceSessionId'], source);
        expect(rows(e['sessionMoves']).single['targetSessionId'], target);
        Navigator.of(t.element(find.byType(NativePlayerPlacement))).pop();
        await t.pumpAndSettle();
        await wait(find.text('نقل مجموعة اللاعب'));
        await tap(find.text('نقل مجموعة اللاعب'));
        await wait(find.byType(NativePlayerPlacement));
        await choose('المجموعة البديلة', textOf(fixture['arabicName']));
        await tap(find.widgetWithText(TextFormField, 'تاريخ بدء النقل'));
        await wait(find.byType(DatePickerDialog));
        final localizations = MaterialLocalizations.of(
          t.element(find.byType(DatePickerDialog)),
        );
        await t.tap(find.byTooltip(localizations.nextMonthTooltip));
        await t.pumpAndSettle();
        await t.tap(find.text('1').last);
        await t.tap(find.text(localizations.okButtonLabel));
        await t.pumpAndSettle();
        await confirm(
          'phase-approved-admin-dated-transfer-review-w375',
          group: true,
        );
        final dated = transport.commands.singleWhere(
          (c) => c.path.endsWith('/group-transfer'),
        );
        debugPrint(
          'Synthetic admin dated transfer: ${dated.result} / ${fixture['id']} / 2026-11-01',
        );
        final datedReplay = obj(
          await admin.request(
            'POST',
            dated.path,
            body: dated.body,
            idempotencyKey: dated.key,
          ),
        );
        expect(datedReplay['id'], dated.result['id']);
        final history = rows(
          await owner.request(
            'GET',
            '/api/v1/people/enrollments/$enrollment/group-transfers',
          ),
        );
        expect(history, hasLength(1));
        expect(history.single['id'], dated.result['id']);
        expect(history.single['targetGroupId'], fixture['id']);
        expect(history.single['effectiveDate'], '2026-11-01');
        expect(
          await owner.request('GET', '/api/v1/subscriptions/periods/$period'),
          beforePeriod,
        );
        expect(
          await owner.request('GET', '/api/v1/subscriptions/receipts/$receipt'),
          beforeReceipt,
        );
        parent = await login(
          '01099900995',
          'v45.5278.transfers.guardian995',
          HttpApiTransport(AppConfig('Demo', base)),
        );
        expect(parent.session!.selected!.role, 'Guardian');
        final familyProfile = obj(
          await parent.request(
            'GET',
            '/api/v1/guardian/children/$player/profile',
          ),
        );
        final familyEnrollment = rows(familyProfile['enrollments'])
            .singleWhere((e) => e['id'] == enrollment);
        expect(
          rows(familyEnrollment['upcoming']).any((s) => s['id'] == source),
          false,
        );
        expect(
          rows(familyEnrollment['upcoming']).any((s) => s['id'] == target),
          true,
        );
        final parentAppearance = Appearance();
        for (final theme in ['football', 'general']) {
          await parentAppearance.completeRoleIntro('Guardian', theme);
        }
        await t.pumpWidget(
          AcademyApp(
            key: const ValueKey('guardian995-transfers'),
            auth: parent,
            environment: 'Demo',
            appearance: parentAppearance,
          ),
        );
        await wait(find.byType(NavigationBar));
        await t.tap(
          find.descendant(
            of: find.byType(NavigationBar),
            matching: find.text('الحصص'),
          ),
        );
        await wait(find.byType(RoleAgenda));
        await wait(find.textContaining('مجموعة قبول نقل وتعويض اصطناعية2016'));
        await capture(
          'phase-approved-guardian-transferred-session-agenda-w375',
        );
        final money = obj(
          await owner.request(
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
      } finally {
        await owner.request('PUT', accessPath, body: originalAccess);
        expect(
          accessBody(obj(await owner.request('GET', accessPath))),
          originalAccess,
        );
        final restored = obj(await admin.request('GET', '/api/v1/my-access'));
        for (final leaf in leaves) {
          expect(
            rows(restored['workflowActions'])
                .singleWhere((a) => a['key'] == leaf)['allowed'],
            false,
          );
        }
        await t.pumpWidget(const SizedBox.shrink());
        parent?.dispose();
        admin.dispose();
        owner.dispose();
      }
    },
  );
}
