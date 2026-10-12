import '../../core/api/api_client.dart';
import '../guardian/guardian_ui.dart';

// Short-lived page memory only. Never persist the roster, lease or decisions.
class AttendanceDraft {
  final String? lease, scope;
  final DateTime expires;
  final Map<String, Map<String, dynamic>> changes;
  AttendanceDraft(
    Map<String, dynamic> snapshot,
    Map<String, String> states, {
    DateTime? now,
  }) : lease = snapshot['draftLease'] as String?,
       scope = snapshot['draftScope'] as String?,
       expires = _expiry(snapshot, now ?? DateTime.now()),
       changes = {
         for (final row in rows(snapshot['players']))
           if (states[textOf(row['sportEnrollmentId'])] !=
               row['attendanceStatus'])
             textOf(row['sportEnrollmentId']): {
               'sportEnrollmentId': row['sportEnrollmentId'],
               'status': states[textOf(row['sportEnrollmentId'])],
               'expectedVersion': row['version'] ?? 0,
             },
       };
  static DateTime _expiry(Map<String, dynamic> snapshot, DateTime now) {
    final local = now.add(const Duration(minutes: 15));
    final server = DateTime.tryParse(
      snapshot['draftExpiresAtUtc'] as String? ?? '',
    );
    return server != null && server.isBefore(local) ? server : local;
  }

  void validate({Map<String, dynamic>? latest, DateTime? now}) {
    if (!expires.isAfter(now ?? DateTime.now())) {
      throw const ApiFailure(410, 'انتهت صلاحية المسودة؛ افتح كشفًا جديدًا.');
    }
    if (latest == null) return;
    if (scope != latest['draftScope']) {
      throw const ApiFailure(
        401,
        'تغير حساب الدخول أو الأكاديمية؛ حُذفت المسودة.',
      );
    }
    if (latest['canManageAttendance'] != true) {
      throw const ApiFailure(403, 'لم يعد تعديل هذه الحصة متاحًا.');
    }
  }

  Map<String, Map<String, dynamic>?> conflicts(Map<String, dynamic> latest) {
    final remote = {
      for (final r in rows(latest['players']))
        textOf(r['sportEnrollmentId']): r,
    };
    return {
      for (final entry in changes.entries)
        if (remote[entry.key] == null ||
            remote[entry.key]!['attendanceStatus'] != entry.value['status'] &&
                (remote[entry.key]!['version'] ?? 0) !=
                    entry.value['expectedVersion'])
          entry.key: remote[entry.key],
    };
  }

  bool confirmed(Map<String, dynamic> latest) => changes.entries.every(
    (e) => rows(latest['players']).any(
      (r) =>
          r['sportEnrollmentId'] == e.key &&
          r['attendanceStatus'] == e.value['status'],
    ),
  );
  void resolve(String id, Map<String, dynamic>? remote, bool keepLocal) {
    if (!keepLocal) {
      changes.remove(id);
      return;
    }
    if (remote != null && changes.containsKey(id)) {
      changes[id]!['expectedVersion'] = remote['version'] ?? 0;
    }
  }

  Map<String, dynamic> get body => {
    if (lease != null) 'draftLease': lease,
    'items': changes.values.toList(),
  };
}
