import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/refined_home_panels.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  for (final light in [true, false]) {
    testWidgets(
      'Approved semantic ink reaches subscription states and financial values, light=$light',
      (t) async {
        await t.pumpWidget(
          MaterialApp(
            theme: light ? AcademyTheme.light() : AcademyTheme.dark(),
            home: Scaffold(
              body: SingleChildScrollView(
                child: Column(
                  children: [
                    HomeSubscriptions(
                      data: const {
                        'total': 10,
                        'active': 6,
                        'expiring': 3,
                        'expired': 4,
                      },
                      onOpen: (_) {},
                    ),
                    HomeOperatingStatement(
                      data: const {
                        'surplus': 5500,
                        'currency': 'EGP',
                        'income': [
                          {'label': 'اشتراكات محصلة', 'amount': 9000},
                        ],
                        'expenses': [
                          {'label': 'رواتب مسددة', 'amount': 3500},
                        ],
                        'totalIncome': 9000,
                        'totalExpenses': 3500,
                      },
                      onOpen: () {},
                    ),
                  ],
                ),
              ),
            ),
          ),
        );
        final positive = light
            ? const Color(0xff16803c)
            : const Color(0xff34d399);
        final negative = light
            ? const Color(0xffc8102e)
            : const Color(0xfff87171);
        final warning = light
            ? const Color(0xffb45309)
            : const Color(0xfffbbf24);
        expect(
          t.widget<UiText>(find.widgetWithText(UiText, '6')).style!.color,
          positive,
        );
        expect(
          t.widget<UiText>(find.widgetWithText(UiText, '3')).style!.color,
          warning,
        );
        expect(
          t.widget<UiText>(find.widgetWithText(UiText, '4')).style!.color,
          negative,
        );
        expect(
          t
              .widget<UiText>(
                find.byWidgetPredicate(
                  (w) => w is UiText && w.data == '5,500 ج.م',
                ),
              )
              .style!
              .color,
          positive,
        );
        final values = t.widgetList<MoneyText>(find.byType(MoneyText));
        expect(
          values
              .where((w) => w.value == 9000)
              .every((w) => w.color == positive),
          isTrue,
        );
        expect(
          values
              .where((w) => w.value == 3500)
              .every((w) => w.color == negative),
          isTrue,
        );
        if (light) {
          double contrast(Color color) =>
              1.05 / (color.computeLuminance() + .05);
          expect(contrast(positive), greaterThanOrEqualTo(4.5));
          expect(contrast(negative), greaterThanOrEqualTo(4.5));
          expect(contrast(warning), greaterThanOrEqualTo(4.5));
        }
        expect(t.takeException(), isNull);
      },
    );
  }
}
