import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/operating_monthly_chart.dart';
import 'package:academy_mobile/features/owner/owner_cockpit.dart';
import 'package:academy_mobile/features/owner/refined_home_panels.dart';
import 'package:academy_mobile/features/shared/experience_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'v48_owner_home_reports_test.dart' show HomeReportReadTransport;

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'real monthly cash panel opens the exact partial month and branch without financial writes',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      const width = int.fromEnvironment(
        'CASH_EXPECTED_WIDTH',
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
        final c = await auth.requestOtp('01099900010');
        await auth.verify('01099900010', c, '246810');
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
      final expected = obj(
        await auth.request(
          'GET',
          '/api/v1/reports/operating-statement?${Uri(queryParameters: {...filters, 'includeMonthly': 'true'}).query}',
        ),
      );
      final months = rows(expected['monthly']);
      expect(months, isNotEmpty);
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        if (f.evaluate().isEmpty) {
          await binding.takeScreenshot(
            'phase-approved-owner-cash-wait-w$width',
          );
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
        await locate(find.byType(HomeOperatingStatement));
        await wait(find.byType(HomeOperatingStatement));
        final panel = t.widget<HomeOperatingStatement>(
          find.byType(HomeOperatingStatement),
        );
        for (final key in [
          'totalIncome',
          'totalExpenses',
          'surplus',
          'receivables',
          'approvedPayrollPayable',
        ]) {
          expect(panel.data[key], expected[key]);
        }
        final chart = find.descendant(
          of: find.byType(HomeOperatingStatement),
          matching: find.byType(OperatingMonthlyChart),
        );
        await locate(chart);
        expect(t.widget<OperatingMonthlyChart>(chart).items, months);
        await binding.takeScreenshot(
          'phase-approved-owner-cash-monthly-home-w$width-${light ? 'light' : 'dark'}',
        );
        for (final month in {
          for (final m in [months.first, months.last]) m['from']: m,
        }.values) {
          final marker = transport.reads.length;
          final point = find.byKey(
            ValueKey('operating-month-${month['from']}'),
          );
          await locate(point);
          await t.tap(point);
          await t.pumpAndSettle();
          await wait(find.byType(NativeOperatingStatement));
          final detail = t.widget<NativeOperatingStatement>(
            find.byType(NativeOperatingStatement),
          );
          expect(detail.filters['from'], month['from']);
          expect(detail.filters['to'], month['to']);
          expect(detail.filters['branchId'], branch['id']);
          expect(detail.filters['allDates'], 'false');
          for (
            var i = 0;
            i < 240 &&
                !transport.reads
                    .skip(marker)
                    .any(
                      (r) => r.path.startsWith(
                        '/api/v1/reports/operating-statement?',
                      ),
                    );
            i++
          ) {
            await t.pump(const Duration(milliseconds: 150));
          }
          final reply = obj(
            transport.reads
                .skip(marker)
                .lastWhere(
                  (r) =>
                      r.path.startsWith('/api/v1/reports/operating-statement?'),
                )
                .value,
          );
          expect(reply['totalIncome'], month['income']);
          expect(reply['totalExpenses'], month['expenses']);
          expect(reply['surplus'], month['surplus']);
          await wait(find.text('الفائض والعجز التشغيلي'));
          await binding.takeScreenshot(
            'phase-approved-owner-cash-monthly-detail-${textOf(month['from']).replaceAll('-', '')}-w$width-${light ? 'light' : 'dark'}',
          );
          await t.tap(find.byType(BackButton));
          await t.pumpAndSettle();
          await wait(find.byType(HomeOperatingStatement));
        }
        expect(t.takeException(), isNull);
      }
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
