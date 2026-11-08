import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_profile.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/guardian/guardian_report.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'guardian_test.dart' show logged;

double contrast(Color foreground, Color background) {
  final a = Color.alphaBlend(foreground, background).computeLuminance();
  final b = background.computeLuminance();
  return a > b ? (a + .05) / (b + .05) : (b + .05) / (a + .05);
}

Color renderedColor(WidgetTester tester, String text) {
  final finder = find.text(text);
  final widget = tester.widget<Text>(finder);
  return DefaultTextStyle.of(tester.element(finder)).style
      .merge(widget.style)
      .color!;
}

Widget page(Widget child, bool light) => MaterialApp(
  locale: const Locale('ar'),
  supportedLocales: const [Locale('ar')],
  localizationsDelegates: GlobalMaterialLocalizations.delegates,
  theme: light ? AcademyTheme.light() : AcademyTheme.dark(),
  home: child,
);

void main() {
  for (final light in [true, false]) {
    testWidgets(
      'Home star count, published zero and status remain legible light=$light',
      (t) async {
        final auth = await logged((m, p, b, token) async => {});
        await t.pumpWidget(
          page(
            Scaffold(
              body: SingleChildScrollView(
                child: PremiumChildCard(
                  auth: auth,
                  childData: {
                    'id': 'child',
                    'arabicName': 'لاعب اصطناعي',
                    'dateOfBirth': '2018-03-12',
                    'monthlyStars': 2,
                    'sports': [
                      {
                        'id': 'football',
                        'sport': 'كرة القدم',
                        'branch': 'فرع الاختبار',
                        'performance': {
                          'meanPerformance': 0,
                          'scoredCriteria': 1,
                          'applicableCriteria': 1,
                          'isFootballReport': true,
                        },
                        'period': {
                          'plan': 'باقة الاختبار',
                          'status': 'Active',
                          'remainingSessions': 4,
                        },
                      },
                    ],
                  },
                  today: '2026-10-22',
                  onTap: () {},
                ),
              ),
            ),
            light,
          ),
        );
        await t.pumpAndSettle();
        final card = t.widget<Container>(
          find.byKey(const ValueKey('child-child')),
        );
        final stops = [(card.decoration! as BoxDecoration).color!];
        for (final text in ['★ ★ ', '0', 'متوسط التقييم', 'فعال']) {
          for (final background in stops) {
            final effectiveBackground = text == '0' || text == 'متوسط التقييم'
                ? const Color(0xFF071B32)
                : text == 'فعال'
                ? Color.alphaBlend(
                    (t
                                .widget<Container>(
                                  find
                                      .ancestor(
                                        of: find.text(text),
                                        matching: find.byType(Container),
                                      )
                                      .first,
                                )
                                .decoration!
                            as BoxDecoration)
                        .color!,
                    background,
                  )
                : background;
            expect(
              contrast(renderedColor(t, text), effectiveBackground),
              greaterThanOrEqualTo(text == '0' ? 3 : 4.5),
              reason: text,
            );
          }
        }
        expect(find.text('4 حصص متبقية'), findsOneWidget);
        expect(find.textContaining('معايير · آخر تقييم منشور'), findsNothing);
        expect(t.takeException(), isNull);
        auth.dispose();
      },
    );
    testWidgets(
      'Profile identity stays legible across ${light ? 'light' : 'dark'} hero gradient',
      (t) async {
        final auth = await logged(
          (m, p, b, token) async => {
            'player': {
              'id': 'child',
              'arabicName': 'لاعب اصطناعي للاختبار',
              'dateOfBirth': '2018-03-12',
            },
            'enrollments': [
              {
                'sport': 'كرة القدم',
                'branch': 'مدينة نصر',
                'group': 'مجموعة الاختبار',
                'registrationFormat': 'Group',
              },
            ],
            'compensations': [],
          },
        );
        await t.pumpWidget(
          page(
            ChildProfile(
              auth: auth,
              environment: 'Demo',
              playerId: 'child',
              today: '2026-10-22',
            ),
            light,
          ),
        );
        await t.pumpAndSettle();
        final hero = t.widget<Container>(
          find.byKey(const ValueKey('guardian-profile-hero-surface')),
        );
        final stops = (hero.decoration! as BoxDecoration).gradient!.colors;
        for (final text in [
          'لاعب اصطناعي للاختبار',
          '8 سنة · كرة القدم · مدينة نصر',
        ]) {
          for (final background in stops) {
            expect(
              contrast(renderedColor(t, text), background),
              greaterThanOrEqualTo(4.5),
              reason: text,
            );
          }
        }
        expect(t.takeException(), isNull);
        auth.dispose();
      },
    );

    testWidgets(
      'Report keeps zero and real score bands readable in ${light ? 'light' : 'dark'} appearance',
      (t) async {
        final auth = await logged(
          (m, p, b, token) async => {
            'player': 'لاعب التقييم',
            'footballPosition': 'حارس مرمى',
            'isFootballReport': true,
            'reportingPeriod': 'أسبوعي',
            'evaluationDate': '2026-10-22',
            'overallScore': 60,
            'axes': [],
            'criteria': [
              for (final score in [0, 65, 80, 95])
                {'name': 'معيار $score', 'score': score},
              {'name': 'معيار غير مسجل', 'score': null},
            ],
          },
        );
        await t.pumpWidget(page(PlayerReport(auth: auth, id: 'report'), light));
        await t.pumpAndSettle();
        final background = light
            ? AcademyTheme.light().colorScheme.surface
            : AcademyTheme.dark().colorScheme.surface;
        expect(
          contrast(renderedColor(t, 'حارس مرمى'), background),
          greaterThanOrEqualTo(4.5),
        );
        for (final score in [0, 65, 80, 95]) {
          await t.scrollUntilVisible(find.text('معيار $score'), 150);
          expect(
            contrast(renderedColor(t, '$score'), background),
            greaterThanOrEqualTo(4.5),
            reason: 'score $score',
          );
        }
        expect(find.text('معيار غير مسجل'), findsNothing);
        expect(t.takeException(), isNull);
        auth.dispose();
      },
    );
  }
}
