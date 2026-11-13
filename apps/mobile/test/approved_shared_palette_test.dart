import 'package:academy_mobile/core/theme/ocean_palette.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:academy_mobile/features/admin/native_operation_form.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_cockpit.dart';
import 'package:academy_mobile/features/shared/experience_ui.dart';
import 'package:academy_mobile/features/shared/match_experience.dart';
import 'package:academy_mobile/features/shared/player_roster_status.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' show owner;

Widget frame(bool light, Widget body) => MaterialApp(
  locale: const Locale('ar'),
  supportedLocales: const [Locale('ar')],
  localizationsDelegates: GlobalMaterialLocalizations.delegates,
  theme: light ? AcademyTheme.light() : AcademyTheme.dark(),
  home: Scaffold(body: body),
);
void main() {
  for (final light in [true, false]) {
    final good = light ? const Color(0xff16803c) : OceanPalette.positive;
    final negative = light ? const Color(0xffc8102e) : OceanPalette.negative;
    final warn = light ? const Color(0xffb45309) : OceanPalette.warning;
    for (final result in [
      ('فوز', 3, 1, good),
      ('خسارة', 1, 3, negative),
      ('تعادل', 1, 1, warn),
    ]) {
      testWidgets(
        'Published match result preserves classification and approved state ink ${result.$1}, light=$light',
        (t) async {
          await t.pumpWidget(
            frame(
              light,
              SingleChildScrollView(
                child: MatchResultCard(
                  data: {
                    'team': 'فريق اصطناعي',
                    'opponent': 'منافس اصطناعي',
                    'tournament': 'بطولة العرض',
                    'venue': 'ملعب العرض',
                    'branch': 'فرع اصطناعي',
                    'matchDate': '2026-10-22',
                    'startTime': '17:00',
                    'status': 'Completed',
                    'ourScore': result.$2,
                    'opponentScore': result.$3,
                  },
                ),
              ),
            ),
          );
          final state = find.widgetWithText(UiText, result.$1);
          expect(state, findsOneWidget);
          expect(t.widget<UiText>(state).style?.color, result.$4);
          // Each score is associated with its team in the current RTL card.
          for (final score in [
            ('فريق اصطناعي', result.$2),
            ('منافس اصطناعي', result.$3),
          ]) {
            expect(
              find.byWidgetPredicate(
                (w) =>
                    w is Semantics &&
                    w.properties.label == '${score.$1}: ${score.$2}',
              ),
              findsOneWidget,
            );
          }
          expect(t.takeException(), isNull);
        },
      );
    }
    testWidgets(
      'Roster warnings, shared section and quote keep their values with approved semantic ink light=$light',
      (t) async {
        await t.pumpWidget(
          frame(
            light,
            SingleChildScrollView(
              child: Column(
                children: [
                  const ExperienceSection(
                    'عنوان اختبار',
                    Icons.event_available_rounded,
                    color: ExperienceColors.green,
                  ),
                  const ExperienceSection(
                    'عنوان مخصص',
                    Icons.star_border,
                    color: Colors.purple,
                  ),
                  const PlayerRosterStatus(
                    player: {
                      'subscription': {
                        'sessionBased': true,
                        'remainingSessions': 1,
                      },
                      'trainingSafetyNote': 'ملحوظة تدريب اصطناعية',
                    },
                  ),
                  const OperationPriceSummary(
                    quote: {
                      'originalAmount': 1000,
                      'discountAmount': 100,
                      'finalAmount': 900,
                      'currency': 'EGP',
                    },
                  ),
                ],
              ),
            ),
          ),
        );
        expect(
          t.widget<Icon>(find.byIcon(Icons.event_available_rounded)).color,
          good,
        );
        expect(
          t.widget<Icon>(find.byIcon(Icons.star_border)).color,
          Colors.purple,
        );
        expect(
          t
              .widget<UiText>(find.widgetWithText(UiText, 'حصة واحدة متبقية'))
              .style!
              .color,
          negative,
        );
        expect(
          t
              .widget<UiText>(
                find.widgetWithText(UiText, 'ملحوظة: ملحوظة تدريب اصطناعية'),
              )
              .style!
              .color,
          warn,
        );
        expect(
          t
              .widgetList<MoneyText>(find.byType(MoneyText))
              .singleWhere((m) => m.value == 900)
              .color,
          good,
        );
        expect(t.takeException(), isNull);
      },
    );
    testWidgets(
      'Operating detail renders same financial amounts with semantic category colors light=$light',
      (t) async {
        final auth = await owner(
          (method, path, body, token) async => {
            'from': '2026-09-22',
            'to': '2026-10-22',
            'currency': 'EGP',
            'totalIncome': 9000,
            'totalExpenses': 3500,
            'surplus': 5500,
            'income': [
              {'label': 'اشتراكات محصلة', 'amount': 9000},
            ],
            'expenses': [
              {'label': 'رواتب مسددة', 'amount': 3500},
            ],
            'receivables': 0,
            'approvedPayrollPayable': 0,
            'unpaidOperatingExpenses': 0,
          },
        );
        await t.pumpWidget(
          frame(
            light,
            NativeOperatingStatement(
              auth: auth,
              filters: const {'branchId': 'b1'},
            ),
          ),
        );
        await t.pumpAndSettle();
        final values = t.widgetList<MoneyText>(find.byType(MoneyText));
        expect(
          values.where((m) => m.value == 9000 && m.color == good),
          isNotEmpty,
        );
        expect(
          values.where((m) => m.value == 3500 && m.color == negative),
          isNotEmpty,
        );
        expect(find.text('الفائض والعجز التشغيلي'), findsOneWidget);
        final date = find.byWidgetPredicate(
          (w) => w is UiText && w.data == '2026-09-22 — 2026-10-22',
        );
        expect(t.widget<UiText>(date).textDirection, TextDirection.ltr);
        final result = find.byWidgetPredicate(
          (w) => w is UiText && w.data == '5,500 ج.م',
        );
        expect(t.widget<UiText>(result).style!.color, good);

        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
}
