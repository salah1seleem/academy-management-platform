import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/coach/coach_attendance.dart';
import 'package:academy_mobile/features/coach/coach_home.dart';
import 'package:academy_mobile/features/coach/coach_player.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'coach_test.dart' show coach, shell;

Json player(int n, [String state = 'NotRecorded']) => {
  'id': 'e$n',
  'sportEnrollmentId': 'e$n',
  'groupId': 'g1',
  'player': 'لاعب التجربة $n',
  'arabicName': 'لاعب التجربة $n',
  'playerCode': 'FB-$n',
  'attendanceStatus': state,
  'version': n,
  'sport': 'كرة القدم',
  'group': 'براعم الاختبار',
};
Json roster({bool editable = false, List<Json>? players}) => {
  'canManageAttendance': editable,
  'canEvaluate': true,
  'draftLease': 'lease',
  'draftScope': 'scope',
  'session': {
    'group': 'براعم الاختبار',
    'sessionDate': '2026-10-15',
    'startTime': '18:00:00',
    'endTime': '19:00:00',
    'branch': 'مدينة نصر',
    'sport': 'كرة القدم',
    'status': 'Scheduled',
  },
  'players': players ?? [player(1), player(2, 'Absent'), player(3, 'Late')],
};

Future<void> reach(WidgetTester tester, Finder finder) async {
  await tester.scrollUntilVisible(
    finder,
    200,
    scrollable: find.byType(Scrollable).first,
  );
  await tester.ensureVisible(finder);
  await tester.pumpAndSettle();
}

void main() {
  testWidgets(
    'Search is local, counters include the whole roster, clear restores all and never writes',
    (tester) async {
      var reads = 0, writes = 0;
      final auth = await coach((m, p, b, t) async {
        m == 'GET' ? reads++ : writes++;
        return roster();
      });
      await tester.pumpWidget(
        shell(CoachAttendance(auth: auth, sessionId: 'actual')),
      );
      await tester.pumpAndSettle();
      expect(find.text('غائب · 1'), findsOneWidget);
      expect(find.text('لم يُسجل · 1'), findsOneWidget);
      expect(find.text('متأخر · 1'), findsOneWidget);
      await reach(tester, find.byType(TextField));
      await tester.enterText(find.byType(TextField), 'fb-2');
      await tester.pumpAndSettle();
      expect(find.text('1 من 3 لاعب'), findsOneWidget);
      expect(find.text('لاعب التجربة 2'), findsOneWidget);
      expect(find.text('لاعب التجربة 1'), findsNothing);
      await tester.enterText(find.byType(TextField), 'غير موجود');
      await tester.pumpAndSettle();
      expect(find.text('لا توجد نتائج لهذا البحث.'), findsOneWidget);
      await tester.tap(find.byTooltip('مسح البحث'));
      await tester.pumpAndSettle();
      expect(find.text('3 من 3 لاعب'), findsOneWidget);
      expect(reads, 1);
      expect(writes, 0);
      expect(find.byType(FilterChip), findsNothing);
      expect(find.byType(PopupMenuButton<String>), findsNothing);
      expect(find.text('حفظ الحضور'), findsNothing);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'Filtered decisions persist together with source versions; loading prevents double send and reload confirms',
    (tester) async {
      final remote = [player(1), player(2)];
      final gate = Completer<Object?>();
      Json? sent;
      var writes = 0;
      final auth = await coach((m, p, b, t) async {
        if (m == 'PUT') {
          writes++;
          sent = b as Json;
          await gate.future;
          for (final item in rows(sent!['items'])) {
            remote.firstWhere(
              (p) => p['sportEnrollmentId'] == item['sportEnrollmentId'],
            )['attendanceStatus'] = item['status'];
          }
          return {};
        }
        return roster(
          editable: true,
          players: remote.map((p) => {...p}).toList(),
        );
      });
      await tester.pumpWidget(
        shell(CoachAttendance(auth: auth, sessionId: 'actual')),
      );
      await tester.pumpAndSettle();
      expect(writes, 0);
      await reach(tester, find.byType(TextField));
      await tester.enterText(find.byType(TextField), 'FB-1');
      await tester.pumpAndSettle();
      await reach(tester, find.text('حاضر'));
      await tester.tap(find.text('حاضر'));
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.byType(TextField));
      await tester.enterText(find.byType(TextField), 'FB-2');
      await tester.pumpAndSettle();
      await reach(tester, find.text('غائب'));
      await tester.tap(find.text('غائب'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('حفظ الحضور'));
      await tester.pump();
      expect(find.text('جارٍ الحفظ…'), findsOneWidget);
      expect(
        tester.widget<FilledButton>(find.byType(FilledButton)).onPressed,
        isNull,
      );
      expect(writes, 1);
      expect(sent, containsPair('draftLease', 'lease'));
      expect(rows(sent!['items']), [
        {'sportEnrollmentId': 'e1', 'status': 'Present', 'expectedVersion': 1},
        {'sportEnrollmentId': 'e2', 'status': 'Absent', 'expectedVersion': 2},
      ]);
      gate.complete({});
      await tester.pumpAndSettle();
      // Reopen from the server, not just a locally changed chip.
      await tester.pumpWidget(const SizedBox());
      await tester.pumpWidget(
        shell(CoachAttendance(auth: auth, sessionId: 'actual')),
      );
      await tester.pumpAndSettle();
      expect(find.text('حاضر · 1'), findsOneWidget);
      expect(find.text('غائب · 1'), findsOneWidget);
      expect(find.text('لم يُسجل · 0'), findsOneWidget);
      expect(writes, 1);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'Session profile rechecks the actual roster and carries session/date to evaluation',
    (tester) async {
      final paths = <String>[];
      Json? created;
      final auth = await coach((m, p, b, t) async {
        paths.add(p);
        if (m == 'POST') {
          created = b as Json;
          return {'id': 'evaluation'};
        }
        if (p.endsWith('/evaluation')) {
          return {
            'player': 'لاعب التجربة 1',
            'group': 'براعم الاختبار',
            'scores': [],
          };
        }
        if (p == '/api/v1/help/lessons/evaluations') {
          return {'id': 'evaluations'};
        }
        return roster();
      });
      await tester.pumpWidget(
        shell(
          CoachPlayerPage(
            auth: auth,
            enrollmentId: 'e1',
            sessionId: 'replacement',
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(paths, [
        '/api/v1/attendance/sessions/replacement/players',
        '/api/v1/help/lessons/evaluations',
      ]);
      expect(find.text('شرح هذه الخطوة'), findsOneWidget);
      expect(find.text('لاعب التجربة 2'), findsNothing);
      await reach(tester, find.text('تقييم اللاعب'));
      await tester.tap(find.text('تقييم اللاعب'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('بدء تقييم جديد'));
      await tester.pumpAndSettle();
      expect(created, containsPair('sportEnrollmentId', 'e1'));
      expect(created, containsPair('trainingSessionId', 'replacement'));
      expect(created, containsPair('evaluationDate', '2026-10-15'));
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'Additional states save and reload, including returning a recorded player to unrecorded',
    (tester) async {
      var status = 'Present';
      var version = 5;
      final sent = <Json>[];
      final auth = await coach((m, p, b, t) async {
        if (m == 'PUT') {
          final item = rows((b as Json)['items']).single;
          expect(item['expectedVersion'], version);
          sent.add(item);
          status = textOf(item['status']);
          version++;
        }
        return roster(
          editable: true,
          players: [
            {...player(1, status), 'version': version},
          ],
        );
      });
      await tester.pumpWidget(
        shell(CoachAttendance(auth: auth, sessionId: 'actual')),
      );
      await tester.pumpAndSettle();
      for (final next in ['Late', 'Excused', 'NotRecorded']) {
        await reach(tester, find.byType(PopupMenuButton<String>));
        await tester.tap(find.byType(PopupMenuButton<String>));
        await tester.pumpAndSettle();
        await tester.tap(
          find.byWidgetPredicate(
            (w) => w is CheckedPopupMenuItem<String> && w.value == next,
          ),
        );
        await tester.pumpAndSettle();
        await tester.tap(find.text('حفظ الحضور'));
        await tester.pumpAndSettle();
        expect(status, next);
        expect(sent.last['status'], next);
        await tester.scrollUntilVisible(
          find.byKey(ValueKey('attendance-count-$next')),
          -250,
          scrollable: find.byType(Scrollable).first,
        );
        expect(find.text('${label(next)} · 1'), findsOneWidget);
      }
      expect(sent.length, 3);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'Group reloads authorized members, excludes other groups, opens a profile and hides on logout',
    (tester) async {
      final paths = <String>[];
      final auth = await coach((m, p, b, t) async {
        paths.add(p);
        if (p.endsWith('/groups')) {
          return [
            {'id': 'g1', 'arabicName': 'براعم الاختبار'},
          ];
        }
        if (p.endsWith('/options')) {
          return {
            'canEvaluate': false,
            'enrollments': [
              player(1),
              {...player(2), 'groupId': 'g2'},
            ],
          };
        }
        return [];
      });
      await tester.pumpWidget(
        shell(
          CoachGroup(
            auth: auth,
            group: {'id': 'g1', 'arabicName': 'براعم الاختبار'},
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('لاعب التجربة 2'), findsNothing);
      expect(find.text('تقييم اللاعب'), findsNothing);
      await reach(tester, find.text('عرض الملف'));
      await tester.tap(find.text('عرض الملف'));
      await tester.pumpAndSettle();
      expect(find.text('ملف اللاعب'), findsOneWidget);
      expect(find.text('لاعب التجربة 1'), findsOneWidget);
      expect(find.text('تقييم اللاعب'), findsNothing);
      expect(paths.where((p) => p.endsWith('/options')).length, 2);
      await auth.logout();
      await tester.pumpAndSettle();
      expect(find.text('لاعب التجربة 1'), findsNothing);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'Missing player and revoked group fail closed with no evaluation or upload',
    (tester) async {
      final auth = await coach(
        (m, p, b, t) async => p.endsWith('/options')
            ? {'enrollments': [], 'canEvaluate': true}
            : [],
      );
      await tester.pumpWidget(
        shell(
          CoachPlayerPage(auth: auth, enrollmentId: 'removed', groupId: 'g1'),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('لم يعد اللاعب ضمن هذا الكشف.'), findsOneWidget);
      expect(find.text('تقييم اللاعب'), findsNothing);
      await tester.pumpWidget(const SizedBox());
      await tester.pumpWidget(
        shell(
          CoachGroup(
            auth: auth,
            group: {'id': 'g1', 'arabicName': 'لم تعد مسندة'},
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('لم تعد هذه المجموعة مسندة إليك.'), findsOneWidget);
      expect(find.text('رفع صور وفيديوهات للحصة'), findsNothing);
    },
  );

  testWidgets(
    'Initial network failure retries while unauthorized roster cannot be retried as editable',
    (tester) async {
      var fail = true;
      final auth = await coach((m, p, b, t) async {
        if (fail) throw const ApiFailure(0, 'تعذر الاتصال');
        return roster(players: []);
      });
      await tester.pumpWidget(
        shell(CoachAttendance(auth: auth, sessionId: 'actual')),
      );
      await tester.pumpAndSettle();
      expect(find.text('إعادة المحاولة'), findsOneWidget);
      fail = false;
      await tester.tap(find.text('إعادة المحاولة'));
      await tester.pumpAndSettle();
      await reach(tester, find.text('لا يوجد لاعبون في هذه الحصة.'));
      expect(find.text('حفظ الحضور'), findsNothing);
      await auth.logout();
      await tester.pumpAndSettle();
      expect(find.text('براعم الاختبار'), findsNothing);
      await tester.pumpWidget(const SizedBox());
      final denied = await coach(
        (m, p, b, t) async =>
            throw const ApiFailure(403, 'ليس لديك صلاحية لهذا الإجراء.'),
      );
      await tester.pumpWidget(
        shell(CoachAttendance(auth: denied, sessionId: 'denied')),
      );
      await tester.pumpAndSettle();
      expect(find.text('إعادة المحاولة'), findsNothing);
      expect(find.text('حفظ الحضور'), findsNothing);
      expect(find.byType(CoachPlayerAvatar), findsNothing);
      expect(tester.takeException(), isNull);
    },
  );

  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets(
      'Session and group at $width, Arabic/English, dark/light and 200% text with 60 children',
      (tester) async {
        tester.view.physicalSize = Size(width, 900);
        tester.view.devicePixelRatio = 1;
        addTearDown(tester.view.resetPhysicalSize);
        addTearDown(tester.view.resetDevicePixelRatio);
        final auth = await coach((m, p, b, t) async {
          if (p.endsWith('/groups')) {
            return [
              {
                'id': 'g1',
                'arabicName': 'مجموعة أطفال التدريب بالعربية الطويلة',
              },
            ];
          }
          if (p.endsWith('/options')) {
            return {
              'canEvaluate': true,
              'enrollments': [for (var n = 0; n < 60; n++) player(n)],
            };
          }
          if (p.contains('upcomingOnly')) return [];
          return roster(
            editable: true,
            players: [for (var n = 0; n < 60; n++) player(n)],
          );
        });
        for (final dark in [false, true]) {
          for (final locale in ['ar', 'en']) {
            for (final page in [
              CoachAttendance(auth: auth, sessionId: 'actual'),
              CoachGroup(
                auth: auth,
                group: {
                  'id': 'g1',
                  'arabicName': 'مجموعة أطفال التدريب بالعربية الطويلة',
                },
              ),
            ]) {
              await tester.pumpWidget(const SizedBox());
              await tester.pumpWidget(
                MaterialApp(
                  locale: Locale(locale),
                  supportedLocales: const [Locale('ar'), Locale('en')],
                  localizationsDelegates: GlobalMaterialLocalizations.delegates,
                  theme: dark ? guardianTheme() : AcademyTheme.light(),
                  builder: (context, child) => MediaQuery(
                    data: MediaQuery.of(context)
                        .copyWith(textScaler: TextScaler.linear(2)),
                    child: child!,
                  ),
                  home: page,
                ),
              );
              await tester.pumpAndSettle();
              await reach(tester, find.byType(TextField));
              await tester.enterText(find.byType(TextField), 'FB-59');
              await tester.pumpAndSettle();
              await reach(tester, find.text('لاعب التجربة 59'));
              expect(find.text('لاعب التجربة 59'), findsOneWidget);
              expect(tester.takeException(), isNull);
            }
          }
        }
      },
    );
  }
}
