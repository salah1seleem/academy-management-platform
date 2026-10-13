import 'package:academy_mobile/features/guardian/family_renewal_quote.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'guardian_test.dart' show shell;

final enrollments = [
  for (final sport in ['قدم', 'سباحة'])
    <String, dynamic>{
      'id': sport,
      'player': 'طفل اختبار',
      'sport': sport,
      'branch': 'الفرع',
      'plans': [
        <String, dynamic>{
          'id': 'p-$sport',
          'name': 'باقة $sport',
          'amount': '1000.00',
          'currency': 'EGP',
        },
      ],
    },
  <String, dynamic>{
    'id': 'none',
    'player': 'شقيق اختبار',
    'sport': 'قدم',
    'branch': 'الفرع',
    'plans': [],
  },
];
void main() {
  for (final width in [360.0, 430.0]) {
    testWidgets(
      'Family preview preserves sports and selections at $width without payment',
      (tester) async {
        tester.view.physicalSize = Size(width, 900);
        tester.view.devicePixelRatio = 1;
        addTearDown(tester.view.resetPhysicalSize);
        addTearDown(tester.view.resetDevicePixelRatio);
        List<Map<String, dynamic>>? sent;
        await tester.pumpWidget(
          shell(
            Scaffold(
              body: FamilyRenewalChoices(
                enrollments: enrollments,
                onQuote: (lines) async {
                  sent = lines;
                  return {
                    'originalAmount': '2000.00',
                    'discountAmount': '0.00',
                    'totalAmount': '2000.00',
                    'currency': 'EGP',
                    'items': <dynamic>[],
                  };
                },
              ),
            ),
          ),
        );
        await tester.pumpAndSettle();
        for (var i = 0; i < 2; i++) {
          final box = find.byType(CheckboxListTile).at(i);
          await tester.ensureVisible(box);
          await tester.tap(box);
          await tester.pumpAndSettle();
          final plan = find.byType(DropdownButtonFormField<String>).at(i);
          await tester.ensureVisible(plan);
          await tester.tap(plan);
          await tester.pumpAndSettle();
          await tester.tap(
            find.text('باقة ${i == 0 ? 'قدم' : 'سباحة'} · 1000.00 ج.م').last,
          );
          await tester.pumpAndSettle();
        }
        final button = find.widgetWithText(
          FilledButton,
          'مراجعة إجمالي 2 تسجيل',
        );
        await tester.ensureVisible(button);
        await tester.tap(button);
        await tester.pumpAndSettle();
        expect(sent, [
          {'sportEnrollmentId': 'قدم', 'subscriptionPlanId': 'p-قدم'},
          {'sportEnrollmentId': 'سباحة', 'subscriptionPlanId': 'p-سباحة'},
        ]);
        expect(find.text('إجمالي تجديد الأسرة'), findsOneWidget);
        expect(find.byType(CheckboxListTile), findsNothing);
        expect(tester.takeException(), isNull);
        final edit = find.text('تعديل التسجيلات والباقات');
        await tester.ensureVisible(edit);
        await tester.tap(edit);
        await tester.pumpAndSettle();
        expect(find.byType(DropdownButtonFormField<String>), findsNWidgets(2));
        expect(find.text('إجمالي تجديد الأسرة'), findsNothing);
      },
    );
  }
  testWidgets('Empty family and unpublished plan cannot be submitted', (
    tester,
  ) async {
    await tester.pumpWidget(
      shell(
        Scaffold(
          body: FamilyRenewalChoices(
            enrollments: [],
            onQuote: (_) async => throw StateError('must not be called'),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('لا توجد تسجيلات متاحة لأبنائك.'), findsOneWidget);
    expect(
      tester.widget<FilledButton>(find.byType(FilledButton)).onPressed,
      isNull,
    );
    await tester.pumpWidget(
      shell(
        Scaffold(
          body: FamilyRenewalChoices(
            key: const ValueKey('other'),
            enrollments: [enrollments.last],
            onQuote: (_) async => throw StateError('must not be called'),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(
      tester.widget<CheckboxListTile>(find.byType(CheckboxListTile)).onChanged,
      isNull,
    );
    expect(
      tester.widget<FilledButton>(find.byType(FilledButton)).onPressed,
      isNull,
    );
  });
  testWidgets(
    'Failed preview retains choices and hides technical errors at large text',
    (tester) async {
      tester.view.physicalSize = const Size(360, 1000);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      var attempts = 0;
      await tester.pumpWidget(
        shell(
          Scaffold(
            body: MediaQuery(
              data: const MediaQueryData(textScaler: TextScaler.linear(1.5)),
              child: FamilyRenewalChoices(
                enrollments: [enrollments.first],
                onQuote: (_) async {
                  attempts++;
                  throw StateError('INTERNAL');
                },
              ),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byType(CheckboxListTile));
      await tester.pumpAndSettle();
      final plan = find.byType(DropdownButtonFormField<String>);
      await tester.ensureVisible(plan);
      await tester.tap(plan);
      await tester.pumpAndSettle();
      await tester.tap(find.text('باقة قدم · 1000.00 ج.م').last);
      await tester.pumpAndSettle();
      final button = find.widgetWithText(FilledButton, 'مراجعة إجمالي 1 تسجيل');
      await tester.ensureVisible(button);
      await tester.tap(button);
      await tester.pumpAndSettle();
      expect(attempts, 1);
      expect(find.textContaining('تعذر مراجعة الباقات'), findsOneWidget);
      expect(find.textContaining('INTERNAL'), findsNothing);
      expect(find.byType(DropdownButtonFormField<String>), findsOneWidget);
      expect(find.text('إجمالي تجديد الأسرة'), findsNothing);
      expect(tester.takeException(), isNull);
    },
  );
}
