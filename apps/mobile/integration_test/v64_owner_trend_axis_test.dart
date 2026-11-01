import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/home_destinations.dart';
import 'package:academy_mobile/features/owner/home_trend.dart';
import 'package:academy_mobile/features/owner/owner_reports.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:academy_mobile/features/owner/refined_home_panels.dart';
import 'package:academy_mobile/features/shared/experience_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'v48_owner_home_reports_test.dart' show HomeReportReadTransport;

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'approved chronological axes keep real weekly and monthly drilldowns',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      const width = int.fromEnvironment(
        'TREND_EXPECTED_WIDTH',
        defaultValue: 440,
      );
      expect(
        (t.view.physicalSize / t.view.devicePixelRatio).width,
        width.toDouble(),
      );
      final transport = HomeReportReadTransport(AppConfig('Demo', base));
      final auth = AuthController(
        transport,
        SecureRefreshStore('v45.5278.os-media-entry.01099900010'),
      );
      await auth.restore();
      if (auth.session == null) {
        final challenge = await auth.requestOtp('01099900010');
        await auth.verify('01099900010', challenge, '246810');
      }
      expect(auth.session!.selected!.role, 'AcademyOwner');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final calendar = obj(
        await auth.request('GET', '/api/v1/academy/calendar'),
      );
      final options = obj(
        await auth.request('GET', '/api/v1/manage/structure/options'),
      );
      final branch = rows(options['branches'])
          .singleWhere((b) => b['arabicName'] == 'فرع الشروق');
      final filters = {
        'from': textOf(calendar['trailingMonthFrom']),
        'to': textOf(calendar['today']),
        'branchId': textOf(branch['id']),
      };
      final summary = obj(
        await auth.request(
          'GET',
          '/api/v1/reports/owner-summary?${Uri(queryParameters: filters).query}',
        ),
      );
      final cockpit = obj(
        await auth.request(
          'GET',
          '/api/v1/reports/owner-cockpit?${Uri(queryParameters: filters).query}',
        ),
      );
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> locate(Finder f) async {
        final scroll = find.byType(Scrollable).first;
        t.state<ScrollableState>(scroll).position.jumpTo(0);
        await t.pumpAndSettle();
        await t.scrollUntilVisible(f, 180, scrollable: scroll, maxScrolls: 150);
        await t.ensureVisible(f);
        await t.pumpAndSettle();
      }

      Future<Json> reply(String path, int marker) async {
        for (var i = 0; i < 240; i++) {
          final responses = transport.reads
              .skip(marker)
              .where((r) => r.path.startsWith(path));
          if (responses.isNotEmpty) return obj(responses.last.value);
          await t.pump(const Duration(milliseconds: 150));
        }
        fail('The scoped report did not arrive.');
      }

      final appearance = Appearance();
      for (final sport in ['football', 'general']) {
        await appearance.completeRoleIntro('AcademyOwner', sport);
      }
      for (final light in [true, false]) {
        await t.pumpWidget(const SizedBox());
        await t.pumpAndSettle();
        await appearance.update(night: !light);
        await t.pumpWidget(
          AcademyApp(
            key: ValueKey(light),
            auth: auth,
            environment: 'Demo',
            appearance: appearance,
          ),
        );
        await wait(find.byType(ScopeBranchTabs));
        final choice = find.descendant(
          of: find.byType(ScopeBranchTabs),
          matching: find.text('فرع الشروق'),
        );
        await t.ensureVisible(choice);
        await t.tap(choice);
        await t.pumpAndSettle();
        await locate(find.byType(HomeSubscriptions));
        await wait(find.byType(HomeSubscriptions));
        for (final monthly in [false, true]) {
          final chart = find.byWidgetPredicate(
            (w) => w is HomeTrend && w.monthly == monthly,
          );
          await locate(chart);
          final data = t.widget<HomeTrend>(chart).items;
          final source = monthly
              ? rows(obj(summary['subscriptions'])['trend'])
              : rows(obj(cockpit['collections'])['weekly']);
          expect(data, source);
          final points = find.descendant(
            of: chart,
            matching: find.byType(Tooltip),
          );
          expect(
            t.getTopLeft(points.first).dx,
            lessThan(t.getTopLeft(points.last).dx),
          );
          expect(
            t
                .widget<UiText>(
                  find.byKey(ValueKey('home-trend-range-$monthly')),
                )
                .textDirection,
            TextDirection.ltr,
          );
          final mode = light ? 'light' : 'dark';
          final kind = monthly ? 'monthly' : 'weekly';
          await binding.takeScreenshot(
            'phase-approved-owner-trend-$kind-w$width-$mode',
          );
          final index = monthly
              ? data.lastIndexWhere(
                  (r) => (r['count'] as num) > 0 && r['asOf'] != summary['to'],
                )
              : data.lastIndexWhere((r) => (r['amount'] as num) > 0);
          expect(index, greaterThanOrEqualTo(0));
          final marker = transport.reads.length;
          await t.tap(points.at(index));
          await t.pumpAndSettle();
          if (monthly) {
            await wait(find.byType(HomeSubscriptionPlayers));
            final page = t.widget<HomeSubscriptionPlayers>(
              find.byType(HomeSubscriptionPlayers),
            );
            expect(page.filters, {
              'branchId': branch['id'],
              'to': data[index]['asOf'],
            });
            final result = await reply(
              '/api/v1/reports/subscription-snapshot?',
              marker,
            );
            expect(result['total'], data[index]['count']);
            expect(result['asOf'], data[index]['asOf']);
          } else {
            await wait(find.byType(OwnerReports));
            final page = t.widget<OwnerReports>(find.byType(OwnerReports));
            expect(page.initialFilters, {
              'branchId': branch['id'],
              'from': data[index]['from'],
              'to': data[index]['to'],
            });
            final result = await reply('/api/v1/reports/financial?', marker);
            expect(
              result['netAmount'] ?? result['totalAmount'],
              data[index]['amount'],
            );
          }
          await binding.takeScreenshot(
            'phase-approved-owner-trend-$kind-detail-w$width-$mode',
          );
          await t.tap(find.byType(BackButton).first);
          await t.pumpAndSettle();
          await wait(find.byType(OwnerDashboard));
        }
        final collections = find.byType(HomeCollectionCards);
        await locate(collections);
        expect(
          t.widget<HomeCollectionCards>(collections).referenceDay,
          summary['collectionDay'],
        );
        final date =
            '${summary['collectionMonthFrom']} — ${summary['collectionDay']}';
        final dateLabels = find.descendant(
          of: collections,
          matching: find.byWidgetPredicate(
            (w) => w is UiText && w.data == date,
          ),
        );
        expect(dateLabels, findsWidgets);
        for (final label in t.widgetList<UiText>(dateLabels)) {
          expect(label.textDirection, TextDirection.ltr);
        }
        await binding.takeScreenshot(
          'phase-approved-owner-trend-collections-w$width-${light ? 'light' : 'dark'}',
        );
      }
      expect(t.takeException(), isNull);
    },
  );
}
