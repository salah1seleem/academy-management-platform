import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/guardian/guardian_day_pass.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart';
import 'guardian_test.dart' show shell;

void main() {
  testWidgets(
    'Linked guardian reviews one day pass and keeps training separate at 390px',
    (tester) async {
      tester.view.physicalSize = const Size(390, 900);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      var created = false;
      Object? submitted;
      final api = FakeApi((method, path, body, token) async {
        if (path.contains('/auth/')) return credentials();
        if (path.endsWith('/options')) {
          return {
            'children': [
              {'id': 'child', 'name': 'طفل اصطناعي'},
            ],
            'bookings': [
              {
                'bookingId': 'booking',
                'date': '2026-10-14',
                'startTime': '17:00:00',
                'endTime': '18:00:00',
                'resource': 'حارة A',
                'plans': [
                  {
                    'id': 'plan',
                    'name': 'زيارة يومية',
                    'amount': '125.00',
                    'currency': 'EGP',
                  },
                ],
              },
            ],
          };
        }
        if (path.endsWith('/orders') && method == 'GET') {
          return created
              ? [
                  {
                    'id': 'ticket',
                    'playerId': 'child',
                    'status': 'PendingProof',
                    'planName': 'زيارة يومية',
                    'amount': '125.00',
                    'currency': 'EGP',
                  },
                ]
              : <Object>[];
        }
        if (path.endsWith('/quote')) {
          return {
            'player': 'طفل اصطناعي',
            'resource': 'حارة A',
            'bookingDate': '2026-10-14',
            'startTime': '17:00:00',
            'endTime': '18:00:00',
            'plan': 'زيارة يومية',
            'amount': '125.00',
            'currency': 'EGP',
            'quoteHash': 'hash',
          };
        }
        if (path.endsWith('/orders') && method == 'POST') {
          submitted = body;
          created = true;
          return {'id': 'ticket'};
        }
        if (path.endsWith('/payment-settings')) return {'configured': false};
        throw StateError('Unexpected path: $path');
      });
      final auth = AuthController(api, MemoryStore());
      await auth.verify('phone', 'challenge', 'otp');
      await tester.pumpWidget(shell(GuardianDayPassPage(auth: auth)));
      await tester.pumpAndSettle();
      expect(find.textContaining('لا تُخصم حصة'), findsOneWidget);
      await tester.tap(find.byType(DropdownButtonFormField<String>).at(0));
      await tester.pumpAndSettle();
      await tester.tap(find.text('طفل اصطناعي').last);
      await tester.pumpAndSettle();
      await tester.tap(find.byType(DropdownButtonFormField<String>).at(1));
      await tester.pumpAndSettle();
      await tester.tap(find.textContaining('حارة A · 2026-10-14').last);
      await tester.pumpAndSettle();
      await tester.tap(find.byType(DropdownButtonFormField<String>).at(2));
      await tester.pumpAndSettle();
      await tester.tap(find.textContaining('زيارة يومية · 125.00').last);
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.text('مراجعة السعر والموعد'));
      await tester.tap(find.text('مراجعة السعر والموعد'));
      await tester.pumpAndSettle();
      expect(find.textContaining('ستراجع الإدارة وصول المبلغ'), findsOneWidget);
      await tester.ensureVisible(find.text('إنشاء طلب واحد'));
      await tester.tap(find.text('إنشاء طلب واحد'));
      await tester.pumpAndSettle();
      expect(submitted, {
        'playerId': 'child',
        'bookingId': 'booking',
        'planId': 'plan',
        'quoteHash': 'hash',
      });
      expect(api.keys.whereType<String>().single, isNotEmpty);
      expect(find.text('بانتظار إثبات التحويل'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox.shrink());
      auth.dispose();
    },
  );
}
