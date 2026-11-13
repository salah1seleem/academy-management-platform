import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/owner/featured_players.dart';
import 'package:academy_mobile/features/owner/member_values.dart';
import 'package:academy_mobile/features/shared/transfer_payment.dart';
import 'package:academy_mobile/features/shared/content_editor.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'modern_experience_v514_test.dart' show frame;
import 'v2_role_workspaces_test.dart' as fixture;

Finder listScroll() => find
    .descendant(
      of: find.byType(ListView).first,
      matching: find.byType(Scrollable),
    )
    .first;

void phone(WidgetTester t) {
  t.view.physicalSize = const Size(375, 812);
  t.view.devicePixelRatio = 1;
  addTearDown(t.view.reset);
}

void main() {
  for (final model in VisualModel.values) {
    testWidgets(
      'featured player list remains compact and readable at 200% $model',
      (t) async {
        phone(t);
        final auth = await fixture.authenticate('AcademyOwner', (
          m,
          p,
          b,
          token,
        ) async {
          expect(m, 'GET');
          return {
            'items': [
              {
                'id': 'p1',
                'name': 'طفل عرض اصطناعي طويل الاسم',
                'stars': 2,
                'score': 85,
                'branch': 'فرع عرض اصطناعي',
              },
              {
                'id': 'p2',
                'name': 'طفل عرض ثان اصطناعي',
                'stars': 1,
                'score': 80,
                'branch': 'فرع عرض آخر',
              },
            ],
          };
        });
        await t.pumpWidget(
          frame(FeaturedPlayers(auth: auth, all: true), model),
        );
        await t.pumpAndSettle();
        expect(find.text('طفل عرض اصطناعي طويل الاسم'), findsOneWidget);
        await t.scrollUntilVisible(find.text('طفل عرض ثان اصطناعي'), 200);
        await t.pumpAndSettle();
        expect(find.text('طفل عرض ثان اصطناعي'), findsOneWidget);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );

    testWidgets(
      'guardian value and linked children remain distinct from payments at 200% $model',
      (t) async {
        phone(t);
        final calls = <String>[];
        final auth = await fixture.authenticate('AcademyOwner', (
          m,
          p,
          b,
          token,
        ) async {
          expect(m, 'GET');
          calls.add(p);
          if (p.endsWith('/guardian-one')) {
            return {
              'displayName': 'ولي أمر عرض اصطناعي',
              'contactPhone': '01099900001',
              'children': [
                {'arabicName': 'طفل مرتبط اصطناعي', 'playerCode': 'PL01'},
              ],
            };
          }
          return {
            'items': [
              {
                'id': 'guardian-one',
                'displayName': 'ولي أمر عرض اصطناعي',
                'contactPhone': '01099900001',
                'linkedChildren': 3,
                'monthlySubscriptionValue': 12500.5,
                'currency': 'EGP',
                'unpricedSubscriptions': 1,
              },
            ],
            'total': 1,
          };
        });
        await t.pumpWidget(
          frame(OwnerGuardianValues(auth: auth, branch: 'branch-one'), model),
        );
        await t.pumpAndSettle();
        expect(t.takeException(), isNull);
        expect(
          find.text(
            'القيمة الشهرية للأبناء المرتبطين؛ ليست سجل مدفوعات الأسرة.',
          ),
          findsOneWidget,
        );
        await t.scrollUntilVisible(
          find.text('ولي أمر عرض اصطناعي'),
          180,
          scrollable: listScroll(),
        );
        await t.pumpAndSettle();
        expect(find.text('قيمة غير مكتملة'), findsOneWidget);
        await t.ensureVisible(find.text('ولي أمر عرض اصطناعي'));
        await t.pumpAndSettle();
        await t.tap(find.text('ولي أمر عرض اصطناعي'));
        await t.pumpAndSettle();
        expect(find.byType(MemberGuardianDetail), findsOneWidget);
        await t.scrollUntilVisible(
          find.text('طفل مرتبط اصطناعي'),
          180,
          scrollable: listScroll(),
        );
        await t.pumpAndSettle();
        expect(find.text('PL01'), findsOneWidget);
        expect(calls.first, contains('branchId=branch-one'));
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }

  testWidgets(
    'paid new enrollment opens only the receipt associated with that request',
    (t) async {
      phone(t);
      final calls = <String>[];
      final auth = await fixture.authenticate('Guardian', (
        m,
        p,
        b,
        token,
      ) async {
        expect(m, 'GET');
        calls.add(p);
        if (p.endsWith('/receipts/receipt-one')) {
          return {
            'receiptNumber': 'RC-TEST-ONE',
            'amount': 900,
            'currency': 'EGP',
            'paidAtUtc': '2026-11-12T09:00:00Z',
            'paymentMethod': 'Online',
          };
        }
        return {
          'originalAmount': 1000,
          'discountAmount': 100,
          'discountReason': 'خصم أخوة محفوظ',
          'amount': 900,
          'currency': 'EGP',
          'status': 'Approved',
          'paymentRequestId': 'payment-one',
          'receiptId': 'receipt-one',
        };
      });
      await t.pumpWidget(
        fixture.app(EnrollmentPaymentPage(auth: auth, id: 'request-one')),
      );
      await t.pumpAndSettle();
      expect(find.text('خصم أخوة محفوظ'), findsOneWidget);
      expect(find.text('المبلغ المحصّل'), findsOneWidget);
      expect(find.byType(TransferPayment), findsNothing);
      await t.tap(find.text('عرض الإيصال'));
      await t.pumpAndSettle();
      expect(find.text('RC-TEST-ONE'), findsOneWidget);
      expect(calls, [
        '/api/v1/guardian/enrollment-requests/request-one/payment',
        '/api/v1/guardian/subscriptions/receipts/receipt-one',
      ]);
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('خصم أخوة محفوظ'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'saved enrollment proof exposes replacement and skips transfer instructions',
    (t) async {
      final calls = <String>[];
      final auth = await fixture.authenticate('Guardian', (
        m,
        p,
        b,
        token,
      ) async {
        calls.add('$m $p');
        return {
          'status': 'UnderReview',
          'amount': 720,
          'currency': 'EGP',
          'proofUrl': '/api/v1/assets/10000000-0000-0000-0000-000000000001',
        };
      });
      await t.pumpWidget(
        fixture.app(EnrollmentPaymentPage(auth: auth, id: 'request-one')),
      );
      await t.pumpAndSettle();
      expect(find.text('المبلغ المطلوب'), findsOneWidget);
      expect(
        find.text('وصل إثبات الدفع. انتظر المراجعة قبل إرسال مبلغ آخر.'),
        findsOneWidget,
      );
      expect(
        t.widget<TransferPayment>(find.byType(TransferPayment)).replacement,
        true,
      );
      expect(find.text('التحويل للأكاديمية'), findsNothing);
      await t.tap(find.text('تحديث الحالة'));
      await t.pumpAndSettle();
      expect(
        calls,
        List.filled(
          2,
          'GET /api/v1/guardian/enrollment-requests/request-one/payment',
        ),
      );
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'denied enrollment payment never exposes an upload action or cached proof',
    (t) async {
      final auth = await fixture.authenticate('Guardian', (
        m,
        p,
        b,
        token,
      ) async {
        throw const ApiFailure(403, 'الطلب غير متاح لهذا الحساب.');
      });
      await t.pumpWidget(
        fixture.app(EnrollmentPaymentPage(auth: auth, id: 'another-request')),
      );
      await t.pumpAndSettle();
      expect(find.text('الطلب غير متاح لهذا الحساب.'), findsOneWidget);
      expect(find.byType(TransferPayment), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'campaign draft cannot save before its required content and audience',
    (t) async {
      var writes = 0;
      final auth = await fixture.authenticate('AcademyOwner', (
        m,
        p,
        b,
        token,
      ) async {
        if (m != 'GET') writes++;
        return {
          'branches': [
            {'id': 'b', 'name': 'فرع عرض اصطناعي'},
          ],
        };
      });
      await t.pumpWidget(
        fixture.app(AcademyContentEditor(auth: auth, ad: true)),
      );
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('حفظ مسودة الإعلان'),
        200,
        scrollable: listScroll(),
      );
      await t.pumpAndSettle();
      await t.ensureVisible(find.text('حفظ مسودة الإعلان'));
      await t.pumpAndSettle();
      await t.tap(find.text('حفظ مسودة الإعلان'));
      await t.pumpAndSettle();
      expect(
        find.text('أدخل العنوان والتفاصيل واختر الفروع للإعلان.'),
        findsOneWidget,
      );
      expect(writes, 0);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
