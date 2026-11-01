import 'package:academy_mobile/features/shared/matches.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'guardian_test.dart' show logged, shell;

Map<String, dynamic> match(String id, String opponent) => {
  'id': id,
  'team': 'فريق اصطناعي',
  'opponent': opponent,
  'tournament': 'دوري اصطناعي',
  'venue': 'ملعب تجريبي',
  'matchDate': '2026-10-22',
  'startTime': id == 'past' ? '08:00:00' : '23:59:00',
  'isUpcoming': true,
  'status': 'Published',
};
void main() {
  for (final child in <String?>[null, 'child-safe']) {
    testWidgets(
      'nearest match uses the academy time-filtered source and preserves child=$child',
      (t) async {
        final paths = <String>[];
        final auth = await logged((m, p, b, token) async {
          paths.add(p);
          return p.contains('upcomingOnly=true')
              ? [match('future', 'منافس قادم')]
              : [
                  match('past', 'منافس بدأ موعده اليوم'),
                  match('future', 'منافس قادم'),
                ];
        });
        await t.pumpWidget(shell(NextMatchStrip(auth: auth, playerId: child)));
        await t.pumpAndSettle();
        expect(find.textContaining('منافس بدأ موعده اليوم'), findsNothing);
        expect(find.textContaining('منافس قادم'), findsOneWidget);
        expect(paths, [
          '/api/v1/my-matches?upcomingOnly=true${child == null ? '' : '&playerId=$child'}',
        ]);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  testWidgets(
    'no time-eligible match hides the strip without a historical fallback',
    (t) async {
      final paths = <String>[];
      final auth = await logged((m, p, b, token) async {
        paths.add(p);
        return p.contains('upcomingOnly=true')
            ? []
            : [match('past', 'منافس بدأ موعده اليوم')];
      });
      await t.pumpWidget(
        shell(NextMatchStrip(auth: auth, playerId: 'child-safe')),
      );
      await t.pumpAndSettle();
      expect(find.textContaining('منافس بدأ موعده اليوم'), findsNothing);
      expect(find.byType(InkWell), findsNothing);
      expect(paths.length, 1);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
