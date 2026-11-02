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
    'Owner assigns a versioned plan, coach publishes evidence and linked guardian sees the same progress without promotion or money',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5241',
      );
      Future<AuthController> login(String phone, String role) async {
        final a = AuthController(
          HttpApiTransport(
            AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
          ),
          SecureRefreshStore('academy.v3.skills.$role'),
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

      Widget shell(Widget page) => MaterialApp(
        key: UniqueKey(),
        locale: const Locale('ar'),
        supportedLocales: const [Locale('ar')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        theme: guardianTheme(),
        home: page,
      );
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 180 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      final family = await login('01099900109', 'guardian'),
          owner = await login('01099900010', 'owner'),
          coach = await login('01099900022', 'coach');
      final child = textOf(
        rows(
          obj(await family.request('GET', '/api/v1/guardian/home'))['children'],
        ).first['id'],
      );
      final enrollment = rows(
        obj(
          await coach.request('GET', '/api/v1/evaluations/options'),
        )['enrollments'],
      ).firstWhere((e) => e['playerId'] == child);
      final e = textOf(enrollment['id']);
      final levels = rows(
        obj(
          await owner.request('GET', '/api/v1/evaluations/skills/options'),
        )['levels'],
      );
      final level = levels.singleWhere(
        (l) => l['arabicName'] == 'أساسيات الكرة — مستوى اصطناعي',
      );
      await owner.request(
        'PUT',
        '/api/v1/people/enrollments/$e/placement-level',
        body: {'levelId': level['id']},
      );
      final money = await owner.request(
            'GET',
            '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
          ),
          profile = await owner.request('GET', '/api/v1/people/players/$child');
      final definitions =
          rows(
            obj(
              await owner.request(
                'GET',
                '/api/v1/evaluations/skills/curricula?levelId=${level['id']}',
              ),
            )['items'],
          ).where((c) => c['state'] == 'Published').toList()..sort(
            (a, b) => (b['revisionNumber'] as num).compareTo(
              a['revisionNumber'] as num,
            ),
          );
      final source = obj(
        await owner.request(
          'GET',
          '/api/v1/evaluations/skills/curricula/${definitions.first['id']}',
        ),
      );
      final title = 'خطة المحاكي — ${goalCommandId().substring(0, 5)}';
      final created = obj(
        await owner.request(
          'POST',
          '/api/v1/evaluations/skills/curricula',
          body: {
            'trainingLevelId': level['id'],
            'title': title,
            'requirements': rows(source['requirements'])
                .map(
                  (r) => {
                    'name': r['name'],
                    'unit': r['unit'],
                    'protocol': r['protocol'],
                  },
                )
                .toList(),
            'copiedFromId': definitions.first['id'],
            'sourceVersion': definitions.first['version'],
            'requestId': goalCommandId(),
          },
        ),
      );
      final definition = obj(
        await owner.request(
          'GET',
          '/api/v1/evaluations/skills/curricula/${created['id']}',
        ),
      );
      await owner.request(
        'POST',
        '/api/v1/evaluations/skills/curricula/${created['id']}/publish',
        body: {
          'version': obj(definition['item'])['version'],
          'requestId': goalCommandId(),
        },
      );
      final old = obj(
        await owner.request(
          'GET',
          '/api/v1/evaluations/skills/enrollments/$e/journey',
        ),
      )['active'];
      await t.pumpWidget(shell(PlayerSkillsPage(auth: owner, enrollmentId: e)));
      await wait(find.byType(DropdownButtonFormField<String>));
      await t.ensureVisible(find.byType(DropdownButtonFormField<String>));
      await t.tap(find.byType(DropdownButtonFormField<String>));
      await t.pumpAndSettle();
      await t.tap(find.textContaining(title).last);
      await t.pumpAndSettle();
      final assign = find.text(
        old == null ? 'إسناد خطة المهارات' : 'استبدال الخطة',
      );
      await t.ensureVisible(assign);
      await t.tap(assign);
      await t.pumpAndSettle();
      if (old != null) {
        await wait(find.text('تأكيد'));
        await t.tap(find.text('تأكيد'));
      }
      await wait(find.text('تم حفظ التحديث الفني.'));
      final journey = obj(
            await owner.request(
              'GET',
              '/api/v1/evaluations/skills/enrollments/$e/journey',
            ),
          ),
          active = obj(journey['active']),
          plan = textOf(obj(active['plan'])['id']);
      expect(obj(obj(active['plan'])['curriculum'])['id'], created['id']);
      await t.pumpWidget(shell(PlayerSkillsPage(auth: coach, planId: plan)));
      await wait(find.byType(DropdownButtonFormField<String>));
      await t.ensureVisible(find.byType(DropdownButtonFormField<String>).first);
      await t.tap(find.byType(DropdownButtonFormField<String>).first);
      await t.pumpAndSettle();
      final firstName = textOf(
        obj(rows(active['skills']).first['requirement'])['name'],
      );
      await t.tap(find.text(firstName).last);
      await t.pumpAndSettle();
      await t.ensureVisible(find.byType(DropdownButtonFormField<String>).last);
      await t.tap(find.byType(DropdownButtonFormField<String>).last);
      await t.pumpAndSettle();
      await t.tap(find.text('أتقن').last);
      await t.pumpAndSettle();
      final note = 'دليل فني اصطناعي من المحاكي ${plan.substring(0, 4)}';
      final noteField = find.widgetWithText(
        TextFormField,
        'ملاحظة للأسرة (اختياري)',
      );
      await t.ensureVisible(noteField);
      await t.enterText(noteField, note);
      await t.testTextInput.receiveAction(TextInputAction.done);
      await t.pumpAndSettle();
      final save = find.text('حفظ ونشر الملاحظة للأسرة');
      await t.ensureVisible(save);
      await t.tap(save);
      await wait(find.text('تم حفظ التحديث الفني.'));
      await t.ensureVisible(find.text(title));
      await t.pumpAndSettle();
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await binding.takeScreenshot('phase-skills-coach-ios');
      }
      final actual = obj(
        await family.request(
          'GET',
          '/api/v1/guardian/children/$child/skills/plans/$plan',
        ),
      );
      expect(obj(actual['progress'])['mastered'], 1);
      expect(
        obj(actual['progress'])['total'],
        rows(source['requirements']).length,
      );
      expect(rows(actual['history']).single['note'], note);
      expect(actual['canReview'], false);
      await t.pumpWidget(
        shell(PlayerSkillsPage(auth: family, playerId: child, planId: plan)),
      );
      await wait(find.text(title));
      await t.ensureVisible(find.text(note));
      expect(find.text('حفظ ونشر الملاحظة للأسرة'), findsNothing);
      await t.ensureVisible(find.text(title));
      await t.pumpAndSettle();
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await binding.takeScreenshot('phase-skills-guardian-ios');
      }
      expect(
        await owner.request('GET', '/api/v1/people/players/$child'),
        profile,
      );
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
