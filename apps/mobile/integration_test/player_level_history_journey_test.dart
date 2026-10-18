import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/player_goals.dart'
    show goalCommandId;
import 'package:academy_mobile/features/shared/player_skills.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Real manual level reviews are shared with the current coach and linked family without private notes or financial changes',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5243',
      );
      Future<AuthController> login(String phone, String role) async {
        final a = AuthController(
          HttpApiTransport(
            AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
          ),
          SecureRefreshStore('academy.v3.level-history.$role'),
        );
        await a.restore();
        if (a.session != null) {
          await a.logout();
        }
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
      final child = textOf(
        rows(
          obj(await family.request('GET', '/api/v1/guardian/home'))['children'],
        ).first['id'],
      );
      final e = rows(
        obj(
          await coach.request('GET', '/api/v1/evaluations/options'),
        )['enrollments'],
      ).firstWhere((e) => e['playerId'] == child);
      final eid = textOf(e['id']);
      final money = await owner.request(
        'GET',
        '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
      );
      final before = obj(
        await owner.request('GET', '/api/v1/people/players/$child'),
      );
      final original = rows(before['enrollments'])
          .singleWhere((x) => x['id'] == eid);
      final levels = rows(
        obj(
          await owner.request('GET', '/api/v1/evaluations/skills/options'),
        )['levels'],
      ).where((l) => l['sportId'] == e['sportId']).toList();
      expect(levels.length, greaterThanOrEqualTo(2));
      final a = levels.first, b = levels.last;
      final note = 'سجل إداري خاص — ${goalCommandId().substring(0, 5)}';
      for (final target in [a, b]) {
        final source = obj(
          await owner.request(
            'GET',
            '/api/v1/people/enrollments/$eid/placement-level',
          ),
        );
        await owner.request(
          'PUT',
          '/api/v1/people/enrollments/$eid/placement-level',
          body: {
            'levelId': target['id'],
            'version': source['version'],
            'requestId': goalCommandId(),
            'note': note,
          },
        );
      }
      final saved = obj(
        await owner.request(
          'GET',
          '/api/v1/people/enrollments/$eid/placement-level',
        ),
      );
      expect(rows(obj(saved['history'])['items']).first['note'], note);
      await t.pumpWidget(
        shell(PlayerSkillsPage(auth: coach, enrollmentId: eid)),
      );
      await wait(find.text('سجل مراجعات المستوى'));
      await t.ensureVisible(find.text('سجل مراجعات المستوى'));
      await t.tap(find.text('سجل مراجعات المستوى'));
      await t.pumpAndSettle();
      expect(
        find.text('${a['arabicName']} ← ${b['arabicName']}'),
        findsWidgets,
      );
      expect(find.text(note), findsNothing);
      expect(find.text('اعتماد المراجعة'), findsNothing);
      final data = obj(
        await family.request('GET', '/api/v1/guardian/children/$child/skills'),
      );
      final item = rows(data['items'])
          .singleWhere((j) => obj(j['enrollment'])['id'] == eid);
      expect(
        rows(obj(item['levelHistory'])['items']).first['toLevelName'],
        b['arabicName'],
      );
      expect(
        rows(obj(item['levelHistory'])['items'])
            .every((r) => r['note'] == null),
        true,
      );
      await t.pumpWidget(
        shell(PlayerSkillsPage(auth: family, playerId: child)),
      );
      await wait(find.text('سجل مراجعات المستوى'));
      await t.ensureVisible(find.text('سجل مراجعات المستوى'));
      await t.tap(find.text('سجل مراجعات المستوى'));
      await t.pumpAndSettle();
      final transition = find.text('${a['arabicName']} ← ${b['arabicName']}');
      await t.ensureVisible(transition.first);
      expect(transition, findsWidgets);
      expect(find.text(note), findsNothing);
      expect(find.text('اعتماد المراجعة'), findsNothing);
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await binding.takeScreenshot('phase-level-history-guardian-ios');
      }
      final after = obj(
        await owner.request('GET', '/api/v1/people/players/$child'),
      );
      final current = rows(after['enrollments'])
          .singleWhere((x) => x['id'] == eid);
      expect(current['trainingLevelId'], b['id']);
      expect(current['trainingGroupId'], original['trainingGroupId']);
      expect(current['subscription'], original['subscription']);
      expect(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
        ),
        money,
      );
      await family.logout();
      await coach.logout();
      await owner.logout();
      await t.pumpWidget(const SizedBox());
    },
  );
}
