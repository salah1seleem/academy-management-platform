import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/native_academy_content.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:academy_mobile/features/shared/private_media.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  const resume = String.fromEnvironment('RESUME_ADMIN_NEWS_ID');
  const event = bool.fromEnvironment('ADMIN_IMAGE_EVENT');
  testWidgets(
    'authorized admin publishes a protected OS-picked news image to one branch and all intended roles, then archives it',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      Future<AuthController> login(String phone) async {
        final key = phone == '01099900010' || phone == '01099900011'
            ? 'v45.5278.os-media-entry.$phone'
            : phone == '01099900100'
            ? 'v45.5278.guardian-workspace.$phone'
            : phone == '01099900020'
            ? 'v45.5278.web-settings.coach20'
            : 'v45.5278.admin-news.$phone';
        final a = AuthController(
          HttpApiTransport(AppConfig('Demo', base)),
          SecureRefreshStore(key),
        );
        await a.restore();
        if (a.session == null) {
          final c = await a.requestOtp(phone);
          await a.verify(phone, c, '246810');
        }
        expect(
          a.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return a;
      }

      final owner = await login('01099900010'),
          admin = await login('01099900011');
      expect(admin.session!.selected!.role, 'AcademyAdmin');
      final before = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      final options = obj(
        await admin.request('GET', '/api/v1/content/news/options'),
      );
      expect(options['canCreate'], isTrue);
      final branch = rows(options['branches'])
          .singleWhere((b) => b['name'] == 'فرع مدينة نصر');
      const title = event
          ? 'فعالية مصورة اصطناعية — 22 أكتوبر'
          : 'خبر إداري مصور اصطناعي — 22 أكتوبر';
      Future<void> capture(String name) async {
        await t.pumpAndSettle();
        await t.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 500)),
        );
        await binding.takeScreenshot(event ? '$name-event' : name);
      }

      Future<void> waitFor(Finder f, {int attempts = 240}) async {
        for (var i = 0; i < attempts && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> reach(Finder f) async {
        final scroll = find.byType(Scrollable).first;
        t.state<ScrollableState>(scroll).position.jumpTo(0);
        await t.pumpAndSettle();
        await t.scrollUntilVisible(f, 150, scrollable: scroll, maxScrolls: 120);
        await t.ensureVisible(f.last);
        await t.pumpAndSettle();
      }

      Future<void> tap(String label) async {
        final f = find.text(label);
        await reach(f);
        await t.tap(f.last);
        await t.pump(const Duration(milliseconds: 400));
      }

      Future<void> show(AuthController a, String key) async {
        final appearance = Appearance();
        for (final theme in ['football', 'general']) {
          await appearance.completeRoleIntro(a.session!.selected!.role, theme);
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
        await t.pump(const Duration(seconds: 1));
      }

      Future<void> content() async {
        await t.tap(
          find.descendant(
            of: find.byType(NavigationBar),
            matching: find.text('المزيد'),
          ),
        );
        await t.pumpAndSettle();
        await tap('الأخبار والفعاليات والإعلانات');
        await waitFor(find.byType(NativeAcademyContent));
      }

      await show(admin, 'publisher');
      await content();
      if (resume.isEmpty) {
        // Refuse to duplicate a previously published acceptance record.
        final existing = rows(
          obj(
            await admin.request(
              'GET',
              '/api/v1/content/news/?paged=true&page=1',
            ),
          )['items'],
        );
        expect(
          existing.where((n) => n['title'] == title),
          isEmpty,
          reason:
              'Resume the saved news ID instead of publishing another copy.',
        );
        await tap('إضافة محتوى');
        await waitFor(find.widgetWithText(TextFormField, 'العنوان'));
        if (event) await tap('فعالية');
        await t.enterText(find.widgetWithText(TextFormField, 'العنوان'), title);
        await t.enterText(
          find.widgetWithText(TextFormField, 'المحتوى'),
          'قبول اصطناعي لصورة محمية وجمهور الفرع؛ لا أطفال حقيقيون ولا إرسال خارجي.',
        );
        await tap(textOf(branch['name']));
        await reach(find.text('رفع صورة'));
        await t.tap(find.text('رفع صورة'));
        await t.pump(const Duration(milliseconds: 600));
        debugPrint(
          'Choose I-Academy-Photo-Test.png in the actual iOS picker for the synthetic news image.',
        );
        await waitFor(find.text('تغيير الصورة'), attempts: 1600);
        await reach(find.text('تغيير الصورة'));
        await capture('phase-approved-admin-news-os-image-preview-w375');
        await tap('معاينة');
        await waitFor(find.textContaining('الخطوة 2 من 2'));
        expect(
          find.text('الجمهور: الإداريون، المدربون، أولياء الأمور'),
          findsOneWidget,
        );
        expect(find.text('الفروع: ${branch['name']}'), findsOneWidget);
        await tap('نشر');
        await waitFor(find.byType(NativeAcademyContent));
        await waitFor(find.text(title));
      }
      final saved = resume.isEmpty
          ? rows(
              obj(
                await admin.request(
                  'GET',
                  '/api/v1/content/news/?paged=true&page=1',
                ),
              )['items'],
            ).singleWhere((n) => n['title'] == title)
          : obj(await admin.request('GET', '/api/v1/content/news/$resume'));
      final id = textOf(saved['id']);
      final detail = obj(
        await admin.request('GET', '/api/v1/content/news/$id'),
      );
      debugPrint('Synthetic admin news: $id / asset ${detail['imageAssetId']}');
      expect(detail['state'], 'Published');
      expect(detail['kind'], event ? 'Event' : 'News');
      expect(detail['targetBranchIds'], [branch['id']]);
      expect((detail['audienceRoles'] as List).toSet(), {
        'AcademyAdmin',
        'Coach',
        'Guardian',
      });
      final image = textOf(detail['imageUrl']);
      expect(image, startsWith('/api/v1/assets/'));
      final coach = await login('01099900020'),
          guardian = await login('01099900100'),
          foreign = await login('01099900109'),
          sameBranch = await login('01099900104');
      final foreignHome = obj(
        await foreign.request('GET', '/api/v1/guardian/home'),
      );
      expect(foreignHome['children'].toString(), contains('فرع مدينتي'));
      expect(
        foreignHome['children'].toString(),
        isNot(contains('فرع مدينة نصر')),
      );
      // A different family in the targeted branch remains a legitimate news
      // recipient. This is distinct from group-only child media authorization.
      expect(
        rows(await sameBranch.request('GET', '/api/v1/news'))
            .any((n) => n['id'] == id),
        isTrue,
      );
      expect((await sameBranch.asset(image)).length, greaterThan(0));
      for (final item in [
        (admin, 'admin'),
        (coach, 'coach'),
        (guardian, 'guardian'),
      ]) {
        expect(
          rows(await item.$1.request('GET', '/api/v1/news'))
              .any((n) => n['id'] == id),
          isTrue,
        );
        expect((await item.$1.asset(image)).length, greaterThan(0));
        await show(item.$1, 'news-home-${item.$2}');
        final cards = find.ancestor(
          of: find.text(title),
          matching: find.byType(GCard),
        );
        // A lazy list can contain no matching ancestor before scrolling.
        // Applying .first at that point throws before scrollUntilVisible moves.
        await reach(cards);
        final visibleCard = cards.first;
        await waitFor(
          find.descendant(of: visibleCard, matching: find.byType(PrivateImage)),
        );
        await waitFor(
          find.descendant(of: visibleCard, matching: find.byType(Image)),
        );
        final rect = t.getRect(visibleCard);
        expect(rect.top, greaterThanOrEqualTo(0));
        expect(rect.bottom, lessThanOrEqualTo(667));
        await capture('phase-approved-news-image-${item.$2}-home-w375');
      }
      expect(
        rows(await foreign.request('GET', '/api/v1/news'))
            .any((n) => n['id'] == id),
        isFalse,
      );
      await expectLater(
        foreign.asset(image),
        throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 404)),
      );
      await show(admin, 'archive');
      await content();
      await waitFor(find.text(title));
      final card = find
          .ancestor(of: find.text(title), matching: find.byType(GCard))
          .first;
      final remove = find.descendant(of: card, matching: find.text('حذف'));
      await reach(remove);
      await t.tap(remove);
      await t.pumpAndSettle();
      await t.tap(find.text('حذف من العرض'));
      await t.pump(const Duration(milliseconds: 400));
      await waitFor(
        find.descendant(
          of: find
              .ancestor(of: find.text(title), matching: find.byType(GCard))
              .first,
          matching: find.text('محذوف من العرض'),
        ),
      );
      expect(
        obj(await admin.request('GET', '/api/v1/content/news/$id'))['isActive'],
        isFalse,
      );
      for (final a in [coach, guardian]) {
        expect(
          rows(await a.request('GET', '/api/v1/news'))
              .any((n) => n['id'] == id),
          isFalse,
        );
        await expectLater(
          a.asset(image),
          throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 404)),
        );
      }
      final after = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      expect(after['netAmount'], before['netAmount']);
      expect(after['totalCount'], before['totalCount']);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox.shrink());
      for (final a in [owner, admin, coach, guardian, foreign, sameBranch]) {
        a.dispose();
      }
    },
  );
}
