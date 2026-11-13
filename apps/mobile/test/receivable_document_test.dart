import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_reports.dart';
import 'package:academy_mobile/features/shared/receivable_balances.dart';
import 'package:academy_mobile/features/shared/experience_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

Json balance() => {
  'id': 'document',
  'player': 'لاعب مستحقات اصطناعي',
  'description': 'مستند اصطناعي مسدد ثم مصحح',
  'currency': 'EGP',
  'charge': '200.00',
  'allocated': '200.00',
  'credits': '100.00',
  'creditUsed': '0.00',
  'creditReceived': '0.00',
  'outstanding': '-100.00',
  'branch': 'مدينة نصر',
  'dueDate': '2026-10-01',
  'overdue': false,
  'source': 'ManualCharge',
};
Json document() => {
  'asOf': '2026-10-22T08:00:00Z',
  'document': {'id': 'document'},
  'balance': balance(),
  'movements': [
    {
      'kind': 'PaymentAllocation',
      'amount': '200.00',
      'currency': 'EGP',
      'collectionId': 'confirmed-collection',
      'reason': 'تخصيص من تحصيل محفوظ',
      'createdAtUtc': '2026-10-22T08:00:00Z',
    },
    {
      'kind': 'CreditNote',
      'amount': '100.00',
      'currency': 'EGP',
      'reason': 'تصحيح المستند بعد سداد',
      'createdAtUtc': '2026-10-22T08:00:00Z',
    },
  ],
  'creditApplications': [],
};

void main() {
  testWidgets(
    'Receivables preserve snapshot basis and branch, omit ineffective period controls and open exact document',
    (t) async {
      final calls = <String>[];
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        expect(m, 'GET');
        calls.add(p);
        if (p.contains('/academy/calendar')) {
          return {'today': '2026-10-22', 'trailingMonthFrom': '2026-09-22'};
        }
        if (p.contains('/structure/options')) {
          return {
            'branches': [
              {'id': 'branch', 'arabicName': 'مدينة نصر'},
            ],
          };
        }
        if (p == '/api/v1/finance/receivables/document') return document();
        return {
          'asOf': '2026-10-22T08:00:00Z',
          'currency': 'EGP',
          'outstanding': '0.00',
          'overdue': '0.00',
          'notYetDue': '0.00',
          'overpaymentCredit': '100.00',
          'items': [balance()],
        };
      });
      await t.pumpWidget(
        app(
          OwnerReports(
            auth: auth,
            initialReport: 'receivables',
            initialFilters: const {
              'from': '2026-09-22',
              'to': '2026-10-22',
              'branchId': 'branch',
            },
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.byType(ScopeDateTiles), findsNothing);
      expect(calls.last, '/api/v1/finance/receivables?branchId=branch');
      final date = MaterialLocalizations.of(
        t.element(find.byType(ReceivableBalances)),
      ).formatShortDate(DateTime.utc(2026, 10, 22));
      expect(find.textContaining('رصيد حتى $date'), findsOneWidget);
      await t.scrollUntilVisible(
        find.text('مستند اصطناعي مسدد ثم مصحح'),
        150,
        scrollable: find.byType(Scrollable).last,
      );
      await t.tap(find.text('مستند اصطناعي مسدد ثم مصحح'));
      await t.pumpAndSettle();
      expect(calls.last, '/api/v1/finance/receivables/document');
      expect(t.widget<MoneyText>(find.byType(MoneyText).first).value, 100);
      expect(find.text('رصيد لصالح العميل'), findsOneWidget);
      await t.scrollUntilVisible(
        find.text('مرجع التحصيل المخصص').hitTestable(),
        200,
        scrollable: find
            .descendant(
              of: find.byType(ListView).last,
              matching: find.byType(Scrollable),
            )
            .first,
      );
      expect(find.text('تخصيص تحصيل محفوظ'), findsOneWidget);
      await t.tap(find.text('مرجع التحصيل المخصص'));
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('confirmed-collection').hitTestable(),
        120,
        scrollable: find
            .descendant(
              of: find.byType(ListView).last,
              matching: find.byType(Scrollable),
            )
            .first,
      );
      expect(find.text('confirmed-collection'), findsOneWidget);
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('confirmed-collection'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  for (final mode in ['mismatch', 'forbidden']) {
    testWidgets('Document $mode never displays unauthorized financial source', (
      t,
    ) async {
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        if (mode == 'forbidden') {
          throw const ApiFailure(403, 'ليس لديك إذن لعرض المستند.');
        }
        return {
          ...document(),
          'document': {'id': 'another-document'},
        };
      });
      await t.pumpWidget(
        app(ReceivableDocumentPage(auth: auth, id: 'document')),
      );
      await t.pumpAndSettle();
      expect(find.byType(MoneyText), findsNothing);
      expect(find.text('لاعب مستحقات اصطناعي'), findsNothing);
      expect(
        find.text(
          mode == 'forbidden'
              ? 'ليس لديك إذن لعرض المستند.'
              : 'لم تتأكد هوية مستند المستحقات. أعد فتحه من القائمة.',
        ),
        findsOneWidget,
      );
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    });
  }
  testWidgets(
    'Guardian balances retain family summary without staff document action',
    (t) async {
      await t.pumpWidget(
        app(
          Scaffold(
            body: ReceivableBalanceContent(
              data: {
                'currency': 'EGP',
                'outstanding': '540.00',
                'overdue': '540.00',
                'notYetDue': '0.00',
                'overpaymentCredit': '0.00',
                'items': [balance()],
              },
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      await t.scrollUntilVisible(find.text('مستند اصطناعي مسدد ثم مصحح'), 150);
      expect(find.text('عرض المستند وحركاته'), findsNothing);
      expect(t.takeException(), isNull);
    },
  );
}
