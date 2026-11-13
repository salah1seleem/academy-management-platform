import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/admin/native_renewal.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/visibility.dart';
import 'v2_role_workspaces_test.dart' show app, authenticate;

const endpoint = '/api/v1/subscriptions/admin-renewals';
const review = '$endpoint/enrollments/period-alias';
const quotePath = '$endpoint/enrollments/enrollment/quote?planId=monthly';
const enrollment = {
  'id': 'enrollment',
  'player': 'لاعب اصطناعي',
  'sport': 'السباحة',
  'branch': 'مدينة نصر',
  'group': 'المجموعة أ',
};
const plan = {
  'id': 'monthly',
  'arabicName': 'باقة تدريب',
  'price': 900,
  'currency': 'EGP',
};
const reviewData = {
  'enrollment': enrollment,
  'plans': [plan],
  'canReadPayment': false,
};

Map<String, Object> quote(String enrollmentId) => {
  'enrollmentId': enrollmentId,
  'planId': 'monthly',
  'quoteHash': 'a' * 64,
  'start': '2026-11-01',
  'end': '2026-11-30',
  'sessions': 8,
  'originalAmount': 900,
  'discountAmount': 180,
  'finalAmount': 720,
  'currency': 'EGP',
};

Future<void> press(WidgetTester t, Finder target) async {
  await revealTestTarget(t, target);
  await t.tap(target);
  await t.pumpAndSettle();
}

Future<void> choosePlan(WidgetTester t) async {
  await press(t, find.byType(DropdownButtonFormField<String>));
  await t.tap(find.textContaining('باقة تدريب').last);
  await t.pumpAndSettle();
}

void main() {
  testWidgets(
    'reads the canonical enrollment quote and replays an uncertain write with the original body',
    (t) async {
      t.view.physicalSize = const Size(320, 850);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.resetPhysicalSize);
      addTearDown(t.view.resetDevicePixelRatio);
      final writes = <Object?>[];
      var readQuote = 0;
      final auth = await authenticate('AcademyOwner', (
        method,
        path,
        body,
        _,
      ) async {
        if (path == review) return reviewData;
        if (path == quotePath) {
          readQuote++;
          return quote('enrollment');
        }
        if (path == endpoint && method == 'POST') {
          writes.add(body);
          return writes.length == 1
              ? {}
              : {'renewalId': 'renewal', 'paymentId': 'payment'};
        }
        throw StateError('Unexpected renewal route $method $path');
      });
      await t.pumpWidget(
        app(
          NativeAdminRenewal(auth: auth, enrollmentId: 'period-alias'),
          scale: 2,
        ),
      );
      await t.pumpAndSettle();
      final create = find.widgetWithText(
        FilledButton,
        'إنشاء طلب التجديد والدفع',
      );
      await revealTestTarget(t, create);
      expect(t.widget<FilledButton>(create).onPressed, isNull);
      await choosePlan(t);
      final due = find.textContaining('المبلغ المطلوب 720');
      await revealTestTarget(t, due);
      expect(t.getRect(due).right, lessThanOrEqualTo(320));
      expect(readQuote, 1);
      expect(writes, isEmpty);
      await press(t, create);
      await press(t, find.text('إعادة إرسال طلب التجديد نفسه'));
      await revealTestTarget(t, find.text('تم إنشاء طلب التجديد.'));
      expect(
        writes,
        List.filled(2, {
          'sportEnrollmentId': 'enrollment',
          'subscriptionPlanId': 'monthly',
          'expectedQuoteHash': 'a' * 64,
        }),
      );
      expect(readQuote, 1);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'a mismatched quote blocks creation and a stale quote requires choosing again',
    (t) async {
      var reads = 0, writes = 0;
      final auth = await authenticate('AcademyAdmin', (
        method,
        path,
        body,
        _,
      ) async {
        if (path == review) return reviewData;
        if (path == quotePath) {
          return quote(++reads == 1 ? 'foreign' : 'enrollment');
        }
        if (path == endpoint && method == 'POST') {
          writes++;
          throw const ApiFailure(409, 'تغيّر السعر أو الفترة المتوقعة.');
        }
        throw StateError('Unexpected renewal route $method $path');
      });
      await t.pumpWidget(
        app(NativeAdminRenewal(auth: auth, enrollmentId: 'period-alias')),
      );
      await t.pumpAndSettle();
      await choosePlan(t);
      await revealTestTarget(t, find.text('تعذر تأكيد تفاصيل عرض التجديد.'));
      expect(find.text('إنشاء طلب التجديد والدفع'), findsNothing);
      expect(writes, 0);
      await press(t, find.text('إعادة قراءة العرض'));
      final create = find.widgetWithText(
        FilledButton,
        'إنشاء طلب التجديد والدفع',
      );
      await press(t, create);
      await revealTestTarget(t, find.text('تغيّر السعر أو الفترة المتوقعة.'));
      await revealTestTarget(t, create);
      expect(t.widget<FilledButton>(create).onPressed, isNull);
      expect(writes, 1);
      expect(reads, 2);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
