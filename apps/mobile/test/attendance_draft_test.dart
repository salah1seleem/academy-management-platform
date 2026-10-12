import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/shared/attendance_draft.dart';
import 'package:flutter_test/flutter_test.dart';

Map<String, dynamic> snapshot({
  String scope = 'session-a',
  String status = 'NotRecorded',
  int version = 0,
  bool manage = true,
}) => {
  'draftLease': 'lease',
  'draftScope': scope,
  'canManageAttendance': manage,
  'players': [
    {'sportEnrollmentId': 'e', 'attendanceStatus': status, 'version': version},
    {
      'sportEnrollmentId': 'unchanged',
      'attendanceStatus': 'Present',
      'version': 2,
    },
  ],
};
void main() {
  final now = DateTime.utc(2026, 10, 12);
  AttendanceDraft draft() => AttendanceDraft(snapshot(), {
    'e': 'Late',
    'unchanged': 'Present',
  }, now: now);
  test(
    'queues only changed rows and carries original versions and short lease',
    () {
      final d = draft();
      expect(d.body['draftLease'], 'lease');
      expect(d.changes.length, 1);
      expect(d.changes['e']!['expectedVersion'], 0);
      d.validate(now: now.add(const Duration(minutes: 14)));
      expect(
        () => d.validate(now: now.add(const Duration(minutes: 15))),
        throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 410)),
      );
    },
  );
  test('lost response is confirmed without replay; another correction requires choice', () {
    final d = draft();
    expect(d.confirmed(snapshot(status: 'Late', version: 5)), isTrue);
    expect(d.conflicts(snapshot(status: 'Late', version: 5)), isEmpty);
    final conflict = d.conflicts(snapshot(status: 'Excused', version: 8));
    expect(conflict.keys, ['e']);
    d.resolve('e', conflict['e'], true);
    expect(d.changes['e']!['expectedVersion'], 8);
    expect(d.conflicts(snapshot(status: 'Excused', version: 8)), isEmpty);
  });
  test('remote choice removes local change; removed roster member cannot be rebased', () {
    final d = draft();
    d.resolve('e', null, true);
    expect(d.changes.length, 1);
    final latest = snapshot()..['players'] = [];
    expect(d.conflicts(latest).containsKey('e'), true);
    d.resolve('e', null, false);
    expect(d.changes, isEmpty);
  });
  test(
    'changed login or academy, revoked permission and lease expiry fail closed',
    () {
      final d = draft();
      expect(
        () => d.validate(
          latest: snapshot(scope: 'another-session'),
          now: now,
        ),
        throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 401)),
      );
      expect(
        () => d.validate(latest: snapshot(manage: false), now: now),
        throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 403)),
      );
      final limited = AttendanceDraft(
        snapshot()
          ..['draftExpiresAtUtc'] = now
              .add(const Duration(minutes: 2))
              .toIso8601String(),
        {'e': 'Absent', 'unchanged': 'Present'},
        now: now,
      );
      expect(
        () => limited.validate(now: now.add(const Duration(minutes: 3))),
        throwsA(isA<ApiFailure>()),
      );
    },
  );
}
