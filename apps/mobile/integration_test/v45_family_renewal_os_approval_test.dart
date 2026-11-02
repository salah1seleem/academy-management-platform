import 'dart:typed_data';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/family_renewal_quote.dart';
import 'package:academy_mobile/features/guardian/family_renewal_request.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:academy_mobile/features/shared/experience_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class ActualFamilyTransport extends HttpApiTransport {
  ActualFamilyTransport(super.config);
  Json? quote, created, proof, review, reviewBody;
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
    if (method == 'POST' &&
        path == '/api/v1/guardian/subscriptions/family/quote') {
      quote = obj(result);
      debugPrint(
        'Actual family quote total: ${quote!['totalAmount']} / ${quote!['currency']}',
      );
    }
    if (method == 'POST' &&
        path == '/api/v1/guardian/subscriptions/family/requests') {
      created = obj(result);
      debugPrint(
        'PERSISTED_FAMILY_REQUEST: ${created!['id']} — resume this exact ID after any interrupted acceptance run.',
      );
    }
    if (method == 'POST' &&
        path.contains('/family/requests/') &&
        path.endsWith('/proof')) {
      proof = obj(result);
      debugPrint('Actual family proof linked to request: ${proof!['id']}');
    }
    if (method == 'POST' &&
        path.contains('/manage/family-renewals/') &&
        path.endsWith('/review')) {
      review = obj(result);
      reviewBody = obj(body);
      debugPrint(
        'Actual family review: ${review!['id']} / ${review!['status']} / ${rows(review!['items']).map((l) => l['receiptId']).join(',')}',
      );
    }
    return result;
  }

  @override
  Future<Object?> upload(
    Uint8List bytes,
    String contentType,
    Map<String, String> fields,
    String token,
  ) async {
    final result = await super.upload(bytes, contentType, fields, token);
    debugPrint(
      'Actual OS synthetic proof asset: ${obj(result)['id']} / ${bytes.length} bytes / ${fields['purpose']}',
    );
    return result;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'imported synthetic family renews two children in one native request with one real OS proof; owner approves atomically and replays without duplicate periods or collections',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      Future<void> waitFor(Finder f) async {
        for (var i = 0; i < 300 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> reach(Finder f) async {
        final scroll = find.byType(Scrollable).first;
        t.state<ScrollableState>(scroll).position.jumpTo(0);
        await t.pumpAndSettle();
        await t.scrollUntilVisible(f, 180, scrollable: scroll, maxScrolls: 120);
        await t.ensureVisible(f.last);
        await t.pumpAndSettle();
      }

      Future<void> tap(String label) async {
        final f = find.text(label);
        await reach(f);
        await t.tap(f.last);
        await t.pumpAndSettle();
      }

      Future<AuthController> login(
        String phone,
        String store,
        ActualFamilyTransport transport,
      ) async {
        final a = AuthController(transport, SecureRefreshStore(store));
        await a.restore();
        if (a.session == null) {
          final c = await a.requestOtp(phone);
          await a.verify(phone, c, '246810');
        }
        expect(
          a.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return a;
      }

      Future<void> app(AuthController a, String key) async {
        final appearance = Appearance();
        for (final theme in ['football', 'general']) {
          await appearance.completeRoleIntro(a.session!.selected!.role, theme);
        }
        await t.pumpWidget(
          AcademyApp(
            key: ValueKey(key),
            auth: a,
            environment: 'Demo',
            appearance: appearance,
          ),
        );
        await waitFor(find.byType(NativeRoleTabs));
      }

      final familyTransport = ActualFamilyTransport(AppConfig('Demo', base)),
          ownerTransport = ActualFamilyTransport(AppConfig('Demo', base));
      final guardian = await login(
        '01200000001',
        'v45.5278.guardian-workspace.01200000001',
        familyTransport,
      );
      final owner = await login(
        '01099900010',
        'v45.5278.os-media-entry.01099900010',
        ownerTransport,
      );
      expect(guardian.session!.selected!.role, 'Guardian');
      expect(owner.session!.selected!.role, 'AcademyOwner');
      final transferBefore = obj(
        await owner.request('GET', '/api/v1/payment-settings'),
      );
      if (transferBefore['configured'] != true) {
        await owner.request(
          'PUT',
          '/api/v1/payment-settings',
          body: {
            'instaPayAddress': 'synthetic-family-v45@example.test',
            'walletNumber': null,
            'recipientName': 'إعداد اختبار اصطناعي — لا تحول أموالًا',
            'isActive': true,
          },
        );
        expect(
          obj(
            await guardian.request('GET', '/api/v1/payment-settings'),
          )['configured'],
          isTrue,
        );
        addTearDown(() async {
          await owner.request(
            'PUT',
            '/api/v1/payment-settings',
            body: {
              'instaPayAddress': transferBefore['instaPayAddress'],
              'walletNumber': transferBefore['walletNumber'],
              'recipientName': transferBefore['recipientName'],
              'isActive': false,
            },
          );
          expect(
            obj(
              await owner.request('GET', '/api/v1/payment-settings'),
            )['configured'],
            isFalse,
          );
          owner.dispose();
        });
        debugPrint(
          'Synthetic local transfer instructions enabled for this acceptance only; teardown restores disabled settings. No external transfer.',
        );
      } else {
        addTearDown(owner.dispose);
      }
      final before = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      final options = rows(
        obj(
          await guardian.request(
            'GET',
            '/api/v1/guardian/subscriptions/family/options',
          ),
        )['items'],
      );
      final sport = options.firstWhere(
        (e) =>
            options
                .where((other) => other['sportId'] == e['sportId'])
                .map((other) => other['playerId'] ?? other['player'])
                .toSet()
                .length >=
            2,
      )['sportId'];
      final chosen = options
          .where((e) => e['sportId'] == sport)
          .take(2)
          .toList();
      expect(chosen.length, 2);
      expect(chosen.map((e) => e['playerId'] ?? e['player']).toSet().length, 2);
      const resume = String.fromEnvironment('EXISTING_FAMILY_REQUEST');
      String id = resume;
      if (id.isEmpty) {
        expect(
          chosen.every((e) => e['hasOpenRequest'] != true),
          isTrue,
          reason: 'Never create another family request over a persisted pending intent.',
        );
        final plan = rows(chosen.first['plans']).firstWhere(
          (p) => chosen.every(
            (e) => rows(e['plans']).any(
              (other) =>
                  other['id'] == p['id'] && other['currency'] == p['currency'],
            ),
          ),
        );
        await app(guardian, 'family-create');
        await waitFor(find.text('أبنائي'));
        final renew = find.byKey(
          const ValueKey('guardian-action-تجديد الاشتراك'),
        );
        await reach(renew);
        await t.tap(renew);
        await t.pumpAndSettle();
        await waitFor(find.byType(RenewalPicker));
        await tap('مراجعة تجديد الأبناء معًا');
        await waitFor(find.byType(NativeFamilyRenewalQuote));
        final sportInput = find.widgetWithText(
          DropdownButtonFormField<String>,
          'الرياضة',
        );
        await waitFor(sportInput);
        if (t
                .widget<DropdownButtonFormField<String>>(sportInput)
                .initialValue !=
            sport) {
          await t.tap(sportInput);
          await t.pumpAndSettle();
          await t.tap(find.text(textOf(chosen.first['sport'])).last);
          await t.pumpAndSettle();
        }
        for (final line in chosen) {
          final checkbox = find.widgetWithText(
            CheckboxListTile,
            textOf(line['player']),
          );
          await reach(checkbox);
          await t.tap(checkbox);
          await t.pumpAndSettle();
        }
        final planInput = find.widgetWithText(
          DropdownButtonFormField<String>,
          'الباقة المشتركة',
        );
        await reach(planInput);
        await t.tap(planInput);
        await t.pumpAndSettle();
        await t.tap(
          find
              .text(
                '${plan['name']} · ${money(plan['amount'], plan['currency'])}',
              )
              .last,
        );
        await t.pumpAndSettle();
        await tap('مراجعة إجمالي 2 أبناء');
        for (var i = 0; i < 240 && familyTransport.quote == null; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        await t.pumpAndSettle();
        expect(familyTransport.quote, isNotNull);
        await reach(find.text('متابعة الدفع للأسرة'));
        await binding.takeScreenshot('phase-approved-family-native-quote-w375');
        await tap('متابعة الدفع للأسرة');
        await waitFor(find.byType(NativeFamilyRenewalRequest));
        expect(familyTransport.created, isNotNull);
        id = textOf(familyTransport.created!['id']);
      }
      expect(RegExp(r'^[a-fA-F0-9-]{36}$').hasMatch(id), isTrue);
      var request = obj(
        await guardian.request(
          'GET',
          '/api/v1/guardian/subscriptions/family/requests/$id',
        ),
      );
      expect(
        rows(request['items']).map((l) => l['sportEnrollmentId']).toSet(),
        chosen.map((e) => e['id']).toSet(),
      );
      final alreadyApproved = request['status'] == 'Approved';
      expect(['Pending', 'Approved'], contains(request['status']));
      if (!alreadyApproved && request['proofUrl'] == null) {
        if (resume.isNotEmpty) {
          await app(guardian, 'family-resume');
          await t.pumpAndSettle();
          final navigator = Navigator.of(
            t.element(find.byType(NativeRoleTabs)),
          );
          navigator.push(
            MaterialPageRoute<void>(
              builder: (_) =>
                  NativeFamilyRenewalRequest(auth: guardian, id: id),
            ),
          );
          await t.pumpAndSettle();
        }
        await waitFor(find.byType(NativeFamilyRenewalRequest));
        final proofButton = find.text('رفع إيصال الدفع');
        await reach(proofButton);
        await t.tap(proofButton);
        await t.pump(const Duration(milliseconds: 350));
        debugPrint(
          'NATIVE_FAMILY_PROOF_PICKER_WAIT: choose I-Academy-Photo-Test.png, synthetic local proof only; no bank transfer.',
        );
        for (var i = 0; i < 1800 && familyTransport.proof == null; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(
          familyTransport.proof,
          isNotNull,
          reason: 'Complete the actual iOS file picker; fixture injection is not acceptance.',
        );
        await waitFor(find.text('بانتظار المراجعة'));
        await reach(find.text('إثبات الأسرة'));
        await binding.takeScreenshot(
          'phase-approved-family-native-proof-saved-w375',
        );
        request = obj(
          await guardian.request(
            'GET',
            '/api/v1/guardian/subscriptions/family/requests/$id',
          ),
        );
        expect(request['proofUrl'], isNotNull);
      }
      if (!alreadyApproved) {
        await app(owner, 'owner-family-review');
        await waitFor(find.text('ملخص الأكاديمية'));
        final queue = find.descendant(
          of: find.widgetWithText(ExperienceSection, 'طلبات الدفع'),
          matching: find.byType(TextButton),
        );
        await reach(queue);
        await t.tap(queue);
        await t.pumpAndSettle();
        await waitFor(find.byType(NativePaymentRequests));
        final source = rows(
          obj(
            await owner.request(
              'GET',
              '/api/v1/manage/payment-requests?allDates=true&status=Pending',
            ),
          )['items'],
        ).singleWhere((r) => r['id'] == id);
        expect(source['type'], 'FamilyRenewal');
        final details = find.descendant(
          of: find.widgetWithText(GCard, textOf(source['player'])),
          matching: find.text('تفاصيل الأسرة والإثبات'),
        );
        await reach(details);
        await t.tap(details);
        await t.pumpAndSettle();
        await waitFor(find.byType(NativeFamilyRenewalRequest));
        expect(
          t
              .widget<NativeFamilyRenewalRequest>(
                find.byType(NativeFamilyRenewalRequest),
              )
              .id,
          id,
        );
        final note = find.widgetWithText(
          TextField,
          'ملاحظة أو سبب الرفض (اختياري)',
        );
        await reach(note);
        await t.enterText(note, 'اختبار قبول تجديد الأسرة على الجهاز V45');
        FocusManager.instance.primaryFocus?.unfocus();
        await t.pumpAndSettle();
        await tap('قبول وتجديد كل التسجيلات');
        expect(find.byType(AlertDialog), findsOneWidget);
        await binding.takeScreenshot(
          'phase-approved-family-native-owner-confirmation-w375',
        );
        await t.tap(find.text('تأكيد قبول الأسرة'));
        await t.pumpAndSettle();
        await waitFor(find.text('تم تجديد الأسرة'));
        expect(ownerTransport.review, isNotNull);
        await binding.takeScreenshot(
          'phase-approved-family-native-owner-approved-w375',
        );
      }
      request = obj(
        await owner.request('GET', '/api/v1/manage/family-renewals/$id'),
      );
      expect(request['status'], 'Approved');
      final lineItems = rows(request['items']);
      expect(lineItems.length, 2);
      expect(lineItems.map((l) => l['receiptId']).toSet().length, 2);
      final allPeriods = rows(
        await owner.request('GET', '/api/v1/subscriptions/periods'),
      );
      for (final line in lineItems) {
        final receipt = obj(
          await owner.request(
            'GET',
            '/api/v1/subscriptions/receipts/${line['receiptId']}',
          ),
        );
        expect(
          num.parse('${receipt['amount']}'),
          num.parse('${line['finalAmount']}'),
        );
        expect(receipt['isVoided'], isFalse);
        final candidates = allPeriods.where(
          (p) => p['sportEnrollmentId'] == line['sportEnrollmentId'],
        );
        final matches = <Json>[];
        for (final p in candidates) {
          final period = obj(
            await owner.request(
              'GET',
              '/api/v1/subscriptions/periods/${p['id']}',
            ),
          );
          if (period['receiptId'] == line['receiptId']) matches.add(period);
        }
        expect(matches.length, 1);
        expect(['Active', 'Scheduled'], contains(matches.single['status']));
        expect(matches.single['currencySnapshot'], line['currency']);
        expect(
          num.parse('${matches.single['priceSnapshot']}'),
          num.parse('${line['finalAmount']}'),
        );
        debugPrint(
          'Actual family receipt and period: ${line['receiptId']} / ${matches.single['id']} / ${matches.single['startDate']} / ${matches.single['endDate']}',
        );
      }
      final approvedMoney = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      expect(
        approvedMoney['totalCount'],
        (before['totalCount'] as num) + (alreadyApproved ? 0 : 2),
      );
      expect(
        (approvedMoney['netAmount'] as num) - (before['netAmount'] as num),
        alreadyApproved ? 0 : num.parse('${request['totalAmount']}'),
      );
      final replay =
          ownerTransport.reviewBody ??
          {
            'accept': true,
            'revision': request['revision'],
            'reason': request['decisionReason'],
            'placements': [
              for (final l in lineItems)
                {'lineId': l['id'], 'groupId': l['groupId']},
            ],
          };
      await owner.request(
        'POST',
        '/api/v1/manage/family-renewals/$id/review',
        body: replay,
      );
      final after = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      expect(after['totalCount'], approvedMoney['totalCount']);
      expect(after['netAmount'], approvedMoney['netAmount']);
      final guardianRead = obj(
        await guardian.request(
          'GET',
          '/api/v1/guardian/subscriptions/family/requests/$id',
        ),
      );
      expect(guardianRead['status'], 'Approved');
      expect(
        rows(guardianRead['items']).map((l) => l['receiptId']).toSet(),
        lineItems.map((l) => l['receiptId']).toSet(),
      );
      debugPrint(
        'Family acceptance finance verified: ${after['totalCount']} collections / ${after['netAmount']} net; duplicate replay changed nothing.',
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      guardian.dispose();
    },
  );
}
