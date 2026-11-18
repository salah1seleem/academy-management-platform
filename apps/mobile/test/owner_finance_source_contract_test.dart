import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/refined_home_panels.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'shared_ui_quality_test.dart' show capture;
import 'v2_role_workspaces_test.dart' show app;

Map<String, dynamic> statement() => {
  'currency': 'EGP',
  'surplus': 16772.35,
  'totalIncome': 16910,
  'totalExpenses': 137.65,
  'income': [
    {'key': 'subscription', 'label': 'اشتراكات محصلة', 'amount': 16910},
  ],
  'expenses': [
    {'key': 'Maintenance', 'label': 'الصيانة والتشغيل', 'amount': 137.65},
  ],
  'monthly': [
    {
      'from': '2026-11-01',
      'to': '2026-11-18',
      'income': 16910,
      'expenses': 137.65,
      'surplus': 16772.35,
    },
  ],
};

Map<String, dynamic> summary() => {
  'currency': 'EGP',
  'collections': {'net': 16910, 'changePercentage': null},
  'target': {'amount': null, 'achievement': null},
  'overdue': {'amount': 925, 'players': 2, 'asOf': '2026-11-18'},
  'attendance': {'percentage': 80},
  'capacity': {'seats': 12},
  'members': {'players': 24},
  'groups': {'active': 3},
};

Widget panel(Map<String, dynamic> data) =>
    HomeOperatingStatement(data: data, onOpen: null);

Widget summaryPanel(Map<String, dynamic> data) => AcademySummaryPanel(
  data: data,
  onReport: (_) {},
  onCapacity: () {},
  showWeeklyTrend: false,
);

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  setUpAll(() async {
    final font = FontLoader('AcademyArabic');
    for (final weight in [300, 400, 500, 600, 700]) {
      font.addFont(rootBundle.load('assets/fonts/AcademyArabic-$weight.ttf'));
    }
    final icons = FontLoader('MaterialIcons')
      ..addFont(rootBundle.load('fonts/MaterialIcons-Regular.otf'));
    await Future.wait([font.load(), icons.load()]);
  });
  final invalid = <String, void Function(Map<String, dynamic>)>{
    'missing surplus': (d) => d.remove('surplus'),
    'missing income total': (d) => d.remove('totalIncome'),
    'missing expense total': (d) => d.remove('totalExpenses'),
    'missing currency': (d) => d.remove('currency'),
    'invalid currency': (d) => d['currency'] = 'جنيه',
    'surplus mismatch': (d) => d['surplus'] = 1,
    'income mismatch': (d) => d['totalIncome'] = 1,
    'expense mismatch': (d) => d['totalExpenses'] = 1,
    'missing income rows': (d) => d.remove('income'),
    'bad expense rows': (d) => d['expenses'] = 'missing',
    'missing line amount': (d) => d['expenses'] = [
      <String, dynamic>{'label': 'الصيانة والتشغيل'},
    ],
    'non-numeric line': (d) => d['income'] = [
      <String, dynamic>{'label': 'اشتراكات محصلة', 'amount': '16910'},
    ],
    'invalid precision': (d) => d['totalExpenses'] = 137.651,
    'non-finite surplus': (d) => d['surplus'] = double.nan,
    'negative expense': (d) {
      d['totalExpenses'] = -137.65;
      d['surplus'] = 17047.65;
      d['expenses'] = [
        <String, dynamic>{'label': 'مصروف', 'amount': -137.65},
      ];
    },
    'bad change percentage': (d) => d['surplusChangePercentage'] = 'bad',
    'non-finite change': (d) => d['surplusChangePercentage'] = double.infinity,
    'invalid monthly rows': (d) => d['monthly'] = 'missing',
    'invalid monthly sum': (d) => d['monthly'] = [
      <String, dynamic>{
        'from': '2026-11-01',
        'to': '2026-11-18',
        'income': 16910,
        'expenses': 137.65,
        'surplus': 0,
      },
    ],
    'invalid monthly date': (d) => d['monthly'] = [
      <String, dynamic>{
        'from': '2026-02-30',
        'to': '2026-11-18',
        'income': 16910,
        'expenses': 137.65,
        'surplus': 16772.35,
      },
    ],
  };
  for (final entry in invalid.entries) {
    testWidgets('OHF44 refuses unverified statement: ${entry.key}', (t) async {
      final data = statement();
      entry.value(data);
      await t.pumpWidget(app(Scaffold(body: panel(data))));
      await t.pumpAndSettle();
      expect(t.takeException(), isNull);
      expect(find.text('تعذر التحقق من أرقام التقرير.'), findsOneWidget);
      expect(find.text('الفائض التشغيلي'), findsNothing);
      expect(find.text('العجز التشغيلي'), findsNothing);
    });
  }
  for (final value in <Object?>[null, -1, '925.00', double.nan]) {
    testWidgets('OHF11 missing/invalid overdue stays unavailable: $value', (
      t,
    ) async {
      final data = summary();
      (data['overdue'] as Map<String, dynamic>)['amount'] = value;
      await t.pumpWidget(
        app(Scaffold(body: SingleChildScrollView(child: summaryPanel(data)))),
      );
      await t.pumpAndSettle();
      expect(t.takeException(), isNull);
      expect(find.text('رصيد المستحقات المتأخرة غير متاح'), findsOneWidget);
      expect(find.text('لا توجد مستحقات متأخرة'), findsNothing);
    });
  }
  for (final value in <Object?>[null, -1, 2.5, '2']) {
    testWidgets('OHF12 invalid player count is not zero: $value', (t) async {
      final data = summary();
      (data['overdue'] as Map<String, dynamic>)['players'] = value;
      await t.pumpWidget(
        app(Scaffold(body: SingleChildScrollView(child: summaryPanel(data)))),
      );
      await t.pumpAndSettle();
      expect(t.takeException(), isNull);
      expect(find.text('غير متاح'), findsWidgets);
      expect(find.text('لا توجد مستحقات متأخرة'), findsNothing);
    });
  }
  for (final value in <Object?>[null, 'bad', '2026-02-30']) {
    testWidgets('OHF13 missing/invalid balance date is explicit: $value', (
      t,
    ) async {
      final data = summary();
      (data['overdue'] as Map<String, dynamic>)['asOf'] = value;
      await t.pumpWidget(
        app(Scaffold(body: SingleChildScrollView(child: summaryPanel(data)))),
      );
      await t.pumpAndSettle();
      expect(t.takeException(), isNull);
      expect(find.text('تاريخ رصيد المتأخرات غير متاح'), findsOneWidget);
    });
  }
  for (final kind in ['zero', 'deficit', 'refund']) {
    testWidgets('OHF46 preserves verified $kind instead of unavailable', (
      t,
    ) async {
      final data = statement();
      final income = kind == 'refund' ? -25.0 : 0.0;
      final expense = kind == 'deficit' ? 137.65 : 0.0;
      data.addAll({
        'totalIncome': income,
        'totalExpenses': expense,
        'surplus': income - expense,
        'income': [
          <String, dynamic>{'label': 'التحصيل بعد الاسترداد', 'amount': income},
        ],
        'expenses': [
          <String, dynamic>{'label': 'المصروفات', 'amount': expense},
        ],
        'monthly': <Object>[],
      });
      await t.pumpWidget(
        app(Scaffold(body: SingleChildScrollView(child: panel(data)))),
      );
      await t.pumpAndSettle();
      expect(t.takeException(), isNull);
      expect(find.text('تعذر التحقق من أرقام التقرير.'), findsNothing);
      expect(
        find.text(kind == 'zero' ? 'الفائض التشغيلي' : 'العجز التشغيلي'),
        findsOneWidget,
      );
    });
  }
  for (final width in [320.0, 390.0]) {
    for (final scale in [1.0, 2.0]) {
      for (final dark in [false, true]) {
        testWidgets('OHF47 financial panels $width text$scale dark$dark', (
          t,
        ) async {
          t.view.physicalSize = Size(width, 844);
          t.view.devicePixelRatio = 1;
          addTearDown(t.view.reset);
          await t.pumpWidget(
            RepaintBoundary(
              key: const ValueKey('quality-capture'),
              child: app(
                Theme(
                  data: dark ? AcademyTheme.dark() : AcademyTheme.light(),
                  child: Scaffold(
                    body: ListView(
                      children: [summaryPanel(summary()), panel(statement())],
                    ),
                  ),
                ),
                scale: scale,
              ),
            ),
          );
          await t.pumpAndSettle();
          await t.ensureVisible(find.text('مستحقات متأخرة'));
          await t.pumpAndSettle();
          await capture(
            t,
            'overdue-${width.toInt()}-text${scale.toInt()}-${dark ? 'dark' : 'light'}',
          );
          await t.scrollUntilVisible(
            find.text('الفائض التشغيلي'),
            180,
            scrollable: find
                .descendant(
                  of: find.byType(ListView),
                  matching: find.byType(Scrollable),
                )
                .first,
            maxScrolls: 40,
          );
          await t.pumpAndSettle();
          await capture(
            t,
            'statement-${width.toInt()}-text${scale.toInt()}-${dark ? 'dark' : 'light'}',
          );
          expect(t.takeException(), isNull);
          final card = t.getRect(find.byType(HomeOperatingStatement));
          expect(card.left, greaterThanOrEqualTo(0));
          expect(card.right, lessThanOrEqualTo(width));
        });
      }
    }
  }
}
