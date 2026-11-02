import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:academy_mobile/core/theme/academy_brand.dart';
import 'package:academy_mobile/features/shared/experience_ui.dart';
import 'package:academy_mobile/features/shared/self_profile.dart';
import 'package:academy_mobile/features/shared/profile_avatar.dart';
import 'package:academy_mobile/features/shared/subscription_identity_card.dart';
import 'package:academy_mobile/features/owner/home_destinations.dart';
import 'package:academy_mobile/features/owner/summary_hero.dart';
import 'package:academy_mobile/features/owner/swipe_card_deck.dart';
import 'package:academy_mobile/features/owner/owner_action_rail.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_brand_experience_test.dart' show railApp;
import 'v2_role_workspaces_test.dart' as f;

void main() {
  for (final role in ['AcademyOwner', 'AcademyAdmin', 'Coach', 'Guardian']) {
    testWidgets(
      '$role personal header keeps controls at top, no media/watermark, accessible name and theme',
      (t) async {
        final auth = await f.authenticate(role, (m, p, b, k) async => {}),
            prefs = Appearance()..dark = false;
        final brand = AcademyBrand(auth);
        await brand.refresh(force: true);
        brand.profileName = 'صلاح سليم عرض';
        for (final width in [320.0, 440.0]) {
          t.view.physicalSize = Size(width, 950);
          t.view.devicePixelRatio = 1;
          for (final scale in [1.0, 2.0]) {
            await t.pumpWidget(
              railApp(
                AppearanceScope(
                  appearance: prefs,
                  child: BrandScope(
                    brand: brand,
                    child: ExperienceHeader(
                      auth: auth,
                      subtitle: 'لوحة الدور',
                      lesson: 'home',
                      home: true,
                      account: const Scaffold(),
                    ),
                  ),
                ),
                reduced: true,
                scale: scale,
              ),
            );
            await t.pumpAndSettle();
            expect(
              find.byKey(const ValueKey('home-header-watermark-zone')),
              findsNothing,
            );
            expect(find.byTooltip('شرح الصفحة بالفيديو'), findsNothing);
            expect(find.byType(SelfProfileAvatar), findsOneWidget);
            expect(find.textContaining('صلاح سليم'), findsOneWidget);
            final title = t.widget<Text>(
              find.descendant(
                of: find.byKey(const ValueKey('home-academy-name')),
                matching: find.byType(Text),
              ),
            );
            expect(title.maxLines, 2);
            expect(title.style!.fontWeight, FontWeight.w800);
            expect(find.byKey(const ValueKey('home-today')), findsNothing);
            expect(
              t.getSize(find.byKey(const ValueKey('home-primary-crest'))).width,
              48,
            );
            expect(t.takeException(), isNull, reason: '$role $width $scale');
          }
        }
        await t.tap(find.byKey(const ValueKey('header-appearance-toggle')));
        await t.pumpAndSettle();
        await t.tap(find.text('أزرق'));
        await t.pumpAndSettle();
        expect(prefs.dark, true);
        await t.pumpWidget(const SizedBox());
        brand.dispose();
        auth.dispose();
        prefs.dispose();
        t.view.reset();
      },
    );
  }
  test('avatar variants are stable, gender-safe and use all three role-specific alternatives', () {
    for (final role in ['child', 'coach', 'guardian']) {
      for (final gender in ['Male', 'Female']) {
        final assets = {
          for (var n = 0; n < 50; n++)
            profileAvatarAsset(
              adult: role != 'child',
              gender: gender,
              identity: 'synthetic-$n',
              role: role,
            ),
        };
        expect(assets.length, 3);
        expect(
          assets.every(
            (p) => p.contains('$role-${gender == 'Male' ? 'male' : 'female'}-'),
          ),
          true,
        );
      }
    }
    expect(profileAvatarAsset(gender: null), endsWith('child-neutral.svg'));
    expect(
      profileAvatarAsset(
        adult: true,
        gender: 'Female',
        identity: 'owner',
        role: 'owner',
      ),
      endsWith('owner-female-1.svg'),
    );
  });
  testWidgets(
    'subscription total excludes expired and counts near-expiry once, with separate player link',
    (t) async {
      var status = '', people = 0, total = 0;
      await t.pumpWidget(
        railApp(
          SingleChildScrollView(
            child: SubscriptionIdentityCard(
              data: const {
                'total': 305,
                'active': 255,
                'expiring': 55,
                'expired': 50,
                'players': 300,
              },
              owner: true,
              onTotal: () => total++,
              onStatus: (v) => status = v,
              onPlayers: () => people++,
            ),
          ),
          reduced: true,
        ),
      );
      expect(find.text('255'), findsOneWidget);
      expect(find.text('200'), findsOneWidget);
      expect(find.text('55'), findsOneWidget);
      expect(find.text('50'), findsOneWidget);
      expect(find.text('305'), findsNothing);
      expect(find.textContaining('قرب الانتهاء ضمن'), findsOneWidget);
      await t.tap(find.text('255'));
      expect(total, 1);
      await t.tap(find.text('200'));
      expect(status, 'current');
      await t.tap(find.text('قائمة اللاعبين · 300 لاعب'));
      expect(people, 1);
      expect(t.takeException(), isNull);
    },
  );
  testWidgets(
    'unique player list keeps two sports inside one actionable player card and retains date filters',
    (t) async {
      final paths = <String>[];
      final auth = await f.authenticate('AcademyOwner', (m, p, b, k) async {
        paths.add(p);
        return {
          'total': 1,
          'page': 1,
          'from': '2026-09-26',
          'asOf': '2026-10-26',
          'canOpenPlayer': true,
          'items': [
            {
              'playerId': 'player-a',
              'name': 'لاعب عرض خيالي',
              'gender': 1,
              'sports': [
                {
                  'sport': 'السباحة',
                  'branch': 'فرع العرض',
                  'planType': 'Sessions',
                  'remainingSessions': 4,
                  'price': 200,
                  'currency': 'EGP',
                  'active': true,
                },
                {
                  'sport': 'كرة القدم',
                  'branch': 'فرع العرض',
                  'planType': 'Duration',
                  'endDate': '2026-10-30',
                  'price': 300,
                  'currency': 'EGP',
                  'active': true,
                },
              ],
            },
          ],
        };
      });
      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: AcademyTheme.light(),
          home: HomeSubscriptionPlayers(
            auth: auth,
            filters: const {'from': '2026-09-26', 'asOf': '2026-10-26'},
            uniquePlayers: true,
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('لاعب عرض خيالي'), findsOneWidget);
      expect(find.byType(PlayerAvatar), findsOneWidget);
      expect(t.widget<PlayerAvatar>(find.byType(PlayerAvatar)).size, 54);
      expect(t.widget<PlayerAvatar>(find.byType(PlayerAvatar)).border, false);
      expect(find.text('ملف اللاعب'), findsOneWidget);
      expect(find.text('متبقي 4 حصة'), findsOneWidget);
      expect(paths.single, contains('from=2026-09-26'));
      expect(paths.single, contains('uniquePlayers=true'));
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'reduced-motion actions stay stable, scrolling reaches all commands and decisions remain manual',
    (t) async {
      t.view.physicalSize = const Size(375, 850);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.reset);
      await t.pumpWidget(
        railApp(
          OwnerActionRail(
            actions: [
              for (var n = 0; n < 8; n++)
                OwnerRailAction('إجراء $n', Icons.add, Colors.red, () {}),
            ],
          ),
        ),
      );
      await t.pump();
      await t.pump(const Duration(milliseconds: 40));
      final first = find.text('إجراء 0');
      final beforeAction = t.getTopLeft(first);
      for (var n = 0; n < 40; n++) {
        await t.pump(const Duration(milliseconds: 40));
      }
      expect(t.getTopLeft(first), beforeAction);
      expect(find.text('إجراء 7'), findsNothing);
      await t.scrollUntilVisible(
        find.text('إجراء 7'),
        160,
        scrollable: find.descendant(
          of: find.byKey(const ValueKey('owner-shortcut-scroll')),
          matching: find.byType(Scrollable),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('إجراء 7'), findsOneWidget);
      expect(t.takeException(), isNull);
      await t.pumpWidget(
        railApp(
          const SwipeCardDeck(
            height: 180,
            children: [Text('قرار أول'), Text('قرار ثانٍ')],
          ),
        ),
      );
      await t.pump();
      final manual = t
          .widget<ListView>(find.byKey(const ValueKey('owner-decisions-rail')))
          .controller!;
      final before = manual.offset;
      for (var n = 0; n < 40; n++) {
        await t.pump(const Duration(milliseconds: 40));
      }
      expect(manual.offset, before);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
    },
  );
  testWidgets(
    'compact collections uses a market trend icon and a single-line target label',
    (t) async {
      t.view.physicalSize = const Size(375, 850);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.reset);
      await t.pumpWidget(
        railApp(
          SummaryHero(
            data: const {
              'collections': {'net': 204200, 'changePercentage': -16.4},
              'target': <String, dynamic>{},
              'currency': 'EGP',
            },
            onReport: () {},
            onTarget: () {},
          ),
          reduced: true,
        ),
      );
      expect(
        t
            .widget<Icon>(find.byKey(const ValueKey('collection-trend-icon')))
            .icon,
        Icons.trending_down_rounded,
      );
      expect(find.text('ضبط هدف التحصيل'), findsOneWidget);
      expect(t.takeException(), isNull);
    },
  );
}
