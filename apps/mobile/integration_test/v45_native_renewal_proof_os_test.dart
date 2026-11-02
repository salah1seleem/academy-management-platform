import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/native_renewal_proof_review.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class SingleProofTransport extends HttpApiTransport {
  SingleProofTransport(super.config);
  Json? created, proofBody;
  final decisions = <Json>[];
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
    if (method == 'POST' && path == '/api/v1/guardian/subscriptions/renewals') {
      created = obj(result);
      debugPrint('Synthetic single renewal saved IDs: $created');
    }
    if (method == 'POST' &&
        path.startsWith('/api/v1/guardian/payments/') &&
        path.endsWith('/proof')) {
      proofBody = obj(body);
      debugPrint('Synthetic single proof asset: ${proofBody!['assetId']}');
    }
    if (method == 'POST' &&
        path.startsWith('/api/v1/manage/payments/') &&
        path.endsWith('/review')) {
      decisions.add(obj(body));
    }
    return result;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'actual guardian renewal OS proof to owner native review and verified receipt activates only once',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      final transport = SingleProofTransport(AppConfig('Demo', base));
      Future<AuthController> restore(String key, String role) async {
        final a = AuthController(transport, SecureRefreshStore(key));
        await a.restore();
        expect(a.session?.selected?.role, role);
        expect(
          a.session?.selected?.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return a;
      }

      final owner = await restore(
        'v45.5278.os-media-entry.01099900010',
        'AcademyOwner',
      );
      final parent = await restore(
        'v45.5278.current-attendance.guardian7',
        'Guardian',
      );
      const enrollment = '04588866-660a-4898-bd4a-2dbddf36efd4';
      final child = rows(
        await parent.request(
          'GET',
          '/api/v1/guardian/subscriptions/enrollments',
        ),
      ).singleWhere((e) => e['id'] == enrollment);
      final before = obj(
        await owner.request('GET', '/api/v1/reports/financial?allDates=true'),
      );
      final settings = obj(
        await owner.request('GET', '/api/v1/payment-settings'),
      );
      final pending = obj(
        await owner.request(
          'GET',
          '/api/v1/manage/payment-requests?allDates=true&status=Pending',
        ),
      );
      const resume = bool.fromEnvironment('RESUME_SINGLE_PROOF');
      if (!resume) {
        expect(
          rows(pending['items']).where((r) => r['player'] == child['player']),
          isEmpty,
          reason: 'Do not repeat an already created synthetic renewal. Resume its logged payment ID instead.',
        );
      }
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
        await t.ensureVisible(f.first);
        await t.pumpAndSettle();
      }

      Future<void> tap(Finder f) async {
        await reach(f);
        await t.tap(f.first);
        await t.pump(const Duration(milliseconds: 400));
      }

      Future<void> show(AuthController a) async {
        await t.pumpWidget(const SizedBox());
        await t.pumpAndSettle();
        final appearance = Appearance();
        for (final theme in ['football', 'general']) {
          await appearance.completeRoleIntro(a.session!.selected!.role, theme);
        }
        await t.pumpWidget(
          AcademyApp(auth: a, environment: 'Demo', appearance: appearance),
        );
        await wait(find.byType(NativeRoleTabs));
      }

      Future<void> capture(String name) async {
        await t.pumpAndSettle();
        await t.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 500)),
        );
        await binding.takeScreenshot(name);
      }

      try {
        await owner.request(
          'PUT',
          '/api/v1/payment-settings',
          body: {
            'instaPayAddress': 'synthetic-single-renewal@example.test',
            'walletNumber': null,
            'recipientName': 'تجديد اصطناعي — لا تحول أموالًا',
            'isActive': true,
          },
        );
        String id;
        if (!resume) {
          await show(parent);
          await wait(find.text('أبنائي'));
          await tap(find.text('تجديد الاشتراك'));
          await wait(find.byType(RenewalPicker));
          await wait(find.text('${child['player']} · ${child['sport']}'));
          await tap(find.text('${child['player']} · ${child['sport']}'));
          await wait(find.byType(PlanPicker));
          await wait(find.text('اختيار الباقة'));
          await tap(find.text('اختيار الباقة').first);
          await wait(find.byType(RenewalReview));
          await tap(find.text('متابعة إلى الدفع'));
          await wait(find.byType(CheckoutPage));
          expect(transport.created, isNotNull);

          id = textOf(transport.created!['paymentId']);
        } else {
          id = '9720e2d6-798a-45e1-b12d-c4fc974c2f97';
          await show(parent);
          final nav = t.state<NavigatorState>(find.byType(Navigator).first);
          nav.push(
            MaterialPageRoute<void>(
              builder: (_) =>
                  CheckoutPage(auth: parent, environment: 'Demo', id: id),
            ),
          );
          await t.pump(const Duration(milliseconds: 400));
          await wait(find.byType(CheckoutPage));
        }
        final source = obj(
          await owner.request('GET', '/api/v1/subscriptions/payments/$id'),
        );
        expect(source['provider'], 'Manual');
        expect(source['sportEnrollmentId'], enrollment);
        expect(source['status'], 'Pending');
        final afterCreate = obj(
          await owner.request('GET', '/api/v1/reports/financial?allDates=true'),
        );
        expect(afterCreate['totalCount'], before['totalCount']);
        expect(afterCreate['totalAmount'], before['totalAmount']);
        if (!resume) {
          await reach(find.text('رفع إيصال الدفع'));
          await t.tap(find.text('رفع إيصال الدفع'));
          await t.pump(const Duration(milliseconds: 500));
          debugPrint(
            'Choose only I-Academy-Photo-Test.png in the real iOS picker for synthetic single renewal proof.',
          );
          for (var i = 0; i < 1600 && transport.proofBody == null; i++) {
            await t.pump(const Duration(milliseconds: 150));
          }
          expect(transport.proofBody, isNotNull);
        }
        await wait(
          find.text(
            'وصل إثبات الدفع. تنتظر العملية مراجعة الإدارة؛ لا تحتاج إلى الدفع مرة أخرى.',
          ),
        );
        await capture('phase-approved-single-renewal-os-proof-w375');
        await show(owner);
        await wait(find.text('ملخص الأكاديمية'));
        await tap(find.byKey(const ValueKey('owner-request-Renewal')));
        await wait(find.byType(NativePaymentRequests));
        final card = find.ancestor(
          of: find.text(textOf(child['player'])),
          matching: find.byType(GCard),
        );
        await reach(card);
        await t.tap(
          find
              .descendant(of: card, matching: find.text('مراجعة إثبات التجديد'))
              .first,
        );
        await t.pump(const Duration(milliseconds: 400));
        await wait(find.byType(NativeRenewalProofReview));
        await wait(find.text(textOf(child['player'])));
        expect(
          t
              .widget<NativeRenewalProofReview>(
                find.byType(NativeRenewalProofReview),
              )
              .id,
          id,
        );
        await tap(find.text('قبول وتفعيل التجديد'));
        await wait(find.text('تأكيد المراجعة'));
        expect(transport.decisions, isEmpty);
        await t.tap(find.text('تأكيد المراجعة'));
        await t.pump(const Duration(milliseconds: 500));
        await wait(find.text('حُفظ قرار المراجعة وأُعيدت قراءة مصدره.'));
        expect(transport.decisions, hasLength(1));
        await capture(
          'phase-approved-owner-single-renewal-proof-confirmed-w375',
        );
        final saved = obj(
          await owner.request('GET', '/api/v1/subscriptions/payments/$id'),
        );
        expect(saved['status'], 'Confirmed');
        expect(saved['receiptId'], isNotNull);
        expect(saved['periodId'], isNotNull);
        debugPrint(
          'Synthetic single renewal confirmed: payment=$id receipt=${saved['receiptId']} period=${saved['periodId']} amount=${saved['finalAmount']}',
        );
        final replay = obj(
          await owner.request(
            'POST',
            '/api/v1/manage/payments/$id/review',
            body: transport.decisions.single,
          ),
        );
        expect(replay['replay'], true);
        final after = obj(
          await owner.request('GET', '/api/v1/reports/financial?allDates=true'),
        );
        expect(after['totalCount'], (before['totalCount'] as num) + 1);
        expect(
          after['totalAmount'],
          (before['totalAmount'] as num) + (saved['finalAmount'] as num),
        );
        expect(after['totalRefunds'], before['totalRefunds']);
        await tap(find.text('عرض الإيصال'));
        await wait(find.byType(ReceiptPage));
        await wait(find.text('إيصال التحصيل'));
        await capture(
          'phase-approved-owner-single-renewal-native-receipt-w375',
        );
        await show(parent);
        await wait(find.text('أبنائي'));
        // Reopen the exact saved checkout through its normal authenticated destination.
        await t.runAsync(() async {
          final own = obj(
            await parent.request(
              'GET',
              '/api/v1/guardian/subscriptions/payments/$id',
            ),
          );
          expect(own['receiptId'], saved['receiptId']);
          expect(own['status'], 'Confirmed');
        });
      } finally {
        await owner.request(
          'PUT',
          '/api/v1/payment-settings',
          body: {
            'instaPayAddress': settings['instaPayAddress'],
            'walletNumber': settings['walletNumber'],
            'recipientName': settings['recipientName'],
            'isActive': settings['isActive'],
          },
        );
        expect(
          await owner.request('GET', '/api/v1/payment-settings'),
          settings,
        );
        await t.pumpWidget(const SizedBox());
        await t.pumpAndSettle();
        owner.dispose();
        parent.dispose();
      }
    },
  );
}
