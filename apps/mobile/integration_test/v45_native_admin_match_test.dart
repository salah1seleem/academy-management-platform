import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/match_experience.dart';
import 'package:academy_mobile/features/shared/matches.dart';
import 'package:academy_mobile/features/shared/native_match_management.dart';
import 'package:academy_mobile/features/shared/notification_inbox.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class MatchAcceptanceTransport extends HttpApiTransport {
  MatchAcceptanceTransport(super.config);
  String? createdId;
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) async {
    final result = await super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
    if (method == 'POST' && path == '/api/v1/manage/matches') {
      createdId = textOf(obj(result)['id']);
      debugPrint('Synthetic match created: $createdId');
    }
    return result;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'authorized native administrator publishes one match and saves its result while denied administrator cannot access management',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final logical = t.view.physicalSize / t.view.devicePixelRatio;
      debugPrint('Match acceptance native viewport: $logical');
      const expectedWidth = String.fromEnvironment('EXPECTED_LOGICAL_WIDTH');
      if (expectedWidth.isNotEmpty) {
        expect(logical.width, double.parse(expectedWidth));
      }
      final transport = MatchAcceptanceTransport(AppConfig('Demo', base));
      Future<AuthController> login(String phone) async {
        final auth = AuthController(
          phone == '01099900011'
              ? transport
              : HttpApiTransport(AppConfig('Demo', base)),
          SecureRefreshStore(
            phone == '01099900010'
                ? 'v45.5278.os-media-entry.01099900010'
                : phone == '01099900011'
                ? 'v45.5278.os-media-entry.01099900011'
                : phone == '01099900020'
                ? 'v45.5278.web-settings.coach20'
                : phone == '01099900100'
                ? 'v45.5278.guardian-workspace.01099900100'
                : phone == '01099900993'
                ? 'v45.5278.enrollment-no-discount.01099900993'
                : 'v45.5278.admin-news.$phone',
          ),
        );
        await auth.restore();
        if (auth.session == null) {
          final c = await auth.requestOtp(phone);
          await auth.verify(phone, c, '246810');
        }
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return auth;
      }

      final owner = await login('01099900010'),
          admin = await login('01099900011'),
          denied = await login('01099900993'),
          coach = await login('01099900020'),
          guardian = await login('01099900100'),
          foreign = await login('01099900104');
      expect(owner.session!.selected!.role, 'AcademyOwner');
      expect(coach.session!.selected!.role, 'Coach');
      expect(admin.session!.selected!.role, 'AcademyAdmin');
      final before = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      final ownChildren = rows(
        obj(await guardian.request('GET', '/api/v1/guardian/home'))['children'],
      ).map((c) => c['id']).toSet();
      final foreignChildren = rows(
        obj(await foreign.request('GET', '/api/v1/guardian/home'))['children'],
      ).map((c) => c['id']).toSet();
      final options = obj(
        await owner.request('GET', '/api/v1/manage/matches/options'),
      );
      final selectedCoach = rows(options['coaches'])
          .singleWhere((c) => c['id'] == coach.session!.selected!.id);
      Json? team, tournament;
      List<Json> reviewed = [];
      for (final candidate in rows(options['teams']).where(
        (t) =>
            t['isActive'] == true &&
            (selectedCoach['allBranches'] == true ||
                (selectedCoach['branchIds'] as List).contains(t['branchId'])) &&
            (selectedCoach['allSports'] == true ||
                (selectedCoach['sportIds'] as List).contains(t['sportId'])),
      )) {
        for (final cup in rows(options['tournaments']).where(
          (c) => c['isActive'] == true && c['sportId'] == candidate['sportId'],
        )) {
          final roster = rows(
            obj(
              await owner.request(
                'GET',
                '/api/v1/manage/matches/teams/${candidate['id']}/roster?tournamentId=${cup['id']}',
              ),
            )['items'],
          );
          if (!roster.any((p) => ownChildren.contains(p['playerId'])) ||
              roster.any((p) => foreignChildren.contains(p['playerId']))) {
            continue;
          }
          team = candidate;
          tournament = cup;
          reviewed = roster;
          break;
        }
        if (team != null) break;
      }
      expect(
        team,
        isNotNull,
        reason: 'Use an existing eligible roster with one linked family and an unrelated control family.',
      );

      final calendar = obj(
        await owner.request('GET', '/api/v1/academy/calendar'),
      );
      final today = DateTime.parse(textOf(calendar['today']));
      const opponent = 'منافس قبول إداري اصطناعي22أكتوبر';
      if (const String.fromEnvironment('RESUME_MATCH_ID').isEmpty) {
        expect(
          rows(await admin.request('GET', '/api/v1/manage/matches'))
              .where((m) => m['opponent'] == opponent),
          isEmpty,
          reason: 'Resume the stored match ID; never create another copy.',
        );
      }
      Future<void> waitFor(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        if (f.evaluate().isEmpty) {
          await binding.takeScreenshot('phase-approved-match-diagnostic');
        }
        expect(f, findsWidgets);
        await t.pump(const Duration(milliseconds: 350));
      }

      Future<void> locate(Finder f) async {
        await waitFor(find.byType(Scrollable));
        final scroll = find.byType(Scrollable).first;
        t.state<ScrollableState>(scroll).position.jumpTo(0);
        await t.pumpAndSettle();
        await t.scrollUntilVisible(f, 150, scrollable: scroll, maxScrolls: 120);
        await t.ensureVisible(f.last);
        await t.pumpAndSettle();
      }

      Future<void> tap(String label) async {
        final f = find.text(label);
        await locate(f);
        await t.tap(f.last);
        await t.pump(const Duration(milliseconds: 400));
      }

      Future<void> field(String label, String value) async {
        final f = find.widgetWithText(TextFormField, label);
        await locate(f);
        await t.enterText(f, value);
        await t.pump(const Duration(milliseconds: 250));
      }

      Future<void> choose(String label, String value) async {
        final f = find.widgetWithText(DropdownButtonFormField<String>, label);
        await locate(f);
        await t.tap(f);
        await t.pumpAndSettle();
        final item = find.text(value);
        await t.ensureVisible(item.last);
        await t.tap(item.last);
        await t.pumpAndSettle();
      }

      Future<void> date(DateTime day) async {
        final f = find.widgetWithText(TextFormField, 'تاريخ المباراة');
        await locate(f);
        await t.tap(f);
        await t.pumpAndSettle();
        final local = MaterialLocalizations.of(
          t.element(find.byType(DatePickerDialog)),
        );
        await t.tap(find.byIcon(Icons.edit_outlined));
        await t.pumpAndSettle();
        await t.enterText(
          find.descendant(
            of: find.byType(DatePickerDialog),
            matching: find.byType(TextField),
          ),
          local.formatCompactDate(day),
        );
        await t.tap(find.text(local.okButtonLabel));
        await t.pumpAndSettle();
      }

      Future<void> capture(String name) async {
        await t.pumpAndSettle();
        await t.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 500)),
        );
        await binding.takeScreenshot(
          '${name.replaceFirst('phase-approved-match-', 'phase-approved-admin-match-')}-w${logical.width.toInt()}',
        );
      }

      Future<void> show(AuthController auth) async {
        final role = auth.session!.selected!.role;
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
        await t.pumpAndSettle();
      }

      Future<void> matches(AuthController auth) async {
        await show(auth);
        await t.tap(
          find.descendant(
            of: find.byType(NavigationBar),
            matching: find.text('المباريات'),
          ),
        );
        await waitFor(find.byType(MatchSchedule));
      }

      Future<void> manageAdmin() async {
        await show(admin);
        await t.tap(
          find.descendant(
            of: find.byType(NavigationBar),
            matching: find.text('المزيد'),
          ),
        );
        await waitFor(find.byType(NativeMore));
        await tap('إدارة المباريات والنتائج');
        await waitFor(find.byType(NativeMatchManagement));
      }

      const resumeId = String.fromEnvironment('RESUME_MATCH_ID');
      if (resumeId.isEmpty) {
        await manageAdmin();
        await waitFor(find.text('إضافة مباراة'));
        await tap('إضافة مباراة');
        await waitFor(find.byType(NativeMatchEditor));
        await choose('الفريق', textOf(team!['name']));
        await choose('البطولة', textOf(tournament!['name']));
        await field('المنافس', opponent);
        await field('الملعب', 'ملعب قبول اصطناعي');
        await date(today.add(const Duration(days: 1)));
        await choose('مدرب المباراة', textOf(selectedCoach['name']));
        await tap('مراجعة المشاركين');
        await waitFor(find.text(textOf(reviewed.first['name'])));
        await tap('نشر المباراة للمشاركين');
        await capture('phase-approved-match-publish-review-native');
        await tap('حفظ المباراة');
        await waitFor(find.byType(NativeMatchManagement));
      }
      final id = resumeId.isEmpty ? transport.createdId : resumeId;
      expect(id, isNotNull);
      if (resumeId.isNotEmpty) {
        expect(transport.createdId, isNull);
      }
      var saved = obj(await owner.request('GET', '/api/v1/manage/matches/$id'));
      expect(saved['status'], 'Published');
      expect(saved['assignedCoachMembershipId'], coach.session!.selected!.id);
      expect(
        rows(saved['players']).map((p) => p['playerId']).toSet(),
        reviewed.map((p) => p['playerId']).toSet(),
      );
      for (final recipient in [coach, guardian]) {
        final feed = rows(await recipient.request('GET', '/api/v1/my-matches'));
        expect(feed.any((m) => m['id'] == id && m['ourScore'] == null), isTrue);
        final inbox = rows(
          obj(await recipient.request('GET', '/api/v1/notifications'))['items'],
        );
        final notice = inbox.singleWhere(
          (n) => n['matchId'] == id && n['eventKey'] == 'Match',
        );
        if (resumeId.isEmpty) {
          expect(notice['isRead'], isFalse);
        }
        expect(textOf(notice['message']).contains('{'), isFalse);
        await show(recipient);
        await t.tap(find.byTooltip('التنبيهات'));
        await waitFor(find.byType(NotificationInbox));
        final text = find.text(textOf(notice['message']));
        await locate(text);
        final card = find
            .ancestor(of: text, matching: find.byType(GCard))
            .first;
        final read = find.descendant(
          of: card,
          matching: find.text('تمت القراءة'),
        );
        if (notice['isRead'] == false) {
          await t.ensureVisible(read);
          await t.tap(read);
          await t.pump(const Duration(milliseconds: 500));
        }
        expect(
          rows(
            obj(
              await recipient.request('GET', '/api/v1/notifications'),
            )['items'],
          ).singleWhere((n) => n['id'] == notice['id'])['isRead'],
          isTrue,
        );
        await capture(
          'phase-approved-match-${recipient.session!.selected!.role == 'Coach' ? 'coach' : 'guardian'}-inbox',
        );
        final open = find.descendant(
          of: card,
          matching: find.text('عرض المباراة'),
        );
        await t.ensureVisible(open);
        await t.tap(open);
        await waitFor(find.byType(MatchSchedule));
        final target = find.byWidgetPredicate(
          (w) => w is MatchResultCard && w.data['id'] == id,
        );
        await locate(target);
        await capture(
          'phase-approved-match-${recipient.session!.selected!.role == 'Coach' ? 'coach' : 'guardian'}-published',
        );
        expect(t.widget<MatchSchedule>(find.byType(MatchSchedule)).matchId, id);
      }
      expect(
        rows(await foreign.request('GET', '/api/v1/my-matches'))
            .any((m) => m['id'] == id),
        isFalse,
      );
      expect(
        rows(
          obj(await foreign.request('GET', '/api/v1/notifications'))['items'],
        ).any((n) => n['matchId'] == id),
        isFalse,
      );
      await manageAdmin();
      final edit = find.byKey(ValueKey('match-edit-$id'));
      await locate(edit);
      await t.tap(edit);
      await waitFor(find.byType(NativeMatchEditor));
      await date(today);
      await field('نتيجة فريقنا', '3');
      await field('نتيجة المنافس', '1');
      await tap('حفظ المباراة');
      await waitFor(find.byType(NativeMatchManagement));
      saved = obj(await owner.request('GET', '/api/v1/manage/matches/$id'));
      expect(saved['status'], 'Completed');
      expect(saved['ourScore'], 3);
      expect(saved['opponentScore'], 1);
      for (final recipient in [owner, coach, guardian]) {
        await matches(recipient);
        final target = find.byWidgetPredicate(
          (w) => w is MatchResultCard && w.data['id'] == id,
        );
        await locate(target);
        expect(
          find.descendant(of: target, matching: find.text('فوز')),
          findsOneWidget,
        );
        expect(t.widget<MatchResultCard>(target).data['ourScore'], 3);
        await capture(
          'phase-approved-match-${recipient.session!.selected!.role == 'AcademyOwner'
              ? 'owner'
              : recipient.session!.selected!.role == 'Coach'
              ? 'coach'
              : 'guardian'}-result',
        );
        if (recipient == owner) continue;
        final notices = rows(
          obj(await recipient.request('GET', '/api/v1/notifications'))['items'],
        ).where((n) => n['matchId'] == id).toList();
        expect(notices, hasLength(2));
        expect(notices.where((n) => n['isRead'] == false), hasLength(1));
      }
      await expectLater(
        denied.request('GET', '/api/v1/manage/matches/options'),
        throwsA(isA<ApiFailure>().having((e) => e.status, 'status', 403)),
      );
      await show(denied);
      await t.tap(
        find.descendant(
          of: find.byType(NavigationBar),
          matching: find.text('المزيد'),
        ),
      );
      await waitFor(find.byType(NativeMore));
      await t.pumpAndSettle();
      expect(find.text('إدارة المباريات والنتائج'), findsNothing);
      await capture('phase-approved-match-denied-management');
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
      for (final auth in [owner, admin, denied, coach, guardian, foreign]) {
        auth.dispose();
      }
    },
  );
}
