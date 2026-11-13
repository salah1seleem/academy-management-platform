import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_expenses.dart';
import 'package:academy_mobile/features/owner/owner_reports.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/visibility.dart';
import 'v2_role_workspaces_test.dart' show app, authenticate;

const root = '/api/v1/finance/operating-expenses';
const scope = {
  'branchId': 'branch-a',
  'from': '2026-11-09',
  'to': '2026-11-09',
};
const sourceFilters = {
  ...scope,
  'category': 'Maintenance',
  'paid': 'true',
  'status': 'Posted',
  'dateBasis': 'payment',
};
const row = <String, dynamic>{
  'id': 'expense-a',
  'branchId': 'branch-a',
  'category': 'Maintenance',
  'scope': 'Branch',
  'status': 'Posted',
  'currency': 'EGP',
  'amount': 123.45,
  'paidOn': '2026-11-09',
  'periodFrom': '2026-09-01',
  'periodTo': '2026-09-01',
};
const details = {
  'expense': {...row, 'note': 'صيانة اصطناعية مثبتة'},
  'allocationTotal': 123.45,
  'allocations': [
    {'serviceKind': 'Training', 'trainingGroupId': null, 'amount': 123.45},
  ],
};
const structure = {
  'branches': [
    {'id': 'branch-a', 'arabicName': 'فرع مدينة نصر'},
    {'id': 'branch-b', 'arabicName': 'فرع الشروق'},
  ],
};
const statement = <String, dynamic>{
  'from': '2026-11-09',
  'to': '2026-11-09',
  'currency': 'EGP',
  'surplus': -123.45,
  'totalIncome': 0,
  'totalExpenses': 123.45,
  'income': [],
  'monthly': [],
  'expenses': [
    {
      'label': 'الصيانة والتشغيل',
      'amount': 123.45,
      'sourceUrl': '/dashboard/finance/operating-expenses?category=Maintenance&status=Posted&paid=true&dateBasis=payment',
    },
  ],
};

Future<AuthController> expenseOwner(
  FutureOr<Object?> Function(Uri uri) handle,
) => authenticate('AcademyOwner', (_, path, _, _) async {
  final uri = Uri.parse(path);
  return switch (uri.path) {
    '/api/v1/academy/calendar' => {
      'today': '2026-11-09',
      'trailingMonthFrom': '2026-10-09',
    },
    '/api/v1/manage/structure/options' => structure,
    _ => await handle(uri),
  };
});

Future<void> tapExpense(WidgetTester tester, num amount) async {
  final item = find.widgetWithText(GLink, money(amount, 'EGP'));
  await revealTestTarget(tester, item);
  await tester.tap(item);
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('Unpaid balance opens the posted source through the report end', (
    t,
  ) async {
    final reads = <Uri>[];
    final auth = await expenseOwner((uri) {
      reads.add(uri);
      return switch (uri.path) {
        '/api/v1/reports/operating-statement' => {
          ...statement,
          'unpaidOperatingExpenses': 56.75,
        },
        root => {
          'count': 1,
          'items': [
            {...row, 'paidOn': null, 'amount': 56.75},
          ],
        },
        '$root/expense-a' => {
          ...details,
          'expense': {...row, 'paidOn': null, 'amount': 56.75},
        },
        _ => <String, dynamic>{},
      };
    });
    await t.pumpWidget(
      app(
        OwnerReports(
          auth: auth,
          initialReport: 'operating',
          initialFilters: const {...scope, 'sportId': 'sport-a'},
        ),
      ),
    );
    await t.pumpAndSettle();
    final source = find.widgetWithText(GLink, 'مصروفات مرحلة غير مسددة');
    await revealTestTarget(t, source);
    await t.tap(source);
    await t.pumpAndSettle();
    expect(reads.singleWhere((uri) => uri.path == root).queryParameters, {
      'to': '2026-11-09',
      'branchId': 'branch-a',
      'sportId': 'sport-a',
      'unpaid': 'true',
      'status': 'Posted',
      'page': '1',
      'pageSize': '25',
    });
    expect(find.text('المصروفات غير المسددة'), findsOneWidget);
    final expense = find.widgetWithText(GLink, 'الصيانة والتشغيل');
    await revealTestTarget(t, expense);
    await t.tap(expense);
    await t.pumpAndSettle();
    expect(reads.any((uri) => uri.path == '$root/expense-a'), isTrue);
    final title = find.text('تفاصيل المصروف');
    final detail = find.byType(OwnerExpenseDetail);
    expect(detail, findsOneWidget);
    Navigator.of(t.element(detail)).pop();
    await t.pumpAndSettle();
    await t.tap(find.byTooltip('رجوع').hitTestable());
    await t.pumpAndSettle();
    expect(source, findsOneWidget);
    expect(title, findsNothing);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  for (final unpaid in [null, 0]) {
    testWidgets('Unpaid balance preserves missing versus zero ($unpaid)', (
      t,
    ) async {
      final auth = await expenseOwner(
        (_) => {...statement, 'unpaidOperatingExpenses': unpaid},
      );
      await t.pumpWidget(
        app(
          OwnerReports(
            auth: auth,
            initialReport: 'operating',
            initialFilters: scope,
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(
        find.widgetWithText(GLink, 'مصروفات مرحلة غير مسددة'),
        findsNothing,
      );
      final row = find.text('مصروفات مرحلة غير مسددة');
      if (unpaid == 0) await revealTestTarget(t, row);
      expect(row, unpaid == 0 ? findsOneWidget : findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    });
  }

  testWidgets('The operating line opens the same payment-date source', (
    t,
  ) async {
    final calls = <Uri>[];
    final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
      final uri = Uri.parse(p);
      calls.add(uri);
      return switch (uri.path) {
        '/api/v1/academy/calendar' => {'today': '2026-11-09'},
        '/api/v1/manage/structure/options' => structure,
        '/api/v1/reports/operating-statement' => statement,
        root => {
          'count': 1,
          'items': [row],
        },
        _ => details,
      };
    });
    await t.pumpWidget(
      app(
        OwnerReports(
          auth: auth,
          initialReport: 'operating',
          initialFilters: scope,
        ),
      ),
    );
    await t.pumpAndSettle();
    final line = find.text('الصيانة والتشغيل');
    await revealTestTarget(t, line);
    await t.tap(line);
    await t.pumpAndSettle();
    expect(
      calls.singleWhere((uri) => uri.path == root).queryParameters,
      containsPair('dateBasis', 'payment'),
    );
    expect(
      calls.singleWhere((uri) => uri.path == root).queryParameters,
      containsPair('branchId', 'branch-a'),
    );
    expect(find.text('تاريخ السداد'), findsOneWidget);
    expect(find.textContaining('Maintenance'), findsNothing);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  testWidgets('An expense opens the authorized stored record and allocations', (
    t,
  ) async {
    final calls = <String>[];
    final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
      calls.add(p);
      return switch (Uri.parse(p).path) {
        '/api/v1/academy/calendar' => {'today': '2026-11-09'},
        '/api/v1/manage/structure/options' => structure,
        root => {
          'count': 1,
          'items': [row],
        },
        '$root/expense-a' => details,
        _ => <String, dynamic>{},
      };
    });
    await t.pumpWidget(
      app(
        OwnerReports(
          auth: auth,
          initialReport: 'expenses',
          initialFilters: sourceFilters,
        ),
      ),
    );
    await t.pumpAndSettle();
    await tapExpense(t, 123.45);
    expect(calls, contains('$root/expense-a'));
    expect(find.text('صيانة اصطناعية مثبتة'), findsOneWidget);
    await revealTestTarget(t, find.text('الأنصبة المحفوظة'));
    await revealTestTarget(t, find.text('غير موزع على المجموعات'));
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  testWidgets(
    'Changing branch and clearing dates preserves the payment source',
    (t) async {
      final reads = <Uri>[];
      final auth = await expenseOwner((uri) {
        if (uri.path == root) {
          reads.add(uri);
          return {
            'count': 1,
            'items': [row],
          };
        }
        return details;
      });
      await t.pumpWidget(
        app(
          OwnerReports(
            auth: auth,
            initialReport: 'expenses',
            initialFilters: sourceFilters,
          ),
        ),
      );
      await t.pumpAndSettle();
      final branch = find.widgetWithText(ChoiceChip, 'فرع الشروق');
      await t.ensureVisible(branch);
      await t.tap(branch);
      await t.pumpAndSettle();
      await t.tap(find.byTooltip('مسح الفترة'));
      await t.pumpAndSettle();
      expect(reads.last.queryParameters, {
        'branchId': 'branch-b',
        'category': 'Maintenance',
        'status': 'Posted',
        'dateBasis': 'payment',
        'paid': 'true',
        'page': '1',
        'pageSize': '25',
      });
      await tapExpense(t, 123.45);
      await t.tap(find.byType(BackButton));
      await t.pumpAndSettle();
      expect(t.widget<ChoiceChip>(branch).selected, isTrue);
      expect(reads, hasLength(3));
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'The general list explains service dates and shows a real empty state',
    (t) async {
      final auth = await expenseOwner((uri) {
        expect(uri.queryParameters, isNot(contains('dateBasis')));
        return {'count': 0, 'items': []};
      });
      await t.pumpWidget(
        app(
          OwnerReports(
            auth: auth,
            initialReport: 'expenses',
            initialFilters: scope,
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('المصروفات'), findsOneWidget);
      expect(find.text('فترة الاستحقاق'), findsOneWidget);
      expect(
        find.text('لا توجد مصروفات مستحقة ضمن النطاق المحدد.'),
        findsOneWidget,
      );
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  for (final status in [403, 404, 503]) {
    testWidgets(
      'Expense detail never shows a cached amount after HTTP $status',
      (t) async {
        var reads = 0;
        final auth = await expenseOwner((uri) {
          expect(uri.path, '$root/expense-a');
          if (++reads == 1) throw ApiFailure(status, 'تعذر قراءة هذا المصروف.');
          return details;
        });
        await t.pumpWidget(
          app(
            OwnerExpenseDetail(
              auth: auth,
              id: 'expense-a',
              metadata: structure,
            ),
          ),
        );
        await t.pumpAndSettle();
        expect(find.text('تعذر قراءة هذا المصروف.'), findsOneWidget);
        expect(find.byType(MoneyText), findsNothing);
        if (status == 503) {
          await t.tap(find.text('إعادة المحاولة'));
          await t.pumpAndSettle();
          expect(find.byType(MoneyText), findsWidgets);
          expect(reads, 2);
        }
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }

  testWidgets('A late detail is discarded after leaving the academy context', (
    t,
  ) async {
    final pending = Completer<Object?>();
    final auth = await expenseOwner((uri) => pending.future);
    await t.pumpWidget(
      app(OwnerExpenseDetail(auth: auth, id: 'expense-a', metadata: structure)),
    );
    await t.pump();
    await auth.logout();
    pending.complete(details);
    await t.pumpAndSettle();
    expect(find.byType(MoneyText), findsNothing);
    expect(find.text('صيانة اصطناعية مثبتة'), findsNothing);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  testWidgets(
    'The second page keeps source filters and survives opening a record',
    (t) async {
      final reads = <Uri>[];
      final auth = await expenseOwner((uri) {
        if (uri.path == root) {
          reads.add(uri);
          return {
            'count': 26,
            'pageSize': 25,
            'items': uri.queryParameters['page'] == '1'
                ? [
                    for (var n = 1; n <= 25; n++)
                      {...row, 'id': 'expense-$n', 'amount': n},
                  ]
                : [
                    {...row, 'id': 'expense-26', 'amount': 26},
                  ],
          };
        }
        return {
          ...details,
          'expense': {...row, 'id': 'expense-26', 'amount': 26},
        };
      });
      await t.pumpWidget(
        app(
          OwnerReports(
            auth: auth,
            initialReport: 'expenses',
            initialFilters: sourceFilters,
          ),
        ),
      );
      await t.pumpAndSettle();
      await tapExpense(t, 26);
      expect(reads, hasLength(2));
      expect(reads.last.queryParameters, {
        ...reads.first.queryParameters,
        'page': '2',
      });
      await t.tap(find.byType(BackButton));
      await t.pumpAndSettle();
      expect(find.widgetWithText(GLink, money(26, 'EGP')), findsOneWidget);
      expect(reads, hasLength(2));
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  test(
    'The expense source accepts only paid routes and preserves all dates',
    () {
      final line = rows(statement['expenses']).single;
      final all = operatingExpenseSource(line, statement, {
        ...scope,
        'allDates': 'true',
      });
      expect(all, containsPair('allDates', 'true'));
      expect(all, isNot(contains('from')));
      expect(
        operatingExpenseSource({...line, 'amount': 0}, statement, scope),
        isNull,
      );
      expect(
        operatingExpenseSource(
          {...line, 'sourceUrl': 'https://example.test${line['sourceUrl']}'},
          statement,
          scope,
        ),
        isNull,
      );
      expect(
        operatingExpenseSource(
          {...line, 'sourceUrl': '/dashboard/payroll'},
          statement,
          scope,
        ),
        isNull,
      );
    },
  );

  testWidgets(
    'Stored group allocations preserve their names, fallback and amounts',
    (t) async {
      final auth = await expenseOwner(
        (_) => {
          ...details,
          'allocations': [
            {
              'serviceKind': 'Training',
              'trainingGroupId': 'known',
              'amount': 61.73,
            },
            {
              'serviceKind': 'Training',
              'trainingGroupId': 'retired',
              'amount': 61.72,
            },
          ],
        },
      );
      await t.pumpWidget(
        app(
          OwnerExpenseDetail(
            auth: auth,
            id: 'expense-a',
            metadata: {
              ...structure,
              'groups': [
                {'id': 'known', 'arabicName': 'مجموعة تدريب اصطناعية'},
              ],
            },
          ),
        ),
      );
      await t.pumpAndSettle();
      for (final label in ['مجموعة تدريب اصطناعية', 'مجموعة محفوظة']) {
        await revealTestTarget(t, find.text(label));
      }
      final amounts = t
          .widgetList<MoneyText>(find.byType(MoneyText))
          .map((w) => w.value);
      expect(amounts, containsAll([61.73, 61.72]));
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets('A different detail id cannot populate the route', (t) async {
    final auth = await expenseOwner((_) => details);
    await t.pumpWidget(
      app(OwnerExpenseDetail(auth: auth, id: 'expense-b', metadata: structure)),
    );
    await t.pumpAndSettle();
    expect(find.text('تعذر التحقق من تفاصيل المصروف.'), findsOneWidget);
    expect(find.byType(MoneyText), findsNothing);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  for (final width in [320.0, 390.0]) {
    testWidgets(
      'Expense dates, amounts and row navigation fit at $width and 200%',
      (t) async {
        t.view.physicalSize = Size(width, 800);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.reset);
        final auth = await expenseOwner(
          (uri) => uri.path == root
              ? {
                  'count': 1,
                  'items': [row],
                }
              : details,
        );
        await t.pumpWidget(
          app(
            OwnerReports(
              auth: auth,
              initialReport: 'expenses',
              initialFilters: sourceFilters,
            ),
            scale: 2,
          ),
        );
        await t.pumpAndSettle();
        expect(find.text('المصروفات'), findsOneWidget);
        expect(find.text('تاريخ السداد'), findsOneWidget);
        await tapExpense(t, 123.45);
        await revealTestTarget(t, find.text('غير موزع على المجموعات'));
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
}
