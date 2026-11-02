import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/core/widgets/app_widgets.dart';
import 'package:academy_mobile/features/coach/coach_evaluation.dart';
import 'package:academy_mobile/features/coach/coach_home.dart';
import 'package:academy_mobile/features/coach/monthly_stars.dart';
import 'package:academy_mobile/features/guardian/guardian_report.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class EvaluationAcceptanceTransport extends HttpApiTransport {
  EvaluationAcceptanceTransport(super.config);
  String? evaluationId;
  Json? starRequest;
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    if (method == 'POST' && path == '/api/v1/coach/monthly-stars') {
      starRequest = obj(body);
      debugPrint(
        'Synthetic star intent: ${starRequest!['requestId']} / team ${starRequest!['teamId']} / player ${starRequest!['playerId']}',
      );
    }
    final result = await super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
    if (method == 'POST' && path == '/api/v1/evaluations') {
      evaluationId = textOf(obj(result)['id']);
      debugPrint('Synthetic evaluation created: $evaluationId');
    }
    if (method == 'POST' && path == '/api/v1/coach/monthly-stars') {
      debugPrint('Synthetic star acknowledged: ${obj(result)['id']}');
    }
    return result;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'native assessment preserves zero and null, publishes to linked family, and grants one real monthly star',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final logical = t.view.physicalSize / t.view.devicePixelRatio;
      expect(logical.width, 375);
      debugPrint('Evaluation acceptance viewport: $logical');
      final transport = EvaluationAcceptanceTransport(AppConfig('Demo', base));
      Future<AuthController> login(String phone) async {
        final auth = AuthController(
          phone == '01099900020'
              ? transport
              : HttpApiTransport(AppConfig('Demo', base)),
          SecureRefreshStore('v44.5278.evaluation.$phone'),
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
          guardian = await login('01099900100'),
          foreign = await login('01099900104');
      final before = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      final home = obj(await guardian.request('GET', '/api/v1/guardian/home'));
      final childIds = rows(home['children']).map((c) => c['id']).toSet();
      final options = obj(
        await coach.request('GET', '/api/v1/evaluations/options'),
      );
      const resumeId = String.fromEnvironment('RESUME_EVALUATION_ID');
      final resume = resumeId.isEmpty
          ? null
          : obj(await coach.request('GET', '/api/v1/evaluations/$resumeId'));
      final enrollment = rows(options['enrollments']).firstWhere(
        (e) =>
            childIds.contains(e['playerId']) &&
            e['groupId'] != null &&
            (resume == null || resume['sportEnrollmentId'] == e['id']),
      );
      final child = rows(home['children'])
          .singleWhere((c) => c['id'] == enrollment['playerId']);
      final groups = rows(await coach.request('GET', '/api/v1/coach/groups'));
      final group = groups.singleWhere((g) => g['id'] == enrollment['groupId']);
      Future<void> waitFor(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        if (f.evaluate().isEmpty) {
          await binding.takeScreenshot(
            'phase-approved-evaluation-diagnostic-w375',
          );
        }
        expect(f, findsWidgets);
        await t.pump(const Duration(milliseconds: 350));
      }

      Future<void> locate(Finder f) async {
        await waitFor(find.byType(Scrollable));
        final scroll = find.byType(Scrollable).first;
        t.state<ScrollableState>(scroll).position.jumpTo(0);
        await t.pumpAndSettle();
        await t.scrollUntilVisible(f, 140, scrollable: scroll, maxScrolls: 60);
        await t.ensureVisible(f.last);
        await t.pumpAndSettle();
      }

      Future<void> tap(String label) async {
        final f = find.text(label);
        await locate(f);
        await t.tap(f.last);
        await t.pump(const Duration(milliseconds: 400));
      }

      Future<void> capture(String name) async {
        await t.pumpAndSettle();
        await binding.takeScreenshot('$name-w375');
      }

      Future<void> show(AuthController auth) async {
        final appearance = Appearance();
        for (final theme in ['football', 'general']) {
          await appearance.completeRoleIntro(
            auth.session!.selected!.role,
            theme,
          );
        }
        await t.pumpWidget(
          AcademyApp(
            key: ValueKey(auth.session!.selected!.role),
            auth: auth,
            environment: 'Demo',
            appearance: appearance,
          ),
        );
        await waitFor(find.byType(NativeRoleTabs));
        await t.pumpAndSettle();
      }

      if (resume?['status'] != 'Published') {
        await show(coach);
        await t.tap(
          find.descendant(
            of: find.byType(NavigationBar),
            matching: find.text('الحضور\nوالتقييم'),
          ),
        );
        await waitFor(find.byType(CoachGroupsPage));
        await tap(textOf(group['arabicName']));
        await waitFor(find.byType(CoachGroup));
        final playerName = find.text(textOf(enrollment['player']));
        await locate(playerName);
        final playerCard = find
            .ancestor(of: playerName, matching: find.byType(AppCard))
            .first;
        final evaluate = find.descendant(
          of: playerCard,
          matching: find.text('تقييم اللاعب'),
        );
        await t.ensureVisible(evaluate);
        await t.tap(evaluate);
        await waitFor(find.byType(CoachEvaluation));
        if (resumeId.isNotEmpty) {
          await tap('مسودات اللاعب');
          await waitFor(find.byType(NativeEvaluationDrafts));
          final draft = find.byKey(ValueKey('evaluation-draft-$resumeId'));
          await locate(draft);
          await t.tap(draft);
          await waitFor(find.byType(CoachEvaluation));
          await tap('إعادة التحقق من المسودة نفسها');
        } else {
          await tap('بدء تقييم جديد');
        }
        await waitFor(find.byType(Slider));
        final sliders = t.widgetList<Slider>(find.byType(Slider)).toList();
        expect(sliders.length, greaterThanOrEqualTo(2));
        expect(sliders.every((s) => s.value == 50), isTrue);
        final first = find.byType(Slider).first;
        await locate(first);
        await t.drag(first, const Offset(500, 0));
        await t.pumpAndSettle();
        expect(t.widget<Slider>(first).value, 0);
        final criterionId = (sliders[1].key as ValueKey<String>).value
            .replaceFirst('evaluation-score-', '');
        final confirm = find.byKey(ValueKey('evaluation-assess-$criterionId'));
        await locate(confirm);
        await t.tap(confirm);
        await t.pumpAndSettle();
        t
            .state<ScrollableState>(find.byType(Scrollable).first)
            .position
            .jumpTo(0);
        await t.pumpAndSettle();
        await capture('phase-approved-evaluation-zero-and-explicit50-native');
        await tap('نشر التقييم لولي الأمر');
        await waitFor(find.text('تم نشر التقييم.'));
        await capture('phase-approved-evaluation-published-native');
      }
      final id = resumeId.isEmpty ? transport.evaluationId : resumeId;
      expect(id, isNotNull);
      final saved = obj(await coach.request('GET', '/api/v1/evaluations/$id'));
      expect(saved['sportEnrollmentId'], enrollment['id']);
      expect(saved['status'], 'Published');
      final assessed = rows(saved['scores'])
          .where((s) => s['score'] != null)
          .toList();
      expect(assessed, hasLength(2));
      expect(assessed.map((s) => s['score']).toSet(), {0, 50});
      expect(
        rows(saved['scores']).where((s) => s['score'] == null),
        isNotEmpty,
      );
      final report = obj(
        await guardian.request(
          'GET',
          '/api/v1/guardian/evaluations/$id/report',
        ),
      );
      expect(rows(report['criteria']), hasLength(2));
      expect(rows(report['criteria']).map((s) => s['score']).toSet(), {0, 50});
      final refreshed = obj(
        await guardian.request('GET', '/api/v1/guardian/home'),
      );
      final refreshedChild = rows(refreshed['children'])
          .singleWhere((c) => c['id'] == child['id']);
      final sport = rows(refreshedChild['sports'])
          .singleWhere((e) => e['id'] == enrollment['id']);
      expect(obj(sport['performance'])['evaluationId'], id);
      expect(rows(obj(sport['performance'])['criteria']), hasLength(2));
      final notices = rows(
        obj(await guardian.request('GET', '/api/v1/notifications'))['items'],
      );
      final notice = notices.singleWhere(
        (n) => n['targetKind'] == 'Evaluation' && n['targetId'] == id,
      );
      await show(guardian);
      await t.tap(find.byTooltip('التنبيهات'));
      await waitFor(find.byType(NotificationInbox));
      final message = find.text(textOf(notice['message']));
      await locate(message);
      final card = find
          .ancestor(of: message, matching: find.byType(GCard))
          .first;
      final open = find.descendant(
        of: card,
        matching: find.text('عرض التقييم'),
      );
      await t.ensureVisible(open);
      await t.tap(open);
      await waitFor(find.byType(PlayerReport));
      await waitFor(find.text(textOf(report['player'])));
      await capture('phase-approved-guardian-published-partial-report');
      for (final criterion in rows(
        saved['scores'],
      ).where((s) => s['score'] == null)) {
        expect(find.text(textOf(criterion['name'])), findsNothing);
      }
      try {
        await foreign.request('GET', '/api/v1/guardian/evaluations/$id/report');
        fail('Unlinked family must not read this report');
      } on ApiFailure catch (e) {
        expect(e.status, 404);
      }
      expect(
        rows(
          obj(await foreign.request('GET', '/api/v1/notifications'))['items'],
        ).any((n) => n['targetId'] == id),
        isFalse,
      );
      final starOptions = obj(
        await coach.request('GET', '/api/v1/coach/monthly-stars'),
      );
      const resumeStar = String.fromEnvironment('RESUME_STAR_REQUEST_ID');
      const resumeTeam = String.fromEnvironment('RESUME_STAR_TEAM_ID');
      if (resumeStar.isNotEmpty) expect(resumeTeam, isNotEmpty);
      final team = rows(starOptions['teams']).firstWhere(
        (team) =>
            (resumeStar.isEmpty
                ? (team['remaining'] as num) > 0
                : team['id'] == resumeTeam) &&
            rows(team['players']).any((p) => p['id'] == child['id']),
      );
      final oldStars =
          rows(team['players'])
                  .singleWhere((p) => p['id'] == child['id'])['stars']
              as int;
      final oldRemaining = team['remaining'] as int;
      await show(coach);
      await t.tap(
        find.descendant(
          of: find.byType(NavigationBar),
          matching: find.text('المزيد'),
        ),
      );
      await tap('نجوم فريقي الشهرية');
      await waitFor(find.byType(CoachMonthlyStars));
      final grant = find.byKey(ValueKey('star-${team['id']}-${child['id']}'));
      await locate(grant);
      if (resumeStar.isEmpty) {
        await t.tap(grant);
        await t.pumpAndSettle();
        await t.tap(find.text('تأكيد'));
        await t.pump(const Duration(milliseconds: 600));
        await waitFor(find.text('تم منح النجمة وإبلاغ ولي الأمر.'));
      } else {
        transport.starRequest = {
          'teamId': team['id'],
          'playerId': child['id'],
          'requestId': resumeStar,
        };
      }
      final latest = obj(
        await coach.request('GET', '/api/v1/coach/monthly-stars'),
      );
      final updated = rows(latest['teams'])
          .singleWhere((x) => x['id'] == team['id']);
      final delta = resumeStar.isEmpty ? 1 : 0;
      expect(updated['remaining'], oldRemaining - delta);
      expect(
        rows(updated['players'])
            .singleWhere((p) => p['id'] == child['id'])['stars'],
        oldStars + delta,
      );
      final replay = await coach.request(
        'POST',
        '/api/v1/coach/monthly-stars',
        body: transport.starRequest,
      );
      expect(obj(replay)['replay'], isTrue);
      expect(await coach.request('GET', '/api/v1/coach/monthly-stars'), latest);
      await locate(grant);
      await capture('phase-approved-coach-monthly-star-stored');
      final finalHome = obj(
        await guardian.request('GET', '/api/v1/guardian/home'),
      );
      expect(
        rows(finalHome['children'])
            .singleWhere((c) => c['id'] == child['id'])['monthlyStars'],
        (refreshedChild['monthlyStars'] as int) + delta,
      );
      expect(
        rows(
          obj(await guardian.request('GET', '/api/v1/notifications'))['items'],
        ).any((n) => n['eventKey'] == 'Star' && n['targetId'] == child['id']),
        isTrue,
      );
      expect(
        rows(
          obj(await foreign.request('GET', '/api/v1/notifications'))['items'],
        ).any((n) => n['eventKey'] == 'Star' && n['targetId'] == child['id']),
        isFalse,
      );
      final after = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      expect(after['totalCount'], before['totalCount']);
      expect(after['netAmount'], before['netAmount']);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      for (final auth in [owner, coach, guardian, foreign]) {
        await auth.logout();
        auth.dispose();
      }
    },
  );
}
