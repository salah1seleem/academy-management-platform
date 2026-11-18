import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/admin/native_refund.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import '../test/auth_controller_test.dart' show MemoryStore;

const _academy = '10000000-0000-0000-0000-000000000001';
const _otherAcademy = '20000000-0000-0000-0000-000000000002';
const _player = '36000000-0000-0000-0000-000000000001';
const _root = '/api/v1/people/players/$_player/refunds';
const _financial = '/api/v1/reports/financial?allDates=true';
const _operating = '/api/v1/reports/operating-statement?allDates=true';
const _success = 'سُجل الاسترداد وظهر في السجل. لا تصرف المبلغ مرة أخرى.';
const _confirm = 'أؤكد أن التحويل تم خارج النظام وأوثق رده مرة واحدة.';

int _minor(Object? value) {
  final match = RegExp(r'^(-?\d+)(?:\.(\d{1,2}))?$').firstMatch('$value');
  if (match == null) throw StateError('Invalid money in test evidence: $value');
  return int.parse('${match[1]}${(match[2] ?? '').padRight(2, '0')}');
}

String _decimal(int minor) =>
    '${minor ~/ 100}.${(minor % 100).toString().padLeft(2, '0')}';

class _RefundTransport extends HttpApiTransport {
  _RefundTransport(this.io) : super(AppConfig('Demo', 'http://127.0.0.1:5397'));
  final Zone io;
  String? fault;
  bool afterWrite = false;
  final attempts = <Json>[];
  final quotes = <Json>[];
  final sourceReads = <Json>[];

  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) => io.run(() async {
    final isWrite = method == 'POST' && path == _root;
    if (isWrite) {
      attempts.add({
        'body': jsonDecode(jsonEncode(body)),
        'key': idempotencyKey,
      });
      if (fault == 'before-write') {
        fault = null;
        throw const ApiFailure(0, 'انقطع الاتصال قبل تأكيد الطلب.');
      }
    }
    if (method == 'GET' && path == _root && fault == 'load-fail') {
      fault = null;
      throw const ApiFailure(503, 'تعذر تحميل السجل.');
    }
    if (path == '$_root/quote' && fault == 'quote-fail') {
      fault = null;
      throw const ApiFailure(503, 'تعذر تحميل المراجعة.');
    }
    if (method == 'GET' &&
        path == _root &&
        afterWrite &&
        fault == 'read-fail') {
      fault = null;
      throw const ApiFailure(503, 'تعذر قراءة السجل.');
    }
    final response = await super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
    if (path == '$_root/quote') quotes.add(obj(response));
    if (method == 'GET' && path == _root) {
      sourceReads.add(obj(response));
      if (afterWrite && fault == 'mismatch-read') {
        fault = null;
        final altered = obj(jsonDecode(jsonEncode(response)));
        final records = rows(altered['refunds']);
        records.first['amount'] = _decimal(_minor(records.first['amount']) + 1);
        altered['refunds'] = records;
        return altered;
      }
    }
    if (isWrite) {
      afterWrite = true;
      attempts.last['result'] = response;
      if (fault == 'after-write') {
        fault = null;
        throw const ApiFailure(0, 'لم يصل تأكيد الطلب.');
      }
    }
    return response;
  });
}

Widget _app(Widget page, {bool dark = false, double scale = 1}) => MaterialApp(
  debugShowCheckedModeBanner: false,
  locale: const Locale('ar'),
  supportedLocales: const [Locale('ar')],
  localizationsDelegates: GlobalMaterialLocalizations.delegates,
  theme: dark ? AcademyTheme.dark() : AcademyTheme.light(),
  builder: (context, child) => MediaQuery(
    data: MediaQuery.of(
      context,
    ).copyWith(textScaler: TextScaler.linear(scale), disableAnimations: true),
    child: child!,
  ),
  home: page,
);

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'installed iOS player refund negative paths and exact money reconciliation',
    (t) async {
      expect(const bool.fromEnvironment('ACADEMY_ISOLATED_IOS_QA'), true);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5397',
      );
      final previous = HttpOverrides.current;
      addTearDown(() => HttpOverrides.global = previous);
      final done = <String>[];
      void passed(String id) {
        expect(done, isNot(contains(id)));
        done.add(id);
        debugPrint('IOS-REFUND $id executed successfully');
      }

      final setup = await t.runAsync(() async {
        HttpOverrides.global = null;
        final api = _RefundTransport(Zone.current);
        addTearDown(api.close);
        final direct = HttpApiTransport(
          AppConfig('Demo', 'http://127.0.0.1:5397'),
        );
        addTearDown(direct.close);
        Future<AuthController> login(
          HttpApiTransport transport,
          String phone,
          String role,
          String academy,
        ) async {
          final auth = AuthController(transport, MemoryStore())
            ..restoring = false;
          await auth.verify(phone, await auth.requestOtp(phone), '246810');
          final member = auth.session!.memberships.singleWhere(
            (m) => m.role == role && m.academyId == academy,
          );
          if (auth.session!.selected?.id != member.id) {
            await auth.selectRole(member.id);
          }
          expect(auth.session!.selected!.role, role);
          expect(auth.session!.selected!.academyId, academy);
          addTearDown(auth.dispose);
          return auth;
        }

        final owner = await login(api, '01000000010', 'AcademyOwner', _academy);
        final witness = await login(
          direct,
          '01000000010',
          'AcademyOwner',
          _academy,
        );
        final family = await login(direct, '01000000001', 'Guardian', _academy);
        final admin = await login(
          direct,
          '01000000011',
          'AcademyAdmin',
          _academy,
        );
        final coach = await login(direct, '01000000020', 'Coach', _academy);
        final foreign = await login(
          direct,
          '01088800003',
          'AcademyOwner',
          _otherAcademy,
        );
        final initial = obj(await witness.request('GET', _root));
        final collection = rows(initial['collections']).firstWhere(
          (c) => c['paymentMethod'] != 'Cash' && _minor(c['available']) > 10000,
        );
        expect(collection['receiptNumber'], isNotEmpty);
        final receipts = obj(
          await witness.request('GET', '/api/v1/subscriptions/collections'),
        );
        final receipt = rows(
          receipts['items'],
        ).singleWhere((r) => r['receiptNumber'] == collection['receiptNumber']);
        final receiptId = receipt['id'] ?? receipt['receiptId'];
        expect(receiptId, isA<String>());
        final savedReceipt = await witness.request(
          'GET',
          '/api/v1/subscriptions/receipts/$receiptId',
        );
        final periods = <String, Object?>{
          'all': await witness.request('GET', '/api/v1/subscriptions/periods'),
        };
        return (
          api,
          owner,
          witness,
          family,
          admin,
          coach,
          foreign,
          initial,
          collection,
          receiptId,
          savedReceipt,
          periods,
          obj(await witness.request('GET', _financial)),
          obj(await witness.request('GET', _operating)),
        );
      });
      if (setup == null) return;
      final (
        api,
        owner,
        witness,
        family,
        admin,
        coach,
        foreign,
        initial,
        collection,
        receiptId,
        savedReceipt,
        periods,
        financialBefore,
        operatingBefore,
      ) = setup;
      final label =
          '${collection['receiptNumber']} · ${day(collection['confirmedAtUtc'])} · المتاح ${money(collection['available'], collection['currency'])}';
      final originalAvailable = _minor(collection['available']);
      final savedIds = <String>[];

      Future<void> wait(Finder f) async {
        final deadline = Stopwatch()..start();
        while (f.evaluate().isEmpty && deadline.elapsed.inSeconds < 30) {
          await t.runAsync(
            () => Future<void>.delayed(const Duration(milliseconds: 25)),
          );
          await t.pump(const Duration(milliseconds: 40));
        }
        expect(f, findsWidgets);
        await t.pump(const Duration(milliseconds: 60));
      }

      Future<void> reach(Finder f) async {
        await wait(find.byType(Scrollable));
        final scroll = find.byType(Scrollable).last;
        final p = t.state<ScrollableState>(scroll).position;
        p.jumpTo(0);
        await t.pump();
        final deadline = Stopwatch()..start();
        while (f.evaluate().isEmpty && deadline.elapsed.inSeconds < 30) {
          if (p.pixels < p.maxScrollExtent) {
            await t.dragFrom(
              Offset(t.getRect(scroll).left + 8, t.getRect(scroll).center.dy),
              const Offset(0, -160),
            );
          }
          await t.runAsync(
            () => Future<void>.delayed(const Duration(milliseconds: 25)),
          );
          await t.pump(const Duration(milliseconds: 40));
        }
        expect(f, findsWidgets);
        await t.ensureVisible(f.last);
        await t.pumpAndSettle();
      }

      Future<void> tap(String text) async {
        final f = find.text(text);
        await reach(f);
        await t.tap(f.last);
        await t.pump();
      }

      Future<void> field(String name, String value) async {
        final f = find.widgetWithText(TextFormField, name);
        await reach(f);
        await t.enterText(f, value);
        await t.pumpAndSettle();
      }

      Future<void> capture(String phase) async {
        expect(t.takeException(), isNull);
        final bytes = await binding.takeScreenshot('phase-iosrefund-$phase');
        expect(bytes, isNotEmpty);
      }

      Future<void> open({
        bool dark = false,
        double scale = 1,
        bool choose = true,
      }) async {
        api.afterWrite = false;
        final loadingFails = api.fault == 'load-fail';
        await t.pumpWidget(const SizedBox());
        await t.pumpAndSettle();
        await t.pumpWidget(
          _app(
            NativePlayerRefund(auth: owner, playerId: _player),
            dark: dark,
            scale: scale,
          ),
        );
        await wait(
          loadingFails
              ? find.text('إعادة تحميل السجل')
              : find.widgetWithText(DropdownButtonFormField<String>, 'التحصيل'),
        );
        if (choose) {
          final selector = find.widgetWithText(
            DropdownButtonFormField<String>,
            'التحصيل',
          );
          await reach(selector);
          await t.tap(selector);
          await t.pumpAndSettle();
          final latest = api.sourceReads.last;
          final current = rows(latest['collections'])
              .singleWhere((c) => c['id'] == collection['id']);
          final choice =
              '${current['receiptNumber']} · ${day(current['confirmedAtUtc'])} · المتاح ${money(current['available'], current['currency'])}';
          await t.tap(find.text(choice).last);
          await t.pumpAndSettle();
        }
      }

      Future<void> review(
        String value, {
        String note = 'سبب اختبار استرداد',
      }) async {
        await field('قيمة الاسترداد', value);
        await field('سبب الاسترداد', note);
        await tap('مراجعة مبلغ الاسترداد');
        await reach(find.byType(CheckboxListTile));
        final f = find.widgetWithText(FilledButton, 'تأكيد تسجيل الاسترداد');
        await reach(f);
        expect(t.widget<FilledButton>(f).onPressed, isNull);
        await tap(_confirm);
        await t.pumpAndSettle();
      }

      Future<void> assertCount(int delta) async {
        final read = await t.runAsync(() => witness.request('GET', _root));
        expect(
          rows(obj(read)['refunds']).length,
          rows(initial['refunds']).length + delta,
        );
      }

      Future<void> recordId() async {
        final data = obj(
          (await t.runAsync(() => witness.request('GET', _root)))!,
        );
        final fresh = rows(data['refunds'])
            .where(
              (r) =>
                  !rows(initial['refunds']).any((o) => o['id'] == r['id']) &&
                  !savedIds.contains(r['id']),
            )
            .toList();
        expect(fresh, hasLength(1));
        savedIds.add(fresh.single['id'] as String);
      }

      // Enter through the real player workspace, then prove the return route.
      await t.pumpWidget(_app(NativePlayerWorkspace(auth: owner, id: _player)));
      await wait(find.text('الاشتراكات والماليات'));
      await tap('الاشتراكات والماليات');
      await tap('تسجيل استرداد');
      await wait(find.byType(NativePlayerRefund));
      await wait(
        find.widgetWithText(DropdownButtonFormField<String>, 'التحصيل'),
      );
      await capture('entry-light');
      passed('RFN01');
      await t.pageBack();
      await t.pumpAndSettle();
      expect(find.byType(NativePlayerWorkspace), findsOneWidget);
      await assertCount(0);
      passed('RFN16');

      api.fault = 'load-fail';
      await open(choose: false);
      await wait(find.text('إعادة تحميل السجل'));
      await capture('source-load-failed');
      await tap('إعادة تحميل السجل');
      await wait(
        find.widgetWithText(DropdownButtonFormField<String>, 'التحصيل'),
      );
      passed('RFN14');
      await open();
      final selector = find.widgetWithText(
        DropdownButtonFormField<String>,
        'التحصيل',
      );
      await reach(selector);
      await t.tap(selector);
      await t.pumpAndSettle();
      expect(find.text(label), findsWidgets);
      await capture('selector-light');
      await t.tap(find.text(label).last);
      await t.pumpAndSettle();
      for (final (id, value) in [
        ('RFN03', ''),
        ('RFN04', '0'),
        ('RFN05', '-1'),
        ('RFN06', '1.001'),
        ('RFN07', _decimal(originalAvailable + 100)),
      ]) {
        final quotes = api.quotes.length, posts = api.attempts.length;
        await field('قيمة الاسترداد', value);
        await tap('مراجعة مبلغ الاسترداد');
        await wait(
          find.text(
            'أدخل مبلغًا صحيحًا ضمن المتاح للاسترداد، بحد أقصى منزلتين عشريتين.',
          ),
        );
        expect(api.quotes.length, quotes);
        expect(api.attempts.length, posts);
        passed(id);
      }
      await capture('invalid-light');
      api.fault = 'quote-fail';
      await field('قيمة الاسترداد', '25.75');
      await tap('مراجعة مبلغ الاسترداد');
      await wait(find.text('تعذر تحميل المراجعة.'));
      expect(find.byType(CheckboxListTile), findsNothing);
      await tap('مراجعة مبلغ الاسترداد');
      await reach(find.byType(CheckboxListTile));
      passed('RFN15');
      await tap(_confirm);
      await field('قيمة الاسترداد', '26');
      expect(find.byType(CheckboxListTile), findsNothing);
      passed('RFN12');
      await review('25.75');
      await field('سبب الاسترداد', 'سبب جديد');
      expect(find.byType(CheckboxListTile), findsNothing);
      passed('RFN13');

      await open(dark: true);
      await review('٢٥٫٧٥', note: '  استرداد اصطناعي أول  ');
      expect(api.quotes.last['amount'], '25.75');
      passed('RFN08');
      await reach(find.text('تأكيد تسجيل الاسترداد'));
      await capture('review-dark');
      await tap('تأكيد تسجيل الاسترداد');
      await wait(find.text(_success));
      await capture('saved-dark');
      await recordId();
      expect(
        api.attempts.last['body'],
        containsPair('reason', 'استرداد اصطناعي أول'),
      );
      passed('RFN09');
      passed('RFN17');
      await open();
      expect(find.textContaining('استرداد اصطناعي أول'), findsWidgets);
      await capture('history-reopened');
      await assertCount(1);
      passed('RFN30');

      for (final (id, mode, value, photo) in [
        ('RFN18', 'after-write', '3.25', 'pending-lost'),
        ('RFN19', 'before-write', '4.50', 'pending-before'),
        ('RFN20', 'read-fail', '1.00', 'pending-read'),
        ('RFN21', 'mismatch-read', '1.25', 'pending-mismatch'),
      ]) {
        await open();
        await review(value, note: 'اختبار $id');
        api.fault = mode;
        final start = api.attempts.length;
        await tap('تأكيد تسجيل الاسترداد');
        final retry = mode == 'read-fail' || mode == 'mismatch-read'
            ? 'التحقق من تسجيل الاسترداد'
            : 'إعادة تأكيد الاسترداد نفسه';
        await wait(find.text(retry));
        expect(find.text(_success), findsNothing);
        expect(
          t
              .widget<DropdownButtonFormField<String>>(
                find.widgetWithText(DropdownButtonFormField<String>, 'التحصيل'),
              )
              .onChanged,
          isNull,
        );
        await reach(find.text(retry));
        await capture(photo);
        if (mode == 'before-write') await assertCount(savedIds.length);
        await tap(retry);
        await wait(find.text(_success));
        await recordId();
        expect(
          api.attempts.length - start,
          mode == 'read-fail' || mode == 'mismatch-read' ? 1 : 2,
        );
        if (mode == 'after-write' || mode == 'before-write') {
          expect(api.attempts[start]['body'], api.attempts[start + 1]['body']);
        }
        if (mode == 'after-write' || mode == 'before-write') {
          expect(api.attempts[start]['key'], api.attempts[start + 1]['key']);
        }
        passed(id);
      }
      final last = api.attempts.last;
      final replay = await t.runAsync(
        () => witness.request(
          'POST',
          _root,
          body: last['body'],
          idempotencyKey: last['key'] as String,
        ),
      );
      expect(obj(replay)['id'], obj(last['result'])['id']);
      Future<void> rejected(
        AuthController actor,
        String method,
        String path, {
        Object? body,
        String key = 'ios-refund-denied',
        List<int> statuses = const [403, 404],
      }) async {
        final status = await t.runAsync(() async {
          try {
            await actor.request(method, path, body: body, idempotencyKey: key);
            return 200;
          } on ApiFailure catch (e) {
            return e.status;
          }
        });
        expect(status, isIn(statuses));
      }

      await rejected(
        witness,
        'POST',
        _root,
        body: {...obj(last['body']), 'amount': '1.26'},
        key: last['key'] as String,
        statuses: [409],
      );
      await assertCount(5);
      passed('RFN22');

      await open();
      final current = rows(api.sourceReads.last['collections'])
          .singleWhere((c) => c['id'] == collection['id']);
      await review('${current['available']}');
      final concurrent = await t.runAsync(
        () => witness.request(
          'POST',
          _root,
          body: {
            'collectionId': collection['id'],
            'amount': '0.50',
            'reason': 'تغير متاح مصرح أثناء المراجعة',
          },
          idempotencyKey: 'ios-refund-concurrent',
        ),
      );
      savedIds.add(obj(concurrent)['id'] as String);
      await tap('تأكيد تسجيل الاسترداد');
      await wait(find.textContaining('الاسترداد يتجاوز المبلغ'));
      expect(find.text(_success), findsNothing);
      await assertCount(6);
      passed('RFN23');

      for (final actor in [family, coach, admin, foreign]) {
        await rejected(actor, 'GET', _root);
        await rejected(
          actor,
          'POST',
          _root,
          body: {
            'collectionId': collection['id'],
            'amount': '0.01',
            'reason': 'رفض دور',
          },
          key: 'ios-refund-role-denial',
        );
      }
      await rejected(
        witness,
        'POST',
        _root,
        body: {
          'collectionId': collection['id'],
          'amount': _decimal(originalAvailable + 100),
        },
        statuses: [409],
      );
      await rejected(
        witness,
        'POST',
        '$_root/quote',
        body: {
          'collectionId': collection['id'],
          'amount': '1.00',
          'reason': 'أ' * 501,
        },
        statuses: [422],
      );
      passed('RFN26');

      for (final dark in [false, true]) {
        await open(dark: dark, scale: 2);
        await field('قيمة الاسترداد', '1.00');
        await tap('مراجعة مبلغ الاسترداد');
        await reach(find.byType(CheckboxListTile));
        await reach(find.text(_confirm));
        await capture('large-text-${dark ? 'dark' : 'light'}');
      }
      passed('RFN29');
      await open();
      await review('1.00');
      final membership = owner.session!.selected!.id;
      final other = owner.session!.memberships
          .singleWhere((m) => m.academyId == _otherAcademy)
          .id;
      await t.runAsync(() => owner.selectRole(other));
      await t.pumpAndSettle();
      expect(find.byType(TextFormField), findsNothing);
      expect(find.byType(CheckboxListTile), findsNothing);
      await capture('context-cleared');
      await rejected(owner, 'GET', _root);
      passed('RFN25');
      await t.runAsync(() => owner.selectRole(membership));

      final finalState = await t.runAsync(() async {
        final finalRead = obj(await witness.request('GET', _root));
        final fresh = rows(finalRead['refunds'])
            .where((r) => savedIds.contains(r['id']))
            .toList();
        expect(fresh, hasLength(6));
        expect(fresh.fold<int>(0, (sum, r) => sum + _minor(r['amount'])), 3625);
        expect(
          _minor(
            rows(finalRead['collections'])
                .singleWhere((c) => c['id'] == collection['id'])['available'],
          ),
          originalAvailable - 3625,
        );
        expect(
          await witness.request(
            'GET',
            '/api/v1/subscriptions/receipts/$receiptId',
          ),
          savedReceipt,
        );
        expect(
          await witness.request('GET', '/api/v1/subscriptions/periods'),
          periods['all'],
        );
        final financial = obj(await witness.request('GET', _financial));
        expect(
          _minor(financial['totalAmount']),
          _minor(financialBefore['totalAmount']),
        );
        expect(
          _minor(financial['totalRefunds']),
          _minor(financialBefore['totalRefunds']) + 3625,
        );
        expect(
          _minor(financial['netAmount']),
          _minor(financialBefore['netAmount']) - 3625,
        );
        final operating = obj(await witness.request('GET', _operating));
        expect(
          _minor(operating['surplus']),
          _minor(operatingBefore['surplus']) - 3625,
        );
        expect(
          _minor(operating['refunds']),
          _minor(operatingBefore['refunds']) + 3625,
        );
        for (final key in [
          'totalExpenses',
          'receivables',
          'approvedPayrollPayable',
          'unpaidOperatingExpenses',
        ]) {
          expect(operating[key], operatingBefore[key]);
        }
        return {
          'source': collection,
          'refundIds': savedIds,
          'totalRefundMinor': 3625,
          'financialBefore': financialBefore,
          'financialAfter': financial,
          'operatingBefore': operatingBefore,
          'operatingAfter': operating,
          'receiptId': receiptId,
          'receiptUnchanged': true,
          'periodsUnchanged': true,
          'attempts': api.attempts,
        };
      });
      expect(finalState, isNotNull);
      passed('RFN27');
      binding.reportData = {
        ...?binding.reportData,
        'refundReview': {
          'executedPassed': done,
          'reconciliation': finalState,
          'source': 'installed-ios-real-isolated-api',
        },
      };
      expect((binding.reportData!['screenshots'] as List).length, 14);
      await t.pumpWidget(const SizedBox());
    },
  );
}
