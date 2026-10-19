import 'package:academy_mobile/features/owner/owner_cockpit.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' show shell;

void main() {
  testWidgets(
    'Cockpit uses response target and explicit decisions with report drilldowns',
    (tester) async {
      String? report;
      await tester.pumpWidget(
        shell(
          SingleChildScrollView(
            child: OwnerCockpitContent(
              data: {
                'currency': 'EGP',
                'collections': {
                  'net': 248000,
                  'previous': 200000,
                  'previousFrom': '2026-08-01',
                  'previousTo': '2026-08-28',
                  'changePercentage': 24,
                },
                'target': {'amount': 320000, 'achievement': 77.5},
                'overdue': {'amount': 1200, 'families': 2},
                'risk': {'players': 3, 'basis': 'تأخر التجديد'},
                'attendance': {'percentage': 80},
                'capacity': {
                  'seats': 4,
                  'conditionalValue': 3600,
                  'unpricedGroups': 1,
                },
              },
              onReport: (value) => report = value,
              onCapacity: () => report = 'capacity',
            ),
          ),
        ),
      );
      expect(find.textContaining('77.5%'), findsOneWidget);
      expect(
        tester
            .widget<LinearProgressIndicator>(
              find.byType(LinearProgressIndicator),
            )
            .value,
        .775,
      );
      final capacity = find.text('أماكن متاحة · 4');
      await tester.ensureVisible(capacity);
      await tester.tap(capacity);
      expect(report, 'capacity');
      final arrears = find.textContaining('متأخرات ·');
      await tester.ensureVisible(arrears);
      await tester.tap(arrears);
      expect(report, 'receivables');
      expect(
        find.textContaining('لا إرسال رسائل أو خصومات تلقائية'),
        findsOneWidget,
      );
    },
  );
}
