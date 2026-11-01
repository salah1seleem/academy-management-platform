import 'dart:async';

import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:academy_mobile/features/owner/refined_home_panels.dart';
import 'package:academy_mobile/features/coach/coach_home.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' show owner, summary;
import 'v2_role_workspaces_test.dart' show app;
import 'coach_test.dart' show coach;
import 'coach_workspace_test.dart' show homeResponse;
import 'subscription_home_panel_test.dart' show home;

void main() {
  testWidgets(
    'available Owner collections render while its operating statement is still pending',
    (t) async {
      final pending = Completer<Object?>();
      final auth = await owner((m, p, b, k) async {
        if (p == '/api/v1/academy/calendar') {
          return {'today': '2026-09-28', 'trailingMonthFrom': '2026-08-28'};
        }
        if (p.contains('operating-statement')) return pending.future;
        if (p.contains('owner-summary')) return summary();
        if (p.contains('owner-cockpit')) {
          return {
            'currency': 'EGP',
            'collections': {'net': 5000, 'changePercentage': null},
            'target': <String, dynamic>{},
            'overdue': {'amount': 0},
            'risk': {'players': 0},
            'attendance': {'percentage': null},
            'capacity': {'seats': 0},
            'members': {'players': 0, 'groups': 0},
          };
        }
        if (p.startsWith('/api/v1/subscriptions/home?')) return home();
        if (p.contains('structure/options')) return {'branches': []};
        if (p.endsWith('/news')) return [];
        if (p.endsWith('my-permissions')) return [];
        if (p.endsWith('my-access')) return {'actions': []};
        return {'items': []};
      });
      await t.pumpWidget(
        app(OwnerDashboard(auth: auth, accountPage: const SizedBox())),
      );
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('التحصيل خلال الفترة'),
        230,
        scrollable: find.byType(Scrollable).first,
        maxScrolls: 30,
      );
      expect(find.text('التحصيل خلال الفترة'), findsOneWidget);
      expect(pending.isCompleted, false);
      expect(find.byType(HomeOperatingStatement), findsNothing);
      pending.complete({
        'currency': 'EGP',
        'surplus': 5000,
        'income': [],
        'expenses': [],
        'totalIncome': 5000,
        'totalExpenses': 0,
      });
      await t.pumpAndSettle();
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'actual nearest Coach session does not wait for evaluation-options data',
    (t) async {
      final pending = Completer<Object?>();
      final auth = await coach(
        (m, p, b, k) async => p == '/api/v1/evaluations/options'
            ? pending.future
            : homeResponse(p),
      );
      await t.pumpWidget(
        app(CoachHome(auth: auth, accountPage: const SizedBox())),
      );
      await t.pumpAndSettle();
      expect(find.byType(CoachSessionCard), findsWidgets);
      expect(find.text('اللاعبون في مجموعاتي'), findsNothing);
      expect(pending.isCompleted, false);
      pending.complete(homeResponse('/api/v1/evaluations/options'));
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('اللاعبون في مجموعاتي'),
        180,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.text('اللاعبون في مجموعاتي'), findsOneWidget);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
