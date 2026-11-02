import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/community_ads.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'support/role_intro.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Native branch approval, optional consent, urgent-before-sponsor, measured view, internal details and withdrawal do not change finance',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5226',
      );
      final transport = HttpApiTransport(
        AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
      );
      final owner = AuthController(
        transport,
        SecureRefreshStore('academy.v3.community.owner.journey'),
      );
      final guardian = AuthController(
        transport,
        SecureRefreshStore('academy.v3.community.guardian.journey'),
      );
      Future<void> login(AuthController auth, String phone) async {
        await auth.restore();
        if (auth.session != null) {
          await auth.logout();
        }
        for (var attempt = 0; ; attempt++) {
          try {
            final challenge = await auth.requestOtp(phone);
            await auth.verify(phone, challenge, '246810');
            break;
          } on ApiFailure catch (e) {
            if (e.status != 429 || attempt >= 2) rethrow;
            await Future<void>.delayed(const Duration(seconds: 31));
          }
        }
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
      }

      Future<void> wait(Finder f) async {
        for (var i = 0; i < 180 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Widget shell(Widget child) => MaterialApp(
        locale: const Locale('ar'),
        supportedLocales: const [Locale('ar')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        theme: guardianTheme(),
        home: child,
      );
      await login(owner, '01099900010');
      expect(owner.session!.selected!.role, 'AcademyOwner');
      const report = '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28';
      final before = await owner.request('GET', report);
      final items = rows(
        obj(
          await owner.request('GET', '/api/v1/manage/community-campaigns/'),
        )['items'],
      );
      final pending = items.singleWhere(
        (c) => textOf(c['internalName']).endsWith('بانتظار المراجعة'),
      );
      var revision = rows(pending['versions'])
          .firstWhere((r) => r['revision'] == pending['currentRevision']);
      String requestId() {
        final h = commandKey().substring(0, 32);
        return '${h.substring(0, 8)}-${h.substring(8, 12)}-${h.substring(12, 16)}-${h.substring(16, 20)}-${h.substring(20)}';
      }

      if (revision['storedState'] != 'PendingApproval') {
        // A repeatable setup through real commands: a new creative never inherits prior approval.
        final created = obj(
          await owner.request(
            'POST',
            '/api/v1/manage/community-campaigns/',
            body: {
              'campaignId': pending['id'],
              'version': pending['version'],
              'internalName': pending['internalName'],
              'advertiser': pending['advertiser'],
              for (final key in [
                'title',
                'body',
                'buttonText',
                'imageAlt',
                'imageAssetId',
                'startsAtUtc',
                'endsAtUtc',
                'priority',
              ])
                key: revision[key],
              'branchIds': rows(revision['targets'])
                  .map((t) => t['branchId'])
                  .toList(),
              'requestId': requestId(),
            },
          ),
        );
        final refreshed = rows(
          obj(
            await owner.request('GET', '/api/v1/manage/community-campaigns/'),
          )['items'],
        ).singleWhere((c) => c['id'] == pending['id']);
        revision = rows(refreshed['versions'])
            .singleWhere((r) => r['id'] == created['revisionId']);
        await owner.request(
          'POST',
          '/api/v1/manage/community-campaigns/${revision['id']}/review',
          body: {
            'action': 'Submit',
            'version': revision['version'],
            'requestId': requestId(),
          },
        );
        revision = rows(
          rows(
            obj(
              await owner.request('GET', '/api/v1/manage/community-campaigns/'),
            )['items'],
          ).singleWhere((c) => c['id'] == pending['id'])['versions'],
        ).singleWhere((r) => r['id'] == created['revisionId']);
      }
      expect(revision['storedState'], 'PendingApproval');
      await t.pumpWidget(shell(OwnerCommunityCampaigns(auth: owner)));
      await wait(find.text('تحديث'));
      final heading = find.text(textOf(pending['internalName']));
      await t.scrollUntilVisible(
        heading,
        400,
        scrollable: find.byType(Scrollable).first,
        maxScrolls: 50,
      );
      final card = find
          .ancestor(of: heading, matching: find.byType(Card))
          .first;
      final approve = find.descendant(
        of: card,
        matching: find.text('اعتماد هذا الفرع'),
      );
      await t.scrollUntilVisible(
        approve,
        300,
        scrollable: find.byType(Scrollable).first,
        maxScrolls: 15,
      );
      await t.pumpAndSettle();
      await t.ensureVisible(approve);
      await t.pumpAndSettle();
      expect(approve.hitTestable(), findsOneWidget);
      await t.tap(approve.hitTestable());
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('تم حفظ المراجعة دون تحصيل أو إرسال خارج التطبيق.'),
        -400,
        scrollable: find.byType(Scrollable).first,
        maxScrolls: 50,
      );
      await wait(find.text('تم حفظ المراجعة دون تحصيل أو إرسال خارج التطبيق.'));
      final approved = rows(
        obj(
          await owner.request('GET', '/api/v1/manage/community-campaigns/'),
        )['items'],
      ).singleWhere((c) => c['id'] == pending['id']);
      expect(
        rows(approved['versions']).singleWhere(
          (r) => r['revision'] == approved['currentRevision'],
        )['storedState'],
        'Approved',
      );
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await t.scrollUntilVisible(
          heading,
          300,
          scrollable: find.byType(Scrollable).first,
          maxScrolls: 50,
        );
        await t.pumpAndSettle();
        await t.ensureVisible(heading);
        await binding.takeScreenshot('phase-community-owner-approval-ios');
      }
      await t.pumpWidget(const SizedBox());
      await login(guardian, '01099900111');
      expect(guardian.session!.selected!.role, 'Guardian');
      await guardian.request(
        'PUT',
        '/api/v1/guardian/community-ads/preference',
        body: {'inAppOptIn': false, 'adultDeclared': true},
      );
      expect(
        obj(
          await guardian.request('GET', '/api/v1/guardian/community-ads/feed'),
        )['item'],
        isNull,
      );
      await t.pumpWidget(shell(GuardianSponsorPreferences(auth: guardian)));
      await wait(find.text('محتوى برعاية داخل التطبيق'));
      await t.tap(find.byType(SwitchListTile));
      await t.ensureVisible(find.text('حفظ الاختيار'));
      await t.tap(find.text('حفظ الاختيار'));
      await wait(
        find.text(
          'تم حفظ اختيارك. إشعارات مواعيد الطفل مستقلة عن هذا الاختيار.',
        ),
      );
      expect(
        obj(
          await guardian.request(
            'GET',
            '/api/v1/guardian/community-ads/preference',
          ),
        )['inAppOptIn'],
        true,
      );
      final feed = obj(
        obj(
          await guardian.request('GET', '/api/v1/guardian/community-ads/feed'),
        )['item'],
      );
      final home = obj(await guardian.request('GET', '/api/v1/guardian/home'));
      expect(feed['subscriptionAsOf'], home['today']);
      expect(
        rows(home['urgentAlerts'])
            .any((a) => textOf(a['message']).startsWith('تنبيه موعد اصطناعي')),
        true,
      );
      await t.pumpWidget(const SizedBox());
      await t.pumpWidget(AcademyApp(auth: guardian, environment: 'Demo'));
      await wait(
        find.byWidgetPredicate(
          (w) => w is NavigationBar || w is AcademyOnboarding,
        ),
      );
      await skipRoleIntroIfPresent(t);
      await wait(find.byType(GuardianSponsoredCard));
      await t.scrollUntilVisible(
        find.byType(GuardianSponsoredCard),
        200,
        scrollable: find.byType(Scrollable).first,
      );
      await t.ensureVisible(find.byType(GuardianUrgentMatchAlerts));
      expect(
        t.getTopLeft(find.byType(GuardianUrgentMatchAlerts)).dy,
        lessThan(t.getTopLeft(find.byType(GuardianSponsoredCard)).dy),
      );
      await t.ensureVisible(find.byType(GuardianSponsoredCard));
      await t.pumpAndSettle();
      await t.pump(const Duration(seconds: 2));
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await binding.takeScreenshot('phase-community-guardian-sponsored-ios');
      }
      final cta = find.descendant(
        of: find.byType(GuardianSponsoredCard),
        matching: find.text(textOf(feed['buttonText'])),
      );
      await t.ensureVisible(cta);
      await t.tap(cta);
      await wait(find.byType(GuardianSponsorDetail));
      await wait(
        find.text('صفحة معلومات، وليست دفعًا أو موافقة على مشاركة بياناتك.'),
      );
      expect(
        obj(
          await guardian.request('GET', '/api/v1/guardian/community-ads/feed'),
        )['item'],
        isNull,
      ); // Measured once per account/day.
      await t.pumpWidget(const SizedBox());
      await t.pumpWidget(shell(GuardianSponsorPreferences(auth: guardian)));
      await wait(find.text('محتوى برعاية داخل التطبيق'));
      await t.tap(find.byType(SwitchListTile));
      await t.ensureVisible(find.text('حفظ الاختيار'));
      await t.tap(find.text('حفظ الاختيار'));
      await wait(
        find.text(
          'تم حفظ اختيارك. إشعارات مواعيد الطفل مستقلة عن هذا الاختيار.',
        ),
      );
      expect(
        obj(
          await guardian.request(
            'GET',
            '/api/v1/guardian/community-ads/preference',
          ),
        )['inAppOptIn'],
        false,
      );
      expect(
        obj(
          await guardian.request('GET', '/api/v1/guardian/community-ads/feed'),
        )['item'],
        isNull,
      );
      expect(await owner.request('GET', report), before);
      await guardian.logout();
      await t.pumpAndSettle();
      expect(find.text('محتوى برعاية داخل التطبيق'), findsNothing);
      await t.pumpWidget(const SizedBox());
      await owner.logout();
      owner.dispose();
      guardian.dispose();
      transport.close();
    },
    timeout: const Timeout(Duration(minutes: 3)),
  );
}
