import 'package:academy_mobile/features/owner/owner_reports.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

void main() {
  testWidgets(
    'Revenue alias opens one report with full-result source summary and same-scope export',
    (t) async {
      final calls = <String>[];
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        calls.add(p);
        if (p.contains('/academy/calendar')) {
          return {'today': '2026-10-21', 'trailingMonthFrom': '2026-09-21'};
        }
        if (p.contains('/structure/options')) {
          return {
            'branches': [
              {'id': 'b1', 'arabicName': 'فرع اختبار'},
            ],
          };
        }
        return {
          'currency': 'EGP',
          'netAmount': 950,
          'totalAmount': 1000,
          'totalCount': 26,
          'sourceSummary': [
            {'key': 'Subscription', 'label': 'اشتراكات اختبار', 'amount': 1000},
            {'key': 'Refund', 'label': 'استرداد اختبار', 'amount': -50},
          ],
          'items': <Object>[],
        };
      });
      await t.pumpWidget(
        app(
          OwnerReports(
            auth: auth,
            initialReport: 'income',
            initialFilters: {
              'branchId': 'b1',
              'from': '2026-09-21',
              'to': '2026-10-21',
            },
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('الإيرادات والتحصيلات'), findsOneWidget);
      expect(find.text('مصادر التحصيل'), findsOneWidget);
      expect(find.text('اشتراكات اختبار'), findsOneWidget);
      expect(find.text('استرداد اختبار'), findsOneWidget);
      expect(calls.any((p) => p.contains('operating-statement')), isFalse);
      expect(calls.last, contains('branchId=b1'));
      expect(calls.last, contains('pageSize=25'));
      final money = t.widgetList<MoneyText>(find.byType(MoneyText));
      expect(money.any((m) => m.value == 950), isTrue);
      expect(money.any((m) => m.value == -50), isTrue);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
