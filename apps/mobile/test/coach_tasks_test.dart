import 'support/infinite_list.dart';

import 'package:academy_mobile/features/shared/coach_tasks.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

const task = {
  'id': 'task',
  'kind': 'Attendance',
  'group': {'id': 'group', 'arabicName': 'مجموعة تجريبية'},
  'assignedToMembershipId': 'coach',
  'state': 'Assigned',
  'version': 7,
  'note': 'ملاحظة محفوظة',
  'dueOn': '2026-10-25',
  'history': <Object>[],
};
Future<void> tap(WidgetTester t, String label) async {
  final f = find.text(label);
  await t.scrollUntilVisible(f, 150, scrollable: find.byType(Scrollable).first);
  await t.tap(f);
  await t.pumpAndSettle();
}

void main() {
  for (final explicit in [false, true]) {
    testWidgets(
      'Admin entry requires explicit coach task permission: $explicit',
      (t) async {
        final auth = await authenticate('AcademyAdmin', (m, p, b, token) async {
          if (p.endsWith('my-permissions')) return ['followups.read'];
          return {
            'useActionGrants': true,
            'actions': [
              {'module': 'followups', 'read': true, 'add': false, 'edit': true},
            ],
            'workflowActions': [
              {
                'key': coachTaskPermission,
                'hasExplicitGrant': explicit,
                'allowed': true,
              },
            ],
          };
        });
        await t.pumpWidget(app(CoachTasksEntry(auth: auth)));
        await t.pumpAndSettle();
        expect(
          find.text('مهام الحضور والتقييم'),
          explicit ? findsOneWidget : findsNothing,
        );
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  testWidgets(
    'Coach updates task with existing note and date, reads confirmation and never writes attendance or evaluation',
    (t) async {
      var state = 'Assigned';
      final writes = <String>[];
      final auth = await authenticate('Coach', (m, p, b, token) async {
        if (m == 'POST') {
          writes.add(p);
          final body = b as Map;
          expect(body['version'], 7);
          expect(body['note'], 'ملاحظة محفوظة');
          expect(body['dueOn'], '2026-10-25');
          expect(body['requestId'], isNotEmpty);
          state = body['state'] as String;
          return {'id': 'task'};
        }
        return {...task, 'state': state};
      });
      await t.pumpWidget(app(CoachTasksPage(auth: auth, id: 'task')));
      await t.pumpAndSettle();
      expect(find.text('فتح المجموعة للحضور والتقييم'), findsOneWidget);
      await tap(t, 'حفظ حالة المهمة');
      expect(writes, ['/api/v1/my-coach-tasks/task']);
      expect(find.text('حُفظت المهمة وأُعيدت قراءتها.'), findsOneWidget);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Manager creates only allowed task kind with selected group-coach pair and paged options',
    (t) async {
      final requests = <String>[];
      var writes = 0;
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        requests.add(p);
        if (m == 'POST') {
          writes++;
          final body = b as Map;
          expect(body['kind'], 'Attendance');
          expect(body['groupId'], 'group');
          expect(body['coachId'], 'coach');
          return {'id': 'task'};
        }
        if (p.contains('/options')) {
          return {
            'items': [
              {
                'groupId': 'group',
                'group': 'مجموعة تجريبية',
                'coachId': 'coach',
                'coach': 'مدرب تجريبي',
                'branch': 'فرع تجريبي',
                'sport': 'كرة القدم',
              },
            ],
            'page': 1,
            'count': 26,
            'hasMore': true,
          };
        }
        return task;
      });
      await t.pumpWidget(app(CoachTasksPage(auth: auth, create: true)));
      await t.pumpAndSettle();
      await scrollForNextPage(t);
      expect(requests.any((p) => p.contains('page=2')), true);
      await tap(t, 'مجموعة تجريبية · مدرب تجريبي');
      await tap(t, 'تأكيد الإسناد');
      expect(writes, 1);
      expect(requests.last, '/api/v1/manage/coach-tasks/task');
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets('Closed task shows history and no update form', (t) async {
    final auth = await authenticate(
      'Coach',
      (m, p, b, token) async => {
        ...task,
        'state': 'Closed',
        'resolutionReason': 'انتهت المتابعة',
      },
    );
    await t.pumpWidget(app(CoachTasksPage(auth: auth, id: 'task')));
    await t.pumpAndSettle();
    expect(find.text('حفظ حالة المهمة'), findsNothing);
    expect(find.text('انتهت المتابعة'), findsOneWidget);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  testWidgets(
    'Ambiguous task save retries same request and version; logout removes private task',
    (t) async {
      final keys = <String>[];
      var state = 'Assigned';
      final auth = await authenticate('Coach', (m, p, b, token) async {
        if (m == 'POST') {
          keys.add((b as Map)['requestId'] as String);
          expect(b['version'], 7);
          state = 'InProgress';
          if (keys.length == 1) {
            throw const ApiFailure(0, 'تعذر تأكيد الاتصال. أعد المحاولة.');
          }
          return {'id': 'task', 'replay': true};
        }
        return {...task, 'state': state};
      });
      await t.pumpWidget(app(CoachTasksPage(auth: auth, id: 'task')));
      await t.pumpAndSettle();
      await tap(t, 'حفظ حالة المهمة');
      expect(find.text('حُفظت المهمة وأُعيدت قراءتها.'), findsNothing);
      await tap(t, 'حفظ حالة المهمة');
      expect(keys.length, 2);
      expect(keys[0], keys[1]);
      expect(find.text('حُفظت المهمة وأُعيدت قراءتها.'), findsOneWidget);
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('ملاحظة محفوظة'), findsNothing);
      expect(find.text('فتح المجموعة للحضور والتقييم'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
