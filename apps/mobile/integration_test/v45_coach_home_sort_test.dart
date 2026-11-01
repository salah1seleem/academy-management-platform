import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:academy_mobile/features/owner/home_destinations.dart';
import 'package:academy_mobile/features/owner/owner_operations.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'actual owner coach ranking switches and keeps sorting in full list and correct coach destination',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final auth = AuthController(
        HttpApiTransport(AppConfig('Demo', base)),
        SecureRefreshStore('v45.5278.os-media-entry.01099900010'),
      );
      await auth.restore();
      if (auth.session == null) {
        final challenge = await auth.requestOtp('01099900010');
        await auth.verify('01099900010', challenge, '246810');
      }
      expect(auth.session!.selected!.role, 'AcademyOwner');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: const bool.fromEnvironment('LIGHT_THEME')
              ? AcademyTheme.light()
              : AcademyTheme.dark(),
          home: OwnerDashboard(auth: auth, accountPage: const SizedBox()),
        ),
      );
      await wait(find.text('ملخص الأكاديمية'));
      final scroll = find.byType(Scrollable).first;
      await t.scrollUntilVisible(
        find.text('حسب التقييم'),
        350,
        scrollable: scroll,
        maxScrolls: 120,
      );
      await t.pumpAndSettle();
      final section = t.widget<HomeCoaches>(find.byType(HomeCoaches));
      expect(section.items.length, greaterThan(1));
      final rated = section.items
          .where((c) => c['parentRating'] != null)
          .toList();
      expect(rated, isNotEmpty);
      final bestRating = rated
          .map((c) => c['parentRating'] as num)
          .reduce((a, b) => a > b ? a : b);
      Finder firstCard() => find
          .descendant(
            of: find.byType(HomeCoaches).last,
            matching: find.byType(Card),
          )
          .first;
      final ratingLeaders = rated
          .where((c) => c['parentRating'] == bestRating)
          .map((c) => c['name'])
          .toList();
      expect(
        ratingLeaders.any(
          (name) => find
              .descendant(of: firstCard(), matching: find.text('$name'))
              .evaluate()
              .isNotEmpty,
        ),
        true,
      );
      await t.ensureVisible(find.text('حسب المجموعات المسندة'));
      await t.tap(find.text('حسب المجموعات المسندة'));
      await t.pumpAndSettle();
      final maxGroups = section.items
          .map((c) => c['groupCount'] as num)
          .reduce((a, b) => a > b ? a : b);
      final leaders = section.items
          .where((c) => c['groupCount'] == maxGroups)
          .toList();
      final first = leaders.singleWhere(
        (c) => find
            .descendant(of: firstCard(), matching: find.text('${c['name']}'))
            .evaluate()
            .isNotEmpty,
      );
      await binding.takeScreenshot('phase-approved-home-coaches-by-groups');
      await Scrollable.ensureVisible(
        t.element(find.text('كل المدربين')),
        alignment: .5,
      );
      await t.pumpAndSettle();
      await t.tap(find.text('كل المدربين'));
      await t.pump(const Duration(milliseconds: 350));
      await wait(find.byWidgetPredicate((w) => w is HomeCoaches && w.all));
      expect(
        t.widget<GridView>(find.byType(GridView)).gridDelegate,
        isA<SliverGridDelegateWithFixedCrossAxisCount>(),
      );
      expect(
        (t.widget<GridView>(find.byType(GridView)).gridDelegate
                as SliverGridDelegateWithFixedCrossAxisCount)
            .crossAxisCount,
        2,
      );
      expect(
        t.widget<HomeCoaches>(find.byType(HomeCoaches).last).byGroups,
        true,
      );
      expect(
        t
            .widget<ChoiceChip>(
              find.widgetWithText(ChoiceChip, 'حسب المجموعات المسندة').last,
            )
            .selected,
        true,
      );
      expect(
        find.descendant(
          of: firstCard(),
          matching: find.text('${first['name']}'),
        ),
        findsOneWidget,
      );
      await binding.takeScreenshot(
        'phase-approved-all-coaches-groups-inherited',
      );
      await t.tap(firstCard());
      await t.pump(const Duration(milliseconds: 350));
      await wait(find.byType(OwnerCoachOperations));
      expect(
        t.widget<OwnerCoachOperations>(find.byType(OwnerCoachOperations)).id,
        first['id'],
      );
      await wait(find.text('${first['name']}'));
      await binding.takeScreenshot('phase-approved-coach-correct-destination');
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
