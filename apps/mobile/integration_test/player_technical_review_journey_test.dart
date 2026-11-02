import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/player_goals.dart'
    show goalCommandId;
import 'package:academy_mobile/features/shared/player_technical_reviews.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Coach requests a human technical review, Owner records readiness and linked Guardian reads the same non-promoting decision',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5249',
      );
      Future<AuthController> login(String phone, String role) async {
        final a = AuthController(
          HttpApiTransport(
            AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
          ),
          SecureRefreshStore('academy.v3.technical.$role'),
        );
        await a.restore();
        if (a.session != null) await a.logout();
        final challenge = await a.requestOtp(phone);
        await a.verify(phone, challenge, '246810');
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
          family = await login('01099900109', 'guardian');
      try {
        final child = textOf(
          rows(
            obj(
              await family.request('GET', '/api/v1/guardian/home'),
            )['children'],
          ).first['id'],
        );
        final enrollment = rows(
          obj(
            await owner.request('GET', '/api/v1/evaluations/options'),
          )['enrollments'],
        ).singleWhere((e) => e['playerId'] == child);
        final eid = textOf(enrollment['id']);
        final journey = obj(
          await owner.request(
            'GET',
            '/api/v1/evaluations/skills/enrollments/$eid/journey',
          ),
        );
        final plan = obj(obj(journey['active'])['plan']),
            pid = textOf(plan['id']);
        final current = obj(
          await coach.request('GET', '/api/v1/evaluations/skills/plans/$pid'),
        );
        for (final s in rows(current['skills'])) {
          final r = obj(s['requirement']);
          await coach.request(
            'POST',
            '/api/v1/evaluations/skills/plans/$pid/reviews',
            body: {
              'requirementId': r['id'],
              'state': 'Mastered',
              'measuredValue': r['unit'] == 'Checklist' ? null : 8,
              'observedOn': current['asOfDate'],
              'note': 'دليل أحدث اصطناعي من مدرب المجموعة',
              'requestId': goalCommandId(),
            },
          );
        }
        final before = obj(
          await owner.request('GET', '/api/v1/people/players/$child'),
        );
        final old = rows(before['enrollments'])
            .singleWhere((e) => e['id'] == eid);
        final money = await owner.request(
          'GET',
          '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
        );
        final path = '/api/v1/evaluations/skills/plans/$pid/technical-reviews';
        // A prior interrupted QA run may have left a request open. New evidence
        // above makes that snapshot historical; close it through the real API,
        // retaining its history instead of resetting or deleting demo rows.
        final prior = obj(await owner.request('GET', path));
        for (final item in rows(prior['items'])) {
          if (item['decision'] != null) continue;
          final request = obj(item['request']);
          final detail = obj(
            await owner.request(
              'GET',
              '/api/v1/evaluations/technical-reviews/${request['id']}',
            ),
          );
          expect(detail['contextChanged'], true);
          await owner.request(
            'POST',
            '/api/v1/evaluations/technical-reviews/${request['id']}/decision',
            body: {
              'version': obj(detail['request'])['version'],
              'outcome': 'ContextChanged',
              'recommendedLevelId': null,
              'familyNote': 'إغلاق طلب اختبار سابق بعد تسجيل أدلة أحدث',
              'requestId': goalCommandId(),
            },
          );
        }
        await t.pumpWidget(
          shell(PlayerTechnicalReviewsPage(auth: coach, planId: pid)),
        );
        await wait(find.text('طلب مراجعة فنية للمستوى'));
        await t.tap(find.text('طلب مراجعة فنية للمستوى'));
        await wait(
          find.text('تم حفظ المراجعة؛ لم يتغير مستوى اللاعب أو مجموعته.'),
        );
        final requests = obj(await owner.request('GET', path)),
            rid = textOf(obj(rows(requests['items']).first['request'])['id']);
        expect(
          obj(rows(requests['items']).first['request'])['actorName'],
          coach.session!.displayName,
        );
        expect(requests['canRequest'], false);
        await t.pumpWidget(
          shell(PlayerTechnicalReviewsPage(auth: owner, requestId: rid)),
        );
        await wait(find.byType(DropdownButtonFormField<String>));
        await t.scrollUntilVisible(
          find.byType(DropdownButtonFormField<String>).first,
          200,
          scrollable: find.byType(Scrollable).first,
        );
        await t.tap(find.byType(DropdownButtonFormField<String>).first);
        await t.pumpAndSettle();
        await t.tap(find.text('مستعد لمراجعة التسكين').last);
        await t.pumpAndSettle();
        final detail = obj(
          await owner.request(
            'GET',
            '/api/v1/evaluations/technical-reviews/$rid',
          ),
        );
        final next = rows(detail['levels']).first;
        await t.ensureVisible(
          find.byType(DropdownButtonFormField<String>).last,
        );
        await t.tap(find.byType(DropdownButtonFormField<String>).last);
        await t.pumpAndSettle();
        await t.tap(find.text(textOf(next['name'])).last);
        await t.pumpAndSettle();
        final note =
            'مراجعة iOS فنية اصطناعية — ${goalCommandId().substring(0, 5)}';
        await t.enterText(find.byType(TextField), note);
        FocusManager.instance.primaryFocus?.unfocus();
        await t.pumpAndSettle();
        await t.scrollUntilVisible(
          find.text('حفظ القرار ونشر الملخص'),
          180,
          scrollable: find.byType(Scrollable).first,
        );
        await t.tap(find.text('حفظ القرار ونشر الملخص'));
        await wait(
          find.text('تم حفظ المراجعة؛ لم يتغير مستوى اللاعب أو مجموعته.'),
        );
        final saved = obj(
          await owner.request(
            'GET',
            '/api/v1/evaluations/technical-reviews/$rid',
          ),
        );
        expect(obj(saved['decision'])['outcome'], 'ReadyForPlacement');
        expect(obj(saved['decision'])['recommendedLevelId'], next['id']);
        expect(saved['canDecide'], false);
        await t.pumpWidget(
          shell(
            PlayerTechnicalReviewsPage(
              auth: family,
              planId: pid,
              playerId: child,
            ),
          ),
        );
        await wait(find.text(note));
        await t.scrollUntilVisible(
          find.text(note),
          180,
          scrollable: find.byType(Scrollable).first,
        );
        expect(find.text('طلب مراجعة فنية للمستوى'), findsNothing);
        expect(find.text('حفظ القرار ونشر الملخص'), findsNothing);
        expect(find.text('الأدلة وقرار المراجعة'), findsNothing);
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-technical-review-family-ios');
        }
        final after = obj(
              await owner.request('GET', '/api/v1/people/players/$child'),
            ),
            now = rows(after['enrollments']).singleWhere((e) => e['id'] == eid);
        expect(now['trainingLevelId'], old['trainingLevelId']);
        expect(now['trainingGroupId'], old['trainingGroupId']);
        expect(now['subscription'], old['subscription']);
        expect(
          await owner.request(
            'GET',
            '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
          ),
          money,
        );
      } finally {
        await family.logout();
        await coach.logout();
        await owner.logout();
        await t.pumpWidget(const SizedBox());
      }
    },
  );
}
