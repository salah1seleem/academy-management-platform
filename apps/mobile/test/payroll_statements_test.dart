import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/shared/payroll_statements.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'coach_test.dart' show coach, shell;
import 'support/visibility.dart';

void main() {
  testWidgets(
    'refresh and app resume replace approved dues with the latest verified read',
    (t) async {
      var reply = Completer<Object?>();
      var reads = 0;
      Map<String, dynamic> account(num paid, num remaining) => {
        'configured': true,
        'currency': 'EGP',
        'asOf': '2026-11-13',
        'currentMonthApproved': true,
        'totalDue': 2924.75,
        'totalPaid': paid,
        'remaining': remaining,
        'items': [
          {
            'month': '2026-11-01',
            'netDue': 2924.75,
            'paid': paid,
            'remaining': remaining,
            'baseDue': 3000,
            'deductions': 75.25,
            'payments': [],
          },
        ],
      };
      final auth = await coach((method, path, body, token) async {
        expect('$method $path', 'GET /api/v1/staff/payroll/account');
        reads++;
        return reply.future;
      });
      await t.pumpWidget(shell(StaffPayrollStatements(auth: auth)));
      reply.complete(account(0, 2924.75));
      await t.pumpAndSettle();
      expect(find.text('المتبقي المعروف: 2,924.75 ج.م'), findsOneWidget);
      reply = Completer<Object?>();
      for (final state in ['paused', 'resumed']) {
        await t.binding.defaultBinaryMessenger.handlePlatformMessage(
          'flutter/lifecycle',
          const StringCodec().encodeMessage('AppLifecycleState.$state'),
          (_) {},
        );
      }
      await t.pump();
      expect(find.text('المتبقي المعروف: 2,924.75 ج.م'), findsNothing);
      reply.complete(account(500.25, 2424.50));
      await t.pumpAndSettle();
      expect(find.text('المتبقي المعروف: 2,424.50 ج.م'), findsOneWidget);
      reply = Completer<Object?>();
      await t.tap(find.text('تحديث المستحقات'));
      await t.pump();
      reply.completeError(const ApiFailure(503, 'تعذر تحميل المستحقات.'));
      await t.pumpAndSettle();
      expect(find.text('المتبقي المعروف: 2,424.50 ج.م'), findsNothing);
      expect(find.text('تعذر تحميل المستحقات.'), findsOneWidget);
      reply = Completer<Object?>();
      await t.tap(find.text('إعادة المحاولة'));
      await t.pump();
      reply.complete(account(2924.75, 0));
      await t.pumpAndSettle();
      expect(find.text('المتبقي المعروف: 0 ج.م'), findsOneWidget);
      reply = Completer<Object?>();
      await t.tap(find.text('تحديث المستحقات'));
      await t.pump();
      await auth.logout();
      reply.complete(account(2924.75, 0));
      await t.pumpAndSettle();
      expect(reads, 5);
      expect(find.text('المتبقي المعروف: 0 ج.م'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Opening signed corrections are included once and are read-only',
    (tester) async {
      final calls = <String>[];
      final auth = await coach((m, p, b, t) async {
        calls.add('$m $p');
        return {
          'configured': true,
          'currency': 'EGP',
          'items': [],
          'totalDue': 1300,
          'totalPaid': 500,
          'remaining': 800,
          'opening': {
            'asOf': '2026-10-01',
            'originalAmount': 1200,
            'amount': 1300,
            'paid': 500,
            'remaining': 800,
            'payments': [
              {'amount': 300, 'kind': 'DirectPayout'},
              {'amount': 200, 'kind': 'AdvanceApplication'},
            ],
            'corrections': [
              {'delta': -200, 'targetAmount': 1000},
              {'delta': 300, 'targetAmount': 1300},
            ],
          },
        };
      });
      await tester.pumpWidget(shell(StaffPayrollStatements(auth: auth)));
      await tester.pumpAndSettle();
      expect(find.text('أصل الافتتاح: 1,200 ج.م'), findsOneWidget);
      expect(find.text('الرصيد القديم: 1,300 ج.م'), findsOneWidget);
      expect(
        find.textContaining('تصحيح موثق — داخل الإجمالي وليس صرفًا'),
        findsNWidgets(2),
      );
      expect(find.text('المتبقي المعروف: 800 ج.م'), findsOneWidget);
      expect(find.byType(TextFormField), findsNothing);
      expect(find.byType(FilledButton), findsNothing);
      expect(calls, ['GET /api/v1/staff/payroll/account']);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'Post-contract compensation is a separate own payable, not salary or a coach payment action',
    (tester) async {
      final calls = <String>[];
      final auth = await coach((m, p, b, t) async {
        calls.add('$m $p');
        return {
          'configured': true,
          'asOf': '2026-10-15',
          'currency': 'EGP',
          'items': [],
          'totalDue': 50,
          'totalPaid': 20,
          'remaining': 30,
          'finalSettlements': [
            {
              'id': 'final',
              'amount': 50,
              'paid': 20,
              'remaining': 30,
              'payments': [
                {'amount': 20},
              ],
            },
          ],
        };
      });
      await tester.pumpWidget(shell(StaffPayrollStatements(auth: auth)));
      await tester.pumpAndSettle();
      expect(
        find.text('عمولة بعد نهاية العقد — ليست راتب شهر'),
        findsOneWidget,
      );
      expect(find.text('المتبقي المعروف: 30 ج.م'), findsOneWidget);
      expect(find.text('المستحق: 50 ج.م'), findsOneWidget);
      expect(find.text('صرف مسجل: 20 ج.م'), findsOneWidget);
      expect(find.byType(TextFormField), findsNothing);
      expect(find.byType(FilledButton), findsNothing);
      expect(calls, ['GET /api/v1/staff/payroll/account']);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'Reviewed opening zero is visible and is not a fabricated monthly salary',
    (tester) async {
      final calls = <String>[];
      final auth = await coach((m, p, b, t) async {
        calls.add('$m $p');
        return {
          'configured': true,
          'asOf': '2026-10-15',
          'currency': 'EGP',
          'items': [],
          'totalDue': 0,
          'totalPaid': 0,
          'remaining': 0,
          'priorRemaining': 0,
          'currentRemaining': 0,
          'openingBalanceVerified': true,
          'opening': {
            'asOf': '2026-10-01',
            'amount': 0,
            'paid': 0,
            'remaining': 0,
            'payments': [],
          },
        };
      });
      await tester.pumpWidget(shell(StaffPayrollStatements(auth: auth)));
      await tester.pumpAndSettle();
      expect(find.text('افتتاح موثق'), findsOneWidget);
      expect(find.text('افتتاح موثق'), findsOneWidget);
      expect(find.text('الرصيد القديم: 0 ج.م'), findsOneWidget);
      expect(find.textContaining('لا كشف معتمد بعد'), findsNothing);
      expect(find.byType(TextFormField), findsNothing);
      expect(find.byType(FilledButton), findsNothing);
      expect(calls, ['GET /api/v1/staff/payroll/account']);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'Own advance before any approved earnings is visible without a false salary or payout',
    (tester) async {
      final auth = await coach(
        (m, p, b, t) async => {
          'configured': true,
          'asOf': '2026-10-15',
          'currency': 'EGP',
          'items': [],
          'recordedAdvanceTotal': 750,
          'unallocatedAdvances': 750,
          'advanceApplied': 0,
        },
      );
      await tester.pumpWidget(shell(StaffPayrollStatements(auth: auth)));
      await tester.pumpAndSettle();
      expect(find.text('سبق صرفها: 750 ج.م'), findsOneWidget);
      expect(find.text('غير المخصص: 750 ج.م'), findsOneWidget);
      expect(find.textContaining('تخصيص السلفة يسدد'), findsOneWidget);
      expect(find.byType(TextFormField), findsNothing);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets('An applied advance settles the month as a distinct entry', (
    tester,
  ) async {
    final auth = await coach(
      (m, p, b, t) async => {
        'configured': true,
        'currency': 'EGP',
        'totalDue': 3600,
        'totalPaid': 2500,
        'remaining': 1100,
        'recordedAdvanceTotal': 750,
        'unallocatedAdvances': 550,
        'advanceApplied': 200,
        'items': [
          {
            'month': '2026-11-01',
            'netDue': 3600,
            'paid': 2500,
            'remaining': 1100,
            'baseDue': 3600,
            'payments': [
              {'amount': 2300, 'kind': 'DirectPayout'},
              {'amount': 200, 'kind': 'AdvanceApplication'},
            ],
          },
        ],
      },
    );
    await tester.pumpWidget(shell(StaffPayrollStatements(auth: auth)));
    await tester.pumpAndSettle();
    expect(find.text('المتبقي: 1,100 ج.م'), findsOneWidget);
    expect(find.text('صرف مسجل: 2,300 ج.م'), findsOneWidget);
    expect(find.text('تسوية من سلفة سابقة: 200 ج.م'), findsOneWidget);
    await revealTestTarget(tester, find.text('السلف المسجلة'));
    expect(find.text('المخصص من سلف سابقة: 200 ج.م'), findsOneWidget);
    expect(find.text('غير المخصص: 550 ج.م'), findsOneWidget);
  });

  testWidgets(
    'Own approved monthly dues show 3600 earned, 2500 paid, 1100 remaining with no mutation',
    (tester) async {
      final calls = <String>[];
      final auth = await coach((m, p, b, t) async {
        calls.add('$m $p');
        return {
          'configured': true,
          'asOf': '2026-10-15',
          'currency': 'EGP',
          'totalDue': 6600,
          'totalPaid': 3500,
          'remaining': 3100,
          'priorRemaining': 2000,
          'currentRemaining': 1100,
          'currentMonthApproved': true,
          'items': [
            {
              'month': '2026-10-01',
              'currency': 'EGP',
              'netDue': 3600,
              'paid': 2500,
              'remaining': 1100,
              'baseDue': 3000,
              'deductions': 0,
              'additions': 600,
              'payments': [
                {'amount': 2500},
              ],
            },
            {
              'month': '2026-09-01',
              'netDue': 3000,
              'paid': 1000,
              'remaining': 2000,
              'baseDue': 3000,
              'deductions': 0,
              'additions': 0,
              'payments': [],
            },
          ],
        };
      });
      await tester.pumpWidget(shell(StaffPayrollStatements(auth: auth)));
      await tester.pumpAndSettle();
      expect(find.text('المستحق: 3,600 ج.م'), findsOneWidget);
      expect(find.text('المسدد: 2,500 ج.م'), findsOneWidget);
      expect(find.text('المتبقي: 1,100 ج.م'), findsOneWidget);
      expect(find.text('المتبقي المعروف: 3,100 ج.م'), findsOneWidget);
      expect(find.text('من أشهر سابقة: 2,000 ج.م'), findsOneWidget);
      expect(find.text('أكتوبر ٢٠٢٦'), findsOneWidget);
      expect(find.text('السلف المسجلة'), findsNothing);
      expect(find.byType(TextFormField), findsNothing);
      expect(find.byType(FilledButton), findsNothing);
      expect(calls, ['GET /api/v1/staff/payroll/account']);
      expect(tester.takeException(), isNull);
    },
  );
  testWidgets(
    'No approved statement does not imply zero wages or a verified opening',
    (tester) async {
      final auth = await coach(
        (m, p, b, t) async => {'configured': true, 'items': []},
      );
      await tester.pumpWidget(shell(StaffPayrollStatements(auth: auth)));
      await tester.pumpAndSettle();
      expect(
        find.textContaining('هذا لا يعني أن مستحقاتك صفر'),
        findsOneWidget,
      );
      expect(find.text('حساب مستحقاتك'), findsNothing);
      expect(find.text('نطاق الكشف'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets('A coach without a linked employee sees only the link state', (
    tester,
  ) async {
    final auth = await coach((m, p, b, t) async => {'configured': false});
    await tester.pumpWidget(shell(StaffPayrollStatements(auth: auth)));
    await tester.pumpAndSettle();
    expect(find.text('لم يربط سجل الموظف بحسابك بعد.'), findsOneWidget);
    expect(find.text('حساب مستحقاتك'), findsNothing);
    expect(find.text('نطاق الكشف'), findsNothing);
  });

  testWidgets(
    '320 wide at 200% keeps decimal amounts, advance allocation and scope readable',
    (tester) async {
      tester.view.physicalSize = const Size(320, 844);
      tester.view.devicePixelRatio = 1;
      tester.platformDispatcher.textScaleFactorTestValue = 2;
      addTearDown(tester.view.reset);
      addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);
      final auth = await coach(
        (m, p, b, t) async => {
          'configured': true,
          'asOf': '2026-11-09',
          'currency': 'EGP',
          'totalDue': 3600,
          'totalPaid': 2500.55,
          'remaining': 1099.45,
          'currentMonthApproved': true,
          'recordedAdvanceTotal': 0,
          'items': [
            {
              'month': '2026-11-01',
              'netDue': 3600,
              'paid': 2500.55,
              'remaining': 1099.45,
              'baseDue': 3625.75,
              'deductions': 25.75,
              'additions': 0,
              'payments': [
                {'amount': 500.25, 'kind': 'AdvanceApplication'},
                {'amount': 2000.30, 'kind': 'DirectPayout'},
              ],
            },
          ],
        },
      );

      final opened = find.text('افتح المستحقات');
      await tester.pumpWidget(
        shell(
          Builder(
            builder: (context) => Scaffold(
              body: Center(
                child: TextButton(
                  onPressed: () => Navigator.of(context).push<void>(
                    MaterialPageRoute(
                      builder: (context) => StaffPayrollStatements(auth: auth),
                    ),
                  ),
                  child: const Text('افتح المستحقات'),
                ),
              ),
            ),
          ),
        ),
      );
      await tester.tap(opened);
      await tester.pumpAndSettle();
      expect(find.text('المتبقي المعروف: 1,099.45 ج.م'), findsOneWidget);
      await revealTestTarget(tester, find.text('الخصومات: 25.75 ج.م'));
      await revealTestTarget(
        tester,
        find.text('تسوية من سلفة سابقة: 500.25 ج.م'),
      );
      await revealTestTarget(tester, find.text('صرف مسجل: 2,000.30 ج.م'));
      final scope = find.text('نطاق الكشف');
      await revealTestTarget(tester, scope);
      expect(find.textContaining('تحتاج توثيقًا منفصلًا'), findsNothing);
      await tester.tap(scope);
      await tester.pumpAndSettle();
      await revealTestTarget(tester, find.textContaining('تحتاج توثيقًا منفصلًا'));
      expect(tester.takeException(), isNull);
      await tester.tap(find.byType(BackButton));
      await tester.pumpAndSettle();
      expect(opened, findsOneWidget);
    },
  );
}
