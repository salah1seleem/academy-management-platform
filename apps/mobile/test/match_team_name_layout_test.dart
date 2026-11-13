import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:academy_mobile/features/shared/match_experience.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  const team = 'فريق النجوم الاصطناعي للناشئين';
  const opponent = 'أكاديمية المنافس الاصطناعي ذات الاسم الطويل جدًا للناشئين';
  testWidgets(
    'A zero belongs to the team on the right, and history notes stay folded',
    (t) async {
      final semantics = t.ensureSemantics();
      t.view.physicalSize = const Size(390, 844);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.reset);
      const note = 'التجمع قبل بداية المباراة بنصف ساعة.';
      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          home: const Scaffold(
            body: SingleChildScrollView(
              child: MatchResultCard(
                data: {
                  'team': team,
                  'opponent': opponent,
                  'tournament': 'بطولة العرض',
                  'matchDate': '2026-10-22',
                  'startTime': '18:00',
                  'status': 'Completed',
                  'resultFinalized': true,
                  'ourScore': 0,
                  'opponentScore': 2,
                  'notes': note,
                },
              ),
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('خسارة'), findsOneWidget);
      final zero = find.widgetWithText(UiText, '0');
      final two = find.widgetWithText(UiText, '2');
      expect(t.getCenter(zero).dx, greaterThan(t.getCenter(two).dx));
      expect(find.bySemanticsLabel('$team: 0'), findsOneWidget);
      expect(find.bySemanticsLabel('$opponent: 2'), findsOneWidget);
      semantics.dispose();
      expect(find.text(note), findsNothing);
      await t.tap(find.text('ملاحظات المباراة'));
      await t.pumpAndSettle();
      expect(find.text(note), findsOneWidget);
      expect(t.takeException(), isNull);
    },
  );
  for (final width in [360.0, 390.0, 440.0]) {
    for (final scale in [1.0, 2.0]) {
      for (final light in [false, true]) {
        testWidgets(
          'Match names stay bounded and available in full: $width/$scale/$light',
          (t) async {
            await t.binding.setSurfaceSize(Size(width, 900));
            addTearDown(() => t.binding.setSurfaceSize(null));
            await t.pumpWidget(
              MaterialApp(
                locale: const Locale('ar'),
                supportedLocales: const [Locale('ar')],
                localizationsDelegates: GlobalMaterialLocalizations.delegates,
                theme: light ? AcademyTheme.light() : AcademyTheme.dark(),
                builder: (context, child) => MediaQuery(
                  data: MediaQuery.of(context)
                      .copyWith(textScaler: TextScaler.linear(scale)),
                  child: child!,
                ),
                home: const Scaffold(
                  body: SingleChildScrollView(
                    child: MatchResultCard(
                      data: {
                        'matchDate': '2026-10-22',
                        'startTime': '18:00',
                        'tournament': 'بطولة اصطناعية',
                        'venue': 'ملعب العرض',
                        'branch': 'فرع الشروق',
                        'status': 'Scheduled',
                        'team': team,
                        'opponent': opponent,
                      },
                    ),
                  ),
                ),
              ),
            );
            await t.pumpAndSettle();
            if (scale == 2) {
              expect(
                t.getCenter(find.text(team)).dy,
                lessThan(t.getCenter(find.text(opponent)).dy),
              );
            }
            final name = t.widget<UiText>(
              find.widgetWithText(UiText, opponent),
            );
            expect(name.maxLines, 2);
            expect(name.overflow, TextOverflow.ellipsis);
            expect(name.semanticsLabel, opponent);
            final tooltip = find.byWidgetPredicate(
              (w) => w is Tooltip && w.message == opponent,
            );
            expect(tooltip, findsOneWidget);
            await t.longPress(tooltip);
            await t.pumpAndSettle();
            expect(find.text(opponent), findsNWidgets(2));
            expect(t.takeException(), isNull);
            await t.pumpWidget(const SizedBox());
          },
        );
      }
    }
  }
}
