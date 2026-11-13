import 'dart:async';
import 'dart:io';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import '../test/auth_controller_test.dart' show MemoryStore;
import '../test/native_provider_quarantine_live_test.dart';

const _receipts = '/api/v1/guardian/subscriptions/receipts';
const _enabled = bool.fromEnvironment('QA_NATIVE_PROVIDER_LIVE');
const _players = [
  ('027f9af1-7ed0-4a70-ad6b-418d773356c9', 'PUB-027f9af17ed0'),
  ('039d3f77-4f40-4d6d-b128-365f933bffc0', 'PUB-039d3f774f40'),
];

class _ReturningFamily extends Appearance {
  @override
  Future<bool> hasRoleIntro(String role, String sportTheme) async => true;
}

class _FamilyTransport extends HttpApiTransport {
  _FamilyTransport(this.io) : super(AppConfig('Demo', 'http://127.0.0.1:5497'));

  final Zone io;
  final receipts = <(Uri, Json)>[];
  final writes = <String>[];

  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) => io.run(() async {
    if (method != 'GET' && !path.startsWith('/api/v1/mobile/auth/')) {
      writes.add('$method $path');
    }
    final response = await super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
    if (method == 'GET' && path.startsWith(_receipts)) {
      receipts.add((Uri.parse(path), obj(response)));
    }
    return response;
  });
}

Future<void> _waitFor(WidgetTester tester, Finder target) async {
  for (var index = 0; index < 430 && target.evaluate().isEmpty; index++) {
    await tester.runAsync(
      () => Future<void>.delayed(const Duration(milliseconds: 35)),
    );
    await tester.pump(const Duration(milliseconds: 50));
  }
  expect(target, findsWidgets);
  await tester.pump(const Duration(milliseconds: 350));
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('Guardian opens distinct issued receipts from the iOS picker', (
    tester,
  ) async {
    final previous = HttpOverrides.current;
    addTearDown(() => HttpOverrides.global = previous);
    final session = await tester.runAsync(() async {
      HttpOverrides.global = null;
      final transport = _FamilyTransport(Zone.current);
      addTearDown(transport.close);
      final auth = AuthController(transport, MemoryStore())..restoring = false;
      const phone = '01000000001';
      await auth.verify(phone, await auth.requestOtp(phone), '246810');
      expect(auth.session?.selected?.role, 'Guardian');
      return (auth, transport);
    });
    expect(session, isNotNull);
    final (auth, transport) = session!;
    await tester.pumpWidget(
      AcademyApp(
        auth: auth,
        environment: 'Demo',
        appearance: _ReturningFamily()..dark = false,
      ),
    );
    final home = find.byKey(const ValueKey('guardian-home-scroll'));
    await _waitFor(tester, home);
    final entry = find.widgetWithText(GLink, 'إيصالاتي');
    await tester.scrollUntilVisible(
      entry,
      350,
      scrollable: find
          .descendant(of: home, matching: find.byType(Scrollable))
          .first,
      maxScrolls: 30,
    );
    await tester.tap(entry.hitTestable());
    final menu = find.byKey(const ValueKey('receipt-child-menu'));
    await _waitFor(tester, menu);
    final all = transport.receipts.single.$2;
    expect(rows(all['children']), hasLength(159));
    final card = find
        .descendant(of: find.byType(ReceiptsPage), matching: find.byType(GCard))
        .first;
    expect(
      tester.getTopLeft(card).dy - tester.getBottomLeft(menu).dy,
      greaterThanOrEqualTo(8),
    );
    await binding.takeScreenshot('phase-iosreceipts-family-all');

    final opened = <String>{};
    for (final (index, player) in _players.indexed) {
      final (id, code) = player;
      final option = rows(all['children'])
          .singleWhere((child) => child['id'] == id);
      expect(option['arabicName'], 'لاعب اختبار نشر الباقات');
      expect(option['playerCode'], code);
      await tester.tap(menu);
      final choice = find.text(code).hitTestable();
      await _waitFor(tester, choice);
      await binding.takeScreenshot(
        'phase-iosreceipts-family-picker-${index + 1}',
      );
      await tester.tap(choice);
      await tester.pump();
      final marker = find.textContaining('\u2066$code\u2069');
      await _waitFor(tester, marker);
      final scoped = transport.receipts
          .lastWhere((read) => read.$1.queryParameters['playerId'] == id)
          .$2;
      final row = rows(scoped['items']).single;
      final number = row['receiptNumber'] as String;
      final receipt = find.text(number).hitTestable();
      expect(row['playerCode'], code);
      expect(row['amount'], 480);
      expect(opened.add(number), isTrue);
      await binding.takeScreenshot(
        'phase-iosreceipts-family-selected-${index + 1}',
      );
      await tester.tap(receipt);
      await _waitFor(tester, find.text('بتوقيت الأكاديمية'));
      final detail = transport.receipts
          .lastWhere((read) => read.$1.path == '$_receipts/${row['id']}')
          .$2;
      expect(detail['receiptNumber'], number);
      expect(detail['amount'], 480);
      expect(detail['paidAtAcademy'], startsWith('2026-09-28T10:15:'));
      final locale = MaterialLocalizations.of(
        tester.element(find.byType(ReceiptPage)),
      );
      final day = locale.formatShortDate(DateTime.utc(2026, 9, 28));
      final clock = locale.formatTimeOfDay(
        const TimeOfDay(hour: 10, minute: 15),
      );
      expect(
        find.textContaining('تاريخ ووقت الدفع: $day · $clock'),
        findsOneWidget,
      );
      await tester.pump(const Duration(milliseconds: 650));
      await binding.takeScreenshot(
        'phase-iosreceipts-family-issued-${index + 1}',
      );
      final reads = transport.receipts.length;
      await tester.tap(find.byTooltip(locale.backButtonTooltip).last);
      await _waitFor(tester, receipt);
      expect(
        tester.widget<DropdownButtonFormField<String>>(menu).initialValue,
        id,
      );
      expect(transport.receipts, hasLength(reads));
    }
    expect(transport.writes, isEmpty);
    expect(tester.takeException(), isNull);
    debugPrint(
      'iOS guardian receipts $opened, 480 each, zone ${DateTime.now().timeZoneOffset}, no writes',
    );
    await tester.pumpWidget(const SizedBox.shrink());
    auth.dispose();
  }, skip: !_enabled);

  runNativeProviderQuarantineAcceptance(
    capture: (name) async {
      await binding.takeScreenshot('phase-iosreceipts-owner-$name');
    },
  );
}
