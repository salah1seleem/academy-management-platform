import '../test/introduction_test_helpers.dart';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/native_product_orders.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_reports.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'v48_owner_home_reports_test.dart' show HomeReportReadTransport;

class FinancialRecordAppearance extends Appearance {
  @override
  Future<bool> hasRoleIntro(String role, String sportTheme) async => true;
}

void main() => runFinancialRecordAcceptance();

void runFinancialRecordAcceptance({bool dark = false, int? expectedWidth}) {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Owner financial tab filters and paginates real ledger rows, refund original receipt and product source without writes',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      if (expectedWidth != null) {
        expect(
          (t.view.physicalSize / t.view.devicePixelRatio).width,
          expectedWidth,
        );
      }
      final transport = HomeReportReadTransport(AppConfig('Demo', base));
      final auth = AuthController(
        transport,
        SecureRefreshStore('v73.5278.approved-intro.AcademyOwner'),
      );
      final appearance = FinancialRecordAppearance()..dark = dark;
      t.platformDispatcher.accessibilityFeaturesTestValue =
          const FakeAccessibilityFeatures(disableAnimations: true);
      addTearDown(t.platformDispatcher.clearAccessibilityFeaturesTestValue);
      Future<void> capture(String oldName) async {
        expect(t.takeException(), isNull);
        final name = expectedWidth == null
            ? oldName
            : 'phase-approved-financial-records-$expectedWidth-${dark ? 'dark' : 'light'}-${oldName.replaceFirst('phase-approved-financial-', '').replaceFirst('-light', '')}';
        await binding.takeScreenshot(name);
      }

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

      Future<void> scroll(Finder f, {bool up = false}) async {
        await t.scrollUntilVisible(
          f,
          up ? -250 : 250,
          scrollable: find.byType(Scrollable).last,
          maxScrolls: 80,
        );
        await t.pumpAndSettle();
      }

      Future<void> back() async {
        if (find.byType(BackButton).evaluate().isNotEmpty) {
          await t.tap(find.byType(BackButton).last);
          await t.pumpAndSettle();
          return;
        }
        final buttons = find.descendant(
          of: find.byType(AppBar).last,
          matching: find.byType(IconButton),
        );
        expect(buttons, findsWidgets);
        await t.tap(buttons.first);
        await t.pumpAndSettle();
      }

      Future<Json> report(String kind, {int page = 1}) async {
        for (var i = 0; i < 240; i++) {
          final reads = transport.reads.where((r) {
            if (!r.path.startsWith('/api/v1/reports/financial?')) return false;
            final q = Uri.parse(r.path).queryParameters;
            return q['allDates'] == 'true' &&
                (q['collectionType'] ?? '') == kind &&
                q['page'] == '$page';
          });
          if (reads.isNotEmpty) {
            await t.pumpAndSettle();
            return obj(reads.last.value);
          }
          await t.pump(const Duration(milliseconds: 150));
        }
        fail('Exact ledger scope was not read');
      }

      Future<void> openRow(Json row) async {
        final text = find.textContaining(textOf(row['receiptNumber']));
        await scroll(text);
        await t.tap(text);
        await wait(find.byType(FinancialCollectionDetail));
        expect(
          t
              .widget<FinancialCollectionDetail>(
                find.byType(FinancialCollectionDetail),
              )
              .row['collectionId'],
          row['collectionId'],
        );
        expect(
          t.widget<MoneyText>(find.byType(MoneyText).last).value,
          row['netAmount'],
        );
      }

      Future<void> originalReceipt(Json row, String action, String shot) async {
        await scroll(find.text(action));
        await t.tap(find.text(action));
        await wait(find.byType(ReceiptPage));
        await wait(find.text('الإيصال صادر من سجل التحصيل على الخادم.'));
        expect(
          t.widget<ReceiptPage>(find.byType(ReceiptPage)).id,
          row['receiptId'],
        );
        final saved = obj(
          transport.reads
              .lastWhere(
                (r) =>
                    r.path ==
                    '/api/v1/subscriptions/receipts/${row['receiptId']}',
              )
              .value,
        );
        expect(
          saved['receiptNumber'],
          textOf(row['receiptNumber']).replaceFirst('استرداد — ', ''),
        );
        await capture(shot);
        await back();
      }

      try {
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
        final before = obj(
          await auth.request(
            'GET',
            '/api/v1/reports/financial?allDates=true&page=1&pageSize=25',
          ),
        );
        expect(before['totalCount'], 73);
        expect(before['netAmount'], 61734);
        await t.pumpWidget(
          AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
        );
        await wait(find.text('التقارير'));
        await t.tap(find.text('التقارير'));
        await wait(find.byType(OwnerReports));
        await wait(find.text('التقارير المالية'));
        await t.tap(find.text('التقارير المالية'));
        await wait(find.text('الإيرادات والتحصيلات'));
        await t.tap(find.text('الإيرادات والتحصيلات'));
        await wait(find.text('مصادر التحصيل'));
        transport.reads.clear();
        await t.tap(find.byTooltip('مسح الفلاتر').last);
        final full = await report('');
        expect(full['totalCount'], before['totalCount']);
        expect(full['netAmount'], before['netAmount']);

        await scroll(find.text('الاستردادات'));
        await t.tap(find.text('الاستردادات'));
        final refunds = await report('Refund');
        expect(refunds['totalCount'], 1);
        final refund = rows(refunds['items']).single;
        expect(refund['netAmount'], -1);
        expect(refund['amount'], 0);
        await openRow(refund);
        await capture('phase-approved-financial-refund-detail-light');
        await originalReceipt(
          refund,
          'عرض إيصال التحصيل الأصلي',
          'phase-approved-financial-refund-original-receipt',
        );
        await back();
        transport.reads.clear();
        await t.tap(find.byTooltip('مسح الفلاتر').last);
        await report('');
        await scroll(find.text('المنتجات الرياضية'), up: true);
        await t.tap(find.text('المنتجات الرياضية'));
        final products = await report('Product');
        final product = rows(products['items']).single;
        expect(product['netAmount'], 450);
        await openRow(product);
        await scroll(find.text('عرض طلب المنتج'));
        await t.tap(find.text('عرض طلب المنتج'));
        await wait(find.byType(NativeProductOrderReview));
        await wait(find.text('مؤكد'));
        final source = obj(
          transport.reads
              .lastWhere(
                (r) =>
                    r.path ==
                    '/api/v1/manage/product-sales/${product['collectionId']}',
              )
              .value,
        );
        expect(source['status'], 'Confirmed');
        expect(source['id'], product['collectionId']);
        expect(source['amount'], product['amount']);
        await capture('phase-approved-financial-product-source-light');
        await back();
        await back();
        transport.reads.clear();
        await t.tap(find.byTooltip('مسح الفلاتر').last);
        await report('');
        await scroll(find.text('الاشتراكات'), up: true);
        await t.tap(find.text('الاشتراكات'));
        final first = await report('Subscription');
        await scroll(find.text('التالي'));
        await t.tap(find.text('التالي'));
        final second = await report('Subscription', page: 2);
        expect(second['netAmount'], first['netAmount']);
        expect(second['totalCount'], first['totalCount']);
        final receiptRow = rows(second['items'])
            .firstWhere((r) => r['collectionType'] == 'Subscription');
        expect(
          rows(first['items'])
              .any((r) => r['collectionId'] == receiptRow['collectionId']),
          false,
        );
        await openRow(receiptRow);
        await originalReceipt(
          receiptRow,
          'عرض الإيصال',
          'phase-approved-financial-page-two-subscription-receipt',
        );
        final after = obj(
          await auth.request(
            'GET',
            '/api/v1/reports/financial?allDates=true&page=1&pageSize=25',
          ),
        );
        expect(after['totalCount'], before['totalCount']);
        expect(after['netAmount'], before['netAmount']);
      } finally {
        await t.pumpWidget(const SizedBox.shrink());
        await t.pumpAndSettle();
        auth.dispose();
        appearance.dispose();
      }
    },
  );
}
