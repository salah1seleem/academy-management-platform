import 'support/visibility.dart';

import 'package:academy_mobile/features/coach/coach_attendance.dart';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/coach/coach_home.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'coach_test.dart' show coach;

Map<String, dynamic> session(
  String id,
  String date,
  String time, {
  String status = 'Scheduled',
}) => {
  'id': id,
  'trainingGroupId': 'g1',
  'group': 'مجموعة الناشئين · فرع مدينة نصر',
  'branch': 'مدينة نصر',
  'sport': 'كرة القدم',
  'sessionDate': date,
  'startTime': time,
  'endTime': '20:30:00',
  'status': status,
  'playerCount': 12,
};
Object? homeResponse(String path, {List<Object?>? sessions}) {
  if (path == '/api/v1/academy/calendar') return {'today': '2026-10-15'};
  if (path == '/api/v1/staff/passkeys/attendance') {
    return {
      'branches': [
        {'enabled': true},
      ],
    };
  }
  if (path == '/api/v1/coach/groups') {
    return [
      {
        'id': 'g1',
        'arabicName': 'مجموعة الناشئين',
        'branchName': 'فرع مدينتي',
        'playerCount': 1,
        'recurring': [
          {
            'dayOfWeek': 'Saturday',
            'startTime': '18:00:00',
            'endTime': '19:00:00',
          },
          {
            'dayOfWeek': 'Monday',
            'startTime': '18:00:00',
            'endTime': '19:00:00',
          },
        ],
      },
    ];
  }
  if (path == '/api/v1/attendance/sessions?nearestOnly=true') {
    return sessions ?? [session('nearest', '2026-10-16', '18:00:00')];
  }
  if (path == '/api/v1/evaluations/options') {
    return {
      'canEvaluate': true,
      'canManageAttendance': false,
      'enrollments': [
        {
          'id': 'e1',
          'groupId': 'g1',
          'player': 'آدم التجريبي',
          'playerCode': 'FB-TEST',
        },
      ],
    };
  }
  return [];
}

Widget app(
  AuthController auth, {
  bool dark = false,
  String locale = 'ar',
  double scale = 1,
}) => MaterialApp(
  locale: Locale(locale),
  supportedLocales: const [Locale('ar'), Locale('en')],
  localizationsDelegates: GlobalMaterialLocalizations.delegates,
  theme: dark ? guardianTheme() : AcademyTheme.light(),
  builder: (context, child) => MediaQuery(
    data: MediaQuery.of(
      context,
    ).copyWith(textScaler: TextScaler.linear(scale), disableAnimations: true),
    child: child!,
  ),
  home: CoachHome(
    auth: auth,
    accountPage: const Scaffold(body: Text('الحساب')),
  ),
);

void main() {
  testWidgets('Refreshing coach home reads the latest clock-in policy', (
    tester,
  ) async {
    var enabled = true;
    var open = false;
    var reads = 0;
    final auth = await coach((method, path, body, token) async {
      if (path == '/api/v1/staff/passkeys/attendance') {
        reads++;
        return {
          'branches': [
            {'enabled': enabled},
          ],
          'hasOpenAttendance': open,
        };
      }
      return homeResponse(path);
    });
    await tester.pumpWidget(app(auth));
    await tester.pumpAndSettle();
    final action = find.text('حضور وانصراف');
    final groups = find.text('مجموعات مسندة');
    expect(action, findsOneWidget);
    expect(groups, findsOneWidget);

    Future<void> refresh() async {
      final indicator = tester.widget<RefreshIndicator>(
        find.byType(RefreshIndicator).first,
      );
      await indicator.onRefresh();
      await tester.pumpAndSettle();
    }

    enabled = false;
    await refresh();
    expect(reads, 2);
    expect(action, findsNothing);
    expect(groups, findsOneWidget);

    open = true;
    await refresh();
    expect(reads, 3);
    expect(action, findsOneWidget);
    expect(find.text('لديك تسجيل حضور مفتوح'), findsOneWidget);

    open = false;
    await refresh();
    expect(reads, 4);
    expect(action, findsNothing);

    enabled = true;
    await refresh();
    expect(reads, 5);
    expect(action, findsOneWidget);
    await tester.pumpWidget(const SizedBox.shrink());
    auth.dispose();
  });

  for (final allowed in [false, true]) {
    testWidgets(
      'Group roster shows evaluation and only dated delegated attendance (grant=$allowed)',
      (tester) async {
        tester.view.physicalSize = const Size(390, 844);
        tester.view.devicePixelRatio = 1;
        addTearDown(tester.view.reset);
        final paths = <String>[];
        final auth = await coach((method, path, body, token) async {
          paths.add(path);
          if (path == '/api/v1/evaluations/options') {
            return {
              ...homeResponse(path) as Map<String, dynamic>,
              'canManageAttendance': allowed,
              'enrollments': [
                {
                  'id': 'e1',
                  'groupId': 'g1',
                  'player': 'آدم التجريبي',
                  'playerCode': 'TEST-1',
                },
                {
                  'id': 'e2',
                  'groupId': 'g2',
                  'player': 'لاعب مجموعة أخرى',
                  'playerCode': 'TEST-2',
                },
              ],
            };
          }
          if (path == '/api/v1/attendance/sessions?groupId=g1') {
            return [
              {
                ...session('foreign', '2026-10-15', '17:00:00'),
                'trainingGroupId': 'g2',
              },
              session('today', '2026-10-15', '18:00:00'),
            ];
          }
          if (path == '/api/v1/attendance/sessions/today/players') {
            return {
              'canManageAttendance': allowed,
              'canEvaluate': true,
              'session': session('today', '2026-10-15', '18:00:00'),
              'players': [
                {
                  'sportEnrollmentId': 'e1',
                  'arabicName': 'آدم التجريبي',
                  'playerCode': 'TEST-1',
                  'attendanceStatus': 'NotRecorded',
                },
              ],
            };
          }
          return homeResponse(path);
        });
        await tester.pumpWidget(app(auth));
        await tester.pumpAndSettle();
        final group = find.byKey(const ValueKey('coach-group-g1'));
        final open = find.descendant(
          of: group,
          matching: find.text('فتح المجموعة'),
        );
        await revealTestTarget(tester, open);
        expect(find.textContaining('السبت، الاثنين'), findsOneWidget);
        await tester.tap(open);
        await tester.pumpAndSettle();
        final player = find.text('آدم التجريبي');
        await revealTestTarget(tester, player);
        expect(player, findsOneWidget);
        expect(find.text('لاعب مجموعة أخرى'), findsNothing);
        final evaluate = find.text('تقييم');
        await revealTestTarget(tester, evaluate);
        expect(evaluate, findsOneWidget);
        expect(
          find.byType(CoachAttendance),
          allowed ? findsOneWidget : findsNothing,
        );
        if (allowed) {
          await revealTestTarget(tester, find.text('حاضر'));
          expect(find.text('حاضر'), findsOneWidget);
          expect(find.text('غائب'), findsOneWidget);
          expect(find.text('حفظ الحضور'), findsOneWidget);
        }
        expect(
          paths.contains('/api/v1/attendance/sessions/today/players'),
          allowed,
        );
        expect(
          paths.contains('/api/v1/attendance/sessions/foreign/players'),
          false,
        );
        expect(tester.takeException(), isNull);
        await tester.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }

  testWidgets(
    'Logout clears private home data without issuing anonymous reloads',
    (tester) async {
      var gets = 0;
      final auth = await coach((m, p, b, t) async {
        if (m == 'GET') gets++;
        return homeResponse(p);
      });
      await tester.pumpWidget(app(auth));
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.byKey(const ValueKey('coach-session-nearest')),
        200,
        scrollable: find.byType(Scrollable).first,
      );
      expect(
        find.byKey(const ValueKey('coach-session-nearest')),
        findsOneWidget,
      );
      final beforeLogout = gets;
      await auth.logout();
      await tester.pumpAndSettle();
      expect(find.byKey(const ValueKey('coach-session-nearest')), findsNothing);
      expect(gets, beforeLogout);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Nearest actual session is separate from groups, ordered, and opens read-only evaluation without attendance permission',
    (tester) async {
      final paths = <String>[];
      var writes = 0;
      final auth = await coach((m, p, b, t) async {
        paths.add(p);
        if (m != 'GET') writes++;
        if (p == '/api/v1/attendance/sessions/nearest/players') {
          return {
            'canManageAttendance': false,
            'canEvaluate': true,
            'session': {
              'group': 'مجموعة الناشئين',
              'sessionDate': '2026-10-16',
              'startTime': '18:00',
            },
            'players': [
              {
                'sportEnrollmentId': 'e1',
                'arabicName': 'آدم التجريبي',
                'playerCode': 'FB-TEST',
                'attendanceStatus': 'NotRecorded',
              },
            ],
          };
        }
        return homeResponse(
          p,
          sessions: [session('nearest', '2026-10-16', '18:00:00')],
        );
      });
      await tester.pumpWidget(app(auth));
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.byKey(const ValueKey('coach-session-nearest')),
        200,
        scrollable: find.byType(Scrollable).first,
      );
      final cards = tester
          .widgetList<CoachSessionCard>(find.byType(CoachSessionCard))
          .toList();
      expect(cards.first.session['id'], 'nearest');
      expect(cards.first.featured, isTrue);
      expect(
        find.byKey(const ValueKey('coach-session-cancelled')),
        findsNothing,
      );
      expect(paths, contains('/api/v1/coach/groups'));
      expect(paths, contains('/api/v1/attendance/sessions?nearestOnly=true'));
      final nearestOpen = find.descendant(
        of: find.byKey(const ValueKey('coach-session-nearest')),
        matching: find.widgetWithText(FilledButton, 'عرض المجموعة ولاعبيها'),
      );
      await tester.scrollUntilVisible(
        nearestOpen,
        180,
        scrollable: find.byType(Scrollable).first,
      );
      await revealTestTarget(tester, nearestOpen);
      await tester.tap(nearestOpen);
      await tester.pumpAndSettle();
      expect(
        paths,
        isNot(contains('/api/v1/attendance/sessions/nearest/players')),
      );
      expect(find.text('مجموعة الناشئين'), findsWidgets);
      await revealTestTarget(tester, find.text('آدم التجريبي'));
      expect(find.text('آدم التجريبي'), findsOneWidget);
      expect(find.text('تسجيل الحضور'), findsNothing);
      expect(find.widgetWithText(FilledButton, 'تقييم'), findsOneWidget);
      expect(find.text('حفظ الحضور'), findsNothing);
      expect(find.byType(ChoiceChip), findsNothing);
      expect(writes, 0);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Empty upcoming sessions keeps assigned groups and player profiles reachable',
    (tester) async {
      final auth = await coach(
        (m, p, b, t) async => homeResponse(p, sessions: []),
      );
      await tester.pumpWidget(app(auth));
      await tester.pumpAndSettle();
      expect(find.text('لا توجد حصة قادمة مجدولة.'), findsNothing);
      await revealTestTarget(tester, find.text('فتح المجموعة'));
      await tester.tap(find.text('فتح المجموعة'));
      await tester.pumpAndSettle();
      await revealTestTarget(tester, find.text('آدم التجريبي'));
      expect(find.text('آدم التجريبي'), findsOneWidget);
      expect(find.text('لاعب واحد في المجموعة'), findsOneWidget);
      expect(find.text('فتح الحصة الأقرب'), findsNothing);
      expect(find.text('رفع صور وفيديوهات المجموعة'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Failed request exposes retry and reloads sessions rather than a blank home',
    (tester) async {
      var fail = true;
      final auth = await coach((m, p, b, t) async {
        if (p.contains('nearestOnly') && fail) {
          throw const ApiFailure(0, 'تعذر الاتصال');
        }
        return homeResponse(p);
      });
      await tester.pumpWidget(app(auth));
      await tester.pumpAndSettle();
      await revealTestTarget(tester, find.text('إعادة تحميل الحصة القادمة'));
      expect(find.text('تعذر الاتصال'), findsOneWidget);
      fail = false;
      await tester.tap(find.text('إعادة تحميل الحصة القادمة'));
      await tester.pumpAndSettle();
      await revealTestTarget(tester, find.text('مجموعات مسندة'));
      expect(find.text('مجموعات مسندة'), findsOneWidget);
      expect(find.text('تعذر الاتصال'), findsNothing);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets(
      'Coach workspace stays usable at $width, both appearances/languages and 200% text',
      (tester) async {
        tester.view.physicalSize = Size(width, 900);
        tester.view.devicePixelRatio = 1;
        addTearDown(tester.view.resetPhysicalSize);
        addTearDown(tester.view.resetDevicePixelRatio);
        final auth = await coach((m, p, b, t) async => homeResponse(p));
        for (final dark in [false, true]) {
          for (final locale in ['ar', 'en']) {
            await tester.pumpWidget(
              app(auth, dark: dark, locale: locale, scale: 2),
            );
            await tester.pumpAndSettle();
            expect(find.byIcon(Icons.fingerprint), findsOneWidget);
            await tester.scrollUntilVisible(
              find.byKey(const ValueKey('coach-session-nearest')),
              200,
              scrollable: find.byType(Scrollable).first,
            );
            expect(
              find.byKey(const ValueKey('coach-session-nearest')),
              findsOneWidget,
            );

            expect(
              tester.takeException(),
              isNull,
              reason: '$width $dark $locale',
            );
            await tester.pumpWidget(const SizedBox());
          }
        }
        auth.dispose();
      },
    );
  }
}
