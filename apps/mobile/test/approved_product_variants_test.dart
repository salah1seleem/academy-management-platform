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
  await t.scrollUntilVisible(f, 150, scrollable: find.byType(Scrollable).first);
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
}
