import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/guardian/sport_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

const product = {
  'id': 'product',
  'sportId': 'football',
  'usesVariants': true,
  'arabicName': 'زي التدريب',
  'displayPrice': 100,
  'discountPercentage': 10,
  'currency': 'EGP',
  'stockQuantity': 3,
};
Map<String, dynamic> stock({int assigned = 7}) => {
  'usesVariants': true,
  'numberedUniform': true,
  'unitPrice': 90,
  'assignedShirtNumber': assigned,
  'variants': [
    {'id': 'M7', 'size': 'M', 'shirtNumber': 7, 'quantity': 1},
    {'id': 'M8', 'size': 'M', 'shirtNumber': 8, 'quantity': 2},
  ],
};
const children = [
  {
    'id': 'enrollment',
    'playerId': 'child',
    'player': 'طفل تجريبي',
    'branchId': 'branch',
    'branch': 'فرع تجريبي',
    'sportId': 'football',
  },
];
Future<void> tap(WidgetTester t, String label) async {
  final f = find.text(label).last;
  await t.pumpAndSettle();
  await t.scrollUntilVisible(f, 150, scrollable: find.byType(Scrollable).first);
  await Scrollable.ensureVisible(t.element(f), alignment: .5);
  await t.pumpAndSettle();
  await t.tap(f);
  await t.pumpAndSettle();
}

Future<void> choose(WidgetTester t, String label, String value) async {
  final f = find.widgetWithText(DropdownButtonFormField<String>, label);
  await t.ensureVisible(f);
  await t.tap(f);
  await t.pumpAndSettle();
  await t.tap(find.text(value).last);
  await t.pumpAndSettle();
}

void main() {
  testWidgets(
    'Assigned number is preselected only when available and requires explicit number confirmation; order carries enrollment/size variant',
    (t) async {
      Map? body;
      final auth = await authenticate('Guardian', (m, p, b, token) async {
        if (p.endsWith('/enrollments')) return children;
        if (p.contains('/variants')) return stock();
        if (m == 'POST' && p.endsWith('/product-sales')) {
          body = b as Map;
          return {'id': 'sale', 'amount': 90, 'currency': 'EGP'};
        }
        return {};
      });
      await t.pumpWidget(app(ProductPurchase(auth: auth, product: product)));
      await t.pumpAndSettle();
      await choose(t, 'المقاس', 'M');
      expect(
        t
            .widget<DropdownButtonFormField<String>>(
              find.widgetWithText(
                DropdownButtonFormField<String>,
                'رقم القميص المتاح',
              ),
            )
            .initialValue,
        'M7',
      );
      await t.scrollUntilVisible(
        find.text('متابعة إلى الدفع'),
        150,
        scrollable: find.byType(Scrollable).first,
      );
      expect(
        t
            .widget<FilledButton>(
              find.widgetWithText(FilledButton, 'متابعة إلى الدفع'),
            )
            .onPressed,
        isNull,
      );
      await tap(t, 'أؤكد رقم القميص المختار: 7');
      await tap(t, 'متابعة إلى الدفع');
      expect(body, containsPair('variantId', 'M7'));
      expect(body, containsPair('sportEnrollmentId', 'enrollment'));
      expect(body, containsPair('playerId', 'child'));
      expect(body, containsPair('branchId', 'branch'));
      expect(body, containsPair('quantity', 1));
      expect(body, containsPair('expectedAmount', '90.00'));
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Unavailable assigned number never silently substitutes another number',
    (t) async {
      final auth = await authenticate(
        'Guardian',
        (m, p, b, token) async => p.endsWith('/enrollments')
            ? children
            : p.contains('/variants')
            ? stock(assigned: 9)
            : {},
      );
      await t.pumpWidget(app(ProductPurchase(auth: auth, product: product)));
      await t.pumpAndSettle();
      await choose(t, 'المقاس', 'M');
      expect(
        t
            .widget<DropdownButtonFormField<String>>(
              find.widgetWithText(
                DropdownButtonFormField<String>,
                'رقم القميص المتاح',
              ),
            )
            .initialValue,
        isNull,
      );
      expect(find.textContaining('رقم الطفل غير متاح'), findsOneWidget);
      await choose(t, 'رقم القميص المتاح', '8 · المتاح 2');
      expect(
        t.widget<CheckboxListTile>(find.byType(CheckboxListTile)).value,
        isFalse,
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Uncertain creation locks selection and repeats the unchanged order',
    (t) async {
      final apiBodies = <Map>[];
      final auth = await authenticate('Guardian', (m, p, b, token) async {
        if (p.endsWith('/enrollments')) return children;
        if (p.contains('/variants')) return stock();
        if (m == 'POST') {
          apiBodies.add(Map.from(b as Map));
          if (apiBodies.length == 1) throw const ApiFailure(0, 'تعذر الاتصال');
          return {'id': 'sale', 'amount': 90, 'currency': 'EGP'};
        }
        return {};
      });
      await t.pumpWidget(app(ProductPurchase(auth: auth, product: product)));
      await t.pumpAndSettle();
      await choose(t, 'المقاس', 'M');
      await tap(t, 'أؤكد رقم القميص المختار: 7');
      await tap(t, 'متابعة إلى الدفع');
      await t.scrollUntilVisible(
        find.text('إعادة تأكيد الطلب نفسه'),
        150,
        scrollable: find.byType(Scrollable).first,
      );
      expect(
        t
            .widget<DropdownButtonFormField<String>>(
              find.widgetWithText(
                DropdownButtonFormField<String>,
                'الطفل والفرع',
              ),
            )
            .onChanged,
        isNull,
      );
      await tap(t, 'إعادة تأكيد الطلب نفسه');
      expect(apiBodies, hasLength(2));
      expect(apiBodies[0], apiBodies[1]);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Logout during inventory lookup hides the previous child and stock result',
    (t) async {
      final pending = Completer<Object?>();
      final auth = await authenticate(
        'Guardian',
        (m, p, b, token) async => p.endsWith('/enrollments')
            ? children
            : p.contains('/variants')
            ? pending.future
            : {},
      );
      await t.pumpWidget(app(ProductPurchase(auth: auth, product: product)));
      await t.pump();
      await auth.logout();
      pending.complete(stock());
      await t.pumpAndSettle();
      expect(find.text('زي التدريب'), findsNothing);
      expect(find.text('طفل تجريبي'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Plain product checks current stock and exact price before ordering',
    (t) async {
      var reads = 0;
      Map? posted;
      final auth = await authenticate('Guardian', (
        method,
        path,
        body,
        _,
      ) async {
        if (path.endsWith('/enrollments')) return children;
        if (path.contains('/variants')) {
          expect(Uri.parse(path).queryParameters['enrollmentId'], 'enrollment');
          return {
            'usesVariants': false,
            'stockQuantity': ++reads == 1 ? 0 : 2,
            'unitPrice': 89.99,
            'currency': 'EGP',
            'variants': [],
          };
        }
        if (method == 'POST' && path.endsWith('/product-sales')) {
          posted = body as Map;
          return {'id': 'sale', 'amount': 179.98, 'currency': 'EGP'};
        }
        return {};
      });
      await t.pumpWidget(
        app(
          ProductPurchase(
            auth: auth,
            product: {...product, 'usesVariants': false},
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('المنتج غير متاح حاليًا.'), findsOneWidget);
      expect(find.text('متابعة إلى الدفع'), findsNothing);
      await tap(t, 'تحديث السعر والتوفّر');
      await t.tap(find.byTooltip('زيادة الكمية'));
      await t.pumpAndSettle();
      await tap(t, 'متابعة إلى الدفع');
      expect(reads, 2);
      expect(posted, containsPair('quantity', 2));
      expect(posted, containsPair('expectedAmount', '179.98'));
      expect(posted, isNot(contains('variantId')));
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'A price conflict requires fresh data and renewed shirt confirmation',
    (t) async {
      final writes = <Map>[];
      var reads = 0;
      final auth = await authenticate('Guardian', (
        method,
        path,
        body,
        _,
      ) async {
        if (path.endsWith('/enrollments')) return children;
        if (path.contains('/variants')) {
          return {...stock(), 'unitPrice': ++reads == 1 ? 90 : 95.25};
        }
        if (method == 'POST' && path.endsWith('/product-sales')) {
          writes.add(body as Map);
          if (writes.length == 1) {
            throw const ApiFailure(409, 'تغير سعر المنتج.');
          }
          return {'id': 'sale', 'amount': 95.25, 'currency': 'EGP'};
        }
        return {};
      });
      await t.pumpWidget(app(ProductPurchase(auth: auth, product: product)));
      await t.pumpAndSettle();
      await choose(t, 'المقاس', 'M');
      await tap(t, 'أؤكد رقم القميص المختار: 7');
      await tap(t, 'متابعة إلى الدفع');
      expect(
        t
            .widget<FilledButton>(
              find.widgetWithText(FilledButton, 'متابعة إلى الدفع'),
            )
            .onPressed,
        isNull,
      );
      await tap(t, 'تحديث السعر والتوفّر');
      expect(
        t.widget<CheckboxListTile>(find.byType(CheckboxListTile)).value,
        isFalse,
      );
      expect(writes, hasLength(1));
      await tap(t, 'أؤكد رقم القميص المختار: 7');
      await tap(t, 'متابعة إلى الدفع');
      expect(writes.map((value) => value['expectedAmount']), [
        '90.00',
        '95.25',
      ]);
      expect(reads, 2);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'a submitted product proof shows review status and explicit replacement at 320',
    (t) async {
      t.view.physicalSize = const Size(320, 844);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.reset);
      final calls = <String>[];
      var status = 'Pending';
      final auth = await authenticate('Guardian', (m, p, b, token) async {
        calls.add('$m $p');
        return {
          'id': 'sale',
          'productName': 'زي اختبار إثبات',
          'sizeSnapshot': 'M',
          'shirtNumberSnapshot': 7,
          'quantity': 1,
          'currency': 'EGP',
          'amount': 90,
          'hasProof': true,
          'status': status,
          if (status == 'Rejected') 'reviewReason': 'الصورة غير مطابقة',
        };
      });
      await t.pumpWidget(
        app(GuardianProductOrder(auth: auth, id: 'sale'), scale: 1.3),
      );
      await t.pumpAndSettle();
      expect(find.text('إثبات الدفع قيد المراجعة'), findsOneWidget);
      expect(find.text('طلب المنتج'), findsOneWidget);
      expect(find.text('M'), findsOneWidget);
      expect(find.text('رقم القميص'), findsOneWidget);
      expect(find.text('التحويل للأكاديمية'), findsNothing);
      expect(find.text('إعادة تأكيد نفس الإثبات'), findsNothing);
      await tap(t, 'استبدال إثبات الدفع');
      expect(find.text('رفع إيصال الدفع'), findsOneWidget);
      expect(find.text('التحويل للأكاديمية'), findsNothing);
      status = 'Rejected';
      await tap(t, 'تحديث حالة الطلب');
      expect(find.text('الصورة غير مطابقة'), findsOneWidget);
      expect(find.text('رفع إيصال الدفع'), findsNothing);
      expect(
        calls.where(
          (call) => call == 'GET /api/v1/guardian/product-sales/sale',
        ),
        hasLength(2),
      );
      expect(calls.any((call) => call.startsWith('POST ')), isFalse);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  for (final outcome in ['Confirmed', 'Rejected']) {
    testWidgets(
      'order details and the list both read the saved $outcome status',
      (t) async {
        t.view.physicalSize = const Size(320, 844);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.reset);
        var status = 'Pending', listReads = 0;
        final paths = <String>[];
        final auth = await authenticate('Guardian', (m, p, b, token) async {
          paths.add('$m $p');
          final row = {
            'id': 'sale',
            'productName': 'زي اختبار',
            'status': status,
            'sizeSnapshot': 'M',
            'shirtNumberSnapshot': 7,
            'quantity': 1,
            'amount': 180,
            'currency': 'EGP',
            'hasProof': true,
            if (status == 'Rejected')
              'reviewReason': 'راجع صورة التحويل مع الإدارة',
          };
          if (Uri.parse(p).path.endsWith('/product-sales')) {
            listReads++;
            if (listReads == 2) throw const ApiFailure(503, 'تعذر الاتصال');
            return [
              row,
              {
                ...row,
                'id': 'other',
                'productName': 'طلب مختلف',
                'status': 'Confirmed',
              },
            ];
          }
          return row;
        });
        await t.pumpWidget(app(GuardianProductOrders(auth: auth), scale: 1.3));
        await t.pumpAndSettle();
        await tap(t, 'متابعة الطلب');
        expect(
          find.byWidgetPredicate(
            (w) => w is GuardianProductOrder && w.id == 'sale',
          ),
          findsOneWidget,
        );
        status = outcome;
        await tap(t, 'تحديث حالة الطلب');
        final state = outcome == 'Confirmed' ? 'تم تأكيد الدفع' : 'الطلب مرفوض';
        expect(find.text(state), findsOneWidget);
        expect(
          find.text(outcome == 'Confirmed' ? 'المبلغ المدفوع' : 'مبلغ الطلب'),
          findsOneWidget,
        );
        if (outcome == 'Rejected')
          expect(find.text('راجع صورة التحويل مع الإدارة'), findsOneWidget);
        expect(find.text('استبدال إثبات الدفع'), findsNothing);
        await t.tap(find.byTooltip("رجوع"));
        await t.pumpAndSettle();
        expect(find.text('تعذر الاتصال'), findsOneWidget);
        expect(find.text('متابعة الطلب'), findsNothing);
        await t.tap(find.text('إعادة المحاولة'));
        await t.pumpAndSettle();
        expect(listReads, 3);
        expect(find.text(state), findsWidgets);
        expect(find.text('متابعة الطلب'), findsNothing);
        expect(paths.every((p) => p.startsWith('GET ')), isTrue);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }

  testWidgets(
    'refreshes the empty orders list and reloads after return without a detail refresh',
    (t) async {
      var status = 'Pending', listReads = 0;
      final row = {
        'id': 'sale',
        'productName': 'طلب اختبار',
        'amount': 180,
        'currency': 'EGP',
        'quantity': 1,
        'hasProof': true,
      };
      final auth = await authenticate('Guardian', (m, p, b, token) async {
        final order = {...row, 'status': status};
        if (Uri.parse(p).path.endsWith('/product-sales'))
          return ++listReads == 1 ? [] : [order];
        return order;
      });
      await t.pumpWidget(app(GuardianProductOrders(auth: auth)));
      await t.pumpAndSettle();
      expect(find.text('لا توجد طلبات منتجات بعد.'), findsOneWidget);
      await t.tap(find.byTooltip('تحديث الطلبات'));
      await t.pumpAndSettle();
      await tap(t, 'متابعة الطلب');
      status = 'Confirmed';
      await t.tap(find.byTooltip("رجوع"));
      await t.pumpAndSettle();
      expect(find.text('تم تأكيد الدفع'), findsOneWidget);
      expect(listReads, 3);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets('opens the matching row and keeps scroll position after return', (
    t,
  ) async {
    t.view.physicalSize = const Size(320, 844);
    t.view.devicePixelRatio = 1;
    addTearDown(t.view.reset);
    var status = 'Pending', listReads = 0;
    final orders = [
      for (var i = 0; i < 12; i++)
        {
          'id': 'sale-$i',
          'productName': 'طلب اختبار $i',
          'amount': 180,
          'currency': 'EGP',
          'quantity': 1,
          'hasProof': true,
          'status': 'Confirmed',
        },
    ];
    final auth = await authenticate('Guardian', (m, p, b, token) async {
      final order = {...orders[6], 'status': status};
      if (Uri.parse(p).path.endsWith('/product-sales')) {
        listReads++;
        return [...orders.take(6), order, ...orders.skip(7)];
      }
      expect(p, '/api/v1/guardian/product-sales/sale-6');
      return order;
    });
    await t.pumpWidget(app(GuardianProductOrders(auth: auth), scale: 1.3));
    await t.pumpAndSettle();
    final scrollable = find.byType(Scrollable);
    final selected = find.text('متابعة الطلب');
    await t.scrollUntilVisible(selected, 280, scrollable: scrollable);
    await Scrollable.ensureVisible(t.element(selected), alignment: .5);
    await t.pumpAndSettle();
    final before = t.state<ScrollableState>(scrollable).position.pixels;
    expect(before, greaterThan(0));
    await t.tap(selected);
    await t.pumpAndSettle();
    expect(find.text('طلب اختبار 6'), findsOneWidget);
    status = 'Rejected';
    await t.tap(find.byTooltip('رجوع'));
    await t.pumpAndSettle();
    expect(listReads, 2);
    expect(t.state<ScrollableState>(scrollable).position.pixels, before);
    expect(find.text('الطلب مرفوض').hitTestable(), findsOneWidget);
    expect(find.text('متابعة الطلب'), findsNothing);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  testWidgets(
    'distinguishes identical orders and restores a later history page after failure and return',
    (t) async {
      t.view.physicalSize = const Size(320, 844);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.reset);
      const root = '/api/v1/guardian/product-sales';
      final requests = <String>[];
      const references = ['A001AB32DC10', 'A002AB32DC10', 'A101AB32DC10'];
      final orders = [
        for (var i = 0; i < references.length; i++)
          {
            'id': 'sale-$i',
            'reference': references[i],
            'productName': 'زجاجة مياه رياضية',
            'amount': 180,
            'currency': 'EGP',
            'quantity': 1,
            'status': 'Rejected',
            'createdOn': i == 1 ? '2025-11-13' : '2026-11-13',
          },
      ];
      var failed = false;
      final auth = await authenticate('Guardian', (method, path, _, _) async {
        expect(method, 'GET');
        requests.add(path);
        final uri = Uri.parse(path);
        if (uri.path != root) {
          return orders.singleWhere(
            (order) => order['id'] == uri.pathSegments.last,
          );
        }
        final page = int.parse(uri.queryParameters['page']!);
        if (page == 2 && !failed) {
          failed = true;
          throw const ApiFailure(503, 'تعذر الاتصال');
        }
        return {
          'page': page,
          'hasMore': page == 1,
          'items': page == 1 ? orders.take(2).toList() : [orders.last],
        };
      });
      await t.pumpWidget(app(GuardianProductOrders(auth: auth), scale: 1.3));
      await t.pumpAndSettle();
      for (var i = 0; i < 2; i++) {
        final row = find.byKey(ValueKey('product-order:sale-$i'));
        final button = find.descendant(
          of: row,
          matching: find.text('عرض الطلب'),
        );
        expect(
          find.descendant(
            of: row,
            matching: find.textContaining(references[i]),
          ),
          findsOneWidget,
        );
        await t.scrollUntilVisible(
          button,
          230,
          scrollable: find.byType(Scrollable),
        );
        await t.ensureVisible(button);
        await t.tap(button);
        await t.pumpAndSettle();
        expect(requests.last, '$root/sale-$i');
        expect(find.textContaining(references[i]), findsOneWidget);
        final date = DateTime.parse(orders[i]['createdOn'] as String);
        final local = MaterialLocalizations.of(
          t.element(find.byType(GuardianProductOrder)),
        );
        expect(find.text(local.formatShortDate(date)), findsOneWidget);
        await t.tap(find.byTooltip('رجوع'));
        await t.pumpAndSettle();
      }
      await t.tap(find.byTooltip('الصفحة التالية'));
      await t.pumpAndSettle();
      expect(find.text('تعذر الاتصال'), findsOneWidget);
      expect(find.text('زجاجة مياه رياضية'), findsNothing);
      await t.tap(find.text('إعادة المحاولة'));
      await t.pumpAndSettle();
      expect(find.text('الصفحة 2'), findsOneWidget);
      expect(find.byTooltip('الصفحة التالية'), findsNothing);
      await tap(t, 'عرض الطلب');
      expect(requests.last, '$root/sale-2');
      expect(find.textContaining(references.last), findsOneWidget);
      await t.tap(find.byTooltip('رجوع'));
      await t.pumpAndSettle();
      expect(requests.last, '$root?page=2&pageSize=25');
      await t.tap(find.byTooltip('تحديث الطلبات'));
      await t.pumpAndSettle();
      expect(requests.last, '$root?page=2&pageSize=25');
      await t.tap(find.byTooltip('الصفحة السابقة'));
      await t.pumpAndSettle();
      expect(requests.last, '$root?page=1&pageSize=25');
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  for (final missing in [false, true]) {
    testWidgets(
      'product detail guards a mismatched or unavailable order: $missing',
      (t) async {
        final auth = await authenticate('Guardian', (m, p, b, token) async {
          if (missing) throw const ApiFailure(404, 'Not found');
          return {
            'id': 'another',
            'productName': 'بيانات غير مطابقة',
            'status': 'Pending',
            'amount': 180,
          };
        });
        await t.pumpWidget(app(GuardianProductOrder(auth: auth, id: 'sale')));
        await t.pumpAndSettle();
        expect(
          find.text(
            missing ? 'طلب المنتج غير متاح لحسابك.' : 'تعذر قراءة طلب المنتج.',
          ),
          findsOneWidget,
        );
        expect(find.text('بيانات غير مطابقة'), findsNothing);
        expect(find.text('رفع إيصال الدفع'), findsNothing);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
}
