import 'package:academy_mobile/features/shared/matches.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'guardian_test.dart' show logged;
import 'coach_test.dart' show shell;

void main() {
  testWidgets(
    'Deep link reads only the scoped match and ignores unrelated legacy rows',
    (t) async {
      final calls = <String>[];
      final auth = await logged((m, p, b, token) async {
        calls.add(p);
        return [
          {'id': 'other', 'team': 'فريق خارج الرابط'},
          {
            'id': 'match',
            'team': 'فريق الرابط',
            'opponent': 'المنافس',
            'status': 'ResultDraft',
            'ourScore': 2,
            'opponentScore': 0,
            'resultFinalized': true,
            'matchDate': '2026-10-22',
          },
        ];
      });
      await t.pumpWidget(
        shell(MatchSchedule(auth: auth, playerId: 'child-1', matchId: 'match')),
      );
      await t.pumpAndSettle();
      expect(calls, ['/api/v1/my-matches?playerId=child-1&matchId=match']);
      expect(find.text('فريق الرابط'), findsOneWidget);
      expect(find.text('فريق خارج الرابط'), findsNothing);
      expect(find.text('النتيجة قيد المراجعة'), findsOneWidget);
      expect(find.text('2'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets(
      'child context, cancellation and bench role fit $width with large text',
      (t) async {
        t.view.physicalSize = Size(width, 1100);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.resetPhysicalSize);
        addTearDown(t.view.resetDevicePixelRatio);
        final calls = <String>[];
        final auth = await logged((m, p, b, token) async {
          calls.add(p);
          if (p == '/api/v1/guardian/home') {
            return {
              'children': [
                {'id': 'child-1', 'arabicName': 'طفل اصطناعي'},
              ],
            };
          }
          return [
            <String, dynamic>{
              'id': 'match',
              'team': 'منتخب اصطناعي',
              'opponent': 'فريق عرض',
              'tournament': 'منافسة محلية',
              'matchDate': '2026-10-17',
              'startTime': '17:30:00',
              'venue': 'ملعب العرض',
              'status': 'Cancelled',
              'coachAssignmentSource': 'Confirmed',
              'coachName': 'مدرب مباراة اصطناعي',
              'participation': 'Bench',
              'ourScore': null,
              'notes': null,
            },
          ];
        });
        await t.pumpWidget(
          shell(
            Builder(
              builder: (context) => MediaQuery(
                data: MediaQuery.of(context)
                    .copyWith(textScaler: const TextScaler.linear(2)),
                child: MatchSchedule(auth: auth, playerId: 'child-1'),
              ),
            ),
          ),
        );
        await t.pumpAndSettle();
        expect(calls, [
          '/api/v1/guardian/home',
          '/api/v1/my-matches?playerId=child-1',
        ]);
        expect(find.text('ملغاة'), findsOneWidget);
        expect(find.text('المشاركة: بديل'), findsOneWidget);
        expect(find.text('مدرب المباراة: مدرب مباراة اصطناعي'), findsOneWidget);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  testWidgets(
    'empty child history is not replaced by all family matches; retry keeps scope',
    (t) async {
      final calls = <String>[];
      var fail = true;
      final auth = await logged((m, p, b, token) async {
        calls.add(p);
        if (p == '/api/v1/guardian/home') {
          return {
            'children': [
              {'id': 'child-2', 'arabicName': 'طفل ثان اصطناعي'},
            ],
          };
        }
        if (fail) throw Exception('synthetic failure');
        return <Map<String, dynamic>>[];
      });
      await t.pumpWidget(shell(MatchSchedule(auth: auth, playerId: 'child-2')));
      await t.pumpAndSettle();
      expect(find.text('إعادة المحاولة'), findsOneWidget);
      fail = false;
      await t.tap(find.text('إعادة المحاولة'));
      await t.pumpAndSettle();
      expect(find.text('لا توجد مباريات منشورة تخصك حاليًا.'), findsOneWidget);
      expect(
        calls
            .where((p) => p != '/api/v1/guardian/home')
            .every((p) => p == '/api/v1/my-matches?playerId=child-2'),
        isTrue,
      );
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
