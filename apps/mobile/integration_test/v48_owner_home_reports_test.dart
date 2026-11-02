import '../test/introduction_test_helpers.dart';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/home_destinations.dart';
import 'package:academy_mobile/features/owner/home_trend.dart';
import 'package:academy_mobile/features/owner/owner_cockpit.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:academy_mobile/features/owner/owner_reports.dart';
import 'package:academy_mobile/features/owner/refined_home_panels.dart';
import 'package:academy_mobile/features/shared/experience_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class HomeReportReadTransport extends HttpApiTransport {
  HomeReportReadTransport(super.config);
  final reads = <({String path, Object? value})>[];
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    if (method != 'GET' && !path.startsWith('/api/v1/mobile/auth/')) {
      throw StateError(
        'This acceptance permits reads and authentication only.',
      );
    }
    final result = await super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
    if (method == 'GET') reads.add((path: path, value: result));
    return result;
  }
}

void main() => runOwnerHomeReportAcceptance();

void runOwnerHomeReportAcceptance({bool light = false}) {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    light
        ? 'Approved light palette and exact owner financial drilldowns from fresh process'
        : 'Real owner chart and financial drilldowns retain branch, exact local dates and server totals without writes',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final transport = HomeReportReadTransport(AppConfig('Demo', base));
      final auth = AuthController(
        transport,
        SecureRefreshStore('v47.5278.decision-assignment.owner10'),
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
      final query = Uri(queryParameters: filters).query;
      final summary = obj(
        await auth.request('GET', '/api/v1/reports/owner-summary?$query'),
      );
      final statement = obj(
        await auth.request('GET', '/api/v1/reports/operating-statement?$query'),
      );
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          if (find.text('تخطي').evaluate().isNotEmpty) {
            await finishRoleIntroduction(t);
          }
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> tap(Finder f) async {
        await t.scrollUntilVisible(
          f,
          220,
          scrollable: find.byType(Scrollable).first,
          maxScrolls: 100,
        );
        await t.ensureVisible(f);
        await t.pumpAndSettle();
        await t.tap(f);
        await t.pumpAndSettle();
      }

      Future<void> back(Finder page) async {
        Navigator.of(t.element(page)).pop();
        await t.pumpAndSettle();
        await wait(find.byType(OwnerDashboard));
      }

      Future<Json> reply(String prefix, int after) async {
        for (var i = 0; i < 240; i++) {
          final matches = transport.reads
              .skip(after)
              .where((r) => r.path.startsWith(prefix));
          if (matches.isNotEmpty) return obj(matches.last.value);
          await t.pump(const Duration(milliseconds: 150));
        }
        fail('Expected report response was not received: $prefix');
      }

      final appearance = Appearance()..dark = !light;
      await t.pumpWidget(
        AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
      );
      await wait(find.byType(ScopeBranchTabs));
      await tap(
        find.descendant(
          of: find.byType(ScopeBranchTabs),
          matching: find.text('فرع الشروق'),
        ),
      );
      await wait(find.byType(HomeSubscriptions));
      final subscriptionPanel = find.byType(HomeSubscriptions);
      await t.ensureVisible(subscriptionPanel);
      await t.pumpAndSettle();
      await binding.takeScreenshot(
        'phase-approved-owner-subscription-home-${light ? 'light' : 'dark'}',
      );
      final subscriptions = obj(summary['subscriptions']);
      final months = rows(subscriptions['trend']);
      final monthIndex = months.lastIndexWhere(
        (m) => (m['count'] as num) > 0 && m['asOf'] != summary['to'],
      );
      expect(monthIndex, greaterThanOrEqualTo(0));
      final monthly = find.descendant(
        of: subscriptionPanel,
        matching: find.byType(HomeTrend),
      );
      var marker = transport.reads.length;
      await tap(
        find
            .descendant(of: monthly, matching: find.byType(Tooltip))
            .at(monthIndex),
      );
      await wait(find.byType(HomeSubscriptionPlayers));
      var list = t.widget<HomeSubscriptionPlayers>(
        find.byType(HomeSubscriptionPlayers),
      );
      expect(list.filters, {
        'branchId': branch['id'],
        'to': months[monthIndex]['asOf'],
      });
      final historical = await reply(
        '/api/v1/reports/subscription-snapshot?',
        marker,
      );
      expect(historical['total'], months[monthIndex]['count']);
      expect(historical['asOf'], months[monthIndex]['asOf']);
      await wait(find.text('${historical['total']} اشتراك'));
      expect(
        find.text('حالة الاشتراكات بتاريخ ${historical['asOf']}'),
        findsOneWidget,
      );
      await binding.takeScreenshot(
        'phase-approved-owner-subscription-month-detail-${light ? 'light' : 'dark'}',
      );
      await back(find.byType(HomeSubscriptionPlayers));
      marker = transport.reads.length;
      await tap(
        find.descendant(
          of: subscriptionPanel,
          matching: find.text('فرع الشروق'),
        ),
      );
      await wait(find.byType(HomeSubscriptionPlayers));
      list = t.widget<HomeSubscriptionPlayers>(
        find.byType(HomeSubscriptionPlayers),
      );
      expect(list.filters['branchId'], branch['id']);
      expect(list.filters['to'], summary['to']);
      final branchSnapshot = await reply(
        '/api/v1/reports/subscription-snapshot?',
        marker,
      );
      expect(branchSnapshot['total'], subscriptions['total']);
      await back(find.byType(HomeSubscriptionPlayers));
      final weekly = find.byWidgetPredicate(
        (w) => w is HomeTrend && !w.monthly,
      );
      await t.scrollUntilVisible(
        weekly,
        -220,
        scrollable: find.byType(Scrollable).first,
        maxScrolls: 100,
      );
      await t.pumpAndSettle();
      final weeks = t.widget<HomeTrend>(weekly).items;
      final weekIndex = weeks.lastIndexWhere((w) => (w['amount'] as num) > 0);
      marker = transport.reads.length;
      await tap(
        find
            .descendant(of: weekly, matching: find.byType(Tooltip))
            .at(weekIndex),
      );
      await wait(find.byType(OwnerReports));
      var report = t.widget<OwnerReports>(find.byType(OwnerReports));
      expect(report.initialReport, 'financial');
      expect(report.initialFilters, {
        'branchId': branch['id'],
        'from': weeks[weekIndex]['from'],
        'to': weeks[weekIndex]['to'],
      });
      final weekReport = await reply('/api/v1/reports/financial?', marker);
      expect(
        weekReport['netAmount'] ?? weekReport['totalAmount'],
        weeks[weekIndex]['amount'],
      );
      await wait(find.text('سجل التحصيلات'));
      await binding.takeScreenshot(
        'phase-approved-owner-week-collection-detail-${light ? 'light' : 'dark'}',
      );
      await back(find.byType(OwnerReports));
      final collections = find.byType(HomeCollectionCards);
      await t.scrollUntilVisible(
        collections,
        220,
        scrollable: find.byType(Scrollable).first,
        maxScrolls: 100,
      );
      await t.ensureVisible(collections);
      await t.pumpAndSettle();
      await binding.takeScreenshot(
        'phase-approved-owner-collections-home-${light ? 'light' : 'dark'}',
      );
      final source = rows(obj(summary['collections'])['monthSources'])
          .firstWhere(
            (s) => s['subscriptionKind'] != null && (s['amount'] as num) > 0,
          );
      marker = transport.reads.length;
      await tap(
        find.descendant(
          of: collections,
          matching: find.text(textOf(source['label'])),
        ),
      );
      await wait(find.byType(OwnerReports));
      report = t.widget<OwnerReports>(find.byType(OwnerReports));
      expect(report.initialFilters, {
        'branchId': branch['id'],
        'from': summary['collectionMonthFrom'],
        'to': summary['collectionDay'],
        'collectionType': source['collectionType'],
        'subscriptionKind': source['subscriptionKind'],
      });
      final sourceReport = await reply('/api/v1/reports/financial?', marker);
      expect(
        sourceReport['netAmount'] ?? sourceReport['totalAmount'],
        source['amount'],
      );
      await wait(find.text('سجل التحصيلات'));
      await binding.takeScreenshot(
        'phase-approved-owner-collection-source-detail-${light ? 'light' : 'dark'}',
      );
      await back(find.byType(OwnerReports));
      marker = transport.reads.length;
      await t.scrollUntilVisible(
        find.byType(HomeOperatingStatement),
        220,
        scrollable: find.byType(Scrollable).first,
        maxScrolls: 100,
      );
      await t.ensureVisible(find.byType(HomeOperatingStatement));
      await t.pumpAndSettle();
      await binding.takeScreenshot(
        'phase-approved-owner-operating-home-${light ? 'light' : 'dark'}',
      );
      await tap(
        find.descendant(
          of: find.byType(HomeOperatingStatement),
          matching: find.text('تقرير الأرباح والخسائر'),
        ),
      );
      await wait(find.byType(NativeOperatingStatement));
      final operating = t.widget<NativeOperatingStatement>(
        find.byType(NativeOperatingStatement),
      );
      expect(operating.filters['branchId'], branch['id']);
      expect(operating.filters['from'], filters['from']);
      expect(operating.filters['to'], filters['to']);
      final detail = await reply(
        '/api/v1/reports/operating-statement?',
        marker,
      );
      for (final key in ['totalIncome', 'totalExpenses', 'surplus']) {
        expect(detail[key], statement[key]);
      }
      await wait(find.text('الفائض والعجز التشغيلي'));
      await binding.takeScreenshot(
        'phase-approved-owner-operating-detail-${light ? 'light' : 'dark'}',
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
