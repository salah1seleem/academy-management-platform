import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:academy_mobile/features/guardian/tournament_fees.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:academy_mobile/features/shared/receivable_balances.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

void main() {
  for (final denied in [false, true]) {
    testWidgets(
      'Tournament notice uses its exact saved source; denied=$denied',
      (t) async {
        final calls = <String>[];
        final auth = await authenticate('Guardian', (m, p, b, token) async {
          calls.add('$m $p');
          if (p == '/api/v1/notifications?page=1') {
            return {
              'unread': 1,
              'total': 1,
              'items': [
                {
                  'id': 'notice',
                  'message': 'تحديث رسم بطولة اصطناعية',
                  'isRead': false,
                  'createdAtUtc': '2026-10-23',
                  'targetKind': 'Tournament',
                  'targetId': 'saved-tournament-payment',
                },
              ],
            };
          }
          if (p ==
              '/api/v1/guardian/tournament-payments/saved-tournament-payment') {
            if (denied) {
              throw const ApiFailure(404, 'السجل غير متاح.');
            }
            return {
              'id': 'saved-tournament-payment',
              'tournamentName': 'بطولة قبول اصطناعية',
              'player': 'لاعب اختبار خيالي',
              'amount': 250,
              'currency': 'EGP',
              'status': 'Confirmed',
              'receiptNumber': 'TRN-SAVED-TEST',
              'confirmedAtUtc': '2026-10-23T12:00:00Z',
            };
          }
          return {};
        });
        await t.pumpWidget(app(NotificationInbox(auth: auth)));
        await t.pumpAndSettle();
        await t.tap(find.text('عرض طلب البطولة'));
        await t.pumpAndSettle();
        expect(
          t.widget<TournamentCheckout>(find.byType(TournamentCheckout)).id,
          'saved-tournament-payment',
        );
        expect(
          calls,
          contains(
            'GET /api/v1/guardian/tournament-payments/saved-tournament-payment',
          ),
        );
        if (denied) {
          expect(find.text('السجل غير متاح.'), findsWidgets);
          expect(find.text('TRN-SAVED-TEST'), findsNothing);
          expect(find.text('لاعب اختبار خيالي'), findsNothing);
        } else {
          expect(find.text('TRN-SAVED-TEST'), findsOneWidget);
          expect(find.text('تم تأكيد التحصيل'), findsOneWidget);
        }
        expect(calls.any((c) => !c.startsWith('GET ')), false);
        expect(calls.any((c) => c.contains('/subscriptions/receipts/')), false);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  for (final kind in ['Receivables', 'Renewal']) {
    testWidgets(
      '$kind reminder opens read-only destination without a guessed ID',
      (t) async {
        final calls = <String>[];
        final auth = await authenticate('Guardian', (m, p, b, token) async {
          calls.add('$m $p');
          if (p == '/api/v1/notifications?page=1') {
            return {
              'unread': 0,
              'total': 1,
              'items': [
                {
                  'id': 'reminder',
                  'message': 'تذكير داخلي اصطناعي',
                  'isRead': true,
                  'createdAtUtc': '2026-10-23',
                  'targetKind': kind,
                  'targetId': null,
                },
              ],
            };
          }
          if (p == '/api/v1/guardian/subscriptions/enrollments') return [];
          return {};
        });
        await t.pumpWidget(app(NotificationInbox(auth: auth)));
        await t.pumpAndSettle();
        await t.tap(
          find.text(kind == 'Receivables' ? 'كشف المستحقات' : 'تجديد الاشتراك'),
        );
        await t.pumpAndSettle();
        expect(
          kind == 'Receivables'
              ? find.byType(ReceivableBalances)
              : find.byType(RenewalPicker),
          findsOneWidget,
        );
        expect(calls.any((c) => !c.startsWith('GET ')), false);
        expect(calls.any((c) => c.contains('/null')), false);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
}
