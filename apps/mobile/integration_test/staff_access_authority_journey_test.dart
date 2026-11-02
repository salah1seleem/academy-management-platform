import 'dart:convert';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/native_staff_management.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

const member = 'f1cb3035-ca00-48dc-b008-b0a1eda9e397';
const endpoint = '/api/v1/manage/staff/$member/access';
const staffLeaf = 'workflow.attendance.staff';
const probePath =
    '/api/v1/attendance/sessions/00000000-0000-0000-0000-000000000011/staff';

List<String> sorted(Object? value) =>
    (value as List? ?? []).cast<String>().toList()..sort();
Json normalizeWrite(Json body) {
  final actions =
      rows(body['actions'])
          .map(
            (a) => <String, dynamic>{
              'module': a['module'],
              for (final k in ['read', 'add', 'edit', 'delete'])
                k: a[k] == true,
            },
          )
          .toList()
        ..sort((a, b) => textOf(a['module']).compareTo(textOf(b['module'])));
  return {
    'allBranches': body['allBranches'] == true,
    'branches': body['allBranches'] == true
        ? <String>[]
        : sorted(body['branches']),
    'allSports': body['allSports'] != false,
    'sports': body['allSports'] != false ? <String>[] : sorted(body['sports']),
    'actions': actions,
    'workflowActions': sorted(body['workflowActions']),
    'sensitivePermissions': sorted(body['sensitivePermissions']),
  };
}

Json normalizeRead(Json read) => normalizeWrite({
  'allBranches': read['allBranches'],
  'allSports': read['allSports'],
  'branches': read['selectedBranches'],
  'sports': read['selectedSports'],
  'actions': [
    for (final m in sorted(read['modules']))
      {
        'module': m,
        for (final k in ['read', 'add', 'edit', 'delete'])
          k:
              rows(read['actions'])
                  .where((a) => a['module'] == m)
                  .firstOrNull?[k] ==
              true,
      },
  ],
  'workflowActions': rows(read['workflowActions'])
      .where((a) => a['allowed'] == true)
      .map((a) => a['key'])
      .toList(),
  'sensitivePermissions': read['selectedSensitivePermissions'],
});

class AccessAcceptanceTransport extends HttpApiTransport {
  AccessAcceptanceTransport(super.config, this.original, this.enabled);
  final Json original, enabled;
  int grantWrites = 0, deniedProbes = 0;
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    if (method != 'GET' && !path.startsWith('/api/v1/mobile/auth/')) {
      if (method == 'PUT' && path == endpoint) {
        final data = obj(body), normalized = normalizeWrite(data);
        if (data.length != original.length ||
            (jsonEncode(normalized) != jsonEncode(original) &&
                jsonEncode(normalized) != jsonEncode(enabled))) {
          throw StateError(
            'Only the captured original or bounded attendance-only intent is permitted.',
          );
        }
        grantWrites++;
      } else if (method == 'PUT' &&
          path == probePath &&
          jsonEncode(body) == jsonEncode({'items': <Object>[]})) {
        deniedProbes++;
      } else {
        throw StateError(
          'No other business writes in staff access acceptance: $method $path',
        );
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

class AccessAppearance extends Appearance {
  @override
  Future<bool> hasRoleIntro(String role, String sportTheme) async => true;
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'owner native permission grant is branch/sport scoped, enforced, reopened and restored',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      const width = int.fromEnvironment('STAFF_ACCESS_WIDTH');
      expect([375, 440], contains(width));
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, width);
      const readOnly = bool.fromEnvironment('STAFF_ACCESS_READ_ONLY');
      expect(readOnly, width == 440);
      final original = normalizeWrite(
        obj(
          jsonDecode(
            utf8.decode(
              base64Decode(
                const String.fromEnvironment('STAFF_ACCESS_EXPECTED'),
              ),
            ),
          ),
        ),
      );
      expect(original['allBranches'], false);
      expect(original['allSports'], false);
      expect(original['branches'], hasLength(1));
      expect(original['sports'], hasLength(1));
      expect(original['workflowActions'], isEmpty);
      final enabled = obj(jsonDecode(jsonEncode(original)));
      enabled['actions'] = [
        for (final a in rows(original['actions']))
          a['module'] == 'attendance' ? {...a, 'read': true, 'edit': true} : a,
      ];
      enabled['workflowActions'] = [staffLeaf];
      final transport = AccessAcceptanceTransport(
        AppConfig('Demo', base),
        original,
        enabled,
      );
      final owner = AuthController(
        transport,
        SecureRefreshStore('v73.5278.approved-intro.AcademyOwner'),
      );
      AuthController? admin;
      final appearance = AccessAppearance()..dark = false;
      t.platformDispatcher.accessibilityFeaturesTestValue =
          const FakeAccessibilityFeatures(disableAnimations: true);
      addTearDown(t.platformDispatcher.clearAccessibilityFeaturesTestValue);
      await owner.restore();
      if (owner.session == null) {
        final c = await owner.requestOtp('01099900010');
        await owner.verify('01099900010', c, '246810');
      }
      expect(owner.session!.selected!.role, 'AcademyOwner');
      expect(
        owner.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final before = obj(await owner.request('GET', endpoint));
      expect(before['useActionGrants'], true);
      expect(before['source'], isNull);
      expect(before['primaryMembershipId'], isNull);
      expect(normalizeRead(before), original);
      final employee = rows(
        obj(await owner.request('GET', '/api/v1/manage/employees'))['items'],
      ).singleWhere((e) => e['membershipId'] == member);
      final staffLabel = textOf(
        rows(before['workflowActions'])
            .singleWhere((a) => a['key'] == staffLeaf)['label'],
      );
      final totals = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      expect(totals['netAmount'], isNotNull);
      expect(totals['totalCount'], isNotNull);
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> reach(Finder f) async {
        if (f.evaluate().isEmpty) {
          final scroll = find.byType(Scrollable).first;
          t.state<ScrollableState>(scroll).position.jumpTo(0);
          await t.pumpAndSettle();
          await t.scrollUntilVisible(
            f,
            150,
            scrollable: scroll,
            maxScrolls: 100,
          );
        }
        await t.ensureVisible(f.first);
        await t.pumpAndSettle();
      }

      Future<void> capture(String suffix) async {
        expect(t.takeException(), isNull);
        await binding.takeScreenshot(
          'phase-approved-staff-access-$width-$suffix',
        );
      }

      Future<void> module() async {
        if (find
            .widgetWithText(CheckboxListTile, staffLabel)
            .evaluate()
            .isEmpty) {
          final tile = find.widgetWithText(ExpansionTile, 'الحضور');
          await reach(tile);
          await t.tap(tile);
          await t.pumpAndSettle();
        }
        await reach(find.widgetWithText(CheckboxListTile, staffLabel));
      }

      Future<void> openEditor() async {
        await wait(find.byType(NativeStaffManagement));
        await wait(find.text('المدربون والموظفون'));
        final search = find.byType(TextField);
        await reach(search);
        await t.enterText(search, textOf(employee['phone']));
        await t.pumpAndSettle();
        final card = find
            .ancestor(
              of: find.text(textOf(employee['name'])),
              matching: find.byType(GCard),
            )
            .first;
        await reach(card);
        final button = find.descendant(
          of: card,
          matching: find.widgetWithText(TextButton, 'الأفعال ونطاق الصلاحيات'),
        );
        await reach(button);
        await t.tap(button);
        await wait(find.byType(NativeStaffAccessEditor));
        await module();
      }

      Future<void> save() async {
        final button = find.text('اعتماد الصلاحيات');
        await reach(button);
        await t.tap(button);
        await t.pumpAndSettle();
        await t.tap(find.text('تأكيد الاعتماد'));
        await t.pump();
        await wait(find.text('حُفظت الصلاحيات وأُعيد التحقق منها.'));
      }

      Future<void> refreshAdmin() async {
        try {
          await admin!.refresh();
        } on ApiFailure catch (e) {
          if (e.status != 401) rethrow;
        }
        if (admin!.session == null) {
          final c = await admin.requestOtp('01099900992');
          await admin!.verify('01099900992', c, '246810');
        }
        expect(admin!.session!.selected!.id, member);
        expect(admin.session!.selected!.role, 'AcademyAdmin');
        expect(
          admin.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
      }

      Future<void> probe(int status) async {
        await expectLater(
          admin!.request('PUT', probePath, body: {'items': <Object>[]}),
          throwsA(isA<ApiFailure>().having((e) => e.status, 'status', status)),
        );
        if (status == 403) {
          await expectLater(
            admin!.request('GET', '/api/v1/attendance/sessions'),
            throwsA(
              isA<ApiFailure>().having((e) => e.status, 'read is denied', 403),
            ),
          );
        } else {
          expect(
            await admin!.request('GET', '/api/v1/attendance/sessions'),
            isA<List>(),
          );
        }
      }

      try {
        if (!readOnly) {
          admin = AuthController(
            transport,
            SecureRefreshStore('v77.5278.staff-access.admin992'),
          );
          await admin.restore();
          await refreshAdmin();
          await probe(403);
          await expectLater(
            admin.request('GET', endpoint),
            throwsA(
              isA<ApiFailure>().having(
                (e) => e.status,
                'owner-only editor',
                403,
              ),
            ),
          );
        }
        await t.pumpWidget(
          AcademyApp(auth: owner, appearance: appearance, environment: 'Demo'),
        );
        await wait(find.byType(NativeRoleTabs));
        await t.tap(find.byKey(const ValueKey('owner-nav-4')));
        await t.pumpAndSettle();
        final staff = find.widgetWithText(GLink, 'الموظفون والصلاحيات');
        await reach(staff);
        await t.tap(staff);
        await openEditor();
        expect(
          t
              .widget<CheckboxListTile>(
                find.widgetWithText(CheckboxListTile, staffLabel),
              )
              .value,
          false,
        );
        await capture(readOnly ? 'original-read-only' : 'original-open');
        if (!readOnly) {
          await t.tap(find.widgetWithText(CheckboxListTile, staffLabel));
          await t.pumpAndSettle();
          await save();
          expect(
            normalizeRead(obj(await owner.request('GET', endpoint))),
            enabled,
          );
          await refreshAdmin();
          await probe(404);
          await expectLater(
            admin!.request(
              'GET',
              '/api/v1/reports/financial?allDates=true&pageSize=1',
            ),
            throwsA(
              isA<ApiFailure>().having(
                (e) => e.status,
                'finance remains denied',
                403,
              ),
            ),
          );
          await t.tap(find.byType(BackButton));
          await openEditor();
          expect(
            t
                .widget<CheckboxListTile>(
                  find.widgetWithText(CheckboxListTile, staffLabel),
                )
                .value,
            true,
          );
          await capture('granted-reopened');
          await t.tap(find.widgetWithText(CheckboxListTile, staffLabel));
          await t.pumpAndSettle();
          for (final (key, label) in [
            ('add', 'إضافة الحضور'),
            ('edit', 'تعديل الحضور'),
            ('delete', 'حذف الحضور'),
            ('read', 'عرض الحضور'),
          ]) {
            final control = find.widgetWithText(SwitchListTile, label);
            await reach(control);
            final wanted =
                rows(original['actions'])
                    .singleWhere((a) => a['module'] == 'attendance')[key] ==
                true;
            if (t.widget<SwitchListTile>(control).value != wanted) {
              await t.tap(control);
              await t.pumpAndSettle();
            }
          }
          await save();
          expect(
            normalizeRead(obj(await owner.request('GET', endpoint))),
            original,
          );
          await refreshAdmin();
          await probe(403);
          await t.tap(find.byType(BackButton));
          await openEditor();
          expect(
            t
                .widget<CheckboxListTile>(
                  find.widgetWithText(CheckboxListTile, staffLabel),
                )
                .value,
            false,
          );
          await capture('original-restored');
        }
        expect(
          normalizeRead(obj(await owner.request('GET', endpoint))),
          original,
        );
        final after = obj(
          await owner.request(
            'GET',
            '/api/v1/reports/financial?allDates=true&pageSize=1',
          ),
        );
        expect(after['netAmount'], totals['netAmount']);
        expect(after['totalCount'], totals['totalCount']);
        expect(transport.grantWrites, readOnly ? 0 : 2);
        expect(transport.deniedProbes, readOnly ? 0 : 3);
        expect(t.takeException(), isNull);
      } finally {
        final live = normalizeRead(obj(await owner.request('GET', endpoint)));
        if (jsonEncode(live) != jsonEncode(original)) {
          expect(
            live,
            enabled,
            reason:
                'Never overwrite a grant changed outside this bounded test.',
          );
          await owner.request('PUT', endpoint, body: original);
          expect(
            normalizeRead(obj(await owner.request('GET', endpoint))),
            original,
          );
        }
        await t.pumpWidget(const SizedBox());
        await t.pumpAndSettle();
        owner.dispose();
        admin?.dispose();
        appearance.dispose();
        transport.close();
      }
    },
    timeout: const Timeout(Duration(minutes: 12)),
  );
}
