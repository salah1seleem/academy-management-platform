import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'guardian_test.dart' show logged;

void main() {
  for (final (paymentMethod, reference, visible) in [
    ('Cash', 'cash-operation-legacy', false),
    ('Online', 'manual-1234567890abcdef1234567890abcdef', false),
    ('Online', 'PSP-TRACK-101', true),
  ]) {
    testWidgets('Receipt reference policy for $paymentMethod ($visible)', (
      t,
    ) async {
      final auth = await logged(
        (method, path, body, token) async => {
          'receiptNumber': 'RP-600',
          'playerNameSnapshot': 'لاعب اختبار',
          'amount': '737.60',
          'currency': 'EGP',
          'paidAtUtc': '2026-11-09T06:00:00Z',
          'originalAmount': 922,
          'discountAmount': '184.40',
          'paymentMethod': paymentMethod,
          'providerReference': reference,
        },
      );
      await t.pumpWidget(_receiptApp(ReceiptPage(auth: auth, id: 'receipt')));
      await t.pumpAndSettle();
      expect(find.text('RP-600'), findsOneWidget);
      expect(find.text('737.60 ج.م'), findsOneWidget);
      expect(find.textContaining('184.40'), findsOneWidget);
      expect(find.text('مرجع الدفع'), visible ? findsOneWidget : findsNothing);
      expect(find.text(reference), visible ? findsOneWidget : findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    });
  }

  testWidgets('Incomplete receipt has no paid amount', (t) async {
    final auth = await logged(
      (method, path, body, token) async => {
        'receiptNumber': 'RP-MISSING',
        'currency': 'EGP',
        'paidAtUtc': '2026-11-09T06:00:00Z',
        'amount': '  ',
      },
    );
    await t.pumpWidget(_receiptApp(ReceiptPage(auth: auth, id: 'receipt')));
    await t.pumpAndSettle();
    expect(find.text('الإيصال غير متاح.'), findsOneWidget);
    expect(find.text('المبلغ المدفوع'), findsNothing);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  for (final (width, scale) in [(390.0, 1.0), (320.0, 2.0)]) {
    testWidgets(
      'Same-name receipt options distinguish codes at $width/$scale',
      (t) async {
        t.view.physicalSize = Size(width, 950);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.reset);
        const name = 'اسم لاعب مطابق للاختبار';
        const menu = ValueKey('receipt-child-menu');
        final paths = <String>[];
        final auth = await logged((method, path, body, token) async {
          expect(method, 'GET');
          paths.add(path);
          final child = Uri.parse(path).queryParameters['playerId'];
          if (path.endsWith('/receipt-b')) {
            return {
              'receiptNumber': 'RC-D02',
              'playerNameSnapshot': name,
              'amount': 725,
              'currency': 'EGP',
              'paidAtUtc': '2026-11-11T22:00:00Z',
            };
          }
          return {
            'children': [
              {'id': 'a', 'arabicName': name, 'playerCode': 'PLR-D01'},
              {'id': 'b', 'arabicName': name, 'playerCode': 'PLR-D02'},
            ],
            'items': [
              for (final (id, code, number) in [
                ('a', 'PLR-D01', 'RC-D01'),
                ('b', 'PLR-D02', 'RC-D02'),
              ])
                if (child == null || child == id)
                  {
                    'id': 'receipt-$id',
                    'receiptNumber': number,
                    'player': name,
                    'playerCode': code,
                    'amount': 725,
                    'currency': 'EGP',
                    'paidAtUtc': '2026-11-11T22:00:00Z',
                  },
            ],
          };
        });
        addTearDown(auth.dispose);
        await t.pumpWidget(
          MaterialApp(
            locale: const Locale('ar'),
            supportedLocales: const [Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            theme: AcademyTheme.light(),
            builder: (context, child) => MediaQuery(
              data: MediaQuery.of(context)
                  .copyWith(textScaler: TextScaler.linear(scale)),
              child: child!,
            ),
            home: ReceiptsPage(auth: auth),
          ),
        );
        await t.pumpAndSettle();
        await t.tap(find.byKey(menu));
        await t.pumpAndSettle();
        expect(find.text('PLR-D01').hitTestable(), findsOneWidget);
        expect(find.text('PLR-D02').hitTestable(), findsOneWidget);
        await t.tap(find.text('PLR-D02').hitTestable());
        await t.pumpAndSettle();
        expect(paths.last, endsWith('playerId=b'));
        expect(find.text('RC-D01'), findsNothing);
        expect(find.textContaining('كود اللاعب'), findsOneWidget);
        expect(find.textContaining('\u2066PLR-D02\u2069'), findsOneWidget);
        final receipt = find.text('RC-D02');
        await t.ensureVisible(receipt);
        await t.tap(receipt);
        await t.pumpAndSettle();
        expect(paths.last, endsWith('/receipt-b'));
        final locale = MaterialLocalizations.of(
          t.element(find.byType(ReceiptPage)),
        );
        await t.tap(find.byTooltip(locale.backButtonTooltip));
        await t.pumpAndSettle();
        expect(receipt.hitTestable(), findsOneWidget);
        expect(
          t
              .widget<DropdownButtonFormField<String>>(find.byKey(menu))
              .initialValue,
          'b',
        );
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
      },
    );
  }

  testWidgets('Subscription list and receipt use the academy clock', (t) async {
    t.view.physicalSize = const Size(320, 1050);
    t.view.devicePixelRatio = 1;
    addTearDown(t.view.reset);
    final row = {
      'id': 'midnight',
      'receiptNumber': 'MID-900',
      'player': 'لاعب اختبار',
      'playerNameSnapshot': 'لاعب اختبار',
      'amount': 900,
      'currency': 'EGP',
      'paidAtUtc': '2026-11-11T22:00:00Z',
      'paidAtAcademy': '2026-11-12T00:00:00+02:00',
    };
    final auth = await logged((method, path, body, token) async {
      if (path.endsWith('/midnight')) return row;
      return {
        'children': [],
        'items': [row],
      };
    });
    await t.pumpWidget(
      MaterialApp(
        locale: const Locale('ar'),
        supportedLocales: const [Locale('ar')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        theme: AcademyTheme.light(),
        builder: (context, child) => MediaQuery(
          data: MediaQuery.of(context)
              .copyWith(textScaler: const TextScaler.linear(2)),
          child: child!,
        ),
        home: ReceiptsPage(auth: auth),
      ),
    );
    await t.pumpAndSettle();
    final localizations = MaterialLocalizations.of(
      t.element(find.byType(ReceiptsPage)),
    );
    final day = localizations.formatShortDate(DateTime.utc(2026, 11, 12));
    final midnight = localizations.formatTimeOfDay(
      const TimeOfDay(hour: 0, minute: 0),
    );
    final display = '$day · $midnight';
    expect(find.text(display), findsOneWidget);
    await t.tap(find.text('MID-900'));
    await t.pumpAndSettle();
    expect(find.textContaining(display), findsOneWidget);
    expect(find.text('بتوقيت الأكاديمية'), findsOneWidget);
    expect(t.takeException(), isNull);
    await t.tap(find.byTooltip(localizations.backButtonTooltip));
    await t.pumpAndSettle();
    expect(find.text(display), findsOneWidget);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  for (final academyTime in [null, 'bad time', '2026-11-12T00:01:00+02:00']) {
    testWidgets('Uses UTC when the academy timestamp is $academyTime', (
      t,
    ) async {
      final auth = await logged(
        (method, path, body, token) async => {
          'receiptNumber': 'LEGACY-UTC',
          'playerNameSnapshot': 'لاعب اختبار',
          'amount': 900,
          'currency': 'EGP',
          'paidAtUtc': '2026-11-11T22:00:00Z',
          'paidAtAcademy': academyTime,
        },
      );
      await t.pumpWidget(_receiptApp(ReceiptPage(auth: auth, id: 'legacy')));
      await t.pumpAndSettle();
      final locale = MaterialLocalizations.of(
        t.element(find.byType(ReceiptPage)),
      );
      final day = locale.formatShortDate(DateTime.utc(2026, 11, 11));
      final time = locale.formatTimeOfDay(const TimeOfDay(hour: 22, minute: 0));
      expect(find.textContaining('$day · $time'), findsOneWidget);
      expect(find.text('بالتوقيت العالمي'), findsOneWidget);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    });
  }

  testWidgets('A historical receipt shows the source day and the paid period', (
    t,
  ) async {
    final paths = <String>[];
    final auth = await logged((method, path, body, token) async {
      expect(method, 'GET');
      paths.add(path);
      if (path.endsWith('/historic')) {
        return {
          'receiptNumber': 'HIST-600',
          'playerNameSnapshot': 'لاعب اختبار',
          'sportNameSnapshot': 'سباحة',
          'originalAmount': 600,
          'amount': 600,
          'discountAmount': 0,
          'currency': 'EGP',
          'paidAtUtc': '2026-09-01T09:00:00Z',
          'historicalPaidOn': '2026-09-01',
          'historicalPeriod': {
            'startDate': '2026-09-01',
            'endDate': '2026-09-30',
          },
          'providerReference': 'HIST-INTERNAL',
          'paymentMethod': 'Cash',
        };
      }
      return {
        'children': [
          {'id': 'child', 'arabicName': 'لاعب اختبار'},
        ],
        'items': [
          {
            'id': 'historic',
            'receiptNumber': 'HIST-600',
            'player': 'لاعب اختبار',
            'sport': 'سباحة',
            'amount': 600,
            'currency': 'EGP',
            'source': 'Subscription',
            'paidAtUtc': '2026-09-01T09:00:00Z',
            'historicalPaidOn': '2026-09-01',
          },
        ],
      };
    });
    await t.pumpWidget(
      _receiptApp(ReceiptsPage(auth: auth, playerId: 'child')),
    );
    await t.pumpAndSettle();
    expect(find.textContaining('تحصيل سابق'), findsOneWidget);
    expect(find.textContaining('12:00'), findsNothing);
    await t.tap(find.text('HIST-600').hitTestable());
    await t.pumpAndSettle();
    expect(paths.last, '/api/v1/guardian/subscriptions/receipts/historic');
    expect(find.textContaining('تاريخ القبض'), findsOneWidget);
    expect(find.textContaining('فترة الاشتراك من'), findsOneWidget);
    expect(find.textContaining('تاريخ ووقت الدفع'), findsNothing);
    expect(find.text('HIST-INTERNAL'), findsNothing);
    expect(find.textContaining('600'), findsWidgets);
    Navigator.of(t.element(find.text('إيصال دفع'))).pop();
    await t.pumpAndSettle();
    expect(paths, hasLength(2));
    expect(find.text('HIST-600'), findsOneWidget);
    await t.pumpWidget(
      _receiptApp(ReceiptPage(auth: auth, id: 'historic', staff: true)),
    );
    await t.pumpAndSettle();
    expect(paths.last, '/api/v1/subscriptions/receipts/historic');
    expect(find.textContaining('فترة الاشتراك من'), findsOneWidget);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  for (final width in [360.0, 390.0]) {
    testWidgets(
      'Profile keeps child filter when a tournament receipt is opened and closed at $width',
      (t) async {
        t.view.physicalSize = Size(width, 900);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.reset);
        final reads = <String>[];
        final auth = await logged((method, path, body, token) async {
          expect(method, 'GET');
          reads.add(path);
          if (path.endsWith('/tournament')) {
            return {
              'status': 'Confirmed',
              'receiptNumber': 'TR-DEMO-005',
              'confirmedAtUtc': '2026-11-04T22:30:00Z',
              'player': 'آدم العرض',
              'tournamentName': 'بطولة العرض',
              'amount': 225,
              'currency': 'EGP',
            };
          }
          return {
            'children': [
              {'id': 'other', 'arabicName': 'سلمى العرض'},
              {'id': 'child', 'arabicName': 'آدم العرض'},
            ],
            'items': [
              {
                'id': 'tournament',
                'receiptNumber': 'TR-DEMO-005',
                'paidAtUtc': '2026-11-04T22:30:00Z',
                'player': 'اسم اللاعب السابق',
                'currentPlayer': 'آدم العرض',
                'source': 'Tournament',
                'plan': 'بطولة العرض',
                'amount': 225,
                'currency': 'EGP',
              },
            ],
          };
        });
        await t.pumpWidget(
          MaterialApp(
            locale: const Locale('ar'),
            supportedLocales: const [Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            theme: AcademyTheme.light(),
            builder: (context, child) => MediaQuery(
              data: MediaQuery.of(context)
                  .copyWith(textScaler: const TextScaler.linear(2)),
              child: child!,
            ),
            home: ReceiptsPage(auth: auth, playerId: 'child'),
          ),
        );
        await t.pumpAndSettle();
        expect(
          reads.single,
          '/api/v1/guardian/subscriptions/receipts?page=1&allIssued=true&playerId=child',
        );
        expect(find.text('الاسم وقت الدفع: اسم اللاعب السابق'), findsOneWidget);
        await t.tap(find.text('TR-DEMO-005').hitTestable());
        await t.pumpAndSettle();
        expect(reads.last, '/api/v1/guardian/tournament-payments/tournament');
        expect(find.text('إيصال رسم البطولة'), findsOneWidget);
        expect(find.textContaining('إثبات التحويل'), findsNothing);
        final title = find.text('إيصال رسم البطولة');
        Navigator.of(t.element(title)).pop();
        await t.pumpAndSettle();
        expect(reads, hasLength(2));
        final child = t.widget<Semantics>(
          find.byKey(const ValueKey('family-filter-child')),
        );
        expect(child.properties.selected, isTrue);
        expect(find.text('TR-DEMO-005'), findsOneWidget);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }

  testWidgets('CV-N01 and CV-N02: older reversed receipt and retained filter', (
    tester,
  ) async {
    final requests = <String>[];
    final auth = await logged((method, path, body, token) async {
      expect(method, 'GET');
      requests.add(path);
      if (path.endsWith('/26')) return _reversedDetail;
      return Uri.parse(path).queryParameters['page'] == '2'
          ? _feed([_receipt(26)], page: 2)
          : _feed(List.generate(25, (index) => _receipt(index + 1)));
    });
    addTearDown(auth.dispose);
    await tester.pumpWidget(
      _receiptApp(ReceiptsPage(auth: auth, playerId: 'a')),
    );
    await tester.pumpAndSettle();
    expect(requests, [_first]);
    await _scrollToEnd(tester);
    await tester.pumpAndSettle();
    expect(requests, [_first, _second]);
    final old = find.text('RC-26');
    await tester.scrollUntilVisible(old, 250, scrollable: _vertical);
    expect(find.text('إيصال معكوس'), findsOneWidget);
    await tester.tap(old.hitTestable());
    await tester.pumpAndSettle();
    expect(requests.last, '/api/v1/guardian/subscriptions/receipts/26');
    expect(find.text('المبلغ المدفوع'), findsOneWidget);
    expect(find.textContaining('900'), findsOneWidget);
    expect(find.text('إيصال معكوس'), findsOneWidget);
    Navigator.of(tester.element(find.text('إيصال دفع'))).pop();
    await tester.pumpAndSettle();
    expect(old, findsOneWidget);
    _scrollTop(tester);
    await tester.pumpAndSettle();
    expect(
      tester
          .widget<Semantics>(find.byKey(const ValueKey('family-filter-a')))
          .properties
          .selected,
      isTrue,
    );
    expect(find.text('RC-1'), findsOneWidget);
    expect(requests, [
      _first,
      _second,
      '/api/v1/guardian/subscriptions/receipts/26',
    ]);
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('CV-N03: append failure keeps earlier receipts and retries', (
    tester,
  ) async {
    final requests = <String>[];
    final auth = await logged((method, path, body, token) async {
      requests.add(path);
      if (path == _second) {
        if (requests.where((value) => value == _second).length == 1) {
          throw const ApiFailure(503, 'تعذر تحميل المزيد.');
        }
        return _feed([_receipt(26)], page: 2);
      }
      return _feed(List.generate(25, (index) => _receipt(index + 1)));
    });
    addTearDown(auth.dispose);
    await tester.pumpWidget(
      _receiptApp(ReceiptsPage(auth: auth, playerId: 'a')),
    );
    await tester.pumpAndSettle();
    await _scrollToEnd(tester);
    await tester.pumpAndSettle();
    expect(find.text('RC-25'), findsOneWidget);
    final retry = find.text('تعذر تحميل المزيد · إعادة المحاولة');
    expect(retry, findsOneWidget);
    await tester.tap(retry);
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.text('RC-26'),
      250,
      scrollable: _vertical,
    );
    expect(requests, [_first, _second, _second]);
    expect(retry, findsNothing);
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('CV-N04: late receipt page cannot cross the selected child', (
    tester,
  ) async {
    final requests = <String>[];
    final late = Completer<Object?>();
    final auth = await logged((method, path, body, token) async {
      requests.add(path);
      if (path == _second) return late.future;
      if (path.endsWith('playerId=b')) {
        return _feed([_receipt(99)], total: 1);
      }
      return _feed(List.generate(25, (index) => _receipt(index + 1)));
    });
    addTearDown(auth.dispose);
    await tester.pumpWidget(
      _receiptApp(ReceiptsPage(auth: auth, playerId: 'a')),
    );
    await tester.pumpAndSettle();
    await _scrollToEnd(tester);
    expect(requests, [_first, _second]);
    _scrollTop(tester);
    await tester.pump();
    final other = find.byKey(const ValueKey('family-filter-b'));
    await tester.ensureVisible(other);
    await tester.pump();
    await tester.tap(other.hitTestable());
    await tester.pumpAndSettle();
    late.complete(_feed([_receipt(26)], page: 2));
    await tester.pumpAndSettle();
    expect(find.text('RC-99'), findsOneWidget);
    expect(find.text('RC-1'), findsNothing);
    expect(find.text('RC-26'), findsNothing);
    expect(requests, [_first, _second, _first.replaceFirst('=a', '=b')]);
    await tester.pumpWidget(const SizedBox());
  });

  testWidgets('CV-N05: authorization loss clears previously loaded receipts', (
    tester,
  ) async {
    final nextPage = Completer<Object?>();
    var requestedNext = false;
    final auth = await logged((method, path, body, token) async {
      if (path == _second) {
        requestedNext = true;
        return nextPage.future;
      }
      return _feed(List.generate(25, (index) => _receipt(index + 1)));
    });
    addTearDown(auth.dispose);
    await tester.pumpWidget(
      _receiptApp(ReceiptsPage(auth: auth, playerId: 'a')),
    );
    await tester.pumpAndSettle();
    expect(find.text('RC-1'), findsOneWidget);
    await _scrollToEnd(tester);
    expect(requestedNext, isTrue);
    nextPage.completeError(const ApiFailure(404, 'الإيصالات غير متاحة.'));
    await tester.pumpAndSettle();
    expect(find.text('الإيصالات غير متاحة.'), findsOneWidget);
    expect(find.text('RC-25'), findsNothing);
    expect(find.byKey(const ValueKey('family-filter-a')), findsNothing);
    await tester.pumpWidget(const SizedBox());
  });
}

const _receiptApi = '/api/v1/guardian/subscriptions/receipts';
const _first = '$_receiptApi?page=1&allIssued=true&playerId=a';
const _second = '$_receiptApi?page=2&allIssued=true&playerId=a';
const _reversedDetail = {
  'receiptNumber': 'RC-26',
  'playerNameSnapshot': 'لاعب اختبار',
  'amount': 900,
  'currency': 'EGP',
  'paidAtUtc': '2026-09-28T09:00:00Z',
  'isVoided': true,
};

Map<String, Object> _receipt(int index) => {
  'id': '$index',
  'receiptNumber': 'RC-$index',
  'player': 'لاعب اختبار',
  'amount': 900,
  'currency': 'EGP',
  'isVoided': index == 26,
  'paidAtUtc': '2026-09-28T09:00:00Z',
};

Map<String, Object> _feed(
  List<Map<String, Object>> items, {
  int page = 1,
  int total = 26,
}) => {
  'children': [
    {'id': 'a', 'arabicName': 'الطفل الأول'},
    {'id': 'b', 'arabicName': 'الطفل الثاني'},
  ],
  'items': items,
  'total': total,
  'page': page,
  'pageSize': 25,
};

Widget _receiptApp(Widget home) => MaterialApp(
  locale: const Locale('ar'),
  supportedLocales: const [Locale('ar')],
  localizationsDelegates: GlobalMaterialLocalizations.delegates,
  theme: AcademyTheme.light(),
  home: home,
);

final _vertical = find.byWidgetPredicate(
  (widget) =>
      widget is Scrollable && widget.axisDirection == AxisDirection.down,
);

Future<void> _scrollToEnd(WidgetTester tester) async {
  await tester.scrollUntilVisible(
    find.text('RC-24'),
    430,
    scrollable: _vertical,
  );
  if (_vertical.evaluate().isNotEmpty) {
    await tester.drag(_vertical, const Offset(0, -240));
    await tester.pump();
  }
}

void _scrollTop(WidgetTester tester) =>
    tester.state<ScrollableState>(_vertical).position.jumpTo(0);
