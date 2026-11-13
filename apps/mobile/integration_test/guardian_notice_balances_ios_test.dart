import 'dart:async';
import 'dart:io';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import '../test/auth_controller_test.dart' show MemoryStore;

const _api = '/api/v1/guardian/receivables';
const _notice = String.fromEnvironment('QA_BALANCE_NOTICE');

class _ReturningFamily extends Appearance {
  @override
  Future<bool> hasRoleIntro(String role, String sportTheme) async => true;
}

class _Transport extends HttpApiTransport {
  _Transport(this.io) : super(AppConfig('Demo', 'http://127.0.0.1:5417'));
  final Zone io;
  final writes = <String>[];
  Json? ledger;

  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    if (method != 'GET' && !path.startsWith('/api/v1/mobile/auth/')) {
      writes.add('$method $path');
    }
    final completion = Completer<Object?>();
    io.run(
      () => super
          .send(
            method,
            path,
            body: body,
            token: token,
            idempotencyKey: idempotencyKey,
          )
          .then<void>(completion.complete, onError: completion.completeError),
    );
    final response = await completion.future;
    if (path == _api) ledger = obj(response);
    return response;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'opens a saved family reminder and reads current balances on iOS',
    (tester) async {
      expect(_notice, matches(RegExp(r'^[0-9a-f-]{36}$')));
      final previous = HttpOverrides.current;
      addTearDown(() => HttpOverrides.global = previous);
      final session = await tester.runAsync(() async {
        HttpOverrides.global = null;
        final transport = _Transport(Zone.current);
        addTearDown(transport.close);
        final auth = AuthController(transport, MemoryStore())
          ..restoring = false;
        addTearDown(auth.dispose);
        const phone = '01099900100';
        await auth.verify(phone, await auth.requestOtp(phone), '246810');
        expect(auth.session?.selected?.role, 'Guardian');
        final inbox = obj(
          await auth.request('GET', '/api/v1/notifications?page=1'),
        );
        final notice = rows(inbox['items'])
            .singleWhere((item) => item['id'] == _notice);
        expect(notice['targetKind'], 'Receivables');
        expect(notice['targetId'], isNull);
        expect(notice['message'], contains('215.75 جنيه'));
        return (auth, transport);
      });
      final (auth, transport) = session!;

      Future<void> waitFor(Finder target) async {
        for (
          var attempt = 0;
          attempt < 240 && target.evaluate().isEmpty;
          attempt++
        ) {
          await tester.runAsync(
            () => Future<void>.delayed(const Duration(milliseconds: 35)),
          );
          await tester.pump(const Duration(milliseconds: 45));
        }
        expect(target, findsWidgets);
        await tester.pump(const Duration(milliseconds: 350));
      }

      await tester.pumpWidget(
        AcademyApp(
          auth: auth,
          environment: 'Demo',
          appearance: _ReturningFamily(),
          entryFrameReady: Future<void>.value(),
        ),
      );
      final bell = find.byType(NotificationBell);
      await waitFor(bell);
      await tester.tap(bell.hitTestable());
      final notice = find.byKey(ValueKey('notice-$_notice'));
      await waitFor(notice);
      final open = find.descendant(
        of: notice,
        matching: find.text('كشف المستحقات'),
      );
      await tester.ensureVisible(open);
      await binding.takeScreenshot('phase-familybalance-inbox');
      await tester.tap(open.hitTestable());
      await waitFor(find.text('أرصدة الأسرة'));
      await waitFor(find.text('إجمالي المستحقات'));
      expect(find.text('أُقفل الرصيد'), findsWidgets);
      final help = find.text('كيف تُحسب المستحقات؟');
      final explanation = find.textContaining('أرصدة مستندات مثبتة');
      expect(explanation, findsNothing);
      await tester.tap(help.hitTestable());
      await tester.pumpAndSettle();
      expect(explanation, findsOneWidget);
      await tester.tap(help.hitTestable());
      await tester.pumpAndSettle();
      expect(explanation, findsNothing);
      expect(transport.ledger?['outstanding'], '0.00');
      expect(transport.ledger?['overdue'], '0.00');
      expect(
        rows(transport.ledger!['items'])
            .where((row) => row['outstanding'] == '0.00'),
        hasLength(2),
      );
      await binding.takeScreenshot('phase-familybalance-settled');
      final back = find.byTooltip('رجوع').hitTestable();
      expect(back, findsOneWidget);
      await tester.tap(back);
      await waitFor(notice);
      expect(transport.writes, isEmpty);
      expect(tester.takeException(), isNull);
      debugPrint(
        'QA family reminder: 215.75 recorded, zero current, two settled, no writes.',
      );
    },
  );
}
