import 'support/infinite_list.dart';

import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/admin/native_product_orders.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

const proof = '/api/v1/assets/10000000-0000-0000-0000-000000000001';
const source = <String, dynamic>{
  'id': 'order',
  'productId': 'product',
  'playerId': 'child',
  'sportEnrollmentId': 'enrollment',
  'variantId': 'variant',
  'branchId': 'branch',
  'productName': 'زي اصطناعي',
  'player': 'لاعب خيالي',
  'sizeSnapshot': 'M',
  'shirtNumberSnapshot': 7,
  'quantity': 1,
  'unitPrice': 450,
  'amount': 450,
  'currency': 'EGP',
  'status': 'Pending',
  'reviewReason': null,
  'proofUrl': proof,
};

Future<void> reach(WidgetTester t, Finder f) async {
  final scroll = find.byType(Scrollable).first;
  t.state<ScrollableState>(scroll).position.jumpTo(0);
  await t.pumpAndSettle();
  await t.scrollUntilVisible(f, 150, scrollable: scroll, maxScrolls: 30);
  await t.ensureVisible(f.first);
  await t.pumpAndSettle();
}

Future<void> confirm(WidgetTester t, {bool accept = true}) async {
  final action = find.text(accept ? 'قبول وخصم المخزون' : 'رفض الطلب');
  await reach(t, action);
  await t.tap(action);
  await t.pumpAndSettle();
  await t.tap(find.text('تأكيد المراجعة'));
  await t.pumpAndSettle();
}

void main() {
  testWidgets(
    'owner proof notification opens its exact native product review rather than the guardian order',
    (t) async {
      final calls = <String>[];
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        calls.add('$m $p');
        if (p.startsWith('/api/v1/notifications')) {
          return {
            'unread': 1,
            'total': 1,
            'items': [
              {
                'id': 'notice',
                'message': 'وصل إثبات منتج اصطناعي',
                'isRead': false,
                'createdAtUtc': '2026-10-22',
                'targetKind': 'ProductReview',
                'targetId': 'order',
              },
            ],
          };
        }
        return {...source, 'proofUrl': null};
      });
      await t.pumpWidget(app(NotificationInbox(auth: auth)));
      await t.pumpAndSettle();
      await reach(t, find.text('مراجعة طلب المنتج'));
      await t.tap(find.text('مراجعة طلب المنتج'));
      await t.pumpAndSettle();
      expect(
        t
            .widget<NativeProductOrderReview>(
              find.byType(NativeProductOrderReview),
            )
            .id,
        'order',
      );
      expect(calls, contains('GET /api/v1/manage/product-sales/order'));
      expect(calls.every((p) => p.startsWith('GET ')), true);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  for (final mode in [
    'accept',
    'reject',
    'post-lost',
    'read-lost',
    'id',
    'amount',
    'quantity',
    'variant',
    'reason',
    'state',
  ]) {
    testWidgets(
      'native product review preserves the exact order and frozen decision: $mode',
      (t) async {
        final calls = <String>[], bodies = <Map>[];
        var written = false,
            failRead = mode == 'read-lost',
            failPost = mode == 'post-lost';
        var wrong = [
          'id',
          'amount',
          'quantity',
          'variant',
          'reason',
          'state',
        ].contains(mode);
        final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
          calls.add('$m $p');
          if (m == 'POST') {
            bodies.add(Map.from(b as Map));
            written = true;
            if (failPost) {
              failPost = false;
              throw const ApiFailure(0, 'انقطع الاتصال');
            }
            return {'status': mode == 'reject' ? 'Rejected' : 'Confirmed'};
          }
          if (written && failRead) {
            failRead = false;
            throw const ApiFailure(0, 'تعذرت القراءة');
          }
          return {
            ...source,
            if (written) 'status': mode == 'reject' ? 'Rejected' : 'Confirmed',
            if (written) 'reviewReason': 'مراجعة اصطناعية',
            if (written && wrong && mode == 'id') 'id': 'other-order',
            if (written && wrong && mode == 'amount') 'amount': 451,
            if (written && wrong && mode == 'quantity') 'quantity': 2,
            if (written && wrong && mode == 'variant')
              'variantId': 'other-variant',
            if (written && wrong && mode == 'reason')
              'reviewReason': 'قرار آخر',
            if (written && wrong && mode == 'state') 'status': 'Pending',
          };
        });
        await t.pumpWidget(
          app(NativeProductOrderReview(auth: auth, id: 'order')),
        );
        await t.pumpAndSettle();
        final note = find.widgetWithText(TextFormField, 'ملاحظة لولي الأمر');
        await reach(t, note);
        await t.enterText(note, 'مراجعة اصطناعية');
        await confirm(t, accept: mode != 'reject');
        final failure = !['accept', 'reject'].contains(mode);
        if (failure) {
          expect(
            find.text('حُفظت المراجعة وأُعيدت قراءة الطلب نفسه.'),
            findsNothing,
          );
          expect(
            find.widgetWithText(TextFormField, 'ملاحظة لولي الأمر'),
            findsNothing,
          );
          wrong = false;
          final retry = find.text(
            mode == 'post-lost'
                ? 'استكمال مراجعة الطلب نفسه'
                : 'إعادة التحقق من الطلب نفسه',
          );
          await reach(t, retry);
          await t.tap(retry);
          await t.pumpAndSettle();
        }
        await reach(t, find.text('حُفظت المراجعة وأُعيدت قراءة الطلب نفسه.'));
        expect(bodies.length, mode == 'post-lost' ? 2 : 1);
        expect(bodies.first, {
          'accept': mode != 'reject',
          'reason': 'مراجعة اصطناعية',
        });
        if (bodies.length == 2) expect(bodies.last, bodies.first);
        expect(
          calls.any(
            (p) =>
                p.contains('/collection') ||
                p.contains('/quote') ||
                p.contains('/catalog/'),
          ),
          false,
        );
        expect(find.text('قبول وخصم المخزون'), findsNothing);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  for (final grants in [(false, true), (true, false), (true, true)]) {
    final (catalog, finance) = grants;
    final allowed = catalog && finance;
    testWidgets(
      'administrator product acceptance requires catalog edit and finance add: $catalog/$finance',
      (t) async {
        final writes = <String>[];
        final auth = await authenticate('AcademyAdmin', (m, p, b, token) async {
          if (m != 'GET') writes.add(p);
          if (p.endsWith('my-permissions')) {
            return ['payment.read', 'content.catalog.manage'];
          }
          if (p.endsWith('my-access')) {
            return {
              'useActionGrants': true,
              'actions': [
                {'module': 'catalog', 'read': true, 'edit': catalog},
                {'module': 'finance', 'read': true, 'add': finance},
              ],
            };
          }
          return source;
        });
        await t.pumpWidget(
          app(NativeProductOrderReview(auth: auth, id: 'order')),
        );
        await t.pumpAndSettle();
        if (allowed) {
          await reach(t, find.text('قبول وخصم المخزون'));
        }
        expect(
          find.text('قبول وخصم المخزون'),
          allowed ? findsOneWidget : findsNothing,
        );
        expect(writes, isEmpty);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  testWidgets(
    'a missing proof never enables accepting or rejecting a product order',
    (t) async {
      final auth = await authenticate(
        'AcademyOwner',
        (m, p, b, token) async => {...source, 'proofUrl': null},
      );
      await t.pumpWidget(
        app(NativeProductOrderReview(auth: auth, id: 'order')),
      );
      await t.pumpAndSettle();
      await reach(t, find.text('بانتظار وصول إثبات الدفع من ولي الأمر.'));
      expect(find.text('قبول وخصم المخزون'), findsNothing);
      expect(find.text('رفض الطلب'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'logout during confirmation cannot submit or retain the product source',
    (t) async {
      final writes = <String>[];
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        if (m != 'GET') writes.add(p);
        return source;
      });
      await t.pumpWidget(
        app(NativeProductOrderReview(auth: auth, id: 'order')),
      );
      await t.pumpAndSettle();
      await reach(t, find.text('قبول وخصم المخزون'));
      await t.tap(find.text('قبول وخصم المخزون'));
      await t.pumpAndSettle();
      await auth.logout();
      await t.tap(find.text('تأكيد المراجعة'));
      await t.pumpAndSettle();
      expect(writes, isEmpty);
      expect(find.text('زي اصطناعي'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'known review permission denial removes previously visible order details',
    (t) async {
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        if (m == 'POST') throw const ApiFailure(403, 'لم تعد لديك الصلاحية');
        return source;
      });
      await t.pumpWidget(
        app(NativeProductOrderReview(auth: auth, id: 'order')),
      );
      await t.pumpAndSettle();
      await confirm(t);
      expect(find.text('زي اصطناعي'), findsNothing);
      expect(find.text('قبول وخصم المخزون'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'logout during the initial read cannot reveal another membership source',
    (t) async {
      final pending = Completer<Object?>();
      final auth = await authenticate(
        'AcademyOwner',
        (m, p, b, token) => pending.future,
      );
      await t.pumpWidget(
        app(NativeProductOrderReview(auth: auth, id: 'order')),
      );
      await t.pump();
      await auth.logout();
      pending.complete(source);
      await t.pumpAndSettle();
      expect(find.text('زي اصطناعي'), findsNothing);
      expect(find.text('قبول وخصم المخزون'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'product list pages and status keep branch and dates without financial commands',
    (t) async {
      final paths = <String>[];
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        paths.add('$m $p');
        final uri = Uri.parse(p);
        return {
          'totalCount': 2,
          'items': [
            {
              ...source,
              'id': uri.queryParameters['page'] == '2' ? 'last' : 'first',
              'hasProof': false,
            },
          ],
        };
      });
      await t.pumpWidget(
        app(
          NativeProductOrders(
            auth: auth,
            filters: const {
              'branchId': 'branch',
              'from': '2026-10-01',
              'to': '2026-10-22',
            },
          ),
        ),
      );
      await t.pumpAndSettle();
      await scrollForNextPage(t);
      await t.pumpAndSettle();
      expect(paths.last, contains('page=2'));
      for (final part in [
        'branchId=branch',
        'from=2026-10-01',
        'to=2026-10-22',
        'pageSize=25',
        'status=Pending',
      ]) {
        expect(paths.last, contains(part));
      }
      expect(paths.every((p) => p.startsWith('GET ')), true);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
