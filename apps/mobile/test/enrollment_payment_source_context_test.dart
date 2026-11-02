import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:academy_mobile/features/admin/native_enrollment_payment_read.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

void main() {
  for (final width in [375.0, 440.0]) {
    for (final scale in [1.0, 2.0]) {
      for (final dark in [true, false]) {
        testWidgets(
          'enrollment source context is readable at $width scale $scale dark=$dark',
          (t) async {
            t.view.physicalSize = Size(width, 900);
            t.view.devicePixelRatio = 1;
            addTearDown(t.view.resetPhysicalSize);
            addTearDown(t.view.resetDevicePixelRatio);
            final calls = <String>[];
            final auth = await authenticate('AcademyOwner', (
              m,
              p,
              b,
              token,
            ) async {
              calls.add('$m $p');
              return {
                'id': 'source',
                'child': 'طفل مصدر اصطناعي باسم طويل لاختبار التفاف السطر',
                'guardian': 'ولي أمر اصطناعي',
                'sport': 'كرة القدم',
                'branch': 'فرع العرض الاصطناعي',
                'status': 'Pending',
                'originalAmount': 1000,
                'discountAmount': 100,
                'amount': 900,
                'currency': 'EGP',
              };
            });
            await t.pumpWidget(
              app(
                Theme(
                  data: dark ? AcademyTheme.dark() : AcademyTheme.light(),
                  child: NativeEnrollmentPaymentRead(auth: auth, id: 'source'),
                ),
                scale: scale,
              ),
            );
            await t.pumpAndSettle();
            expect(
              find.text('طفل مصدر اصطناعي باسم طويل لاختبار التفاف السطر'),
              findsOneWidget,
            );
            expect(find.text('ولي الأمر: ولي أمر اصطناعي'), findsOneWidget);
            expect(
              find.text('كرة القدم · فرع العرض الاصطناعي'),
              findsOneWidget,
            );
            await t.scrollUntilVisible(
              find.text('المبلغ النهائي'),
              140,
              scrollable: find.byType(Scrollable).first,
            );
            expect(find.text('900 ج.م'), findsOneWidget);
            for (final label in [
              'الحالة',
              'السعر الأصلي',
              'الخصم',
              'المبلغ النهائي',
            ]) {
              expect(
                t
                    .widget<UiText>(
                      find.byWidgetPredicate(
                        (w) => w is UiText && w.data == label,
                      ),
                    )
                    .style
                    ?.color,
                isNull,
              );
            }
            expect(calls, [
              'GET /api/v1/manage/enrollment-requests/source/payment',
            ]);
            expect(t.takeException(), isNull);
            await auth.logout();
            await t.pumpAndSettle();
            expect(find.text('ولي الأمر: ولي أمر اصطناعي'), findsNothing);
            await t.pumpWidget(const SizedBox());
            auth.dispose();
          },
        );
      }
    }
  }
}
