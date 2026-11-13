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
      final formattedDate = MaterialLocalizations.of(
        t.element(find.byType(HomeSubscriptionPlayers)),
      ).formatShortDate(DateTime(2026, 9, 30));
      expect(find.text('الحالة في $formattedDate'), findsOneWidget);
      expect(find.text('متبقي 19 يوم'), findsOneWidget);
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
      expect(find.text('الحالة في $formattedDate'), findsOneWidget);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Snapshot displays specific subscription status and honors server permissions',
    (t) async {
      final auth = await owner(
        (method, path, body, token) async => {
          'total': 2,
          'asOf': '2026-11-07',
          'canOpenPlayer': false,
          'canEvaluate': false,
          'items': [
            {
              'id': 'e1',
              'playerId': 'p1',
              'name': 'لاعب خيالي',
              'playerCode': 'PL01',
              'sport': 'كرة القدم',
              'branch': 'فرع خيالي',
              'frozen': true,
            },
            {
              'id': 'e2',
              'playerId': 'p2',
              'name': 'لاعب خيالي',
              'playerCode': 'PL02',
              'sport': 'كرة القدم',
              'branch': 'فرع خيالي',
              'active': false,
            },
          ],
        },
      );
      await t.pumpWidget(
        shell(HomeSubscriptionPlayers(auth: auth, filters: const {})),
      );
      await t.pumpAndSettle();
      expect(find.text('مجمّد'), findsOneWidget);
      expect(find.text('منتهٍ'), findsOneWidget);
      expect(find.text('PL01'), findsOneWidget);
      expect(find.text('PL02'), findsOneWidget);
      expect(find.text('ملف اللاعب'), findsNothing);
      expect(find.text('تقييم'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets('An empty scoped snapshot displays the confirmed zero', (
    t,
  ) async {
    final requests = <String>[];
    final auth = await owner((method, path, body, token) async {
      requests.add(path);
      return {'total': 0, 'asOf': '2026-11-07', 'items': []};
    });
    await t.pumpWidget(
      shell(
        HomeSubscriptionPlayers(
          auth: auth,
          filters: const {'branchId': 'b1', 'asOf': '2026-11-07'},
          state: 'expiring',
          academyToday: '2026-11-07',
        ),
      ),
    );
    await t.pumpAndSettle();
    expect(find.text('عدد التسجيلات · 0'), findsOneWidget);
    expect(find.text('لا توجد سجلات في هذا النطاق.'), findsOneWidget);
    expect(find.text('تنبيه أولياء الأمور'), findsNothing);
    expect(requests, hasLength(1));
    expect(Uri.parse(requests.single).queryParameters['branchId'], 'b1');
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  testWidgets(
    'expiry reminders are available for today only and recheck the academy date before sending',
    (t) async {
      var today = '2026-11-08';
      final writes = <Object?>[];
      final auth = await owner((method, path, body, token) async {
        if (method == 'POST') {
          writes.add(body);
          return {'message': 'أُرسل التنبيه.'};
        }
        if (path == '/api/v1/academy/calendar') return {'today': today};
        return {
          'total': 1,
          'asOf': '2026-11-07',
          'items': [
            {
              'id': 'e1',
              'playerId': 'p1',
              'name': 'لاعب خيالي',
              'sport': 'كرة القدم',
              'branch': 'فرع خيالي',
              'active': true,
              'endDate': '2026-11-11',
            },
          ],
        };
      });
      Widget list(String academyToday) => shell(
        HomeSubscriptionPlayers(
          auth: auth,
          filters: const {
            'branchId': 'b1',
            'sportId': 's1',
            'asOf': '2026-11-07',
          },
          state: 'expiring',
          academyToday: academyToday,
        ),
      );
      await t.pumpWidget(list('2026-11-08'));
      await t.pumpAndSettle();
      expect(find.text('تنبيه أولياء الأمور'), findsNothing);
      await t.pumpWidget(list('2026-11-07'));
      await t.pumpAndSettle();
      await t.tap(find.text('تنبيه أولياء الأمور'));
      await t.pumpAndSettle();
      await t.tap(find.text('إرسال التنبيه'));
      await t.pumpAndSettle();
      expect(find.textContaining('تغيّر تاريخ القائمة'), findsOneWidget);
      expect(writes, isEmpty);

      today = '2026-11-07';
      await t.tap(find.text('تنبيه أولياء الأمور'));
      await t.pumpAndSettle();
      await t.tap(find.text('إرسال التنبيه'));
      await t.pumpAndSettle();
      expect(writes.single, {'branchId': 'b1', 'sportId': 's1'});
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
