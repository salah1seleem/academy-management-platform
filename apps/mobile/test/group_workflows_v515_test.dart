import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/group_schedule.dart';
import 'package:academy_mobile/features/owner/native_staff_management.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

void main() {
  test('Arabic twelve-hour time preserves morning, noon and midnight', () {
    expect(trainingTimeOf('16:30:00'), '4:30 مساءً');
    expect(trainingTimeOf('12:00'), '12:00 مساءً');
    expect(trainingTimeOf('00:15'), '12:15 صباحًا');
    expect(trainingTimeOf('09:05'), '9:05 صباحًا');
  });
  testWidgets(
    'Recurring days share one range without generating dated occurrences',
    (t) async {
      await t.pumpWidget(
        app(
          const WeeklyGroupSchedule(
            schedules: [
              {
                'dayOfWeek': 'Saturday',
                'startTime': '16:30',
                'endTime': '17:30',
              },
              {'dayOfWeek': 'Monday', 'startTime': '16:30', 'endTime': '17:30'},
              {
                'dayOfWeek': 'Wednesday',
                'startTime': '16:30',
                'endTime': '17:30',
              },
            ],
          ),
        ),
      );
      expect(find.text('السبت'), findsOneWidget);
      expect(find.text('الاثنين'), findsOneWidget);
      expect(find.text('الأربعاء'), findsOneWidget);
      expect(find.text('من 4:30 مساءً إلى 5:30 مساءً'), findsOneWidget);
      expect(t.takeException(), isNull);
    },
  );
  for (final deny in [false, true]) {
    testWidgets(
      'Scrolling appends next page and redacts on revoked access deny=$deny',
      (t) async {
        final paths = <String>[];
        final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
          paths.add(p);
          final page = Uri.parse(p).queryParameters['page'];
          if (page == '2' && deny) throw const ApiFailure(403, 'تغيرت صلاحيتك');
          return {
            'page': int.parse(page!),
            'pageSize': 5,
            'total': 10,
            'items': [
              for (var i = 0; i < 5; i++)
                {'name': 'لاعب ${page == '1' ? i : i + 5}'},
            ],
          };
        });
        await t.pumpWidget(
          app(
            RemoteBody(
              auth: auth,
              path: '/list?page=1&pageSize=5&search=أحمد',
              builder: (c, v) => ListView(
                children: [
                  for (final row in rows(obj(v)['items']))
                    SizedBox(height: 150, child: Text(row['name'] as String)),
                ],
              ),
            ),
          ),
        );
        await t.pumpAndSettle();
        await t.drag(find.byType(ListView), const Offset(0, -650));
        await t.pumpAndSettle();
        expect(paths.length, 2);
        expect(Uri.parse(paths.last).queryParameters['search'], 'أحمد');
        if (deny) {
          expect(find.text('تغيرت صلاحيتك'), findsOneWidget);
          expect(find.text('لاعب 0'), findsNothing);
        } else {
          await t.drag(find.byType(ListView), const Offset(0, -350));
          await t.pumpAndSettle();
          expect(find.text('لاعب 5'), findsOneWidget);
          await t.drag(find.byType(ListView), const Offset(0, 1300));
          await t.pumpAndSettle();
          expect(find.text('لاعب 0'), findsOneWidget);
        }
        expect(t.takeException(), isNull);
      },
    );
  }
  testWidgets('Owner staff directory loads even when payroll is unavailable', (
    t,
  ) async {
    final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
      if (p == '/api/v1/manage/employees') {
        throw const ApiFailure(403, 'ليست لديك صلاحية الرواتب');
      }
      if (p == '/api/v1/manage/staff') {
        return {
          'members': [
            {
              'id': 'staff',
              'name': 'موظف عرض',
              'role': 'AcademyAdmin',
              'isActive': true,
            },
          ],
        };
      }
      throw StateError(p);
    });
    await t.pumpWidget(app(NativeStaffManagement(auth: auth)));
    await t.pumpAndSettle();
    expect(find.text('موظف عرض'), findsOneWidget);
    expect(find.text('ليست لديك صلاحية الرواتب'), findsNothing);
    expect(find.text('إضافة موظف'), findsOneWidget);
    expect(t.takeException(), isNull);
  });
}
