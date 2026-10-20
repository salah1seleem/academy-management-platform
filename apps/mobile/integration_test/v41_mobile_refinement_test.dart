import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/academy_brand.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/coach/coach_home.dart';
import 'package:academy_mobile/features/coach/monthly_stars.dart';
import 'package:academy_mobile/features/admin/admin_home.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:academy_mobile/features/owner/featured_players.dart';
import 'package:academy_mobile/features/owner/owner_reports.dart';
import 'package:academy_mobile/features/shared/content_editor.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:academy_mobile/features/shared/matches.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'V41 actual role screens, coach star grants, linked inbox and owner ranking',
    (tester) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      final api = const String.fromEnvironment('API_BASE_URL');
      expect(api, 'http://127.0.0.1:5260');
      final suffix = const String.fromEnvironment(
        'CAPTURE_SIZE',
        defaultValue: 'small',
      );
      final owned = <AuthController>[];
      final brands = <AcademyBrand>[];
      var mountedScreen = 0;
      Future<AuthController> login(String phone) async {
        final a = AuthController(
          HttpApiTransport(AppConfig('Demo', api)),
          SecureRefreshStore('v41.$phone'),
        );
        await a.restore();
        final challenge = await a.requestOtp(phone);
        await a.verify(phone, challenge, '246810');
        expect(
          a.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        owned.add(a);
        return a;
      }

      Future<void> waitFor(Finder f) async {
        for (var i = 0; i < 160 && f.evaluate().isEmpty; i++) {
          await tester.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await tester.pump(const Duration(milliseconds: 300));
      }

      Future<void> mount(
        AuthController a,
        Widget home, {
        bool tabs = false,
      }) async {
        final brand = AcademyBrand(a);
        brands.add(brand);
        await brand.refresh(force: true);
        await tester.pumpWidget(
          MaterialApp(
            // Each direct-screen harness mount starts a fresh navigator. A
            // pushed report must not remain over the next independent screen.
            key: ValueKey('${a.session!.selected!.id}:${mountedScreen++}'),
            debugShowCheckedModeBanner: false,
            locale: const Locale('ar'),
            supportedLocales: const [Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            theme: guardianTheme(
              primary: brand.primary,
              secondary: brand.secondary,
            ),
            home: BrandScope(
              brand: brand,
              child: tabs
                  ? NativeRoleTabs(
                      auth: a,
                      role: a.session!.selected!.role,
                      environment: 'Demo',
                      account: RoleFoundation(auth: a),
                      home: home,
                    )
                  : home,
            ),
          ),
        );
        await tester.pump(const Duration(seconds: 1));
      }

      Future<void> shot(String title) async {
        await tester.pump(const Duration(milliseconds: 300));
        expect(tester.takeException(), isNull);
        await binding.takeScreenshot('phase-refinement-$title-$suffix');
      }

      final owner = await login('01099900010');
      final calendar = obj(
        await owner.request('GET', '/api/v1/academy/calendar'),
      );
      final summary = obj(
        await owner.request('GET', '/api/v1/reports/owner-summary'),
      );
      expect(summary['from'], calendar['trailingMonthFrom']);
      expect(summary['to'], calendar['today']);
      await mount(
        owner,
        OwnerDashboard(
          auth: owner,
          accountPage: RoleFoundation(auth: owner),
        ),
        tabs: true,
      );
      await waitFor(find.text('ملخص الأكاديمية'));
      expect(find.text('مرجع التحليل'), findsNothing);
      expect(find.text('المباريات'), findsWidgets);
      expect(find.text('التقارير'), findsWidgets);
      await shot('owner-top');
      if (const bool.fromEnvironment('EXTENDED_ACCEPTANCE')) {
        await tester.tap(find.byTooltip('ضبط هدف التحصيل'));
        await waitFor(find.text('حفظ الهدف وعرض الشهر الحالي'));
        await tester.enterText(find.byType(TextField).first, '50000');
        await tester.tap(find.text('حفظ الهدف وعرض الشهر الحالي'));
        await waitFor(find.text('هدف تحصيل الشهر'));
        await shot('owner-configured-target');
      }
      final homeScroll = find.byType(Scrollable).first;
      for (var i = 0; i < 5; i++) {
        await tester.drag(homeScroll, const Offset(0, -590));
        await tester.pump(const Duration(milliseconds: 300));
        await shot('owner-scroll-$i');
      }
      expect(find.text('أبرز اللاعبين'), findsWidgets);

      final coach = await login('01099900020');
      await mount(
        coach,
        CoachHome(
          auth: coach,
          accountPage: RoleFoundation(auth: coach),
        ),
        tabs: true,
      );
      await waitFor(find.text('المباريات'));
      await shot('coach-home');
      await mount(coach, CoachPlayers(auth: coach));
      await waitFor(find.text('حصتان متبقيتان'));
      expect(find.text('الاشتراك انتهى'), findsWidgets);
      expect(find.textContaining('ربو خفيف'), findsWidgets);
      await shot('coach-roster');

      final options = obj(
        await coach.request('GET', '/api/v1/coach/monthly-stars'),
      );
      final teams = rows(options['teams']);
      final team85 = teams.singleWhere((t) => t['name'] == 'منتخب 2018');
      final team87 = teams.singleWhere(
        (t) => textOf(t['name']).contains('عرض تجريبي'),
      );
      final players = rows(team85['players']);
      final featured = obj(
        await owner.request('GET', '/api/v1/reports/featured-players'),
      );
      final player85 = rows(featured['items'])
          .singleWhere((p) => p['score'] == 85);
      final player87 = rows(featured['items'])
          .singleWhere((p) => p['score'] == 87);
      expect(players.any((p) => p['id'] == player85['id']), isTrue);
      // Repeat-safe capture run on a second viewport uses the already granted
      // legitimate stars. Fresh grants are always made through the actual UI.
      Future<void> grantTo(Json team, Json player, int target) async {
        final current =
            rows(team['players'])
                    .singleWhere((p) => p['id'] == player['id'])['stars']
                as int;
        for (var i = current; i < target; i++) {
          await mount(coach, CoachMonthlyStars(auth: coach));
          final key = find.byKey(
            ValueKey('star-${team['id']}-${player['id']}'),
          );
          await waitFor(find.textContaining('نجوم لكل فريق'));
          // gList is a real lazy scrolling list: the next team's controls are
          // not built until that team enters the viewport.
          await tester.scrollUntilVisible(key, 300, maxScrolls: 40);
          expect(key, findsOneWidget);
          await tester.ensureVisible(key);
          await tester.tap(key);
          await tester.pumpAndSettle();
          await tester.tap(find.text('تأكيد'));
          await tester.pump(const Duration(seconds: 1));
          for (var n = 0; n < 80; n++) {
            final latest = obj(
              await coach.request('GET', '/api/v1/coach/monthly-stars'),
            );
            final changed = rows(latest['teams'])
                .singleWhere((t) => t['id'] == team['id']);
            if (rows(changed['players'])
                    .singleWhere((p) => p['id'] == player['id'])['stars'] ==
                i + 1) {
              break;
            }
            await tester.pump(const Duration(milliseconds: 200));
          }
        }
      }

      await grantTo(team85, player85, 3);
      await grantTo(team87, player87, 2);
      final after = obj(
        await owner.request('GET', '/api/v1/reports/featured-players'),
      );
      final ordered = rows(after['items']);
      expect(
        ordered.indexWhere((p) => p['id'] == player85['id']),
        lessThan(ordered.indexWhere((p) => p['id'] == player87['id'])),
      );
      expect(
        ordered.singleWhere((p) => p['id'] == player85['id'])['score'],
        85,
      );
      await mount(owner, FeaturedPlayers(auth: owner, all: true));
      await waitFor(find.text('85'));
      expect(find.text('3 ★'), findsWidgets);
      await shot('owner-featured');

      final guardian = await login('01099900101');
      final family = obj(
        await guardian.request('GET', '/api/v1/guardian/home'),
      );
      expect(rows(family['children']).length, 2);
      await mount(
        guardian,
        GuardianHome(
          auth: guardian,
          environment: 'Demo',
          accountPage: RoleFoundation(auth: guardian),
        ),
        tabs: true,
      );
      await waitFor(find.text('الجدول والحصص'));
      await shot('guardian-home');
      final children = rows(family['children']);
      for (final child in children) {
        final selector = find.byKey(ValueKey('select-child-${child['id']}'));
        await tester.ensureVisible(selector);
        await tester.tap(selector);
        await tester.pump(const Duration(milliseconds: 500));
        expect(
          find.byKey(ValueKey('child-workspace-${child['id']}')),
          findsOneWidget,
        );
      }
      await shot('guardian-selected-child');
      await mount(guardian, NotificationInbox(auth: guardian));
      await waitFor(find.textContaining('غير مقروءة'));
      expect(find.textContaining('نجمة'), findsWidgets);
      await shot('guardian-notifications');

      final admin = await login('01099900011');
      await mount(
        admin,
        AdminHome(
          auth: admin,
          accountPage: RoleFoundation(auth: admin),
        ),
        tabs: true,
      );
      await tester.pump(const Duration(seconds: 2));
      await shot('admin-home');
      await mount(admin, CoachPlayers(auth: admin, admin: true));
      await waitFor(find.text('حصتان متبقيتان'));
      expect(find.text('الاشتراك انتهى'), findsWidgets);
      expect(find.textContaining('ربو خفيف'), findsWidgets);
      await shot('admin-roster');

      await mount(owner, MatchSchedule(auth: owner));
      await waitFor(find.text('كل الفرق'));
      await shot('owner-matches');
      await mount(coach, MatchSchedule(auth: coach));
      await waitFor(find.text('كل الفرق'));
      expect(find.text('كل الفروع'), findsNothing);
      await shot('coach-matches');
      if (const bool.fromEnvironment('EXTENDED_ACCEPTANCE')) {
        await mount(owner, MatchSchedule(auth: owner));
        await waitFor(find.text('كل الفرق'));
        final tab = find.text('منتخب 2018').first;
        await tester.ensureVisible(tab);
        await tester.tap(tab);
        await tester.pump(const Duration(milliseconds: 500));
        await waitFor(find.text('مباريات مكتملة'));
        await tester.scrollUntilVisible(
          find.text('4 — 2'),
          250,
          maxScrolls: 25,
          scrollable: find
              .byWidgetPredicate(
                (w) => w is Scrollable && w.axisDirection == AxisDirection.down,
              )
              .first,
        );
        expect(find.text('4 — 2'), findsWidgets);
        await shot('updated-result');
        await mount(
          owner,
          OwnerReports(auth: owner, initialReport: 'financial-hub'),
        );
        await waitFor(find.text('المصروفات'));
        expect(find.text('الإيرادات'), findsWidgets);
        await shot('reports-menu');
        await tester.tap(find.text('التحصيلات'));
        await waitFor(find.textContaining('صافي التحصيل:'));
        await shot('collections-report');
        final csv = await owner.asset(
          '/api/v1/reports/financial/export?from=${calendar['trailingMonthFrom']}&to=${calendar['today']}',
        );
        expect(csv.length, greaterThan(100));
        await mount(owner, AcademyNewsManagement(auth: owner));
        await waitFor(find.text('إضافة خبر'));
        await tester.tap(find.text('إضافة خبر'));
        await tester.pumpAndSettle();
        final headline =
            'لقاء أولياء الأمور ${DateTime.now().microsecondsSinceEpoch}';
        final title = find.byWidgetPredicate(
              (w) => w is TextField && w.decoration?.labelText == 'العنوان',
            ),
            body = find.byWidgetPredicate(
              (w) => w is TextField && w.decoration?.labelText == 'التفاصيل',
            );
        await tester.enterText(title, headline);
        await tester.enterText(
          body,
          'لقاء تعريفي للأسر داخل بيئة القبول الاصطناعية فقط.',
        );
        await tester.ensureVisible(find.text('حفظ كمسودة'));
        await tester.tap(find.text('حفظ كمسودة'));
        await waitFor(find.text('إضافة خبر').hitTestable());
        await tester.pumpAndSettle();
        expect(find.widgetWithText(ListTile, headline), findsOneWidget);
        final saved = rows(await owner.request('GET', '/api/v1/content/news'))
            .singleWhere((n) => n['title'] == headline);
        expect(saved['publishedAtUtc'], isNull);
        await tester.tap(find.widgetWithText(ListTile, headline));
        await tester.pumpAndSettle();
        await tester.ensureVisible(find.text('نشر الخبر'));
        await tester.tap(find.text('نشر الخبر'));
        await waitFor(find.text('إضافة خبر').hitTestable());
        await tester.pumpAndSettle();
        expect(find.widgetWithText(ListTile, headline), findsOneWidget);
        expect(
          rows(await guardian.request('GET', '/api/v1/news'))
              .any((n) => n['title'] == headline),
          isTrue,
        );
        await shot('published-news');
      }
      await mount(
        owner,
        OwnerDashboard(
          auth: owner,
          accountPage: RoleFoundation(auth: owner),
        ),
        tabs: true,
      );
      await waitFor(find.text('ملخص الأكاديمية'));
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox.shrink());
      for (final b in brands) {
        b.dispose();
      }
      for (final a in owned) {
        a.dispose();
      }
    },
  );
}
