import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/core/theme/approved_role_introduction.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_action_rail.dart';
import 'package:academy_mobile/features/owner/owner_quick_operations.dart';
import 'package:academy_mobile/features/coach/coach_home.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'approved_role_navigation_palette_test.dart' show modelTheme;
import 'v2_role_workspaces_test.dart' as f;

Widget frame(Widget home, VisualModel m, {double scale = 2}) => MaterialApp(
  locale: const Locale('ar'),
  supportedLocales: const [Locale('ar')],
  localizationsDelegates: GlobalMaterialLocalizations.delegates,
  theme: modelTheme(m),
  builder: (context, child) => MediaQuery(
    data: MediaQuery.of(context).copyWith(textScaler: TextScaler.linear(scale)),
    child: child!,
  ),
  home: home,
);
void main() {
  for (final model in VisualModel.values) {
    testWidgets(
      'V5.14 stable actions expose every command once at large text $model',
      (t) async {
        t.view.physicalSize = const Size(375, 812);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.reset);
        var activated = -1;
        await t.pumpWidget(
          frame(
            Scaffold(
              body: SingleChildScrollView(
                child: Padding(
                  padding: const EdgeInsets.all(20),
                  child: OwnerActionRail(
                    automatic: true,
                    actions: [
                      for (var i = 0; i < 8; i++)
                        OwnerRailAction(
                          'إجراء $i',
                          Icons.bolt,
                          Colors.red,
                          () => activated = i,
                        ),
                    ],
                  ),
                ),
              ),
            ),
            model,
          ),
        );
        await t.pumpAndSettle();
        final initial = t.getTopLeft(find.text('إجراء 0'));
        await t.pump(const Duration(seconds: 10));
        expect(t.getTopLeft(find.text('إجراء 0')), initial);
        expect(activated, -1);
        expect(find.text('إجراء 7'), findsNothing);
        for (var i = 0; i < 8; i++) {
          await t.scrollUntilVisible(
            find.text('إجراء $i'),
            160,
            scrollable: find.descendant(
              of: find.byKey(const ValueKey('owner-shortcut-scroll')),
              matching: find.byType(Scrollable),
            ),
          );
          await Scrollable.ensureVisible(
            t.element(find.text('إجراء $i')),
            alignment: .5,
          );
          await t.pumpAndSettle();
          await t.tap(find.text('إجراء $i'));
          expect(activated, i);
        }
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
      },
    );
    for (final role in ['AcademyOwner', 'AcademyAdmin', 'Coach', 'Guardian']) {
      testWidgets(
        'V5.14 onboarding remains scrollable and completes once: $role $model',
        (t) async {
          t.view.physicalSize = const Size(375, 667);
          t.view.devicePixelRatio = 1;
          addTearDown(t.view.reset);
          var complete = 0;
          await t.pumpWidget(
            frame(
              ApprovedRoleIntroduction(
                role: role,
                sportTheme: 'football',
                training: true,
                academyName: 'أكاديمية العرض الخيالية',
                crest: const Icon(Icons.sports_soccer),
                onComplete: () async {
                  complete++;
                },
              ),
              model,
            ),
          );
          for (var i = 0; i < 3; i++) {
            await t.tap(find.byKey(const ValueKey('role-intro-next')));
            await t.pumpAndSettle();
          }
          expect(complete, 1);
          expect(t.takeException(), isNull);
          await t.pumpWidget(const SizedBox());
        },
      );
    }
    testWidgets(
      'V5.14 session and facts remain readable without overflow $model',
      (t) async {
        t.view.physicalSize = const Size(375, 812);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.reset);
        var opened = 0;
        await t.pumpWidget(
          frame(
            Scaffold(
              body: gList([
                CoachSessionCard(
                  featured: true,
                  session: const {
                    'id': 'session',
                    'group': 'مجموعة كرة القدم',
                    'sessionDate': '2026-10-29',
                    'startTime': '16:00:00',
                    'endTime': '17:00:00',
                    'branch': 'فرع العرض',
                    'sport': 'كرة القدم',
                    'playerCount': 12,
                  },
                  onOpen: () => opened++,
                ),
                const Facts([('العمر', '13'), ('الطول · سم', '150')]),
              ]),
            ),
            model,
          ),
        );
        await t.scrollUntilVisible(
          find.text('عرض المجموعة ولاعبيها'),
          180,
          scrollable: find.byType(Scrollable).first,
        );
        await t.pumpAndSettle();
        await t.tap(find.text('عرض المجموعة ولاعبيها'));
        expect(opened, 1);
        await t.scrollUntilVisible(
          find.text('الطول · سم'),
          180,
          scrollable: find.byType(Scrollable).first,
        );
        expect(t.takeException(), isNull);
        final muted = modelTheme(model).colorScheme.onSurfaceVariant;
        final surface = modelTheme(model).colorScheme.surface;
        final l = [muted.computeLuminance(), surface.computeLuminance()]
          ..sort();
        expect((l.last + .05) / (l.first + .05), greaterThan(4.5));
        await t.pumpWidget(const SizedBox());
      },
    );
  }
  testWidgets(
    'V5.14 operation search filters visible permitted actions and restores the directory',
    (t) async {
      final auth = await f.authenticate(
        'AcademyOwner',
        (m, p, b, k) async => [],
      );
      await t.pumpWidget(f.app(OwnerQuickOperations(auth: auth)));
      await t.pumpAndSettle();
      await t.enterText(
        find.byKey(const ValueKey('operation-search')),
        'تجديد',
      );
      await t.pumpAndSettle();
      expect(find.text('تجديد اشتراك'), findsOneWidget);
      expect(find.text('إضافة فرع'), findsNothing);
      await t.enterText(find.byKey(const ValueKey('operation-search')), '');
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('إضافة فرع'),
        200,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.text('إضافة فرع'), findsOneWidget);
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('إضافة فرع'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
