import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/coach/coach_attendance.dart';
import 'package:academy_mobile/features/coach/coach_home.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart';

Map<String, dynamic> coachCredentials() => {
  'accessToken': 'coach-access',
  'refreshToken': 'coach-refresh',
  'displayName': 'مدرب الاختبار',
  'accessExpiresAtUtc': '2100-01-01T00:00:00Z',
  'membershipId': 'coach',
  'memberships': [
    {
      'id': 'coach',
      'academyId': 'academy',
      'academyName': 'أكاديمية العرض',
      'role': 'Coach',
    },
  ],
};

Future<AuthController> coach(Handler handler) async {
  final auth = AuthController(
    FakeApi(
      (m, p, b, t) => p.contains('/auth/')
          ? Future.value(coachCredentials())
          : handler(m, p, b, t),
    ),
    MemoryStore(),
  )..restoring = false;
  await auth.verify('phone', 'challenge', 'otp');
  return auth;
}

Widget shell(Widget page) => MaterialApp(
  locale: const Locale('ar'),
  supportedLocales: const [Locale('ar')],
  localizationsDelegates: GlobalMaterialLocalizations.delegates,
  theme: AcademyTheme.light(),
  home: page,
);

void main() {
  testWidgets('Coach home exposes assigned work and no finance', (
    tester,
  ) async {
    final auth = await coach((m, p, b, t) async {
      if (p == '/api/v1/coach/groups') {
        return [
          {'id': 'g1', 'arabicName': 'براعم 2018'},
        ];
      }
      if (p == '/api/v1/attendance/sessions') {
        return [
          {
            'id': 's1',
            'trainingGroupId': 'g1',
            'group': 'براعم 2018',
            'branch': 'مدينة نصر',
            'sport': 'كرة القدم',
            'sessionDate': '2026-09-28',
            'startTime': '15:00',
            'endTime': '16:00',
            'recorded': 3,
            'playerCount': 5,
          },
        ];
      }
      if (p == '/api/v1/evaluations/options') {
        return {
          'enrollments': [
            {
              'id': 'e1',
              'groupId': 'g1',
              'group': 'براعم 2018',
              'player': 'آدم',
              'playerCode': 'FB-001',
            },
          ],
          'sports': [],
        };
      }
      return [];
    });
    await tester.pumpWidget(
      shell(CoachHome(auth: auth, accountPage: const SizedBox())),
    );
    await tester.pumpAndSettle();
    expect(find.textContaining('مدرب الاختبار'), findsOneWidget);
    expect(find.text('براعم 2018'), findsWidgets);
    for (final forbidden in ['تحصيل', 'مدفوعات', 'إيرادات', 'اشتراكات']) {
      expect(find.textContaining(forbidden), findsNothing);
    }
    expect(tester.takeException(), isNull);
  });

  testWidgets('Coach attendance supports three states and correction save', (
    tester,
  ) async {
    Object? sent;
    final auth = await coach((m, p, b, t) async {
      if (m == 'PUT') {
        sent = b;
        return {'items': []};
      }
      return {
        'session': {
          'group': 'براعم',
          'sessionDate': '2026-09-28',
          'startTime': '15:00',
        },
        'players': [
          {
            'sportEnrollmentId': 'e1',
            'arabicName': 'آدم',
            'playerCode': 'FB-001',
            'attendanceStatus': 'NotRecorded',
          },
        ],
      };
    });
    await tester.pumpWidget(
      shell(CoachAttendance(auth: auth, sessionId: 's1')),
    );
    await tester.pumpAndSettle();
    expect(find.text('حاضر'), findsOneWidget);
    expect(find.text('غائب'), findsOneWidget);
    expect(find.text('لم يُسجل'), findsOneWidget);
    await tester.tap(find.text('حاضر'));
    await tester.tap(find.text('حفظ الحضور'));
    await tester.pumpAndSettle();
    expect(sent.toString(), contains('Present'));
    expect(find.textContaining('يمكن تصحيحه'), findsOneWidget);
  });
}
