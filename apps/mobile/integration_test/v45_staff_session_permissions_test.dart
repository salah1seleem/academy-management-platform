import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/owner/native_staff_management.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/staff_capabilities.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class SessionPermissionTransport extends HttpApiTransport {
  SessionPermissionTransport(super.config);
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    try {
      return await super.send(
        method,
        path,
        body: body,
        token: token,
        idempotencyKey: idempotencyKey,
      );
    } on ApiFailure catch (failure) {
      if (method == 'POST' && path.endsWith('/sessions')) {
        debugPrint(
          'Synthetic scoped session response: ${failure.status} / ${failure.message}',
        );
      }
      rethrow;
    }
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'owner creates a synthetic admin and saves only session editing, admin grants once then loses access after revocation',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      Future<AuthController> login(String phone) async {
        final auth = AuthController(
          SessionPermissionTransport(AppConfig('Demo', base)),
          SecureRefreshStore('v45.5278.staff-grant.$phone'),
        );
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

      final owner = await login('01099900010');
      final money = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      Future<void> waitFor(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        if (f.evaluate().isEmpty) {
          await binding.takeScreenshot(
            'phase-approved-staff-permission-diagnostic-w375',
          );
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> reach(Finder f) async {
        await waitFor(find.byType(Scrollable));
        final scroll = find.byType(Scrollable).first;
        t.state<ScrollableState>(scroll).position.jumpTo(0);
        await t.pumpAndSettle();
        await t.scrollUntilVisible(f, 140, scrollable: scroll);
        await t.ensureVisible(f.last);
        await t.pumpAndSettle();
      }

      Future<void> tap(String label) async {
        final f = find.text(label);
        await reach(f);
        await t.tap(f.last);
        await t.pumpAndSettle();
      }

      Future<void> field(String label, String value) async {
        final f = find.widgetWithText(TextFormField, label);
        await reach(f);
        await t.enterText(f, value);
        await t.pumpAndSettle();
      }

      Future<void> show(Widget child, String key) async {
        await t.pumpWidget(
          MaterialApp(
            key: ValueKey(key),
            debugShowCheckedModeBanner: false,
            locale: const Locale('ar'),
            supportedLocales: const [Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            theme: AcademyTheme.dark(),
            home: child,
          ),
        );
        await t.pump(const Duration(milliseconds: 350));
      }

      const phone = '01099900992', name = 'إداري حصص اصطناعي992';
      var accounts = obj(await owner.request('GET', '/api/v1/manage/staff'));
      var account = rows(accounts['members'])
          .where(
            (m) =>
                m['phoneNumber'] == '+201099900992' ||
                m['phoneNumber'] == phone,
          )
          .firstOrNull;
      if (account == null) {
        await show(NativeStaffManagement(auth: owner), 'staff');
        await waitFor(find.text('إضافة حساب دخول'));
        await tap('إضافة حساب دخول');
        await field('الاسم', name);
        await field('الهاتف', phone);
        await field('البريد الإلكتروني', 'sessions.admin.992@example.test');
        await field('المسمى الوظيفي', 'حساب قبول اصطناعي');
        await field('كلمة المرور الأولية', 'Demo-Session-Account-2026!');
        await tap('إنشاء الحساب');
        await waitFor(find.byType(NativeStaffAccessEditor));
        accounts = obj(await owner.request('GET', '/api/v1/manage/staff'));
        account = rows(accounts['members'])
            .singleWhere((m) => m['name'] == name);
      } else {
        await show(
          NativeStaffAccessEditor(
            auth: owner,
            membershipId: textOf(account['id']),
            name: name,
          ),
          'resume-editor',
        );
      }
      final memberId = textOf(account['id']);
      debugPrint('Synthetic session admin membership: $memberId');
      final endpoint = '/api/v1/manage/staff/$memberId/access';
      final selectedPeriod = obj(
        await owner.request(
          'GET',
          '/api/v1/subscriptions/periods/02f8772b-4afe-4446-9f52-4af1f96d11c6',
        ),
      );
      await waitFor(find.text('الأفعال المخولة'));
      Future<void> narrowScope(String switchLabel, String selectedLabel) async {
        final tile = find.widgetWithText(SwitchListTile, switchLabel);
        if (t.widget<SwitchListTile>(tile).value) {
          await tap(switchLabel);
        }
        final checkbox = find.ancestor(
          of: find.text(selectedLabel),
          matching: find.byType(CheckboxListTile),
        );
        await reach(checkbox);
        if (t.widget<CheckboxListTile>(checkbox).value != true) {
          await tap(selectedLabel);
        }
      }

      await narrowScope('كل الفروع', textOf(selectedPeriod['branch']));
      await narrowScope('كل الرياضات', textOf(selectedPeriod['sport']));

      Future<void> openSubscriptions() async {
        // The editor uses a lazy list. Return to the module before looking for its children.
        await reach(find.text('الباقات والاشتراكات'));
        if (find.text('إضافة حصص وتحديد نهاية الاشتراك').evaluate().isEmpty) {
          await t.tap(find.text('الباقات والاشتراكات').last);
          await t.pumpAndSettle();
        }
        await waitFor(find.text('إضافة حصص وتحديد نهاية الاشتراك'));
      }

      Future<void> saveAccess() async {
        await tap('اعتماد الصلاحيات');
        await waitFor(find.text('تأكيد الاعتماد'));
        await t.tap(find.text('تأكيد الاعتماد'));
        await t.pumpAndSettle();
        await waitFor(find.text('حُفظت الصلاحيات وأُعيد التحقق منها.'));
      }

      await openSubscriptions();
      final check = find.ancestor(
        of: find.text('إضافة حصص وتحديد نهاية الاشتراك'),
        matching: find.byType(CheckboxListTile),
      );
      if (!t.widget<CheckboxListTile>(check).value!) {
        await tap('إضافة حصص وتحديد نهاية الاشتراك');
      }
      await saveAccess();
      final savedAccess = obj(await owner.request('GET', endpoint));
      expect(savedAccess['allBranches'], isFalse);
      expect(savedAccess['allSports'], isFalse);
      expect(savedAccess['selectedBranches'], hasLength(1));
      expect(savedAccess['selectedSports'], hasLength(1));
      final action = rows(savedAccess['actions'])
          .singleWhere((a) => a['module'] == 'subscriptions');
      expect(action['read'], isTrue);
      expect(action['edit'], isTrue);
      expect(action['add'], isFalse);
      expect(
        rows(savedAccess['workflowActions'])
            .where((a) => a['allowed'] == true)
            .map((a) => a['key']),
        ['workflow.subscriptions.sessions'],
      );
      await openSubscriptions();
      await reach(find.text('إضافة حصص وتحديد نهاية الاشتراك'));
      await binding.takeScreenshot(
        'phase-approved-staff-session-permission-saved-w375',
      );
      final admin = await login(phone);
      expect(admin.session!.selected!.role, 'AcademyAdmin');
      var access = await StaffCapabilities.load(admin);
      expect(
        access.allows(
          'subscription.adjustment.manage',
          'subscriptions',
          'Edit',
          ['workflow.subscriptions.sessions'],
        ),
        isTrue,
      );
      expect(access.allows('payment.read', 'payments', 'Read'), isFalse);
      expect(
        access.allows('people.write', 'players', 'Add', [
          'workflow.players.create',
        ]),
        isFalse,
      );
      const periodId = '02f8772b-4afe-4446-9f52-4af1f96d11c6';
      var before = obj(
        await admin.request('GET', '/api/v1/subscriptions/periods/$periodId'),
      );
      expect(before['canGrantSessions'], isTrue);
      expect(before['priceSnapshot'], isNull);
      expect(before['receiptId'], isNull);
      final existing = rows(before['adjustments'])
          .where(
            (a) =>
                a['type'] == 'SessionsAdded' &&
                a['reason'] == 'قبول إداري992 اصطناعي',
          )
          .firstOrNull;
      await show(
        NativeSubscriptionPeriod(auth: admin, id: periodId, access: access),
        'admin-period',
      );
      if (existing == null) {
        final select = find.widgetWithText(
          DropdownButtonFormField<String>,
          'الإجراء',
        );
        await reach(select);
        await t.tap(select);
        await t.pumpAndSettle();
        await t.tap(find.text('إضافة حصص وتحديد نهاية الاشتراك').last);
        await t.pumpAndSettle();
        await field('عدد الحصص الإضافية', '1');
        final date = find.widgetWithText(
          TextFormField,
          'نهاية الاشتراك الجديدة',
        );
        await reach(date);
        await t.tap(date);
        await t.pumpAndSettle();
        final local = MaterialLocalizations.of(
          t.element(find.byType(DatePickerDialog)),
        );
        await t.tap(find.byIcon(Icons.edit_outlined));
        await t.pumpAndSettle();
        final end = DateTime.parse(textOf(before['endDate']))
            .add(const Duration(days: 1));
        await t.enterText(
          find.descendant(
            of: find.byType(DatePickerDialog),
            matching: find.byType(TextField),
          ),
          local.formatCompactDate(end),
        );
        await t.tap(find.text(local.okButtonLabel));
        await t.pumpAndSettle();
        await field('سبب التعديل', 'قبول إداري992 اصطناعي');
        await tap('راجعت أثر التعديل على الاشتراك وأؤكد تنفيذه.');
        await tap('تأكيد تعديل الاشتراك');
        await waitFor(find.text('حُفظ التعديل وأُعيدت قراءة سجل الاشتراك.'));
      }
      final committed = obj(
        await owner.request('GET', '/api/v1/subscriptions/periods/$periodId'),
      );
      final audit = rows(committed['adjustments']).singleWhere(
        (a) =>
            a['type'] == 'SessionsAdded' &&
            a['reason'] == 'قبول إداري992 اصطناعي',
      );
      debugPrint(
        'Synthetic admin session grant: $periodId / audit ${audit['id']} / balance ${committed['remainingSessions']} / end ${committed['endDate']}',
      );
      expect(audit['sessionsDelta'], 1);
      expect(
        (audit['newRemainingSessions'] as int) -
            (audit['oldRemainingSessions'] as int),
        1,
      );
      await reach(find.textContaining('+1 حصة'));
      await binding.takeScreenshot(
        'phase-approved-admin-session-grant-redacted-w375',
      );
      expect(find.textContaining('القيمة المسجلة'), findsNothing);
      await show(
        NativeStaffAccessEditor(
          auth: owner,
          membershipId: memberId,
          name: name,
        ),
        'revoke',
      );
      await openSubscriptions();
      await tap('إضافة حصص وتحديد نهاية الاشتراك');
      await saveAccess();
      access = await StaffCapabilities.load(admin);
      await show(
        NativeSubscriptionPeriod(auth: admin, id: periodId, access: access),
        'admin-after-revoke',
      );
      await waitFor(find.text('سجل التعديلات'));
      expect(find.text('إضافة حصص وتحديد نهاية الاشتراك'), findsNothing);
      final read = obj(
        await admin.request('GET', '/api/v1/subscriptions/periods/$periodId'),
      );
      expect(read['canGrantSessions'], isFalse);
      await expectLater(
        admin.request(
          'POST',
          '/api/v1/subscriptions/periods/$periodId/sessions',
          body: {
            'sessions': 1,
            'endDate': committed['endDate'],
            'reason': 'طلب مرفوض بعدسحبالإذن',
            'expectedVersion': committed['version'],
          },
          idempotencyKey: 'rejected-session-admin-992',
        ),
        throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 403)),
      );
      final finalRead = obj(
        await owner.request('GET', '/api/v1/subscriptions/periods/$periodId'),
      );
      expect(finalRead['remainingSessions'], committed['remainingSessions']);
      expect(
        rows(finalRead['adjustments']).length,
        rows(committed['adjustments']).length,
      );
      final after = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      expect(money['netAmount'], isNotNull);
      expect(after['netAmount'], money['netAmount']);
      expect(after['totalCount'], money['totalCount']);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      await admin.logout();
      await owner.logout();
      admin.dispose();
      owner.dispose();
    },
  );
}
