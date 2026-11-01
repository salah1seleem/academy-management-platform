import 'package:academy_mobile/features/owner/home_trend.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';

import 'v2_role_workspaces_test.dart' show app;

void main() async {
  TestWidgetsFlutterBinding.ensureInitialized();
  await initializeDateFormatting('ar_EG');
  final weeks = <Json>[
    for (var i = 0; i < 8; i++)
      {
        'from': '2026-10-${(i + 1).toString().padLeft(2, '0')}',
        'to': '2026-10-${(i + 8).toString().padLeft(2, '0')}',
        'amount': i == 2 ? -120.5 : i * 300,
      },
  ];
  final months = <Json>[
    for (var i = 5; i < 11; i++)
      {
        'month': '2026-${i.toString().padLeft(2, '0')}-01',
        'asOf': '2026-${i.toString().padLeft(2, '0')}-21',
        'count': i * 10,
      },
  ];
  for (final width in [375.0, 390.0, 440.0]) {
    for (final scale in [1.0, 2.0]) {
      for (final dark in [false, true]) {
        testWidgets(
          'Home trends $width scale $scale dark $dark retain exact negative values and local date drilldowns',
          (t) async {
            t.view.physicalSize = Size(width, 844);
            t.view.devicePixelRatio = 1;
            addTearDown(t.view.resetPhysicalSize);
            addTearDown(t.view.resetDevicePixelRatio);
            Json? week, month;
            await t.pumpWidget(
              app(
                Builder(
                  builder: (context) => MediaQuery(
                    data: MediaQuery.of(context)
                        .copyWith(textScaler: TextScaler.linear(scale)),
                    child: Theme(
                      data: dark ? ThemeData.dark() : ThemeData.light(),
                      child: Scaffold(
                        body: SingleChildScrollView(
                          child: Column(
                            children: [
                              HomeTrend(
                                items: weeks,
                                monthly: false,
                                currency: 'EGP',
                                onOpen: (v) => week = v,
                              ),
                              HomeTrend(
                                items: months,
                                monthly: true,
                                onOpen: (v) => month = v,
                              ),
                            ],
                          ),
                        ),
                      ),
                    ),
                  ),
                ),
              ),
            );
            await t.pumpAndSettle();
            final weekly = find.byWidgetPredicate(
              (w) => w is HomeTrend && !w.monthly,
            );
            final monthly = find.byWidgetPredicate(
              (w) => w is HomeTrend && w.monthly,
            );
            for (final chart in [weekly, monthly]) {
              final points = find.descendant(
                of: chart,
                matching: find.byType(Tooltip),
              );
              expect(
                t.getTopLeft(points.first).dx,
                lessThan(t.getTopLeft(points.last).dx),
                reason: 'The approved time axis runs oldest to newest left to right in RTL.',
              );
            }
            expect(
              find.byKey(const ValueKey('home-trend-range-false')),
              findsNothing,
            );
            for (final isMonthly in [true]) {
              final range = t.widget<UiText>(
                find.byKey(ValueKey('home-trend-range-$isMonthly')),
              );
              expect(range.textDirection, TextDirection.ltr);
              expect(
                range.data,
                isMonthly
                    ? '2026-05-01 — 2026-10-21'
                    : '2026-10-01 — 2026-10-15',
              );
            }
            expect(find.text('-120.5'), findsOneWidget);
            await t.ensureVisible(find.text('-120.5'));
            await t.tap(find.text('-120.5'));
            expect(week, weeks[2]);
            await t.ensureVisible(find.text('100'));
            await t.tap(find.text('100'));
            expect(month, months.last);
            expect(find.text('نمو الاشتراكات'), findsOneWidget);
            expect(t.takeException(), isNull);
          },
        );
      }
    }
  }
}
