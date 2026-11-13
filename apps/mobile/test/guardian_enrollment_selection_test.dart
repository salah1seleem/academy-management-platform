import 'package:academy_mobile/features/guardian/guardian_profile.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/shared/subscription_home.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'guardian_home_workspace_test.dart' as workspace;
import 'guardian_test.dart' show logged;
import 'subscription_home_panel_test.dart' as subscriptions;
import 'v2_role_workspaces_test.dart' as roles;

void main() {
  testWidgets(
    'sport selection scopes subscription and published assessment, and survives child switching',
    (t) async {
      Future<void> reveal(Finder target, {bool up = false}) async {
        await t.scrollUntilVisible(
          target,
          up ? -180 : 180,
          scrollable: find
              .descendant(
                of: find.byKey(const ValueKey('guardian-home-scroll')),
                matching: find.byType(Scrollable),
              )
              .first,
        );
        await t.pumpAndSettle();
      }

      final home = workspace.home();
      final first = (home['children'] as List).first as Map<String, dynamic>;
      first['sports'] = [
        {
          ...(first['sports'] as List).first as Map<String, dynamic>,
          'id': 'swim-a',
          'sport': 'السباحة',
          'branch': 'فرع الموج',
          'performance': {
            'evaluationId': 'swim-report',
            'meanPerformance': 80,
            'overallScore': 99,
          },
        },
        {
          ...(first['sports'] as List).first as Map<String, dynamic>,
          'id': 'foot-a',
          'sport': 'كرة القدم',
          'branch': 'فرع الكرة',
          'performance': {
            'evaluationId': 'foot-report',
            'meanPerformance': 64,
            'overallScore': 91,
          },
        },
      ];
      final auth = await logged(
        (m, p, b, k) async =>
            p == '/api/v1/guardian/home' ? home : workspace.response(p),
      );
      await t.pumpWidget(workspace.app(auth, dark: false));
      await t.pumpAndSettle();
      await t.tap(find.byType(DropdownButtonFormField<String>).first);
      await t.pumpAndSettle();
      await t.tap(find.textContaining('فرع الكرة').last);
      await t.pumpAndSettle();
      final input = t.widget<DropdownButtonFormField<String>>(
        find.byType(DropdownButtonFormField<String>).first,
      );
      expect(input.decoration.fillColor, isNull);
      expect(
        Theme.of(t.element(find.byType(DropdownButtonFormField<String>).first))
            .brightness,
        Brightness.light,
      );
      final card = find.byKey(const ValueKey('child-a'));
      Finder inCard(Finder target) =>
          find.descendant(of: card, matching: target);
      expect(inCard(find.text('64')), findsOneWidget);
      expect(inCard(find.text('91')), findsNothing);
      await reveal(find.byKey(const ValueKey('child-tab-b')), up: true);
      await t.tap(find.byKey(const ValueKey('child-tab-b')));
      await t.pumpAndSettle();
      await t.tap(find.byKey(const ValueKey('child-tab-a')));
      await t.pumpAndSettle();
      expect(inCard(find.text('64')), findsOneWidget);
      final profileLink = inCard(find.text('ملف لينا والتقرير'));
      await t.ensureVisible(profileLink);
      await t.pumpAndSettle();
      await t.tap(profileLink);
      await t.pumpAndSettle();
      final profile = t.widget<ChildProfile>(find.byType(ChildProfile));
      expect(profile.playerId, 'a');
      expect(profile.initialEnrollmentId, 'foot-a');
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'subscription total label remains readable on its light identity surface',
    (t) async {
      final auth = await roles.authenticate(
        'AcademyOwner',
        (m, p, b, k) async => subscriptions.home(),
      );
      await t.pumpWidget(
        roles.app(
          Scaffold(
            body: SingleChildScrollView(
              child: SubscriptionHomePanel(auth: auth),
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      final text = t.widget<Text>(find.text('إجمالي الاشتراكات السارية'));
      final color = text.style!.color!;
      final contrast =
          (AcademyTheme.light().colorScheme.surface.computeLuminance() + .05) /
          (color.computeLuminance() + .05);
      expect(contrast, greaterThan(4.5));
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
