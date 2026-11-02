import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/coach/coach_home_summary.dart';
import 'package:academy_mobile/features/coach/monthly_stars.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show FakeApi, MemoryStore;
import 'coach_test.dart' show coach, coachCredentials;

Widget shell(
  Widget child, {
  bool dark = false,
  double scale = 1,
  bool page = false,
}) => MaterialApp(
  locale: const Locale('ar'),
  supportedLocales: const [Locale('ar')],
  localizationsDelegates: GlobalMaterialLocalizations.delegates,
  theme: dark ? AcademyTheme.dark() : AcademyTheme.light(),
  builder: (context, page) => MediaQuery(
    data: MediaQuery.of(context).copyWith(textScaler: TextScaler.linear(scale)),
    child: page!,
  ),
  home: page ? child : Scaffold(body: SingleChildScrollView(child: child)),
);

const groups = [
  {'id': 'g1', 'arabicName': 'الناشئون · فرع مدينة نصر التجريبي'},
  {'id': 'g2', 'arabicName': 'الأشبال · فرع الشروق التجريبي'},
  {'id': 'g3', 'arabicName': 'مجموعة بلا سجلات'},
];
const sessions = [
  {'trainingGroupId': 'g1', 'recorded': 2, 'present': 2},
  {'trainingGroupId': 'g1', 'recorded': 8, 'present': 4},
  {'trainingGroupId': 'g2', 'recorded': 2, 'present': 0},
  {
    'trainingGroupId': 'sub',
    'group': 'مجموعة المدرب البديل',
    'recorded': 3,
    'present': 3,
  },
  {
    'trainingGroupId': 'g1',
    'recorded': 100,
    'present': 100,
    'status': 'Cancelled',
  },
  {'trainingGroupId': 'g1', 'recorded': 0, 'present': 0, 'playerCount': 999},
];
Object budgets({int remaining = 2}) => {
  'month': '2026-10-01',
  'teams': [
    {
      'id': 'team',
      'name': 'فريق الناشئين ذو الاسم الطويل بأكاديمية العرض الاصطناعية',
      'remaining': remaining,
      'players': [],
    },
  ],
};

void main() {
  for (final dark in [false, true]) {
    for (final remaining in [0, 3]) {
      testWidgets(
        'existing star workspace readable dark=$dark remaining=$remaining without enabling exhausted grant',
        (t) async {
          final auth = await coach(
            (m, p, b, k) async => {
              'month': '2026-10-01',
              'teams': [
                {
                  'id': 'team',
                  'name': 'فريق اختباري',
                  'remaining': remaining,
                  'players': [
                    {'id': 'child', 'arabicName': 'لاعب اصطناعي', 'stars': 2},
                  ],
                },
              ],
            },
          );
          await t.pumpWidget(
            shell(CoachMonthlyStars(auth: auth), dark: dark, page: true),
          );
          await t.pumpAndSettle();
          final text = t.widget<Text>(find.text('2 ★ هذا الشهر'));
          final foreground = text.style!.color!.computeLuminance();
          final background = (dark ? AcademyTheme.dark() : AcademyTheme.light())
              .colorScheme
              .surface
              .computeLuminance();
          final ratio = foreground > background
              ? (foreground + .05) / (background + .05)
              : (background + .05) / (foreground + .05);
          expect(ratio, greaterThanOrEqualTo(4.5));
          final action = t.widget<IconButton>(
            find.byKey(const ValueKey('star-team-child')),
          );
          expect(action.onPressed == null, remaining == 0);
          expect(t.takeException(), isNull);
          await t.pumpWidget(const SizedBox());
          auth.dispose();
        },
      );
    }
  }

  testWidgets(
    'recorded weighted attendance preserves zero, substitutes, cancellations and unrecorded semantics',
    (t) async {
      await t.pumpWidget(
        shell(
          const CoachAttendanceSummary(
            groups: groups,
            sessions: sessions,
            today: '2026-10-23',
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(
        find.text('60%'),
        findsNWidgets(2),
      ); // 9/15 total, 6/10 first group; not mean of percentages.
      expect(find.text('0%'), findsOneWidget);
      expect(find.text('100%'), findsOneWidget);
      expect(find.text('—'), findsOneWidget); // no records is not absence.
      expect(find.text('مجموعة المدرب البديل'), findsOneWidget);
      expect(find.text('2026-10-01 — 2026-10-23'), findsOneWidget);
      expect(t.takeException(), isNull);
    },
  );
  testWidgets(
    'empty attendance is unknown rather than fabricated zero percent',
    (t) async {
      await t.pumpWidget(
        shell(
          const CoachAttendanceSummary(
            groups: groups,
            sessions: [],
            today: '2026-10-23',
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('0%'), findsNothing);
      expect(find.text('لا يوجد حضور مسجل خلال الفترة.'), findsOneWidget);
      expect(find.text('—'), findsNWidgets(4));
    },
  );
  for (final width in [375.0, 440.0]) {
    for (final dark in [false, true]) {
      for (final scale in [1.0, 2.0]) {
        testWidgets(
          'overview $width dark=$dark scale=$scale keeps long identities and actual budgets readable',
          (t) async {
            t.view.physicalSize = Size(width, 900);
            t.view.devicePixelRatio = 1;
            addTearDown(t.view.resetPhysicalSize);
            addTearDown(t.view.resetDevicePixelRatio);
            final auth = await coach((m, p, b, k) async => budgets());
            await t.pumpWidget(
              shell(
                Column(
                  children: [
                    const CoachAttendanceSummary(
                      groups: groups,
                      sessions: sessions,
                      today: '2026-10-23',
                    ),
                    CoachHomeStarBudgets(auth: auth),
                  ],
                ),
                dark: dark,
                scale: scale,
              ),
            );
            await t.pumpAndSettle();
            await t.ensureVisible(
              find.byKey(const ValueKey('coach-star-remaining-team')),
            );
            await t.pumpAndSettle();
            expect(find.text('2 من 3 متبقية'), findsOneWidget);
            expect(find.byIcon(Icons.star_rounded), findsNWidgets(2));
            expect(t.takeException(), isNull);
            await t.pumpWidget(const SizedBox());
            auth.dispose();
          },
        );
      }
    }
  }
  testWidgets(
    'returning from the existing star workspace rereads remaining budget without a grant',
    (t) async {
      var reads = 0, writes = 0;
      final auth = await coach((m, p, b, k) async {
        if (m != 'GET') writes++;
        reads++;
        return budgets(remaining: reads > 1 ? 0 : 2);
      });
      await t.pumpWidget(shell(CoachHomeStarBudgets(auth: auth)));
      await t.pumpAndSettle();
      expect(find.text('2 من 3 متبقية'), findsOneWidget);
      await t.tap(find.widgetWithText(OutlinedButton, 'منح نجمة'));
      await t.pumpAndSettle();
      expect(find.text('نجوم فريقي الشهرية'), findsOneWidget);
      await t.tap(find.byTooltip('رجوع'));
      await t.pumpAndSettle();
      expect(find.text('0 من 3 متبقية'), findsOneWidget);
      expect(writes, 0);
      expect(reads, 3);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'failed budget read has retry, never fabricated three available stars',
    (t) async {
      var fail = true;
      final auth = await coach((m, p, b, k) async {
        if (fail) throw const ApiFailure(0, 'تعذر الاتصال');
        return budgets(remaining: 0);
      });
      await t.pumpWidget(shell(CoachHomeStarBudgets(auth: auth)));
      await t.pumpAndSettle();
      expect(find.text('تعذر الاتصال'), findsOneWidget);
      expect(find.text('3 من 3 متبقية'), findsNothing);
      fail = false;
      await t.tap(find.text('إعادة المحاولة'));
      await t.pumpAndSettle();
      expect(find.text('0 من 3 متبقية'), findsOneWidget);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'second academy replaces old budget, and late old response cannot restore private team after logout',
    (t) async {
      var second = false;
      final late = Completer<Object?>();
      final auth = AuthController(
        FakeApi((m, p, b, k) async {
          if (p.contains('/auth/')) {
            return {
              ...coachCredentials(),
              'memberships': [
                {
                  'id': 'coach',
                  'academyId': second ? 'academy-two' : 'academy-one',
                  'academyName': second ? 'الثانية' : 'الأولى',
                  'role': 'Coach',
                  'contextRevision': second ? 2 : 1,
                },
              ],
            };
          }
          if (second) return late.future;
          return budgets();
        }),
        MemoryStore(),
      )..restoring = false;
      await auth.verify('synthetic', 'challenge', '246810');
      await t.pumpWidget(shell(CoachHomeStarBudgets(auth: auth)));
      await t.pumpAndSettle();
      expect(find.text('2 من 3 متبقية'), findsOneWidget);
      second = true;
      await auth.verify('synthetic', 'challenge', '246810');
      await t.pump();
      expect(find.text('2 من 3 متبقية'), findsNothing);
      await auth.logout();
      await t.pump();
      late.complete(budgets(remaining: 1));
      await t.pumpAndSettle();
      expect(find.textContaining('من 3 متبقية'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
