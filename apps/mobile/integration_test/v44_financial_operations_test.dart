import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/native_enrollment.dart';
import 'package:academy_mobile/features/admin/native_renewal.dart';
import 'package:academy_mobile/features/admin/native_operation_form.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

// Records only synthetic financial commands, never authentication responses.
class FinancialTransport extends HttpApiTransport {
  FinancialTransport(super.config);
  final registrations = <({Json body, String key, Json result})>[];
  final renewals = <Json>[];
  Json? lastQuote;
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
    if (method == 'POST' && path == '/api/v1/people/registration-quote') {
      lastQuote = obj(result);
    }
    if (method == 'POST' && path == '/api/v1/people/registrations') {
      registrations.add((
        body: Map.from(body as Map),
        key: idempotencyKey!,
        result: obj(result),
      ));
    }
    if (method == 'POST' && path == '/api/v1/subscriptions/admin-renewals') {
      renewals.add(obj(result));
    }
    return result;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  const resumePlayer = String.fromEnvironment('RESUME_REGISTRATION_PLAYER_ID');
  const resumePeriod = String.fromEnvironment('RESUME_REGISTRATION_PERIOD_ID');
  const resumePayment = String.fromEnvironment('RESUME_PAYMENT_ID');
  testWidgets(
    resumePlayer.isEmpty
        ? 'isolated native fixed and percentage registrations and exact renewal receipts persist'
        : 'isolated native renewal resumes a previously verified synthetic registration without re-registering',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final logical = t.view.physicalSize / t.view.devicePixelRatio;
      debugPrint('Financial acceptance native viewport: $logical');
      const expectedWidth = String.fromEnvironment('EXPECTED_LOGICAL_WIDTH');
      if (expectedWidth.isNotEmpty) {
        expect(logical.width, double.parse(expectedWidth));
      }
      final transport = FinancialTransport(AppConfig('Demo', base));
      final auth = AuthController(
        transport,
        SecureRefreshStore('v44.5278.financial.owner'),
      );
      await auth.restore();
      if (auth.session == null) {
        final challenge = await auth.requestOtp('01099900010');
        await auth.verify('01099900010', challenge, '246810');
      }
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      expect(auth.session!.selected!.role, 'AcademyOwner');
      Future<void> capture(String name) async {
        await t.pumpAndSettle();
        await binding.takeScreenshot('$name-w${logical.width.toInt()}');
      }

      Future<void> waitFor(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        if (f.evaluate().isEmpty) {
          await binding.takeScreenshot('phase-approved-financial-diagnostic');
        }
        expect(f, findsWidgets);
        await t.pump(const Duration(milliseconds: 350));
      }

      Future<void> locate(Finder f) async {
        final scroll = find.byType(Scrollable).first;
        t.state<ScrollableState>(scroll).position.jumpTo(0);
        await t.pump(const Duration(milliseconds: 200));
        await t.scrollUntilVisible(f, 150, scrollable: scroll);
        await t.ensureVisible(f.last);
        await t.pump(const Duration(milliseconds: 300));
      }

      Future<void> tap(String text) async {
        final f = find.text(text);
        await locate(f);
        await t.tap(f.last);
        await t.pump(const Duration(milliseconds: 400));
      }

      Future<void> field(String label, String value) async {
        final f = find.widgetWithText(TextFormField, label);
        await locate(f);
        await t.enterText(f, value);
        await t.pump(const Duration(milliseconds: 250));
      }

      Future<void> choose(String label, String option) async {
        final f = find.widgetWithText(DropdownButtonFormField<String>, label);
        await locate(f);
        await t.tap(f);
        await t.pumpAndSettle();
        final item = find.text(option);
        if (item.evaluate().isEmpty) {
          await t.scrollUntilVisible(
            item,
            90,
            scrollable: find.byType(Scrollable).last,
          );
        }
        await t.ensureVisible(item);
        await t.tap(item);
        await t.pumpAndSettle();
      }

      Future<void> back() async {
        await t.tap(find.byTooltip('رجوع').first);
        await t.pumpAndSettle();
      }

      Future<void> confirm() async {
        await waitFor(find.byType(AlertDialog));
        await t.tap(find.text('تأكيد').last);
        await t.pump(const Duration(milliseconds: 450));
      }

      final before = obj(
        await auth.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&page=1&pageSize=1',
        ),
      );
      final structure = obj(
        await auth.request('GET', '/api/v1/manage/structure/options'),
      );
      final plans = rows(
        await auth.request('GET', '/api/v1/subscriptions/plans'),
      );
      Json? group, plan;
      for (final g in rows(structure['groups'])) {
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
        final details = obj(
          await auth.request('GET', '/api/v1/manage/groups/${g['id']}/details'),
        );
        final capacity = obj(details['group'])['capacity'] as num?;
        if (capacity != null &&
            capacity - rows(details['players']).length < 2) {
          continue;
        }
        group = g;
        plan = eligible;
        break;
      }
      expect(
        group,
        isNotNull,
        reason: 'Two available seats and an eligible existing plan are required; do not override capacity.',
      );
      expect(plan, isNotNull);
      final branchName = textOf(
        rows(structure['branches'])
            .singleWhere((b) => b['id'] == group!['branchId'])['arabicName'],
      );
      final sportName = textOf(
        rows(structure['sports'])
            .singleWhere((s) => s['id'] == group!['sportId'])['arabicName'],
      );
      final appearance = Appearance();
      for (final theme in ['football', 'general']) {
        await appearance.completeRoleIntro('AcademyOwner', theme);
      }
      await t.pumpWidget(
        AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
      );
      await waitFor(find.text('ملخص الأكاديمية'));
      await locate(find.text('التحصيل الأسبوعي'));
      await capture('phase-approved-home-weekly-native');
      await locate(find.text('نمو الاشتراكات'));
      await capture('phase-approved-home-subscription-trend-native');
      await t.tap(
        find.descendant(
          of: find.byType(NavigationBar),
          matching: find.text('العمليات'),
        ),
      );
      await waitFor(find.text('اشتراك جديد'));
      final stamp = DateTime.now().millisecondsSinceEpoch.toString();
      var total = 0.0;
      for (var i = 0; i < (resumePlayer.isEmpty ? 2 : 0); i++) {
        await tap('اشتراك جديد');
        await waitFor(find.byType(NativeNewEnrollment));
        await waitFor(
          find.widgetWithText(TextFormField, 'اسم اللاعب بالعربية'),
        );
        await field(
          'اسم اللاعب بالعربية',
          'لاعب قبول مالي اصطناعي $stamp ${i + 1}',
        );
        await tap('تاريخ الميلاد');
        await waitFor(find.byType(DatePickerDialog));
        // The actual form defaults to 2016-01-01, checked against the chosen group above.
        await t.tap(
          find.text(
            MaterialLocalizations.of(t.element(find.byType(DatePickerDialog)))
                .okButtonLabel,
          ),
        );
        await t.pumpAndSettle();
        await field('اسم ولي الأمر', 'ولي أمر قبول مالي اصطناعي ${i + 1}');
        await field('رقم الهاتف', i == 0 ? '01099900980' : '01099900981');
        await choose('الفرع', branchName);
        await choose('الرياضة', sportName);
        await choose('المجموعة', textOf(group!['arabicName']));
        await choose(
          'الباقة',
          '${plan!['arabicName']} · ${money(plan['price'], plan['currency'])}',
        );
        await tap('إضافة خصم استثنائي');
        if (i == 1) await choose('نوع الخصم', 'نسبة مئوية');
        await field('قيمة الخصم', i == 0 ? '100' : '15');
        await field('سبب الخصم', 'قبول اصطناعي للتصميم النهائي $stamp');
        // Review explicitly replaces any previously applicable family offer on a repeated demo run.
        await tap('استبدال أي خصم قائم بعد المراجعة');
        await tap('حساب المبلغ المستحق');
        await waitFor(find.text('المبلغ النهائي'));
        final quote = Map<String, dynamic>.from(transport.lastQuote!);
        expect(
          storedAmountMatches(quote['originalAmount'], plan['price']),
          isTrue,
        );
        expect(
          num.parse('${quote['discountAmount']}'),
          i == 0 ? 100 : num.parse('${plan['price']}') * .15,
        );
        await choose('طريقة التحصيل', 'تحويل إلكتروني تم استلامه');
        await tap('أؤكد استلام المبلغ النهائي فعليًا');
        await capture(
          'phase-approved-registration-${i == 0 ? 'fixed' : 'percent'}-review',
        );
        await tap('مراجعة الاشتراك');
        await waitFor(find.text('الخطوة 2 من 2 · المراجعة والتأكيد'));
        await capture(
          'phase-approved-registration-${i == 0 ? 'fixed' : 'percent'}-confirmation',
        );
        await tap('تأكيد وإنشاء الاشتراك');
        await confirm();
        await waitFor(find.text('تم التسجيل والتحصيل وإصدار الإيصال.'));
        expect(
          transport.registrations
              .map((r) => r.result['playerId'])
              .toSet()
              .length,
          i + 1,
        );
        final saved = transport.registrations.last;
        final period = obj(
          await auth.request(
            'GET',
            '/api/v1/subscriptions/periods/${saved.result['periodId']}',
          ),
        );
        final receipt = obj(
          await auth.request(
            'GET',
            '/api/v1/subscriptions/receipts/${saved.result['receiptId']}',
          ),
        );
        expect(period['sportEnrollmentId'], saved.result['sportEnrollmentId']);
        expect(period['receiptId'], receipt['id']);
        expect(
          storedAmountMatches(receipt['amount'], quote['finalAmount']),
          isTrue,
        );
        final replay = obj(
          await auth.request(
            'POST',
            '/api/v1/people/registrations',
            body: saved.body,
            idempotencyKey: saved.key,
          ),
        );
        for (final key in [
          'playerId',
          'sportEnrollmentId',
          'periodId',
          'paymentId',
          'receiptId',
        ]) {
          expect(replay[key], saved.result[key]);
        }
        expect(replay['replay'], isTrue);
        total += num.parse('${quote['finalAmount']}').toDouble();
        await capture(
          'phase-approved-registration-${i == 0 ? 'fixed' : 'percent'}-saved',
        );
        await tap('عرض الإيصال');
        await waitFor(find.textContaining('إيصال'));
        await t.pump(const Duration(seconds: 1));
        await capture(
          'phase-approved-registration-${i == 0 ? 'fixed' : 'percent'}-receipt',
        );
        await back();
        await back();
      }
      // Continue the exact saved payment after an interrupted local acceptance run.
      final first = resumePlayer.isEmpty
          ? transport.registrations.first.result
          : <String, dynamic>{
              'playerId': resumePlayer,
              'periodId': resumePeriod,
              'sportEnrollmentId': rows(
                obj(
                  await auth.request(
                    'GET',
                    '/api/v1/people/players/$resumePlayer',
                  ),
                )['enrollments'],
              ).single['id'],
            };
      final player = obj(
        await auth.request(
          'GET',
          '/api/v1/people/players/${first['playerId']}',
        ),
      );
      late Json request;
      if (resumePayment.isNotEmpty) {
        await t.tap(
          find.descendant(
            of: find.byType(NavigationBar),
            matching: find.text('الرئيسية'),
          ),
        );
        await t.pumpAndSettle();
        final queue = find.byKey(const ValueKey('owner-request-Renewal'));
        await locate(queue);
        await t.tap(queue);
        await t.pump(const Duration(milliseconds: 400));
        await waitFor(find.byType(NativePaymentRequests));
        final name = find.text(textOf(player['arabicName']));
        await locate(name);
        final card = find
            .ancestor(of: name, matching: find.byType(GCard))
            .first;
        final resume = find.descendant(
          of: card,
          matching: find.text('استكمال طلب التجديد'),
        );
        await t.ensureVisible(resume);
        await t.tap(resume);
        await waitFor(find.byType(NativeRenewalPayment));
        request = {'paymentId': resumePayment};
      } else {
        await tap('تجديد اشتراك');
        await waitFor(find.byType(NativeAdminRenewal));
        await field(
          'اسم اللاعب أو الكود أو هاتف ولي الأمر',
          textOf(player['playerCode']),
        );
        await tap('بحث');
        await waitFor(find.textContaining(textOf(player['playerCode'])));
        final row = find.textContaining(
          '${player['arabicName']} · ${player['playerCode']}',
        );
        await locate(row);
        await t.tap(row.last);
        await waitFor(
          find.widgetWithText(DropdownButtonFormField<String>, 'الباقة'),
        );
        await choose(
          'الباقة',
          '${plan!['arabicName']} · ${money(plan['price'], plan['currency'])}',
        );
        await tap('إنشاء طلب التجديد والدفع');
        await waitFor(find.byType(NativeRenewalPayment));
        request = transport.renewals.single;
      }
      final pending = obj(
        await auth.request(
          'GET',
          '/api/v1/subscriptions/payments/${request['paymentId']}',
        ),
      );
      expect(pending['sportEnrollmentId'], first['sportEnrollmentId']);
      expect(pending['periodId'], isNull);
      await choose('طريقة التحصيل', 'تحويل إلكتروني تم استلامه');
      await tap('تأكيد التحصيل وإصدار الإيصال');
      await confirm();
      await waitFor(find.text('تم التحصيل'));
      final confirmed = obj(
        await auth.request(
          'GET',
          '/api/v1/subscriptions/payments/${request['paymentId']}',
        ),
      );
      expect(confirmed['status'], 'Confirmed');
      expect(confirmed['periodId'], isNot(first['periodId']));
      final renewed = obj(
        await auth.request(
          'GET',
          '/api/v1/subscriptions/periods/${confirmed['periodId']}',
        ),
      );
      expect(renewed['sportEnrollmentId'], first['sportEnrollmentId']);
      expect(renewed['receiptId'], confirmed['receiptId']);
      total += num.parse('${confirmed['finalAmount']}').toDouble();
      await capture('phase-approved-renewal-confirmed-native');
      final after = obj(
        await auth.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&page=1&pageSize=1',
        ),
      );
      expect(
        (after['totalCount'] as num) - (before['totalCount'] as num),
        resumePlayer.isEmpty ? 3 : 1,
      );
      expect((after['netAmount'] as num) - (before['netAmount'] as num), total);
      expect(t.takeException(), isNull);
      debugPrint(
        'Synthetic financial acceptance: ${resumePlayer.isEmpty ? '2 registrations + 1 renewal' : 'resumed existing registration + 1 renewal'}, net change $total.',
      );
      await t.pumpWidget(const SizedBox());
      await auth.logout();
      auth.dispose();
    },
  );
}
