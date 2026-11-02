import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:academy_mobile/features/shared/match_experience.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  const team = 'فريق النجوم الاصطناعي للناشئين';
  const opponent = 'أكاديمية المنافس الاصطناعي ذات الاسم الطويل جدًا للناشئين';
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
