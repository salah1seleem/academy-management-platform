import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/admin/native_enrollment_payment_read.dart';
import 'package:academy_mobile/features/admin/native_renewal_proof_review.dart';
import 'package:academy_mobile/features/guardian/family_renewal_request.dart';
import 'package:academy_mobile/features/shared/matches.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

void main() {
  for (final kind in ['Renewal', 'FamilyRenewal', 'Enrollment']) {
    testWidgets(
      'Owner notice opens its exact saved $kind source, including processed history, without collecting',
      (t) async {
        final calls = <String>[];
        final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
          calls.add('$m $p');
          if (p.contains('/notifications')) {
            return {
              'unread': 1,
              'total': 1,
              'items': [
                {
                  'id': 'n1',
                  'targetKind': 'PaymentQueue',
                  'targetId': 'saved-source',
                  'paymentReviewType': kind,
                  'createdAtUtc': '2026-10-22',
                  'message': 'إثبات دفع لطلب محفوظ',
                  'isRead': false,
                },
              ],
            };
          }
          if (p.endsWith('/my-permissions')) return [];
          if (p.endsWith('/my-access')) return {};
          if (p == '/api/v1/manage/enrollment-requests/saved-source/payment') {
            return {
              'id': 'saved-source',
              'status': 'Approved',
              'originalAmount': 1000,
              'discountAmount': 100,
              'amount': 900,
              'currency': 'EGP',
              'proofUrl': null,
            };
          }
          throw const ApiFailure(403, 'لا تملك صلاحية عرض هذا الطلب.');
        });
        await t.pumpWidget(app(NotificationInbox(auth: auth)));
        await t.pumpAndSettle();
        await t.tap(find.text('عرض طلب الدفع ومراجعته'));
        await t.pumpAndSettle();
        if (kind == 'Renewal') {
          expect(
            t
                .widget<NativeRenewalProofReview>(
                  find.byType(NativeRenewalProofReview),
                )
                .id,
            'saved-source',
          );
        }
        if (kind == 'FamilyRenewal') {
          final page = t.widget<NativeFamilyRenewalRequest>(
            find.byType(NativeFamilyRenewalRequest),
          );
          expect(page.id, 'saved-source');
          expect(page.staff, true);
        }
        if (kind == 'Enrollment') {
          expect(
            t
                .widget<NativeEnrollmentPaymentRead>(
                  find.byType(NativeEnrollmentPaymentRead),
                )
                .id,
            'saved-source',
          );
          expect(find.text('مقبول'), findsOneWidget);
          expect(find.text('900 ج.م'), findsOneWidget);
          await auth.logout();
          await t.pumpAndSettle();
          expect(find.text('900 ج.م'), findsNothing);
        }
        expect(
          calls.any((p) => p.startsWith('POST ') || p.startsWith('PUT ')),
          false,
        );
        expect(calls.any((p) => p.contains('manage/payment-requests?')), false);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  for (final correct in [true, false]) {
    testWidgets(
      'Unpriced enrollment notice never invents amounts or displays a different request; correct=$correct',
      (t) async {
        final auth = await authenticate(
          'AcademyOwner',
          (m, p, b, token) async => {
            'id': correct ? 'source' : 'different',
            'status': 'Pending',
            'originalAmount': null,
            'discountAmount': 0,
            'amount': null,
            'currency': 'EGP',
            'proofUrl': null,
          },
        );
        await t.pumpWidget(
          app(NativeEnrollmentPaymentRead(auth: auth, id: 'source')),
        );
        await t.pumpAndSettle();
        expect(
          find.text(
            correct
                ? 'لم تُحدد قيمة الدفع لهذا الطلب بعد.'
                : 'لم تتأكد هوية الطلب. أعد فتحه.',
          ),
          findsOneWidget,
        );
        expect(find.text('0 ج.م'), findsNothing);
        expect(find.text('المبلغ النهائي'), findsNothing);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  testWidgets(
    'Removed coach match notice opens current authorized matches without an old match id',
    (t) async {
      final calls = <String>[];
      final auth = await authenticate('Coach', (m, p, b, token) async {
        calls.add(p);
        if (p.contains('/notifications')) {
          return {
            'unread': 1,
            'total': 1,
            'items': [
              {
                'id': 'n1',
                'targetKind': 'Matches',
                'targetId': null,
                'matchId': null,
                'createdAtUtc': '2026-10-22',
                'message': 'تم تعديل إسناد إحدى مبارياتك',
                'isRead': false,
              },
            ],
          };
        }
        if (p == '/api/v1/my-matches') return [];
        if (p.endsWith('/my-permissions')) return [];
        return {};
      });
      await t.pumpWidget(app(NotificationInbox(auth: auth)));
      await t.pumpAndSettle();
      await t.tap(find.text('عرض المباريات الحالية'));
      await t.pumpAndSettle();
      expect(t.widget<MatchSchedule>(find.byType(MatchSchedule)).matchId, null);
      expect(calls, contains('/api/v1/my-matches'));
      expect(find.text('لا توجد مباريات منشورة تخصك حاليًا.'), findsOneWidget);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
