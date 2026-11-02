import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/role_agenda.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Guardian and coach open their combined schedule from bottom navigation against the existing API',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5222',
      ); // Never the manual Demo on5080.
      final transport = HttpApiTransport(
        AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
      );
      final auth = AuthController(
        transport,
        SecureRefreshStore('academy.v3.role-agenda.journey'),
      );
      Future<void> login(String phone) async {
        if (phone == '01099900010' &&
            auth.session?.selected?.role == 'AcademyOwner') {
          return;
        }
        if (auth.session != null) await auth.logout();
        final challenge = await auth.requestOtp(phone);
        await auth.verify(phone, challenge, '246810');
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
      }

      await auth.restore();
      await login('01099900010'); // Only the guarded synthetic Football tenant.
      final before = obj(
        await auth.request('GET', '/api/v1/reports/owner-summary'),
      );
      final options = obj(
        await auth.request('GET', '/api/v1/manage/matches/options'),
      );
      final team = rows(options['teams'])
          .firstWhere((x) => textOf(x['name']).contains('منتخب 2018'));
      final staff = obj(await auth.request('GET', '/api/v1/manage/staff/'));
      final member = rows(staff['members'])
          .firstWhere((x) => x['email'] == 'coach1.football@example.test');
      final coaches = rows(
        await auth.request('GET', '/api/v1/manage/structure/coaches'),
      );
      final coach = coaches.firstWhere((x) => x['id'] == member['id']);
      final participant = rows(options['players']).firstWhere(
        (p) =>
            (team['enrollmentIds'] as List).contains(p['id']) &&
            p['trainingGroupId'] != null,
      );
      final group = rows(coach['groups'])
          .firstWhere((g) => g['id'] == participant['trainingGroupId']);
      final date = DateTime.now()
          .toUtc()
          .add(const Duration(days: 2))
          .toIso8601String()
          .split('T')
          .first;
      final existingSessions = rows(
        await auth.request(
          'GET',
          '/api/v1/attendance/sessions?from=$date&to=$date',
        ),
      );
      if (!existingSessions.any(
        (s) =>
            s['trainingGroupId'] == group['id'] && s['startTime'] == '16:00:00',
      )) {
        await auth.request(
          'POST',
          '/api/v1/attendance/sessions',
          body: {
            'trainingGroupId': group['id'],
            'sessionDate': date,
            'startTime': '16:00:00',
            'endTime': '16:30:00',
            'notes': 'حصة تقويم اصطناعية معزولة',
          },
        );
      }
      final games = rows(await auth.request('GET', '/api/v1/manage/matches/'));
      const opponent = 'منافس تقويم اصطناعي';
      if (!games.any(
        (m) => m['opponent'] == opponent && m['matchDate'] == date,
      )) {
        final roster = obj(
          await auth.request(
            'GET',
            '/api/v1/manage/matches/teams/${team['id']}/roster',
          ),
        );
        final tournament = rows(options['tournaments'])
            .firstWhere((x) => x['sportId'] == team['sportId']);
        await auth.request(
          'POST',
          '/api/v1/manage/matches/',
          body: {
            'tournamentId': tournament['id'],
            'teamId': team['id'],
            'opponent': opponent,
            'matchDate': date,
            'startTime': '18:00:00',
            'venue': 'ملعب التقويم الاصطناعي',
            'benchPlayerIds': [],
            'publish': true,
            'isActive': true,
            'rosterQuoteHash': roster['quoteHash'],
            'coachMembershipId': coach['id'],
            'coachAssignmentVersion': 0,
          },
        );
      }
      final after = obj(
        await auth.request('GET', '/api/v1/reports/owner-summary'),
      );
      expect(after['collections'], before['collections']);
      expect(after['paymentRequests'], before['paymentRequests']);
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 180 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> show(String role) async {
        await t.pumpWidget(
          MaterialApp(
            debugShowCheckedModeBanner: false,
            locale: const Locale('ar'),
            supportedLocales: const [Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            theme: guardianTheme(),
            home: NativeRoleTabs(
              key: ValueKey(role),
              auth: auth,
              role: role,
              home: const SizedBox(),
              account: const SizedBox(),
            ),
          ),
        );
        await t.pumpAndSettle();
        await t.tap(find.text(role == 'Guardian' ? 'التقويم' : 'جدولي'));
        await wait(find.byType(RoleAgenda));
        await wait(find.text('منتخب 2018 — عرض تجريبي × $opponent'));
      }

      await login('01099900101');
      await show('Guardian');
      await t.scrollUntilVisible(
        find.text(textOf(group['name'])),
        160,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.text(textOf(group['name'])), findsOneWidget);
      await t.scrollUntilVisible(
        find.text('منتخب 2018 — عرض تجريبي × $opponent'),
        160,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.text('مدرب المباراة: ${coach['arabicName']}'), findsWidgets);
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await binding.takeScreenshot('phase-agenda-guardian');
      }
      await t.pumpWidget(const SizedBox());
      await login('01099900020');
      await show('Coach');
      await t.scrollUntilVisible(
        find.text(textOf(group['name'])),
        160,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.text(textOf(group['name'])), findsOneWidget);
      await t.scrollUntilVisible(
        find.text('منتخب 2018 — عرض تجريبي × $opponent'),
        160,
        scrollable: find.byType(Scrollable).first,
      );
      if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
        await binding.takeScreenshot('phase-agenda-coach');
      }
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('منتخب 2018 — عرض تجريبي × $opponent'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
      transport.close();
    },
  );
}
