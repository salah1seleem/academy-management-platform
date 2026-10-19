import 'package:academy_mobile/features/shared/vip_rewards.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' show owner, shell;

void main() {
  testWidgets(
    'VIP preview is not issuance and changing a value removes confirmation',
    (tester) async {
      final writes = <Map<String, dynamic>>[];
      final auth = await owner((method, path, body, token) async {
        if (method == 'POST') {
          final b = Map<String, dynamic>.from(body as Map);
          writes.add(b);
          return {
            'quoteHash': 'hash',
            'recipients': [
              {'name': 'أسرة اصطناعية'},
            ],
            'percentage': b['percentage'],
            'expiresOn': b['expiresOn'],
            'message': b['message'],
            'delivery': 'إشعار داخلي فقط',
          };
        }
        return {
          'today': '2026-09-28',
          'currency': 'EGP',
          'conditions': 'باقة واحدة دون جمع',
          'items': [
            {'id': 'g', 'name': 'أسرة اصطناعية', 'monthlyValue': 1000},
          ],
        };
      });
      await tester.pumpWidget(shell(NativeVipIssue(auth: auth)));
      await tester.pumpAndSettle();
      await tester.tap(find.byType(CheckboxListTile));
      await tester.enterText(find.byType(TextField).first, '20');
      await tester.ensureVisible(find.text('صالحة حتى'));
      await tester.tap(find.text('صالحة حتى'));
      await tester.pumpAndSettle();
      await tester.tap(
        find.text(
          MaterialLocalizations.of(
            tester.element(find.byType(DatePickerDialog)),
          ).okButtonLabel,
        ),
      );
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.text('معاينة المكافأة'),
        150,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.tap(find.text('معاينة المكافأة'));
      await tester.pumpAndSettle();
      expect(writes, hasLength(1));
      expect(writes.single['guardianIds'], ['g']);
      expect(writes.single.containsKey('quoteHash'), false);
      expect(find.text('تأكيد الإصدار'), findsOneWidget);
      await tester.ensureVisible(find.byType(TextField).first);
      await tester.enterText(find.byType(TextField).first, '25');
      await tester.pumpAndSettle();
      expect(find.text('تأكيد الإصدار'), findsNothing);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets('family reward history has no owner controls', (tester) async {
    final auth = await owner(
      (method, path, body, token) async => {
        'count': 1,
        'conditions': 'باقة واحدة دون جمع',
        'items': [
          {
            'id': 'v',
            'percentage': 20,
            'message': 'شكرًا لثقتكم',
            'expiresOn': '2026-10-28',
            'state': 'Reserved',
            'read': false,
            'history': [],
          },
        ],
      },
    );
    await tester.pumpWidget(shell(NativeVipRewards(auth: auth)));
    await tester.pumpAndSettle();
    expect(find.textContaining('محجوزة لطلب'), findsOneWidget);
    expect(find.text('إصدار مكافأة'), findsNothing);
    expect(find.text('إلغاء المكافأة'), findsNothing);
    expect(tester.takeException(), isNull);
  });
}
