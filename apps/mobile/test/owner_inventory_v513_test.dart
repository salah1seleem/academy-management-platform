import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/owner/inventory_preview.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show FakeApi, MemoryStore, credentials;
import 'v2_role_workspaces_test.dart' as f;

const kit = {'id': 'kit', 'arabicName': 'طقم التدريب', 'usesVariants': true};
const twoSizes = [
  {'size': 'M', 'color': 'كحلي', 'shirtNumber': 7, 'quantity': 3},
  {'size': 'M', 'color': 'أبيض', 'shirtNumber': 7, 'quantity': 4},
];

Map<String, Object> stock(
  int quantity,
  List<Map<String, Object>> rows, {
  String id = 'kit',
  bool usesVariants = true,
}) => {
  'id': id,
  'usesVariants': usesVariants,
  'stockQuantity': quantity,
  'variants': rows,
};

void main() {
  testWidgets('owner sees stock then sizes without prices or write commands', (
    t,
  ) async {
    final calls = <String>[];
    final auth = await f.authenticate('AcademyOwner', (m, p, b, token) async {
      calls.add('$m $p');
      if (p.contains('/inventory')) {
        return {
          'id': 'kit',
          'usesVariants': true,
          'stockQuantity': 12,
          'variants': [
            {'size': '8', 'quantity': 4},
            {'size': '10', 'quantity': 8},
          ],
        };
      }
      return [
        {
          'id': 'kit',
          'arabicName': 'طقم التدريب',
          'imageReference': '/demo-assets/catalog-iacademy-kit.png',
          'stockQuantity': 12,
          'usesVariants': true,
          'displayPrice': 850,
          'currency': 'EGP',
        },
      ];
    });
    await t.pumpWidget(
      f.app(Scaffold(body: OwnerInventoryPreview(auth: auth))),
    );
    await t.pumpAndSettle();
    expect(find.text('المتبقي 12'), findsOneWidget);
    expect(find.text('المخزون حسب المقاس'), findsOneWidget);
    expect(find.textContaining('850'), findsNothing);
    await t.tap(find.text('طقم التدريب'));
    await t.pumpAndSettle();
    expect(find.text('إجمالي المتبقي 12'), findsOneWidget);
    expect(find.text('مقاس 8'), findsOneWidget);
    expect(find.text('مقاس 10'), findsOneWidget);
    expect(calls.every((c) => c.startsWith('GET ')), isTrue);
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  testWidgets(
    'Products without variants show total stock without promising sizes',
    (t) async {
      final auth = await f.authenticate(
        'AcademyOwner',
        (m, p, b, token) async => [
          {
            'id': 'plain',
            'arabicName': 'طقم تجريبي',
            'stockQuantity': 22,
            'usesVariants': false,
          },
        ],
      );
      await t.pumpWidget(
        f.app(Scaffold(body: OwnerInventoryPreview(auth: auth))),
      );
      await t.pumpAndSettle();
      expect(find.text('المتبقي 22'), findsOneWidget);
      expect(find.text('المخزون حسب المقاس'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'catalog distinguishes zero from missing stock and retries malformed data',
    (t) async {
      Object? catalog = [
        {...kit, 'stockQuantity': 0},
        {'id': 'other', 'arabicName': 'حقيبة تدريب'},
      ];
      final auth = await f.authenticate(
        'AcademyOwner',
        (_, _, _, _) async => catalog,
      );
      addTearDown(auth.dispose);
      await t.pumpWidget(
        f.app(Scaffold(body: OwnerInventoryPreview(auth: auth))),
      );
      await t.pumpAndSettle();
      expect(find.text('المتبقي 0'), findsOneWidget);
      expect(find.text('الرصيد غير متاح'), findsOneWidget);
      catalog = {'unexpected': []};
      await t.tap(find.byTooltip('تحديث المنتجات'));
      await t.pumpAndSettle();
      expect(find.text('تعذر قراءة المنتجات. حدّث القائمة.'), findsOneWidget);
      expect(find.text('المتبقي 0'), findsNothing);
      catalog = [];
      await t.tap(find.byTooltip('تحديث المنتجات'));
      await t.pumpAndSettle();
      expect(find.text('لا توجد منتجات رياضية نشطة.'), findsOneWidget);
    },
  );

  testWidgets(
    'refreshes selected stock and reads the catalog again on return',
    (t) async {
      var quantity = 7, catalogReads = 0, inventoryReads = 0;
      final auth = await f.authenticate('AcademyOwner', (
        method,
        path,
        _,
        _,
      ) async {
        expect(method, 'GET');
        if (path.endsWith('/inventory')) {
          inventoryReads++;
          if (path == '/api/v1/content/catalog/bag/inventory') {
            return stock(9, [], id: 'bag', usesVariants: false);
          }
          expect(path, '/api/v1/content/catalog/kit/inventory');
          return stock(quantity, [
            {...twoSizes.first, 'quantity': quantity - 4},
            twoSizes.last,
          ]);
        }
        catalogReads++;
        return [
          {...kit, 'stockQuantity': quantity},
          {'id': 'bag', 'arabicName': 'حقيبة تدريب', 'stockQuantity': 9},
        ];
      });
      addTearDown(auth.dispose);
      await t.pumpWidget(
        f.app(Scaffold(body: OwnerInventoryPreview(auth: auth))),
      );
      await t.pumpAndSettle();
      await t.tap(find.text('طقم التدريب'));
      await t.pumpAndSettle();
      expect(find.text('إجمالي المتبقي 7'), findsOneWidget);
      expect(find.text('كحلي · مقاس M · رقم 7'), findsOneWidget);
      expect(find.text('أبيض · مقاس M · رقم 7'), findsOneWidget);
      quantity = 6;
      await t.tap(find.byTooltip('تحديث المخزون'));
      await t.pumpAndSettle();
      expect(find.text('إجمالي المتبقي 6'), findsOneWidget);
      expect(find.text('2'), findsOneWidget);
      expect(find.text('4'), findsOneWidget);
      await t.binding.handlePopRoute();
      await t.pumpAndSettle();
      expect(find.text('المتبقي 6'), findsOneWidget);
      expect((catalogReads, inventoryReads), (2, 2));
      await t.tap(find.text('حقيبة تدريب'));
      await t.pumpAndSettle();
      expect(find.text('إجمالي المتبقي 9'), findsOneWidget);
      expect(find.text('المقاسات'), findsNothing);
    },
  );

  testWidgets(
    'does not render incomplete, inconsistent or mismatched inventory',
    (t) async {
      Object? result = stock(7, twoSizes, id: 'other');
      final auth = await f.authenticate(
        'AcademyOwner',
        (_, _, _, _) async => result,
      );
      addTearDown(auth.dispose);
      await t.pumpWidget(f.app(OwnerInventoryRead(auth: auth, product: kit)));
      await t.pumpAndSettle();
      for (final bad in [
        stock(8, twoSizes),
        {...stock(7, twoSizes), 'stockQuantity': null},
        stock(7, [
          {'size': 'M'},
        ]),
      ]) {
        expect(find.text('تعذر التحقق من رصيد المنتج.'), findsOneWidget);
        expect(find.textContaining('إجمالي المتبقي'), findsNothing);
        result = bad;
        await t.tap(find.text('إعادة المحاولة'));
        await t.pumpAndSettle();
      }
      expect(find.text('تعذر التحقق من رصيد المنتج.'), findsOneWidget);
      result = stock(0, [
        {'size': 'M', 'quantity': 0},
      ]);
      await t.tap(find.text('إعادة المحاولة'));
      await t.pumpAndSettle();
      expect(find.text('إجمالي المتبقي 0'), findsOneWidget);
      expect(find.text('0'), findsOneWidget);
      result = stock(9, [], usesVariants: false);
      await t.tap(find.byTooltip('تحديث المخزون'));
      await t.pumpAndSettle();
      expect(find.text('إجمالي المتبقي 9'), findsOneWidget);
      expect(find.text('المقاسات'), findsNothing);
    },
  );

  testWidgets('recovers from server failure and removes data on denied reads', (
    t,
  ) async {
    var status = 503;
    final auth = await f.authenticate('AcademyOwner', (_, _, _, _) async {
      if (status == 200) return stock(7, twoSizes);
      throw ApiFailure(status, 'تعذر قراءة المخزون.');
    });
    addTearDown(auth.dispose);
    await t.pumpWidget(f.app(OwnerInventoryRead(auth: auth, product: kit)));
    await t.pumpAndSettle();
    expect(find.text('تعذر قراءة المخزون.'), findsOneWidget);
    status = 200;
    await t.tap(find.text('إعادة المحاولة'));
    await t.pumpAndSettle();
    expect(find.text('إجمالي المتبقي 7'), findsOneWidget);
    for (final (code, message) in [
      (403, 'لا يمكن عرض هذه الصفحة'),
      (404, 'مخزون هذا المنتج غير متاح.'),
    ]) {
      status = code;
      await t.tap(find.byTooltip('تحديث المخزون'));
      await t.pumpAndSettle();
      expect(find.text(message), findsOneWidget);
      expect(find.text('إجمالي المتبقي 7'), findsNothing);
    }
  });

  testWidgets(
    'ignores a pending stock response after the owner changes academy',
    (t) async {
      final response = Completer<Object?>();
      final auth = AuthController(
        FakeApi((method, path, _, _) async {
          if (path.endsWith('/inventory')) return response.future;
          final switched = path.endsWith('/select-role');
          return {
            ...credentials(suffix: switched ? 'new' : 'previous'),
            'membershipId': switched ? 'other' : 'owner',
            'memberships': [
              for (final id in ['owner', 'other'])
                {
                  'id': id,
                  'academyId': 'academy-$id',
                  'academyName': 'أكاديمية اختبار',
                  'role': 'AcademyOwner',
                },
            ],
          };
        }),
        MemoryStore(),
      )..restoring = false;
      addTearDown(auth.dispose);
      await auth.verify('phone', 'challenge', 'code');
      await t.pumpWidget(f.app(OwnerInventoryRead(auth: auth, product: kit)));
      await t.pump();
      await auth.selectRole('other');
      expect(auth.session?.selected?.id, 'other');
      response.complete(stock(7, twoSizes));
      await t.pumpAndSettle();
      expect(find.text('إجمالي المتبقي 7'), findsNothing);
      expect(find.text('كحلي · مقاس M · رقم 7'), findsNothing);
      expect(find.text('طقم التدريب'), findsNothing);
    },
  );

  testWidgets('stock and refresh remain reachable at 320 and double text', (
    t,
  ) async {
    t.view.devicePixelRatio = 1;
    t.view.physicalSize = const Size(320, 740);
    addTearDown(t.view.resetDevicePixelRatio);
    addTearDown(t.view.resetPhysicalSize);
    final auth = await f.authenticate(
      'AcademyOwner',
      (_, _, _, _) async => stock(7, twoSizes),
    );
    addTearDown(auth.dispose);
    await t.pumpWidget(
      f.app(
        Builder(
          builder: (context) {
            return MediaQuery(
              data: MediaQuery.of(context)
                  .copyWith(textScaler: const TextScaler.linear(2)),
              child: OwnerInventoryRead(auth: auth, product: kit),
            );
          },
        ),
      ),
    );
    await t.pumpAndSettle();
    expect(find.text('إجمالي المتبقي 7').hitTestable(), findsOneWidget);
    expect(find.byTooltip('تحديث المخزون').hitTestable(), findsOneWidget);
    expect(t.takeException(), isNull);
  });
}
