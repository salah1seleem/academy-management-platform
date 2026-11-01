import 'support/visibility.dart';

import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:academy_mobile/core/widgets/app_widgets.dart';
import 'package:academy_mobile/features/coach/coach_evaluation.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as fixtures;

void main() {
  for (final width in [375.0, 440.0]) {
    for (final dark in [false, true]) {
      testWidgets(
        'approved criterion sizes and persistent publish controls width=$width dark=$dark scale=2',
        (t) async {
          t.view.physicalSize = Size(width, 900);
          t.view.devicePixelRatio = 1;
          addTearDown(t.view.resetPhysicalSize);
          addTearDown(t.view.resetDevicePixelRatio);
          final auth = await fixtures.authenticate('Coach', (
            method,
            path,
            body,
            token,
          ) async {
            expect(method, 'GET');
            expect(path, '/api/v1/evaluations/existing');
            return {
              'id': 'existing',
              'sportEnrollmentId': 'enrollment',
              'status': 'Draft',
              'version': 1,
              'player': 'لاعب اصطناعي',
              'reportingPeriod': 'تقييم دوري',
              'generalNotes': null,
              'scores': [
                {'criterionId': 'zero', 'name': 'التوازن', 'score': 0},
                {'criterionId': 'untouched', 'name': 'التركيز', 'score': null},
              ],
            };
          });
          await t.pumpWidget(
            MaterialApp(
              locale: const Locale('ar'),
              supportedLocales: const [Locale('ar')],
              localizationsDelegates: GlobalMaterialLocalizations.delegates,
              theme: dark ? AcademyTheme.dark() : AcademyTheme.light(),
              builder: (context, child) => MediaQuery(
                data: MediaQuery.of(context)
                    .copyWith(textScaler: const TextScaler.linear(2)),
                child: child!,
              ),
              home: CoachEvaluation(
                auth: auth,
                evaluationId: 'existing',
                enrollment: const {'id': 'enrollment'},
              ),
            ),
          );
          await t.pumpAndSettle();
          expect(t.widget<PlayerAvatar>(find.byType(PlayerAvatar)).size, 52);
          final avatarBox = find.descendant(
            of: find.byType(PlayerAvatar),
            matching: find.byWidgetPredicate(
              (w) => w is Container && w.decoration is BoxDecoration,
            ),
          );
          final decoration =
              t.widget<Container>(avatarBox.first).decoration! as BoxDecoration;
          expect(decoration.shape, BoxShape.rectangle);
          expect(decoration.border, isNull);
          final title = find.byWidgetPredicate(
            (w) => w is UiText && w.data == 'لاعب اصطناعي',
          );
          expect(t.widget<UiText>(title).style!.fontSize, 18);
          final criterion = find.byWidgetPredicate(
            (w) => w is UiText && w.data == 'التوازن',
          );
          expect(t.widget<UiText>(criterion).style!.fontSize, 15);
          expect(
            t.widget<UiText>(criterion).style!.fontWeight,
            FontWeight.w600,
          );
          final zero = find.byWidgetPredicate(
            (w) => w is UiText && w.data == '0',
          );
          expect(t.widget<UiText>(zero).style!.fontSize, 24);
          expect(
            t.widget<UiText>(zero).style!.color,
            Theme.of(t.element(zero)).colorScheme.error,
          );
          final untouched = find.byWidgetPredicate(
            (w) => w is UiText && w.data == 'لم يُقيّم',
          );
          await revealTestTarget(t, untouched);
          expect(t.widget<UiText>(untouched).style!.fontSize, 13);
          for (final slider in find.byType(Slider).evaluate()) {
            final data = SliderTheme.of(slider);
            expect(data.trackHeight, 4);
            expect(data.thumbShape, isA<RoundSliderThumbShape>());
            expect(data.trackShape, isA<RoundedRectSliderTrackShape>());
            expect(
              data.thumbShape!.getPreferredSize(true, true),
              const Size.square(24),
            );
          }
          await revealTestTarget(
            t,
            find.byKey(const ValueKey('evaluation-score-untouched')),
          );
          final neutral = SliderTheme.of(
            t.element(find.byKey(const ValueKey('evaluation-score-untouched'))),
          );
          expect(neutral.activeTrackColor, neutral.inactiveTrackColor);
          final scaffold = t.widget<AppScaffold>(find.byType(AppScaffold));
          expect(scaffold.bottomBar, isNotNull);
          final publish = find.widgetWithText(
            FilledButton,
            'نشر التقييم لولي الأمر',
          );
          expect(publish, findsOneWidget);
          expect(
            find.ancestor(of: publish, matching: find.byType(ListView)),
            findsNothing,
          );
          expect(t.getRect(publish).bottom, lessThanOrEqualTo(900));
          expect(t.getRect(publish).height, greaterThanOrEqualTo(52));
          expect(t.takeException(), isNull);
          await t.pumpWidget(const SizedBox());
          auth.dispose();
        },
      );
    }
  }
}
