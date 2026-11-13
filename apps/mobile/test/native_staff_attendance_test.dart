import 'dart:async';
import 'dart:convert';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/auth/session.dart';
import 'package:academy_mobile/core/auth/staff_passkey_provider.dart';
import 'package:academy_mobile/features/shared/native_staff_attendance.dart';
import 'package:academy_mobile/features/shared/staff_attendance_exceptions.dart';
import 'package:academy_mobile/features/shared/staff_passkeys.dart';
import 'package:academy_mobile/features/shared/staff_work_periods.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show Handler, MemoryStore;
import 'coach_test.dart' show coach, coachCredentials, shell;
import 'support/visibility.dart';

class FakeSigner implements StaffPasskeyProvider {
  @override
  bool enabled = true;
  @override
  String rpId = 'academy.test';
  int registrations = 0, assertions = 0, cancellations = 0;
  Object? error;
  Future<void>? wait;
  final proof = <String, dynamic>{
    'id': 'AQ',
    'rawId': 'AQ',
    'type': 'public-key',
    'response': {
      'signature': 'Ag',
      'userHandle': 'Aw',
      'authenticatorData': 'BA',
      'clientDataJSON': 'BQ',
    },
    'extensions': <String, dynamic>{},
  };
  @override
  Future<Map<String, dynamic>> register(Map<String, dynamic> o) async {
    registrations++;
    if (error != null) throw error!;
    return proof;
  }

  @override
  Future<Map<String, dynamic>> assertCredential(Map<String, dynamic> o) async {
    assertions++;
    await wait;
    if (error != null) throw error!;
    return proof;
  }

  @override
  Future<void> cancel() async {
    cancellations++;
  }
}

class FakeLocation implements StaffLocationSource {
  int captures = 0;
  @override
  Future<StaffLocationSample> capture() async {
    captures++;
    return StaffLocationSample(30.05, 31.3, 5, DateTime.utc(2026, 10, 17, 10));
  }
}

Map<String, dynamic> state({bool open = false, bool configured = true}) => {
  'configured': configured,
  'nativeIosOriginConfigured': true,
  'rpId': 'academy.test',
  'hasCredential': true,
  'hasOpenAttendance': open,
  'requiresSourceReview': false,
  'branches': <Map<String, dynamic>>[
    {
      'id': 'branch',
      'name': 'فرع اصطناعي',
      'configured': true,
      'enabled': true,
      'radiusMeters': 50,
      'maxAccuracyMeters': 30,
      'maxAgeSeconds': 30,
    },
  ],
  'items': <Map<String, dynamic>>[],
};
Map<String, dynamic> options(String operation) => {
  'purpose': operation,
  'challengeId': 'nonce',
  'options': {
    'rpId': 'academy.test',
    'userVerification': 'required',
    'challenge': 'AQ',
    'allowCredentials': [
      {'id': 'Ag', 'type': 'public-key'},
    ],
  },
};
Map<String, dynamic> receipt(String operation) => {
  'attendanceRecorded': true,
  'operation': operation,
  'attendanceId': '10000000-0000-0000-0000-000000000001',
  'workPeriodId': '20000000-0000-0000-0000-000000000002',
  'trustSource': 'ServerVerified',
  'confirmedAtUtc': '2026-10-17T10:00:01Z',
};

const restartId = '40000000-0000-0000-0000-000000000004';
const restartLookup =
    '${NativeStaffAttendanceFlow.root}/attempts/$restartId?includeState=true';

class ClockTransport extends HttpApiTransport {
  final Handler handler;
  ClockTransport(String origin, this.handler)
    : super(AppConfig('Demo', origin));

  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) => handler(method, path, body, token);
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  testWidgets(
    'attendance reloads branch policy and keeps recovery actions at 320',
    (t) async {
      t.view.physicalSize = const Size(320, 950);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.reset);
      final signer = FakeSigner(), location = FakeLocation();
      var branchEnabled = true, branchConfigured = true, otherEnabled = false;
      var open = false, statusReads = 0;
      final auth = await coach((method, path, body, token) async {
        expect(method, 'GET');
        if (path == '/api/v1/staff/work-periods') {
          return {'configured': true, 'today': '2026-11-12', 'items': []};
        }
        if (path == '$ownStaffExceptions/options') return {'branches': []};
        if (path == '$ownStaffExceptions/') return [];
        expect(path, NativeStaffAttendanceFlow.root);
        statusReads++;
        final data = state(open: open);
        final original =
            (data['branches'] as List<Map<String, dynamic>>).single;
        return {
          ...data,
          'branches': [
            {
              ...original,
              'enabled': branchEnabled,
              'configured': branchConfigured && branchEnabled,
            },
            {
              ...original,
              'id': 'second',
              'name': 'الفرع الثاني',
              'enabled': otherEnabled,
              'configured': otherEnabled,
              'radiusMeters': 80,
            },
          ],
        };
      });
      await t.pumpWidget(
        shell(
          Builder(
            builder: (context) => MediaQuery(
              data: MediaQuery.of(context)
                  .copyWith(textScaler: const TextScaler.linear(2)),
              child: NativeStaffAttendancePage(
                auth: auth,
                provider: signer,
                location: location,
              ),
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      final dropdown = find.byType(DropdownButtonFormField<String>);
      Future<void> select(String name) async {
        await revealTestTarget(t, dropdown);
        await t.tap(dropdown);
        await t.pumpAndSettle();
        await t.tap(find.text(name).last);
        await t.pumpAndSettle();
      }

      Future<void> press(String label) async {
        final target = find.text(label);
        await revealTestTarget(t, target);
        await t.tap(target);
        await t.pumpAndSettle();
      }

      Future<void> resume() async {
        for (final state in ['paused', 'resumed']) {
          await t.binding.defaultBinaryMessenger.handlePlatformMessage(
            'flutter/lifecycle',
            const StringCodec().encodeMessage('AppLifecycleState.$state'),
            (_) {},
          );
        }
        await t.pumpAndSettle();
      }

      await select('فرع اصطناعي');
      expect(find.textContaining('النطاق 50 مترًا'), findsOneWidget);
      branchEnabled = false;
      final readsBeforeResume = statusReads;
      await resume();
      expect(statusReads, readsBeforeResume + 1);
      await revealTestTarget(
        t,
        find.textContaining('التسجيل الذاتي موقوف للفروع المتاحة'),
      );
      expect(dropdown, findsNothing);
      expect(find.text('تسجيل حضوري'), findsNothing);
      open = true;
      await press('تحديث حالة الحضور');
      await revealTestTarget(
        t,
        find.textContaining('فترة دوامك ما زالت مفتوحة'),
      );
      expect(find.text('تسجيل انصرافي'), findsNothing);
      for (final (label, page) in [
        ('سجل دوامي', StaffWorkPeriodsPage),
        ('طلب إثبات حضور للمراجعة', StaffAttendanceExceptionPage),
      ]) {
        final readsBefore = statusReads;
        await press(label);
        final route = find.byType(page);
        expect(route, findsOneWidget);
        Navigator.of(t.element(route)).pop();
        await t.pumpAndSettle();
        expect(statusReads, readsBefore + 1);
      }
      await press('طريقة التسجيل والخصوصية');
      await revealTestTarget(
        t,
        find.textContaining('يُطلب الموقع عند التسجيل'),
      );
      branchEnabled = true;
      branchConfigured = false;
      await press('تحديث حالة الحضور');
      await revealTestTarget(
        t,
        find.textContaining('يلزم إعداد موقع الفرع قبل التسجيل'),
      );
      branchConfigured = true;
      otherEnabled = true;
      open = false;
      await press('تحديث حالة الحضور');
      await select('فرع اصطناعي');
      branchEnabled = false;
      await press('تحديث حالة الحضور');
      expect(find.textContaining('النطاق 50 مترًا'), findsNothing);
      await select('الفرع الثاني');
      expect(find.textContaining('النطاق 80 مترًا'), findsOneWidget);
      branchEnabled = true;
      await resume();
      expect(find.textContaining('النطاق 80 مترًا'), findsOneWidget);
      expect(location.captures, 0);
      expect(signer.assertions, 0);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'attendance opens history, review and privacy help without recording',
    (tester) async {
      final reads = <String>[];
      final auth = await coach((method, path, body, token) async {
        expect(method, 'GET');
        reads.add(path);
        if (path == NativeStaffAttendanceFlow.root) return state();
        if (path == '/api/v1/staff/work-periods') {
          return {
            'configured': true,
            'today': '2026-11-12',
            'workDate': '2026-11-12',
            'items': <Object>[],
          };
        }
        if (path == '$ownStaffExceptions/options') {
          return {'branches': <Object>[]};
        }
        expect(path, '$ownStaffExceptions/');
        return <Object>[];
      });
      await tester.pumpWidget(
        shell(
          NativeStaffAttendancePage(
            auth: auth,
            provider: FakeSigner()..enabled = false,
          ),
        ),
      );
      await tester.pumpAndSettle();

      Future<void> openAndReturn(String label, Finder target) async {
        final link = find.text(label);
        await revealTestTarget(tester, link);
        await tester.tap(link);
        await tester.pumpAndSettle();
        expect(target, findsOneWidget);
        Navigator.of(tester.element(target)).pop();
        await tester.pumpAndSettle();
        expect(find.text('حضوري وانصرافي'), findsOneWidget);
      }

      await openAndReturn('سجل دوامي', find.byType(StaffWorkPeriodsPage));
      await openAndReturn(
        'طلب إثبات حضور للمراجعة',
        find.byType(StaffAttendanceExceptionPage),
      );
      final help = find.text('طريقة التسجيل والخصوصية');
      await revealTestTarget(tester, help);
      await tester.tap(help);
      await tester.pumpAndSettle();
      expect(find.textContaining('يُطلب الموقع عند التسجيل'), findsOneWidget);
      expect(
        reads,
        containsAll([
          '/api/v1/staff/work-periods',
          '$ownStaffExceptions/options',
          '$ownStaffExceptions/',
        ]),
      );
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'opens verification from attendance and reloads the branch on return',
    (tester) async {
      var credentialAvailable = false;
      final attendanceReads = <String>[];
      final auth = await coach((method, path, body, token) async {
        expect(method, 'GET');
        if (path == NativeStaffAttendanceFlow.root) {
          attendanceReads.add(path);
          return {...state(), 'hasCredential': credentialAvailable};
        }
        expect(path, '/api/v1/staff/passkeys');
        return {...state(), 'items': <Object>[]};
      });
      await tester.pumpWidget(
        shell(
          NativeStaffAttendancePage(
            auth: auth,
            provider: FakeSigner(),
            location: FakeLocation(),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('فرع الحضور'), findsNothing);
      expect(find.text('تسجيل حضوري'), findsNothing);
      await tester.tap(find.text('وسائل التحقق'));
      await tester.pumpAndSettle();
      expect(find.text('وسائل التحقق الخاصة بي'), findsOneWidget);
      credentialAvailable = true;
      Navigator.of(tester.element(find.byType(StaffPasskeyPage))).pop();
      await tester.pumpAndSettle();
      expect(find.text('فرع الحضور'), findsOneWidget);
      expect(find.text('وسائل التحقق'), findsNothing);
      expect(attendanceReads, hasLength(2));
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  for (final operation in ['CheckIn', 'CheckOut']) {
    test(
      'recovers $operation with a new session and no second signed write',
      () async {
        final store = MemoryStore();
        final calls = <String>[];
        var saved = false;
        Future<Object?> handler(
          String method,
          String path,
          Object? body,
          String? token,
        ) async {
          calls.add('$method $path');
          if (path == restartLookup) return receipt(operation);
          if (path.endsWith('/options')) {
            return {...options(operation), 'challengeId': restartId};
          }
          if (path.endsWith('/complete')) {
            saved = true;
            throw const ApiFailure(0, 'تعذر الاتصال');
          }
          return state(
            open: saved ? operation == 'CheckIn' : operation == 'CheckOut',
          );
        }

        final firstAuth = await coach(handler);
        final first = NativeStaffAttendanceFlow(
          firstAuth,
          provider: FakeSigner(),
          location: FakeLocation(),
          attemptStore: store,
        );
        await first.load();
        await first.submit('branch', operation);
        expect(first.hasPending, isTrue);
        expect(first.message, isEmpty);
        expect(jsonDecode(store.token!), {
          'challengeId': restartId,
          'operation': operation,
        });
        first.dispose();
        firstAuth.dispose();

        final nextAuth = await coach(handler);
        final signer = FakeSigner(), location = FakeLocation();
        final reopened = NativeStaffAttendanceFlow(
          nextAuth,
          provider: signer,
          location: location,
          attemptStore: store,
        );
        await reopened.load();
        expect(
          reopened.message,
          contains(
            operation == 'CheckIn' ? 'تم تسجيل حضورك' : 'تم تسجيل انصرافك',
          ),
        );
        expect(reopened.data!['hasOpenAttendance'], operation == 'CheckIn');
        expect(reopened.hasPending, isFalse);
        expect(store.token, isNull);
        expect(calls.where((call) => call.startsWith('POST')), [
          'POST ${NativeStaffAttendanceFlow.root}/options',
          'POST ${NativeStaffAttendanceFlow.root}/complete',
        ]);
        expect(
          calls.where((call) => call == 'GET $restartLookup'),
          hasLength(1),
        );
        expect(signer.assertions, 0);
        expect(location.captures, 0);
        reopened.dispose();
        nextAuth.dispose();
      },
    );
  }

  testWidgets('shows a restored attempt before returning to branch selection', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 1;
    tester.view.physicalSize = const Size(320, 760);
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    final store = MemoryStore()
      ..token = jsonEncode({'challengeId': restartId, 'operation': 'CheckIn'});
    var closed = false, stateAvailable = false;
    final calls = <String>[];
    final auth = await coach((method, path, body, token) async {
      calls.add('$method $path');
      if (path == restartLookup) {
        return {
          'attendanceRecorded': false,
          'attemptState': closed ? 'Closed' : 'Pending',
        };
      }
      if (!stateAvailable) throw const ApiFailure(503, 'تعذر الاتصال');
      return state();
    });
    final signer = FakeSigner(), location = FakeLocation();
    await tester.pumpWidget(
      shell(
        NativeStaffAttendancePage(
          auth: auth,
          provider: signer,
          location: location,
          attemptStore: store,
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('محاولة الحضور السابقة'), findsOneWidget);
    expect(find.textContaining('لم يظهر تسجيل مؤكد بعد'), findsOneWidget);
    expect(find.text('إعادة إرسال الطلب نفسه'), findsNothing);
    expect(find.text('فرع الحضور'), findsNothing);
    expect(find.text('تسجيل حضوري'), findsNothing);

    closed = true;
    final verify = find.text('التحقق من نتيجة التسجيل');
    await tester.tap(verify);
    await tester.pumpAndSettle();
    expect(verify, findsOneWidget);
    expect(find.text('فرع الحضور'), findsNothing);
    expect(store.token, isNotNull);

    stateAvailable = true;
    await tester.tap(verify);
    await tester.pumpAndSettle();
    expect(find.text('انتهت المحاولة دون تسجيل.'), findsOneWidget);
    expect(find.text('فرع الحضور'), findsOneWidget);
    expect(verify, findsNothing);
    expect(store.token, isNull);
    expect(signer.assertions, 0);
    expect(location.captures, 0);
    expect(calls.every((call) => call.startsWith('GET')), isTrue);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox());
    auth.dispose();
  });

  test(
    'keeps a restored reference on an unavailable or mismatched receipt',
    () async {
      final store = MemoryStore()
        ..token = jsonEncode({
          'challengeId': restartId,
          'operation': 'CheckOut',
        });
      var lookup = 0;
      final auth = await coach((method, path, body, token) async {
        if (path == restartLookup) {
          lookup++;
          if (lookup == 1) throw const ApiFailure(503, 'تعذر الاتصال');
          if (lookup == 2) return receipt('CheckIn');
          if (lookup == 3) {
            return {...receipt('CheckOut'), 'workPeriodId': null};
          }
          if (lookup == 4) {
            return {...receipt('CheckOut'), 'confirmedAtUtc': 'قديم'};
          }
          return receipt('CheckOut');
        }
        return state();
      });
      final signer = FakeSigner(), location = FakeLocation();
      final flow = NativeStaffAttendanceFlow(
        auth,
        provider: signer,
        location: location,
        attemptStore: store,
      );
      for (var attempt = 0; attempt < 4; attempt++) {
        await flow.load();
        expect(flow.error, contains('تعذر التحقق من نتيجة التسجيل'));
        expect(flow.message, isEmpty);
        expect(flow.hasPending, isTrue);
        expect(flow.canRetry, isFalse);
        expect(store.token, isNotNull);
        await flow.submit('branch', 'CheckOut');
      }
      await flow.load();
      expect(flow.message, contains('تم تسجيل انصرافك'));
      expect(store.token, isNull);
      expect(signer.assertions, 0);
      expect(location.captures, 0);
      flow.dispose();
      auth.dispose();
    },
  );

  test('recovers a committed receipt when the branch is disabled and state briefly fails', () async {
    final store = MemoryStore()
      ..token = jsonEncode({'challengeId': restartId, 'operation': 'CheckIn'});
    var branchReadable = false;
    final auth = await coach((method, path, body, token) async {
      if (path == restartLookup) return receipt('CheckIn');
      if (!branchReadable) throw const ApiFailure(503, 'تعذر الاتصال');
      return {
        ...state(open: true),
        'branches': [
          {'id': 'branch', 'enabled': false},
        ],
      };
    });
    final flow = NativeStaffAttendanceFlow(
      auth,
      provider: FakeSigner(),
      location: FakeLocation(),
      attemptStore: store,
    );
    await flow.load();
    expect(flow.message, contains('تم تسجيل حضورك'));
    expect(flow.error, contains('تعذر تحديث الحالة'));
    expect(flow.enabled, isFalse);
    expect(store.token, isNull);
    branchReadable = true;
    await flow.load();
    expect(flow.data!['hasOpenAttendance'], isTrue);
    expect(flow.data!['branches'].single['enabled'], isFalse);
    flow.dispose();
    auth.dispose();
  });

  test('a failed secure write still allows the original request to be retried in the open screen', () async {
    final store = MemoryStore()..failWrite = true;
    final signer = FakeSigner(), location = FakeLocation(), bodies = <String>[];
    final auth = await coach((method, path, body, token) async {
      if (path.endsWith('/options')) {
        return {...options('CheckIn'), 'challengeId': restartId};
      }
      if (path.endsWith('/complete')) {
        bodies.add(jsonEncode(body));
        if (bodies.length == 1) throw const ApiFailure(0, 'تعذر الاتصال');
        return receipt('CheckIn');
      }
      return state();
    });
    final flow = NativeStaffAttendanceFlow(
      auth,
      provider: signer,
      location: location,
      attemptStore: store,
    );
    await flow.load();
    await flow.submit('branch', 'CheckIn');
    expect(flow.canRetry, isTrue);
    expect(store.token, isNull);
    await flow.submit('', '');
    expect(bodies, [bodies.first, bodies.first]);
    expect(signer.assertions, 1);
    expect(location.captures, 1);
    expect(flow.message, contains('تم تسجيل حضورك'));
    flow.dispose();
    auth.dispose();
  });

  test(
    'isolates device storage by the authenticated membership and server',
    () async {
      FlutterSecureStorage.setMockInitialValues({});
      const deviceStore = FlutterSecureStorage();
      final lookups = <String>[];
      Future<Object?> handler(
        String method,
        String path,
        Object? body,
        String? token,
      ) async {
        if (path == restartLookup) {
          lookups.add(path);
          return receipt('CheckIn');
        }
        if (path.endsWith('/options')) {
          return {...options('CheckIn'), 'challengeId': restartId};
        }
        if (path.endsWith('/complete')) {
          throw const ApiFailure(0, 'تعذر الاتصال');
        }
        return state();
      }

      AuthController account(String host, String member) =>
          AuthController(ClockTransport(host, handler), MemoryStore())
            ..session = Session.fromJson({
              ...coachCredentials(),
              'membershipId': member,
              'memberships': [
                {
                  'id': member,
                  'academyId': 'academy',
                  'academyName': 'أكاديمية العرض',
                  'role': 'Coach',
                },
              ],
            });
      final own = account('https://clock.example.test', 'coach');
      final first = NativeStaffAttendanceFlow(
        own,
        provider: FakeSigner(),
        location: FakeLocation(),
      );
      await first.load();
      await first.submit('branch', 'CheckIn');
      expect((await deviceStore.readAll()).values.map(jsonDecode).single, {
        'challengeId': restartId,
        'operation': 'CheckIn',
      });
      first.dispose();
      own.dispose();
      for (final accountDetails in [
        ('https://clock.example.test', 'different-membership'),
        ('https://other-clock.example.test', 'coach'),
        ('https://clock.example.test', 'coach'),
      ]) {
        final auth = account(accountDetails.$1, accountDetails.$2);
        final flow = NativeStaffAttendanceFlow(auth, provider: FakeSigner());
        await flow.load();
        final originalScope =
            accountDetails == ('https://clock.example.test', 'coach');
        expect(flow.message.isNotEmpty, originalScope);
        expect(lookups, hasLength(originalScope ? 1 : 0));
        flow.dispose();
        auth.dispose();
      }
      expect(await deviceStore.readAll(), isEmpty);
    },
  );

  test(
    'only clears a missing restored attempt after reading current attendance',
    () async {
      final store = MemoryStore()
        ..token = jsonEncode({
          'challengeId': restartId,
          'operation': 'CheckIn',
        });
      var stateAvailable = false;
      final auth = await coach((method, path, body, token) async {
        if (path == restartLookup) {
          throw const ApiFailure(404, 'المحاولة غير متاحة');
        }
        if (!stateAvailable) throw const ApiFailure(503, 'تعذر الاتصال');
        return state();
      });
      final flow = NativeStaffAttendanceFlow(
        auth,
        provider: FakeSigner(),
        attemptStore: store,
      );
      await flow.load();
      expect(flow.hasPending, isTrue);
      expect(flow.enabled, isFalse);
      expect(store.token, isNotNull);
      stateAvailable = true;
      await flow.load();
      expect(flow.hasPending, isFalse);
      expect(flow.enabled, isTrue);
      expect(flow.message, 'لم تعد هذه المحاولة متاحة.');
      expect(store.token, isNull);
      flow.dispose();
      auth.dispose();
    },
  );

  test('a late restored receipt does not update a signed-out screen', () async {
    final store = MemoryStore()
      ..token = jsonEncode({'challengeId': restartId, 'operation': 'CheckIn'});
    final requestStarted = Completer<void>();
    final result = Completer<Object?>();
    final auth = await coach((method, path, body, token) async {
      expect(path, restartLookup);
      requestStarted.complete();
      return result.future;
    });
    final flow = NativeStaffAttendanceFlow(
      auth,
      provider: FakeSigner(),
      attemptStore: store,
    );
    final loading = flow.load();
    await requestStarted.future;
    await auth.logout();
    result.complete(receipt('CheckIn'));
    await loading;
    expect(flow.hasPending, isFalse);
    expect(flow.message, isEmpty);
    expect(flow.data, isNull);
    expect(store.token, isNotNull);
    flow.dispose();
    auth.dispose();
  });

  test('location only on click, bound intent and signed complete precede success; checkout uses same pipeline', () async {
    final writes = <String>[],
        bodies = <Map<String, dynamic>>[],
        signer = FakeSigner(),
        location = FakeLocation();
    bool open = false;
    final auth = await coach((m, p, b, t) async {
      if (m == 'GET') return state(open: open);
      writes.add(p);
      if (p.endsWith('/options')) return options((b as Map)['operation']);
      final command = Map<String, dynamic>.from(b as Map);
      bodies.add(command);
      open = (command['intent'] as Map)['operation'] == 'CheckIn';
      return receipt(open ? 'CheckIn' : 'CheckOut');
    });
    final flow = NativeStaffAttendanceFlow(
      auth,
      provider: signer,
      location: location,
    );
    await flow.load();
    expect(location.captures, 0);
    await flow.submit('foreign', 'CheckIn');
    expect(writes, isEmpty);
    await flow.submit('branch', 'CheckIn');
    expect(flow.message, 'تم تسجيل حضورك بوقت الخادم المؤكد.');
    expect(flow.data!['hasOpenAttendance'], isTrue);
    expect(flow.hasPending, isFalse);
    expect(
      (bodies.single['intent'] as Map)['capturedAtUtc'],
      '2026-10-17T10:00:00.000Z',
    );
    expect(bodies.single['credential'], signer.proof);
    await flow.submit('branch', 'CheckOut');
    expect(flow.message, 'تم تسجيل انصرافك للفترة نفسها بوقت الخادم المؤكد.');
    expect(writes, [
      '${NativeStaffAttendanceFlow.root}/options',
      '${NativeStaffAttendanceFlow.root}/complete',
      '${NativeStaffAttendanceFlow.root}/options',
      '${NativeStaffAttendanceFlow.root}/complete',
    ]);
    expect(jsonEncode(bodies), isNot(contains('biometric')));
    expect(signer.assertions, 2);
    flow.dispose();
    auth.dispose();
  });
  test('lost reply retains the exact proof and intent, retries no new location or nonce even with changed input', () async {
    final bodies = <String>[], signer = FakeSigner(), location = FakeLocation();
    var first = true, optionCalls = 0;
    final auth = await coach((m, p, b, t) async {
      if (m == 'GET') return state();
      if (p.endsWith('/options')) {
        optionCalls++;
        return options('CheckIn');
      }
      bodies.add(jsonEncode(b));
      if (first) {
        first = false;
        throw const ApiFailure(0, 'offline');
      }
      return receipt('CheckIn');
    });
    final flow = NativeStaffAttendanceFlow(
      auth,
      provider: signer,
      location: location,
    );
    await flow.load();
    await flow.submit('branch', 'CheckIn');
    expect(flow.message, isEmpty);
    expect(flow.hasPending, isTrue);
    expect(flow.error, contains('تحقّق من النتيجة'));
    await flow.submit('other', 'CheckOut');
    expect(bodies[1], bodies[0]);
    expect(optionCalls, 1);
    expect(location.captures, 1);
    expect(signer.assertions, 1);
    expect(flow.hasPending, isFalse);
    flow.dispose();
    auth.dispose();
  });
  test('HTTP conflict ends the command without success, invalid server receipt never claims attendance', () async {
    for (final invalid in [false, true]) {
      final signer = FakeSigner(), location = FakeLocation();
      final auth = await coach((m, p, b, t) async {
        if (m == 'GET') return state();
        if (p.endsWith('/options')) return options('CheckIn');
        if (invalid) {
          return {'attendanceRecorded': true, 'operation': 'CheckIn'};
        }
        throw const ApiFailure(409, 'تغيرت سياسة الفرع؛ لم يسجل حضور.');
      });
      final flow = NativeStaffAttendanceFlow(
        auth,
        provider: signer,
        location: location,
      );
      await flow.load();
      await flow.submit('branch', 'CheckIn');
      expect(flow.message, isEmpty);
      expect(flow.hasPending, invalid);
      if (!invalid) expect(flow.error, 'تغيرت سياسة الفرع؛ لم يسجل حضور.');
      flow.dispose();
      auth.dispose();
    }
  });
  test('refresh reads the exact confirmed attempt after a lost reply without resending', () async {
    final calls = <String>[], signer = FakeSigner(), location = FakeLocation();
    final root = NativeStaffAttendanceFlow.root;
    final auth = await coach((method, path, body, token) async {
      calls.add('$method $path');
      if (path == '$root/options') return options('CheckIn');
      if (path == '$root/complete') throw const ApiFailure(0, 'offline');
      if (path == '$root/attempts/nonce') return receipt('CheckIn');
      return state(open: calls.any((p) => p.contains('/complete')));
    });
    final flow = NativeStaffAttendanceFlow(
      auth,
      provider: signer,
      location: location,
    );
    await flow.load();
    await flow.submit('branch', 'CheckIn');
    expect(flow.hasPending, isTrue);
    await flow.load();
    expect(calls, contains('GET $root/attempts/nonce'));
    expect(calls.where((p) => p.endsWith('/complete')), hasLength(1));
    expect(flow.hasPending, isFalse);
    expect(flow.message, 'تم تسجيل حضورك بوقت الخادم المؤكد.');
    expect(location.captures, 1);
    expect(signer.assertions, 1);
    flow.dispose();
    auth.dispose();
  });
  test('rate limiting preserves proof; expired replay clears it and requires current state', () async {
    final proof = <String>[];
    final root = NativeStaffAttendanceFlow.root;
    final signer = FakeSigner(), location = FakeLocation();
    var stateReads = 0;
    final auth = await coach((method, path, body, token) async {
      if (path == '$root/options') return options('CheckIn');
      if (path == '$root/attempts/nonce') {
        throw const ApiFailure(404, 'غير متاح');
      }
      if (path == '$root/complete') {
        proof.add(jsonEncode(body));
        if (proof.length == 1) throw const ApiFailure(429, 'انتظر قليلًا');
        throw const ApiFailure(409, 'انتهت محاولة التسجيل.');
      }
      stateReads++;
      if (stateReads == 3) throw const ApiFailure(503, 'تعذر الاتصال');
      return state();
    });
    final flow = NativeStaffAttendanceFlow(
      auth,
      provider: signer,
      location: location,
    );
    await flow.load();
    await flow.submit('branch', 'CheckIn');
    await flow.load();
    expect(flow.error, contains('لم تُحفظ نتيجة مؤكدة'));
    expect(flow.hasPending, isTrue);
    await flow.submit('', '');
    expect(flow.hasPending, isFalse);
    expect(flow.error, 'انتهت محاولة التسجيل.');
    expect(flow.enabled, isFalse);
    expect(proof, [proof.first, proof.first]);
    expect(signer.assertions, 1);
    expect(location.captures, 1);
    await flow.load();
    expect(flow.enabled, isTrue);
    flow.dispose();
    auth.dispose();
  });

  test('location reasons are localized only on staff attendance endpoints', () {
    const path = '${NativeStaffAttendanceFlow.root}/options';
    final outside = jsonEncode({
      'message': 'الموقع خارج نطاق الفرع؛ لم يسجل حضور. يمكنك طلب مراجعة.',
    });
    final boundary = jsonEncode({
      'message': 'الموقع قريب من حدود النطاق ولا يكفي للقبول؛ أعد المحاولة أو اطلب مراجعة.',
    });
    expect(
      staffAttendanceConflictMessage(path, outside),
      contains('خارج نطاق الفرع'),
    );
    expect(
      staffAttendanceConflictMessage(path, boundary),
      contains('حدود النطاق'),
    );
    expect(staffAttendanceConflictMessage('/api/v1/other', outside), isNull);
    expect(
      staffAttendanceConflictMessage(
        path,
        '{"message":"internal stack trace"}',
      ),
      isNull,
    );
  });
  test(
    'a receipt arriving after sign-out cannot confirm the old attempt',
    () async {
      final late = Completer<Object?>();
      final auth = await coach((method, path, body, token) async {
        if (path.endsWith('/options')) return options('CheckIn');
        if (path.endsWith('/complete')) throw const ApiFailure(0, 'offline');
        if (path.endsWith('/attempts/nonce')) return late.future;
        return state();
      });
      final flow = NativeStaffAttendanceFlow(
        auth,
        provider: FakeSigner(),
        location: FakeLocation(),
      );
      await flow.load();
      await flow.submit('branch', 'CheckIn');
      final lookup = flow.load();
      await auth.logout();
      late.complete(receipt('CheckIn'));
      await lookup;
      expect(flow.message, isEmpty);
      expect(flow.data, isNull);
      expect(flow.hasPending, isFalse);
      flow.dispose();
      auth.dispose();
    },
  );
  test(
    'context change while OS prompt is pending discards proof before complete',
    () async {
      final wait = Completer<void>(),
          signer = FakeSigner(),
          writes = <String>[];
      signer.wait = wait.future;
      final auth = await coach((m, p, b, t) async {
        if (m == 'GET') return state();
        writes.add(p);
        return options('CheckIn');
      });
      final flow = NativeStaffAttendanceFlow(
        auth,
        provider: signer,
        location: FakeLocation(),
      );
      await flow.load();
      final submitting = flow.submit('branch', 'CheckIn');
      await Future<void>.delayed(Duration.zero);
      expect(signer.assertions, 1);
      await auth.logout();
      wait.complete();
      await submitting;
      expect(writes, ['${NativeStaffAttendanceFlow.root}/options']);
      expect(flow.data, isNull);
      expect(flow.hasPending, isFalse);
      expect(flow.message, isEmpty);
      flow.dispose();
      auth.dispose();
    },
  );
  test('OS cancellation and disabled configuration never submit completion or fall back to biometric boolean', () async {
    final signer = FakeSigner()
          ..error = const StaffPasskeyProblem('تم إلغاء التحقق؛ لم يسجل حضور.'),
        writes = <String>[];
    final auth = await coach((m, p, b, t) async {
      if (m == 'GET') return state();
      writes.add(p);
      return options('CheckIn');
    });
    final flow = NativeStaffAttendanceFlow(
      auth,
      provider: signer,
      location: FakeLocation(),
    );
    await flow.load();
    await flow.submit('branch', 'CheckIn');
    expect(flow.error, contains('إلغاء'));
    expect(writes, ['${NativeStaffAttendanceFlow.root}/options']);
    expect(flow.hasPending, isFalse);
    signer.enabled = false;
    await flow.submit('branch', 'CheckIn');
    expect(writes, hasLength(1));
    flow.dispose();
    auth.dispose();
  });
  testWidgets(
    'enrollment and test verification use real API contracts without claiming attendance',
    (t) async {
      final signer = FakeSigner(), posts = <String>[];
      final auth = await coach((m, p, b, token) async {
        if (p.endsWith('/attendance')) return state();
        if (m == 'GET') {
          return {
            ...state(),
            'items': <Map<String, dynamic>>[
              {'id': 'key', 'name': 'وسيلة اصطناعية', 'isActive': true},
            ],
          };
        }
        posts.add(p);
        final registration = p.contains('/register/');
        if (p.endsWith('/options')) {
          return {
            ...options(registration ? 'Register' : 'Verify'),
            'options': {'challenge': 'AQ'},
          };
        }
        return {
          'verified': true,
          'purpose': registration ? 'Register' : 'Verify',
          'attendanceRecorded': false,
          'credential': {'id': 'key'},
        };
      });
      await t.pumpWidget(shell(StaffPasskeyPage(auth: auth, provider: signer)));
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('إضافة وسيلة تحقق'),
        150,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(find.text('إضافة وسيلة تحقق'));
      await t.pumpAndSettle();
      expect(find.text('اكتب اسمًا من حرفين إلى ٨٠ حرفًا.'), findsOneWidget);
      expect(posts, isEmpty);
      await t.enterText(find.byType(TextField), 'هاتف العمل');
      await t.ensureVisible(find.text('إضافة وسيلة تحقق'));
      await t.tap(find.text('إضافة وسيلة تحقق'));
      await t.pumpAndSettle();
      expect(find.text('تمت إضافة وسيلة التحقق.'), findsOneWidget);
      await t.scrollUntilVisible(
        find.text('تجربة وسيلة التحقق'),
        150,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(find.text('تجربة وسيلة التحقق'));
      await t.pumpAndSettle();
      expect(find.text('تم التحقق من الوسيلة.'), findsOneWidget);
      expect(posts, [
        '/api/v1/staff/passkeys/register/options',
        '/api/v1/staff/passkeys/register/complete',
        '/api/v1/staff/passkeys/verify/options',
        '/api/v1/staff/passkeys/verify/complete',
      ]);
      expect(signer.registrations, 1);
      expect(signer.assertions, 1);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets(
      'fallback remains actionable at $width without native setup or a location request',
      (t) async {
        t.view.physicalSize = Size(width, 1100);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.resetPhysicalSize);
        addTearDown(t.view.resetDevicePixelRatio);
        final signer = FakeSigner()..enabled = false, location = FakeLocation();
        final auth = await coach((m, p, b, token) async => state());
        await t.pumpWidget(
          shell(
            NativeStaffAttendancePage(
              auth: auth,
              provider: signer,
              location: location,
            ),
          ),
        );
        await t.pumpAndSettle();
        expect(
          find.textContaining('إعداد الحضور الآمن غير مكتمل'),
          findsOneWidget,
        );
        expect(find.text('تسجيل حضوري'), findsNothing);
        await t.scrollUntilVisible(
          find.text('طلب إثبات حضور للمراجعة'),
          160,
          scrollable: find.byType(Scrollable).first,
        );
        expect(location.captures, 0);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  for (final width in [320.0, 390.0]) {
    testWidgets(
      'load failure is separate from setup and the pending action fits $width px at 200%',
      (t) async {
        t.view.physicalSize = Size(width, 844);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.reset);
        var reads = 0;
        final auth = await coach((method, path, body, token) async {
          if (path.endsWith('/options')) return options('CheckIn');
          if (path.endsWith('/complete')) throw const ApiFailure(0, 'offline');
          if (++reads == 1) throw const ApiFailure(503, 'تعذر الاتصال.');
          return state();
        });
        await t.pumpWidget(
          shell(
            Builder(
              builder: (context) => MediaQuery(
                data: MediaQuery.of(context)
                    .copyWith(textScaler: const TextScaler.linear(2)),
                child: NativeStaffAttendancePage(
                  auth: auth,
                  provider: FakeSigner(),
                  location: FakeLocation(),
                ),
              ),
            ),
          ),
        );
        await t.pumpAndSettle();
        expect(find.textContaining('تعذر قراءة حالة حضورك'), findsOneWidget);
        expect(
          find.textContaining('إعداد الحضور الآمن غير مكتمل'),
          findsNothing,
        );
        await t.tap(find.text('تحديث حالة الحضور'));
        await t.pumpAndSettle();
        await t.tap(find.byType(DropdownButtonFormField<String>));
        await t.pumpAndSettle();
        await t.tap(find.text('فرع اصطناعي').last);
        await t.pumpAndSettle();
        final entry = find.text('تسجيل حضوري');
        await revealTestTarget(t, entry);
        await t.tap(entry);
        await t.pumpAndSettle();
        await revealTestTarget(t, find.text('التحقق من نتيجة التسجيل'));
        expect(find.text('تسجيل انصرافي'), findsNothing);
        await revealTestTarget(t, find.text('طريقة التسجيل والخصوصية'));
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  testWidgets(
    'enabled form at 200% keeps actions visible and clears a revoked branch safely',
    (t) async {
      t.view.physicalSize = const Size(360, 1100);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.resetPhysicalSize);
      addTearDown(t.view.resetDevicePixelRatio);
      var revoked = false;
      final location = FakeLocation();
      final auth = await coach(
        (m, p, b, token) async => {
          ...state(),
          if (revoked) 'branches': <Map<String, dynamic>>[],
        },
      );
      await t.pumpWidget(
        shell(
          Builder(
            builder: (context) => MediaQuery(
              data: MediaQuery.of(context)
                  .copyWith(textScaler: const TextScaler.linear(2)),
              child: NativeStaffAttendancePage(
                auth: auth,
                provider: FakeSigner(),
                location: location,
              ),
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.byType(DropdownButtonFormField<String>),
        200,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(find.byType(DropdownButtonFormField<String>));
      await t.pumpAndSettle();
      await t.tap(find.text('فرع اصطناعي').last);
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('تسجيل حضوري'),
        200,
        scrollable: find.byType(Scrollable).first,
      );
      expect(
        t
            .widget<FilledButton>(
              find.widgetWithText(FilledButton, 'تسجيل حضوري'),
            )
            .onPressed,
        isNotNull,
      );
      expect(location.captures, 0);
      revoked = true;
      await t.ensureVisible(find.text('تحديث حالة الحضور'));
      await t.tap(find.text('تحديث حالة الحضور'));
      await t.pumpAndSettle();
      expect(find.byType(DropdownButtonFormField<String>), findsNothing);
      expect(find.text('تسجيل حضوري'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
