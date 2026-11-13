import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/owner/owner_reports.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

void main() {
  testWidgets(
    'Ledger paging retries without mixing branches or source totals',
    (t) async {
      final requests = <Uri>[];
      var failed = false;
      final auth = await authenticate('AcademyOwner', (
        method,
        path,
        body,
        token,
      ) async {
        if (path.contains('/academy/calendar')) return {'today': '2026-09-30'};
        if (path.contains('/structure/options')) {
          return {
            'branches': [
              {'id': 'a', 'arabicName': 'فرع أول'},
              {'id': 'b', 'arabicName': 'فرع ثان'},
            ],
          };
        }
        final uri = Uri.parse(path);
        requests.add(uri);
        final branch = uri.queryParameters['branchId'];
        final second = uri.queryParameters['page'] == '2';
        if (branch == 'a' && second && !failed) {
          failed = true;
          throw const ApiFailure(503, 'تعذر الاتصال.');
        }
        return {
          'currency': 'EGP',
          'netAmount': branch == 'a' ? 15600 : 300,
          'totalCount': branch == 'a' ? 26 : 1,
          'pageSize': 25,
          'sourceSummary': [
            {
              'key': 'Subscription',
              'label': 'الاشتراكات',
              'amount': branch == 'a' ? 15600 : 300,
            },
          ],
          'items': [
            for (final index
                in branch == 'a'
                    ? (second ? [26] : List.generate(25, (i) => i + 1))
                    : [1])
              {
                'collectionType': 'Subscription',
                'collectionId': '$branch-$index',
                'receiptNumber': branch == 'a' ? 'QA-A-$index' : 'QA-B-1',
                'player': 'لاعب اختبار',
                'amount': branch == 'a' ? 600 : 300,
                'currency': 'EGP',
              },
          ],
        };
      });
      await t.pumpWidget(
        app(
          OwnerReports(
            auth: auth,
            initialReport: 'financial',
            initialFilters: const {
              'from': '2026-09-01',
              'to': '2026-09-30',
              'branchId': 'a',
            },
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(
        t
            .widgetList<MoneyText>(find.byType(MoneyText))
            .any((value) => value.value == 15600),
        isTrue,
      );
      await t.scrollUntilVisible(
        find.textContaining('QA-A-25'),
        420,
        scrollable: find.byType(Scrollable).last,
      );
      await t.pumpAndSettle();
      final retry = find.text('تعذر تحميل المزيد · إعادة المحاولة');
      expect(retry, findsOneWidget);
      await t.tap(retry.hitTestable());
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.textContaining('QA-A-26'),
        200,
        scrollable: find.byType(Scrollable).last,
      );
      expect(
        requests.where((r) => r.queryParameters['page'] == '2'),
        hasLength(2),
      );
      expect(requests.last.queryParameters['branchId'], 'a');
      await t.tap(find.text('فرع ثان').hitTestable());
      await t.pumpAndSettle();
      expect(find.textContaining('QA-A-26'), findsNothing);
      expect(find.textContaining('QA-B-1'), findsOneWidget);
      expect(requests.last.queryParameters['page'], '1');
      expect(requests.last.queryParameters['branchId'], 'b');
      await t.tap(find.text('الاشتراكات').hitTestable());
      await t.pumpAndSettle();
      expect(requests.last.queryParameters['collectionType'], 'Subscription');
      await t.tap(find.text('عرض سجل كل المصادر').hitTestable());
      await t.pumpAndSettle();
      expect(requests.last.queryParameters['collectionType'], isNull);
      await t.tap(find.byTooltip('مسح الفلاتر').hitTestable());
      await t.pumpAndSettle();
      expect(requests.last.queryParameters['allDates'], 'true');
      expect(requests.last.queryParameters['branchId'], isNull);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

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
