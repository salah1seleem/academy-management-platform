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
  if (path == '/api/v1/coach/groups') {
    return [
      {'id': 'g1', 'arabicName': 'مجموعة الناشئين'},
    ];
  }
  if (path == '/api/v1/attendance/sessions?upcomingOnly=true') {
    return sessions ?? [session('nearest', '2026-10-16', '18:00:00')];
  }
  if (path == '/api/v1/evaluations/options') {
    return {
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
    data: MediaQuery.of(context).copyWith(textScaler: TextScaler.linear(scale)),
    child: child!,
  ),
  home: CoachHome(
    auth: auth,
    accountPage: const Scaffold(body: Text('الحساب')),
  ),
);

void main() {
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
          sessions: [
            session('later', '2026-10-17', '19:00:00'),
            session('cancelled', '2026-10-15', '16:00:00', status: 'Cancelled'),
            session('nearest', '2026-10-16', '18:00:00'),
          ],
        );
      });
      await tester.pumpWidget(app(auth));
      await tester.pumpAndSettle();
      final cards = tester
          .widgetList<CoachSessionCard>(find.byType(CoachSessionCard))
          .toList();
      expect(cards.first.session['id'], 'nearest');
      expect(cards.first.featured, isTrue);
      expect(
        find.byKey(const ValueKey('coach-session-cancelled')),
        findsNothing,
      );
      expect(find.text('المجموعات المسندة'), findsOneWidget);
      expect(paths, contains('/api/v1/attendance/sessions?upcomingOnly=true'));
      await tester.tap(find.text('فتح الحصة'));
      await tester.pumpAndSettle();
      expect(paths, contains('/api/v1/attendance/sessions/nearest/players'));
      expect(find.text('تفاصيل الحصة'), findsOneWidget);
      expect(find.text('تقييم اللاعب'), findsOneWidget);
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
      expect(find.text('لا توجد حصة قادمة خلال 14 يومًا'), findsOneWidget);
      await tester.tap(find.text('مجموعة الناشئين'));
      await tester.pumpAndSettle();
      expect(find.text('آدم التجريبي'), findsOneWidget);
      expect(find.text('فتح الحصة الأقرب'), findsNothing);
      expect(find.text('رفع صور وفيديوهات للحصة'), findsOneWidget);
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
        if (p.contains('upcomingOnly') && fail) {
          throw const ApiFailure(0, 'تعذر الاتصال');
        }
        return homeResponse(p);
      });
      await tester.pumpWidget(app(auth));
      await tester.pumpAndSettle();
      expect(find.text('تعذر الاتصال'), findsOneWidget);
      fail = false;
      await tester.tap(find.text('إعادة المحاولة'));
      await tester.pumpAndSettle();
      expect(find.text('حصتك الأقرب'), findsOneWidget);
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
            expect(
              find.byKey(const ValueKey('coach-session-nearest')),
              findsOneWidget,
            );
            await tester.scrollUntilVisible(
              find.byIcon(Icons.fingerprint),
              250,
              scrollable: find.byType(Scrollable).first,
            );
            expect(find.byIcon(Icons.fingerprint), findsOneWidget);
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
