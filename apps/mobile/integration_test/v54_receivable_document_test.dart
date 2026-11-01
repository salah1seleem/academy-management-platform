import '../test/introduction_test_helpers.dart';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_reports.dart';
import 'package:academy_mobile/features/shared/receivable_balances.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'support/receivable_fixture.dart';

class ReceivableAcceptanceTransport extends HttpApiTransport {
  ReceivableAcceptanceTransport(super.config);
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    final prepare = const bool.fromEnvironment('PREPARE_RECEIVABLE_DEMO');
    if (method != 'GET' &&
        !path.startsWith('/api/v1/mobile/auth/') &&
        !(prepare &&
            method == 'POST' &&
            (path == '/api/v1/finance/receivables/' ||
                RegExp(r'^/api/v1/finance/receivables/[a-f0-9-]{36}/movements$')
                    .hasMatch(path)))) {
      throw StateError(
        'Only explicit receivable demo preparation is allowed; no new payments.',
      );
    }
    return super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Real owner and linked family receivables trace 1040 outstanding and separate 100 credit without new collections',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final controllers = <AuthController>[];
      Future<AuthController> restore(String key, String role) async {
        final auth = AuthController(
          ReceivableAcceptanceTransport(AppConfig('Demo', base)),
          SecureRefreshStore(key),
        );
        controllers.add(auth);
        await auth.restore();
        expect(auth.session!.selected!.role, role);
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return auth;
      }

      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          if (find.text('تخطي').evaluate().isNotEmpty) {
            await finishRoleIntroduction(t);
          }
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> scroll(Finder f) async {
        await t.scrollUntilVisible(
          f,
          200,
          scrollable: find
              .descendant(
                of: find.byType(ListView).last,
                matching: find.byType(Scrollable),
              )
              .first,
          maxScrolls: 80,
        );
        await t.pumpAndSettle();
      }

      try {
        final owner = await restore(
          'v47.5278.decision-assignment.owner10',
          'AcademyOwner',
        );
        if (const bool.fromEnvironment('PREPARE_RECEIVABLE_DEMO')) {
          final fixture = await prepareReceivableFixture(owner);
          debugPrint(
            'RECEIVABLE_DEMO_IDS=${fixture['overdueId']}/${fixture['futureId']}/${fixture['creditId']}',
          );
        }
        final summary = obj(
          await owner.request(
            'GET',
            '/api/v1/finance/receivables?playerId=$receivableDemoPlayer',
          ),
        );
        expect(summary['outstanding'], '1040.00');
        expect(summary['overdue'], '540.00');
        expect(summary['notYetDue'], '500.00');
        expect(summary['overpaymentCredit'], '100.00');
        final credit = rows(summary['items'])
            .singleWhere((r) => textOf(r['outstanding']).startsWith('-'));
        final doc = obj(
          await owner.request(
            'GET',
            '/api/v1/finance/receivables/${credit['id']}',
          ),
        );
        expect(obj(doc['balance'])['outstanding'], '-100.00');
        expect(rows(doc['movements']).length, 2);
        expect(
          rows(doc['movements']).singleWhere(
            (m) => m['kind'] == 'PaymentAllocation',
          )['collectionId'],
          receivableDemoCollection,
        );
        await t.pumpWidget(
          AcademyApp(
            auth: owner,
            environment: 'Demo',
            appearance: Appearance()..dark = false,
          ),
        );
        await wait(find.text('التقارير'));
        await t.tap(find.text('التقارير'));
        await wait(find.byType(OwnerReports));
        await wait(find.text('التقارير المالية'));
        await t.tap(find.text('التقارير المالية'));
        await wait(find.text('المستحقات المالية'));
        await t.tap(find.text('المستحقات المالية'));
        await wait(find.text('إجمالي المستحقات'));
        expect(find.text('1,040 ج.م'), findsOneWidget);
        final summaryCard = find
            .ancestor(
              of: find.text('إجمالي المستحقات'),
              matching: find.byType(GCard),
            )
            .first;
        expect(
          find.descendant(of: summaryCard, matching: find.text('540 ج.م')),
          findsOneWidget,
        );
        expect(
          find.descendant(of: summaryCard, matching: find.text('500 ج.م')),
          findsOneWidget,
        );
        expect(find.byTooltip('مسح الفلاتر'), findsNothing);
        await binding.takeScreenshot(
          'phase-approved-owner-receivable-summary-light',
        );
        await scroll(find.text(textOf(credit['description'])));
        await t.tap(find.text(textOf(credit['description'])));
        await wait(find.byType(ReceivableDocumentPage));
        await wait(find.text('رصيد لصالح العميل'));
        expect(
          t.widget<MoneyText>(find.byType(MoneyText).first).value,
          '-100.00',
        );
        await binding.takeScreenshot(
          'phase-approved-owner-receivable-credit-document',
        );
        await scroll(find.text(receivableDemoCollection));
        await binding.takeScreenshot(
          'phase-approved-owner-receivable-source-movements',
        );
        final family = await restore(
          'v46.urgent-summary.guardian0',
          'Guardian',
        );
        final other = await restore('v46.urgent-summary.guardian1', 'Guardian');
        final own = obj(
          await family.request('GET', '/api/v1/guardian/receivables'),
        );
        final unlinked = obj(
          await other.request('GET', '/api/v1/guardian/receivables'),
        );
        expect(own['outstanding'], '1040.00');
        expect(own['overpaymentCredit'], '100.00');
        expect(
          rows(unlinked['items']).any(
            (r) => textOf(r['description']).startsWith(receivableDemoMarker),
          ),
          false,
        );
        try {
          await other.request(
            'GET',
            '/api/v1/finance/receivables/${credit['id']}',
          );
          fail('Guardian cannot read staff financial document');
        } on ApiFailure catch (e) {
          expect(e.status, 403);
        }
        await t.pumpWidget(
          AcademyApp(
            key: const ValueKey('family-balances'),
            auth: family,
            environment: 'Demo',
            appearance: Appearance()..dark = false,
          ),
        );
        await wait(find.byKey(const ValueKey('guardian-home-scroll')));
        await t.scrollUntilVisible(
          find.text('أرصدة الأسرة'),
          400,
          scrollable: find
              .descendant(
                of: find.byKey(const ValueKey('guardian-home-scroll')),
                matching: find.byType(Scrollable),
              )
              .first,
          maxScrolls: 100,
        );
        await t.pumpAndSettle();
        final familyAction = find.text('أرصدة الأسرة');
        if (familyAction.hitTestable().evaluate().isEmpty) {
          await t.drag(
            find
                .descendant(
                  of: find.byKey(const ValueKey('guardian-home-scroll')),
                  matching: find.byType(Scrollable),
                )
                .first,
            const Offset(0, -200),
          );
          await t.pumpAndSettle();
        }
        await binding.takeScreenshot(
          'phase-approved-guardian-receivable-footer-link',
        );
        expect(familyAction.hitTestable(), findsOneWidget);
        await t.tap(familyAction);
        await wait(find.text('إجمالي المستحقات'));
        expect(find.text('1,040 ج.م'), findsOneWidget);
        expect(find.text('عرض المستند وحركاته'), findsNothing);
        await binding.takeScreenshot(
          'phase-approved-guardian-receivable-own-summary',
        );
        final after = obj(
          await owner.request(
            'GET',
            '/api/v1/reports/financial?allDates=true&pageSize=25',
          ),
        );
        expect(after['totalCount'], 73);
        expect(after['netAmount'], 61734);
      } finally {
        for (final auth in controllers) {
          auth.dispose();
        }
      }
    },
  );
}
