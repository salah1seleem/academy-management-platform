import 'package:academy_mobile/features/guardian/guardian_home.dart';
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
      await t.tap(find.text('كرة القدم · فرع الكرة').last);
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
      final details = find.byKey(const ValueKey('child-home-details'));
      await reveal(details);
      await t.tap(details);
      await t.pumpAndSettle();
      await reveal(find.byType(GuardianSubscriptionSummary));
      var summary = t.widget<GuardianSubscriptionSummary>(
        find.byType(GuardianSubscriptionSummary),
      );
      expect((summary.childData['sports'] as List).single['id'], 'foot-a');
      expect(
        find.descendant(
          of: find.byType(GuardianSubscriptionSummary),
          matching: find.text('الاشتراك · السباحة'),
        ),
        findsNothing,
      );
      await reveal(find.byKey(const ValueKey('child-tab-b')), up: true);
      await t.tap(find.byKey(const ValueKey('child-tab-b')));
      await t.pumpAndSettle();
      await t.tap(find.byKey(const ValueKey('child-tab-a')));
      await t.pumpAndSettle();
      await reveal(find.byType(GuardianSubscriptionSummary));
      summary = t.widget<GuardianSubscriptionSummary>(
        find.byType(GuardianSubscriptionSummary),
      );
      expect((summary.childData['sports'] as List).single['id'], 'foot-a');
      await reveal(find.byKey(const ValueKey('child-section-2')), up: true);
      await t.tap(find.byKey(const ValueKey('child-section-2')));
      await t.pumpAndSettle();
      await reveal(find.byType(GuardianLatestProgress));
      final progress = t.widget<GuardianLatestProgress>(
        find.byType(GuardianLatestProgress),
      );
      expect(progress.childData['latestEvaluation']['id'], 'foot-report');
      expect(
        find.descendant(
          of: find.byType(GuardianLatestProgress),
          matching: find.text('64'),
        ),
        findsOneWidget,
      );
      expect(
        find.descendant(
          of: find.byType(GuardianLatestProgress),
          matching: find.text('91'),
        ),
        findsNothing,
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'subscription helper remains readable on its light identity surface',
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
      final text = t.widget<Text>(find.text('قرب الانتهاء ضمن الفعالة'));
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
