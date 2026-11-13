import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/shared/receivable_balances.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

void main() {
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets('Posted decimal balances remain readable at $width', (
      tester,
    ) async {
      tester.view.physicalSize = Size(width, 1800);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: guardianTheme(),
          home: Scaffold(
            body: Directionality(
              textDirection: TextDirection.rtl,
              child: ReceivableBalanceContent(
                data: {
                  'currency': 'EGP',
                  'outstanding': '1740.00',
                  'overdue': '1240.00',
                  'notYetDue': '500.00',
                  'overpaymentCredit': '0.00',
                  'items': [
                    {
                      'player': 'لاعب اختبار اصطناعي',
                      'description': 'مطالبة موثقة',
                      'charge': '540.00',
                      'outstanding': '340.00',
                      'creditReceived': '60.00',
                      'creditUsed': '0.0',
                      'currency': 'EGP',
                      'dueDate': '2026-09-20',
                      'overdue': true,
                    },
                  ],
                },
              ),
            ),
          ),
        ),
      );
      expect(find.text('1,740 ج.م'), findsOneWidget);
      expect(find.text('1,240 ج.م'), findsOneWidget);
      expect(find.text('تسوية برصيد دائن'), findsOneWidget);
      expect(find.text('60 ج.م'), findsOneWidget);
      expect(find.text('رصيد دائن مستخدم'), findsNothing);
      expect(
        find.text(
          'انتهاء الاشتراك ليس دينًا. هذه أرصدة مستندات مثبتة وليست طلبات دفع معلقة.',
        ),
        findsNothing,
      );
      await tester.tap(find.text('كيف تُحسب المستحقات؟'));
      await tester.pumpAndSettle();
      expect(
        find.text(
          'انتهاء الاشتراك ليس دينًا. هذه أرصدة مستندات مثبتة وليست طلبات دفع معلقة.',
        ),
        findsOneWidget,
      );
      expect(tester.takeException(), isNull);
    });
  }
  testWidgets(
    'staff status changes keep the same branch snapshot and show credit as positive',
    (t) async {
      final reads = <String>[];
      final auth = await authenticate('AcademyOwner', (
        method,
        path,
        _,
        _,
      ) async {
        reads.add(path);
        return {
          'asOf': '2026-11-07T12:00:00Z',
          'currency': 'EGP',
          'outstanding': '340.00',
          'overdue': '340.00',
          'notYetDue': '0.00',
          'overpaymentCredit': '100.00',
          'items': [
            {
              'id': 'overdue',
              'player': 'لاعب مصري خيالي',
              'description': 'مطالبة متأخرة',
              'charge': '540.00',
              'outstanding': '340.00',
              'currency': 'EGP',
              'dueDate': '2026-10-20',
              'overdue': true,
            },
            {
              'id': 'credit',
              'player': 'لاعبة مصرية خيالية',
              'description': 'تسوية تدريب',
              'charge': '200.00',
              'outstanding': '-100.00',
              'currency': 'EGP',
              'dueDate': '2026-10-20',
              'overdue': false,
            },
          ],
        };
      });
      await t.pumpWidget(
        app(
          Scaffold(
            body: ReceivableBalances(
              auth: auth,
              staff: true,
              filters: const {'branchId': 'b1', 'state': 'overdue'},
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('ملخص رصيد الفرع'), findsOneWidget);
      expect(find.text('رصيد دائن للأسرة'), findsOneWidget);
      expect(find.text('تسوية تدريب'), findsNothing);
      await t.tap(find.byType(DropdownButton<String>));
      await t.pumpAndSettle();
      await t.tap(find.text('رصيد دائن').last);
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('تسوية تدريب'),
        150,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.text('مطالبة متأخرة'), findsNothing);
      expect(find.text('100 ج.م'), findsWidgets);
      expect(find.text('-100 ج.م'), findsNothing);
      expect(reads, ['/api/v1/finance/receivables?branchId=b1']);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  for (final field in ['overdue', 'asOf', 'outstanding', 'rowOverdue', 'id']) {
    testWidgets('staff ledger recovers after an invalid $field', (t) async {
      t.view.physicalSize = const Size(320, 900);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.resetPhysicalSize);
      addTearDown(t.view.resetDevicePixelRatio);
      final reads = <String>[];
      final row = {
        'id': 'claim',
        'player': 'لاعب اختبار',
        'description': 'مطالبة تدريب',
        'currency': 'EGP',
        'charge': '540.00',
        'outstanding': '340.00',
        'overdue': true,
      };
      final ledger = {
        'asOf': '2026-11-07T12:00:00Z',
        'currency': 'EGP',
        'outstanding': '840.00',
        'overdue': '340.00',
        'notYetDue': '500.00',
        'overpaymentCredit': '100.00',
        'items': [row],
      };
      final invalid = switch (field) {
        'outstanding' || 'rowOverdue' || 'id' => {
          ...ledger,
          'items': [
            {
              ...row,
              if (field == 'outstanding') 'outstanding': 'غير متاح',
              if (field == 'rowOverdue') 'overdue': null,
              if (field == 'id') 'id': '',
            },
          ],
        },
        _ => {...ledger, field: 'غير متاح'},
      };
      final auth = await authenticate('AcademyOwner', (_, path, _, _) async {
        reads.add(path);
        return reads.length == 1 ? invalid : ledger;
      });
      await t.pumpWidget(
        app(
          Scaffold(
            body: ReceivableBalances(
              auth: auth,
              staff: true,
              filters: const {'branchId': 'b1', 'state': 'overdue'},
            ),
          ),
          scale: 2,
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('تعذر تحميل المطالبات في هذا النطاق.'), findsOneWidget);
      expect(find.text('إجمالي المستحقات'), findsNothing);
      expect(find.text('مطالبة تدريب'), findsNothing);
      await t.tap(find.text('إعادة المحاولة'));
      await t.pumpAndSettle();
      expect(find.text('ملخص رصيد الفرع'), findsOneWidget);
      expect(find.text('840 ج.م'), findsOneWidget);
      expect(
        t
            .widget<DropdownButton<String>>(find.byType(DropdownButton<String>))
            .value,
        'overdue',
      );
      expect(reads, List.filled(2, '/api/v1/finance/receivables?branchId=b1'));
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    });
  }

  testWidgets('staff ledger drops old totals when a branch is denied', (
    t,
  ) async {
    final denied = Completer<Object?>();
    final empty = {
      'asOf': '2026-11-07T12:00:00Z',
      'currency': 'EGP',
      'outstanding': '0.00',
      'overdue': '0.00',
      'notYetDue': '0.00',
      'overpaymentCredit': '0.00',
      'items': [],
    };
    final auth = await authenticate('AcademyOwner', (_, path, _, _) async {
      return switch (Uri.parse(path).queryParameters['branchId']) {
        'denied' => denied.future,
        'empty' => empty,
        _ => {...empty, 'outstanding': '340.00', 'overdue': '340.00'},
      };
    });
    Widget screen(String branch) => app(
      Scaffold(
        body: ReceivableBalances(
          auth: auth,
          staff: true,
          filters: {'branchId': branch, 'state': 'overdue'},
        ),
      ),
    );
    await t.pumpWidget(screen('first'));
    await t.pumpAndSettle();
    expect(find.text('340 ج.م'), findsWidgets);
    await t.pumpWidget(screen('denied'));
    expect(find.text('340 ج.م'), findsNothing);
    denied.completeError(const ApiFailure(403, 'الفرع خارج نطاق حسابك.'));
    await t.pumpAndSettle();
    expect(find.text('لا يمكن عرض هذه الصفحة'), findsOneWidget);
    expect(find.text('إجمالي المستحقات'), findsNothing);
    await t.pumpWidget(screen('empty'));
    await t.pumpAndSettle();
    expect(find.text('0 ج.م'), findsWidgets);
    expect(
      find.text('لا توجد مطالبات بهذه الحالة في النطاق المحدد.'),
      findsOneWidget,
    );
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
}
