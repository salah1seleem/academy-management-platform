import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/admin/native_renewal_proof_review.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'approved_native_product_orders_test.dart' show reach;
import 'approved_native_renewal_test.dart' show payment;
import 'v2_role_workspaces_test.dart' show authenticate, app;

const evidence = {
  'id': 'proof',
  'url': '/api/v1/assets/10000000-0000-0000-0000-000000000001',
  'status': 'Pending',
  'reviewReason': null,
};

void main() {
  testWidgets(
    'saved pending proof survives reopening checkout without another upload or demo payment',
    (t) async {
      final calls = <String>[];
      final auth = await authenticate('Guardian', (m, p, b, token) async {
        calls.add('$m $p');
        if (p == '/api/v1/guardian/subscriptions/payments/payment') {
          return {...payment, 'proofStatus': 'Pending', 'provider': 'Manual'};
        }
        throw StateError('Unexpected $m $p');
      });
      for (var open = 0; open < 2; open++) {
        await t.pumpWidget(
          app(CheckoutPage(auth: auth, environment: 'Demo', id: 'payment')),
        );
        await t.pumpAndSettle();
        await reach(
          t,
          find.text(
            'وصل إثبات الدفع. تنتظر العملية مراجعة الإدارة؛ لا تحتاج إلى الدفع مرة أخرى.',
          ),
        );
        expect(find.text('رفع إيصال الدفع'), findsNothing);
        expect(find.text('محاكاة دفع ناجح'), findsNothing);
        expect(calls.every((p) => p.startsWith('GET ')), true);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        await t.pumpAndSettle();
      }
      auth.dispose();
    },
  );

  for (final mode in [
    'accept',
    'reject',
    'lost-post',
    'lost-read',
    'wrong-proof',
    'wrong-source',
    'wrong-price',
    'wrong-receipt',
    'wrong-period',
    'wrong-reason',
    'denied',
  ]) {
    testWidgets(
      'renewal proof verifies its frozen source, decision and activation: $mode',
      (t) async {
        var written = false, failed = false, repaired = false;
        final calls = <String>[], bodies = <Map>[];
        final accept = mode != 'reject';
        final savedMessage = accept
            ? 'تم قبول التجديد وإصدار الإيصال.'
            : 'تم رفض إثبات التحويل.';
        final mismatch = mode.startsWith('wrong-');
        final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
          calls.add('$m $p');
          if (m == 'POST') {
            bodies.add(Map.from(b as Map));
            written = true;
            if (mode == 'lost-post' && !failed) {
              failed = true;
              throw const ApiFailure(0, 'انقطع الاتصال');
            }
            if (mode == 'denied') throw const ApiFailure(403, 'سُحب الإذن');
            return {
              'status': accept ? 'Accepted' : 'Rejected',
              'receiptId': accept ? 'receipt' : null,
            };
          }
          if (p.endsWith('/payments/payment')) {
            if (written && mode == 'lost-read' && !failed) {
              failed = true;
              throw const ApiFailure(503, 'تعذرت القراءة');
            }
            return {
              ...payment,
              'amount': 400,
              'branchId': 'branch',
              'sportId': 'sport',
              'groupId': 'group',
              'sportEnrollmentId':
                  written && mode == 'wrong-source' && !repaired
                  ? 'foreign'
                  : 'enrollment',
              'finalAmount': written && mode == 'wrong-price' && !repaired
                  ? 401
                  : 400,
              'status': written
                  ? (accept ? 'Confirmed' : 'Cancelled')
                  : 'Pending',
              'receiptId': written && accept ? 'receipt' : null,
              'periodId': written && accept ? 'period' : null,
            };
          }
          if (p.endsWith('/proof')) {
            return {
              ...evidence,
              'id': written && mode == 'wrong-proof' && !repaired
                  ? 'foreign'
                  : 'proof',
              'status': written
                  ? (accept ? 'Accepted' : 'Rejected')
                  : 'Pending',
              'reviewReason': written && mode == 'wrong-reason' && !repaired
                  ? 'ملاحظة أخرى'
                  : null,
            };
          }
          if (p.endsWith('/structure/options')) {
            return {
              'groups': [
                {
                  'id': 'group',
                  'branchId': 'branch',
                  'sportId': 'sport',
                  'arabicName': 'مجموعة اصطناعية',
                },
                {
                  'id': 'foreign',
                  'branchId': 'other',
                  'sportId': 'sport',
                  'arabicName': 'مجموعة أخرى',
                },
              ],
            };
          }
          if (p.endsWith('/receipts/receipt')) {
            return {
              'id': 'receipt',
              'isVoided': false,
              'paymentMethod': 'Online',
              'currency': 'EGP',
              'amount': mode == 'wrong-receipt' && !repaired ? 401 : 400,
              'originalAmount': 500,
              'discountAmount': 100,
              'finalAmount': 400,
            };
          }
          if (p.endsWith('/periods/period')) {
            return {
              'id': 'period',
              'receiptId': 'receipt',
              'sportEnrollmentId': 'enrollment',
              'subscriptionPlanId': mode == 'wrong-period' && !repaired
                  ? 'other'
                  : 'plan',
              'priceSnapshot': 400,
              'status': 'Scheduled',
            };
          }
          throw StateError('Unexpected $m $p');
        });
        await t.pumpWidget(
          app(NativeRenewalProofReview(auth: auth, id: 'payment')),
        );
        await t.pumpAndSettle();
        final action = find.text(
          accept ? 'قبول وتفعيل التجديد' : 'رفض الإثبات',
        );
        await reach(t, action);
        await t.tap(action);
        await t.pumpAndSettle();
        expect(bodies, isEmpty);
        await t.tap(find.text('تأكيد المراجعة'));
        await t.pumpAndSettle();
        if (mode == 'denied') {
          expect(find.text(savedMessage), findsNothing);
          expect(find.text('لاعب اصطناعي'), findsNothing);
        } else {
          if (mismatch || mode.startsWith('lost-')) {
            expect(find.text(savedMessage), findsNothing);
            repaired = true;
            final retry = find.text(
              mode == 'lost-post'
                  ? 'استكمال مراجعة الطلب نفسه'
                  : 'إعادة التحقق من الطلب نفسه',
            );
            await reach(t, retry);
            await t.tap(retry);
            await t.pumpAndSettle();
          }
          await reach(t, find.text(savedMessage));
          expect(find.text(savedMessage), findsOneWidget);
        }
        expect(bodies.length, mode == 'lost-post' ? 2 : 1);
        expect(bodies.first, {
          'accept': accept,
          'groupId': accept ? 'group' : null,
          'reason': null,
        });
        if (bodies.length == 2) expect(bodies[0], bodies[1]);
        if (!accept) {
          expect(
            calls.where(
              (p) => p.contains('/receipts/') || p.contains('/periods/'),
            ),
            isEmpty,
          );
        }
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  testWidgets(
    'payment queue opens the exact renewal with proof without collecting',
    (t) async {
      final calls = <String>[];
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        calls.add('$m $p');
        if (p.startsWith('/api/v1/manage/payment-requests?')) {
          return {
            'pending': 1,
            'items': [
              {
                'id': 'payment',
                'type': 'Renewal',
                'hasProof': true,
                'player': 'لاعب اصطناعي',
                'amount': 400,
                'currency': 'EGP',
              },
            ],
          };
        }
        if (p.endsWith('/payments/payment')) {
          return {
            ...payment,
            'amount': 400,
            'branchId': 'branch',
            'sportId': 'sport',
            'groupId': 'group',
          };
        }
        if (p.endsWith('/proof')) return evidence;
        if (p.endsWith('/structure/options')) return {'groups': []};
        throw StateError('Unexpected $m $p');
      });
      await t.pumpWidget(app(NativePaymentRequests(auth: auth)));
      await t.pumpAndSettle();
      await reach(t, find.text('مراجعة إثبات التجديد'));
      await t.tap(find.text('مراجعة إثبات التجديد'));
      await t.pumpAndSettle();
      expect(
        t
            .widget<NativeRenewalProofReview>(
              find.byType(NativeRenewalProofReview),
            )
            .id,
        'payment',
      );
      expect(calls.every((c) => c.startsWith('GET ')), true);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'administrator payment read alone does not read protected renewal details or review',
    (t) async {
      final calls = <String>[];
      final auth = await authenticate('AcademyAdmin', (m, p, b, token) async {
        calls.add('$m $p');
        if (p.endsWith('/my-permissions')) return ['payment.read'];
        if (p.endsWith('/my-access')) {
          return {
            'useActionGrants': true,
            'actions': [
              {'module': 'payments', 'read': true},
            ],
          };
        }
        throw StateError('Unexpected $m $p');
      });
      await t.pumpWidget(
        app(NativeRenewalProofReview(auth: auth, id: 'payment')),
      );
      await t.pumpAndSettle();
      expect(calls.any((p) => p.contains('/payments/payment')), false);
      expect(find.text('قبول وتفعيل التجديد'), findsNothing);
      expect(
        find.text('ليست لديك صلاحية عرض تفاصيل هذا التجديد.'),
        findsOneWidget,
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
