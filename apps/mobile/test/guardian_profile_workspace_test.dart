import 'dart:async';

import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_profile.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/guardian/guardian_profile_header.dart';
import 'package:academy_mobile/features/guardian/guardian_report.dart';
import 'package:academy_mobile/features/guardian/guardian_content.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'guardian_test.dart' show logged;
import 'v2_role_workspaces_test.dart' show app;

Json profileFixture() => {
  'player': {
    'id': 'child',
    'arabicName': 'طفل اصطناعي متعدد الرياضات للاختبار',
    'dateOfBirth': '2018-03-12',
  },
  'monthlyStars': 2,
  'enrollments': [
    {
      'id': 'football',
      'sport': 'كرة القدم',
      'branch': 'فرع الاختبار',
      'group': 'مجموعة الكرة',
      'period': null,
    },
    {
      'id': 'swimming',
      'sport': 'السباحة',
      'branch': 'فرع الاختبار',
      'group': 'مجموعة السباحة',
      'period': null,
    },
  ],
  'performances': [
    {
      'enrollmentId': 'football',
      'evaluationCount': 1,
      'latest': {
        'evaluationId': 'report-football',
        'evaluationDate': '2026-10-21',
        'overallScore': 0,
        'meanPerformance': 0,
        'scoredCriteria': 1,
        'applicableCriteria': 4,
        'isFootballReport': true,
        'axes': [
          {'axis': 'Passing', 'value': 0},
          {'axis': 'Speed', 'value': 0},
          {'axis': 'Dribbling', 'value': 0},
        ],
        'criteria': [
          {'name': 'مؤشر كرة القدم المسجل', 'value': 0},
        ],
      },
      'history': [
        {
          'evaluationId': 'report-football',
          'evaluationDate': '2026-10-21',
          'overallScore': 0,
          'meanPerformance': 0,
          'scoredCriteria': 1,
          'applicableCriteria': 4,
        },
      ],
    },
    {
      'enrollmentId': 'swimming',
      'evaluationCount': 1,
      'latest': {
        'evaluationId': 'report-swimming',
        'evaluationDate': '2026-10-20',
        'overallScore': null,
        'meanPerformance': null,
        'isFootballReport': false,
        'axes': [],
        'criteria': [
          {'name': 'مؤشر السباحة المسجل', 'value': 50},
        ],
      },
      'history': [
        {
          'evaluationId': 'report-swimming',
          'evaluationDate': '2026-10-20',
          'overallScore': null,
          'meanPerformance': null,
        },
      ],
    },
  ],
  'attendance': [
    {
      'id': 'a1',
      'sportEnrollmentId': 'football',
      'sessionDate': '2026-10-21',
      'group': 'كشف الكرة المسجل',
      'status': 'Present',
    },
    {
      'id': 'a2',
      'sportEnrollmentId': 'swimming',
      'sessionDate': '2026-10-20',
      'group': 'كشف السباحة المسجل',
      'status': 'Absent',
    },
  ],
  'compensations': [],
};
Future<void> reveal(WidgetTester t, Finder f) async {
  if (f.evaluate().isEmpty) {
    await t.scrollUntilVisible(
      f,
      160,
      scrollable: find.byType(Scrollable).first,
      maxScrolls: 40,
    );
  }
  await t.ensureVisible(f);
  await t.pumpAndSettle();
}

void main() {
  testWidgets(
    'detail tabs keep a compact identity and nutrition uses academy day independently of subscription snapshot',
    (t) async {
      final paths = <String>[];
      final auth = await logged((m, p, b, token) async {
        paths.add(p);
        if (p.contains('/nutrition/recommendations')) {
          return <String, dynamic>{'date': '2026-10-29', 'trainingDay': false};
        }
        return profileFixture();
      });
      await t.pumpWidget(
        app(
          ChildProfile(
            auth: auth,
            environment: 'Demo',
            playerId: 'child',
            today: '2026-10-28',
            nutritionDate: '2026-10-29',
          ),
        ),
      );
      await t.pumpAndSettle();
      final tab = find.byKey(const ValueKey('profile-tab-health'));
      await reveal(t, tab);
      await t.tap(tab);
      await t.pumpAndSettle();
      expect(
        find.byKey(const ValueKey('profile-compact-identity')),
        findsOneWidget,
      );
      expect(
        find.byKey(const ValueKey('guardian-profile-header')),
        findsNothing,
      );
      expect(
        paths,
        contains(
          '/api/v1/guardian/children/child/nutrition/recommendations?date=2026-10-29',
        ),
      );
      expect(paths.any((p) => p.contains('date=2026-10-28')), isFalse);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'profile primary value uses the published mean when weighted power differs',
    (t) async {
      final data = profileFixture();
      final latest = (data['performances'] as List).first['latest'] as Json;
      latest['overallScore'] = 81;
      latest['meanPerformance'] = 55.5;
      final auth = await logged((m, p, b, token) async => data);
      await t.pumpWidget(
        app(
          ChildProfile(
            auth: auth,
            environment: 'Demo',
            playerId: 'child',
            today: '2026-10-25',
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('متوسط الأداء'), findsOneWidget);
      expect(find.text('55.5'), findsOneWidget);
      expect(find.text('81'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'home card forwards the selected sport when opening the profile',
    (t) async {
      final auth = await logged((m, p, b, token) async => <String, dynamic>{});
      String? opened;
      var generic = 0;
      final data = profileFixture();
      await t.pumpWidget(
        app(
          Scaffold(
            body: SingleChildScrollView(
              child: PremiumChildCard(
                auth: auth,
                childData: {
                  ...obj(data['player']),
                  'sports': data['enrollments'],
                },
                today: '2026-10-22',
                onTap: () => generic++,
                onOpenEnrollment: (id) => opened = id,
              ),
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      await reveal(t, find.byType(DropdownButtonFormField<String>));
      await t.tap(find.byType(DropdownButtonFormField<String>));
      await t.pumpAndSettle();
      await t.tap(find.text('السباحة · 8 سنة · فرع الاختبار').last);
      await t.pumpAndSettle();
      await reveal(t, find.textContaining('والتقرير'));
      await t.tap(find.textContaining('والتقرير'));
      await t.pumpAndSettle();
      expect(opened, 'swimming');
      expect(generic, 0);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  for (final width in [375.0, 440.0]) {
    for (final scale in [1.0, 2.0]) {
      for (final dark in [false, true]) {
        testWidgets(
          'profile preserves sport-specific data width=$width scale=$scale dark=$dark',
          (t) async {
            t.view.physicalSize = Size(width, 812);
            t.view.devicePixelRatio = 1;
            addTearDown(t.view.resetPhysicalSize);
            addTearDown(t.view.resetDevicePixelRatio);
            final writes = <String>[];
            final auth = await logged((m, p, b, token) async {
              if (m != 'GET') writes.add(p);
              return profileFixture();
            });
            await t.pumpWidget(
              app(
                Theme(
                  data: dark ? AcademyTheme.dark() : AcademyTheme.light(),
                  child: ChildProfile(
                    auth: auth,
                    environment: 'Demo',
                    playerId: 'child',
                    today: '2026-10-22',
                  ),
                ),
                scale: scale,
              ),
            );
            await t.pumpAndSettle();
            expect(find.byType(GuardianProfileHeader), findsOneWidget);
            expect(
              t
                  .widget<GuardianProfileHeader>(
                    find.byType(GuardianProfileHeader),
                  )
                  .performance['overallScore'],
              0,
            );
            await reveal(
              t,
              find.byKey(const ValueKey('profile-enrollment-swimming')),
            );
            await t.tap(
              find.byKey(const ValueKey('profile-enrollment-swimming')),
            );
            await t.pumpAndSettle();
            final header = t.widget<GuardianProfileHeader>(
              find.byType(GuardianProfileHeader),
            );
            expect(header.performance['overallScore'], isNull);
            expect(header.enrollment!['id'], 'swimming');
            expect(find.byType(RadarChart), findsNothing);
            await reveal(t, find.byKey(const ValueKey('profile-tab-att')));
            await t.tap(find.byKey(const ValueKey('profile-tab-att')));
            await t.pumpAndSettle();
            await reveal(t, find.text('كشف السباحة المسجل'));
            expect(find.text('كشف السباحة المسجل'), findsOneWidget);
            expect(find.text('كشف الكرة المسجل'), findsNothing);
            expect(t.takeException(), isNull);
            expect(writes, isEmpty);
            await t.pumpWidget(const SizedBox());
            auth.dispose();
          },
        );
      }
    }
  }
  testWidgets(
    'published history opens its exact existing report and honors initial enrollment',
    (t) async {
      final calls = <String>[];
      final auth = await logged((m, p, b, token) async {
        calls.add(p);
        if (p.endsWith('/profile')) return profileFixture();
        return <String, dynamic>{};
      });
      await t.pumpWidget(
        app(
          ChildProfile(
            auth: auth,
            environment: 'Demo',
            playerId: 'child',
            today: '2026-10-22',
            initialEnrollmentId: 'swimming',
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(
        t
            .widget<GuardianProfileHeader>(find.byType(GuardianProfileHeader))
            .enrollment!['id'],
        'swimming',
      );
      await reveal(t, find.byKey(const ValueKey('profile-tab-prog')));
      await t.tap(find.byKey(const ValueKey('profile-tab-prog')));
      await t.pumpAndSettle();
      await reveal(t, find.text('لا توجد قيم مسجلة'));
      await t.tap(find.text('لا توجد قيم مسجلة'));
      await t.pumpAndSettle();
      expect(
        t.widget<PlayerReport>(find.byType(PlayerReport)).id,
        'report-swimming',
      );
      expect(
        calls,
        contains('/api/v1/guardian/evaluations/report-swimming/report'),
      );
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'logout redacts the entire child profile and a late response cannot restore it',
    (t) async {
      final pending = Completer<Object?>();
      final auth = await logged((m, p, b, token) async => pending.future);
      await t.pumpWidget(
        app(
          ChildProfile(
            auth: auth,
            environment: 'Demo',
            playerId: 'child',
            today: '2026-10-22',
          ),
        ),
      );
      await t.pump();
      await auth.logout();
      await t.pumpAndSettle();
      pending.complete(profileFixture());
      await t.pumpAndSettle();
      await auth.verify('phone', 'challenge', 'otp');
      await t.pumpAndSettle();
      expect(find.byType(GuardianProfileHeader), findsNothing);
      expect(find.text('طفل اصطناعي متعدد الرياضات للاختبار'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'gallery embeds the existing private gallery rather than adding a second page shell',
    (t) async {
      final auth = await logged((m, p, b, token) async {
        if (p.endsWith('/profile')) return profileFixture();
        if (p.contains('consent') || p.contains('storage')) {
          return <String, dynamic>{};
        }
        return <Json>[];
      });
      await t.pumpWidget(
        app(
          ChildProfile(
            auth: auth,
            environment: 'Demo',
            playerId: 'child',
            today: '2026-10-22',
          ),
        ),
      );
      await t.pumpAndSettle();
      await reveal(t, find.byKey(const ValueKey('profile-tab-media')));
      await t.tap(find.byKey(const ValueKey('profile-tab-media')));
      await t.pumpAndSettle();
      await reveal(t, find.byType(GalleryPage));
      expect(t.widget<GalleryPage>(find.byType(GalleryPage)).embedded, isTrue);
      expect(find.byType(Scaffold), findsOneWidget);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
