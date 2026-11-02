import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:academy_mobile/features/owner/inventory_preview.dart';

import 'v2_role_workspaces_test.dart' as f;

void main() {
  testWidgets('owner sees stock then sizes without prices or write commands', (
    t,
  ) async {
    final calls = <String>[];
    final auth = await f.authenticate('AcademyOwner', (m, p, b, token) async {
      calls.add('$m $p');
      if (p.contains('/inventory')) {
        return {
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
      expect(find.text('المخزون الإجمالي'), findsOneWidget);
      expect(find.text('المخزون حسب المقاس'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
