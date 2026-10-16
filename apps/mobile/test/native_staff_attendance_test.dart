import 'dart:async';
import 'dart:convert';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/staff_passkey_provider.dart';
import 'package:academy_mobile/features/shared/native_staff_attendance.dart';
import 'package:academy_mobile/features/shared/staff_passkeys.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'coach_test.dart' show coach, shell;

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

void main() {
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
    expect(flow.error, contains('الطلب نفسه'));
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
      expect(
        find.text('تم تسجيل وسيلة التحقق على الخادم؛ لم يسجل حضور.'),
        findsOneWidget,
      );
      await t.scrollUntilVisible(
        find.text('تجربة وسيلة التحقق'),
        150,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(find.text('تجربة وسيلة التحقق'));
      await t.pumpAndSettle();
      expect(
        find.text('تم التحقق من الوسيلة؛ هذا التحقق ليس تسجيل حضور.'),
        findsOneWidget,
      );
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
          find.textContaining('الإعداد المحلي الحالي لا يكفي'),
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
      expect(
        t
            .widget<FilledButton>(
              find.widgetWithText(FilledButton, 'تسجيل حضوري'),
            )
            .onPressed,
        isNull,
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
