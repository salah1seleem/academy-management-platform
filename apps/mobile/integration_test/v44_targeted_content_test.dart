import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/native_academy_content.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'isolated native content review publishes to one branch and role, then withdraws without financial changes',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      Future<AuthController> login(String phone) async {
        final auth = AuthController(
          HttpApiTransport(AppConfig('Demo', base)),
          SecureRefreshStore('v44.5278.content.$phone'),
        );
        await auth.restore();
        if (auth.session == null) {
          final challenge = await auth.requestOtp(phone);
          await auth.verify(phone, challenge, '246810');
        }
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return auth;
      }

      final owner = await login('01099900010'),
          coach = await login('01099900020'),
          guardian = await login('01099900100');
      expect(owner.session!.selected!.role, 'AcademyOwner');
      expect(coach.session!.selected!.role, 'Coach');
      expect(guardian.session!.selected!.role, 'Guardian');
      final moneyBefore = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&page=1&pageSize=1',
        ),
      );
      expect(moneyBefore['netAmount'], isA<num>());
      expect(moneyBefore['totalCount'], isA<num>());
      final group = rows(await coach.request('GET', '/api/v1/coach/groups'))
          .first;
      final structure = obj(
        await owner.request('GET', '/api/v1/manage/structure/options'),
      );
      final branchId = rows(structure['groups'])
          .firstWhere((g) => g['id'] == group['id'])['branchId'];
      final options = obj(
        await owner.request('GET', '/api/v1/content/news/options'),
      );
      final branchName = textOf(
        rows(options['branches'])
            .firstWhere((b) => b['id'] == branchId)['name'],
      );
      final title = 'خبر قبول اصطناعي ${DateTime.now().millisecondsSinceEpoch}';
      Future<void> waitFor(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> tap(String label) async {
        final scroll = find.byType(Scrollable).first;
        t.state<ScrollableState>(scroll).position.jumpTo(0);
        await t.pumpAndSettle();
        final f = find.text(label);
        await t.scrollUntilVisible(f, 140, scrollable: scroll);
        await t.ensureVisible(f.last);
        await t.pumpAndSettle();
        await t.tap(f.last);
        await t.pump(const Duration(milliseconds: 400));
      }

      Future<void> show(AuthController auth, String role) async {
        final appearance = Appearance();
        for (final theme in ['football', 'general']) {
          await appearance.completeRoleIntro(role, theme);
        }
        await t.pumpWidget(
          AcademyApp(
            key: ValueKey(role),
            auth: auth,
            environment: 'Demo',
            appearance: appearance,
          ),
        );
        await waitFor(find.byType(NativeRoleTabs));
        await t.pump(const Duration(seconds: 1));
        await t.pumpAndSettle();
      }

      await show(owner, 'AcademyOwner');
      await t.tap(
        find.descendant(
          of: find.byType(NavigationBar),
          matching: find.text('المزيد'),
        ),
      );
      await t.pumpAndSettle();
      await tap('الأخبار والفعاليات والإعلانات');
      await waitFor(find.byType(NativeAcademyContent));
      await waitFor(find.text('إضافة محتوى'));
      await tap('إضافة محتوى');
      await waitFor(find.byType(NativeContentEditor));
      await waitFor(find.widgetWithText(TextFormField, 'العنوان'));
      await t.enterText(find.widgetWithText(TextFormField, 'العنوان'), title);
      await t.enterText(
        find.widgetWithText(TextFormField, 'المحتوى'),
        'محتوى اصطناعي لجمهور المدربين في فرع واحد. لا أسماء أطفال ولا رسائل خارجية.',
      );
      await tap('الكل');
      await tap('المدربون');
      await tap(branchName);
      await tap('معاينة');
      await waitFor(find.textContaining('الخطوة 2 من 2'));
      expect(find.text('الجمهور: المدربون'), findsOneWidget);
      expect(find.text('الفروع: $branchName'), findsOneWidget);
      await binding.takeScreenshot('phase-approved-content-review-native');
      await tap('نشر');
      await waitFor(find.byType(NativeAcademyContent));
      await waitFor(find.text(title));
      await binding.takeScreenshot('phase-approved-content-published-native');
      final items = obj(
        await owner.request('GET', '/api/v1/content/news/?paged=true&page=1'),
      );
      final saved = rows(items['items'])
          .singleWhere((n) => n['title'] == title);
      final id = textOf(saved['id']);
      expect(saved['audienceRoles'], ['Coach']);
      expect(saved['targetBranchIds'], [branchId]);
      expect(saved['state'], 'Published');
      expect(
        rows(await coach.request('GET', '/api/v1/news'))
            .any((n) => n['id'] == id),
        isTrue,
      );
      expect(
        rows(await guardian.request('GET', '/api/v1/news'))
            .any((n) => n['id'] == id),
        isFalse,
      );
      await show(coach, 'Coach');
      await t.scrollUntilVisible(
        find.text(title),
        150,
        scrollable: find.byType(Scrollable).first,
      );
      await t.pumpAndSettle();
      expect(find.text(title), findsOneWidget);
      await binding.takeScreenshot('phase-approved-content-coach-home-native');
      // Restore the synthetic content through the actual list action, retaining the record.
      await show(owner, 'AcademyOwner');
      await t.tap(
        find.descendant(
          of: find.byType(NavigationBar),
          matching: find.text('المزيد'),
        ),
      );
      await t.pumpAndSettle();
      await tap('الأخبار والفعاليات والإعلانات');
      await waitFor(find.text(title));
      final card = find
          .ancestor(of: find.text(title), matching: find.byType(GCard))
          .first;
      final remove = find.descendant(of: card, matching: find.text('حذف'));
      await t.ensureVisible(remove);
      await t.tap(remove);
      await t.pumpAndSettle();
      await t.tap(find.text('حذف من العرض'));
      await waitFor(
        find.descendant(
          of: find
              .ancestor(of: find.text(title), matching: find.byType(GCard))
              .first,
          matching: find.text('محذوف من العرض'),
        ),
      );
      final archived = obj(
        await owner.request('GET', '/api/v1/content/news/$id'),
      );
      expect(archived['isActive'], isFalse);
      expect(
        rows(await coach.request('GET', '/api/v1/news'))
            .any((n) => n['id'] == id),
        isFalse,
      );
      expect(t.takeException(), isNull);
      final moneyAfter = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&page=1&pageSize=1',
        ),
      );
      expect(moneyAfter['netAmount'], moneyBefore['netAmount']);
      expect(moneyAfter['totalCount'], moneyBefore['totalCount']);
      await t.pumpWidget(const SizedBox());
      for (final auth in [owner, coach, guardian]) {
        await auth.logout();
        auth.dispose();
      }
    },
  );
}
