import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:academy_mobile/features/owner/refined_home_panels.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import '../test/auth_controller_test.dart' show MemoryStore;

const _a = '10000000-0000-0000-0000-000000000001';
const _b = '20000000-0000-0000-0000-000000000002';
const _ledger = '/api/v1/finance/receivables';
const _expense = '/api/v1/finance/operating-expenses';
const _statement = '/api/v1/reports/operating-statement';

int _minor(Object? value) {
  final match = RegExp(r'^(-?\d+)(?:\.(\d{1,2}))?$').firstMatch('$value');
  if (match == null) throw StateError('Non-monetary QA evidence: $value');
  return int.parse('${match[1]}${(match[2] ?? '').padRight(2, '0')}');
}

class _OwnerTransport extends HttpApiTransport {
  _OwnerTransport(this.io) : super(AppConfig('Demo', 'http://127.0.0.1:5397'));
  final Zone io;
  String? fault;
  final requests = <Json>[];
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) => io.run(() async {
    if (method == 'GET') requests.add({'method': method, 'path': path});
    final uri = Uri.parse(path);
    final name = uri.path.endsWith('/owner-cockpit')
        ? 'cockpit'
        : uri.path == _statement
        ? 'statement'
        : '';
    if (fault == '$name-503' && name.isNotEmpty) {
      fault = null;
      throw const ApiFailure(503, 'تعذر تحميل البيانات.');
    }
    final value = await super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
    if (fault == 'statement-contract' && name == 'statement') {
      fault = null;
      return Map<String, dynamic>.from(obj(value))..remove('surplus');
    }
    return value;
  });
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'installed owner financial sources, negative paths and reconciliation',
    (t) async {
      expect(const bool.fromEnvironment('ACADEMY_ISOLATED_IOS_QA'), true);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5397',
      );
      final fixture = obj(
        jsonDecode(const String.fromEnvironment('OWNER_FINANCE_FIXTURE')),
      );
      expect(fixture['academyId'], _a);
      expect(fixture['otherAcademyId'], _b);
      final previous = HttpOverrides.current;
      addTearDown(() => HttpOverrides.global = previous);
      binding.testTextInput.register();
      addTearDown(binding.testTextInput.unregister);
      final done = <String>[];
      final evidence = <Json>[];
      final writes = <Json>[];
      void passed(String id, Json detail) {
        expect(done, isNot(contains(id)));
        done.add(id);
        evidence.add({'case': id, ...detail});
        debugPrint('IOS-OWNER $id executed successfully');
      }

      final setup = await t.runAsync(() async {
        HttpOverrides.global = null;
        final api = _OwnerTransport(Zone.current);
        final direct = HttpApiTransport(
          AppConfig('Demo', 'http://127.0.0.1:5397'),
        );
        addTearDown(api.close);
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
          final membership = auth.session!.memberships.singleWhere(
            (m) => m.role == role && m.academyId == academy,
          );
          if (auth.session!.selected?.id != membership.id) {
            await auth.selectRole(membership.id);
          }
          expect(auth.session!.selected?.id, membership.id);
          addTearDown(auth.dispose);
          return auth;
        }

        final owner = await login(api, '01000000010', 'AcademyOwner', _a);
        final witness = await login(direct, '01000000010', 'AcademyOwner', _a);
        final foreign = await login(direct, '01088800003', 'AcademyOwner', _b);
        final family = await login(direct, '01000000001', 'Guardian', _a);
        final coach = await login(direct, '01000000020', 'Coach', _a);
        final admin = await login(direct, '01000000011', 'AcademyAdmin', _a);
        final appearance = Appearance()..bindIntroUser(owner.session!.userId);
        await appearance.completeRoleIntro('AcademyOwner', 'football');
        final initial = obj(
          await witness.request('GET', '$_statement?allDates=true'),
        );
        return (
          api: api,
          owner: owner,
          witness: witness,
          foreign: foreign,
          family: family,
          coach: coach,
          admin: admin,
          appearance: appearance,
          initial: initial,
        );
      });
      final s = setup!;
      Future<Json> get(AuthController auth, String path) async =>
          (await t.runAsync(() async => obj(await auth.request('GET', path))))!;
      Future<Json> post(String path, Json body, String key) async {
        final result = (await t.runAsync(
          () async => obj(
            await s.witness.request(
              'POST',
              path,
              body: body,
              idempotencyKey: key,
            ),
          ),
        ))!;
        writes.add({'path': path, 'body': body, 'key': key, 'result': result});
        return result;
      }

      Future<void> deny(
        AuthController auth,
        String method,
        String path,
        Json? body,
        String key,
        Set<int> statuses,
      ) async {
        final response = await t.runAsync(() async {
          final client = HttpClient();
          try {
            final request = await client.openUrl(
              method,
              Uri.parse('http://127.0.0.1:5397$path'),
            );
            request.followRedirects = false;
            request.headers.set(
              HttpHeaders.authorizationHeader,
              'Bearer ${auth.session!.accessToken}',
            );
            request.headers.set('Idempotency-Key', key);
            if (body != null) {
              request.headers.contentType = ContentType.json;
              request.write(jsonEncode(body));
            }
            final result = await request.close();
            return {
              'status': result.statusCode,
              'body': await utf8.decoder.bind(result).join(),
            };
          } finally {
            client.close(force: true);
          }
        });
        expect(response, isNotNull);
        expect(statuses, contains(response!['status']));
        expect(response['body'], isNot(contains('صيانة ديمو مثبتة')));
        evidence.add({'deniedMethod': method, 'path': path, ...response});
      }

      Future<void> pump() async {
        await t.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 35)),
        );
        await t.pump(const Duration(milliseconds: 60));
      }

      Future<void> wait(Finder finder) async {
        final timer = Stopwatch()..start();
        while (finder.evaluate().isEmpty && timer.elapsed.inSeconds < 35) {
          await pump();
        }
        expect(finder, findsWidgets);
      }

      Finder outer() => find
          .descendant(
            of: find
                .byWidgetPredicate(
                  (w) => w is ListView && w.scrollDirection == Axis.vertical,
                )
                .first,
            matching: find.byType(Scrollable),
          )
          .first;
      Future<void> reach(Finder finder) async {
        final scroll = outer();
        await wait(scroll);
        final position = t.state<ScrollableState>(scroll).position;
        for (
          var attempt = 0;
          attempt < 45 && finder.evaluate().isEmpty;
          attempt++
        ) {
          final forward = position.pixels < position.maxScrollExtent - 1;
          await t.dragFrom(
            Offset(t.getRect(scroll).left + 8, t.getRect(scroll).center.dy),
            Offset(0, forward ? -220 : 650),
          );
          await pump();
        }
        await wait(finder);
        await t.ensureVisible(finder.first);
        await t.pumpAndSettle();
      }

      Future<void> tap(Finder finder) async {
        await reach(finder);
        expect(finder.hitTestable(), findsWidgets);
        await t.tap(finder.hitTestable().first);
        await pump();
      }

      Future<void> branch(String label) async {
        final rail = find.byKey(const ValueKey('owner-branch-rail'));
        await reach(rail);
        final scroll = find
            .descendant(of: rail, matching: find.byType(Scrollable))
            .first;
        await t.scrollUntilVisible(
          find.text(label),
          160,
          scrollable: scroll,
          maxScrolls: 20,
        );
        await t.pumpAndSettle();
        expect(find.text(label).hitTestable(), findsOneWidget);
        await t.tap(find.text(label).hitTestable());
        await pump();
      }

      Future<void> refresh() async {
        await wait(find.byType(OwnerDashboard));
        final scroll = outer();
        final position = t.state<ScrollableState>(scroll).position;
        for (var i = 0; position.pixels > 1 && i < 24; i++) {
          await t.drag(scroll, const Offset(0, 800));
          await t.pumpAndSettle();
        }
        expect(position.pixels, lessThanOrEqualTo(1));
        final before = s.api.requests.length;
        await t.drag(scroll, const Offset(0, 500));
        await pump();
        for (var i = 0; i < 80; i++) {
          await pump();
          if (s.api.requests.length > before &&
              find.byType(CircularProgressIndicator).evaluate().isEmpty) {
            break;
          }
        }
        expect(s.api.requests.length, greaterThan(before));
      }

      Future<void> capture(String phase, Finder finder) async {
        await reach(finder);
        await t.pumpAndSettle();
        expect(t.takeException(), isNull);
        await binding.takeScreenshot('phase-iosowner-$phase');
      }

      Future<void> overdue(int amount, int players) async {
        final panel = find.byType(AcademySummaryPanel);
        await reach(panel);
        final timer = Stopwatch()..start();
        while (timer.elapsed.inSeconds < 30) {
          final data = obj(
            t.widget<AcademySummaryPanel>(panel).data['overdue'],
          );
          if (_minor(data['amount']) == amount * 100 &&
              data['players'] == players) {
            break;
          }
          await pump();
        }
        final data = obj(t.widget<AcademySummaryPanel>(panel).data['overdue']);
        expect(_minor(data['amount']), amount * 100);
        expect(data['players'], players);
        expect(data['asOf'], fixture['today']);
        await reach(
          find.text(
            amount == 0 && players == 0
                ? 'لا توجد مستحقات متأخرة'
                : 'لاعبون عليهم متأخرات',
          ),
        );
        if (amount > 0) {
          expect(
            find.descendant(
              of: panel,
              matching: find.byWidgetPredicate(
                (w) => w is MoneyText && _minor(w.value) == amount * 100,
              ),
            ),
            findsWidgets,
          );
        }
      }

      Future<void> statement(int income, int expenses) async {
        final panel = find.byType(HomeOperatingStatement);
        await reach(panel);
        final timer = Stopwatch()..start();
        while (timer.elapsed.inSeconds < 30) {
          final data = t.widget<HomeOperatingStatement>(panel).data;
          if (_minor(data['totalIncome']) == income &&
              _minor(data['totalExpenses']) == expenses) {
            break;
          }
          await pump();
        }
        final data = t.widget<HomeOperatingStatement>(panel).data;
        expect(_minor(data['totalIncome']), income);
        expect(_minor(data['totalExpenses']), expenses);
        expect(_minor(data['surplus']), income - expenses);
        await reach(
          find.text(income < expenses ? 'العجز التشغيلي' : 'الفائض التشغيلي'),
        );
      }

      final today = DateTime.parse(fixture['today'] as String);
      String day(int offset) =>
          today.add(Duration(days: offset)).toIso8601String().substring(0, 10);
      Json chargeBody(
        Json enrollment,
        String amount,
        int offset,
        String label,
      ) => {
        'sportEnrollmentId': enrollment['Id'],
        'planId': null,
        'amount': amount,
        'dueDate': day(offset),
        'source': 'ManualCharge',
        'description': label,
      };
      final p1 = obj(fixture['p1']), p2 = obj(fixture['p2']);
      final charges = <Json>[];
      await t.pumpWidget(
        AcademyApp(
          auth: s.owner,
          environment: 'Demo',
          appearance: s.appearance,
        ),
      );
      await wait(find.byType(OwnerDashboard));
      await overdue(0, 0);
      await capture('zero-confirmed', find.text('لا توجد مستحقات متأخرة'));
      passed('OHF01', {
        'layer': 'installed-ios-and-api',
        'overdue': 0,
        'players': 0,
      });
      for (final item in [(p1, '540.00'), (p1, '260.00'), (p2, '125.00')]) {
        charges.add(
          await post(
            '$_ledger/',
            chargeBody(item.$1, item.$2, -1, 'مطالبة اختبار المتأخرات'),
            'ohf-charge-${charges.length + 1}',
          ),
        );
      }
      await refresh();
      await overdue(925, 2);
      await capture(
        'overdue-925-two-players',
        find.text('لاعبون عليهم متأخرات'),
      );
      passed('OHF02', {
        'layer': 'installed-ios-and-api',
        'documents': charges.map((c) => c['id']).toList(),
        'overdue': 925,
        'players': 2,
      });
      await branch('فرع مدينة نصر');
      await overdue(800, 1);
      passed('OHF03', {
        'layer': 'installed-ios-and-api',
        'overdue': 800,
        'players': 1,
      });
      await tap(find.text('إلغاء التصفية'));
      await overdue(925, 2);
      await post(
        '$_ledger/',
        chargeBody(p1, '40.00', 0, 'مطالبة تستحق اليوم'),
        'ohf-charge-today',
      );
      await refresh();
      await overdue(925, 2);
      passed('OHF04', {
        'layer': 'installed-ios-and-api',
        'todayCharge': 40,
        'overdue': 925,
      });
      await post(
        '$_ledger/',
        chargeBody(p1, '500.00', 10, 'مطالبة مستقبلية'),
        'ohf-charge-future',
      );
      await refresh();
      await overdue(925, 2);
      passed('OHF05', {
        'layer': 'installed-ios-and-api',
        'futureCharge': 500,
        'overdue': 925,
      });
      await branch('فرع الزقازيق');
      await overdue(125, 1);
      passed('OHF06', {
        'layer': 'installed-ios-and-api',
        'overdue': 125,
        'players': 1,
      });
      await branch('فرع التجمع الخامس');
      await overdue(0, 0);
      passed('OHF07', {
        'layer': 'installed-ios-and-api',
        'overdue': 0,
        'players': 0,
      });
      await tap(find.text('إلغاء التصفية'));
      await overdue(925, 2);
      s.api.fault = 'cockpit-503';
      await refresh();
      await capture(
        'summary-unavailable',
        find.text('إعادة تحميل ملخص الأكاديمية'),
      );
      expect(find.text('لاعبون عليهم متأخرات'), findsNothing);
      await tap(find.text('إعادة تحميل ملخص الأكاديمية'));
      await overdue(925, 2);
      passed('OHF09', {
        'layer': 'installed-ios-controlled-read-fault',
        'recovered': true,
      });

      final doc = charges.first['id'];
      final collections = await t.runAsync(
        () => s.witness.request('GET', '$_ledger/$doc/collections'),
      );
      final available = rows(collections)
          .firstWhere((c) => _minor(c['available']) >= 4000);
      final allocation = <String, dynamic>{
        'kind': 'PaymentAllocation',
        'amount': '40.00',
        'collectionId': available['id'],
        'reason': 'تخصيص من تحصيل موجود دون قبض جديد',
      };
      final allocationResult = await post(
        '$_ledger/$doc/movements',
        allocation,
        'ohf-allocation',
      );
      await refresh();
      await overdue(885, 2);
      passed('OHF20', {
        'layer': 'installed-ios-and-api',
        'overdue': 885,
        'allocationId': allocationResult['id'],
      });
      final replay = await post(
        '$_ledger/$doc/movements',
        allocation,
        'ohf-allocation',
      );
      expect(replay['id'], allocationResult['id']);
      await deny(
        s.witness,
        'POST',
        '$_ledger/$doc/movements',
        {...allocation, 'amount': '41.00'},
        'ohf-allocation',
        {409},
      );
      passed('OHF21', {
        'layer': 'api',
        'replayId': replay['id'],
        'modifiedReplayRejected': true,
      });
      for (final index in [0, 1, 2]) {
        await post('$_ledger/${charges[index]['id']}/movements', {
          'kind': 'CreditNote',
          'amount': ['500.00', '260.00', '125.00'][index],
          'collectionId': null,
          'reason': 'إشعار دائن لاختبار مصدر المتأخرات',
        }, 'ohf-credit-$index');
        final values = [(385, 2), (125, 1), (0, 0)][index];
        await refresh();
        await overdue(values.$1, values.$2);
        passed('OHF${23 + index}', {
          'layer': 'installed-ios-and-api',
          'overdue': values.$1,
          'players': values.$2,
        });
      }

      final expenseBody = <String, dynamic>{
        'branchId': fixture['branchCity'],
        'scope': 'Branch',
        'targetId': fixture['branchCity'],
        'category': 'Maintenance',
        'periodFrom': fixture['serviceDay'],
        'periodTo': fixture['serviceDay'],
        'paidOn': fixture['today'],
        'amount': '137.65',
        'currency': 'EGP',
        'note': 'صيانة ديمو مثبتة',
      };
      final expense = await post('$_expense/', expenseBody, 'ohf-paid-expense');
      final path = '$_expense/${expense['id']}';
      final baseExpense = _minor(s.initial['totalExpenses']);
      expect(
        _minor(
          (await get(s.witness, '$_statement?allDates=true'))['totalExpenses'],
        ),
        baseExpense,
      );
      passed('OHF31', {
        'layer': 'api',
        'expenseId': expense['id'],
        'cashUnchanged': true,
      });
      await post('$path/approve', {}, 'ohf-paid-approve');
      expect(
        _minor(
          (await get(s.witness, '$_statement?allDates=true'))['totalExpenses'],
        ),
        baseExpense,
      );
      passed('OHF32', {'layer': 'api', 'approvedCashUnchanged': true});
      await deny(
        s.witness,
        'POST',
        '$path/post',
        {'quoteHash': '0' * 64},
        'ohf-bad-quote',
        {409},
      );
      final quote = await post('$path/quote', {}, 'ohf-paid-quote');
      final posted = await post('$path/post', {
        'quoteHash': quote['quoteHash'],
      }, 'ohf-paid-post');
      final after = await get(s.witness, '$_statement?allDates=true');
      expect(_minor(after['totalIncome']), _minor(s.initial['totalIncome']));
      expect(_minor(after['totalExpenses']), baseExpense + 13765);
      expect(_minor(after['surplus']), _minor(s.initial['surplus']) - 13765);
      passed('OHF34', {
        'layer': 'api',
        'expenseId': expense['id'],
        'before': s.initial,
        'after': after,
      });
      expect(
        (await post('$path/post', {
          'quoteHash': quote['quoteHash'],
        }, 'ohf-paid-post'))['id'],
        posted['id'],
      );
      await deny(
        s.witness,
        'POST',
        '$path/post',
        {'quoteHash': quote['quoteHash']},
        'ohf-paid-post-new',
        {409},
      );
      passed('OHF35', {'layer': 'api', 'replayWithoutDuplicate': true});
      final unpaid = await post('$_expense/', {
        ...expenseBody,
        'paidOn': null,
        'amount': '200.00',
      }, 'ohf-unpaid-expense');
      final unpaidPath = '$_expense/${unpaid['id']}';
      await post('$unpaidPath/approve', {}, 'ohf-unpaid-approve');
      final unpaidQuote = await post(
        '$unpaidPath/quote',
        {},
        'ohf-unpaid-quote',
      );
      await post('$unpaidPath/post', {
        'quoteHash': unpaidQuote['quoteHash'],
      }, 'ohf-unpaid-post');
      final withUnpaid = await get(s.witness, '$_statement?allDates=true');
      expect(
        _minor(withUnpaid['totalExpenses']),
        _minor(after['totalExpenses']),
      );
      expect(
        _minor(withUnpaid['unpaidOperatingExpenses']),
        _minor(s.initial['unpaidOperatingExpenses']) + 20000,
      );
      passed('OHF36', {
        'layer': 'api',
        'unpaidExpenseId': unpaid['id'],
        'cashUnchanged': true,
      });
      await refresh();
      final period = find.byKey(const ValueKey('owner-period-statement'));
      await reach(period);
      await tap(find.descendant(of: period, matching: find.text('هذا اليوم')));
      final todayStatement = await get(
        s.witness,
        '$_statement?from=${fixture['today']}&to=${fixture['today']}',
      );
      expect(_minor(todayStatement['totalIncome']), 0);
      expect(_minor(todayStatement['totalExpenses']), 13765);
      await statement(0, 13765);
      await capture('paid-expense-deficit', find.text('العجز التشغيلي'));
      passed('OHF40', {
        'layer': 'installed-ios-and-api',
        'income': 0,
        'paidExpense': '137.65',
        'deficit': '-137.65',
      });
      s.api.fault = 'statement-503';
      await refresh();
      await capture(
        'statement-unavailable',
        find.text('إعادة تحميل الموقف التشغيلي'),
      );
      await tap(find.text('إعادة تحميل الموقف التشغيلي'));
      await statement(0, 13765);
      passed('OHF43', {
        'layer': 'installed-ios-controlled-read-fault',
        'recovered': true,
      });
      s.api.fault = 'statement-contract';
      await refresh();
      await capture(
        'statement-contract-unavailable',
        find.text('تعذر التحقق من أرقام التقرير.'),
      );
      await tap(find.text('إعادة تحميل التقرير'));
      await statement(0, 13765);
      evidence.add({
        'case': 'OHF44',
        'layer': 'installed-ios-controlled-read-fault',
        'executedVariant': 'missing surplus then real API retry',
        'otherVariants': 'Widget only',
      });
      final bCockpit = await get(s.foreign, '/api/v1/reports/owner-cockpit');
      final bStatement = await get(s.foreign, '$_statement?allDates=true');
      expect(_minor(obj(bCockpit['overdue'])['amount']), 8500);
      expect(_minor(bStatement['totalExpenses']), 1999);
      evidence.add({
        'case': 'OHF29',
        'layer': 'api-only-partial',
        'controlOverdue': '85.00',
        'controlExpense': '19.99',
        'nativeContextSwitch': 'not executed',
      });
      for (final auth in [s.family, s.coach, s.admin]) {
        await deny(auth, 'GET', _statement, null, 'ohf-read-denied', {403});
        await deny(
          auth,
          'POST',
          '$_expense/',
          expenseBody,
          'ohf-write-denied',
          {403},
        );
      }
      await deny(s.foreign, 'GET', path, null, 'ohf-cross-read', {404});
      await deny(s.foreign, 'POST', '$path/approve', {}, 'ohf-cross-approve', {
        404,
      });
      evidence.add({
        'case': 'OHF30',
        'layer': 'raw-api-partial',
        'checks': 8,
        'remaining': 'full read/write endpoint matrix',
      });
      binding.reportData = {
        ...?binding.reportData,
        'ownerFinanceReview': {
          'executedPassedWithinDeclaredLayer': done,
          'evidence': evidence,
          'writes': writes,
          'requests': s.api.requests,
          'screensRemainOpen': true,
          'fullCasesWritten': 48,
        },
      };
      expect(t.takeException(), isNull);
    },
    timeout: const Timeout(Duration(minutes: 9)),
  );
}
