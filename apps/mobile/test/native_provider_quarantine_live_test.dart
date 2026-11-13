import 'dart:async';
import 'dart:io';
import 'dart:ui' as ui;

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:academy_mobile/features/admin/native_renewal.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/provider_quarantine.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show MemoryStore;

const _root = '/api/v1/finance/provider-quarantine';
const _base = 'http://127.0.0.1:5497';
const _capture = ValueKey('native-provider-capture');
const _pending = '55cc6e0c-30d3-4e7a-be8e-b8204e6992be';
const _corrected = '16afb347-4536-47cf-80a1-3764e9711a53';
const _beforeMidnight = '85000010-0000-0000-0000-000000000091';
const _midnight = '85000010-0000-0000-0000-000000000092';
const _foreign = '424a1b41-3007-479c-901f-3d89d420d35f';
const _missing = '11111111-1111-1111-1111-111111111111';

class _Appearance extends Appearance {
  @override
  Future<bool> hasRoleIntro(String role, String sportTheme) async => true;
}

class _FixtureRefreshStore implements RefreshStore {
  _FixtureRefreshStore(this.file);
  final File file;

  @override
  Future<String?> read() async {
    final value = (await file.readAsString()).trim();
    return value.isEmpty ? null : value;
  }

  @override
  Future<void> write(String value) => file.writeAsString(value, flush: true);

  @override
  Future<void> clear() => write('');
}

class _Transport extends HttpApiTransport {
  _Transport(this.io) : super(AppConfig('Demo', _base));
  final Zone io;
  final reads = <(Uri, Object?)>[];
  final failures = <String>{};
  final writes = <String>[];
  bool authenticated = false;

  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    final uri = Uri.parse(path);
    if (authenticated &&
        method != 'GET' &&
        !path.startsWith('/api/v1/mobile/auth/')) {
      writes.add('$method ${uri.path}');
    }
    if (failures.remove(uri.path)) {
      throw const ApiFailure(503, 'تعذر تحميل سجل الاختلاف.');
    }
    // Complete the request in the caller's test zone, including failures.
    final completion = Completer<Object?>();
    io.run(() {
      super
          .send(
            method,
            path,
            body: body,
            token: token,
            idempotencyKey: idempotencyKey,
          )
          .then<void>(completion.complete, onError: completion.completeError);
    });
    final Object? response;
    try {
      response = await completion.future;
    } on ApiFailure catch (failure) {
      debugPrint('native API $method ${uri.path}: ${failure.status}');
      rethrow;
    }
    if (method == 'GET') reads.add((uri, response));
    return response;
  }

  Json last(String path) =>
      obj(reads.lastWhere((read) => read.$1.path == path).$2);
}

Future<void> _wait(WidgetTester tester, Finder target) async {
  for (var i = 0; i < 380 && target.evaluate().isEmpty; i++) {
    await tester.runAsync(
      () => Future<void>.delayed(const Duration(milliseconds: 30)),
    );
    await tester.pump(const Duration(milliseconds: 50));
  }
  if (target.evaluate().isEmpty) {
    debugPrint(
      tester
          .widgetList<Text>(find.byType(Text))
          .map((w) => w.data)
          .whereType<String>()
          .join(' | '),
    );
  }
  expect(target, findsWidgets);
  await tester.pump(const Duration(milliseconds: 350));
}

Future<void> _save(WidgetTester tester, String name) async {
  const output = String.fromEnvironment('QA_PROVIDER_ARTIFACTS');
  if (output.isEmpty) return;
  final boundary = tester.renderObject<RenderRepaintBoundary>(
    find.byKey(_capture),
  );
  await tester.runAsync(() async {
    final image = await boundary.toImage();
    final data = await image.toByteData(format: ui.ImageByteFormat.png);
    await File('$output/$name.png').writeAsBytes(data!.buffer.asUint8List());
    image.dispose();
  });
}

Future<void> _tap(WidgetTester tester, Finder target) async {
  await _wait(tester, target);
  await tester.ensureVisible(target.first);
  await tester.tap(target.first);
  await tester.pump(const Duration(milliseconds: 350));
}

Future<void> _openIssue(WidgetTester tester, String id) async {
  final item = find.byKey(ValueKey('provider-issue-$id'));
  final content = find.byType(ProviderQuarantineContent);
  await _wait(tester, content);
  final scrollable = find
      .descendant(of: content, matching: find.byType(Scrollable))
      .first;
  if (item.hitTestable().evaluate().isEmpty) {
    tester.state<ScrollableState>(scrollable).position.jumpTo(0);
    await tester.pump();
  }
  await tester.scrollUntilVisible(
    item,
    230,
    scrollable: scrollable,
    maxScrolls: 16,
  );
  await _tap(tester, item);
  await _wait(tester, find.text('تفاصيل اختلاف الدفع'));
}

Future<void> _back(WidgetTester tester) async {
  final buttons = find.byType(BackButton);
  await _wait(tester, buttons);
  await _tap(tester, buttons.last);
}

Future<void> _date(WidgetTester tester, String label, DateTime value) async {
  await _tap(tester, find.text(label));
  final dialog = find.byType(DatePickerDialog);
  await _wait(tester, dialog);
  final locale = MaterialLocalizations.of(tester.element(dialog));
  await _tap(tester, find.byTooltip(locale.inputDateModeButtonLabel));
  await tester.enterText(
    find.byType(TextFormField).last,
    locale.formatCompactDate(value),
  );
  await _tap(tester, find.text(locale.okButtonLabel));
}

Future<(AuthController, _Transport)> _login(
  String phone,
  String role, {
  RefreshStore? store,
}) async {
  final transport = _Transport(Zone.current);
  final auth = AuthController(transport, store ?? MemoryStore())
    ..restoring = false;
  if (store != null) await auth.restore();
  if (auth.session == null) {
    await auth.verify(phone, await auth.requestOtp(phone), '246810');
  }
  expect(auth.session?.selected?.role, role);
  transport.authenticated = true;
  return (auth, transport);
}

Future<List<Object?>> _financial(AuthController auth) async {
  final income = obj(
    await auth.request(
      'GET',
      '/api/v1/reports/financial?allDates=true&collectionType=Subscription',
    ),
  );
  final result = obj(
    await auth.request(
      'GET',
      '/api/v1/reports/operating-statement?allDates=true',
    ),
  );
  return [income['totalCount'], income['netAmount'], result['surplus']];
}

void main() => runNativeProviderQuarantineAcceptance();

void runNativeProviderQuarantineAcceptance({
  Future<void> Function(String name)? capture,
}) {
  const enabled = bool.fromEnvironment('QA_NATIVE_PROVIDER_LIVE');
  TestWidgetsFlutterBinding.ensureInitialized();
  if (enabled && capture == null) {
    setUpAll(() async {
      await Future.wait([
        (FontLoader('AcademyArabic')
              ..addFont(rootBundle.load('assets/fonts/AcademyArabic-400.ttf'))
              ..addFont(rootBundle.load('assets/fonts/AcademyArabic-700.ttf')))
            .load(),
        (FontLoader(
          'Cairo',
        )..addFont(rootBundle.load('assets/fonts/Cairo.ttf'))).load(),
        (FontLoader(
          'MaterialIcons',
        )..addFont(rootBundle.load('fonts/MaterialIcons-Regular.otf'))).load(),
      ]);
    });
  }
  testWidgets(
    'opens actual owner reports, discrepancy sources, receipt and retained filters',
    (t) async {
      if (capture == null) {
        t.view.physicalSize = const Size(390, 844);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.reset);
      }
      Future<void> save(String name) => capture != null
          ? capture(name.replaceFirst('-390', '-ios'))
          : _save(t, name);
      t.platformDispatcher.accessibilityFeaturesTestValue =
          const FakeAccessibilityFeatures(disableAnimations: true);
      addTearDown(t.platformDispatcher.clearAccessibilityFeaturesTestValue);
      final previous = HttpOverrides.current;
      addTearDown(() => HttpOverrides.global = previous);
      final signedIn = await t.runAsync(() async {
        HttpOverrides.global = null;
        const output = String.fromEnvironment('QA_PROVIDER_ARTIFACTS');
        final sessionFile = File('$output/.owner-refresh');
        final store = output.isNotEmpty && await sessionFile.exists()
            ? _FixtureRefreshStore(sessionFile)
            : null;
        final (auth, transport) = await _login(
          '01000000010',
          'AcademyOwner',
          store: store,
        );
        expect(
          auth.session?.selected?.academyId,
          '10000000-0000-0000-0000-000000000001',
        );
        final before = await _financial(auth);
        await auth.request('GET', '/api/v1/academy/calendar');
        await auth.request('GET', '/api/v1/manage/structure/options');
        await expectLater(
          auth.request('GET', '$_root?monthToDate=true&from=2026-11-01'),
          throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 422)),
        );
        transport.failures.addAll([_root, '$_root/$_pending']);
        return (auth, transport, before);
      });
      if (signedIn == null) return;
      final (auth, transport, before) = signedIn;
      final appearance = _Appearance()..dark = true;
      await t.pumpWidget(
        RepaintBoundary(
          key: _capture,
          child: AcademyApp(
            auth: auth,
            environment: 'Demo',
            appearance: appearance,
          ),
        ),
      );
      await _wait(t, find.text('التقارير'));
      await _tap(t, find.text('التقارير').last);
      await _tap(t, find.text('التقارير المالية'));
      await _wait(t, find.text('اختلافات الدفع'));
      await save('financial-hub-390');
      await _tap(t, find.text('اختلافات الدفع'));
      await _tap(t, find.text('إعادة المحاولة'));
      await _wait(t, find.text('\u20661–10\u2069 من 13'));
      expect(transport.last(_root)['from'], '2026-11-01');
      expect(transport.last(_root)['to'], '2026-11-12');
      await save('list-month-390');
      await _date(t, 'من', DateTime(2026, 11, 11));
      await _wait(t, find.text('\u20661–3\u2069 من 3'));
      await _date(t, 'إلى', DateTime(2026, 11, 11));
      await _wait(t, find.text('\u20661–1\u2069 من 1'));
      expect(rows(transport.last(_root)['items']).single['id'], _midnight);
      await _tap(t, find.byTooltip('مسح الفترة'));
      await _wait(t, find.text('\u20661–10\u2069 من 14'));
      final first = rows(transport.last(_root)['items']);
      await _tap(t, find.byTooltip('الصفحة التالية'));
      await _wait(t, find.text('\u206611–14\u2069 من 14'));
      final second = rows(transport.last(_root)['items']);
      expect(
        {
          for (final row in [...first, ...second]) row['id'],
        }.length,
        14,
      );
      await _openIssue(t, textOf(second.first['id']));
      await _wait(t, find.text('فتح طلب الدفع'));
      await _back(t);
      await _wait(t, find.text('\u206611–14\u2069 من 14'));
      await save('list-page-two-390');
      await _tap(t, find.byTooltip('الصفحة السابقة'));
      await _wait(t, find.text('\u20661–10\u2069 من 14'));
      await _openIssue(t, _pending);
      await _tap(t, find.text('إعادة المحاولة'));
      await _wait(t, find.text('بانتظار تأكيد الدفع'));
      expect(
        t.widgetList<MoneyText>(find.byType(MoneyText)).map((w) => w.value),
        ['720.00', '721.00'],
      );
      expect(find.text('عرض إيصال التحصيل'), findsNothing);
      await save('pending-detail-390');
      await _tap(t, find.text('فتح طلب الدفع'));
      await _wait(t, find.text('بانتظار التحصيل'));
      final payment = t.widget<NativeRenewalPayment>(
        find.byType(NativeRenewalPayment),
      );
      expect(payment.readOnly, true);
      expect(
        payment.id,
        transport.last('$_root/$_pending')['paymentRequestId'],
      );
      expect(find.text('طريقة التحصيل'), findsNothing);
      expect(
        transport.last(
          '/api/v1/subscriptions/payments/${payment.id}',
        )['amount'],
        720,
      );
      await save('payment-source-390');
      await _back(t);
      await _wait(t, find.text('بانتظار تأكيد الدفع'));
      await _back(t);
      await _openIssue(t, _corrected);
      await _wait(t, find.text('تم تأكيد الطلب'));
      expect(
        t.widgetList<MoneyText>(find.byType(MoneyText)).map((w) => w.value),
        ['900.00', '900.25'],
      );
      final issue = transport.last('$_root/$_corrected');
      await _tap(t, find.text('تفاصيل الإشارة والمراجع'));
      await _wait(t, find.text('مرجع الطلب والإشارة'));
      expect(find.text('المرجع الوارد'), findsNothing);
      await save('references-expanded-390');
      await _tap(t, find.text('تفاصيل الإشارة والمراجع'));
      await t.pumpAndSettle();
      expect(find.text('مرجع الطلب والإشارة'), findsNothing);
      await save('confirmed-detail-390');
      await _tap(t, find.text('عرض إيصال التحصيل'));
      await _wait(t, find.text('المبلغ المدفوع'));
      expect(
        find.byWidgetPredicate((w) => w is UiText && w.data == '900 ج.م'),
        findsOneWidget,
      );
      final receipt = t.widget<ReceiptPage>(find.byType(ReceiptPage));
      expect(receipt.id, issue['receiptId']);
      final receiptData = transport.last(
        '/api/v1/subscriptions/receipts/${receipt.id}',
      );
      expect(receiptData['amount'], 900);
      expect(receiptData['paidAtAcademy'], startsWith('2026-11-12T08:12:'));
      expect(find.text('بتوقيت الأكاديمية'), findsOneWidget);
      final receiptLocale = MaterialLocalizations.of(
        t.element(find.byType(ReceiptPage)),
      );
      final paidDay = receiptLocale.formatShortDate(DateTime.utc(2026, 11, 12));
      final paidTime = receiptLocale.formatTimeOfDay(
        const TimeOfDay(hour: 8, minute: 12),
      );
      expect(find.textContaining('$paidDay · $paidTime'), findsOneWidget);
      await save('receipt-source-390');
      await _back(t);
      await _wait(t, find.text('تم تأكيد الطلب'));
      await _back(t);
      await _openIssue(t, _midnight);
      await _wait(t, find.textContaining('بتوقيت القاهرة'));
      expect(
        transport.last('$_root/$_midnight')['observedAtCairo'],
        '2026-11-11T00:00:00',
      );
      final clock = MaterialLocalizations.of(
        t.element(find.byType(MoneyText).first),
      );
      expect(
        find.textContaining(
          clock.formatTimeOfDay(const TimeOfDay(hour: 0, minute: 0)),
        ),
        findsOneWidget,
      );
      debugPrint('native device zone: ${DateTime.now().timeZoneOffset}');
      await save('cairo-midnight-390');
      await _back(t);
      if (capture == null) {
        await _openIssue(t, _beforeMidnight);
        await _wait(t, find.textContaining('بتوقيت القاهرة'));
        expect(
          transport.last('$_root/$_beforeMidnight')['observedAtCairo'],
          startsWith('2026-11-10T23:59:59'),
        );
        expect(
          find.textContaining(
            clock.formatTimeOfDay(const TimeOfDay(hour: 23, minute: 59)),
          ),
          findsOneWidget,
        );
        await save('cairo-before-midnight-390');
        await _back(t);
      }
      await _back(t);
      await _wait(t, find.text('التقارير المالية'));
      final after = await t.runAsync(() => _financial(auth));
      expect(after, before);
      expect(transport.writes, isEmpty);
      expect(t.takeException(), isNull);
      debugPrint(
        'owner native route, 503 retry, 14 records, exact receipt, Cairo clock and unchanged financial reports: $after',
      );
      await t.pumpWidget(const SizedBox.shrink());
      auth.dispose();
    },
    skip: !enabled,
  );

  test(
    'actual mobile tokens cannot read other roles or academy records',
    () async {
      final previous = HttpOverrides.current;
      HttpOverrides.global = null;
      addTearDown(() => HttpOverrides.global = previous);
      for (final (phone, role) in [
        ('01000000901', 'AcademyAdmin'),
        ('01000000020', 'Coach'),
        ('01000000001', 'Guardian'),
      ]) {
        final (auth, _) = await _login(phone, role);
        try {
          for (final path in [_root, '$_root/$_pending']) {
            await expectLater(
              auth.request('GET', path),
              throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 403)),
            );
          }
        } finally {
          auth.dispose();
        }
      }
      final (other, _) = await _login('01000000902', 'AcademyOwner');
      try {
        final response = obj(await other.request('GET', _root));
        expect(rows(response['items']).map((r) => r['id']), [_foreign]);
        for (final id in [_pending, _missing]) {
          await expectLater(
            other.request('GET', '$_root/$id'),
            throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 404)),
          );
        }
      } finally {
        other.dispose();
      }
    },
    skip: !enabled || capture != null,
  );
}
