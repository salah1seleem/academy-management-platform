import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/guardian/guardian_profile.dart';
import 'package:academy_mobile/features/guardian/guardian_content.dart';
import 'package:academy_mobile/features/guardian/guardian_payments.dart';
import 'package:academy_mobile/features/shared/matches.dart';
import 'package:academy_mobile/features/guardian/daily_nutrition.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/visibility.dart';

import 'guardian_test.dart' show logged;
import 'premium_home_test.dart' show shell;

Json child(String id, {bool expired = false}) => {
  'id': id,
  'arabicName': id == 'a' ? 'لينا طفلة العرض' : 'آدم طفل العرض',
  'dateOfBirth': '2018-03-12',
  'latestEvaluation': {
    'id': 'report-$id',
    'sport': 'كرة القدم',
    'evaluationDate': '2026-10-12',
    'overallScore': id == 'a' ? 0 : 82,
  },
  'sports': [
    {
      'id': 'enrollment-$id',
      'sport': 'كرة القدم',
      'branch': 'فرع مدينة نصر',
      'group': 'براعم أ',
      'nextSession': {
        'id': 'session-$id',
        'sessionDate': '2026-10-16',
        'startTime': '18:00:00',
        'endTime': '19:00:00',
        'resource': 'ملعب التدريب',
      },
      'period': {
        'status': expired ? 'Expired' : 'Active',
        'startDate': '2026-09-01',
        'endDate': expired ? '2026-10-01' : '2026-10-30',
        'plan': 'باقة التدريب الشهرية',
      },
    },
  ],
};
Json home() => {
  'academyName': 'أكاديمية العرض الرياضي',
  'guardianName': 'ولي أمر اصطناعي',
  'today': '2026-09-28',
  'trainingToday': '2026-10-15',
  'children': [child('a', expired: true), child('b')],
};
Object? response(String path) {
  if (path == '/api/v1/guardian/home') return home();
  if (path.contains('nutrition/recommendations')) {
    return {
      'breakfast': <String, dynamic>{},
      'lunch': <String, dynamic>{},
      'dinner': <String, dynamic>{},
      'isTrainingDay': true,
    };
  }
  if (path.contains('/report')) {
    return {
      'id': 'report-a',
      'player': 'لينا طفلة العرض',
      'sport': 'كرة القدم',
      'reportingPeriod': 'متابعة منشورة',
      'evaluationDate': '2026-10-12',
      'overallScore': 0,
      'performanceMean': {
        'value': 0,
        'scoredCriteria': 1,
        'applicableCriteria': 2,
        'coveragePercent': 50,
      },
      'isFootballReport': false,
      'criteria': <Object>[],
    };
  }
  return [];
}

Widget app(
  AuthController auth, {
  bool dark = true,
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
  home: GuardianHome(
    auth: auth,
    environment: 'Demo',
    accountPage: const Scaffold(body: Text('الحساب')),
  ),
);

void main() {
  testWidgets(
    'Compact shortcuts keep training, match and nutrition scoped to the linked child',
    (tester) async {
      final paths = <String>[];
      final auth = await logged((m, p, b, t) async {
        paths.add(p);
        if (p == '/api/v1/my-matches?upcomingOnly=true') {
          return [
            {
              'id': 'match-b',
              'isUpcoming': true,
              'matchDate': '2026-10-17',
              'startTime': '18:30:00',
              'children': ['b'],
            },
          ];
        }
        return response(p);
      });
      await tester.pumpWidget(app(auth));
      await tester.pumpAndSettle();
      final childA = find.byKey(const ValueKey('child-a'));
      final childB = find.byKey(const ValueKey('child-b'));
      Finder inCard(Finder card, String label) =>
          find.descendant(of: card, matching: find.text(label));
      expect(inCard(childA, 'المباراة القادمة'), findsNothing);
      expect(inCard(childA, 'الحصة القادمة'), findsOneWidget);
      expect(find.byKey(const ValueKey('child-home-details')), findsNothing);
      expect(find.text('أقرب المواعيد لأبنائي'), findsNothing);
      expect(find.text('أرصدة الأسرة'), findsNothing);
      final session = inCard(childA, 'الحصة القادمة');
      await revealTestTarget(tester, session);
      await tester.tap(session);
      await tester.pumpAndSettle();
      final records = tester.widget<ProfileRecords>(
        find.byType(ProfileRecords),
      );
      expect(records.playerId, 'a');
      expect(records.enrollmentId, 'enrollment-a');
      tester.state<NavigatorState>(find.byType(Navigator).first).pop();
      await tester.pumpAndSettle();
      final sibling = find.byKey(const ValueKey('child-tab-b'));
      await revealTestTarget(tester, sibling);
      await tester.tap(sibling);
      await tester.pumpAndSettle();
      final nextMatch = inCard(childB, 'المباراة القادمة');
      await revealTestTarget(tester, nextMatch);
      await tester.tap(nextMatch);
      await tester.pumpAndSettle();
      final matches = tester.widget<MatchSchedule>(find.byType(MatchSchedule));
      expect(matches.playerId, 'b');
      expect(matches.matchId, 'match-b');
      tester.state<NavigatorState>(find.byType(Navigator).first).pop();
      await tester.pumpAndSettle();
      final nutrition = inCard(childB, 'مكتبة التغذية');
      await revealTestTarget(tester, nutrition);
      await tester.tap(nutrition);
      await tester.pumpAndSettle();
      final library = tester.widget<NutritionLibrary>(
        find.byType(NutritionLibrary),
      );
      expect(library.playerId, 'b');
      expect(library.today, '2026-10-15');
      expect(
        paths.where((p) => p == '/api/v1/my-matches?upcomingOnly=true').length,
        1,
      );
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Receipts can switch to the selected linked child and back to all payments',
    (tester) async {
      final paths = <String>[];
      final auth = await logged((method, path, body, token) async {
        paths.add(path);
        final selected = Uri.parse(path).queryParameters['playerId'];
        return {
          'children': [
            {'id': 'a', 'arabicName': 'لينا العرض'},
            {'id': 'b', 'arabicName': 'آدم العرض'},
          ],
          'items': [
            for (final id in ['a', 'b'])
              if (selected == null || selected == id)
                {
                  'id': id,
                  'receiptNumber': 'RC-$id',
                  'player': 'لاعب $id',
                  'amount': 1000,
                  'currency': 'EGP',
                  'paidAtUtc': '2026-10-18',
                },
          ],
        };
      });
      await tester.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: AcademyTheme.light(),
          home: ReceiptsPage(auth: auth),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.textContaining('RC-a'), findsOneWidget);
      expect(find.textContaining('RC-b'), findsOneWidget);
      await tester.tap(find.byKey(const ValueKey('family-filter-b')));
      await tester.pumpAndSettle();
      expect(find.textContaining('RC-a'), findsNothing);
      expect(find.textContaining('RC-b'), findsOneWidget);
      expect(
        paths.last,
        '/api/v1/guardian/subscriptions/receipts?page=1&allIssued=true&playerId=b',
      );
      await tester.tap(find.byKey(const ValueKey('family-filter-')));
      await tester.pumpAndSettle();
      expect(find.textContaining('RC-a'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Nutrition preview uses the actual training day and retries failed reads',
    (tester) async {
      var fail = true;
      final paths = <String>[];
      final auth = await logged((m, p, b, t) async {
        paths.add(p);
        if (fail) throw const ApiFailure(0, 'تعذر الاتصال');
        return response(p);
      });
      await tester.pumpWidget(
        shell(
          DailyNutritionCard(
            auth: auth,
            playerId: 'a',
            playerName: 'لينا',
            today: '2026-10-15',
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('تعذر تحميل وجبات اليوم'), findsOneWidget);
      fail = false;
      await tester.tap(find.byIcon(Icons.refresh));
      await tester.pumpAndSettle();
      expect(find.text('اقتراحات من مكتبة التغذية'), findsOneWidget);
      expect(paths.every((p) => p.endsWith('?date=2026-10-15')), isTrue);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'No linked children leaves real family services available without private child requests',
    (tester) async {
      final paths = <String>[];
      final auth = await logged((m, p, b, t) async {
        paths.add(p);
        return p == '/api/v1/guardian/home'
            ? {...home(), 'children': <Object>[]}
            : [];
      });
      await tester.pumpWidget(app(auth));
      await tester.pumpAndSettle();
      expect(find.text('لا يوجد أبناء مرتبطون بهذا الحساب.'), findsOneWidget);
      expect(find.byType(DropdownButtonFormField<String>), findsNothing);
      expect(
        find.byKey(const ValueKey('guardian-action-اشتراك جديد')),
        findsOneWidget,
      );
      expect(paths.any((p) => p.contains('/children/')), isFalse);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets(
      'Guardian home at $width keeps all services readable in both languages/appearances and 200% text',
      (tester) async {
        tester.view.physicalSize = Size(width, 900);
        tester.view.devicePixelRatio = 1;
        addTearDown(tester.view.resetPhysicalSize);
        addTearDown(tester.view.resetDevicePixelRatio);
        final paths = <String>[];
        final auth = await logged((m, p, b, t) async {
          paths.add(p);
          return response(p);
        });
        for (final dark in [false, true]) {
          for (final locale in ['ar', 'en']) {
            await tester.pumpWidget(
              app(auth, dark: dark, locale: locale, scale: 2),
            );
            await tester.pumpAndSettle();
            expect(find.byKey(const ValueKey('child-a')), findsOneWidget);
            expect(
              find.byKey(const ValueKey('child-home-details')),
              findsNothing,
            );
            for (final action in [
              'اشتراك جديد',
              'تجديد الاشتراك',
              'تجديد اشتراك لغيره',
              'تقييم المدربين',
              'شكوى أو اقتراح',
            ]) {
              final key = find.byKey(ValueKey('guardian-action-$action'));
              await revealTestTarget(tester, key);
              expect(key, findsOneWidget);
            }
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
