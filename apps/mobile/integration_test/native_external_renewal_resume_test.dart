import 'dart:async';
import 'dart:io';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import '../test/auth_controller_test.dart' show MemoryStore;

const _base = '/api/v1/guardian/subscriptions';
const _external = '$_base/external';
const _player = 'عمر أحمد حسن';
const _url = 'http://127.0.0.1:5500';

class _ReturningGuardian extends Appearance {
  @override
  Future<bool> hasRoleIntro(String role, String sportTheme) async => true;
}

class _ConnectedTransport extends HttpApiTransport {
  _ConnectedTransport(this.io) : super(AppConfig('Demo', _url));
  final Zone io;
  final writes = <String>[];
  final paymentReads = <String>[];

  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    final answer = Completer<Object?>();
    if (path == '$_external/renewals') writes.add(path);
    if (method == 'GET' && path.startsWith('$_base/payments/')) {
      paymentReads.add(path);
    }
    io.run(() async {
      try {
        answer.complete(
          await super.send(
            method,
            path,
            body: body,
            token: token,
            idempotencyKey: idempotencyKey,
          ),
        );
      } catch (error, trace) {
        answer.completeError(error, trace);
      }
    });
    return answer.future;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'guardian reopens the same external checkout and another account cannot follow it',
    (t) async {
      expect(const bool.fromEnvironment('QA_ISOLATED_EXTERNAL_RESUME'), isTrue);
      final overrides = HttpOverrides.current;
      addTearDown(() => HttpOverrides.global = overrides);
      final setup = (await t.runAsync(() async {
        HttpOverrides.global = null;
        final api = _ConnectedTransport(Zone.current);
        addTearDown(api.close);
        Future<AuthController> login(String phone) async {
          final auth = AuthController(api, MemoryStore())..restoring = false;
          addTearDown(auth.dispose);
          await auth.verify(phone, await auth.requestOtp(phone), '246810');
          expect(auth.session?.selected?.role, 'Guardian');
          expect(
            auth.session?.selected?.academyId,
            '10000000-0000-0000-0000-000000000001',
          );
          return auth;
        }

        final first = await login('01000000001');
        final choices = rows(
          await first.request(
            'POST',
            '$_external/search',
            body: {'name': _player},
          ),
        );
        final reference = choices.single['reference'];
        final selected = obj(
          await first.request(
            'POST',
            '$_external/resolve',
            body: {'reference': reference},
          ),
        );
        final plan = rows(selected['plans'])
            .firstWhere((row) => row['price'] == 900);
        final open = selected['openRenewal'] as Json?;
        final id = open?['kind'] == 'Payment'
            ? open!['paymentId'] as String
            : obj(
                    await first.request(
                      'POST',
                      '$_external/renewals',
                      body: {
                        'reference': reference,
                        'subscriptionPlanId': plan['id'],
                      },
                      idempotencyKey: commandKey(),
                    ),
                  )['paymentId']
                  as String;
        final reopened = await login('01000000001');
        final other = await login('01000000002');
        final payment = obj(
          await reopened.request('GET', '$_base/payments/$id'),
        );
        expect(payment['status'], 'Pending');
        expect(payment['amount'], 900);
        await expectLater(
          other.request('GET', '$_base/payments/$id'),
          throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 404)),
        );
        return (api, reopened, other, id);
      }))!;
      final (api, payer, other, paymentId) = setup;
      final initialWrites = api.writes.length;

      Future<void> waitFor(Finder finder) async {
        for (var i = 0; i < 240 && finder.evaluate().isEmpty; i++) {
          await t.runAsync(
            () => Future<void>.delayed(const Duration(milliseconds: 35)),
          );
          await t.pump(const Duration(milliseconds: 45));
        }
        expect(finder, findsWidgets);
      }

      Future<void> tap(Finder finder) async {
        await waitFor(finder);
        await t.ensureVisible(finder.first);
        await t.pump(const Duration(milliseconds: 350));
        expect(finder.hitTestable(), findsWidgets);
        await t.tap(finder.hitTestable().first);
        await t.pump(const Duration(milliseconds: 550));
      }

      Future<void> select(AuthController auth) async {
        await t.pumpWidget(const SizedBox());
        await t.pump();
        await t.pumpWidget(
          AcademyApp(
            auth: auth,
            environment: 'Demo',
            appearance: _ReturningGuardian(),
            entryFrameReady: Future<void>.value(),
          ),
        );
        await t.pump(const Duration(seconds: 2));
        await tap(
          find.byKey(const ValueKey('guardian-action-تجديد اشتراك لغيره')),
        );
        await waitFor(find.byType(TextField));
        await t.enterText(find.byType(TextField), _player);
        await tap(find.text('بحث عن لاعب'));
        final result = find.descendant(
          of: find.byType(GLink),
          matching: find.text(_player),
        );
        await waitFor(result);
        expect(
          t.widget<EditableText>(find.byType(EditableText)).focusNode.hasFocus,
          isFalse,
        );
        await tap(result);
      }

      await select(payer);
      await waitFor(find.text('متابعة طلب الدفع'));
      expect(find.text('اختيار الباقة'), findsNothing);
      await binding.takeScreenshot('phase-externalresume-ios-payer');
      await tap(find.text('متابعة طلب الدفع'));
      await waitFor(find.text('المبلغ المطلوب'));
      expect(api.paymentReads.last, '$_base/payments/$paymentId');
      await binding.takeScreenshot('phase-externalresume-ios-checkout');
      await t.binding.handlePopRoute();
      await t.pump(const Duration(milliseconds: 600));
      await waitFor(find.text('تجديد اشتراك لشخص آخر'));
      await select(other);
      await waitFor(find.text('تواصل مع الأكاديمية لمتابعة الطلب.'));
      expect(find.text('متابعة طلب الدفع'), findsNothing);
      expect(find.text('اختيار الباقة'), findsNothing);
      expect(api.writes.length, initialWrites);
      await binding.takeScreenshot('phase-externalresume-ios-other-payer');
    },
  );
}
