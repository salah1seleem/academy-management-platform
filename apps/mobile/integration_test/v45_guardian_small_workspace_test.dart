import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/guardian/family_child_filter.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:academy_mobile/features/shared/role_agenda.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'actual guardian at375 swipes siblings, retains selected child through long scrolling, reads protected home media, uses all-child agenda and changes a child sport',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      Future<void> waitFor(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> reach(Finder f) async {
        final scroll = find.byType(Scrollable).first;
        t.state<ScrollableState>(scroll).position.jumpTo(0);
        await t.pumpAndSettle();
        await t.scrollUntilVisible(f, 180, scrollable: scroll, maxScrolls: 120);
        await t.ensureVisible(f.last);
        await t.pumpAndSettle();
      }

      Future<AuthController> login(String phone) async {
        final a = AuthController(
          HttpApiTransport(AppConfig('Demo', base)),
          SecureRefreshStore('v45.5278.guardian-workspace.$phone'),
        );
        await a.restore();
        if (a.session == null) {
          final c = await a.requestOtp(phone);
          await a.verify(phone, c, '246810');
        }
        expect(a.session!.selected!.role, 'Guardian');
        expect(
          a.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return a;
      }

      Future<void> show(AuthController a, String key) async {
        final appearance = Appearance();
        for (final theme in ['football', 'general']) {
          await appearance.completeRoleIntro('Guardian', theme);
        }
        await t.pumpWidget(
          AcademyApp(
            key: ValueKey(key),
            auth: a,
            environment: 'Demo',
            appearance: appearance,
          ),
        );
        await waitFor(find.byType(NativeRoleTabs));
        await waitFor(find.text('أبنائي'));
      }

      final a = await login('01200000001');
      final home = obj(await a.request('GET', '/api/v1/guardian/home'));
      final children = rows(home['children']);
      expect(children.length, greaterThanOrEqualTo(2));
      await show(a, 'siblings');
      await reach(find.byType(GuardianChildCarousel));
      var pager = find.descendant(
        of: find.byType(GuardianChildCarousel),
        matching: find.byType(PageView),
      );
      expect(t.widget<PageView>(pager).controller!.viewportFraction, .94);
      await t.drag(pager, const Offset(320, 0));
      await t.pumpAndSettle();
      expect(t.widget<PageView>(pager).controller!.page!.round(), 1);
      final chosen = children[1];
      final workspace = find.byKey(ValueKey('child-${chosen['id']}'));
      await reach(workspace);
      expect(workspace, findsOneWidget);
      await binding.takeScreenshot(
        'phase-approved-guardian-swiped-workspace-w375',
      );
      await reach(find.text('إيصالاتي'));
      expect(find.text('أرصدة الأسرة'), findsNothing);
      t
          .state<ScrollableState>(find.byType(Scrollable).first)
          .position
          .jumpTo(0);
      await t.pumpAndSettle();
      await waitFor(find.byType(GuardianChildCarousel));
      pager = find.descendant(
        of: find.byType(GuardianChildCarousel),
        matching: find.byType(PageView),
      );
      expect(
        t.widget<PageView>(pager).controller!.page!.round(),
        1,
        reason: 'Returning from the media section must keep the card aligned with the selected-child workspace.',
      );
      await binding.takeScreenshot(
        'phase-approved-guardian-selected-card-restored-w375',
      );
      final nav = find.byType(NavigationBar);
      expect(t.widget<NavigationBar>(nav).destinations.length, 4);
      await t.tap(find.descendant(of: nav, matching: find.text('الحصص')));
      await t.pumpAndSettle();
      await waitFor(find.byType(RoleAgenda));
      final choose = find.byKey(const ValueKey('family-filter-'));
      await waitFor(choose);
      await t.ensureVisible(choose);
      await t.tap(choose);
      await t.pumpAndSettle();
      expect(
        t.widget<FamilyChildFilter>(find.byType(FamilyChildFilter)).selected,
        '',
      );
      await binding.takeScreenshot(
        'phase-approved-guardian-all-children-agenda-w375',
      );
      for (final tab in ['المباريات', 'التقييمات', 'الرئيسية']) {
        await t.tap(
          find.descendant(
            of: find.byType(NavigationBar),
            matching: find.text(tab),
          ),
        );
        await t.pumpAndSettle();
        expect(t.takeException(), isNull);
      }
      await t.pumpWidget(const SizedBox());
      final multi = a;
      final family = obj(await multi.request('GET', '/api/v1/guardian/home'));
      final multiChildren = rows(family['children']);
      final target = multiChildren.firstWhere(
        (c) => rows(c['sports']).length > 1,
      );
      await show(multi, 'multi-sport');
      await reach(find.byType(GuardianChildCarousel));
      final targetIndex = multiChildren.indexWhere(
        (c) => c['id'] == target['id'],
      );
      pager = find.descendant(
        of: find.byType(GuardianChildCarousel),
        matching: find.byType(PageView),
      );
      for (var i = 0; i < targetIndex; i++) {
        await t.drag(pager, const Offset(320, 0));
        await t.pumpAndSettle();
      }
      final card = find.byWidgetPredicate(
        (w) => w is PremiumChildCard && w.childData['id'] == target['id'],
      );
      final sports = rows(target['sports']);
      final second = sports[1];
      final sportPicker = find.descendant(
        of: card,
        matching: find.byType(DropdownButtonFormField<String>),
      );
      await t.ensureVisible(sportPicker);
      await t.tap(sportPicker);
      await t.pumpAndSettle();
      await t.tap(
        find.text('${second['sport']} · ${second['branch'] ?? ''}').last,
      );
      await t.pumpAndSettle();
      expect(
        t.widget<DropdownButtonFormField<String>>(sportPicker).initialValue,
        second['id'],
      );
      await binding.takeScreenshot(
        'phase-approved-guardian-sport-context-w375',
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      multi.dispose();
      final mediaAuth = await login('01099900100');
      await show(mediaAuth, 'existing-media');
      await reach(find.byType(GuardianHomeMedia));
      await waitFor(find.text('صور وفيديوهات التمرين'));
      await reach(find.text('صورة قبول اصطناعية من ملفات iOS V45'));
      final mediaChildren = rows(
        obj(
          await mediaAuth.request('GET', '/api/v1/guardian/home'),
        )['children'],
      );
      expect(
        t.widget<GuardianHomeMedia>(find.byType(GuardianHomeMedia)).playerId,
        mediaChildren.first['id'],
      );
      await binding.takeScreenshot(
        'phase-approved-guardian-protected-home-media-w375',
      );
      await t.pumpWidget(const SizedBox());
      mediaAuth.dispose();
    },
  );
}
