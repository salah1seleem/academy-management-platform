import 'dart:async';

import 'package:academy_mobile/core/theme/academy_brand.dart';
import 'package:academy_mobile/features/shared/role_agenda.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

Map<String, dynamic> game(
  String child, {
  String opponent = 'منافس اصطناعي',
  bool cancelled = false,
}) => {
  'id': 'match-$child',
  'children': [child],
  'isUpcoming': !cancelled,
  'scheduleWindow': 'Upcoming',
  'status': cancelled ? 'Cancelled' : 'Published',
  'team': 'منتخب تجريبي',
  'opponent': opponent,
  'matchDate': '2026-10-21',
  'startTime': '18:00:00',
  'venue': 'ملعب العرض',
  'tournament': 'بطولة عرض',
  'coachAssignmentSource': 'Confirmed',
  'coachName': 'مدرب المباراة',
};
Map<String, dynamic> training(String child) => {
  'id': 'session-$child',
  'group': 'مجموعة $child',
  'sessionDate': '2026-10-21',
  'startTime': '17:00:00',
  'endTime': '18:00:00',
  'sport': 'كرة القدم',
  'branch': 'فرع اصطناعي',
};
Map<String, dynamic> profile(String id) => {
  'player': {'id': id},
  'enrollments': [
    {
      'sport': 'كرة القدم',
      'branch': 'فرع اصطناعي',
      'group': 'مجموعة $id',
      'upcoming': [training(id)],
    },
  ],
};
final family = {
  'children': [
    {'id': 'a', 'arabicName': 'ابن أول اصطناعي'},
    {'id': 'b', 'arabicName': 'ابن ثان اصطناعي'},
  ],
};

void main() {
  for (final guardian in [true, false]) {
    for (final width in [360.0, 390.0, 430.0]) {
      testWidgets(
        'unified ${guardian ? 'guardian' : 'coach'} agenda is scoped, chronological and fits $width at 200%',
        (t) async {
          t.view.physicalSize = Size(width, 1300);
          t.view.devicePixelRatio = 1;
          addTearDown(t.view.resetPhysicalSize);
          addTearDown(t.view.resetDevicePixelRatio);
          final calls = <String>[];
          final auth = await authenticate(guardian ? 'Guardian' : 'Coach', (
            m,
            p,
            b,
            token,
          ) async {
            calls.add('$m $p');
            if (p == '/api/v1/guardian/home') return family;
            if (p.endsWith('/profile')) {
              return profile(p.contains('/b/') ? 'b' : 'a');
            }
            if (p.contains('/attendance/')) return [training('a')];
            return [
              game('a'),
              game('a', opponent: 'منافس ملغى', cancelled: true),
            ];
          });
          await t.pumpWidget(
            app(RoleAgenda(auth: auth, guardian: guardian), scale: 2),
          );
          await t.pumpAndSettle();
          await t.scrollUntilVisible(
            find.text('منتخب تجريبي × منافس اصطناعي'),
            150,
            scrollable: find.byType(Scrollable).first,
          );
          expect(find.text('منافس ملغى'), findsNothing);
          expect(find.text('مدرب المباراة: مدرب المباراة'), findsOneWidget);
          expect(
            calls,
            guardian
                ? [
                    'GET /api/v1/guardian/home',
                    'GET /api/v1/guardian/children/a/profile',
                    'GET /api/v1/my-matches?upcomingOnly=true',
                    'GET /api/v1/guardian/children/b/profile',
                  ]
                : [
                    'GET /api/v1/attendance/sessions?upcomingOnly=true',
                    'GET /api/v1/my-matches?upcomingOnly=true',
                  ],
          );
          expect(t.takeException(), isNull);
          await auth.logout();
          await t.pumpAndSettle();
          expect(find.text('منتخب تجريبي × منافس اصطناعي'), findsNothing);
          await t.pumpWidget(const SizedBox());
          auth.dispose();
        },
      );
    }
  }
  testWidgets(
    'guardian child selector replaces both sources, refresh removes cancellation, and filtering never broadens scope',
    (t) async {
      final calls = <String>[];
      var cancelled = false;
      final auth = await authenticate('Guardian', (m, p, b, token) async {
        calls.add('$m $p');
        if (p == '/api/v1/guardian/home') return family;
        final child = p.contains('/b/') || p.endsWith('=b') ? 'b' : 'a';
        if (p.endsWith('/profile')) return profile(child);
        return [game(child, opponent: 'منافس $child', cancelled: cancelled)];
      });
      await t.pumpWidget(app(RoleAgenda(auth: auth, guardian: true)));
      await t.pumpAndSettle();
      expect(find.text('مجموعة a'), findsOneWidget);
      expect(
        t.getTopLeft(find.text('مجموعة a')).dy,
        lessThan(t.getTopLeft(find.text('منتخب تجريبي × منافس a')).dy),
      );
      await t.tap(find.byKey(const ValueKey('family-filter-b')));
      await t.pumpAndSettle();
      expect(find.text('مجموعة a'), findsNothing);
      expect(find.text('مجموعة b'), findsOneWidget);
      await t.tap(find.widgetWithText(ChoiceChip, 'المباريات'));
      await t.pumpAndSettle();
      expect(find.text('مجموعة b'), findsNothing);
      expect(find.text('منتخب تجريبي × منافس b'), findsOneWidget);
      cancelled = true;
      await t.tap(find.byTooltip('تحديث المواعيد'));
      await t.pumpAndSettle();
      expect(find.text('منتخب تجريبي × منافس b'), findsNothing);
      expect(
        calls
            .where((p) => p.contains('/my-matches'))
            .every(
              (p) =>
                  p.endsWith('upcomingOnly=true') ||
                  p.endsWith('=a') ||
                  p.endsWith('=b'),
            ),
        isTrue,
      );
      expect(calls.every((p) => p.startsWith('GET ')), isTrue);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'shared session labels makeup only for the booked child and refresh clears it',
    (t) async {
      var released = false;
      final auth = await authenticate('Guardian', (
        method,
        path,
        body,
        token,
      ) async {
        if (path == '/api/v1/guardian/home') return family;
        if (!path.endsWith('/profile')) return [];
        final id = path.contains('/a/') ? 'a' : 'b';
        return {
          'player': {'id': id},
          'enrollments': [
            {
              'id': 'enrollment-$id',
              'sport': 'كرة القدم',
              'branch': 'فرع العرض',
              'upcoming': [training('shared')],
            },
          ],
          'compensations': [
            if (id == 'a')
              {
                'sportEnrollmentId': 'enrollment-a',
                'bookings': [
                  {
                    'trainingSessionId': 'session-shared',
                    'state': released ? 'Cancelled' : 'Reserved',
                  },
                ],
              },
          ],
        };
      });
      await t.pumpWidget(app(RoleAgenda(auth: auth, guardian: true)));
      await t.pumpAndSettle();
      expect(find.text('حصة تعويضية لـ ابن أول اصطناعي'), findsOneWidget);
      expect(find.text('مجموعة shared'), findsOneWidget);
      released = true;
      await t.tap(find.byTooltip('تحديث المواعيد'));
      await t.pumpAndSettle();
      expect(find.textContaining('حصة تعويضية'), findsNothing);
      expect(find.text('مجموعة shared'), findsOneWidget);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'coach sees the exact roster action and retries only the failed schedule source',
    (t) async {
      var trainingReads = 0, matchReads = 0;
      final auth = await authenticate('Coach', (
        method,
        path,
        body,
        token,
      ) async {
        expect(method, 'GET');
        if (path.contains('my-matches')) {
          matchReads++;
          throw StateError('test match error');
        }
        if (path.endsWith('/players')) {
          expect(path, '/api/v1/attendance/sessions/session-a/players');
          return {
            'session': {...training('a'), 'status': 'Scheduled'},
            'players': [],
            'canManageAttendance': false,
          };
        }
        expect(path, '/api/v1/attendance/sessions?upcomingOnly=true');
        trainingReads++;
        return [
          {
            ...training('a'),
            'resource': 'المسبح الرئيسي · حارة 3',
            'isSubstitute': true,
          },
          {...training('cancelled'), 'status': 'Cancelled'},
        ];
      });
      await t.pumpWidget(app(RoleAgenda(auth: auth, guardian: false)));
      await t.pumpAndSettle();
      expect(find.text('مدرب بديل'), findsOneWidget);
      expect(find.text('المسبح الرئيسي · حارة 3'), findsOneWidget);
      expect(find.text('مجموعة cancelled'), findsNothing);
      await t.tap(find.widgetWithText(ChoiceChip, 'الحصص'));
      await t.pumpAndSettle();
      expect(find.text('تعذر تحميل المباريات — أعد المحاولة'), findsNothing);
      await t.tap(find.widgetWithText(ChoiceChip, 'الكل'));
      await t.pumpAndSettle();
      await t.tap(find.text('تعذر تحميل المباريات — أعد المحاولة'));
      await t.pumpAndSettle();
      expect(trainingReads, 1);
      expect(matchReads, 2);
      await t.ensureVisible(find.text('كشف الحضور'));
      await t.tap(find.text('كشف الحضور'));
      await t.pumpAndSettle();
      expect(find.text('كشف الحضور للعرض فقط.'), findsOneWidget);
      expect(find.text('حفظ الحضور'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'one failed source leaves the other visible and retry does not fall back to unscoped matches',
    (t) async {
      var wrongScope = true;
      final auth = await authenticate('Guardian', (m, p, b, token) async {
        if (p == '/api/v1/guardian/home') return family;
        if (p.endsWith('/profile')) {
          return profile(p.contains('/b/') ? 'b' : 'a');
        }
        return [game(wrongScope ? 'foreign-child' : 'a')];
      });
      await t.pumpWidget(app(RoleAgenda(auth: auth, guardian: true)));
      await t.pumpAndSettle();
      expect(find.text('تعذر تحميل المباريات — أعد المحاولة'), findsOneWidget);
      expect(find.text('مجموعة a'), findsOneWidget);
      expect(find.text('منتخب تجريبي × منافس اصطناعي'), findsNothing);
      wrongScope = false;
      await t.tap(find.text('تعذر تحميل المباريات — أعد المحاولة'));
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('منتخب تجريبي × منافس اصطناعي'),
        150,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.text('تعذر تحميل المباريات — أعد المحاولة'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  for (final guardian in [true, false]) {
    testWidgets(
      'a swimming academy without competitions renders only training for ${guardian ? 'families' : 'coaches'}',
      (t) async {
        final calls = <String>[];
        final auth = await authenticate(guardian ? 'Guardian' : 'Coach', (
          m,
          p,
          b,
          token,
        ) async {
          calls.add('$m $p');
          if (p == '/api/v1/academy/experience') {
            return {
              'onboarding': {
                'sportTheme': 'swimming',
                'features': ['nutrition'],
              },
            };
          }
          if (p == '/api/v1/me/profile') return {};
          if (p == '/api/v1/guardian/home') return family;
          if (p.endsWith('/profile')) {
            return profile(p.contains('/b/') ? 'b' : 'a');
          }
          if (p.contains('/attendance/')) return [training('a')];
          throw StateError('Unexpected request');
        });
        final brand = AcademyBrand(auth);
        await t.pumpWidget(
          app(
            BrandScope(
              brand: brand,
              child: RoleAgenda(auth: auth, guardian: guardian),
            ),
          ),
        );
        await t.pumpAndSettle();
        expect(
          find.text(guardian ? 'جدول التدريب' : 'الحصص خلال 14 يومًا'),
          findsOneWidget,
        );
        expect(find.text('مجموعة a'), findsOneWidget);
        expect(find.text('المباريات'), findsNothing);
        expect(find.text('سجل المباريات والإلغاءات'), findsNothing);
        expect(find.text('تعذر تحميل المباريات — أعد المحاولة'), findsNothing);
        expect(calls.any((call) => call.contains('/my-matches')), isFalse);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        brand.dispose();
        auth.dispose();
      },
    );
  }
  testWidgets('late agenda responses cannot reveal records after logout', (
    t,
  ) async {
    final response = Completer<Object?>();
    final auth = await authenticate(
      'Coach',
      (m, p, b, token) => response.future,
    );
    await t.pumpWidget(app(RoleAgenda(auth: auth, guardian: false)));
    await t.pump();
    // Logout's remote revocation is best-effort; use a separate response for it.
    final logout = auth.logout();
    response.complete([game('a')]);
    await logout;
    await t.pumpAndSettle();
    expect(find.text('منتخب تجريبي × منافس اصطناعي'), findsNothing);
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  for (final role in ['Guardian', 'Coach']) {
    testWidgets(
      '$role bottom navigation opens the role workspace without reading finances',
      (t) async {
        final calls = <String>[];
        final auth = await authenticate(role, (m, p, b, token) async {
          calls.add(p);
          if (p == '/api/v1/guardian/home') return {'children': []};
          return [];
        });
        await t.pumpWidget(
          app(
            NativeRoleTabs(
              auth: auth,
              role: role,
              home: const SizedBox(),
              account: const SizedBox(),
            ),
          ),
        );
        await t.pumpAndSettle();
        await t.tap(find.text(role == 'Guardian' ? 'الحصص' : 'المجموعات'));
        await t.pumpAndSettle();
        expect(
          find.byType(role == 'Guardian' ? RoleAgenda : CoachGroupsPage),
          findsOneWidget,
        );
        expect(
          calls.any((p) => p.contains('/reports/') || p.contains('/manage/')),
          isFalse,
        );
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
}
