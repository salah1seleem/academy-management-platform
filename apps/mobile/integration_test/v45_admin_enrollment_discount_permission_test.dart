import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/native_enrollment.dart';
import 'package:academy_mobile/features/admin/native_operation_form.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/staff_capabilities.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class AdminEnrollmentAcceptanceTransport extends HttpApiTransport {
  AdminEnrollmentAcceptanceTransport(super.config);
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
  const resumePlayer = String.fromEnvironment('RESUME_ADMIN_PLAYER_ID');
  const resumePeriod = String.fromEnvironment('RESUME_ADMIN_PERIOD_ID');
  const resumeReceipt = String.fromEnvironment('RESUME_ADMIN_RECEIPT_ID');
  testWidgets(
    'branch scoped admin creates an ordinary enrollment natively while exceptional quote and registration are forbidden',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      Future<AuthController> login(
        String phone, {
        HttpApiTransport? transport,
      }) async {
        final auth = AuthController(
          transport ?? HttpApiTransport(AppConfig('Demo', base)),
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

      final owner = await login('01099900010');
      if (resumePlayer.isEmpty) {
        final existing = obj(
          await owner.request(
            'GET',
            '/api/v1/people/players?search=01099900994&page=1&pageSize=25',
          ),
        );
        expect(
          rows(existing['items'])
              .where((p) => p['arabicName'] == 'لاعب تسجيل إداري اصطناعي994'),
          isEmpty,
          reason: 'This synthetic enrollment already exists. Resume its saved IDs rather than creating another player.',
        );
      }
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
      Json? group, plan;
      // Choose an existing valid group with room. Do not change capacity or age rules.
      for (final g in rows(structure['groups']).reversed) {
        if (g['registrationFormat'] != 'Group') continue;
        final category = rows(structure['categories'])
            .singleWhere((c) => c['id'] == g['ageCategoryId']);
        if ((category['minimumBirthYear'] as num? ?? 0) > 2016 ||
            (category['maximumBirthYear'] as num? ?? 9999) < 2016) {
          continue;
        }
        final eligible = plans
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
        if (eligible == null) continue;
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
        group = g;
        plan = eligible;
        break;
      }
      expect(group, isNotNull);
      expect(plan, isNotNull);
      const adminPhone = '01099900993', adminName = 'إداري تسجيل اصطناعي993';
      var account =
          rows(
                obj(
                  await owner.request('GET', '/api/v1/manage/staff'),
                )['members'],
              )
              .where(
                (a) =>
                    a['phoneNumber'] == '+201099900993' ||
                    a['phoneNumber'] == adminPhone,
              )
              .firstOrNull;
      account ??= obj(
        await owner.request(
          'POST',
          '/api/v1/manage/staff',
          body: {
            'name': adminName,
            'email': 'enrollment.admin.993@example.test',
            'phone': adminPhone,
            'temporaryPassword': 'Demo-Enrollment-Account-2026!',
            'jobTitle': 'حساب قبول اصطناعي',
            'role': 'AcademyAdmin',
          },
        ),
      );
      final accessPath = '/api/v1/manage/staff/${account['id']}/access';
      final catalogue = obj(await owner.request('GET', accessPath));
      final actions = (catalogue['modules'] as List).cast<String>().map((name) {
        final read = [
          'players',
          'subscriptions',
          'payments',
          'finance',
          'academy',
        ].contains(name);
        return <String, dynamic>{
          'module': name,
          'read': read,
          'add': [
            'players',
            'subscriptions',
            'payments',
            'finance',
          ].contains(name),
          'edit': name == 'payments',
          'delete': false,
        };
      }).toList();
      expect(actions.where((a) => a['read'] == true), hasLength(5));
      await owner.request(
        'PUT',
        accessPath,
        body: {
          'allBranches': false,
          'branches': [group!['branchId']],
          // Provisioning a new family includes a shared Guardian membership.
          // Existing sport-scoped contexts deliberately cannot create identities.
          'allSports': true,
          'sports': <String>[],
          'actions': actions,
          'sensitivePermissions': <String>[],
          'workflowActions': [
            'workflow.players.read',
            'workflow.players.create',
            'workflow.payments.read',
            'workflow.payments.collect',
          ],
        },
      );
      final savedAccess = obj(await owner.request('GET', accessPath));
      expect(savedAccess['selectedBranches'], [group['branchId']]);
      expect(savedAccess['allSports'], isTrue);
      expect(savedAccess['selectedSports'], isEmpty);
      final transport = AdminEnrollmentAcceptanceTransport(
        AppConfig('Demo', base),
      );
      final admin = await login(adminPhone, transport: transport);
      expect(admin.session!.selected!.role, 'AcademyAdmin');
      final caps = await StaffCapabilities.load(admin);
      expect(
        caps.allows('people.manage', 'players', 'Add', [
          'workflow.players.create',
        ]),
        isTrue,
      );
      expect(caps.explicitAction('finance', 'Add'), isTrue);
      expect(
        rows(caps.scope['workflowActions']).singleWhere(
          (a) => a['key'] == 'workflow.subscriptions.exceptional-discount',
        )['allowed'],
        isFalse,
      );
      final exception = {
        'type': 2,
        'value': '100',
        'reason': 'اختبار منع الخصم لغير المخول',
        'replaceExistingDiscount': true,
      };
      final quoteBody = {
        'planId': plan!['id'],
        'branchId': group['branchId'],
        'guardianPhone': '01099900994',
        'discountRuleId': null,
      };
      await expectLater(
        admin.request(
          'POST',
          '/api/v1/people/registration-quote',
          body: {...quoteBody, 'exceptionalDiscount': exception},
        ),
        throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 403)),
      );
      await expectLater(
        admin.request(
          'POST',
          '/api/v1/people/registrations',
          body: {
            'playerArabicName': 'لاعب خصم ممنوع اصطناعي',
            'dateOfBirth': '2016-01-01',
            'gender': 1,
            'guardianName': 'ولي أمر اصطناعي',
            'guardianPhone': '01099900994',
            'relationshipType': 'ولي أمر',
            'branchId': group['branchId'],
            'sportId': group['sportId'],
            'groupId': group['id'],
            'planId': plan['id'],
            'paymentMethod': 'BankTransfer',
            'expectedAmount': '${num.parse('${plan['price']}') - 100}',
            'exceptionalDiscount': exception,
          },
          idempotencyKey: 'v45-admin-exception-denied-20261021',
        ),
        throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 403)),
      );
      final afterRejected = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      expect(before['netAmount'], isA<num>());
      expect(before['totalCount'], isA<num>());
      expect(afterRejected['netAmount'], before['netAmount']);
      expect(afterRejected['totalCount'], before['totalCount']);

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
        await t.scrollUntilVisible(f, 140, scrollable: scroll, maxScrolls: 100);
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
      if (resumePlayer.isEmpty) {
        await tap('اشتراك جديد');
        await waitFor(find.byType(NativeNewEnrollment));
        await waitFor(
          find.widgetWithText(TextFormField, 'اسم اللاعب بالعربية'),
        );
        await field('اسم اللاعب بالعربية', 'لاعب تسجيل إداري اصطناعي994');
        await tap('تاريخ الميلاد');
        await waitFor(find.byType(DatePickerDialog));
        await t.tap(
          find.text(
            MaterialLocalizations.of(t.element(find.byType(DatePickerDialog)))
                .okButtonLabel,
          ),
        );
        await t.pumpAndSettle();
        await field('اسم ولي الأمر', 'ولي أمر تسجيل إداري اصطناعي994');
        await field('رقم الهاتف', '01099900994');
        await choose(
          'الفرع',
          textOf(
            rows(
              structure['branches'],
            ).singleWhere((b) => b['id'] == group!['branchId'])['arabicName'],
          ),
        );
        await choose(
          'الرياضة',
          textOf(
            rows(structure['sports'])
                .singleWhere((s) => s['id'] == group!['sportId'])['arabicName'],
          ),
        );
        await choose('المجموعة', textOf(group['arabicName']));
        await choose(
          'الباقة',
          '${plan['arabicName']} · ${money(plan['price'], plan['currency'])}',
        );
        await reach(find.text('حساب المبلغ المستحق'));
        expect(find.text('إضافة خصم استثنائي'), findsNothing);
        await t.tap(find.text('حساب المبلغ المستحق'));
        await waitFor(find.text('المبلغ النهائي'));
        expect(
          storedAmountMatches(
            transport.quote!['originalAmount'],
            plan['price'],
          ),
          isTrue,
        );
        expect(num.parse('${transport.quote!['discountAmount']}'), 0);
        await choose('طريقة التحصيل', 'تحويل إلكتروني تم استلامه');
        await tap('أؤكد استلام المبلغ النهائي فعليًا');
        await binding.takeScreenshot(
          'phase-approved-admin-ordinary-enrollment-review-w375',
        );
        await tap('مراجعة الاشتراك');
        await waitFor(find.text('الخطوة 2 من 2 · المراجعة والتأكيد'));
        await reach(find.text('تأكيد وإنشاء الاشتراك'));
        await t.tap(find.text('تأكيد وإنشاء الاشتراك'));
        await t.pump(const Duration(milliseconds: 400));
        await waitFor(find.byType(AlertDialog), settle: false);
        await t.tap(find.text('تأكيد').last);
        await t.pump(const Duration(milliseconds: 400));
        await waitFor(find.text('تم التسجيل والتحصيل وإصدار الإيصال.'));
        await binding.takeScreenshot(
          'phase-approved-admin-ordinary-enrollment-saved-w375',
        );
      }
      final result =
          transport.registration?.result ??
          <String, dynamic>{
            'playerId': resumePlayer,
            'periodId': resumePeriod,
            'receiptId': resumeReceipt,
          };
      debugPrint('Synthetic ordinary admin enrollment: $result');
      final period = obj(
        await admin.request(
          'GET',
          '/api/v1/subscriptions/periods/${result['periodId']}',
        ),
      );
      final receipt = obj(
        await admin.request(
          'GET',
          '/api/v1/subscriptions/receipts/${result['receiptId']}',
        ),
      );
      expect(period['receiptId'], result['receiptId']);
      expect(receipt['playerNameSnapshot'], 'لاعب تسجيل إداري اصطناعي994');
      expect(storedAmountMatches(receipt['amount'], plan['price']), isTrue);
      expect(num.parse('${receipt['discountAmount']}'), 0);
      if (transport.registration case final saved?) {
        expect(saved.body['exceptionalDiscount'], isNull);
        final replay = obj(
          await admin.request(
            'POST',
            '/api/v1/people/registrations',
            body: saved.body,
            idempotencyKey: saved.key,
          ),
        );
        expect(replay['replay'], isTrue);
        for (final key in [
          'playerId',
          'periodId',
          'receiptId',
          'paymentId',
          'sportEnrollmentId',
        ]) {
          expect(replay[key], saved.result[key]);
        }
      }
      final after = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      expect(
        after['totalCount'],
        (before['totalCount'] as num) + (resumePlayer.isEmpty ? 1 : 0),
      );
      expect(
        after['netAmount'],
        (before['netAmount'] as num) +
            (resumePlayer.isEmpty ? num.parse('${plan['price']}') : 0),
      );
      debugPrint(
        'Synthetic financial before: ${before['totalCount']} / ${before['netAmount']} '
        'after: ${after['totalCount']} / ${after['netAmount']}',
      );
      await t.pumpWidget(const SizedBox.shrink());
      admin.dispose();
      owner.dispose();
    },
  );
}
