import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/matches.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'v57_role_footer_design_test.dart' show FooterReadTransport;

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'coach and guardian nearest strip opens the time-eligible source without business writes',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 200 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      for (final sample in [
        ('Coach', '01099900020', 'coach'),
        ('Guardian', '01099900101', 'guardian'),
      ]) {
        final transport = FooterReadTransport(AppConfig('Demo', base));
        final auth = AuthController(
          transport,
          SecureRefreshStore('v45.5278.os-media-entry.${sample.$2}'),
        );
        await auth.restore();
        if (auth.session == null) {
          final c = await auth.requestOtp(sample.$2);
          await auth.verify(sample.$2, c, '246810');
        }
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        expect(auth.session!.selected!.role, sample.$1);
        String? player;
        if (sample.$1 == 'Guardian') {
          final home = obj(await auth.request('GET', '/api/v1/guardian/home'));
          player = textOf(rows(home['children']).first['id']);
        }
        final items =
            rows(
              await auth.request(
                'GET',
                '/api/v1/my-matches?upcomingOnly=true${player == null ? '' : '&playerId=$player'}',
              ),
            )..sort(
              (a, b) => '${a['matchDate']} ${a['startTime']}'.compareTo(
                '${b['matchDate']} ${b['startTime']}',
              ),
            );
        expect(items, isNotEmpty);
        expect(items.first['scheduleWindow'], 'Upcoming');
        final appearance = Appearance();
        for (final theme in ['football', 'general']) {
          await appearance.completeRoleIntro(sample.$1, theme);
        }
        await t.pumpWidget(
          AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
        );
        await wait(
          find.text(sample.$1 == 'Guardian' ? 'أبنائي' : 'المجموعات المسندة'),
        );
        await t.scrollUntilVisible(
          find.byType(NextMatchStrip),
          200,
          scrollable: find.byType(Scrollable).first,
          maxScrolls: 60,
        );
        final strip = find.byType(NextMatchStrip);
        expect(t.widget<NextMatchStrip>(strip).playerId, player);
        final title = '${items.first['team']} × ${items.first['opponent']}';
        await wait(find.descendant(of: strip, matching: find.text(title)));
        await t.ensureVisible(strip);
        await t.pumpAndSettle();
        await binding.takeScreenshot(
          'phase-approved-${sample.$3}-nearest-window',
        );
        await t.tap(find.descendant(of: strip, matching: find.text(title)));
        await wait(find.byType(MatchSchedule));
        expect(
          t.widget<MatchSchedule>(find.byType(MatchSchedule)).matchId,
          items.first['id'],
        );
        expect(
          t.widget<MatchSchedule>(find.byType(MatchSchedule)).playerId,
          player,
        );
        await wait(find.text(textOf(items.first['opponent'])));
        await binding.takeScreenshot(
          'phase-approved-${sample.$3}-nearest-window-source',
        );
        expect(transport.writes, isEmpty);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
        appearance.dispose();
      }
    },
  );
}
