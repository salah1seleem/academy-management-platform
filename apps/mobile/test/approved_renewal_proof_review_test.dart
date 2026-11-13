import 'dart:convert';
import 'dart:typed_data';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/admin/native_renewal_proof_review.dart';
import 'package:academy_mobile/features/shared/private_media.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'approved_native_product_orders_test.dart' show reach;
import 'approved_native_renewal_test.dart' show payment;
import 'auth_controller_test.dart'
    show FakeApi, Handler, MemoryStore, credentials;
import 'support/infinite_list.dart';
import 'v2_role_workspaces_test.dart' show authenticate, app;

const assetId = '10000000-0000-0000-0000-000000000001';
const evidence = {
  'id': 'proof',
  'assetId': assetId,
  'url': '/api/v1/assets/$assetId',
  'status': 'Pending',
  'reviewReason': null,
};

class _ProofApi extends FakeApi implements AssetTransport {
  _ProofApi(super.handler, {this.firstImageFailure});

  final String? firstImageFailure;
  int assetReads = 0;
  static final _pixel = base64Decode(
    'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=',
  );

  @override
  Future<Uint8List> asset(String path, String token) async {
    if (++assetReads == 1) {
      if (firstImageFailure == 'unavailable') {
        throw const ApiFailure(503, 'تعذرت قراءة الصورة');
      }
      if (firstImageFailure == 'invalid') return Uint8List.fromList([1, 2, 3]);
    }
    return _pixel;
  }

  @override
  Future<Object?> upload(
    Uint8List bytes,
    String contentType,
    Map<String, String> fields,
    String token,
  ) => throw StateError('A staff review must not upload a proof');
}

Future<AuthController> _owner(
  Handler handler, {
  String? firstImageFailure,
}) async {
  final auth = AuthController(
    _ProofApi((method, path, body, token) {
      if (path.contains('/auth/')) {
        return Future.value({
          ...credentials(),
          'memberships': [
            {
              'id': 'guardian',
              'academyId': 'academy',
              'academyName': 'أكاديمية اختبار',
              'role': 'AcademyOwner',
            },
          ],
        });
      }
      return handler(method, path, body, token);
    }, firstImageFailure: firstImageFailure),
    MemoryStore(),
  )..restoring = false;
  await auth.verify('phone', 'challenge', 'code');
  return auth;
}

Future<void> _waitFor(WidgetTester tester, Finder finder) async {
  for (var attempt = 0; attempt < 80 && finder.evaluate().isEmpty; attempt++) {
    await tester.runAsync(
      () => Future<void>.delayed(const Duration(milliseconds: 10)),
    );
    await tester.pump(const Duration(milliseconds: 20));
  }
  expect(finder, findsOneWidget);
}

void main() {
  for (final assigned in [true, false]) {
    testWidgets('review explains the note limit, assigned group $assigned', (
      t,
    ) async {
      t.view.physicalSize = const Size(390, 844);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.reset);
      final writes = <Object?>[];
      final auth = await _owner((method, path, body, token) async {
        if (method == 'POST') writes.add(body);
        if (path.endsWith('/payments/payment')) {
          return {
            ...payment,
            'branchId': 'branch',
            'sportId': 'sport',
            'groupId': assigned ? 'group' : null,
          };
        }
        if (path.endsWith('/proof')) return evidence;
        if (path.endsWith('/structure/options')) {
          return {
            'groups': [
              {
                'id': 'group',
                'branchId': 'branch',
                'sportId': 'sport',
                'arabicName': 'مجموعة اصطناعية',
              },
            ],
          };
        }
        throw StateError('Unexpected $method $path');
      });
      await t.pumpWidget(
        app(NativeRenewalProofReview(auth: auth, id: 'payment')),
      );
      await _waitFor(t, find.text('تكبير'));
      final note = find.byType(TextFormField);
      await reach(t, note);
      final limit = List.filled(500, 'أ').join();
      await t.enterText(note, List.filled(501, 'أ').join());
      FocusManager.instance.primaryFocus?.unfocus();
      await t.pumpAndSettle();
      final rejection = find.text('رفض الإثبات');
      for (final action in [
        if (assigned) find.text('قبول وتفعيل التجديد'),
        rejection,
      ]) {
        await reach(t, action);
        await t.tap(action);
        await t.pumpAndSettle();
        expect(find.text('اكتب ملاحظة من 500 حرف أو أقل.'), findsOneWidget);
        expect(find.byType(AlertDialog), findsNothing);
        expect(writes, isEmpty);
      }
      await reach(t, note);
      await t.tap(note.hitTestable());
      await t.pump();
      t.view.viewInsets = const FakeViewPadding(bottom: 320);
      await t.pumpAndSettle();
      expect(note.hitTestable(), findsOneWidget);
      expect(t.getBottomRight(note).dy, lessThanOrEqualTo(524));
      await t.enterText(note, ' $limit ');
      t.view.resetViewInsets();
      FocusManager.instance.primaryFocus?.unfocus();
      await t.pumpAndSettle();
      await reach(t, rejection);
      await t.tap(rejection);
      await t.pumpAndSettle();
      expect(find.byType(AlertDialog), findsOneWidget);
      expect(find.text('اكتب ملاحظة من 500 حرف أو أقل.'), findsNothing);
      await t.tap(
        find.descendant(
          of: find.byType(AlertDialog),
          matching: find.text('رجوع'),
        ),
      );
      await t.pumpAndSettle();
      expect(writes, isEmpty);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    });
  }

  for (final failure in ['unavailable', 'invalid']) {
    testWidgets('a $failure proof image must recover before review', (t) async {
      t.view.physicalSize = const Size(390, 1150);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.reset);
      final calls = <String>[];
      final auth = await _owner((method, path, body, token) async {
        calls.add('$method $path');
        if (path.endsWith('/payments/payment')) {
          return {...payment, 'branchId': 'branch', 'sportId': 'sport'};
        }
        if (path.endsWith('/proof')) return evidence;
        if (path.endsWith('/structure/options')) {
          return {
            'groups': [
              {
                'id': 'group',
                'branchId': 'branch',
                'sportId': 'sport',
                'arabicName': 'مجموعة اصطناعية',
              },
            ],
          };
        }
        throw StateError('Unexpected $method $path');
      }, firstImageFailure: failure);
      await t.pumpWidget(
        app(NativeRenewalProofReview(auth: auth, id: 'payment')),
      );
      final retry = find.text('إعادة تحميل الصورة');
      final accept = find.text('قبول وتفعيل التجديد');
      final reject = find.text('رفض الإثبات');
      final zoom = find.text('تكبير');
      await _waitFor(t, find.text('تعذر فتح صورة الإثبات.'));
      await t.pump();
      expect(t.getSize(find.byType(PrivateImage)).height, 120);
      expect(accept, findsNothing);
      expect(reject, findsNothing);
      expect(zoom, findsNothing);
      await reach(t, retry);
      await t.tap(retry);
      await _waitFor(t, zoom);
      await reach(t, accept);
      expect(reject, findsOneWidget);
      expect(zoom, findsOneWidget);
      expect(calls, hasLength(3));
      expect(calls.every((call) => call.startsWith('GET ')), true);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    });
  }

  testWidgets(
    'saved pending proof survives reopening checkout without another upload or demo payment',
    (t) async {
      final calls = <String>[];
      final auth = await authenticate('Guardian', (m, p, b, token) async {
        calls.add('$m $p');
        if (p == '/api/v1/guardian/subscriptions/payments/payment') {
          return {
            ...payment,
            'amount': 400,
            'proofStatus': 'Pending',
            'provider': 'Manual',
          };
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
    'changed-while-confirming',
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
        const replacement = '10000000-0000-0000-0000-000000000002';
        final changed = mode == 'changed-while-confirming';
        final auth = await _owner((m, p, b, token) async {
          calls.add('$m $p');
          if (m == 'POST') {
            bodies.add(Map.from(b as Map));
            if (changed && bodies.length == 1) {
              throw const ApiFailure(
                409,
                'تغيّر إثبات التحويل. افتح الإثبات الحالي.',
              );
            }
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
              if (changed && bodies.isNotEmpty) ...{
                'assetId': replacement,
                'url': '/api/v1/assets/$replacement',
              },
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
        await _waitFor(t, find.text('تكبير'));
        await reach(t, action);
        await t.tap(action);
        await t.pumpAndSettle();
        expect(bodies, isEmpty);
        await t.tap(find.text('تأكيد المراجعة'));
        await t.pumpAndSettle();
        if (changed) {
          expect(find.text(savedMessage), findsNothing);
          expect(action, findsNothing);
          final reload = find.text('إعادة تحميل الطلب');
          await reach(t, reload);
          await t.tap(reload);
          await t.pumpAndSettle();
          await _waitFor(t, find.text('تكبير'));
          await reach(t, action);
          await t.tap(action);
          await t.pumpAndSettle();
          await t.tap(find.text('تأكيد المراجعة'));
          await t.pumpAndSettle();
        }
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
        expect(bodies.length, mode == 'lost-post' || changed ? 2 : 1);
        expect(bodies.first, {
          'accept': accept,
          'groupId': accept ? 'group' : null,
          'reason': null,
          'expectedProofAssetId': assetId,
        });
        if (changed) {
          expect(bodies.last, {
            ...bodies.first,
            'expectedProofAssetId': replacement,
          });
        } else if (bodies.length == 2) {
          expect(bodies[0], bodies[1]);
        }
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
  testWidgets('payment queue retries the next page in the same scope', (
    t,
  ) async {
    final requests = <Uri>[];
    final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
      expect(m, 'GET');
      final uri = Uri.parse(p);
      requests.add(uri);
      final page = int.parse(uri.queryParameters['page']!);
      if (page == 2 && requests.length == 2) {
        throw const ApiFailure(503, 'تعذر تحميل المزيد.');
      }
      return {
        'pending': 26,
        'renewals': 26,
        'total': 26,
        'page': page,
        'pageSize': 25,
        'items': [
          for (
            var index = page == 1 ? 1 : 26;
            index <= (page == 1 ? 25 : 26);
            index++
          )
            {
              'id': 'request-$index',
              'type': 'Renewal',
              'canOpen': false,
              'hasProof': false,
              'player': 'طلب اصطناعي $index',
              'amount': 400,
              'currency': 'EGP',
            },
        ],
      };
    });
    await t.pumpWidget(
      app(
        NativePaymentRequests(
          auth: auth,
          filters: const {
            'type': 'Renewal',
            'branchId': 'test-branch',
            'from': '2026-11-01',
            'to': '2026-11-13',
          },
        ),
      ),
    );
    await t.pumpAndSettle();
    expect(find.text('طلبات معلّقة (26)'), findsOneWidget);
    for (var attempt = 0; attempt < 30 && requests.length == 1; attempt++) {
      await scrollForNextPage(t);
    }
    await t.pumpAndSettle();
    final retry = find.text('تعذر تحميل المزيد · إعادة المحاولة');
    expect(retry, findsOneWidget);
    await t.tap(retry);
    await t.pumpAndSettle();
    expect(requests.map((uri) => uri.queryParameters['page']), ['1', '2', '2']);
    expect(requests.last.queryParameters, {
      ...requests.first.queryParameters,
      'page': '2',
    });
    await scrollForNextPage(t);
    expect(find.text('طلب اصطناعي 26'), findsOneWidget);
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

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
    'delegated queue keeps the payment row when detail access is withdrawn',
    (t) async {
      final calls = <String>[];
      final auth = await authenticate('AcademyAdmin', (m, p, b, token) async {
        calls.add('$m $p');
        if (p.startsWith('/api/v1/manage/payment-requests?')) {
          return {
            'pending': 1,
            'canReviewRenewalProof': false,
            'items': [
              {
                'id': 'payment',
                'reference': '100000000001',
                'type': 'Renewal',
                'hasProof': true,
                'canOpen': false,
                'player': 'لاعب اصطناعي',
                'amount': 400,
                'currency': 'EGP',
              },
            ],
          };
        }
        throw StateError('Unexpected $m $p');
      });
      await t.pumpWidget(app(NativePaymentRequests(auth: auth)));
      await t.pumpAndSettle();
      expect(find.textContaining('100000000001'), findsOneWidget);
      expect(find.text('400 ج.م'), findsOneWidget);
      expect(find.text('عرض إثبات التجديد'), findsNothing);
      expect(find.text('مراجعة إثبات التجديد'), findsNothing);
      expect(calls, hasLength(1));
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'delegated queue recovers a failed read into a real empty state',
    (t) async {
      var reads = 0;
      final auth = await authenticate('AcademyAdmin', (m, p, b, token) async {
        expect(m, 'GET');
        expect(p, startsWith('/api/v1/manage/payment-requests?'));
        if (++reads == 1) {
          throw const ApiFailure(503, 'تعذر تحميل طلبات الدفع.');
        }
        return {'pending': 0, 'items': []};
      });
      await t.pumpWidget(app(NativePaymentRequests(auth: auth)));
      await t.pumpAndSettle();
      expect(find.text('تعذر تحميل طلبات الدفع.'), findsOneWidget);
      await t.tap(find.text('إعادة المحاولة'));
      await t.pumpAndSettle();
      expect(reads, 2);
      expect(find.text('تعذر تحميل طلبات الدفع.'), findsNothing);
      expect(find.text('طلبات معلّقة (0)'), findsNothing);
      expect(
        find.text('لا توجد طلبات دفع معلقة ضمن النطاق المختار.'),
        findsOneWidget,
      );
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'delegated proof reader opens the exact evidence without fetching decision options',
    (t) async {
      final calls = <String>[];
      final auth = await authenticate('AcademyAdmin', (m, p, b, token) async {
        calls.add('$m $p');
        if (p.startsWith('/api/v1/manage/payment-requests?')) {
          return {
            'pending': 1,
            'canReviewRenewalProof': false,
            'items': [
              {
                'id': 'payment',
                'type': 'Renewal',
                'hasProof': true,
                'canOpen': true,
                'player': 'لاعب اصطناعي',
                'amount': 400,
                'currency': 'EGP',
              },
            ],
          };
        }
        if (p.endsWith('/my-permissions')) {
          return [
            'payment.read',
            'subscription.read',
            'subscription.renew.manage',
          ];
        }
        if (p.endsWith('/my-access')) {
          return {
            'useActionGrants': true,
            'actions': [
              {'module': 'payments', 'read': true},
              {'module': 'subscriptions', 'read': true},
            ],
          };
        }
        if (p.endsWith('/payments/payment')) {
          return {...payment, 'canReviewProof': false};
        }
        if (p.endsWith('/proof')) return evidence;
        throw StateError('Unexpected $m $p');
      });
      await t.pumpWidget(app(NativePaymentRequests(auth: auth)));
      await t.pumpAndSettle();
      await t.tap(find.text('عرض إثبات التجديد'));
      await t.pumpAndSettle();
      expect(find.byType(NativeRenewalProofReview), findsOneWidget);
      expect(find.text('إثبات التجديد'), findsOneWidget);
      expect(find.text('إثبات التحويل'), findsOneWidget);
      expect(find.text('قبول وتفعيل التجديد'), findsNothing);
      expect(find.text('رفض الإثبات'), findsNothing);
      expect(calls, contains('GET /api/v1/manage/payments/payment/proof'));
      expect(calls.any((path) => path.contains('/structure/options')), isFalse);
      expect(calls.every((path) => path.startsWith('GET ')), isTrue);
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
