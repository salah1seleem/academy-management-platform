import 'introduction_test_helpers.dart';

import 'dart:async';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/core/theme/approved_role_introduction.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/shared/role_intro_gate.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as f;

Widget view(
  Widget child, {
  bool dark = false,
  Locale locale = const Locale('ar'),
  double scale = 1,
  double topPadding = 0,
}) => MaterialApp(
  locale: locale,
  supportedLocales: const [Locale('ar'), Locale('en')],
  localizationsDelegates: GlobalMaterialLocalizations.delegates,
  theme: dark ? AcademyTheme.dark() : AcademyTheme.light(),
  builder: (context, child) => MediaQuery(
    data: MediaQuery.of(context).copyWith(
      textScaler: TextScaler.linear(scale),
      padding: EdgeInsets.only(top: topPadding),
    ),
    child: child!,
  ),
  home: child,
);

const titles = {
  'AcademyOwner': [
    'صورة واضحة لأكاديميتك',
    'تابع التفاصيل التي تصنع الفرق',
    'تجربة تليق باسم أكاديميتك',
  ],
  'AcademyAdmin': [
    'يومك أوضح، وشغلك أسهل',
    'كل اشتراك في وقته',
    'من التسجيل إلى التدريب',
  ],
  'Coach': [
    'حصصك جاهزة أمامك',
    'تابع تطور كل لاعب',
    'حضور واضح، ومتابعة مستمرة',
  ],
  'Guardian': [
    'شوف تطور ابنك بوضوح',
    'ابقَ قريبًا من رحلته',
    'اشتراكه ومتابعته بسهولة',
  ],
};
void main() {
  for (final role in titles.keys) {
    for (final width in [375.0, 440.0]) {
      for (final dark in [false, true]) {
        testWidgets(
          'approved $role introduction keeps all three topics and controls width=$width dark=$dark',
          (t) async {
            t.view.physicalSize = Size(width, width == 375 ? 812 : 956);
            t.view.devicePixelRatio = 1;
            addTearDown(t.view.reset);
            var complete = 0;
            await t.pumpWidget(
              view(
                AcademyOnboarding(
                  role: role,
                  sportTheme: 'football',
                  features: const ['training'],
                  onComplete: () async {
                    complete++;
                  },
                ),
                dark: dark,
              ),
            );
            await t.pumpAndSettle();
            for (var i = 0; i < 3; i++) {
              expect(find.text(titles[role]![i]), findsOneWidget);
              expect(find.text('تخطي'), findsNothing);
              expect(
                Directionality.of(t.element(find.text(titles[role]![i]))),
                TextDirection.rtl,
              );
              expect(find.text('248,000'), findsNothing);
              expect(find.text('84%'), findsNothing);
              expect(t.takeException(), isNull);
              if (i < 2) {
                await t.tap(find.text('التالي'));
                await t.pumpAndSettle();
              }
            }
            await t.tap(find.text('ابدأ'));
            await t.pumpAndSettle();
            expect(complete, 1);
            await t.pumpWidget(const SizedBox());
          },
        );
      }
    }
  }
  testWidgets(
    'photo swipes and accessible dots navigate; a save in progress blocks navigation',
    (t) async {
      var busy = false;
      Widget page() => view(
        AcademyOnboarding(
          role: 'AcademyOwner',
          sportTheme: 'football',
          features: const ['training'],
          busy: busy,
          onComplete: () async {},
        ),
      );
      await t.pumpWidget(page());
      await t.pumpAndSettle();
      await t.drag(
        find.byKey(const ValueKey('intro-photo')),
        const Offset(150, 0),
      );
      await t.pumpAndSettle();
      expect(find.text(titles['AcademyOwner']![1]), findsOneWidget);
      await t.tap(find.text('السابق'));
      await t.pumpAndSettle();
      expect(find.text(titles['AcademyOwner']![0]), findsOneWidget);
      // Future progress indicators cannot bypass the manual pages.
      await t.tap(find.text('التالي'));
      await t.pumpAndSettle();
      await t.tap(find.text('التالي'));
      await t.pumpAndSettle();
      expect(find.text(titles['AcademyOwner']![2]), findsOneWidget);
      busy = true;
      await t.pumpWidget(page());
      await t.pumpAndSettle();
      await t.drag(
        find.byKey(const ValueKey('intro-photo')),
        const Offset(-150, 0),
      );
      await t.pumpAndSettle();
      expect(find.text(titles['AcademyOwner']![2]), findsOneWidget);
      expect(
        t.widget<FilledButton>(find.byType(FilledButton)).onPressed,
        isNull,
      );
    },
  );
  for (final role in titles.keys) {
    testWidgets(
      '$role replays its own introduction from account without clearing completion',
      (t) async {
        final auth = await f.authenticate(role, (m, p, b, k) async {
          if (p == '/api/v1/academy/experience') {
            return {
              'displayName': 'أكاديمية الاختبار',
              'onboarding': {
                'sportTheme': 'football',
                'features': ['training'],
              },
            };
          }
          // Account replay must remain available independently of home reads.
          if (p == '/api/v1/academy/calendar') {
            throw StateError('synthetic dashboard unavailable');
          }
          return [];
        });
        final prefs = Appearance();
        await prefs.completeRoleIntro(role, 'football');
        await t.pumpWidget(
          AcademyApp(auth: auth, environment: 'Demo', appearance: prefs),
        );
        await t.pumpAndSettle();
        await t.tap(find.byTooltip('حسابي'));
        await t.pumpAndSettle();
        await t.ensureVisible(find.text('ابدأ من هنا'));
        await t.tap(find.text('ابدأ من هنا'));
        await t.pumpAndSettle();
        expect(find.byType(RoleIntroReplay), findsOneWidget);
        expect(find.text(titles[role]![0]), findsOneWidget);
        await finishRoleIntroduction(t);
        await t.pumpAndSettle();
        expect(find.byType(RoleIntroReplay), findsNothing);
        expect(await prefs.hasRoleIntro(role, 'football'), true);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
        prefs.dispose();
      },
    );
  }
  for (final failed in [false, true]) {
    testWidgets(
      'owner account and introduction stay reachable when home ${failed ? 'fails' : 'is pending'}',
      (t) async {
        final pending = Completer<Object?>();
        final auth = await f.authenticate('AcademyOwner', (m, p, b, k) async {
          if (p == '/api/v1/academy/experience') {
            return {
              'displayName': 'أكاديمية الاختبار',
              'onboarding': {
                'sportTheme': 'football',
                'features': ['training'],
              },
            };
          }
          if (p == '/api/v1/academy/calendar') {
            if (failed) throw StateError('synthetic read failure');
            return pending.future;
          }
          return [];
        });
        final prefs = Appearance();
        await prefs.completeRoleIntro('AcademyOwner', 'football');
        await t.pumpWidget(
          AcademyApp(auth: auth, environment: 'Demo', appearance: prefs),
        );
        // An unresolved home read intentionally keeps its progress indicator live.
        for (var i = 0; i < 50; i++) {
          await t.pump(const Duration(milliseconds: 50));
        }
        expect(find.byTooltip('حسابي'), findsOneWidget);
        if (failed) expect(find.textContaining('تعذر تحميل'), findsWidgets);
        await t.tap(find.byTooltip('حسابي'));
        await t.pump(const Duration(milliseconds: 400));
        await t.ensureVisible(find.text('ابدأ من هنا'));
        await t.tap(find.text('ابدأ من هنا'));
        await t.pumpAndSettle();
        expect(find.byType(RoleIntroReplay), findsOneWidget);
        expect(await prefs.hasRoleIntro('AcademyOwner', 'football'), true);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
        prefs.dispose();
        if (!pending.isCompleted) pending.complete(<String, Object>{});
      },
    );
  }
  testWidgets(
    'replay is permanently redacted after logout even when the same role logs in again',
    (t) async {
      final auth = await f.authenticate('Coach', (m, p, b, k) async => []);
      await t.pumpWidget(view(RoleIntroReplay(auth: auth)));
      await t.pumpAndSettle();
      expect(find.byType(AcademyOnboarding), findsOneWidget);
      await auth.logout();
      await t.pumpAndSettle();
      await auth.verify('phone', 'challenge', 'code');
      await t.pumpAndSettle();
      expect(find.byType(AcademyOnboarding), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'multi-sport introduction retains approved photo composition without claiming football',
    (t) async {
      await t.pumpWidget(
        view(
          AcademyOnboarding(
            role: 'Guardian',
            sportTheme: 'general',
            features: const ['training'],
            onComplete: () async {},
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('شوف تطور ابنك بوضوح'), findsOneWidget);
      final photos = t.widgetList<Image>(
        find.descendant(
          of: find.byKey(const ValueKey('intro-photo')),
          matching: find.byType(Image),
        ),
      );
      expect(
        photos.where(
          (p) =>
              p.image is AssetImage &&
              (p.image as AssetImage).assetName.startsWith(
                'assets/demo/portraits/',
              ),
        ),
        hasLength(1),
      );
      expect(find.byIcon(Icons.sports_soccer), findsNothing);
      expect(t.takeException(), isNull);
    },
  );
  for (final width in [375.0, 440.0]) {
    for (final dark in [false, true]) {
      for (final language in ['ar', 'en']) {
        testWidgets(
          'photo header remains readable and separate at 200% width=$width dark=$dark language=$language',
          (t) async {
            t.view.physicalSize = Size(width, width == 375 ? 667 : 956);
            t.view.devicePixelRatio = 1;
            addTearDown(t.view.reset);
            const name =
                'أكاديمية الاختبار المصرية للتدريب الرياضي وتنمية المواهب';
            await t.pumpWidget(
              view(
                ApprovedRoleIntroduction(
                  role: 'Guardian',
                  sportTheme: 'general',
                  training: true,
                  academyName: name,
                  crest: const SizedBox(width: 40, height: 40),
                  onComplete: () async {},
                ),
                dark: dark,
                locale: Locale(language),
                scale: 2,
                topPadding: width == 375 ? 20 : 62,
              ),
            );
            await t.pumpAndSettle();
            await t.tap(find.text(language == 'ar' ? 'التالي' : 'Next'));
            await t.pumpAndSettle();
            await t.tap(find.text(language == 'ar' ? 'التالي' : 'Next'));
            await t.pumpAndSettle();
            final header = find.byKey(const ValueKey('intro-brand-header'));
            final photo = find.byKey(const ValueKey('intro-photo'));
            expect(
              t.getRect(header).bottom,
              lessThanOrEqualTo(t.getRect(photo).top),
            );
            expect(
              find.byKey(const ValueKey('intro-feature-chip')),
              findsNothing,
            );
            final theme = Theme.of(t.element(header));
            double contrast(Color a, Color b) {
              final x = a.computeLuminance(), y = b.computeLuminance();
              return ((x > y ? x : y) + .05) / ((x > y ? y : x) + .05);
            }

            expect(
              contrast(
                theme.colorScheme.onSurface,
                theme.scaffoldBackgroundColor,
              ),
              greaterThanOrEqualTo(4.5),
            );
            final kicker = find.byWidgetPredicate(
              (w) =>
                  w is Text &&
                  w.data ==
                      (language == 'ar'
                          ? 'الاشتراك والمتابعة'
                          : 'Subscriptions and follow-up') &&
                  w.style?.fontSize == 12,
            );
            expect(
              contrast(
                t.widget<Text>(kicker).style!.color!,
                theme.scaffoldBackgroundColor,
              ),
              greaterThanOrEqualTo(4.5),
            );
            expect(
              t.getRect(find.byType(FilledButton)).bottom,
              lessThanOrEqualTo(width == 375 ? 667 : 956),
            );
            expect(t.takeException(), isNull);
          },
        );
      }
    }
  }
}
