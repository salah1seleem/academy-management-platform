import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/family_nutrition_rail.dart';
import 'package:academy_mobile/features/guardian/guardian_content.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'guardian_test.dart' show logged;

const children = <Json>[
  {'id': 'a', 'arabicName': 'طفل العرض الأول'},
  {'id': 'b', 'arabicName': 'طفلة العرض الثانية'},
];
Json meal(String id) => {
  'id': id,
  'arabicName': 'وجبة اصطناعية $id',
  'imageReference': '/demo-assets/nutrition/breakfast-1.jpg',
  'arabicDescription': 'وصف غذائي تجريبي',
  'servingDescription': 'حصة تجريبية',
};
Widget app(Widget child, {bool light = true, double scale = 1}) => MaterialApp(
  locale: const Locale('ar'),
  supportedLocales: const [Locale('ar')],
  localizationsDelegates: GlobalMaterialLocalizations.delegates,
  theme: light ? AcademyTheme.light() : AcademyTheme.dark(),
  builder: (c, w) => MediaQuery(
    data: MediaQuery.of(c).copyWith(textScaler: TextScaler.linear(scale)),
    child: w!,
  ),
  home: Scaffold(body: SingleChildScrollView(child: child)),
);
void main() {
  for (final light in [true, false]) {
    for (final width in [375.0, 440.0]) {
      testWidgets(
        'meal image, child, source and RTL rail fit $width light=$light at 200%',
        (t) async {
          t.view.physicalSize = Size(width, 956);
          t.view.devicePixelRatio = 1;
          addTearDown(t.view.resetPhysicalSize);
          addTearDown(t.view.resetDevicePixelRatio);
          final paths = <String>[];
          final auth = await logged((m, p, b, token) async {
            paths.add('$m $p');
            return p.contains('/recommendations')
                ? {
                    'breakfast': meal(p.contains('/a/') ? 'a' : 'b'),
                    'isTrainingDay': true,
                  }
                : p.contains('/nutrition?')
                ? [meal('a')]
                : meal('a');
          });
          await t.pumpWidget(
            app(
              FamilyNutritionRail(
                auth: auth,
                children: children,
                today: '2026-10-22',
              ),
              light: light,
              scale: 2,
            ),
          );
          await t.pumpAndSettle();
          final first = find.byKey(const ValueKey('family-meal-0')),
              second = find.byKey(const ValueKey('family-meal-1'));
          expect(t.getTopLeft(first).dx, greaterThan(t.getTopLeft(second).dx));
          expect(t.getSize(first).width, closeTo(width * .58, .1));
          final image = t.widget<DemoImage>(
            find.descendant(of: first, matching: find.byType(DemoImage)),
          );
          expect(image.borderRadius, BorderRadius.zero);
          expect(image.height, closeTo(width * .58 * .75, .1));
          expect(
            find.descendant(
              of: first,
              matching: find.text('اقتراح ليوم 2026-10-22'),
            ),
            findsOneWidget,
          );
          expect(find.text('وجبة اصطناعية a'), findsNothing);
          expect(find.text('طفل العرض الأول'), findsOneWidget);
          expect(
            paths,
            containsAll([
              'GET /api/v1/guardian/children/a/nutrition/recommendations?date=2026-10-22',
              'GET /api/v1/guardian/children/b/nutrition/recommendations?date=2026-10-22',
            ]),
          );
          await t.tap(first);
          await t.pumpAndSettle();
          expect(find.byType(NutritionLibrary), findsOneWidget);
          expect(
            t.widget<NutritionLibrary>(find.byType(NutritionLibrary)).playerId,
            'a',
          );
          expect(
            paths,
            contains('GET /api/v1/guardian/nutrition?category=Breakfast'),
          );
          expect(paths.every((p) => p.startsWith('GET ')), isTrue);
          expect(t.takeException(), isNull);
          await t.pumpWidget(const SizedBox());
          auth.dispose();
        },
      );
    }
  }
  testWidgets(
    'one unavailable child does not hide another meal and failure retry stays GET only',
    (t) async {
      var fail = true;
      final paths = <String>[];
      final auth = await logged((m, p, b, token) async {
        paths.add('$m $p');
        if (p.contains('/b/') && fail) {
          throw const ApiFailure(503, 'Technical transport error');
        }
        return {'breakfast': meal(p.contains('/a/') ? 'a' : 'b')};
      });
      await t.pumpWidget(
        app(
          FamilyNutritionRail(
            auth: auth,
            children: children,
            today: '2026-10-22',
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(
        find.descendant(
          of: find.byKey(const ValueKey('family-meal-0')),
          matching: find.text('اقتراح ليوم 2026-10-22'),
        ),
        findsOneWidget,
      );
      expect(find.text('تعذر تحميل وجبات اليوم'), findsOneWidget);
      expect(find.text('Technical transport error'), findsNothing);
      await t.drag(
        find.byKey(const ValueKey('family-meals-rail')),
        const Offset(500, 0),
      );
      await t.pumpAndSettle();
      fail = false;
      await t.tap(find.text('إعادة المحاولة'));
      await t.pumpAndSettle();
      expect(
        find.descendant(
          of: find.byKey(const ValueKey('family-meal-1')),
          matching: find.text('اقتراح ليوم 2026-10-22'),
        ),
        findsOneWidget,
      );
      expect(paths.every((p) => p.startsWith('GET ')), isTrue);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets('logout hides cards before delayed private reads complete', (
    t,
  ) async {
    final delayed = Completer<Object?>();
    final auth = await logged((m, p, b, token) => delayed.future);
    await t.pumpWidget(
      app(
        FamilyNutritionRail(
          auth: auth,
          children: children,
          today: '2026-10-22',
        ),
      ),
    );
    await t.pump();
    await auth.logout();
    await t.pump();
    delayed.complete({'breakfast': meal('private')});
    await t.pumpAndSettle();
    expect(find.text('وجبة اصطناعية private'), findsNothing);
    expect(find.byKey(const ValueKey('family-meals-rail')), findsNothing);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  testWidgets(
    'no suggestions opens the scoped library without inventing a meal',
    (t) async {
      final paths = <String>[];
      final auth = await logged((m, p, b, token) async {
        paths.add(p);
        return p.contains('/recommendations') ? <String, dynamic>{} : [];
      });
      await t.pumpWidget(
        app(
          FamilyNutritionRail(
            auth: auth,
            children: [children.first],
            today: '2026-10-22',
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(
        find.text('لا توجد اقتراحات مناسبة حاليًا'),
        findsOneWidget,
        reason: t
            .widgetList<Text>(find.byType(Text))
            .map((w) => w.data)
            .join(' | '),
      );
      await t.tap(find.text('عرض مكتبة التغذية'));
      await t.pumpAndSettle();
      expect(
        paths.any(
          (p) => p.contains('/nutrition?') || p.contains('/nutrition/'),
        ),
        isTrue,
      );
      expect(paths.any((p) => p.contains('/guardian/nutrition/')), isFalse);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'partial meal without an id is not invented as an actionable meal',
    (t) async {
      final auth = await logged(
        (m, p, b, token) async => <String, dynamic>{
          'breakfast': <String, dynamic>{'arabicName': 'وجبة بلا مصدر'},
        },
      );
      await t.pumpWidget(
        app(
          FamilyNutritionRail(
            auth: auth,
            children: [children.first],
            today: '2026-10-22',
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('وجبة بلا مصدر'), findsNothing);
      expect(find.text('لا توجد اقتراحات مناسبة حاليًا'), findsOneWidget);
      expect(find.text('عرض مكتبة التغذية'), findsOneWidget);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'one dated card per child stays aligned with enlarged text and different meals',
    (t) async {
      t.view.physicalSize = const Size(375, 956);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.resetPhysicalSize);
      addTearDown(t.view.resetDevicePixelRatio);
      final auth = await logged(
        (m, p, b, token) async => {
          'breakfast': {...meal('a'), 'arabicName': 'شوفان'},
          'lunch': {
            ...meal('b'),
            'arabicName': 'مكرونة ولحم قليل الدهن وسلطة خضراء',
          },
        },
      );
      await t.pumpWidget(
        app(
          FamilyNutritionRail(
            auth: auth,
            children: children,
            today: '2026-10-22',
          ),
          scale: 2,
        ),
      );
      await t.pumpAndSettle();
      final a = find.byKey(const ValueKey('family-meal-0')),
          b = find.byKey(const ValueKey('family-meal-1'));
      expect(t.getSize(a).height, t.getSize(b).height);
      expect(t.getSize(a).height, greaterThan(200));
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
