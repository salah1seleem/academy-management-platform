import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:academy_mobile/features/owner/owner_honors_section.dart';
import 'package:academy_mobile/features/owner/honor_portrait_card.dart';
import 'package:academy_mobile/features/owner/home_destinations.dart';
import 'package:academy_mobile/features/owner/refined_home_panels.dart';
import 'package:academy_mobile/features/shared/experience_ui.dart';
import 'package:academy_mobile/features/shared/owner_brand_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'v73_owner_brand_rail_test.dart' show BrandRailReadTransport;

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'owner compact filters, moving header, financial halves and honors list drilldowns',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      t.platformDispatcher.accessibilityFeaturesTestValue =
          const FakeAccessibilityFeatures(disableAnimations: true);
      final auth = AuthController(
        BrandRailReadTransport(AppConfig('Demo', base)),
        SecureRefreshStore('v45.5278.os-media-entry.01099900010'),
      );
      await auth.restore();
      if (auth.session == null) {
        final challenge = await auth.requestOtp('01099900010');
        await auth.verify('01099900010', challenge, '246810');
      }
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final appearance = Appearance();
      await appearance.update(night: false);
      for (final sport in ['football', 'general']) {
        await appearance.completeRoleIntro('AcademyOwner', sport);
      }
      await t.pumpWidget(
        AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
      );
      Future<void> wait(Finder finder) async {
        for (var i = 0; i < 240 && finder.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(finder, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> reach(Finder finder) async {
        final scroll = find
            .descendant(
              of: find.byType(OwnerDashboard),
              matching: find.byType(Scrollable),
            )
            .first;
        await t.scrollUntilVisible(
          finder,
          220,
          scrollable: scroll,
          maxScrolls: 100,
        );
        await t.ensureVisible(finder);
        await t.pumpAndSettle();
      }

      await wait(find.byType(ScopeDateTiles));
      expect(
        t.widget<ScopeDateTiles>(find.byType(ScopeDateTiles)).compact,
        true,
      );
      expect(
        t.getSize(find.byType(OwnerBrandNavigation)).height,
        lessThan(120),
      );
      expect(find.byType(ExperienceHeader), findsOneWidget);
      await binding.takeScreenshot('phase-approved-owner-home-compact-top');
      final from = find.bySemanticsLabel(RegExp(r'^من 2026'));
      await t.tap(from.first);
      await t.pumpAndSettle();
      expect(find.byType(DatePickerDialog), findsOneWidget);
      final cancelLabel = MaterialLocalizations.of(
        t.element(find.byType(DatePickerDialog)),
      ).cancelButtonLabel;
      await t.tap(find.text(cancelLabel));
      await t.pumpAndSettle();
      final branch = find.descendant(
        of: find.byType(ScopeBranchTabs),
        matching: find.text('فرع الشروق'),
      );
      await t.tap(branch);
      await t.pumpAndSettle();
      await wait(find.byType(AcademySummaryPanel));
      await reach(find.byKey(const ValueKey('collection-breakdown-halves')));
      expect(find.byTooltip('حسابي').hitTestable(), findsNothing);
      await binding.takeScreenshot(
        'phase-approved-owner-home-collection-halves',
      );
      final daily = find.descendant(
        of: find.byType(HomeCollectionCards),
        matching: find.text('اليوم'),
      );
      await reach(daily);
      await t.tap(daily);
      await t.pumpAndSettle();
      expect(find.text('لا توجد تحصيلات مسجلة لهذه الفترة.'), findsNothing);
      await reach(find.byKey(const ValueKey('operating-halves')));
      await binding.takeScreenshot(
        'phase-approved-owner-home-operating-halves',
      );
      final decisions = find.descendant(
        of: find.byType(HomeDecisionCards),
        matching: find.byKey(const ValueKey('owner-decisions-rail')),
      );
      await reach(decisions);
      await t.drag(decisions, const Offset(240, 0));
      await t.pumpAndSettle();
      await binding.takeScreenshot('phase-approved-owner-home-decision-deck');
      await reach(find.text('نجوم الأكاديمية'));
      await wait(find.byType(OwnerHonorsSection));
      final cards = find.descendant(
        of: find.byType(OwnerHonorsSection),
        matching: find.byType(HonorPortraitCard),
      );
      await wait(cards);
      await binding.takeScreenshot('phase-approved-owner-home-honors');
      final memberChip = find.descendant(
        of: find.byType(OwnerHonorsSection),
        matching: find.widgetWithText(ChoiceChip, 'أعضاء مميزون'),
      );
      await t.tap(memberChip);
      await t.pumpAndSettle();
      final list = find.byKey(const ValueKey('owner-honors-rail'));
      final visible = t.widget<ListView>(list).controller!;
      final memberCard = find
          .descendant(
            of: find.byType(OwnerHonorsSection),
            matching: find.byType(HonorPortraitCard),
          )
          .hitTestable();
      expect(visible.offset, greaterThan(0));
      await t.tap(memberCard.first);
      await t.pumpAndSettle();
      await wait(find.byType(OwnerVipRanking));
      await t.tap(find.byType(BackButton));
      await t.pumpAndSettle();
      final coachChip = find.descendant(
        of: find.byType(OwnerHonorsSection),
        matching: find.widgetWithText(ChoiceChip, 'المدربون'),
      );
      await t.tap(coachChip);
      await t.pumpAndSettle();
      await t.tap(
        find
            .descendant(
              of: find.byType(OwnerHonorsSection),
              matching: find.byType(HonorPortraitCard),
            )
            .hitTestable()
            .first,
      );
      await t.pumpAndSettle();
      await wait(find.byType(HomeCoaches));
      await t.tap(find.byType(BackButton));
      await t.pumpAndSettle();
      await appearance.update(night: true);
      await t.pumpAndSettle();
      await t.pump(const Duration(milliseconds: 350));
      await binding.takeScreenshot('phase-approved-owner-home-honors-dark');
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      await t.pumpAndSettle();
      auth.dispose();
      t.platformDispatcher.clearAccessibilityFeaturesTestValue();
    },
  );
}
