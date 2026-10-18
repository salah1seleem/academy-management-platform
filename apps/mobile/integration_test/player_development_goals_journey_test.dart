import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/player_goals.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Coach creates publishes and reviews a goal; the linked Guardian sees it without automatic promotion or money',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5240',
      );
      Future<AuthController> login(String phone, String role) async {
        final a = AuthController(
          HttpApiTransport(
            AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
          ),
          SecureRefreshStore('academy.v3.player-goals.$role'),
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

      final guardian = await login('01099900109', 'guardian');
      final home = obj(await guardian.request('GET', '/api/v1/guardian/home'));
      final child = textOf(rows(home['children']).first['id']);
      final owner = await login('01099900010', 'owner');
      final financial = await owner.request(
        'GET',
        '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
      );
      final profile = await owner.request(
        'GET',
        '/api/v1/people/players/$child',
      );
      final coach = await login('01099900022', 'coach');
      final options = obj(
        await coach.request('GET', '/api/v1/evaluations/options'),
      );
      final enrollment = rows(options['enrollments'])
          .firstWhere((e) => e['playerId'] == child);
      final enrollmentId = textOf(enrollment['id']);
      expect(
        obj(
          await coach.request(
            'GET',
            '/api/v1/evaluations/development-goals/enrollments/$enrollmentId',
          ),
        )['canCreate'],
        true,
      );
      await t.pumpWidget(
        shell(PlayerGoalsPage(auth: coach, enrollmentId: enrollmentId)),
      );
      await wait(find.text('هدف جديد'));
      await t.ensureVisible(find.text('هدف جديد'));
      await t.tap(find.text('هدف جديد'));
      await wait(find.widgetWithText(TextFormField, 'عنوان الهدف'));
      final title = 'تمرير متقن — تجربة ${goalCommandId().substring(0, 4)}';
      await t.enterText(
        find.widgetWithText(TextFormField, 'عنوان الهدف'),
        title,
      );
      await t.enterText(
        find.widgetWithText(TextFormField, 'ما الذي نعمل عليه؟'),
        'خطوة فنية مع المدرب نراجعها دون ترقية آلية.',
      );
      await t.testTextInput.receiveAction(TextInputAction.done);
      await t.pumpAndSettle();
      await t.ensureVisible(find.text('حفظ المسودة'));
      await t.tap(find.text('حفظ المسودة'));
      await wait(find.text('نشر الهدف للأسرة'));
      final list = obj(
        await coach.request(
          'GET',
          '/api/v1/evaluations/development-goals/enrollments/$enrollmentId',
        ),
      );
      final goal = rows(list['items']).firstWhere((g) => g['title'] == title);
      final id = textOf(goal['id']);
      final response =
          'مراجعة اصطناعية من المدرب: تحسن التمرير — ${id.substring(0, 4)}';
      await t.ensureVisible(find.text('نشر الهدف للأسرة'));
      await t.tap(find.text('نشر الهدف للأسرة'));
      await wait(find.text('قيد العمل · منشور للأسرة'));
      final note = find.widgetWithText(
        TextField,
        'ملاحظة التقدم للأسرة (اختياري)',
      );
      await t.ensureVisible(note);
      await t.enterText(note, response);
      await t.testTextInput.receiveAction(TextInputAction.done);
      await t.pumpAndSettle();
      await t.ensureVisible(find.text('حفظ المراجعة'));
      await t.tap(find.text('حفظ المراجعة'));
      await wait(find.text('تم حفظ تحديث الهدف وسجل المراجعة.'));
      await t.ensureVisible(find.text(title));
      await t.pumpAndSettle();
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await binding.takeScreenshot('phase-player-goal-coach-ios');
      }
      final saved = obj(
        await coach.request('GET', '/api/v1/evaluations/development-goals/$id'),
      );
      expect(rows(saved['history']).map((c) => c['action']).toList(), [
        'Reviewed',
        'Published',
        'Created',
      ]);
      await coach.logout();
      // One real guardian session for the whole multi-actor journey: do not
      // request a second OTP just to revisit the same family's published data.
      final family = guardian;
      final mine = obj(
        await family.request(
          'GET',
          '/api/v1/guardian/children/$child/development-goals',
        ),
      );
      final actual = rows(mine['items'])
          .firstWhere((r) => obj(r['goal'])['id'] == id);
      expect(rows(actual['reviews']).map((c) => c['action']).toList(), [
        'Reviewed',
        'Published',
      ]);
      await t.pumpWidget(shell(PlayerGoalsPage(auth: family, playerId: child)));
      await wait(find.text(title));
      expect(find.text('حفظ المراجعة'), findsNothing);
      expect(find.text('حفظ المسودة'), findsNothing);
      await t.ensureVisible(find.text(title));
      await t.pumpAndSettle();
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await binding.takeScreenshot('phase-player-goal-guardian-ios');
      }
      expect(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?from=2026-09-01&to=2026-09-28',
        ),
        financial,
      );
      expect(
        await owner.request('GET', '/api/v1/people/players/$child'),
        profile,
      );
      await family.logout();
      await owner.logout();
      await t.pumpWidget(const SizedBox());
    },
  );
}
