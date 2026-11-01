import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/admin/native_renewal.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show FakeApi;
import 'v2_role_workspaces_test.dart' show authenticate, app;

const payment = {
  'id': 'payment',
  'renewalId': 'renewal',
  'sportEnrollmentId': 'enrollment',
  'subscriptionPlanId': 'plan',
  'player': 'لاعب اصطناعي',
  'sport': 'كرة القدم',
  'plan': 'باقة اختبار',
  'status': 'Pending',
  'isCurrent': true,
  'canCollect': true,
  'originalAmount': 500,
  'discountAmount': 100,
  'finalAmount': 400,
  'currency': 'EGP',
  'periodId': null,
  'receiptId': null,
};
Future<void> tap(WidgetTester t, String label) async {
  final f = find.text(label);
  await t.scrollUntilVisible(f, 120, scrollable: find.byType(Scrollable).first);
  await t.ensureVisible(f.last);
  await t.pumpAndSettle();
  await t.tap(f.last);
  // Confirmation keeps the operation busy until the modal is answered.
  await t.pump(const Duration(milliseconds: 300));
  if (find.byType(AlertDialog).evaluate().isEmpty) {
    await t.pumpAndSettle();
  }
}

Future<void> choose(WidgetTester t, String label, String value) async {
  final f = find.widgetWithText(DropdownButtonFormField<String>, label);
  await t.ensureVisible(f);
  await t.pumpAndSettle();
  await t.tap(f);
  await t.pumpAndSettle();
  await t.tap(find.text(value).last);
  await t.pumpAndSettle();
}

void main() {
  for (final collect in [false, true]) {
    testWidgets(
      'Administrator payment read ${collect ? 'with electronic collection' : 'alone'} never opens a cash shift or unauthorized collection',
      (t) async {
        final auth = await authenticate('AcademyAdmin', (m, p, b, token) async {
          if (p.endsWith('/my-permissions')) {
            return ['payment.read', if (collect) 'subscription.renew.manage'];
          }
          if (p.endsWith('/my-access')) {
            return {
              'useActionGrants': true,
              'actions': [
                {
                  'module': 'payments',
                  'read': true,
                  'add': collect,
                  'edit': collect,
                },
                {
                  'module': 'subscriptions',
                  'read': true,
                  'add': false,
                  'edit': false,
                },
              ],
              'workflowActions': [
                {
                  'key': 'workflow.payments.collect',
                  'hasExplicitGrant': true,
                  'allowed': collect,
                },
              ],
            };
          }
          if (m != 'GET') {
            throw StateError(
              'No collection command is authorized by this test.',
            );
          }
          return payment;
        });
        await t.pumpWidget(
          app(NativeRenewalPayment(auth: auth, id: 'payment')),
        );
        await t.pumpAndSettle();
        expect(find.text('بانتظار التحصيل'), findsOneWidget);
        if (collect) {
          final choice = find.widgetWithText(
            DropdownButtonFormField<String>,
            'طريقة التحصيل',
          );
          await t.ensureVisible(choice);
          await t.tap(choice);
          await t.pumpAndSettle();
          expect(find.text('تحويل إلكتروني تم استلامه'), findsWidgets);
          expect(find.text('نقدي من ورديتي'), findsNothing);
        } else {
          expect(find.text('طريقة التحصيل'), findsNothing);
          expect(find.text('تأكيد التحصيل وإصدار الإيصال'), findsNothing);
        }
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  for (final lost in [false, true]) {
    testWidgets(
      'Renewal request ${lost ? 'lost response' : 'success'} preserves intent and resumes the saved payment without another create',
      (t) async {
        final writes = <Json>[];
        final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
          if (p.endsWith('/enrollments/enrollment')) {
            return {
              'enrollment': {
                'player': 'لاعب اصطناعي',
                'sport': 'كرة القدم',
                'branch': 'فرع اختبار',
                'group': 'مجموعة اختبار',
              },
              'previewStart': '2026-10-21',
              'plans': [
                {
                  'id': 'plan',
                  'arabicName': 'باقة اختبار',
                  'price': 500,
                  'currency': 'EGP',
                },
              ],
            };
          }
          if (m == 'POST') {
            writes.add(Map.from(b as Map));
            if (lost && writes.length == 1) {
              throw const ApiFailure(0, 'فقد رد إنشاء الطلب.');
            }
            return {'paymentId': 'payment', 'renewalId': 'renewal'};
          }
          return {...payment, 'canCollect': false};
        });
        await t.pumpWidget(
          app(NativeAdminRenewal(auth: auth, enrollmentId: 'enrollment')),
        );
        await t.pumpAndSettle();
        await choose(t, 'الباقة', 'باقة اختبار · 500 ج.م');
        await tap(t, 'إنشاء طلب التجديد والدفع');
        if (lost) {
          expect(find.byType(NativeRenewalPayment), findsNothing);
          await tap(t, 'إعادة التحقق من طلب التجديد');
          expect(writes[0], writes[1]);
        }
        expect(find.byType(NativeRenewalPayment), findsOneWidget);
        expect(find.text('بانتظار التحصيل'), findsOneWidget);
        final count = writes.length;
        await t.tap(find.byTooltip('رجوع'));
        await t.pumpAndSettle();
        await tap(t, 'استكمال طلب التجديد');
        expect(find.byType(NativeRenewalPayment), findsOneWidget);
        expect(writes.length, count);
        final api = auth.transport as FakeApi;
        final keys = [
          for (var i = 0; i < api.paths.length; i++)
            if (api.paths[i] == '/api/v1/subscriptions/admin-renewals')
              api.keys[i],
        ];
        expect(keys.every((key) => key == keys.first && key != null), isTrue);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  for (final fault in [
    'success',
    'lostWrite',
    'lostRead',
    'wrongReceipt',
    'foreignPeriod',
    'forbidden',
  ]) {
    testWidgets(
      'Renewal collection $fault requires its exact stored receipt and period and never re-collects confirmed payment',
      (t) async {
        var posts = 0, paid = false, failed = false, recover = false;
        final readPaths = <String>[];
        final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
          if (m == 'POST') {
            posts++;
            expect(b, {
              'paymentMethod': 'Online',
              'cashSessionId': null,
              'expectedAmount': '400',
            });
            if (fault == 'forbidden') {
              throw const ApiFailure(403, 'غير مخول بالتحصيل.');
            }
            paid = true;
            if (fault == 'lostWrite' && !failed) {
              failed = true;
              throw const ApiFailure(0, 'فقد رد التحصيل.');
            }
            return {'confirmed': true};
          }
          readPaths.add(p);
          if (p.endsWith('/payments/payment')) {
            if (fault == 'lostRead' && paid && !failed) {
              failed = true;
              throw const ApiFailure(503, 'تعذر قراءة الطلب.');
            }
            return {
              ...payment,
              'status': paid ? 'Confirmed' : 'Pending',
              'receiptId': paid ? 'receipt' : null,
              'periodId': paid ? 'period' : null,
            };
          }
          if (p.endsWith('/receipts/receipt')) {
            return {
              'id': 'receipt',
              'isVoided': false,
              'paymentMethod': 'Online',
              'currency': 'EGP',
              'amount': fault == 'wrongReceipt' && !recover ? 401 : 400,
              'originalAmount': 500,
              'discountAmount': 100,
              'finalAmount': 400,
            };
          }
          if (p.endsWith('/periods/period')) {
            return {
              'id': 'period',
              'sportEnrollmentId': fault == 'foreignPeriod' && !recover
                  ? 'another-enrollment'
                  : 'enrollment',
              'subscriptionPlanId': 'plan',
              'receiptId': 'receipt',
              'priceSnapshot': 400,
              'status': 'Scheduled',
            };
          }
          throw StateError('Unexpected $m $p');
        });
        await t.pumpWidget(
          app(NativeRenewalPayment(auth: auth, id: 'payment')),
        );
        await t.pumpAndSettle();
        await choose(t, 'طريقة التحصيل', 'تحويل إلكتروني تم استلامه');
        await tap(t, 'تأكيد التحصيل وإصدار الإيصال');
        expect(posts, 0);
        await t.tap(find.text('تأكيد'));
        await t.pumpAndSettle();
        expect(posts, 1);
        if (fault == 'forbidden') {
          expect(find.text('غير مخول بالتحصيل.'), findsOneWidget);
          expect(find.text('تم التحصيل'), findsNothing);
          expect(
            readPaths.any(
              (p) => p.contains('/receipts/') || p.contains('/periods/'),
            ),
            isFalse,
          );
        } else {
          if (fault != 'success') {
            expect(
              find.text('حُفظ التحصيل وفترة التجديد، وأُعيدت قراءة الإيصال.'),
              findsNothing,
            );
            recover = true;
            await tap(t, 'إعادة التحقق من التحصيل نفسه');
          }
          expect(find.text('تم التحصيل'), findsOneWidget);
          expect(find.text('عرض الإيصال'), findsOneWidget);
          expect(posts, 1);
          await t.scrollUntilVisible(
            find.text('حُفظ التحصيل وفترة التجديد، وأُعيدت قراءة الإيصال.'),
            120,
            scrollable: find.byType(Scrollable).first,
          );
          expect(
            find.text('حُفظ التحصيل وفترة التجديد، وأُعيدت قراءة الإيصال.'),
            findsOneWidget,
          );
          expect(
            readPaths,
            containsAll([
              '/api/v1/subscriptions/receipts/receipt',
              '/api/v1/subscriptions/periods/period',
            ]),
          );
        }
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
}
