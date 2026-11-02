import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter_test/flutter_test.dart';

/// Synthetic fixture setup through the ordinary authorized API. No raw SQL,
/// fake attendance, payments, weakened eligibility, or changes to seed groups.
Future<Json> preparePlacementFixture(AuthController auth) async {
  expect(auth.session!.selected!.role, 'AcademyOwner');
  expect(
    auth.session!.selected!.academyId,
    '30000000-0000-0000-0000-000000000003',
  );
  const sourceGroup = '428e9073-f93c-14ae-d9d5-c38e50278314';
  const groupName = 'مجموعة قبول نقل وتعويض اصطناعية2016';
  const levelName = 'مستوى قبول اصطناعي2016';
  final source = obj(
    await auth.request('GET', '/api/v1/manage/groups/$sourceGroup/details'),
  );
  final original = obj(source['group']);
  String idOf(Object? v) => v is String ? v : textOf(obj(v)['id']);
  final levels = rows(
    await auth.request('GET', '/api/v1/manage/training-levels'),
  );
  var level = levels
      .where(
        (l) =>
            l['arabicName'] == levelName && l['sportId'] == original['sportId'],
      )
      .firstOrNull;
  final levelId = level == null
      ? idOf(
          await auth.request(
            'POST',
            '/api/v1/manage/training-levels',
            body: {
              'arabicName': levelName,
              'sportId': original['sportId'],
              'rank': 1,
            },
          ),
        )
      : textOf(level['id']);
  final options = obj(
    await auth.request('GET', '/api/v1/manage/structure/options'),
  );
  final groups = rows(options['groups'])
      .where((g) => g['arabicName'] == groupName)
      .toList();
  expect(groups.length, lessThanOrEqualTo(1));
  final groupId = groups.isEmpty
      ? idOf(
          await auth.request(
            'POST',
            '/api/v1/manage/groups',
            body: {
              'arabicName': groupName,
              'branchId': original['branchId'],
              'sportId': original['sportId'],
              'ageCategoryId': '9041294d-2aa4-d903-0aca-906b5c3ade4f',
              'capacity': 20,
            },
          ),
        )
      : textOf(groups.single['id']);
  var detail = obj(
    await auth.request('GET', '/api/v1/manage/groups/$groupId/details'),
  );
  if (obj(detail['group'])['trainingLevelId'] != levelId) {
    await auth.request(
      'PUT',
      '/api/v1/manage/groups/$groupId/placement-level',
      body: {'levelId': levelId},
    );
  }
  final coach = rows(source['coaches']).first['id'];
  if (!rows(detail['coaches']).any((c) => c['id'] == coach)) {
    await auth.request(
      'POST',
      '/api/v1/manage/groups/$groupId/coaches',
      body: {'membershipId': coach},
    );
  }
  if (rows(detail['sessions']).isEmpty) {
    final timetable = {
      'from': '2026-10-29',
      'to': '2026-11-12',
      'days': [4],
      'startTime': '18:00:00',
      'endTime': '19:00:00',
    };
    final preview = obj(
      await auth.request(
        'POST',
        '/api/v1/manage/groups/$groupId/timetable/preview',
        body: timetable,
      ),
    );
    expect(preview['conflicts'], isEmpty);
    await auth.request(
      'POST',
      '/api/v1/manage/groups/$groupId/timetable',
      body: {...timetable, 'previewFingerprint': preview['fingerprint']},
      idempotencyKey: 'v45-native-placement-2016-calendar',
    );
  }
  detail = obj(
    await auth.request('GET', '/api/v1/manage/groups/$groupId/details'),
  );
  expect(obj(detail['group'])['trainingLevelId'], levelId);
  expect(rows(detail['sessions']), hasLength(3));
  final session = rows(detail['sessions'])
      .singleWhere((s) => s['sessionDate'] == '2026-10-29');
  final result = {
    'id': groupId,
    'arabicName': groupName,
    'levelId': levelId,
    'targetSessionId': session['id'],
    'sourceGroupId': sourceGroup,
  };
  // Return reference IDs only; this is fixture preparation, not UI acceptance.
  return result;
}

Future<void> reviewFixturePlayerLevel(
  AuthController auth,
  String enrollment,
  String levelId,
) async {
  final path = '/api/v1/people/enrollments/$enrollment/placement-level';
  final before = obj(await auth.request('GET', path));
  if (before['trainingLevelId'] != levelId) {
    await auth.request(
      'PUT',
      path,
      body: {
        'levelId': levelId,
        'note': 'مراجعة مستوى لسيناريو قبول اصطناعي فقط',
      },
    );
  }
  expect(obj(await auth.request('GET', path))['trainingLevelId'], levelId);
}
