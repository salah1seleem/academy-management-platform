import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_reports.dart';
import 'package:academy_mobile/features/owner/external_agreements.dart';
import 'package:academy_mobile/features/admin/native_product_orders.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

Json ledger(String type) => {
  'collectionType': type,
  'collectionId': 'source-id',
  'receiptId': 'original-receipt-id',
  'receiptNumber': 'REF-TEST',
  'player': 'لاعب اصطناعي',
  'sport': 'كرة القدم',
  'branch': 'مدينة نصر',
  'group': 'مجموعة الاختبار',
  'plan': 'الخدمة المحفوظة',
  'amount': type == 'Refund' || type == 'Void' ? 0 : 450,
  'netAmount': type == 'Refund' || type == 'Void' ? -1 : 450,
  'refundAmount': type == 'Refund' || type == 'Void' ? 1 : 0,
  'currency': 'EGP',
  'confirmedAtUtc': '2026-10-22T23:45:00Z',
  'paymentMethod': 'Cash',
  'providerReference': 'saved-reference',
};

void main() {
  testWidgets('collection detail and receipt show the same academy time', (
    t,
  ) async {
    const utc = '2026-10-23T09:00:00Z';
    const local = '2026-10-22T23:00:00-10:00';
    final calls = <String>[];
    final auth = await authenticate('AcademyOwner', (method, path, _, _) async {
      calls.add('$method $path');
      return {
        'receiptNumber': 'REF-TEST',
        'playerNameSnapshot': 'لاعب اصطناعي',
        'amount': 450,
        'currency': 'EGP',
        'paidAtUtc': utc,
        'paidAtAcademy': local,
      };
    });
    await t.pumpWidget(
      app(
        FinancialCollectionDetail(
          auth: auth,
          row: {
            ...ledger('Subscription'),
            'confirmedAtUtc': utc,
            'confirmedAtAcademy': local,
          },
        ),
      ),
    );
    await t.pumpAndSettle();
    final date = find.text('تاريخ التأكيد بتوقيت الأكاديمية');
    await t.scrollUntilVisible(date, 150);
    final tile = t.widget<ListTile>(
      find.ancestor(of: date, matching: find.byType(ListTile)),
    );
    final stamp = receiptTimestamp(t.element(date), utc, local)!;
    expect(tile.subtitle, isNotNull);
    expect(find.text(stamp.text), findsOneWidget);
    expect(stamp.text, contains('٢٢'));
    await t.scrollUntilVisible(find.text('عرض الإيصال'), 150);
    await t.tap(find.text('عرض الإيصال').hitTestable());
    await t.pumpAndSettle();
    expect(find.textContaining(stamp.text), findsOneWidget);
    expect(calls, ['GET /api/v1/subscriptions/receipts/original-receipt-id']);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  testWidgets(
    'Historical collection detail and receipt agree on the paid day',
    (t) async {
      final calls = <String>[];
      final auth = await authenticate('AcademyOwner', (
        method,
        path,
        body,
        token,
      ) async {
        expect(method, 'GET');
        calls.add(path);
        return {
          'receiptNumber': 'HIST-600',
          'playerNameSnapshot': 'لاعب اصطناعي',
          'historicalPaidOn': '2026-09-01',
          'historicalPeriod': {
            'startDate': '2026-09-01',
            'endDate': '2026-09-30',
          },
          'amount': 600,
          'currency': 'EGP',
          'paymentMethod': 'Cash',
        };
      });
      await t.pumpWidget(
        app(
          FinancialCollectionDetail(
            auth: auth,
            row: {
              ...ledger('Subscription'),
              'receiptNumber': 'HIST-600',
              'provider': 'HistoricalImport',
              'providerReference': 'HIST-INTERNAL',
              'historicalPaidOn': '2026-09-01',
              'netAmount': 600,
            },
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('تحصيل سابق'), findsOneWidget);
      expect(t.widget<MoneyText>(find.byType(MoneyText)).value, 600);
      await t.scrollUntilVisible(find.text('تاريخ القبض'), 150);
      expect(find.text('١ سبتمبر ٢٠٢٦'), findsOneWidget);
      expect(find.text('تاريخ التأكيد بالتوقيت العالمي'), findsNothing);
      expect(find.text('HIST-INTERNAL'), findsNothing);
      await t.scrollUntilVisible(find.text('عرض الإيصال'), 150);
      await t.tap(find.text('عرض الإيصال').hitTestable());
      await t.pumpAndSettle();
      expect(calls, ['/api/v1/subscriptions/receipts/original-receipt-id']);
      expect(find.textContaining('فترة الاشتراك من'), findsOneWidget);
      expect(find.textContaining('تاريخ القبض'), findsOneWidget);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  for (final kind in ['Refund', 'Void']) {
    testWidgets(
      '$kind preserves negative ledger net and original receipt without displaying zero as payout',
      (t) async {
        t.view.physicalSize = const Size(375, 667);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.resetPhysicalSize);
        addTearDown(t.view.resetDevicePixelRatio);
        final calls = <String>[];
        final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
          expect(m, 'GET');
          calls.add(p);
          return {
            'academyName': 'أكاديمية الاختبار',
            'receiptNumber': 'ORIGINAL-900',
            'playerNameSnapshot': 'لاعب اصطناعي',
            'sportNameSnapshot': 'كرة القدم',
            'planNameSnapshot': 'الباقة الأصلية',
            'originalAmount': 1000,
            'discountAmount': 100,
            'amount': 900,
            'currency': 'EGP',
            'paidAtUtc': '2026-10-21T15:00:00Z',
          };
        });
        await t.pumpWidget(
          app(
            FinancialCollectionDetail(
              auth: auth,
              row: {
                ...ledger(kind),
                if (kind == 'Void')
                  'confirmedAtAcademy': '2026-10-22T23:45:00+02:00',
              },
            ),
            scale: kind == 'Void' ? 2 : 1,
          ),
        );
        await t.pumpAndSettle();
        expect(t.widget<MoneyText>(find.byType(MoneyText)).value, -1);
        await t.scrollUntilVisible(
          find.textContaining('قيمة المبلغ المردود: 1'),
          150,
        );
        expect(find.textContaining('قيمة المبلغ المردود: 1'), findsOneWidget);
        expect(find.text('المبلغ'), findsNothing);
        await t.scrollUntilVisible(
          find.text('تاريخ الصرف بالتوقيت العالمي'),
          200,
        );
        expect(find.text('تاريخ الصرف بالتوقيت العالمي'), findsOneWidget);
        await t.scrollUntilVisible(find.text('عرض إيصال التحصيل الأصلي'), 200);
        await t.pumpAndSettle();
        await t.ensureVisible(find.text('عرض إيصال التحصيل الأصلي'));
        await t.pumpAndSettle();
        await t.tap(find.text('عرض إيصال التحصيل الأصلي'));
        await t.pumpAndSettle();
        expect(
          t.widget<ReceiptPage>(find.byType(ReceiptPage)).id,
          'original-receipt-id',
        );
        expect(calls, ['/api/v1/subscriptions/receipts/original-receipt-id']);
        expect(find.text('ORIGINAL-900'), findsOneWidget);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }

  for (final type in ['Product', 'Tournament', 'DayPass', 'External']) {
    testWidgets(
      '$type detail retains its source without requesting a subscription receipt',
      (t) async {
        final calls = <String>[];
        final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
          expect(m, 'GET');
          calls.add(p);
          throw const ApiFailure(403, 'ليس لديك إذن لقراءة المصدر.');
        });
        await t.pumpWidget(
          app(FinancialCollectionDetail(auth: auth, row: ledger(type))),
        );
        await t.pumpAndSettle();
        expect(t.widget<MoneyText>(find.byType(MoneyText)).value, 450);
        await t.scrollUntilVisible(find.text('الخدمة المحفوظة'), 200);
        await t.scrollUntilVisible(
          find.text('تاريخ التأكيد بالتوقيت العالمي'),
          200,
        );
        if (type == 'Product' || type == 'External') {
          final action = type == 'Product'
              ? 'عرض طلب المنتج'
              : 'عرض الاتفاق وإيصالاته';
          await t.scrollUntilVisible(find.text(action), 200);
          await t.pumpAndSettle();
          await t.ensureVisible(find.text(action));
          await t.pumpAndSettle();
          await t.tap(find.text(action));
          await t.pumpAndSettle();
          if (type == 'Product') {
            expect(
              t
                  .widget<NativeProductOrderReview>(
                    find.byType(NativeProductOrderReview),
                  )
                  .id,
              'source-id',
            );
            expect(calls.single, '/api/v1/manage/product-sales/source-id');
          } else {
            expect(
              t
                  .widget<NativeExternalAgreementDetail>(
                    find.byType(NativeExternalAgreementDetail),
                  )
                  .id,
              'original-receipt-id',
            );
            expect(
              calls.single,
              '/api/v1/finance/external-agreements/original-receipt-id',
            );
          }
          expect(find.text('ليس لديك إذن لقراءة المصدر.'), findsOneWidget);
        }
        expect(calls.any((p) => p.contains('/subscriptions/receipts/')), false);
        expect(find.byType(ReceiptPage), findsNothing);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }

  testWidgets(
    'Snapshot hides on logout and empty receipt identifier never becomes a fabricated receipt link',
    (t) async {
      final auth = await authenticate(
        'AcademyOwner',
        (m, p, b, token) async => {},
      );
      await t.pumpWidget(
        app(
          FinancialCollectionDetail(
            auth: auth,
            row: {
              ...ledger('Subscription'),
              'receiptId': '00000000-0000-0000-0000-000000000000',
            },
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('لاعب اصطناعي'), findsOneWidget);
      await t.scrollUntilVisible(find.text('saved-reference'), 200);
      expect(find.text('عرض الإيصال'), findsNothing);
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('الخدمة المحفوظة'), findsNothing);
      expect(find.byType(MoneyText), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
