import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/guardian/daily_nutrition.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/academy_news.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart';
import 'guardian_test.dart' show logged;

Widget shell(Widget child) => MaterialApp(
  locale: const Locale('ar'),
  localizationsDelegates: GlobalMaterialLocalizations.delegates,
  supportedLocales: const [Locale('ar')],
  theme: guardianTheme(),
  home: Scaffold(body: SingleChildScrollView(child: child)),
);

void main() {
  testWidgets('Next session shows only its own resource context without a lane roster', (tester) async {
    await tester.pumpWidget(shell(NextSessionCard(childName: 'لاعب تجريبي', sport: {'group': 'براعم', 'branch': 'فرع الاختبار', 'nextSession': {'sessionDate': '2026-10-24', 'startTime': '17:00', 'endTime': '18:00', 'resource': 'المسبح · A'}}, onTap: () {})));
    expect(find.text('المسبح · A'), findsOneWidget);
    expect(find.textContaining('لاعب تجريبي'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
  testWidgets(
    'Child card keeps the published score, compact FIFA summary and subscription dates',
    (tester) async {
      await tester.binding.setSurfaceSize(const Size(390, 844));
      addTearDown(() => tester.binding.setSurfaceSize(null));
      final auth = AuthController(
        FakeApi((m, p, b, t) async => {}),
        MemoryStore(),
      );
      var opened = false;
      await tester.pumpWidget(
        shell(
          PremiumChildCard(
            auth: auth,
            today: '2026-09-28',
            onTap: () => opened = true,
            childData: {
              'id': 'p1',
              'arabicName': 'لاعب تجريبي',
              'dateOfBirth': '2018-05-01',
              'power': {'overallScore': 77.1, 'axes': <Object>[]},
              'sports': [
                {
                  'sport': 'كرة القدم',
                  'branch': 'فرع تجريبي',
                  'period': {
                    'status': 'Active',
                    'startDate': '2026-09-01',
                    'endDate': '2026-09-30',
                  },
                },
              ],
            },
          ),
        ),
      );
      expect(find.text('77'), findsOneWidget);
      expect(find.text('آخر تقييم منشور'), findsOneWidget);
      expect(find.textContaining('2026-09-30'), findsOneWidget);
      expect(
        tester.getSize(find.byKey(const ValueKey('child-p1'))).height,
        lessThan(250),
      );
      await tester.tap(find.text('عرض الملف'));
      expect(opened, isTrue);
      expect(tester.takeException(), isNull);
      auth.dispose();
    },
  );

  testWidgets(
    'Home daily meals retain pre/post training suggestions without prices or medical promises',
    (tester) async {
      final auth = await logged(
        (m, p, b, t) async => {
          'breakfast': {
            'id': 'b',
            'arabicName': 'شوفان',
            'imageReference': 'demo://nutrition/breakfast-1',
          },
          'lunch': {
            'id': 'l',
            'arabicName': 'دجاج وخضار',
            'imageReference': 'demo://nutrition/lunch-1',
          },
          'dinner': {
            'id': 'd',
            'arabicName': 'بيض وخبز',
            'imageReference': 'demo://nutrition/dinner-1',
          },
          'preTraining': {'id': 'pre', 'arabicName': 'موز وزبادي'},
          'postTraining': {'id': 'post', 'arabicName': 'حليب وفاكهة'},
          'isTrainingDay': true,
        },
      );
      await tester.pumpWidget(
        shell(
          DailyNutritionCard(
            auth: auth,
            playerId: 'p1',
            playerName: 'طفل تجريبي',
            today: '2026-09-28',
            compact: false,
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('اقتراحات اليوم'), findsOneWidget);
      expect(find.text('قبل التدريب · موز وزبادي'), findsOneWidget);
      expect(find.text('بعد التدريب · حليب وفاكهة'), findsOneWidget);
      expect(find.textContaining('خطة علاجية'), findsNothing);
      expect(find.textContaining('جنيه'), findsNothing);
      expect(find.text('شراء'), findsNothing);
      auth.dispose();
    },
  );

  testWidgets('An empty news response leaves no empty news heading on Home', (
    tester,
  ) async {
    final auth = await logged((m, p, b, t) async => []);
    await tester.pumpWidget(shell(NewsStrip(auth: auth)));
    await tester.pumpAndSettle();
    expect(find.text('من الأكاديمية'), findsNothing);
    auth.dispose();
  });
}
