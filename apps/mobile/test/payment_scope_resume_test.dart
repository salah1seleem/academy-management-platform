import 'dart:async';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show FakeApi, MemoryStore, credentials;
import 'support/visibility.dart';

class _ReturningAdmin extends Appearance {
  @override
  Future<bool> hasRoleIntro(String role, String sportTheme) async => true;
}

Map<String, Object> _membership(int revision) => {
  'id': 'administrator',
  'academyId': 'academy',
  'academyName': 'أكاديمية العرض',
  'role': 'AcademyAdmin',
  'contextRevision': revision,
};

const _payments = ValueKey('admin-home-payments-task');
const _player = 'لاعب طلب الاختبار';
const _modal = 'إثبات مفتوح في نافذة مستقلة';

void main() {
  testWidgets(
    'a delegated session hides the queue and root dialog until authority is confirmed',
    (tester) async {
      var granted = true;
      var checks = 0;
      var reply = Completer<Object?>();
      final store = MemoryStore();
      final api = FakeApi((method, path, body, token) async {
        if (path.endsWith('/otp/verify')) {
          return {
            ...credentials(),
            'membershipId': 'administrator',
            'memberships': [_membership(1)],
          };
        }
        expect(method, 'GET');
        if (path.endsWith('/mobile/auth/memberships')) {
          checks++;
          return reply.future;
        }
        if (path.endsWith('/my-permissions')) {
          return granted ? ['payment.read'] : <String>[];
        }
        if (path.contains('/manage/payment-requests?')) {
          if (!granted) {
            throw const ApiFailure(403, 'لا تملك صلاحية هذه العملية');
          }
          return {
            'pending': 1,
            'items': [
              {
                'id': 'synthetic',
                'type': 'Enrollment',
                'player': _player,
                'sport': 'السباحة',
                'branch': 'فرع العرض',
                'plan': 'اشتراك شهري',
                'amount': 720,
                'currency': 'EGP',
              },
            ],
          };
        }
        return <String, dynamic>{};
      });
      final auth = AuthController(api, store)..restoring = false;
      addTearDown(auth.dispose);
      await auth.verify('phone', 'challenge', 'code');
      await tester.pumpWidget(
        AcademyApp(
          auth: auth,
          environment: 'Demo',
          appearance: _ReturningAdmin(),
        ),
      );
      await tester.pumpAndSettle();
      final entry = find.descendant(
        of: find.byKey(_payments),
        matching: find.text('عرض الطلبات'),
      );
      await revealTestTarget(tester, entry);
      await tester.tap(entry);
      await tester.pumpAndSettle();
      expect(find.text(_player), findsOneWidget);
      expect(find.text('طلبات معلّقة (1)'), findsOneWidget);
      final context = tester.element(find.text(_player));
      unawaited(
        showDialog<void>(
          context: context,
          builder: (_) => const AlertDialog(title: Text(_modal)),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text(_modal), findsOneWidget);

      Future<void> resume() async {
        tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.hidden);
        tester.binding.handleAppLifecycleStateChanged(
          AppLifecycleState.resumed,
        );
        await tester.pump();
      }

      await resume();
      expect(checks, 1);
      expect(find.text(_modal).hitTestable(), findsNothing);
      expect(find.text('جارٍ التحقق من صلاحياتك…'), findsOneWidget);
      reply.complete([_membership(1)]);
      await tester.pumpAndSettle();
      expect(find.text(_modal).hitTestable(), findsOneWidget);
      expect(store.token, 'refresh-one');

      reply = Completer<Object?>();
      await resume();
      reply.completeError(const ApiFailure(503, 'Service unavailable'));
      await tester.pumpAndSettle();
      expect(find.text(_modal).hitTestable(), findsNothing);
      expect(find.textContaining('Service unavailable'), findsNothing);
      reply = Completer<Object?>();
      await tester.tap(find.text('إعادة المحاولة'));
      await tester.pump();
      granted = false;
      reply.complete([_membership(2)]);
      await tester.pumpAndSettle();
      expect(checks, 3);
      expect(auth.session?.selected?.contextRevision, 2);
      expect(find.text(_modal), findsNothing);
      expect(find.text(_player), findsNothing);
      expect(find.byKey(_payments), findsNothing);
      expect(find.text('يوم الأكاديمية'), findsOneWidget);
      expect(store.token, 'refresh-one');
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
    },
  );
}
