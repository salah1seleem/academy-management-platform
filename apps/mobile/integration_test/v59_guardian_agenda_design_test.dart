import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/family_child_filter.dart';
import 'package:academy_mobile/features/guardian/guardian_profile.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/matches.dart';
import 'package:academy_mobile/features/shared/experience_ui.dart';
import 'package:academy_mobile/features/shared/role_agenda.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'v57_role_footer_design_test.dart' show FooterReadTransport;

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'real family agenda keeps all-child and child scope, dates and source navigation in both appearances',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      const tag = String.fromEnvironment(
        'AGENDA_VIEWPORT_TAG',
        defaultValue: 'regular',
      );
      const expectedWidth = int.fromEnvironment(
        'AGENDA_EXPECTED_WIDTH',
        defaultValue: 440,
      );
      expect(
        t.view.physicalSize.width / t.view.devicePixelRatio,
        expectedWidth.toDouble(),
      );
      final transport = FooterReadTransport(AppConfig('Demo', base));
      final auth = AuthController(
        transport,
        SecureRefreshStore('v45.5278.os-media-entry.01099900101'),
      );
      await auth.restore();
      if (auth.session == null) {
        final challenge = await auth.requestOtp('01099900101');
        await auth.verify('01099900101', challenge, '246810');
      }
      expect(auth.session!.selected!.role, 'Guardian');
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      final home = obj(await auth.request('GET', '/api/v1/guardian/home'));
      final children = rows(home['children']);
      expect(children.length, greaterThanOrEqualTo(2));
      final sources = <String, Json>{};
      final sessions = <String, Json>{};
      for (final child in children) {
        final id = textOf(child['id']);
        final profile = obj(
          await auth.request('GET', '/api/v1/guardian/children/$id/profile'),
        );
        sources[id] = profile;
        for (final enrollment in rows(profile['enrollments'])) {
          for (final session in rows(enrollment['upcoming'])) {
            sessions[textOf(session['id'])] = {
              'id': session['id'],
              'group': session['group'] ?? enrollment['group'],
              'date': day(session['sessionDate']),
              'startTime': session['startTime'],
              'endTime': session['endTime'],
            };
          }
        }
      }
      final ordered = sessions.values.toList()
        ..sort(
          (a, b) => '${a['date']} ${a['startTime']}'.compareTo(
            '${b['date']} ${b['startTime']}',
          ),
        );
      expect(ordered, isNotEmpty);
      final games =
          rows(
                await auth.request(
                  'GET',
                  '/api/v1/my-matches?upcomingOnly=true',
                ),
              )
              .where(
                (m) => m['isUpcoming'] == true && m['status'] != 'Cancelled',
              )
              .toList()
            ..sort(
              (a, b) => '${a['matchDate']} ${a['startTime']}'.compareTo(
                '${b['matchDate']} ${b['startTime']}',
              ),
            );
      final appearance = Appearance();
      await appearance.completeRoleIntro('Guardian', 'football');
      await appearance.completeRoleIntro('Guardian', 'general');
      Future<void> wait(Finder finder) async {
        for (var i = 0; i < 200 && finder.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(finder, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> tap(Finder finder) async {
        await wait(finder);
        await t.ensureVisible(finder);
        await t.pumpAndSettle();
        await t.tap(finder);
        await t.pumpAndSettle();
      }

      Future<void> chooseChild(String id) async {
        final target = find.byKey(ValueKey('family-filter-$id'));
        await t.scrollUntilVisible(
          target,
          -160,
          scrollable: find.byType(Scrollable).first,
          maxScrolls: 30,
        );
        await tap(target);
      }

      for (final light in [true, false]) {
        await t.pumpWidget(const SizedBox());
        await t.pumpAndSettle();
        await appearance.update(night: !light);
        await t.pumpWidget(
          AcademyApp(
            key: ValueKey(light),
            auth: auth,
            environment: 'Demo',
            appearance: appearance,
          ),
        );
        await wait(find.text('أبنائي'));
        await tap(
          find.descendant(
            of: find.byType(NavigationBar),
            matching: find.text('الحصص'),
          ),
        );
        await wait(find.byType(RoleAgenda));
        await wait(find.byType(FamilyChildFilter));
        await chooseChild('');
        expect(
          t.widget<FamilyChildFilter>(find.byType(FamilyChildFilter)).selected,
          '',
        );
        final first = find.byKey(
          ValueKey('guardian-agenda-${ordered.first['id']}'),
        );
        await t.scrollUntilVisible(
          first,
          160,
          scrollable: find.byType(Scrollable).first,
          maxScrolls: 30,
        );
        await wait(first);
        await t.ensureVisible(first);
        await t.pumpAndSettle();
        final card = t.widget<GuardianAgendaCard>(first);
        expect(card.isMatch, false);
        expect(card.data['group'], ordered.first['group']);
        expect(card.childNames, isNotEmpty);
        expect(
          find.byKey(ValueKey('agenda-date-${ordered.first['date']}')),
          findsOneWidget,
        );

        await binding.takeScreenshot(
          'phase-approved-guardian-agenda-$tag-${light ? 'light' : 'dark'}',
        );
        await tap(first);
        await wait(find.text('التدريب والمواعيد'));
        await wait(find.byType(ProfileRecords));
        final profile = t.widget<ProfileRecords>(find.byType(ProfileRecords));
        expect(sources.containsKey(profile.playerId), true);
        expect(profile.section, 'schedule');
        final sport = textOf(
          rows(sources[profile.playerId]!['enrollments']).first['sport'],
        );
        await wait(find.text(sport));
        final range =
            '${trainingTimeOf(ordered.first['startTime'])} — ${trainingTimeOf(ordered.first['endTime'])}';
        await wait(find.text(range));
        expect(
          t.widget<Text>(find.text(range).first).textDirection,
          TextDirection.ltr,
        );
        await binding.takeScreenshot(
          'phase-approved-guardian-session-source-$tag-${light ? 'light' : 'dark'}',
        );
        await tap(find.byType(BackButton));
        await wait(find.byType(FamilyChildFilter));
        final secondId = textOf(children[1]['id']);
        await chooseChild(secondId);
        await wait(find.byType(FamilyChildFilter));
        expect(
          t.widget<FamilyChildFilter>(find.byType(FamilyChildFilter)).selected,
          secondId,
        );
        await tap(find.widgetWithText(ChoiceChip, 'المباريات'));
        expect(find.byType(NavigationBar), findsOneWidget);
        expect(find.byType(ExperienceHeader), findsOneWidget);
        expect(find.byType(AppBar), findsNothing);
        await binding.takeScreenshot(
          'phase-approved-guardian-child-matches-$tag-${light ? 'light' : 'dark'}',
        );
        const nativeHold = int.fromEnvironment('NATIVE_CAPTURE_HOLD');
        if (light && nativeHold > 0) {
          debugPrint('native-agenda-capture-ready');
          await Future<void>.delayed(Duration(seconds: nativeHold));
        }
        await chooseChild('');
        if (games.isNotEmpty) {
          final match = find.byKey(
            ValueKey('guardian-agenda-${games.first['id']}'),
          );
          await wait(match);
          await tap(match);
          await wait(find.byType(MatchSchedule));
          expect(
            t.widget<MatchSchedule>(find.byType(MatchSchedule)).matchId,
            games.first['id'],
          );
          await wait(find.text(textOf(games.first['opponent'])));
          await binding.takeScreenshot(
            'phase-approved-guardian-match-source-$tag-${light ? 'light' : 'dark'}',
          );
          await tap(find.byType(BackButton));
        } else {
          await wait(find.text('لا توجد مواعيد قادمة في القائمة المعروضة.'));
        }
        expect(t.takeException(), isNull);
      }
      expect(transport.writes, isEmpty);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
      appearance.dispose();
    },
  );
}
