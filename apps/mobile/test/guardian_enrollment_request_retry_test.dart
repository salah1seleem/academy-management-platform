import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/guardian/enrollment_requests.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:academy_mobile/features/shared/transfer_payment.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show FakeApi, MemoryStore, credentials;
import 'guardian_test.dart' show logged, shell;

const options = {
  'children': [
    {'id': 'child', 'name': 'طفل اختبار'},
  ],
  'sports': [
    {'id': 'sport', 'name': 'كرة القدم'},
  ],
  'branches': [
    {'id': 'branch', 'name': 'فرع العرض'},
  ],
  'plans': [
    {
      'id': 'a',
      'arabicName': 'باقة أولى',
      'sportId': 'sport',
      'price': 200,
      'currency': 'EGP',
    },
    {
      'id': 'b',
      'arabicName': 'باقة ثانية',
      'sportId': 'sport',
      'price': 300,
      'currency': 'EGP',
    },
  ],
};

Finder field(String title) => find.byWidgetPredicate(
  (widget) =>
      widget is DropdownButtonFormField<String> &&
      widget.decoration.labelText == title,
);

Future<void> select(WidgetTester t, String title, String option) async {
  final dropdown = field(title);
  await t.ensureVisible(dropdown);
  await t.pumpAndSettle();
  await t.tap(dropdown);
  await t.pumpAndSettle();
  await t.tap(find.textContaining(option).last);
  await t.pumpAndSettle();
}

Future<void> press(WidgetTester t, String label) async {
  final button = find.widgetWithText(FilledButton, label);
  await t.ensureVisible(button);
  await t.pumpAndSettle();
  await t.tap(button);
  await t.pumpAndSettle();
}

void main() {
  for (final rejected in [false, true]) {
    testWidgets(
      rejected
          ? 'Validation releases the draft for a new selection'
          : 'A lost response locks the plan and retries the original request',
      (t) async {
        final writes = <({Map<String, dynamic> body, String? key})>[];
        late final FakeApi api;
        api = FakeApi((m, p, b, _) async {
          if (p.contains('/auth/')) return credentials();
          if (p.endsWith('/enrollment-request-options')) return options;
          if (m == 'POST' && p.endsWith('/enrollment-requests')) {
            writes.add((
              body: Map<String, dynamic>.from(b as Map),
              key: api.keys.last,
            ));
            if (writes.length == 1) {
              throw ApiFailure(
                rejected ? 422 : 503,
                rejected ? 'اختر باقة أخرى.' : 'تعذر تأكيد وصول الطلب.',
              );
            }
            return {'id': 'request'};
          }
          if (p.endsWith('/payment')) {
            return {
              'id': 'request',
              'status': 'Approved',
              'amount': 200,
              'currency': 'EGP',
            };
          }
          return <String, dynamic>{};
        });
        final auth = AuthController(api, MemoryStore())..restoring = false;
        await auth.verify('phone', 'challenge', 'otp');
        await t.pumpWidget(shell(EnrollmentForm(auth: auth)));
        await t.pumpAndSettle();
        await select(t, 'الطفل', 'طفل اختبار');
        await select(t, 'الرياضة', 'كرة القدم');
        await select(t, 'الفرع المفضل', 'فرع العرض');
        await select(t, 'الباقة', 'باقة أولى');
        await press(t, 'متابعة إلى الدفع');
        expect(writes.single.body['subscriptionPlanId'], 'a');
        final plan = t.widget<DropdownButtonFormField<String>>(field('الباقة'));
        if (rejected) {
          expect(plan.onChanged, isNotNull);
          await select(t, 'الباقة', 'باقة ثانية');
          await press(t, 'متابعة إلى الدفع');
          expect(writes.last.key, isNot(writes.first.key));
          expect(writes.last.body['subscriptionPlanId'], 'b');
        } else {
          expect(plan.onChanged, isNull);
          await press(t, 'إعادة إرسال الطلب');
          expect(writes.last.key, writes.first.key);
          expect(writes.last.body, writes.first.body);
        }
        expect(writes, hasLength(2));
        expect(find.byType(EnrollmentPaymentPage), findsOneWidget);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }

  testWidgets('An enrollment without a plan never prompts a bank transfer', (
    t,
  ) async {
    final reads = <String>[];
    final auth = await logged((m, p, b, k) async {
      reads.add(p);
      return {'id': 'request', 'status': 'Pending', 'amount': null};
    });
    await t.pumpWidget(shell(EnrollmentPaymentPage(auth: auth, id: 'request')));
    await t.pumpAndSettle();
    expect(find.text('قيد الانتظار'), findsOneWidget);
    expect(find.text('لا يوجد مبلغ مطلوب لهذا الطلب.'), findsOneWidget);
    expect(find.byType(TransferPayment), findsNothing);
    expect(find.text('رفع إيصال الدفع'), findsNothing);
    expect(find.text('غير متاح غير متاح'), findsNothing);
    expect(reads, ['/api/v1/guardian/enrollment-requests/request/payment']);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  testWidgets('Paid and unpaid approvals offer different next actions', (
    t,
  ) async {
    final auth = await logged((m, p, b, k) async {
      if (p.endsWith('/enrollment-requests')) {
        return [
          for (final paid in [true, false])
            {
              'id': 'request-$paid',
              'child': paid ? 'لاعب دُفع اشتراكه' : 'لاعب يختار الباقة',
              'sport': 'كرة القدم',
              'branch': 'فرع العرض',
              'status': 'Approved',
              'createdAtUtc': '2026-11-09T10:00:00Z',
              'createdSportEnrollmentId': 'enrollment-$paid',
              'paymentRequestId': paid ? 'payment' : null,
            },
        ];
      }
      return {'items': [], 'children': []};
    });
    await t.pumpWidget(shell(EnrollmentRequests(auth: auth)));
    await t.pumpAndSettle();
    expect(find.text('اختيار باقة والاشتراك'), findsOneWidget);
    await t.tap(find.text('عرض الإيصالات'));
    await t.pumpAndSettle();
    expect(find.byType(ReceiptsPage), findsOneWidget);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
}
