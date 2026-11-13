import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'shared_ui_quality_test.dart' show qualityFrame;
import 'support/visibility.dart';
import 'v2_role_workspaces_test.dart' show authenticate;

const beneficiary = 'عمر مثال الاختبار';
const blocked =
    'يوجد أكثر من تسجيل بهذه البيانات. تواصل مع الأكاديمية لتحديد التسجيل.';
const search = 'بحث عن لاعب';

Map<String, Object?> result(String sport, String branch, String? reference) => {
  'name': beneficiary,
  'sport': sport,
  'branch': branch,
  'reference': reference,
};

void main() {
  for (final (legacy, width, scale) in [
    (false, 320.0, 2.0),
    (true, 390.0, 1.0),
  ]) {
    testWidgets(
      'only distinct enrollment actions are offered (legacy $legacy)',
      (t) async {
        t.view.devicePixelRatio = 1;
        t.view.physicalSize = Size(width, 844);
        addTearDown(t.view.resetPhysicalSize);
        addTearDown(t.view.resetDevicePixelRatio);
        final calls = <Object?>[];
        final auth = await authenticate('Guardian', (_, path, body, _) async {
          if (path.endsWith('/search')) {
            return [
              result('كرة القدم', 'فرع مدينة نصر', legacy ? 'old-first' : null),
              if (legacy) result('كرة القدم', 'فرع مدينة نصر', 'old-second'),
              result('السباحة', 'فرع التجمع الخامس', 'distinct'),
            ];
          }
          expect(path, endsWith('/resolve'));
          calls.add(body);
          return {
            'playerDisplayName': beneficiary,
            'sport': 'السباحة',
            'branch': 'فرع التجمع الخامس',
            'plans': [
              {
                'id': 'plan',
                'arabicName': 'باقة سباحة',
                'price': 600,
                'currency': 'EGP',
                'durationDays': 30,
              },
            ],
          };
        });
        addTearDown(auth.dispose);
        await t.pumpWidget(
          qualityFrame(
            Builder(
              builder: (context) => MediaQuery(
                data: MediaQuery.of(context)
                    .copyWith(textScaler: TextScaler.linear(scale)),
                child: ExternalRenewal(auth: auth, environment: 'Demo'),
              ),
            ),
          ),
        );
        await t.enterText(find.byType(TextField), 'عمر مثال');
        await t.tap(find.text(search));
        await t.pumpAndSettle();
        expect(find.text(blocked), findsOneWidget);
        final blockedCard = find.ancestor(
          of: find.text(blocked),
          matching: find.byType(GCard),
        );
        expect(t.widget<GCard>(blockedCard).onTap, isNull);
        await t.tap(find.text(blocked));
        await t.pumpAndSettle();
        expect(calls, isEmpty);
        await revealTestTarget(t, find.byType(GLink));
        expect(find.byType(GLink), findsOneWidget);
        await t.tap(find.byType(GLink));
        await t.pumpAndSettle();
        expect(calls, [
          {'reference': 'distinct'},
        ]);
        expect(find.byType(PlanPicker), findsOneWidget);
        const registration = 'السباحة · فرع التجمع الخامس';
        expect(find.text(registration), findsOneWidget);
        final choose = find.widgetWithText(FilledButton, 'اختيار الباقة');
        await revealTestTarget(t, choose);
        await t.tap(choose);
        await t.pumpAndSettle();
        expect(find.byType(RenewalReview), findsOneWidget);
        expect(find.text(registration), findsOneWidget);
        await t.tap(find.byType(BackButton).last);
        await t.pumpAndSettle();
        await t.tap(find.byType(BackButton).last);
        await t.pumpAndSettle();
        await revealTestTarget(t, find.text(blocked));
        await revealTestTarget(t, find.byType(GLink));
        expect(find.byType(GLink), findsOneWidget);
        await revealTestTarget(t, find.byType(TextField));
        expect(
          t.widget<TextField>(find.byType(TextField)).controller!.text,
          'عمر مثال',
        );
        final field = find.byType(TextField);
        expect(t.widget<TextField>(field).enabled, isTrue);
        await t.tap(field);
        await t.pump();
        await t.enterText(field, 'عمر آخر');
        await t.pumpAndSettle();
        expect(t.widget<TextField>(field).controller!.text, 'عمر آخر');
        expect(find.text(blocked), findsNothing);
        expect(find.byType(GLink), findsNothing);
      },
    );
  }

  testWidgets(
    'a changed branch in the resolved response requires a fresh search',
    (t) async {
      final auth = await authenticate('Guardian', (_, path, _, _) async {
        if (path.endsWith('/search')) {
          return [result('السباحة', 'فرع التجمع الخامس', 'distinct')];
        }
        expect(path, endsWith('/resolve'));
        return {
          'playerDisplayName': beneficiary,
          'sport': 'السباحة',
          'branch': 'فرع آخر',
          'plans': <Object>[],
        };
      });
      addTearDown(auth.dispose);
      await t.pumpWidget(
        qualityFrame(ExternalRenewal(auth: auth, environment: 'Demo')),
      );
      await t.enterText(find.byType(TextField), 'عمر مثال');
      await t.tap(find.text(search));
      await t.pumpAndSettle();
      await t.tap(find.byType(GLink));
      await t.pumpAndSettle();
      expect(
        find.text('انتهى الاختيار أو تغيّر التسجيل. أعد البحث.'),
        findsOneWidget,
      );
      expect(find.byType(GLink), findsNothing);
      expect(find.byType(PlanPicker), findsNothing);
    },
  );

  testWidgets(
    'validation, empty results, retry, editing and expired selections',
    (t) async {
      var searches = 0;
      final auth = await authenticate('Guardian', (_, path, body, _) async {
        if (path.endsWith('/resolve')) {
          throw const ApiFailure(404, 'طلب غير متاح');
        }
        expect(path, endsWith('/search'));
        searches++;
        if (searches == 1) return <Object>[];
        if (searches == 2) {
          throw const ApiFailure(503, 'تعذر البحث. أعد المحاولة.');
        }
        return [result('السباحة', 'فرع التجمع الخامس', 'distinct')];
      });
      addTearDown(auth.dispose);
      await t.pumpWidget(
        qualityFrame(ExternalRenewal(auth: auth, environment: 'Demo')),
      );
      await t.tap(find.text(search));
      await t.pumpAndSettle();
      expect(
        find.text('اكتب ثلاثة أحرف على الأقل من اسم اللاعب.'),
        findsOneWidget,
      );
      expect(searches, 0);

      final field = find.byType(TextField);
      await t.enterText(field, '  عمر  ');
      await t.testTextInput.receiveAction(TextInputAction.search);
      await t.pumpAndSettle();
      expect(find.text('لا توجد تسجيلات مطابقة للاسم.'), findsOneWidget);
      await t.tap(find.text(search));
      await t.pumpAndSettle();
      expect(find.text('تعذر البحث. أعد المحاولة.'), findsOneWidget);
      await t.tap(find.text(search));
      await t.pumpAndSettle();
      expect(find.byType(GLink), findsOneWidget);
      await t.enterText(field, 'اسم آخر');
      await t.pumpAndSettle();
      expect(find.byType(GLink), findsNothing);
      await t.tap(find.text(search));
      await t.pumpAndSettle();
      await t.tap(find.byType(GLink));
      await t.pumpAndSettle();
      expect(
        find.text('انتهى الاختيار أو تغيّر التسجيل. أعد البحث.'),
        findsOneWidget,
      );
      expect(find.byType(GLink), findsNothing);
      expect(find.byType(PlanPicker), findsNothing);
      expect(searches, 4);
    },
  );
}
