import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/shared/role_agenda.dart';
import 'package:academy_mobile/features/shared/matches.dart';
import 'package:academy_mobile/features/guardian/family_child_filter.dart';
import 'package:academy_mobile/features/guardian/guardian_profile.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'role_agenda_test.dart' as f;
import 'v2_role_workspaces_test.dart' show authenticate;

void main() {
  for (final light in [true, false]) {
    for (final width in [375.0, 440.0]) {
      testWidgets(
        'approved guardian days, horizontal child tabs and unambiguous times fit $width light=$light at 200%',
        (t) async {
          t.view.physicalSize = Size(width, 1300);
          t.view.devicePixelRatio = 1;
          addTearDown(t.view.resetPhysicalSize);
          addTearDown(t.view.resetDevicePixelRatio);
          final calls = <String>[];
          final auth = await authenticate('Guardian', (m, p, b, token) async {
            calls.add('$m $p');
            if (p == '/api/v1/guardian/home') return f.family;
            if (p.endsWith('/profile')) {
              return f.profile(p.contains('/b/') ? 'b' : 'a');
            }
            return [f.game(p.endsWith('=b') ? 'b' : 'a')];
          });
          await t.pumpWidget(
            MaterialApp(
              locale: const Locale('ar'),
              supportedLocales: const [Locale('ar')],
              localizationsDelegates: GlobalMaterialLocalizations.delegates,
              theme: light ? AcademyTheme.light() : AcademyTheme.dark(),
              builder: (c, w) => MediaQuery(
                data: MediaQuery.of(c)
                    .copyWith(textScaler: TextScaler.linear(2)),
                child: w!,
              ),
              home: RoleAgenda(auth: auth, guardian: true),
            ),
          );
          await t.pumpAndSettle();
          expect(find.byType(DropdownButtonFormField<String>), findsNothing);
          expect(find.byKey(const ValueKey('family-filter-a')), findsOneWidget);
          expect(find.byKey(const ValueKey('family-filter-b')), findsOneWidget);
          final date = find.byKey(const ValueKey('agenda-date-2026-10-21'));
          expect(
            find.descendant(
              of: date,
              matching: find.textContaining('الأربعاء'),
            ),
            findsOneWidget,
          );
          final card = find.byKey(const ValueKey('guardian-agenda-session-a'));
          expect(card, findsOneWidget);
          expect(
            t
                .widget<Text>(
                  find.descendant(of: card, matching: find.text('5:00 مساءً')),
                )
                .textDirection,
            TextDirection.ltr,
          );
          expect(
            find.descendant(of: card, matching: find.text('ابن أول اصطناعي')),
            findsOneWidget,
          );
          expect(
            find.descendant(of: card, matching: find.text('حاضر')),
            findsNothing,
          );
          await t.drag(
            find.byKey(const ValueKey('family-child-filter')),
            const Offset(600, 0),
          );
          await t.pumpAndSettle();
          await t.ensureVisible(find.byKey(const ValueKey('family-filter-b')));
          await t.pumpAndSettle();
          await t.tap(find.byKey(const ValueKey('family-filter-b')));
          await t.pumpAndSettle();
          expect(find.text('مجموعة a'), findsNothing);
          expect(find.text('مجموعة b'), findsOneWidget);
          expect(
            calls,
            contains('GET /api/v1/my-matches?upcomingOnly=true&playerId=b'),
          );
          expect(calls.every((p) => p.startsWith('GET ')), isTrue);
          expect(t.takeException(), isNull);
          await t.pumpWidget(const SizedBox());
          auth.dispose();
        },
      );
    }
  }
  testWidgets(
    'the shared match child tabs keep all and per-child queries distinct',
    (t) async {
      final calls = <String>[];
      final auth = await authenticate('Guardian', (m, p, b, token) async {
        calls.add(p);
        if (p == '/api/v1/guardian/home') return f.family;
        if (p.startsWith('/api/v1/my-matches')) {
          return [
            f.game(
              p.endsWith('=b') ? 'b' : 'a',
              opponent: p.endsWith('=b')
                  ? 'منافس الطفل الثاني'
                  : 'منافس الطفل الأول',
            ),
          ];
        }
        return [];
      });
      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('ar'),
          supportedLocales: const [Locale('ar')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: AcademyTheme.light(),
          home: MatchSchedule(auth: auth),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('منافس الطفل الأول'), findsOneWidget);
      await t.tap(find.byKey(const ValueKey('family-filter-b')));
      await t.pumpAndSettle();
      expect(find.text('منافس الطفل الأول'), findsNothing);
      expect(find.text('منافس الطفل الثاني'), findsOneWidget);
      expect(calls, contains('/api/v1/my-matches?playerId=b'));
      await t.tap(find.byKey(const ValueKey('family-filter-')));
      await t.pumpAndSettle();
      expect(calls.last, '/api/v1/my-matches');
      expect(
        t.widget<FamilyChildFilter>(find.byType(FamilyChildFilter)).selected,
        '',
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'child tabs expose selection and at least a44 tap area, and loading disables changes',
    (t) async {
      var chosen = '';
      await t.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: FamilyChildFilter(
              children: f.family['children']! as List<Map<String, dynamic>>,
              selected: 'a',
              onSelected: (id) => chosen = id,
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      final a = find.byKey(const ValueKey('family-filter-a'));
      expect(t.getSize(a).height, greaterThanOrEqualTo(44));
      expect(t.widget<Semantics>(a).properties.selected, true);
      await t.tap(find.byKey(const ValueKey('family-filter-b')));
      expect(chosen, 'b');
      chosen = '';
      await t.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: FamilyChildFilter(
              children: f.family['children']! as List<Map<String, dynamic>>,
              selected: 'a',
              enabled: false,
              onSelected: (id) => chosen = id,
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      await t.tap(find.byKey(const ValueKey('family-filter-b')));
      expect(chosen, '');
    },
  );
  for (final light in [true, false]) {
    for (final width in [375.0, 440.0]) {
      testWidgets(
        'profile schedule clocks retain Arabic start/end RTL without seconds, light=$light width=$width',
        (t) async {
          t.view.physicalSize = Size(width, 1300);
          t.view.devicePixelRatio = 1;
          addTearDown(t.view.resetPhysicalSize);
          addTearDown(t.view.resetDevicePixelRatio);
          final profile = f.profile('a');
          final enrollment =
              (profile['enrollments'] as List).single as Map<String, dynamic>;
          enrollment['sessionMoves'] = [];
          enrollment['recurring'] = [
            {
              'dayOfWeek': 'Wednesday',
              'startTime': '19:00:00',
              'endTime': '20:00:00',
            },
          ];
          final auth = await authenticate(
            'Guardian',
            (m, p, b, token) async => profile,
          );
          await t.pumpWidget(
            MaterialApp(
              locale: const Locale('ar'),
              supportedLocales: const [Locale('ar')],
              localizationsDelegates: GlobalMaterialLocalizations.delegates,
              theme: light ? AcademyTheme.light() : AcademyTheme.dark(),
              builder: (c, w) => MediaQuery(
                data: MediaQuery.of(c)
                    .copyWith(textScaler: TextScaler.linear(2)),
                child: w!,
              ),
              home: ProfileRecords(
                auth: auth,
                playerId: 'a',
                section: 'schedule',
              ),
            ),
          );
          await t.pumpAndSettle();
          for (final time in [
            'من 7:00 مساءً إلى 8:00 مساءً',
            'من 5:00 مساءً إلى 6:00 مساءً',
          ]) {
            await t.scrollUntilVisible(
              find.text(time),
              150,
              scrollable: find.byType(Scrollable).first,
            );
            expect(find.text(time), findsOneWidget);
            expect(
              t.widget<Text>(find.text(time)).textDirection ??
                  Directionality.of(t.element(find.text(time))),
              TextDirection.rtl,
            );
          }
          expect(t.takeException(), isNull);
          await t.pumpWidget(const SizedBox());
          auth.dispose();
        },
      );
    }
  }
}
