import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/guardian/family_renewal_quote.dart';
import 'package:academy_mobile/features/guardian/family_renewal_request.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'guardian_test.dart' show logged, shell;

Map<String, dynamic> detail({String status = 'Pending'}) => {
  'id': 'family',
  'revision': 2,
  'status': status,
  'originalAmount': '2000.00',
  'discountAmount': '400.00',
  'totalAmount': '1600.00',
  'currency': 'EGP',
  'items': [
    for (final name in ['آدم اختبار', 'لينا اختبار'])
      {
        'id': name,
        'player': name,
        'sport': 'كرة القدم',
        'branch': 'فرع اختبار',
        'plan': 'شهر',
        'finalAmount': '800.00',
        'currency': 'EGP',
        if (status == 'Approved') 'receiptId': name,
      },
  ],
};
void main() {
  testWidgets(
    'Family creation retries the identical key and locks editing after a lost response',
    (tester) async {
      final bodies = <Map<String, dynamic>>[];
      Map<String, dynamic>? created;
      await tester.pumpWidget(
        shell(
          Scaffold(
            body: FamilyRenewalChoices(
              enrollments: [
                {
                  'id': 'enrollment',
                  'player': 'طفل اختبار',
                  'sport': 'كرة القدم',
                  'branch': 'الفرع',
                  'plans': [
                    {
                      'id': 'plan',
                      'name': 'شهر',
                      'amount': '1000.00',
                      'currency': 'EGP',
                    },
                  ],
                },
              ],
              onQuote: (lines) async => {
                'quoteHash': 'hash',
                'originalAmount': '1000.00',
                'discountAmount': '0.00',
                'totalAmount': '1000.00',
                'currency': 'EGP',
                'items': <dynamic>[],
              },
              onCreate: (body) async {
                bodies.add(body);
                if (bodies.length == 1) throw Exception('INTERNAL');
                return {'id': 'family'};
              },
              onCreated: (value) => created = value,
            ),
          ),
        ),
      );
      await tester.tap(find.byType(CheckboxListTile));
      await tester.pumpAndSettle();
      await tester.tap(find.byType(DropdownButtonFormField<String>));
      await tester.pumpAndSettle();
      await tester.tap(find.text('شهر · 1,000 ج.م').last);
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.text('مراجعة إجمالي 1 تسجيل'));
      await tester.tap(find.text('مراجعة إجمالي 1 تسجيل'));
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.text('متابعة الدفع للأسرة'));
      await tester.tap(find.text('متابعة الدفع للأسرة'));
      await tester.pumpAndSettle();
      expect(find.textContaining('لم يصل تأكيد الطلب'), findsOneWidget);
      expect(find.textContaining('INTERNAL'), findsNothing);
      final edit = tester.widget<OutlinedButton>(
        find.widgetWithText(OutlinedButton, 'تعديل التسجيلات والباقات'),
      );
      expect(edit.onPressed, isNull);
      await tester.ensureVisible(find.text('إعادة تأكيد نفس الطلب'));
      await tester.tap(find.text('إعادة تأكيد نفس الطلب'));
      await tester.pumpAndSettle();
      expect(bodies.length, 2);
      expect(bodies.first, bodies.last);
      expect(bodies.first['idempotencyKey'], isNotEmpty);
      expect(created, {'id': 'family'});
    },
  );
  for (final width in [360.0, 430.0]) {
    testWidgets(
      'Approved family shows both receipts at $width and large text',
      (tester) async {
        tester.view.physicalSize = Size(width, 1000);
        tester.view.devicePixelRatio = 1;
        addTearDown(tester.view.resetPhysicalSize);
        addTearDown(tester.view.resetDevicePixelRatio);
        final auth = await logged(
          (m, p, b, t) async => detail(status: 'Approved'),
        );
        await tester.pumpWidget(
          shell(
            MediaQuery(
              data: MediaQueryData(
                size: Size(width, 1000),
                textScaler: const TextScaler.linear(1.5),
              ),
              child: NativeFamilyRenewalRequest(auth: auth, id: 'family'),
            ),
          ),
        );
        await tester.pumpAndSettle();
        expect(find.text('تم تجديد الأسرة'), findsOneWidget);
        expect(find.text('عرض الإيصال'), findsNWidgets(2));
        expect(find.text('إلغاء طلب التجديد'), findsNothing);
        expect(tester.takeException(), isNull);
      },
    );
  }
  testWidgets(
    'Cancel requires confirmation and sends the displayed revision only',
    (tester) async {
      Object? sent;
      var status = 'Pending';
      final auth = await logged((m, p, b, t) async {
        if (p == '/api/v1/payment-settings') return {'configured': false};
        if (m == 'POST') {
          sent = b;
          status = 'Cancelled';
        }
        return detail(status: status);
      });
      await tester.pumpWidget(
        shell(NativeFamilyRenewalRequest(auth: auth, id: 'family')),
      );
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(find.text('إلغاء طلب التجديد'), 300);
      await tester.tap(find.text('إلغاء طلب التجديد'));
      await tester.pumpAndSettle();
      expect(sent, isNull);
      expect(find.byType(AlertDialog), findsOneWidget);
      await tester.tap(find.text('تأكيد الإلغاء'));
      await tester.pumpAndSettle();
      expect(sent, {'revision': 2});
      expect(find.text('ملغي'), findsOneWidget);
    },
  );
  testWidgets(
    'Staff family view remains read-only and does not expose guardian upload',
    (tester) async {
      final auth = await logged((m, p, b, t) async => detail());
      await tester.pumpWidget(
        shell(
          NativeFamilyRenewalRequest(auth: auth, id: 'family', staff: true),
        ),
      );
      await tester.pumpAndSettle();
      expect(
        find.textContaining('الاعتماد الجماعي متاحة من لوحة الويب'),
        findsOneWidget,
      );
      expect(find.text('إلغاء طلب التجديد'), findsNothing);
      expect(find.text('رفع إيصال الدفع'), findsNothing);
    },
  );
  testWidgets(
    'Unavailable family fails closed without child names or financial actions',
    (tester) async {
      final auth = await logged(
        (m, p, b, t) async =>
            throw const ApiFailure(404, 'طلب الأسرة غير متاح.'),
      );
      await tester.pumpWidget(
        shell(NativeFamilyRenewalRequest(auth: auth, id: 'other')),
      );
      await tester.pumpAndSettle();
      expect(find.text('آدم اختبار'), findsNothing);
      expect(find.text('إلغاء طلب التجديد'), findsNothing);
      expect(find.textContaining('غير متاح'), findsWidgets);
    },
  );
}
