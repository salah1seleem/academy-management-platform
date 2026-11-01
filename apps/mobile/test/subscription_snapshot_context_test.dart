import 'support/infinite_list.dart';

import 'dart:async';

import 'package:academy_mobile/features/owner/home_destinations.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' show owner, shell;

void main() {
  testWidgets(
    'Subscription detail displays the returned snapshot date and retains branch while paging without stale rows',
    (t) async {
      final paths = <String>[];
      final pending = Completer<Object?>();
      final auth = await owner((method, path, body, token) async {
        paths.add(path);
        if (path.contains('page=2')) return pending.future;
        return {
          'total': 51,
          'asOf': '2026-09-30',
          'items': [
            {
              'id': 'e1',
              'playerId': 'p1',
              'name': 'طفل سبتمبر الاصطناعي',
              'sport': 'كرة القدم',
              'branch': 'فرع اصطناعي',
              'active': true,
              'endDate': '2026-10-19',
            },
          ],
        };
      });
      await t.pumpWidget(
        shell(
          HomeSubscriptionPlayers(
            auth: auth,
            filters: const {'branchId': 'b1', 'to': '2026-09-30'},
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('حتى 2026-09-30'), findsOneWidget);
      expect(paths.single, contains('branchId=b1'));
      expect(paths.single, contains('asOf=2026-09-30'));
      await scrollForNextPage(t);
      await t.pump();
      await t.pump(const Duration(milliseconds: 20));
      expect(find.text('طفل سبتمبر الاصطناعي'), findsOneWidget);
      expect(paths.last, contains('page=2'));
      expect(paths.last, contains('asOf=2026-09-30'));
      pending.complete({'total': 51, 'asOf': '2026-09-30', 'items': []});
      await t.pumpAndSettle();
      expect(find.text('طفل سبتمبر الاصطناعي'), findsOneWidget);
      expect(find.text('حتى 2026-09-30'), findsOneWidget);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
