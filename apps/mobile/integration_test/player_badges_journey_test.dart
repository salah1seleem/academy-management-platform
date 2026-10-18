import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/player_badges.dart';
import 'package:academy_mobile/features/shared/player_goals.dart'
    show goalCommandId;
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Coach grants and revokes a private badge; linked family sees the saved rule, same-sport summary and preserved history without money changes',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5255',
      );
      Future<AuthController> login(String phone, String role) async {
        final a = AuthController(
          HttpApiTransport(
            AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
          ),
          SecureRefreshStore('academy.v3.badges.$role'),
        );
        await a.restore();
        if (a.session != null) {
          await a.logout();
        }
        final c = await a.requestOtp(phone);
        await a.verify(phone, c, '246810');
        expect(
          a.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return a;
      }

      Widget shell(Widget child) => MaterialApp(
        key: UniqueKey(),
        locale: const Locale('ar'),
        supportedLocales: const [Locale('ar')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        theme: guardianTheme(),
        home: child,
      );
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 180 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      final owner = await login('01099900010', 'owner'),
          coach = await login('01099900022', 'coach'),
          family = await login('01099900110', 'guardian');
      try {
        final child = textOf(
          rows(
            obj(
              await family.request('GET', '/api/v1/guardian/home'),
            )['children'],
          ).first['id'],
        );
        final e = rows(
              obj(
                await coach.request('GET', '/api/v1/evaluations/options'),
              )['enrollments'],
            ).singleWhere((x) => x['playerId'] == child),
            eid = textOf(e['id']);
        final money = await owner.request(
          'GET',
          '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
        );
        final profile = rows(
          obj(
            await owner.request('GET', '/api/v1/people/players/$child'),
          )['enrollments'],
        ).singleWhere((x) => x['id'] == eid);
        final title =
            'مجهود ملحوظ — iOS اصطناعي ${goalCommandId().substring(0, 5)}';
        final initial =
            rows(
                  obj(
                    await coach.request(
                      'GET',
                      '/api/v1/evaluations/badges/enrollments/$eid',
                    ),
                  )['enrollments'],
                ).single['points']
                as int;
        await owner.request(
          'POST',
          '/api/v1/evaluations/badges/rules',
          body: {
            'sportId': e['sportId'],
            'title': title,
            'criterion': 'محاولة وتعاون لاحظهما المدرب، دون مقارنة بين الأطفال أو أثر مالي',
            'points': 20,
            'confirmPublication': true,
            'requestId': goalCommandId(),
          },
        );
        await t.pumpWidget(
          shell(PlayerBadgesPage(auth: coach, enrollmentId: eid)),
        );
        await wait(find.byType(DropdownButtonFormField<String>));
        await t.scrollUntilVisible(
          find.byType(DropdownButtonFormField<String>),
          180,
          scrollable: find.byType(Scrollable).first,
        );
        await t.tap(find.byType(DropdownButtonFormField<String>));
        await t.pumpAndSettle();
        await t.tap(find.text('$title · 20').last);
        await t.pumpAndSettle();
        final grant = find.widgetWithText(
          FilledButton,
          'منح الشارة ونشرها للأسرة',
        );
        await t.scrollUntilVisible(
          grant,
          180,
          scrollable: find.byType(Scrollable).first,
        );
        await t.ensureVisible(grant);
        await t.tap(grant);
        await wait(find.text('شارة سارية'));
        final staff = obj(
          await coach.request(
            'GET',
            '/api/v1/evaluations/badges/enrollments/$eid',
          ),
        );
        expect(rows(staff['enrollments']).single['points'], initial + 20);
        expect(
          rows(rows(staff['enrollments']).single['items'])
              .singleWhere((x) => x['title'] == title)['state'],
          'Granted',
        );
        await t.pumpWidget(
          shell(PlayerBadgesPage(auth: family, playerId: child)),
        );
        await wait(find.text(title));
        final familyCard = find.ancestor(
          of: find.text(title),
          matching: find.byType(GCard),
        );
        final familyActive = find.descendant(
          of: familyCard,
          matching: find.text('شارة سارية'),
        );
        await t.scrollUntilVisible(
          familyActive,
          180,
          scrollable: find.byType(Scrollable).first,
        );
        await t.ensureVisible(familyActive);
        await t.pumpAndSettle();
        expect(find.text('منح الشارة ونشرها للأسرة'), findsNothing);
        expect(find.text('سحب الشارة مع حفظ سجلها'), findsNothing);
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-badge-family-granted-ios');
        }
        await t.pumpWidget(
          shell(PlayerBadgesPage(auth: coach, enrollmentId: eid)),
        );
        await wait(find.text(title));
        final card = find.ancestor(
          of: find.text(title),
          matching: find.byType(GCard),
        );
        final tile = find.descendant(
          of: card,
          matching: find.byType(ExpansionTile),
        );
        await t.scrollUntilVisible(
          tile,
          180,
          scrollable: find.byType(Scrollable).first,
        );
        await t.ensureVisible(tile);
        await t.pumpAndSettle();
        await t.tap(tile);
        await t.pumpAndSettle();
        final revoke = find.descendant(
          of: card,
          matching: find.widgetWithText(FilledButton, 'تأكيد سحب الشارة'),
        );
        await t.scrollUntilVisible(
          revoke,
          180,
          scrollable: find.byType(Scrollable).first,
        );
        await t.ensureVisible(revoke);
        await t.tap(revoke);
        await wait(find.text('شارة مسحوبة — مستبعدة من الملخص'));
        await t.pumpWidget(
          shell(PlayerBadgesPage(auth: family, playerId: child)),
        );
        await wait(find.text('شارة مسحوبة — مستبعدة من الملخص'));
        await t.scrollUntilVisible(
          find.text('شارة مسحوبة — مستبعدة من الملخص'),
          180,
          scrollable: find.byType(Scrollable).first,
        );
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-badge-family-revoked-ios');
        }
        final result = rows(
          obj(
            await family.request(
              'GET',
              '/api/v1/guardian/children/$child/badges',
            ),
          )['enrollments'],
        ).single;
        expect(result['points'], initial);
        expect(
          rows(result['items'])
              .singleWhere((x) => x['title'] == title)['state'],
          'Revoked',
        );
        final after = rows(
          obj(
            await owner.request('GET', '/api/v1/people/players/$child'),
          )['enrollments'],
        ).singleWhere((x) => x['id'] == eid);
        expect(after['subscription'], profile['subscription']);
        expect(after['trainingGroupId'], profile['trainingGroupId']);
        expect(after['trainingLevelId'], profile['trainingLevelId']);
        expect(
          await owner.request(
            'GET',
            '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
          ),
          money,
        );
        expect(t.takeException(), isNull);
      } finally {
        await owner.logout();
        await coach.logout();
        await family.logout();
        await t.pumpWidget(const SizedBox.shrink());
      }
    },
  );
}
