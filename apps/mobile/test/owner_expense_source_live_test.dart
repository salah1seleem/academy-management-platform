import 'dart:async';
import 'dart:io';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_reports.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show MemoryStore;
import 'support/visibility.dart';
import 'v2_role_workspaces_test.dart' show app;

class _LiveReads extends HttpApiTransport {
  _LiveReads(super.config, this.ioZone);
  final Zone ioZone;
  final reads = <Uri>[];
  bool authenticated = false;

  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    if (authenticated) {
      expect(method, 'GET');
      reads.add(Uri.parse(path));
    }
    return ioZone.run(
      () => super.send(
        method,
        path,
        body: body,
        token: token,
        idempotencyKey: idempotencyKey,
      ),
    );
  }
}

void main() {
  const enabled = bool.fromEnvironment('QA_OWNER_EXPENSE_LIVE');
  const base = String.fromEnvironment('API_BASE_URL');
  TestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Flutter opens the real saved expense from the actual operating report',
    (t) async {
      expect(base, 'http://127.0.0.1:5495');
      t.view.physicalSize = const Size(390, 844);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.reset);
      final previousOverrides = HttpOverrides.current;
      addTearDown(() => HttpOverrides.global = previousOverrides);
      final auth = await t.runAsync(() async {
        HttpOverrides.global = null;
        final transport = _LiveReads(AppConfig('Demo', base), Zone.current);
        final session = AuthController(transport, MemoryStore())
          ..restoring = false;
        const phone = '01099900010';
        await session.verify(phone, await session.requestOtp(phone), '246810');
        expect(
          session.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        expect(session.session!.selected!.role, 'AcademyOwner');
        transport.authenticated = true;
        return (session, transport);
      });
      expect(auth, isNotNull);
      final (session, transport) = auth!;
      const branch = 'e716f000-761e-123b-adc2-9f7bc300ba9d';
      const expense = '03c0a6dd-afca-4de5-a0ae-539c24f8e3dd';
      const expenseRoot = '/api/v1/finance/operating-expenses';

      Future<void> waitFor(Finder target) async {
        for (var i = 0; i < 100 && target.evaluate().isEmpty; i++) {
          await t.runAsync(
            () => Future<void>.delayed(const Duration(milliseconds: 30)),
          );
          await t.pump(const Duration(milliseconds: 32));
        }
        await revealTestTarget(t, target);
      }

      await t.pumpWidget(
        app(
          OwnerReports(
            auth: session,
            initialReport: 'operating',
            initialFilters: const {
              'from': '2026-11-09',
              'to': '2026-11-09',
              'branchId': branch,
            },
          ),
        ),
      );
      final line = find.text('الصيانة والتشغيل');
      await waitFor(line);
      expect(
        t
            .widgetList<MoneyText>(find.byType(MoneyText))
            .any((amount) => amount.value == 123.45),
        isTrue,
      );
      await t.tap(line);
      await t.pump();
      final row = find.widgetWithText(GLink, money(123.45, 'EGP'));
      await waitFor(row);
      expect(
        transport.reads
            .singleWhere((p) => p.path == expenseRoot)
            .queryParameters,
        {
          'from': '2026-11-09',
          'to': '2026-11-09',
          'branchId': branch,
          'dateBasis': 'payment',
          'status': 'Posted',
          'paid': 'true',
          'category': 'Maintenance',
          'page': '1',
          'pageSize': '25',
        },
      );
      await t.tap(row);
      await t.pump();
      await waitFor(find.text('الأنصبة المحفوظة'));
      expect(
        find.text('إجمالي التوزيع ${money(123.45, 'EGP')}'),
        findsOneWidget,
      );
      expect(
        transport.reads.where((p) => p.path == '$expenseRoot/$expense'),
        hasLength(1),
      );
      await t.tap(find.byType(BackButton));
      await t.pumpAndSettle();
      expect(row, findsOneWidget);
      expect(transport.reads.where((p) => p.path == expenseRoot), hasLength(1));
      await t.tap(find.byType(BackButton));
      await t.pumpAndSettle();
      expect(find.text('الأرباح والخسائر'), findsOneWidget);
      await t.pumpWidget(const SizedBox());
      session.dispose();
    },
    skip: !enabled,
  );
}
