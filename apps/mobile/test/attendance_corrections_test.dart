import 'support/infinite_list.dart';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/attendance_corrections.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show app, authenticate;

Json correction({
  String state = 'Pending',
  Object? reason,
  bool canApprove = true,
}) => {
  'id': 'request-1',
  'version': 7,
  'trainingSessionId': 'session-1',
  'sessionDate': '2026-10-21',
  'group': 'مجموعة تدريب اصطناعية',
  'kind': 'Players',
  'state': state,
  'reason': 'تصحيح سجل ورقي اصطناعي محفوظ للمراجعة',
  'requester': 'إداري اختبار',
  'reviewer': state == 'Pending' ? null : 'مالك الاختبار',
  'decisionReason': reason,
  'canApprove': state == 'Pending' && canApprove,
  'appliedAtUtc': state == 'Approved' ? '2026-10-22T10:00:00Z' : null,
  'items': [
    {
      'subjectId': 'enrollment-1',
      'name': 'لاعب اختبار باسم طويل واضح للمراجعة',
      'before': 'Absent',
      'after': 'NotRecorded',
    },
  ],
};

Future<void> reveal(WidgetTester tester, Finder finder) async {
  await tester.scrollUntilVisible(
    finder,
    160,
    scrollable: find.byType(Scrollable).first,
    maxScrolls: 80,
  );
  await tester.ensureVisible(finder);
  await tester.pumpAndSettle();
}

void main() {
  testWidgets(
    'history reaches older pages and retains the visible page after a failed read',
    (t) async {
      var fail = true;
      final calls = <String>[];
      final auth = await authenticate('AcademyAdmin', (m, p, b, token) async {
        calls.add(p);
        if (p == '/api/v1/my-permissions') return ['attendance.read'];
        if (p == '/api/v1/my-access') return <String, dynamic>{};
        if (p.contains('/corrections?')) {
          final page = int.parse(Uri.parse(p).queryParameters['page']!);
          if (page == 2 && fail) {
            fail = false;
            throw const ApiFailure(503, 'تعذر تحميل الصفحة التالية.');
          }
          return {
            'page': page,
            'pageSize': 25,
            'total': 2,
            'items': [
              {
                ...correction(canApprove: false),
                'id': 'request-$page',
                'group': 'مجموعة الصفحة $page',
              },
            ],
          };
        }
        return correction(state: 'Rejected', canApprove: false);
      });
      await t.pumpWidget(app(AttendanceCorrectionsPage(auth: auth)));
      await t.pumpAndSettle();
      expect(find.text('مجموعة الصفحة 1'), findsOneWidget);
      await scrollForNextPage(t);
      await t.pumpAndSettle();
      expect(find.text('مجموعة الصفحة 1'), findsOneWidget);
      await t.tap(find.text('تحديث الطلبات'));
      await t.pumpAndSettle();
      expect(find.text('إجمالي الطلبات 2'), findsOneWidget);
      await scrollForNextPage(t);
      await t.pumpAndSettle();
      expect(find.text('مجموعة الصفحة 2'), findsOneWidget);
      expect(find.text('مجموعة الصفحة 1'), findsOneWidget);
      expect(find.text('الصفحة التالية'), findsNothing);
      await t.tap(find.text('مجموعة الصفحة 2'));
      await t.pumpAndSettle();
      expect(find.byType(AttendanceCorrectionDetail), findsOneWidget);
      expect(calls, contains('/api/v1/attendance/corrections/request-2'));
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets('history clears on loss of authorization', (t) async {
    final auth = await authenticate(
      'AcademyOwner',
      (m, p, b, token) async => {
        'page': 1,
        'pageSize': 25,
        'total': 1,
        'items': [correction()],
      },
    );
    await t.pumpWidget(app(AttendanceCorrectionsPage(auth: auth)));
    await t.pumpAndSettle();
    expect(find.text('مجموعة تدريب اصطناعية'), findsOneWidget);
    await auth.logout();
    await t.pumpAndSettle();
    expect(find.text('مجموعة تدريب اصطناعية'), findsNothing);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  testWidgets(
    'reject reads the saved rejection and never claims attendance was applied',
    (t) async {
      var stored = false;
      final bodies = <Object?>[];
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        if (m == 'POST') {
          expect(p, endsWith('/reject'));
          bodies.add(b);
          stored = true;
          return {'state': 'Rejected'};
        }
        return correction(state: stored ? 'Rejected' : 'Pending');
      });
      await t.pumpWidget(
        app(AttendanceCorrectionDetail(auth: auth, id: 'request-1')),
      );
      await t.pumpAndSettle();
      await reveal(t, find.text('عدم الاعتماد'));
      await t.tap(find.text('عدم الاعتماد'));
      await t.pumpAndSettle();
      await t.tap(find.text('تأكيد القرار'));
      await t.pumpAndSettle();
      await reveal(
        t,
        find.text('لم يُعتمد الطلب؛ تأكدت حالته دون تغيير الحضور.'),
      );
      expect(bodies, [
        {'expectedVersion': 7, 'reason': null},
      ]);
      expect(
        find.text('اعتمد التصحيح وتأكد تطبيقه على السجل المحفوظ.'),
        findsNothing,
      );
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets('decision notice opens the exact authorized correction source', (
    t,
  ) async {
    final calls = <String>[];
    final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
      calls.add(p);
      if (p.contains('/notifications')) {
        return {
          'total': 1,
          'unread': 1,
          'items': [
            {
              'id': 'notice-1',
              'message': 'اعتمد تصحيح الحضور',
              'targetKind': 'AttendanceCorrection',
              'targetId': 'request-1',
              'createdAtUtc': '2026-10-22',
              'isRead': false,
            },
          ],
        };
      }
      return correction(state: 'Approved');
    });
    await t.pumpWidget(app(NotificationInbox(auth: auth)));
    await t.pumpAndSettle();
    await t.tap(find.text('عرض طلب تصحيح الحضور'));
    await t.pumpAndSettle();
    expect(
      t
          .widget<AttendanceCorrectionDetail>(
            find.byType(AttendanceCorrectionDetail),
          )
          .id,
      'request-1',
    );
    expect(calls, contains('/api/v1/attendance/corrections/request-1'));
    await reveal(t, find.text('تصحيح سجل ورقي اصطناعي محفوظ للمراجعة'));
    expect(find.text('اعتماد وتطبيق التصحيح'), findsNothing);
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  for (final width in [375.0, 440.0]) {
    for (final scale in [1.0, 2.0]) {
      for (final dark in [false, true]) {
        testWidgets(
          'history page controls width=$width scale=$scale dark=$dark remain usable',
          (t) async {
            t.view.physicalSize = Size(width, 812);
            t.view.devicePixelRatio = 1;
            addTearDown(t.view.resetPhysicalSize);
            addTearDown(t.view.resetDevicePixelRatio);
            final auth = await authenticate('AcademyOwner', (
              m,
              p,
              b,
              token,
            ) async {
              final page = int.parse(Uri.parse(p).queryParameters['page']!);
              return {
                'page': page,
                'pageSize': 25,
                'total': 2,
                'items': [
                  {
                    ...correction(),
                    'id': 'request-$page',
                    'group': 'مجموعة الصفحة $page',
                  },
                ],
              };
            });
            await t.pumpWidget(
              app(
                Theme(
                  data: dark ? AcademyTheme.dark() : AcademyTheme.light(),
                  child: AttendanceCorrectionsPage(auth: auth),
                ),
                scale: scale,
              ),
            );
            await t.pumpAndSettle();
            await scrollForNextPage(t);
            await t.pumpAndSettle();
            await reveal(t, find.text('مجموعة الصفحة 2'));
            expect(find.text('مجموعة الصفحة 2'), findsOneWidget);
            expect(t.takeException(), isNull);
            await t.pumpWidget(const SizedBox());
            auth.dispose();
          },
        );
      }
    }
  }
  for (final width in [375.0, 440.0]) {
    for (final scale in [1.0, 2.0]) {
      for (final dark in [false, true]) {
        testWidgets(
          'correction review width=$width scale=$scale dark=$dark keeps source and actions readable',
          (t) async {
            t.view.physicalSize = Size(width, 812);
            t.view.devicePixelRatio = 1;
            addTearDown(t.view.resetPhysicalSize);
            addTearDown(t.view.resetDevicePixelRatio);
            final auth = await authenticate(
              'AcademyOwner',
              (m, p, b, token) async => correction(),
            );
            await t.pumpWidget(
              app(
                Theme(
                  data: dark ? AcademyTheme.dark() : AcademyTheme.light(),
                  child: AttendanceCorrectionDetail(
                    auth: auth,
                    id: 'request-1',
                  ),
                ),
                scale: scale,
              ),
            );
            await t.pumpAndSettle();
            final requester = t.widget<UiText>(
              find.widgetWithText(UiText, 'إداري اختبار'),
            );
            expect(requester.style?.fontSize, 14);
            expect(
              requester.style?.color,
              (dark ? AcademyTheme.dark() : AcademyTheme.light())
                  .colorScheme
                  .onSurface,
            );
            await reveal(t, find.text('تصحيح سجل ورقي اصطناعي محفوظ للمراجعة'));
            expect(
              find.text('تصحيح سجل ورقي اصطناعي محفوظ للمراجعة'),
              findsOneWidget,
            );
            await reveal(t, find.text('المسجل: غائب'));
            expect(find.text('المسجل: غائب'), findsOneWidget);
            expect(find.text('المطلوب: لم يُسجل'), findsOneWidget);
            await reveal(t, find.text('عدم الاعتماد'));
            expect(find.text('اعتماد وتطبيق التصحيح'), findsOneWidget);
            expect(t.takeException(), isNull);
            await t.pumpWidget(const SizedBox());
            auth.dispose();
          },
        );
      }
    }
  }

  for (final lostResponse in [false, true]) {
    testWidgets(
      'decision readback/response loss retries the same snapshot, lostResponse=$lostResponse',
      (t) async {
        var stored = false, posts = 0, failRead = true;
        final bodies = <Object?>[];
        final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
          if (m == 'POST') {
            bodies.add(b);
            posts++;
            stored = true;
            if (lostResponse && posts == 1) {
              throw const ApiFailure(503, 'تعذر تأكيد الاستجابة.');
            }
            return {'id': 'request-1', 'state': 'Approved'};
          }
          if (stored && !lostResponse && failRead) {
            failRead = false;
            throw const ApiFailure(503, 'تعذرت قراءة القرار.');
          }
          return correction(
            state: stored ? 'Approved' : 'Pending',
            reason: stored ? 'راجعت المصدر' : null,
          );
        });
        await t.pumpWidget(
          app(AttendanceCorrectionDetail(auth: auth, id: 'request-1')),
        );
        await t.pumpAndSettle();
        await reveal(t, find.byType(TextFormField));
        await t.enterText(find.byType(TextFormField), 'راجعت المصدر');
        await reveal(t, find.text('اعتماد وتطبيق التصحيح'));
        await t.tap(find.text('اعتماد وتطبيق التصحيح'));
        await t.pumpAndSettle();
        await t.tap(find.text('تأكيد القرار'));
        await t.pumpAndSettle();
        expect(
          find.text('اعتمد التصحيح وتأكد تطبيقه على السجل المحفوظ.'),
          findsNothing,
        );
        expect(find.text('عدم الاعتماد'), findsNothing);
        await reveal(t, find.text('إعادة التحقق من القرار'));
        await t.tap(find.text('إعادة التحقق من القرار'));
        await t.pumpAndSettle();
        expect(
          find.text('اعتمد التصحيح وتأكد تطبيقه على السجل المحفوظ.'),
          findsOneWidget,
        );
        expect(posts, lostResponse ? 2 : 1);
        for (final body in bodies) {
          expect(body, {'expectedVersion': 7, 'reason': 'راجعت المصدر'});
        }
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  testWidgets('a mismatched saved decision never displays success', (t) async {
    var stored = false;
    final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
      if (m == 'POST') {
        stored = true;
        return {};
      }
      return correction(
        state: stored ? 'Approved' : 'Pending',
        reason: stored ? 'قرار آخر' : null,
      );
    });
    await t.pumpWidget(
      app(AttendanceCorrectionDetail(auth: auth, id: 'request-1')),
    );
    await t.pumpAndSettle();
    await reveal(t, find.text('اعتماد وتطبيق التصحيح'));
    await t.tap(find.text('اعتماد وتطبيق التصحيح'));
    await t.pumpAndSettle();
    await t.tap(find.text('تأكيد القرار'));
    await t.pumpAndSettle();
    expect(
      find.text('اعتمد التصحيح وتأكد تطبيقه على السجل المحفوظ.'),
      findsNothing,
    );
    expect(
      find.text('لم يتأكد القرار من السجل المحفوظ. حدّث الطلب وراجع حالته.'),
      findsOneWidget,
    );
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  testWidgets(
    'administrator reads own request without approval even if a response claims canApprove',
    (t) async {
      final auth = await authenticate('AcademyAdmin', (m, p, b, token) async {
        if (p.endsWith('/my-permissions')) return ['attendance.read'];
        if (p.endsWith('/my-access')) {
          return {
            'useActionGrants': true,
            'actions': [
              {'module': 'attendance', 'read': true, 'edit': false},
            ],
          };
        }
        return correction();
      });
      await t.pumpWidget(
        app(AttendanceCorrectionDetail(auth: auth, id: 'request-1')),
      );
      await t.pumpAndSettle();
      await reveal(t, find.text('تصحيح سجل ورقي اصطناعي محفوظ للمراجعة'));
      expect(
        find.text('تصحيح سجل ورقي اصطناعي محفوظ للمراجعة'),
        findsOneWidget,
      );
      expect(find.text('اعتماد وتطبيق التصحيح'), findsNothing);
      expect(find.text('عدم الاعتماد'), findsNothing);
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('تصحيح سجل ورقي اصطناعي محفوظ للمراجعة'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  for (final role in ['Coach', 'Guardian']) {
    testWidgets('$role cannot read a correction or discover its entry', (
      t,
    ) async {
      final requests = <String>[];
      final auth = await authenticate(role, (m, p, b, token) async {
        requests.add(p);
        return correction();
      });
      await t.pumpWidget(
        app(
          Column(
            children: [
              AttendanceCorrectionsEntry(auth: auth),
              Expanded(
                child: AttendanceCorrectionDetail(auth: auth, id: 'request-1'),
              ),
            ],
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(
        requests.any((p) => p.contains('/attendance/corrections/')),
        false,
      );
      expect(find.text('طلبات تصحيح الحضور'), findsNothing);
      expect(find.text('تصحيح سجل ورقي اصطناعي محفوظ للمراجعة'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    });
  }
}
