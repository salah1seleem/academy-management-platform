import 'package:academy_mobile/features/owner/owner_welcome_banner.dart';
import 'package:academy_mobile/core/theme/academy_home_skin.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/owner/refined_home_panels.dart';
import 'package:academy_mobile/features/owner/owner_honors_section.dart';
import 'package:academy_mobile/features/owner/honor_portrait_card.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:academy_mobile/features/owner/operating_monthly_chart.dart';
import 'package:academy_mobile/features/shared/experience_ui.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:academy_mobile/features/shared/owner_brand_navigation.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as f;
import 'owner_test.dart' as legacy;

const collections = <String, dynamic>{
  'today': 0,
  'todayCash': 0,
  'todayOnline': 0,
  'month': 7000,
  'monthCash': 6000,
  'monthOnline': 1000,
  'todaySources': <Object>[],
  'todayBranches': <Object>[],
  'monthSources': [
    {
      'label': 'اشتراكات التدريب',
      'amount': 7000,
      'collectionType': 'Subscription',
    },
  ],
  'monthBranches': [
    {'id': 'b1', 'name': 'فرع الشروق', 'amount': 7000},
  ],
};
const cockpit = <String, dynamic>{
  'currency': 'EGP',
  'collections': {'net': 37434, 'changePercentage': 54.0},
  'target': {'amount': 45000, 'achievement': 83},
  'overdue': {'amount': 540, 'families': 3},
  'risk': {'players': 10},
  'attendance': {'percentage': 78.8},
  'capacity': {'seats': 130},
  'members': {'players': 50, 'groups': 12},
  'followups': {'overdue': 1},
};
const coachRows = <Map<String, dynamic>>[
  {
    'id': 'c2',
    'name': 'مدرب ثان',
    'parentRating': 2.5,
    'groupCount': 9,
    'sessionCount': 30,
    'parentRatingCount': 2,
    'branches': ['b1'],
    'sports': ['كرة القدم'],
  },
  {
    'id': 'c1',
    'name': 'مدرب أعلى تقييمًا',
    'parentRating': 4.9,
    'groupCount': 2,
    'sessionCount': 6,
    'parentRatingCount': 3,
    'branches': ['b1'],
    'sports': ['كرة القدم'],
  },
];
const statement = <String, dynamic>{
  'currency': 'EGP',
  'surplus': 5500,
  'totalIncome': 7000,
  'totalExpenses': 1500,
  'income': [
    {'label': 'اشتراكات محصلة', 'amount': 7000},
  ],
  'expenses': [
    {'label': 'رواتب مسددة', 'amount': 1500},
  ],
  'monthly': [
    {
      'from': '2026-10-01',
      'to': '2026-10-23',
      'income': 7000,
      'expenses': 1500,
      'surplus': 5500,
    },
  ],
};
void main() {
  for (final width in [375.0, 440.0]) {
    for (final scale in [1.0, 2.0]) {
      for (final dark in [false, true]) {
        testWidgets(
          'owner home halves and controls $width scale=$scale dark=$dark',
          (t) async {
            t.view.physicalSize = Size(width, 956);
            t.view.devicePixelRatio = 1;
            addTearDown(t.view.reset);
            final paths = <String>[];
            final auth = await f.authenticate('AcademyOwner', (
              m,
              p,
              b,
              token,
            ) async {
              paths.add(p);
              if (p.contains('featured-players')) {
                return {
                  'items': [
                    {
                      'id': 'p1',
                      'name': 'لاعب تجريبي أول',
                      'score': 91,
                      'stars': 3,
                      'branch': 'فرع الشروق',
                    },
                  ],
                };
              }
              if (p.contains('vip-members')) {
                return {
                  'items': [
                    {
                      'id': 'g1',
                      'displayName': 'عضو تجريبي أول',
                      'monthlySubscriptionValue': 9000,
                      'currency': 'EGP',
                    },
                  ],
                };
              }
              return {'items': <Object>[], 'unread': 0};
            });
            var followups = 0;
            String? reminder;
            String selectedBranch = '';
            await t.pumpWidget(
              f.app(
                Theme(
                  data: dark
                      ? AcademyTheme.dark(primary: const Color(0xFF157868))
                      : AcademyTheme.light(primary: const Color(0xFF157868)),
                  child: AcademyHomeSkin(
                    child: Scaffold(
                      body: SingleChildScrollView(
                        child: Padding(
                          padding: const EdgeInsets.all(16),
                          child: Column(
                            children: [
                              const OwnerWelcomeBanner(),
                              ExperienceHeader(
                                auth: auth,
                                subtitle: 'لوحة المالك',
                                lesson: 'home',
                                account: const SizedBox(),
                              ),
                              AcademySummaryPanel(
                                data: cockpit,
                                onReport: (_) {},
                                onCapacity: () {},
                                onTarget: () {},
                                dateFilters: ScopeDateTiles(
                                  compact: true,
                                  from: '2026-09-23',
                                  to: '2026-10-23',
                                  onChanged: (_, _) {},
                                  onClear: () {},
                                ),
                              ),
                              StatefulBuilder(
                                builder: (context, setState) => ScopeBranchTabs(
                                  items: const [
                                    {'id': 'b1', 'arabicName': 'فرع الشروق'},
                                  ],
                                  selected: selectedBranch,
                                  onSelected: (v) =>
                                      setState(() => selectedBranch = v),
                                ),
                              ),
                              HomeCollectionCards(
                                data: collections,
                                currency: 'EGP',
                                referenceDay: '2026-10-23',
                                onOpen: (_, _) {},
                                onSource: (_, _) {},
                                onBranch: (_, _) {},
                              ),
                              HomeOperatingStatement(
                                data: statement,
                                onOpen: () {},
                              ),
                              HomeDecisionCards(
                                data: cockpit,
                                onReport: (_) {},
                                onCapacity: () {},
                                onFollowUps: () => followups++,
                                onReminders: (v) => reminder = v,
                              ),
                              OwnerHonorsSection(
                                auth: auth,
                                coaches: coachRows,
                                branches: const [
                                  {'id': 'b1', 'arabicName': 'فرع الشروق'},
                                ],
                                filters: const {'branchId': 'b1'},
                                branch: 'b1',
                                asOf: '2026-10-23',
                              ),
                            ],
                          ),
                        ),
                      ),
                    ),
                  ),
                ),
                scale: scale,
              ),
            );
            await t.pumpAndSettle();
            final banner = find.byType(OwnerWelcomeBanner);
            expect(banner, findsOneWidget);
            final bannerBox = t.getRect(banner);
            expect(bannerBox.left, greaterThanOrEqualTo(0));
            expect(bannerBox.right, lessThanOrEqualTo(width));
            expect(t.takeException(), isNull);
            final compact = t.widget<ScopeDateTiles>(
              find.byType(ScopeDateTiles),
            );
            expect(compact.compact, isTrue);
            final halves = find.byKey(const ValueKey('summary-halves'));
            final logo = find.byKey(const ValueKey('summary-watermark'));
            final financial = find
                .descendant(of: halves, matching: find.byType(MoneyText))
                .first;
            expect(logo, findsNothing);
            expect(t.getSize(financial).width, greaterThan(0));
            final branchChip = find.descendant(
              of: find.byType(ScopeBranchTabs),
              matching: find.widgetWithText(ChoiceChip, 'فرع الشروق'),
            );
            await t.ensureVisible(branchChip);
            await t.tap(branchChip);
            await t.pumpAndSettle();
            expect(selectedBranch, 'b1');
            expect(t.widget<ChoiceChip>(branchChip).selected, isTrue);
            final daily = find.descendant(
              of: find.byType(HomeCollectionCards),
              matching: find.text('اليوم'),
            );
            await t.ensureVisible(daily);
            await t.tap(daily);
            await t.pumpAndSettle();
            final source = find.descendant(
              of: find.byType(HomeCollectionCards),
              matching: find.text('مصادر التحصيل'),
            );
            final branch = find.descendant(
              of: find.byType(HomeCollectionCards),
              matching: find.text('حسب الفرع'),
            );
            expect(
              t.getTopLeft(source).dy,
              closeTo(t.getTopLeft(branch).dy, 1),
            );
            expect(
              find.text('لا توجد تحصيلات مسجلة لهذه الفترة.'),
              findsNothing,
            );
            expect(
              find.descendant(
                of: find.byType(HomeCollectionCards),
                matching: find.text('0%'),
              ),
              findsNWidgets(2),
            );
            expect((collections['monthSources'] as List).first['amount'], 7000);
            expect(find.byType(OperatingMonthlyChart), findsOneWidget);
            final income = find.text('الإيرادات'),
                expenses = find.text('المصروفات');
            await t.ensureVisible(income);
            expect(
              t.getTopLeft(income).dy,
              closeTo(t.getTopLeft(expenses).dy, 1),
            );
            final action = find.text('استكمال المتابعات');
            await t.ensureVisible(action);
            await t.tap(action);
            expect(followups, 1);
            final pages = find.descendant(
              of: find.byType(HomeDecisionCards),
              matching: find.byKey(const ValueKey('owner-decisions-rail')),
            );
            await t.ensureVisible(pages);
            await t.pumpAndSettle();
            await t.drag(pages, const Offset(280, 0));
            await t.pumpAndSettle();
            await t.ensureVisible(find.text('تحضير تذكيرات التحصيل'));
            await t.pumpAndSettle();
            await t.tap(find.text('تحضير تذكيرات التحصيل'));
            expect(reminder, 'Overdue');
            final honors = find.byType(OwnerHonorsSection);
            final coachChip = find.descendant(
              of: honors,
              matching: find.widgetWithText(ChoiceChip, 'المدربون'),
            );
            await t.ensureVisible(coachChip);
            await t.tap(coachChip);
            await t.pumpAndSettle();
            final cards = t.widgetList<HonorPortraitCard>(
              find.descendant(
                of: honors,
                matching: find.byType(HonorPortraitCard),
              ),
            );
            final coach = cards.singleWhere(
              (c) => c.name == 'مدرب أعلى تقييمًا',
            );
            expect(cards.map((c) => c.photoHeight).toSet().length, 1);
            expect(coach.category, 'المدربون');
            expect(coach.fallbackAsset, 'assets/avatars/adult-neutral.svg');
            expect(coach.details.any((d) => d.$1 == 'فرع الشروق'), isTrue);
            expect(
              coach.details.map((d) => d.$2).toSet().length,
              greaterThanOrEqualTo(4),
            );
            expect(
              paths.where((p) => p.contains('vip-members')).single,
              contains('branchId=b1'),
            );
            expect(t.takeException(), isNull);
            await t.pumpWidget(const SizedBox());
            auth.dispose();
          },
        );
      }
    }
  }
  testWidgets('owner header stays available and footer remains usable', (
    t,
  ) async {
    final auth = await legacy.homeOwner((m, p, b, token) async {
      if (p.contains('structure/options')) return {'branches': <Object>[]};
      return legacy.summary();
    });
    await t.pumpWidget(
      f.app(
        NativeRoleTabs(
          auth: auth,
          role: 'AcademyOwner',
          home: OwnerDashboard(auth: auth, accountPage: const SizedBox()),
          account: const SizedBox(),
        ),
      ),
    );
    await t.pumpAndSettle();
    expect(find.byType(ExperienceHeader), findsOneWidget);
    expect(
      t.getSize(find.byType(OwnerBrandNavigation)).height,
      lessThanOrEqualTo(110),
    );
    final list = find
        .descendant(
          of: find.byType(OwnerDashboard),
          matching: find.byType(Scrollable),
        )
        .first;
    await t.drag(list, const Offset(0, -600));
    await t.pumpAndSettle();
    expect(find.byTooltip('حسابي').hitTestable(), findsOneWidget);
    expect(find.text('الإجراءات السريعة'), findsNothing);
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
  testWidgets('honors remain stationary until manually selected or swiped', (
    t,
  ) async {
    final auth = await f.authenticate(
      'AcademyOwner',
      (m, p, b, token) async => {'items': <Object>[]},
    );
    await t.pumpWidget(
      f.app(
        Builder(
          builder: (context) => MediaQuery(
            data: MediaQuery.of(context).copyWith(disableAnimations: false),
            child: Scaffold(
              body: OwnerHonorsSection(
                auth: auth,
                coaches: coachRows,
                branches: const [],
                filters: const {},
                branch: '',
                asOf: '2026-10-23',
              ),
            ),
          ),
        ),
      ),
    );
    await t.pump();
    await t.pump();
    final controller = t
        .widget<ListView>(find.byKey(const ValueKey('owner-honors-rail')))
        .controller!;
    final start = controller.offset;
    for (var i = 0; i < 60; i++) {
      await t.pump(const Duration(milliseconds: 50));
    }
    expect(controller.offset, start);
    final gesture = await t.startGesture(
      t.getCenter(find.byKey(const ValueKey('owner-honors-rail'))),
    );
    final stopped = controller.offset;
    for (var i = 0; i < 20; i++) {
      await t.pump(const Duration(milliseconds: 50));
    }
    expect(controller.offset, stopped);
    await gesture.cancel();
    for (var i = 0; i < 100; i++) {
      await t.pump(const Duration(milliseconds: 50));
    }
    expect(controller.offset, stopped);
    await t.tap(find.text('المدربون').first);
    await t.pumpAndSettle();
    expect(controller.offset, greaterThan(start));
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
}
