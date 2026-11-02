import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/admin/native_refund.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'approved_native_refund_test.dart' as flow;
import 'v2_role_workspaces_test.dart' show authenticate, app;

void main() {
  for (final fault in [
    'success',
    'amount',
    'currency',
    'collection',
    'method',
    'reason',
    'shift',
    'lostRead',
  ]) {
    testWidgets(
      'refund confirms the exact stored financial source and resumes acknowledged reads only: $fault',
      (t) async {
        var writes = 0, recovered = false;
        final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
          if (p.endsWith('/quote')) {
            return {
              'amount': '13.34',
              'available': '100.00',
              'currency': 'EGP',
              'sessions': [],
            };
          }
          if (m == 'POST') {
            writes++;
            return {'id': 'refund'};
          }
          final value = flow.source('Online', writes > 0);
          if (writes > 0) {
            if (fault == 'lostRead' && !recovered) {
              throw const ApiFailure(503, 'تعذر قراءة السجل.');
            }
            value['refunds'] = [
              {
                'id': 'refund',
                'collectionId': !recovered && fault == 'collection'
                    ? 'other'
                    : 'collection',
                'amount': !recovered && fault == 'amount' ? '13.35' : '13.34',
                'currency': !recovered && fault == 'currency' ? 'USD' : 'EGP',
                'payoutMethod': !recovered && fault == 'method'
                    ? 'Cash'
                    : 'Online',
                'reason': !recovered && fault == 'reason'
                    ? 'سبب آخر'
                    : 'سبب اصطناعي',
                'cashSessionId': !recovered && fault == 'shift'
                    ? 'other-shift'
                    : null,
              },
            ];
          }
          return value;
        });
        await t.pumpWidget(
          app(NativePlayerRefund(auth: auth, playerId: 'player')),
        );
        await t.pumpAndSettle();
        await flow.choose(t, 'التحصيل', '2026-10-21 · المتاح 100 ج.م');
        await t.enterText(
          find.widgetWithText(TextFormField, 'قيمة الاسترداد'),
          '13.34',
        );
        await t.enterText(
          find.widgetWithText(TextFormField, 'سبب الاسترداد'),
          'سبب اصطناعي',
        );
        await flow.tap(t, 'مراجعة مبلغ الاسترداد');
        await flow.tap(
          t,
          'أؤكد أن التحويل تم خارج النظام وأوثق رده مرة واحدة.',
        );
        await flow.tap(t, 'تأكيد تسجيل الاسترداد');
        const success =
            'سُجل الاسترداد وظهر في السجل. لا تصرف المبلغ مرة أخرى.';
        if (fault != 'success') {
          expect(find.text(success), findsNothing);
          expect(writes, 1);
          recovered = true;
          await flow.tap(t, 'إعادة تأكيد الاسترداد نفسه');
        }
        expect(find.text(success), findsOneWidget);
        expect(
          writes,
          1,
          reason: 'An acknowledged refund only retries reading; it never posts again.',
        );
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox.shrink());
        auth.dispose();
      },
    );
  }
}
