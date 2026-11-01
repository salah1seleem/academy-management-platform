import 'package:academy_mobile/features/admin/native_enrollment.dart';
import 'package:academy_mobile/features/admin/native_renewal.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/cash_collection_choice.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'approved_native_renewal_test.dart' show payment, choose;
import 'v2_role_workspaces_test.dart' show authenticate, app;

void main() {
  for (final enabled in [false, true]) {
    testWidgets(
      'Cash readiness ${enabled ? 'without shift' : 'disabled'} is not a network error for both entry points',
      (t) async {
        var reads = 0, writes = 0;
        final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
          if (m != 'GET') {
            writes++;
            throw StateError('No business write expected');
          }
          reads++;
          return {
            'cashEnabled': enabled,
            'canPrepare': true,
            'sessions': <Json>[],
          };
        });
        for (final child in <Widget>[
          CashCollectionChoice(
            auth: auth,
            path: '/api/v1/manage/payments/payment/cash-options',
            value: null,
            onChange: (_) {},
          ),
          NativeRegistrationCashChoice(
            auth: auth,
            branch: 'branch',
            plan: 'plan',
            value: null,
            onChange: (_) {},
          ),
        ]) {
          await t.pumpWidget(app(GuardianPage(title: 'التحصيل', child: child)));
          await t.pumpAndSettle();
          expect(
            find.text(
              enabled ? 'لا توجد وردية مفتوحة لك' : 'التحصيل النقدي غير مفعّل',
            ),
            findsOneWidget,
          );
          expect(find.text('تعذر تحميل البيانات'), findsNothing);
          expect(find.byType(DropdownButtonFormField<String>), findsNothing);
          await t.tap(find.text('تحديث حالة النقدي'));
          await t.pumpAndSettle();
          expect(t.takeException(), isNull);
          await t.pumpWidget(const SizedBox());
        }
        expect(reads, 4);
        expect(writes, 0);
        auth.dispose();
      },
    );
  }
  testWidgets(
    'Owner renewal cash can refresh from disabled to available and requires an explicit personal shift',
    (t) async {
      var enabled = false, writes = 0;
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        if (m != 'GET') {
          writes++;
          throw StateError('No collection is authorized');
        }
        if (p.endsWith('/cash-options')) {
          return {
            'cashEnabled': enabled,
            'canPrepare': true,
            'sessions': enabled
                ? [
                    {'id': 'shift', 'name': 'ورديتي', 'branch': 'فرع اصطناعي'},
                  ]
                : <Json>[],
          };
        }
        return payment;
      });
      await t.pumpWidget(app(NativeRenewalPayment(auth: auth, id: 'payment')));
      await t.pumpAndSettle();
      await choose(t, 'طريقة التحصيل', 'نقدي من ورديتي');
      expect(find.text('التحصيل النقدي غير مفعّل'), findsOneWidget);
      final submit = find.widgetWithText(
        FilledButton,
        'تأكيد التحصيل وإصدار الإيصال',
      );
      expect(t.widget<FilledButton>(submit).onPressed, isNull);
      enabled = true;
      await t.ensureVisible(find.text('تحديث حالة النقدي'));
      await t.tap(find.text('تحديث حالة النقدي'));
      await t.pumpAndSettle();
      expect(find.text('التحصيل النقدي غير مفعّل'), findsNothing);
      expect(t.widget<FilledButton>(submit).onPressed, isNull);
      await choose(t, 'وردية التحصيل', 'ورديتي · فرع اصطناعي');
      expect(t.widget<FilledButton>(submit).onPressed, isNotNull);
      expect(writes, 0);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
