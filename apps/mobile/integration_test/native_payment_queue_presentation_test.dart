import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/native_renewal.dart';
import 'package:academy_mobile/features/admin/native_renewal_proof_review.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/decision_center.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import '../test/auth_controller_test.dart' show MemoryStore;
import '../test/support/visibility.dart';

const _queue = '/api/v1/manage/payment-requests?allDates=true&status=Pending';
const _finance = '/api/v1/reports/financial?allDates=true';

class _OwnerWebSession {
  final _client = HttpClient();
  final _cookies = <String, Cookie>{};

  Future<Object?> request(String method, String path, {Object? body}) async {
    final csrf = body == null
        ? null
        : obj(await request('GET', '/api/v1/auth/csrf'));
    final outgoing = await _client.openUrl(
      method,
      Uri.parse('http://127.0.0.1:5499$path'),
    );
    outgoing.cookies.addAll(_cookies.values);
    if (csrf != null) {
      outgoing.headers.contentType = ContentType.json;
      outgoing.headers.set(
        csrf['headerName'] as String,
        csrf['token'] as String,
      );
      outgoing.write(jsonEncode(body));
    }
    final response = await outgoing.close();
    for (final cookie in response.cookies) {
      _cookies[cookie.name] = cookie;
    }
    final content = await utf8.decoder.bind(response).join();
    expect(
      response.statusCode,
      inInclusiveRange(200, 299),
      reason: '$method $path',
    );
    return content.isEmpty ? null : jsonDecode(content);
  }

  void close() => _client.close(force: true);
}

class _ReturningRole extends Appearance {
  @override
  Future<bool> hasRoleIntro(String role, String sportTheme) async => true;
}

class _ReadQueueTransport extends HttpApiTransport {
  _ReadQueueTransport(this.io)
    : super(AppConfig('Demo', 'http://127.0.0.1:5499'));

  final Zone io;
  final reads = <String>[];
  bool failQueueOnce = false;

  Future<T> _network<T>(Future<T> Function() request) {
    final result = Completer<T>();
    io.run(() async {
      try {
        result.complete(await request());
      } catch (error, stack) {
        result.completeError(error, stack);
      }
    });
    return result.future;
  }

  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    if (method == 'GET') {
      reads.add(path);
      if (path == _queue && failQueueOnce) {
        failQueueOnce = false;
        return Future.error(const ApiFailure(503, 'تعذر تحميل طلبات الدفع.'));
      }
    } else if (!path.startsWith('/api/v1/mobile/auth/')) {
      fail('The payment queue test is read-only.');
    }
    return _network(
      () => super.send(
        method,
        path,
        body: body,
        token: token,
        idempotencyKey: idempotencyKey,
      ),
    );
  }

  @override
  Future<Uint8List> asset(String path, String token) =>
      _network(() => super.asset(path, token));
}

Future<void> _waitFor(WidgetTester tester, Finder finder) async {
  for (var attempt = 0; attempt < 180 && finder.evaluate().isEmpty; attempt++) {
    await tester.runAsync(
      () => Future<void>.delayed(const Duration(milliseconds: 30)),
    );
    await tester.pump(const Duration(milliseconds: 45));
  }
  expect(finder, findsWidgets);
}

Finder _card(Json row) =>
    find.byKey(ValueKey('payment-request:${row['type']}:${row['id']}'));

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Owner can distinguish live payment requests and see which amounts are unset',
    (t) async {
      expect(const bool.fromEnvironment('QA_ISOLATED_PAYMENT_QUEUE'), isTrue);
      final previous = HttpOverrides.current;
      addTearDown(() => HttpOverrides.global = previous);
      final setup = (await t.runAsync(() async {
        HttpOverrides.global = null;
        final transport = _ReadQueueTransport(Zone.current);
        addTearDown(transport.close);
        final auth = AuthController(transport, MemoryStore())
          ..restoring = false;
        const phone = '01000000010';
        await auth.verify(phone, await auth.requestOtp(phone), '246810');
        expect(auth.session?.selected?.role, 'AcademyOwner');
        expect(
          auth.session?.selected?.academyId,
          '10000000-0000-0000-0000-000000000001',
        );
        final snapshot = obj(await auth.request('GET', _queue));
        final finance = obj(await auth.request('GET', _finance));
        transport.failQueueOnce = true;
        return (auth, transport, snapshot, finance);
      }))!;
      final (auth, transport, snapshot, finance) = setup;
      final items = rows(snapshot['items']);
      final unset = items.firstWhere(
        (row) => row['type'] == 'Enrollment' && row['amount'] == null,
      );
      final candidates = items.where(
        (row) =>
            row['type'] == 'Renewal' &&
            row['hasProof'] == true &&
            row['amount'] == 720,
      );
      final groups = <(Object?, Object?, Object?, Object?), List<Json>>{};
      for (final row in candidates) {
        groups
            .putIfAbsent((
              row['player'],
              row['sport'],
              row['branch'],
              row['plan'],
            ), () => [])
            .add(row);
      }
      final twins = groups.values
          .firstWhere((group) => group.length >= 2)
          .take(2)
          .toList();
      expect(twins[0]['reference'], isNot(twins[1]['reference']));

      Future<void> tap(Finder finder) async {
        await revealTestTarget(t, finder);
        expect(finder.hitTestable(), findsOneWidget);
        await t.tap(finder);
        await t.pump(const Duration(milliseconds: 700));
      }

      Future<void> shot(String name) async {
        await t.pump(const Duration(milliseconds: 650));
        await binding.takeScreenshot('phase-iosqueue-$name');
      }

      final queueCount = find.textContaining('طلبات معلّقة (');

      await t.pumpWidget(const SizedBox());
      await t.pump();
      await t.pumpWidget(
        AcademyApp(
          auth: auth,
          environment: 'Demo',
          appearance: _ReturningRole()..dark = true,
        ),
      );
      await _waitFor(t, find.byType(NativeRoleTabs));
      await _waitFor(
        t,
        find.byWidgetPredicate(
          (widget) =>
              widget is Scrollable &&
              widget.axisDirection == AxisDirection.down,
        ),
      );
      await revealTestTarget(t, find.byType(OwnerDecisionCenter));
      final allDecisions = find.text('كل القرارات');
      await _waitFor(t, allDecisions);
      await tap(allDecisions);
      final reviewRequests = find.text('مراجعة الطلبات');
      await _waitFor(t, reviewRequests);
      await tap(reviewRequests);
      expect(find.byType(NativePaymentRequests), findsOneWidget);
      expect(find.text('تعذر تحميل طلبات الدفع.'), findsOneWidget);
      await tap(find.text('إعادة المحاولة'));
      await _waitFor(t, queueCount);
      final unsetCard = _card(unset);
      await revealTestTarget(t, unsetCard);
      expect(
        find.descendant(of: unsetCard, matching: find.text('المبلغ غير محدد')),
        findsOneWidget,
      );
      expect(
        find.descendant(
          of: unsetCard,
          matching: find.textContaining(unset['reference'] as String),
        ),
        findsOneWidget,
      );
      await shot('unset-amount');

      for (final (index, item) in twins.indexed) {
        final payment = _card(item);
        await revealTestTarget(t, payment);
        expect(
          find.descendant(
            of: payment,
            matching: find.textContaining(item['reference'] as String),
          ),
          findsOneWidget,
        );
        expect(
          find.descendant(of: payment, matching: find.text('720 ج.م')),
          findsOneWidget,
        );
        await shot('renewal-${index + 1}');
        await tap(
          find.descendant(
            of: payment,
            matching: find.text('مراجعة إثبات التجديد'),
          ),
        );
        final review = find.byType(NativeRenewalProofReview);
        expect(t.widget<NativeRenewalProofReview>(review).id, item['id']);
        expect(
          transport.reads,
          contains('/api/v1/subscriptions/payments/${item['id']}'),
        );
        await tap(find.text('العودة لقائمة طلبات الدفع'));
        expect(find.byType(NativePaymentRequests), findsOneWidget);
        await _waitFor(t, queueCount);
      }

      final after = (await t.runAsync(
        () async => obj(await auth.request('GET', _finance)),
      ))!;
      expect(after['totalCount'], finance['totalCount']);
      expect(after['totalAmount'], finance['totalAmount']);
      debugPrint(
        'iOS payment queue verified: unset=${unset['reference']}, renewals=${twins.map((row) => row['reference']).join(',')}, total=${finance['totalAmount']}',
      );
    },
    skip: const bool.fromEnvironment('QA_ONLY_DELEGATED_PAYMENT'),
  );
  testWidgets(
    'Delegated administrator can read the exact pending payment and proof without financial controls',
    (t) async {
      expect(
        const bool.fromEnvironment('QA_ISOLATED_DELEGATED_PAYMENT'),
        isTrue,
      );
      final previous = HttpOverrides.current;
      addTearDown(() => HttpOverrides.global = previous);
      final (auth, transport, snapshot) = (await t.runAsync(() async {
        HttpOverrides.global = null;
        final transport = _ReadQueueTransport(Zone.current);
        addTearDown(transport.close);
        final auth = AuthController(transport, MemoryStore())
          ..restoring = false;
        const phone = '01000000030';
        await auth.verify(phone, await auth.requestOtp(phone), '246810');
        expect(auth.session?.selected?.role, 'AcademyAdmin');
        expect(
          auth.session?.selected?.academyId,
          '10000000-0000-0000-0000-000000000001',
        );
        final snapshot = obj(await auth.request('GET', _queue));
        expect(snapshot['canReviewRenewalProof'], isFalse);
        return (auth, transport, snapshot);
      }))!;
      final items = rows(snapshot['items']);
      final unproved = items.firstWhere(
        (row) =>
            row['type'] == 'Renewal' &&
            row['hasProof'] != true &&
            row['amount'] == 900,
      );
      final proved = items.firstWhere(
        (row) => row['id'] == '6a65000a-bee5-498d-954d-152558510026',
      );
      expect(unproved['canOpen'], isTrue);
      expect(proved['canOpen'], isTrue);
      final count = find.text('طلبات معلّقة (${snapshot['pending']})');

      Future<void> tap(Finder finder) async {
        await revealTestTarget(t, finder);
        expect(finder.hitTestable(), findsOneWidget);
        await t.tap(finder);
        await t.pump(const Duration(milliseconds: 650));
      }

      await t.pumpWidget(const SizedBox());
      await t.pump();
      await t.pumpWidget(
        AcademyApp(
          auth: auth,
          environment: 'Demo',
          appearance: _ReturningRole(),
        ),
      );
      await _waitFor(t, find.byType(NativeRoleTabs));
      await _waitFor(t, find.byType(Scrollable));
      final task = find.byKey(const ValueKey('admin-home-payments-task'));
      await revealTestTarget(t, task);
      final entry = find.descendant(
        of: task,
        matching: find.text('عرض الطلبات'),
      );
      await tap(entry);
      await _waitFor(t, count);
      final unprovedCard = _card(unproved);
      await revealTestTarget(t, unprovedCard);
      await binding.takeScreenshot('phase-iosdelegated-payment-queue');
      await tap(
        find.descendant(
          of: unprovedCard,
          matching: find.text('عرض طلب التجديد'),
        ),
      );
      await _waitFor(t, find.text('بانتظار التحصيل'));
      expect(
        t.widget<NativeRenewalPayment>(find.byType(NativeRenewalPayment)).id,
        unproved['id'],
      );
      expect(find.text('طريقة التحصيل'), findsNothing);
      expect(find.text('تأكيد التحصيل وإصدار الإيصال'), findsNothing);
      await binding.takeScreenshot('phase-iosdelegated-payment-readonly');
      final back = find.byType(BackButton);
      await tap(back);
      await _waitFor(t, count);

      final provedCard = _card(proved);
      await revealTestTarget(t, provedCard);
      await tap(
        find.descendant(
          of: provedCard,
          matching: find.text('عرض إثبات التجديد'),
        ),
      );
      await _waitFor(t, find.text('إثبات التحويل'));
      expect(find.text('إثبات التجديد'), findsOneWidget);
      final zoom = find.text('تكبير');
      await revealTestTarget(t, find.text('إثبات التحويل'));
      await _waitFor(t, zoom);
      expect(
        t
            .widget<NativeRenewalProofReview>(
              find.byType(NativeRenewalProofReview),
            )
            .id,
        proved['id'],
      );
      expect(find.text('قبول وتفعيل التجديد'), findsNothing);
      expect(find.text('رفض الإثبات'), findsNothing);
      expect(
        transport.reads,
        contains('/api/v1/manage/payments/${proved['id']}/proof'),
      );
      await binding.takeScreenshot('phase-iosdelegated-proof-readonly');
      if (const bool.fromEnvironment('QA_ISOLATED_MOBILE_SCOPE_RESUME')) {
        final owner = (await t.runAsync(() async {
          final owner = _OwnerWebSession();
          addTearDown(owner.close);
          await owner.request(
            'POST',
            '/api/v1/auth/login',
            body: {
              'email': 'owner.nogoom@example.test',
              'password': 'Demo-Only-123!',
            },
          );
          final identity = obj(await owner.request('GET', '/api/v1/me'));
          expect(identity['role'], 'AcademyOwner');
          expect(identity['academyId'], auth.session?.selected?.academyId);
          return owner;
        }))!;
        final member = auth.session!.selected!;
        expect(member.id, '80e02a6f-42ba-4376-9957-27c66cde2db2');
        final accessPath = '/api/v1/manage/staff/${member.id}/access';
        final original = (await t.runAsync(
          () async => obj(await owner.request('GET', accessPath)),
        ))!;
        final grants = rows(original['actions']);
        expect(
          grants
              .where((row) => row['read'] == true)
              .map((row) => row['module']),
          unorderedEquals(['payments', 'subscriptions']),
        );
        final before = (await t.runAsync(
          () async => obj(await owner.request('GET', _finance)),
        ))!;
        Future<void> changeRead({
          required bool payments,
          required bool subscriptions,
          bool editPayments = false,
        }) async {
          await t.runAsync(
            () => owner.request(
              'PUT',
              accessPath,
              body: {
                'allBranches': true,
                'branches': <String>[],
                'actions': [
                  for (final module
                      in (original['modules'] as List).cast<String>())
                    {
                      'module': module,
                      'read':
                          module == 'payments' && payments ||
                          module == 'subscriptions' && subscriptions,
                      'add': false,
                      'edit': module == 'payments' && editPayments,
                      'delete': false,
                    },
                ],
              },
            ),
          );
        }

        Future<void> lifecycle(AppLifecycleState state) async {
          await t.binding.defaultBinaryMessenger.handlePlatformMessage(
            SystemChannels.lifecycle.name,
            const StringCodec().encodeMessage(state.toString()),
            null,
          );
        }

        Future<void> resume() async {
          await lifecycle(AppLifecycleState.resumed);
          await t.pump();
        }

        Future<void> background() => lifecycle(AppLifecycleState.paused);

        Future<void> reopenQueue() async {
          await _waitFor(t, find.text('يوم الأكاديمية'));
          await _waitFor(t, find.byType(Scrollable));
          await revealTestTarget(t, task);
          await tap(entry);
          await _waitFor(t, count);
        }

        final closeImage = find.byTooltip('إغلاق');
        final privateProof = '/api/v1/manage/payments/${proved['id']}/proof';
        int contextReads() => transport.reads
            .where((path) => path.endsWith('/mobile/auth/memberships'))
            .length;
        final token = auth.session!.accessToken;
        await tap(zoom);
        await _waitFor(t, closeImage);
        try {
          final first = contextReads();
          await background();
          await resume();
          await _waitFor(t, closeImage.hitTestable());
          expect(contextReads(), first + 1);
          expect(auth.session!.accessToken, token);
          expect(
            auth.session!.selected!.contextRevision,
            member.contextRevision,
          );

          await background();
          await changeRead(payments: false, subscriptions: true);
          await resume();
          await _waitFor(t, find.text('يوم الأكاديمية'));
          await t.pumpAndSettle();
          expect(closeImage, findsNothing);
          expect(find.byType(NativeRenewalProofReview), findsNothing);
          expect(find.byType(NativePaymentRequests), findsNothing);
          expect(task, findsNothing);
          expect(
            auth.session!.selected!.contextRevision,
            greaterThan(member.contextRevision),
          );
          await binding.takeScreenshot('phase-iosscope-revoked');
          await t.runAsync(() async {
            for (final path in [_queue, privateProof]) {
              await expectLater(
                auth.request('GET', path),
                throwsA(
                  isA<ApiFailure>().having((e) => e.status, 'status', 403),
                ),
              );
            }
          });

          await background();
          await changeRead(payments: true, subscriptions: false);
          await resume();
          await reopenQueue();
          await revealTestTarget(t, provedCard);
          expect(
            find.descendant(of: provedCard, matching: find.text('720 ج.م')),
            findsOneWidget,
          );
          expect(
            find.descendant(
              of: provedCard,
              matching: find.text('عرض إثبات التجديد'),
            ),
            findsNothing,
          );
          await binding.takeScreenshot('phase-iosscope-queue-only');
          await t.runAsync(() async {
            await expectLater(
              auth.request(
                'GET',
                '/api/v1/subscriptions/payments/${proved['id']}',
              ),
              throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 403)),
            );
          });

          await background();
          await changeRead(
            payments: true,
            subscriptions: true,
            editPayments: true,
          );
          await resume();
          await reopenQueue();
          await revealTestTarget(t, provedCard);
          await tap(
            find.descendant(
              of: provedCard,
              matching: find.text('مراجعة إثبات التجديد'),
            ),
          );
          await _waitFor(t, find.text('إثبات التحويل'));
          await revealTestTarget(t, find.text('إثبات التحويل'));
          await _waitFor(t, zoom);
          await tap(find.text('رفض الإثبات'));
          final confirm = find.text('تأكيد المراجعة');
          await _waitFor(t, confirm);
          await background();
          await changeRead(payments: true, subscriptions: true);
          await resume();
          await reopenQueue();
          expect(confirm, findsNothing);
          expect(find.byType(NativeRenewalProofReview), findsNothing);
          await revealTestTarget(t, provedCard);
          expect(
            find.descendant(
              of: provedCard,
              matching: find.text('عرض إثبات التجديد'),
            ),
            findsOneWidget,
          );
          await binding.takeScreenshot('phase-iosscope-after-confirmation');
          final savedProof = (await t.runAsync(
            () async => obj(await auth.request('GET', privateProof)),
          ))!;
          expect(savedProof['status'], 'Pending');
          final after = (await t.runAsync(
            () async => obj(await owner.request('GET', _finance)),
          ))!;
          expect(after['totalCount'], before['totalCount']);
          expect(after['totalAmount'], before['totalAmount']);
          debugPrint(
            'iOS payment scope verified: checks=${contextReads()}, unchanged=${after['totalCount']}/${after['totalAmount']}',
          );
        } finally {
          await changeRead(payments: true, subscriptions: true);
        }
        return;
      }
      await tap(find.text('العودة لقائمة طلبات الدفع'));
      await _waitFor(t, count);
      debugPrint(
        'iOS delegated payment verified: pending=${snapshot['pending']}, renewal=${unproved['reference']}, proof=${proved['reference']}',
      );
    },
    skip: !const bool.fromEnvironment('QA_ISOLATED_DELEGATED_PAYMENT'),
  );
}
