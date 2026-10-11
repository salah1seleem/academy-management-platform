import 'dart:async';

import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/academy_brand.dart';
import 'package:academy_mobile/features/guardian/daily_nutrition.dart';
import 'package:academy_mobile/features/guardian/tournament_fees.dart';
import 'package:academy_mobile/features/shared/matches.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart';
import 'guardian_test.dart' show logged;
import 'premium_home_test.dart' show shell;

void main() {
  test('Invalid brand values cannot inject URLs or alternate assets', () {
    const fallback = Colors.red;
    expect(AcademyBrand.color('url(example)', fallback), fallback);
    expect(AcademyBrand.color('#123456', fallback), const Color(0xFF123456));
  });
  test(
    'A late tenant branding response cannot reappear after logout',
    () async {
      final pending = Completer<Object?>();
      final auth = AuthController(
        FakeApi(
          (m, p, b, t) => p.contains('/auth/')
              ? Future.value(credentials())
              : pending.future,
        ),
        MemoryStore(),
      );
      await auth.verify('phone', 'challenge', 'otp');
      final brand = AcademyBrand(auth);
      await auth.logout();
      pending.complete({
        'displayName': 'Private old tenant',
        'primaryColor': '#123456',
        'coachAttendanceEnabled': true,
      });
      await Future<void>.delayed(Duration.zero);
      expect(brand.signedIn, false);
      expect(brand.logo, isNull);
      expect(brand.name, 'The Professional Academy');
      expect(brand.coachAttendance, false);
      brand.dispose();
      auth.dispose();
    },
  );
  testWidgets(
    'Daily Home preview is small and keeps detailed suggestions in the library',
    (tester) async {
      await tester.binding.setSurfaceSize(const Size(390, 844));
      addTearDown(() => tester.binding.setSurfaceSize(null));
      final auth = await logged(
        (m, p, b, t) async => {
          'breakfast': <String, dynamic>{},
          'lunch': <String, dynamic>{},
          'dinner': <String, dynamic>{},
          'preTraining': {'arabicName': 'موز'},
          'isTrainingDay': true,
        },
      );
      await tester.pumpWidget(
        shell(
          DailyNutritionCard(
            auth: auth,
            playerId: 'p',
            playerName: 'طفل اختبار',
            today: '2026-09-28',
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('متابعة الغذاء اليومي لطفلك'), findsOneWidget);
      expect(find.text('قبل التدريب · موز'), findsNothing);
      expect(
        tester.getSize(find.byType(DailyNutritionCard)).height,
        lessThan(120),
      );
      expect(tester.takeException(), isNull);
      auth.dispose();
    },
  );
  testWidgets(
    'A malformed nutrition response presents a retry without crashing',
    (tester) async {
      final auth = await logged((m, p, b, t) async => []);
      await tester.pumpWidget(
        shell(
          DailyNutritionCard(
            auth: auth,
            playerId: 'p',
            playerName: 'طفل',
            today: 'invalid',
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('تعذر تحميل وجبات اليوم'), findsOneWidget);
      expect(find.byTooltip('حاول مجددًا'), findsOneWidget);
      expect(tester.takeException(), isNull);
      auth.dispose();
    },
  );
  testWidgets(
    'Tournament selection carries the child and one command key to its independent receipt',
    (tester) async {
      Object? submitted;
      final api = FakeApi((method, path, body, token) async {
        if (path.contains('/auth/')) return credentials();
        if (path.endsWith('/options')) {
          return {
            'enrollments': [
              {
                'id': 'e',
                'sportId': 's',
                'player': 'طفل عرض',
                'branch': 'فرع عرض',
              },
            ],
            'tournaments': [
              {
                'id': 't',
                'sportId': 's',
                'name': 'بطولة عرض',
                'fee': 250,
                'currency': 'EGP',
              },
            ],
            'payments': <Object>[],
          };
        }
        if (method == 'POST') {
          submitted = body;
          return {'id': 'p'};
        }
        return {
          'tournamentName': 'بطولة عرض',
          'player': 'طفل عرض',
          'amount': 250,
          'currency': 'EGP',
          'status': 'Confirmed',
          'receiptNumber': 'TRN-TEST-ONLY',
          'confirmedAtUtc': '2026-09-28T12:00:00Z',
        };
      });
      final auth = AuthController(api, MemoryStore());
      await auth.verify('phone', 'challenge', 'otp');
      await tester.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          home: TournamentFees(auth: auth),
        ),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byType(DropdownButtonFormField<String>).first);
      await tester.pumpAndSettle();
      await tester.tap(find.text('طفل عرض · فرع عرض').last);
      await tester.pumpAndSettle();
      await tester.tap(find.byType(DropdownButtonFormField<String>).last);
      await tester.pumpAndSettle();
      await tester.tap(find.text('بطولة عرض · 250 ج.م').last);
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(FilledButton, 'متابعة إلى الدفع'));
      await tester.pumpAndSettle();
      expect(submitted, {'enrollmentId': 'e', 'tournamentId': 't'});
      expect(api.keys.whereType<String>().single, isNotEmpty);
      expect(find.text('TRN-TEST-ONLY'), findsOneWidget);
      expect(find.text('تم تأكيد التحصيل'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox.shrink());
      auth.dispose();
    },
  );
  testWidgets(
    'No matches means no empty section; published next match is compact',
    (tester) async {
      final auth = await logged((m, p, b, t) async => []);
      await tester.pumpWidget(shell(NextMatchStrip(auth: auth)));
      await tester.pumpAndSettle();
      expect(find.text('مباراة'), findsNothing);
      auth.dispose();
      final second = await logged(
        (m, p, b, t) async => [
          {
            'id': 'm',
            'isUpcoming': true,
            'team': 'منتخب اختبار',
            'opponent': 'فريق آخر',
            'tournament': 'دوري اختبار',
            'matchDate': '2026-10-03',
            'startTime': '17:00:00',
            'venue': 'ملعب تجريبي',
          },
        ],
      );
      await tester.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          home: Scaffold(body: NextMatchStrip(auth: second)),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('منتخب اختبار × فريق آخر'), findsOneWidget);
      expect(tester.takeException(), isNull);
      second.dispose();
    },
  );
}
