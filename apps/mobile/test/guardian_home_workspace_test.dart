import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/guardian/daily_nutrition.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

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
    data: MediaQuery.of(context).copyWith(textScaler: TextScaler.linear(scale)),
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
    'Child selection scopes its session, subscription, report and nutrition; renewal preserves enrollment context without writes',
    (tester) async {
      final paths = <String>[];
      var writes = 0;
      final auth = await logged((m, p, b, t) async {
        paths.add(p);
        if (m != 'GET') writes++;
        return response(p);
      });
      await tester.pumpWidget(app(auth));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const ValueKey('select-child-a')));
      await tester.pumpAndSettle();
      expect(find.byKey(const ValueKey('child-a')), findsOneWidget);
      expect(find.byKey(const ValueKey('child-b')), findsNothing);
      expect(find.text('الحصة القادمة · آدم طفل العرض'), findsNothing);
      await tester.tap(find.text('التقييمات'));
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.text('تطور لينا طفلة العرض'),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      expect(
        find.descendant(
          of: find.byType(GuardianLatestProgress),
          matching: find.text('0'),
        ),
        findsOneWidget,
      );
      expect(paths.where((p) => p == '/api/v1/guardian/home').length, 1);
      await tester.scrollUntilVisible(
        find.text('الاشتراك'),
        -180,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.tap(find.text('الاشتراك'));
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.byKey(const ValueKey('home-renew-enrollment-a')),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.tap(find.byKey(const ValueKey('home-renew-enrollment-a')));
      await tester.pumpAndSettle();
      expect(
        paths,
        contains(
          '/api/v1/guardian/subscriptions/enrollments/enrollment-a/plans',
        ),
      );
      expect(
        paths,
        isNot(contains('/api/v1/guardian/subscriptions/enrollments')),
      );
      expect(find.text('اختيار الباقة'), findsOneWidget);
      expect(writes, 0);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Latest published progress opens that exact report and retains zero score',
    (tester) async {
      final paths = <String>[];
      final auth = await logged((m, p, b, t) async {
        paths.add(p);
        return response(p);
      });
      await tester.pumpWidget(app(auth));
      await tester.pumpAndSettle();
      await tester.tap(find.text('التقييمات'));
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.text('تطور لينا طفلة العرض'),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('تطور لينا طفلة العرض'));
      await tester.pumpAndSettle();
      expect(paths, contains('/api/v1/guardian/evaluations/report-a/report'));
      expect(find.text('تقرير اللاعب'), findsOneWidget);
      expect(find.text('0'), findsOneWidget);
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
      expect(find.text('متابعة الغذاء اليومي لطفلك'), findsOneWidget);
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
            await tester.scrollUntilVisible(
              find.byKey(const ValueKey('child-section-3')),
              180,
              scrollable: find.byType(Scrollable).first,
            );
            await tester.ensureVisible(
              find.byKey(const ValueKey('child-section-3')),
            );
            await tester.tap(find.byKey(const ValueKey('child-section-3')));
            await tester.pumpAndSettle();
            for (final action in [
              'اشتراك جديد',
              'تجديد الاشتراك',
              'تجديد اشتراك لغيره',
              'الباقات',
              'المتجر',
              'البطولات',
              'زيارة يومية',
            ]) {
              final key = find.byKey(ValueKey('guardian-action-$action'));
              await tester.scrollUntilVisible(
                key,
                250,
                scrollable: find.byType(Scrollable).first,
              );
              expect(key, findsOneWidget);
            }
            expect(
              paths.any(
                (p) => p.contains(
                  '/children/a/nutrition/recommendations?date=2026-10-15',
                ),
              ),
              isTrue,
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
