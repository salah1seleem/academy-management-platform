import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/owner/operating_monthly_chart.dart';
import 'package:academy_mobile/features/owner/refined_home_panels.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

const months = <Map<String, dynamic>>[
  {
    'from': '2026-08-12',
    'to': '2026-08-31',
    'income': 1000,
    'expenses': 200,
    'surplus': 800,
  },
  {
    'from': '2026-09-01',
    'to': '2026-09-28',
    'income': -50,
    'expenses': 100,
    'surplus': -150,
  },
];
void main() {
  for (final width in [375.0, 440.0]) {
    for (final dark in [false, true]) {
      for (final scale in [1.0, 2.0]) {
        testWidgets(
          'cash month source remains readable and opens exact partial range width=$width dark=$dark scale=$scale',
          (t) async {
            t.view.physicalSize = Size(width, 900);
            t.view.devicePixelRatio = 1;
            addTearDown(t.view.resetPhysicalSize);
            addTearDown(t.view.resetDevicePixelRatio);
            Map<String, dynamic>? opened;
            await t.pumpWidget(
              MaterialApp(
                locale: const Locale('ar'),
                supportedLocales: const [Locale('ar')],
                localizationsDelegates: GlobalMaterialLocalizations.delegates,
                theme: dark ? AcademyTheme.dark() : AcademyTheme.light(),
                builder: (context, child) => MediaQuery(
                  data: MediaQuery.of(context)
                      .copyWith(textScaler: TextScaler.linear(scale)),
                  child: child!,
                ),
                home: Scaffold(
                  body: SingleChildScrollView(
                    child: HomeOperatingStatement(
                      data: {
                        'currency': 'EGP',
                        'surplus': 650,
                        'totalIncome': 950,
                        'totalExpenses': 300,
                        'monthly': months,
                        'income': [
                          {'label': 'اشتراكات محصلة', 'amount': 1000},
                          {'label': 'استردادات', 'amount': -50},
                          {'label': 'مسابح غير مستخدمة', 'amount': 0},
                        ],
                        'expenses': [
                          {'label': 'إيجار', 'amount': 300},
                        ],
                        'receivables': 900,
                        'approvedPayrollPayable': 600,
                      },
                      onOpen: () {},
                      onMonth: (m) => opened = m,
                    ),
                  ),
                ),
              ),
            );
            await t.pumpAndSettle();
            expect(find.text('مسابح غير مستخدمة'), findsNothing);
            final month = find.byKey(
              const ValueKey('operating-month-2026-08-12'),
            );
            await t.ensureVisible(month);
            await t.pumpAndSettle();
            await t.tap(month);
            await t.pumpAndSettle();
            expect(opened, months.first);
            final painter =
                t
                        .widget<CustomPaint>(
                          find.byWidgetPredicate(
                            (w) =>
                                w is CustomPaint &&
                                w.painter is OperatingMonthlyPainter,
                          ),
                        )
                        .painter!
                    as OperatingMonthlyPainter;
            expect(painter.items[1]['income'], -50);
            expect(find.text('آخر 3 أشهر'), findsNothing);
            expect(t.takeException(), isNull);
          },
        );
      }
    }
  }
  testWidgets('zero net never hides genuine receipt and refund categories', (
    t,
  ) async {
    await t.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: SingleChildScrollView(
            child: HomeOperatingStatement(
              data: const {
                'currency': 'EGP',
                'surplus': 0,
                'totalIncome': 0,
                'totalExpenses': 0,
                'income': [
                  {'label': 'اشتراك محصل', 'amount': 100},
                  {'label': 'استرداد حقيقي', 'amount': -100},
                ],
                'expenses': [],
              },
              onOpen: () {},
            ),
          ),
        ),
      ),
    );
    expect(find.text('اشتراك محصل'), findsOneWidget);
    expect(find.text('استرداد حقيقي'), findsOneWidget);
    expect(
      find.text('لا توجد حركة قبض أو صرف مسجلة في هذه الفترة.'),
      findsNothing,
    );
    expect(t.takeException(), isNull);
  });
  testWidgets('absent cash series does not invent a chart', (t) async {
    await t.pumpWidget(
      const MaterialApp(
        home: OperatingMonthlyChart(items: [], currency: 'EGP'),
      ),
    );
    expect(
      find.byWidgetPredicate(
        (w) => w is CustomPaint && w.painter is OperatingMonthlyPainter,
      ),
      findsNothing,
    );
  });
}
