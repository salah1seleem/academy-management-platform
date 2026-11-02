import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/native_operation_form.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/staff_capabilities.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class AdminDiscountAcceptanceTransport extends HttpApiTransport {
  AdminDiscountAcceptanceTransport(super.config);
  Json? quote;
  ({Json body, String key, Json result})? registration;
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    final Object? value;
    try {
      value = await super.send(
        method,
        path,
        body: body,
        token: token,
        idempotencyKey: idempotencyKey,
      );
    } on ApiFailure catch (e) {
      if (method == 'POST' && path == '/api/v1/people/registrations') {
        debugPrint(
          'Synthetic registration rejection: ${e.status} / ${e.message}',
        );
      }
      rethrow;
    }
    if (method == 'POST' && path == '/api/v1/people/registration-quote') {
      quote = obj(value);
    }
    if (method == 'POST' && path == '/api/v1/people/registrations') {
      registration = (
        body: Map<String, dynamic>.from(body as Map),
        key: idempotencyKey!,
        result: obj(value),
      );
    }
    return value;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'explicitly authorized admin confirms a native percentage discount and restores permission',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      Future<AuthController> login(
        String phone,
        HttpApiTransport transport,
      ) async {
        final auth = AuthController(
          transport,
          SecureRefreshStore(
            phone == '01099900010'
                ? 'v45.5278.os-media-entry.$phone'
                : 'v45.5278.enrollment-no-discount.$phone',
          ),
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

      final owner = await login(
        '01099900010',
        HttpApiTransport(AppConfig('Demo', base)),
      );
      final transport = AdminDiscountAcceptanceTransport(
        AppConfig('Demo', base),
      );
      final admin = await login('01099900993', transport);
      expect(admin.session!.selected!.role, 'AcademyAdmin');
      final path = '/api/v1/manage/staff/${admin.session!.selected!.id}/access';
      final original = obj(await owner.request('GET', path));
      Json accessBody(Json v) => {
        'allBranches': v['allBranches'],
        'branches': v['selectedBranches'],
        'allSports': v['allSports'],
        'sports': v['selectedSports'],
        'actions': rows(v['actions'])
          ..sort((a, b) => textOf(a['module']).compareTo(textOf(b['module']))),
        'sensitivePermissions': v['selectedSensitivePermissions'],
        'workflowActions': rows(v['workflowActions'])
            .where((a) => a['allowed'] == true)
            .map((a) => a['key'])
            .toList(),
      };
      final restore = accessBody(original);
      const leaf = 'workflow.subscriptions.exceptional-discount';
      expect((restore['workflowActions'] as List).contains(leaf), isFalse);
      final existing = obj(
        await owner.request(
          'GET',
          '/api/v1/people/players?search=01099900995&page=1&pageSize=25',
        ),
      );
      const resume = bool.fromEnvironment('RESUME_AUTHORIZED_DISCOUNT');
      if (resume) {
        // Read the saved write; never reenroll the synthetic player for evidence.
        expect(
          rows(existing['items'])
              .where((p) => p['id'] == '46c43bbe-b1b4-43bd-a10f-ce6fa2e34efd'),
          hasLength(1),
        );
        final allowed = (restore['workflowActions'] as List).toSet();
        expect(allowed, {
          'workflow.players.read',
          'workflow.players.create',
          'workflow.payments.read',
          'workflow.payments.collect',
        });
        expect(
          rows(restore['actions'])
              .where((a) => a['read'] == true)
              .map((a) => a['module'])
              .toSet(),
          {'players', 'subscriptions', 'payments', 'finance', 'academy'},
        );
        expect(
          rows(restore['actions'])
              .where((a) => a['add'] == true)
              .map((a) => a['module'])
              .toSet(),
          {'players', 'subscriptions', 'payments', 'finance'},
        );
        expect(
          rows(restore['actions'])
              .where((a) => a['edit'] == true)
              .map((a) => a['module'])
              .toSet(),
          {'payments'},
        );
        expect(restore['allBranches'], false);
        expect((restore['branches'] as List), hasLength(1));
        expect(restore['allSports'], true);
        expect(restore['sports'], isEmpty);
        expect(restore['sensitivePermissions'], isEmpty);
        const periodId = '26beb9c0-eaee-4fe8-8739-aa303cd4b766';
        const receiptId = '204edd10-3892-4d67-9288-02b2cf04e572';
        final period = obj(
          await admin.request('GET', '/api/v1/subscriptions/periods/$periodId'),
        );
        expect(period['receiptId'], receiptId);
        expect(obj(period['pricing'])['discountValue'], 15);
        expect(obj(period['pricing'])['discountAmount'], 135);
        expect(period['priceSnapshot'], 765);
        final receipt = obj(
          await admin.request(
            'GET',
            '/api/v1/subscriptions/receipts/$receiptId',
          ),
        );
        expect(receipt['amount'], 765);
        expect(receipt['paymentMethod'], 'Online');
        final appearance = Appearance();
        for (final theme in ['football', 'general']) {
          await appearance.completeRoleIntro('AcademyAdmin', theme);
        }
        await t.pumpWidget(
          AcademyApp(auth: admin, environment: 'Demo', appearance: appearance),
        );
        Future<void> wait(Finder f) async {
          for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
            await t.pump(const Duration(milliseconds: 150));
          }
          expect(f, findsWidgets);
          await t.pumpAndSettle();
        }

        await wait(find.byType(NavigationBar));
        final access = await StaffCapabilities.load(admin);
        Navigator.of(t.element(find.byType(NavigationBar))).push(
          MaterialPageRoute<void>(
            builder: (_) => NativeSubscriptionPeriod(
              auth: admin,
              id: periodId,
              access: access,
            ),
          ),
        );
        await wait(find.text('إيصال التحصيل'));
        await t.ensureVisible(find.text('إيصال التحصيل'));
        await t.pumpAndSettle();
        await t.tap(find.text('إيصال التحصيل'));
        await wait(find.byType(ReceiptPage));
        await wait(find.textContaining('765'));
        await t.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 500)),
        );
        await binding.takeScreenshot(
          'phase-approved-admin-authorized-discount-receipt-resumed-w375',
        );
        expect(transport.registration, isNull);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox.shrink());
        admin.dispose();
        owner.dispose();
        return;
      }
      expect(
        rows(existing['items'])
            .where((p) => p['arabicName'] == 'لاعب خصم إداري اصطناعي995'),
        isEmpty,
        reason: 'Do not create the saved discount again; resume the stored source IDs.',
      );
      final before = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      final structure = obj(
        await owner.request('GET', '/api/v1/manage/structure/options'),
      );
      final plans = rows(
        await owner.request('GET', '/api/v1/subscriptions/plans'),
      );
      Json? selectedGroup, selectedPlan;
      for (final g in rows(structure['groups']).reversed) {
        if (g['registrationFormat'] != 'Group' ||
            !(original['selectedBranches'] as List).contains(g['branchId'])) {
          continue;
        }
        final c = rows(structure['categories'])
            .singleWhere((c) => c['id'] == g['ageCategoryId']);
        if ((c['minimumBirthYear'] as num? ?? 0) > 2016 ||
            (c['maximumBirthYear'] as num? ?? 9999) < 2016) {
          continue;
        }
        final p = plans
            .where(
              (p) =>
                  p['sportId'] == g['sportId'] &&
                  p['isActive'] == true &&
                  p['serviceKind'] == 'Training' &&
                  (p['trainingFormat'] == null ||
                      p['trainingFormat'] == 'Group') &&
                  (p['allBranches'] == true ||
                      (p['branchIds'] as List? ?? []).contains(
                        g['branchId'],
                      )) &&
                  num.parse('${p['price']}') > 100,
            )
            .firstOrNull;
        if (p == null) continue;
        final detail = obj(
          await owner.request(
            'GET',
            '/api/v1/manage/groups/${g['id']}/details',
          ),
        );
        final capacity = obj(detail['group'])['capacity'] as num?;
        if (capacity != null && capacity <= rows(detail['players']).length) {
          continue;
        }
        selectedGroup = g;
        selectedPlan = p;
        break;
      }
      expect(selectedGroup, isNotNull);
      expect(selectedPlan, isNotNull);
      final group = selectedGroup!, plan = selectedPlan!;
      final permission = {
        ...restore,
        'workflowActions': [...restore['workflowActions'] as List, leaf],
      };
      try {
        await owner.request('PUT', path, body: permission);
        final scope = obj(await admin.request('GET', '/api/v1/my-access'));
        expect(
          rows(scope['workflowActions'])
              .singleWhere((a) => a['key'] == leaf)['allowed'],
          isTrue,
        );
        Future<void> waitFor(Finder f, {bool settle = true}) async {
          for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
            await t.pump(const Duration(milliseconds: 150));
          }
          if (f.evaluate().isEmpty) {
            final scrolls = find.byType(Scrollable);
            if (scrolls.evaluate().isNotEmpty) {
              t.state<ScrollableState>(scrolls.first).position.jumpTo(0);
              await t.pump(const Duration(milliseconds: 250));
            }
            await binding.takeScreenshot(
              'phase-approved-admin-enrollment-diagnostic-w375',
            );
            debugPrint(
              'Synthetic native registration visible diagnostic: ${t.widgetList<Text>(find.byType(Text)).map((w) => w.data).whereType<String>().join(' / ')}',
            );
          }
          expect(f, findsWidgets);
          if (settle) await t.pumpAndSettle();
        }

        Future<void> reach(Finder f) async {
          final scroll = find.byType(Scrollable).first;
          t.state<ScrollableState>(scroll).position.jumpTo(0);
          await t.pumpAndSettle();
          await t.scrollUntilVisible(
            f,
            140,
            scrollable: scroll,
            maxScrolls: 100,
          );
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

        Future<void> choose(String label, String option) async {
          final f = find.widgetWithText(DropdownButtonFormField<String>, label);
          await reach(f);
          await t.tap(f);
          await t.pumpAndSettle();
          final item = find.text(option).last;
          if (item.evaluate().isEmpty) {
            await t.scrollUntilVisible(
              item,
              90,
              scrollable: find.byType(Scrollable).last,
            );
          }
          await t.ensureVisible(item);
          await t.pumpAndSettle();
          await t.tap(item);
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
          await appearance.completeRoleIntro('AcademyAdmin', theme);
        }
        await t.pumpWidget(
          AcademyApp(auth: admin, environment: 'Demo', appearance: appearance),
        );
        await waitFor(find.byType(NavigationBar));
        await t.tap(
          find.descendant(
            of: find.byType(NavigationBar),
            matching: find.text('العمليات'),
          ),
        );
        await waitFor(find.text('اشتراك جديد'));
        await tap('اشتراك جديد');
        await waitFor(
          find.widgetWithText(TextFormField, 'اسم اللاعب بالعربية'),
        );
        await field('اسم اللاعب بالعربية', 'لاعب خصم إداري اصطناعي995');
        await tap('تاريخ الميلاد');
        await waitFor(find.byType(DatePickerDialog));
        await t.tap(
          find.text(
            MaterialLocalizations.of(t.element(find.byType(DatePickerDialog)))
                .okButtonLabel,
          ),
        );
        await t.pumpAndSettle();
        await field('اسم ولي الأمر', 'ولي أمر خصم اصطناعي995');
        await field('رقم الهاتف', '01099900995');
        await choose(
          'الفرع',
          textOf(
            rows(structure['branches'])
                .singleWhere((b) => b['id'] == group['branchId'])['arabicName'],
          ),
        );
        await choose(
          'الرياضة',
          textOf(
            rows(structure['sports'])
                .singleWhere((b) => b['id'] == group['sportId'])['arabicName'],
          ),
        );
        await choose('المجموعة', textOf(group['arabicName']));
        await choose(
          'الباقة',
          '${plan['arabicName']} · ${money(plan['price'], plan['currency'])}',
        );
        await tap('إضافة خصم استثنائي');
        await choose('نوع الخصم', 'نسبة مئوية');
        await field('قيمة الخصم', '15');
        await field('سبب الخصم', 'خصم قبول اصطناعي موثق للإداري المخول995');
        await tap('حساب المبلغ المستحق');
        await waitFor(find.text('المبلغ النهائي'));
        final amount = num.parse('${transport.quote!['finalAmount']}');
        expect(
          storedAmountMatches(
            transport.quote!['originalAmount'],
            plan['price'],
          ),
          isTrue,
        );
        expect(amount, num.parse('${plan['price']}') * 85 / 100);
        await choose('طريقة التحصيل', 'تحويل إلكتروني تم استلامه');
        await tap('أؤكد استلام المبلغ النهائي فعليًا');
        await tap('مراجعة الاشتراك');
        await waitFor(find.text('الخطوة 2 من 2 · المراجعة والتأكيد'));
        await reach(find.text('تأكيد وإنشاء الاشتراك'));
        await capture('phase-approved-admin-authorized-discount-review-w375');
        await t.tap(find.text('تأكيد وإنشاء الاشتراك'));
        await t.pump(const Duration(milliseconds: 400));
        await waitFor(find.byType(AlertDialog), settle: false);
        await t.tap(find.text('تأكيد').last);
        await t.pump(const Duration(milliseconds: 400));
        await waitFor(find.text('تم التسجيل والتحصيل وإصدار الإيصال.'));
        await capture('phase-approved-admin-authorized-discount-saved-w375');
        final saved = transport.registration!;
        debugPrint('Synthetic authorized admin discount: ${saved.result}');
        expect(saved.result['paymentId'], isNotNull);
        expect(saved.result['receiptId'], isNotNull);
        expect(saved.result['waiverId'], isNull);
        final period = obj(
          await admin.request(
            'GET',
            '/api/v1/subscriptions/periods/${saved.result['periodId']}',
          ),
        );
        expect(
          storedAmountMatches(
            period['priceSnapshot'],
            transport.quote!['finalAmount'],
          ),
          isTrue,
        );
        expect(period['receiptId'], saved.result['receiptId']);
        final receipt = obj(
          await admin.request(
            'GET',
            '/api/v1/subscriptions/receipts/${saved.result['receiptId']}',
          ),
        );
        expect(num.parse('${receipt['amount']}'), amount);
        expect(receipt['paymentMethod'], 'Online');
        final pricing = obj(period['pricing']);
        debugPrint('Synthetic discount stored pricing: $pricing');
        for (final k in ['originalAmount', 'discountAmount', 'finalAmount']) {
          expect(storedAmountMatches(pricing[k], transport.quote![k]), isTrue);
        }
        final replay = obj(
          await admin.request(
            'POST',
            '/api/v1/people/registrations',
            body: saved.body,
            idempotencyKey: saved.key,
          ),
        );
        for (final k in [
          'playerId',
          'sportEnrollmentId',
          'periodId',
          'waiverId',
          'paymentId',
          'receiptId',
        ]) {
          expect(replay[k], saved.result[k]);
        }
        final unchanged = rows(
          await owner.request('GET', '/api/v1/subscriptions/plans'),
        ).singleWhere((p) => p['id'] == plan['id']);
        expect(unchanged['price'], plan['price']);
        final after = obj(
          await owner.request(
            'GET',
            '/api/v1/reports/financial?allDates=true&pageSize=1',
          ),
        );
        expect(after['netAmount'], (before['netAmount'] as num) + amount);
        expect(after['totalCount'], (before['totalCount'] as num) + 1);
        expect(t.takeException(), isNull);
      } finally {
        await owner.request('PUT', path, body: restore);
        final restored = obj(await admin.request('GET', '/api/v1/my-access'));
        expect(
          rows(restored['workflowActions'])
              .singleWhere((a) => a['key'] == leaf)['allowed'],
          isFalse,
        );
        final finalAccess = accessBody(obj(await owner.request('GET', path)));
        expect(finalAccess, restore);
        await t.pumpWidget(const SizedBox.shrink());
        admin.dispose();
        owner.dispose();
      }
    },
  );
}
