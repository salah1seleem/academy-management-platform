import 'package:academy_mobile/features/guardian/family_renewal_quote.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'guardian_test.dart' show shell;

final sharedEnrollments = <Map<String, dynamic>>[
  for (final id in ['a', 'b'])
    {
      'id': id,
      'playerId': 'player-$id',
      'player': 'ابن $id اصطناعي',
      'sportId': 'football',
      'sport': 'كرة القدم',
      'branch': 'فرع $id',
      'plans': [
        {
          'id': 'common',
          'name': 'شهر مشترك',
          'amount': '1000.00',
          'currency': 'EGP',
        },
        if (id == 'a')
          {
            'id': 'only-a',
            'name': 'باقة للفرع الأول',
            'amount': '500.00',
            'currency': 'EGP',
          },
      ],
    },
  {
    'id': 'swim',
    'playerId': 'player-a',
    'player': 'ابن a اصطناعي',
    'sportId': 'swimming',
    'sport': 'السباحة',
    'branch': 'فرع أول',
    'plans': [
      {
        'id': 'swim-plan',
        'name': 'سباحة',
        'amount': '800.00',
        'currency': 'EGP',
      },
    ],
  },
];
void main() {
  testWidgets(
    'Same-sport children select a real common plan and server child allocations stay visible',
    (t) async {
      await t.binding.setSurfaceSize(const Size(390, 1000));
      addTearDown(() => t.binding.setSurfaceSize(null));
      List<Map<String, dynamic>>? sent;
      await t.pumpWidget(
        shell(
          Scaffold(
            body: FamilyRenewalChoices(
              sharedSportOnly: true,
              enrollments: sharedEnrollments,
              onQuote: (lines) async {
                sent = lines;
                return {
                  'quoteHash': 'hash',
                  'currency': 'EGP',
                  'originalAmount': '2000.00',
                  'discountAmount': '100.00',
                  'totalAmount': '1900.00',
                  'items': [
                    {
                      'player': 'ابن a اصطناعي',
                      'sport': 'كرة القدم',
                      'plan': 'شهر مشترك',
                      'finalAmount': '1000.00',
                      'discountAmount': '0.00',
                      'currency': 'EGP',
                    },
                    {
                      'player': 'ابن b اصطناعي',
                      'sport': 'كرة القدم',
                      'plan': 'شهر مشترك',
                      'finalAmount': '900.00',
                      'discountAmount': '100.00',
                      'currency': 'EGP',
                    },
                  ],
                };
              },
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('السباحة'), findsNothing);
      for (final id in ['a', 'b']) {
        final checkbox = find.widgetWithText(
          CheckboxListTile,
          'ابن $id اصطناعي',
        );
        await t.ensureVisible(checkbox);
        await t.tap(checkbox);
        await t.pumpAndSettle();
      }
      await t.ensureVisible(find.byType(DropdownButtonFormField<String>).last);
      await t.tap(find.byType(DropdownButtonFormField<String>).last);
      await t.pumpAndSettle();
      expect(find.textContaining('باقة للفرع الأول'), findsNothing);
      await t.tap(find.text('شهر مشترك · 1,000 ج.م').last);
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('مراجعة إجمالي 2 أبناء'),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(find.text('مراجعة إجمالي 2 أبناء'));
      await t.pumpAndSettle();
      expect(sent, [
        {'sportEnrollmentId': 'a', 'subscriptionPlanId': 'common'},
        {'sportEnrollmentId': 'b', 'subscriptionPlanId': 'common'},
      ]);
      expect(find.textContaining('1,900'), findsWidgets);
      expect(find.textContaining('ابن a اصطناعي'), findsWidgets);
      expect(find.textContaining('ابن b اصطناعي'), findsWidgets);
      expect(t.takeException(), isNull);
    },
  );
  testWidgets(
    'No common plan prevents quoting; changing sport clears sibling selections',
    (t) async {
      var calls = 0;
      final data = sharedEnrollments
          .map((e) => Map<String, dynamic>.from(e))
          .toList();
      data[1]['plans'] = [
        {
          'id': 'other',
          'name': 'باقة مختلفة',
          'amount': '1000.00',
          'currency': 'EGP',
        },
      ];
      await t.pumpWidget(
        shell(
          Scaffold(
            body: FamilyRenewalChoices(
              sharedSportOnly: true,
              enrollments: data,
              onQuote: (lines) async {
                calls++;
                return {};
              },
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      for (final id in ['a', 'b']) {
        t
            .widget<CheckboxListTile>(
              find.widgetWithText(CheckboxListTile, 'ابن $id اصطناعي'),
            )
            .onChanged!(true);
        await t.pumpAndSettle();
      }
      expect(find.textContaining('لا توجد باقة مشتركة مؤهلة'), findsOneWidget);
      expect(
        t.widget<FilledButton>(find.byType(FilledButton)).onPressed,
        isNull,
      );
      t
          .widget<DropdownButtonFormField<String>>(
            find.byType(DropdownButtonFormField<String>).first,
          )
          .onChanged!('swimming');
      await t.pumpAndSettle();
      expect(find.text('ابن b اصطناعي'), findsNothing);
      expect(
        t.widget<CheckboxListTile>(find.byType(CheckboxListTile)).value,
        isFalse,
      );
      expect(calls, 0);
      expect(t.takeException(), isNull);
    },
  );
}
