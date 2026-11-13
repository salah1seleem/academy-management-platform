import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:academy_mobile/features/admin/native_renewal.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_reports.dart';
import 'package:academy_mobile/features/owner/provider_quarantine.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show app, authenticate;

const _root = '/api/v1/finance/provider-quarantine';
const _issue = {
  'id': 'issue',
  'paymentRequestId': 'payment',
  'player': 'لاعب اصطناعي',
  'branch': 'فرع تجريبي',
  'provider': 'InternalTest',
  'providerEventId': 'demo-event',
  'reasonCode': 'amount-mismatch',
  'receivedStatus': 'succeeded',
  'expectedReference': 'reference',
  'receivedReference': 'reference',
  'expectedAmount': '900.00',
  'receivedAmount': '900.25',
  'expectedCurrency': 'EGP',
  'receivedCurrency': 'EGP',
  'createdAtUtc': '2026-11-10T22:00:00Z',
  'observedAtCairo': '2026-11-11T00:00:00',
  'paymentStatus': 'Pending',
};

void viewport(WidgetTester tester, double width) {
  tester.view.physicalSize = Size(width, 900);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
}

Finder pagerButton(String label) => find.byWidgetPredicate(
  (widget) => widget is IconButton && widget.tooltip == label,
);

Map<String, Object?> issuePage(Uri query, {bool empty = false}) {
  final p = query.queryParameters;
  final page = int.parse(p['page'] ?? '1');
  final monthly = p['monthToDate'] == 'true';
  return {
    'from': monthly ? '2026-11-01' : p['from'],
    'to': monthly ? '2026-11-12' : p['to'],
    'page': page,
    'pageSize': 10,
    'total': empty ? 0 : 14,
    'items': [
      if (!empty)
        for (var index = 0; index < (page == 1 ? 10 : 4); index++)
          {..._issue, 'id': 'issue-$page-$index'},
    ],
  };
}

void main() {
  for (final (width, scale) in [(320.0, 2.0), (390.0, 1.0), (430.0, 1.0)]) {
    testWidgets('preserves both amounts and RTL at $width and $scale', (
      t,
    ) async {
      viewport(t, width);
      await t.pumpWidget(
        app(
          Scaffold(
            body: ProviderQuarantineContent(
              data: {
                'page': 1,
                'total': 1,
                'pageSize': 10,
                'items': [
                  {..._issue, 'paymentStatus': 'Confirmed'},
                ],
              },
            ),
          ),
          scale: scale,
        ),
      );
      expect(find.text('تم تأكيد الطلب'), findsOneWidget);
      expect(find.text('المبلغ مختلف'), findsOneWidget);
      expect(find.textContaining('بتوقيت القاهرة'), findsOneWidget);
      expect(
        t.widgetList<MoneyText>(find.byType(MoneyText)).map((w) => w.value),
        ['900.00', '900.25'],
      );
      expect(
        Directionality.of(t.element(find.byType(MoneyText).first)),
        TextDirection.rtl,
      );
      expect(find.textContaining('إشارة صحيحة أخرى'), findsNothing);
      expect(t.takeException(), isNull);
    });
  }

  testWidgets('paginates, preserves page on back and clears all dates', (
    t,
  ) async {
    viewport(t, 390);
    final requests = <Uri>[];
    final auth = await authenticate('AcademyOwner', (
      method,
      path,
      body,
      token,
    ) async {
      expect(method, 'GET');
      final uri = Uri.parse(path);
      requests.add(uri);
      if (uri.path.startsWith('$_root/')) {
        return {..._issue, 'id': uri.pathSegments.last};
      }
      return issuePage(uri);
    });
    await t.pumpWidget(app(NativeProviderQuarantine(auth: auth)));
    await t.pumpAndSettle();
    expect(requests.single.queryParameters['monthToDate'], 'true');
    expect(find.text('\u20661–10\u2069 من 14'), findsOneWidget);
    expect(
      find.byWidgetPredicate(
        (w) => w is UiText && w.semanticsLabel == 'من 1 إلى 10 من 14 سجل',
      ),
      findsOneWidget,
    );
    expect(
      t.widget<IconButton>(pagerButton('الصفحة السابقة')).onPressed,
      isNull,
    );
    await t.tap(find.byTooltip('الصفحة التالية'));
    await t.pumpAndSettle();
    expect(requests.last.queryParameters, {
      'page': '2',
      'pageSize': '10',
      'from': '2026-11-01',
      'to': '2026-11-12',
    });
    expect(find.text('\u206611–14\u2069 من 14'), findsOneWidget);
    expect(
      t.widget<IconButton>(pagerButton('الصفحة التالية')).onPressed,
      isNull,
    );
    await t.tap(find.byKey(const ValueKey('provider-issue-issue-2-0')));
    await t.pumpAndSettle();
    expect(find.text('تفاصيل اختلاف الدفع'), findsOneWidget);
    expect(requests.last.path, '$_root/issue-2-0');
    await t.tap(find.byType(BackButton).last);
    await t.pumpAndSettle();
    expect(find.text('\u206611–14\u2069 من 14'), findsOneWidget);
    await t.tap(find.byTooltip('الصفحة السابقة'));
    await t.pumpAndSettle();
    expect(find.text('\u20661–10\u2069 من 14'), findsOneWidget);
    await t.tap(find.byTooltip('مسح الفترة'));
    await t.pumpAndSettle();
    expect(requests.last.queryParameters, {'page': '1', 'pageSize': '10'});
    expect(find.byTooltip('مسح الفترة'), findsNothing);
  });

  testWidgets('chooses an actual date and keeps a valid empty range', (
    t,
  ) async {
    viewport(t, 390);
    final requests = <Uri>[];
    final auth = await authenticate('AcademyOwner', (
      method,
      path,
      body,
      token,
    ) async {
      final uri = Uri.parse(path);
      requests.add(uri);
      return issuePage(uri, empty: uri.queryParameters.containsKey('from'));
    });
    await t.pumpWidget(app(NativeProviderQuarantine(auth: auth)));
    await t.pumpAndSettle();
    await t.tap(find.text('من').first);
    await t.pumpAndSettle();
    final localization = MaterialLocalizations.of(
      t.element(find.byType(DatePickerDialog)),
    );
    await t.tap(find.byTooltip(localization.inputDateModeButtonLabel));
    await t.pumpAndSettle();
    await t.enterText(
      find.byType(TextFormField).last,
      localization.formatCompactDate(DateTime(2026, 12, 1)),
    );
    await t.tap(find.text(localization.okButtonLabel));
    await t.pumpAndSettle();
    expect(requests.last.queryParameters, {
      'page': '1',
      'pageSize': '10',
      'from': '2026-12-01',
      'to': '2026-12-01',
    });
    expect(find.text('لا توجد اختلافات في هذه الفترة.'), findsOneWidget);
    expect(find.byTooltip('الصفحة التالية'), findsNothing);
    await t.tap(find.byTooltip('مسح الفترة'));
    await t.pumpAndSettle();
    expect(find.text('\u20661–10\u2069 من 14'), findsOneWidget);
  });

  testWidgets('shows the linked payment without offering collection', (
    t,
  ) async {
    viewport(t, 390);
    final paths = <String>[];
    final auth = await authenticate('AcademyOwner', (
      method,
      path,
      body,
      token,
    ) async {
      expect(method, 'GET');
      paths.add(path);
      if (path == '$_root/issue') return _issue;
      if (path == '/api/v1/subscriptions/payments/payment') {
        return {
          'id': 'payment',
          'isCurrent': true,
          'canCollect': true,
          'status': 'Pending',
          'player': 'لاعب اصطناعي',
          'plan': 'اشتراك شهري',
          'sport': 'كرة قدم',
          'currency': 'EGP',
          'originalAmount': 900,
          'discountAmount': 0,
          'finalAmount': 900,
        };
      }
      return <String, Object?>{};
    });
    await t.pumpWidget(app(NativeProviderIssue(auth: auth, id: 'issue')));
    await t.pumpAndSettle();
    expect(find.text('عرض إيصال التحصيل'), findsNothing);
    await t.tap(find.text('فتح طلب الدفع'));
    await t.pumpAndSettle();
    expect(
      t
          .widget<NativeRenewalPayment>(find.byType(NativeRenewalPayment))
          .readOnly,
      true,
    );
    expect(find.text('بانتظار التحصيل'), findsOneWidget);
    expect(find.text('طريقة التحصيل'), findsNothing);
    expect(find.text('تأكيد التحصيل وإصدار الإيصال'), findsNothing);
    expect(paths, contains('/api/v1/subscriptions/payments/payment'));
    await t.tap(find.byType(BackButton).last);
    await t.pumpAndSettle();
    expect(find.text('تفاصيل اختلاف الدفع'), findsOneWidget);
  });

  testWidgets('retries a failed detail and hides missing details', (t) async {
    viewport(t, 390);
    var calls = 0;
    final auth = await authenticate('AcademyOwner', (
      method,
      path,
      body,
      token,
    ) async {
      if (path == '$_root/issue') {
        if (++calls == 1) {
          throw const ApiFailure(503, 'تعذر تحميل سجل الاختلاف.');
        }
        return _issue;
      }
      throw const ApiFailure(404, 'اختلاف الدفع غير متاح.');
    });
    await t.pumpWidget(app(NativeProviderIssue(auth: auth, id: 'issue')));
    await t.pumpAndSettle();
    expect(find.text('تعذر تحميل سجل الاختلاف.'), findsOneWidget);
    expect(find.text('فتح طلب الدفع'), findsNothing);
    await t.tap(find.text('إعادة المحاولة'));
    await t.pumpAndSettle();
    expect(calls, 2);
    expect(find.text('فتح طلب الدفع'), findsOneWidget);
    await t.pumpWidget(
      app(
        NativeProviderIssue(
          key: const ValueKey('missing'),
          auth: auth,
          id: 'missing',
        ),
      ),
    );
    await t.pumpAndSettle();
    expect(find.text('اختلاف الدفع غير متاح.'), findsOneWidget);
    expect(find.text('فتح طلب الدفع'), findsNothing);
  });

  testWidgets(
    'shows distinct references and currencies behind the disclosure',
    (t) async {
      viewport(t, 320);
      final auth = await authenticate(
        'AcademyOwner',
        (method, path, body, token) async => {
          ..._issue,
          'reasonCode': 'UnknownInternalCode',
          'receivedStatus': 'UnknownInternalStatus',
          'receivedCurrency': 'USD',
          'receivedReference': 'other-payment-reference',
          'signature': 'test-value-never-displayed',
        },
      );
      await t.pumpWidget(
        app(NativeProviderIssue(auth: auth, id: 'issue'), scale: 2),
      );
      await t.pumpAndSettle();
      expect(find.text('اختلاف يتطلب المتابعة'), findsOneWidget);
      final amounts = t.widgetList<MoneyText>(find.byType(MoneyText));
      expect(amounts.map((w) => (w.value, w.currency)), [
        ('900.00', 'EGP'),
        ('900.25', 'USD'),
      ]);
      final disclosure = find.text('تفاصيل الإشارة والمراجع');
      await t.scrollUntilVisible(disclosure, 200);
      await t.pumpAndSettle();
      await t.ensureVisible(disclosure);
      await t.pumpAndSettle();
      await t.tap(disclosure.hitTestable());
      await t.pumpAndSettle();
      expect(find.text('حالة الإشارة غير معروفة'), findsOneWidget);
      await t.scrollUntilVisible(
        find.text('other-payment-reference'),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.text('مرجع الطلب'), findsOneWidget);
      expect(find.text('المرجع الوارد'), findsOneWidget);
      expect(find.text('reference'), findsOneWidget);
      expect(find.text('other-payment-reference'), findsOneWidget);
      expect(find.textContaining('UnknownInternal'), findsNothing);
      expect(find.text('test-value-never-displayed'), findsNothing);
      expect(t.takeException(), isNull);
    },
  );

  for (final role in ['AcademyOwner', 'AcademyAdmin', 'Coach', 'Guardian']) {
    testWidgets('financial entry is owner-only for $role', (t) async {
      viewport(t, 390);
      final auth = await authenticate(
        role,
        (method, path, body, token) async => {
          'trailingMonthFrom': '2026-10-12',
          'today': '2026-11-12',
          'branches': <Object>[],
        },
      );
      await t.pumpWidget(
        app(OwnerReports(auth: auth, initialReport: 'financial-hub')),
      );
      await t.pumpAndSettle();
      expect(find.text('الإيرادات والتحصيلات'), findsOneWidget);
      expect(
        find.text('اختلافات الدفع'),
        role == 'AcademyOwner' ? findsOneWidget : findsNothing,
      );
    });
  }
}
