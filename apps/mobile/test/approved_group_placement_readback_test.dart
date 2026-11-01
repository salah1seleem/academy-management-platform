import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/admin/native_structure_operations.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'approved_player_placement_test.dart' as flow;
import 'v2_role_workspaces_test.dart' show authenticate, app;

void main() {
  for (final fault in [
    'success',
    'lostCreate',
    'lostRead',
    'source',
    'roster',
    'date',
    'count',
  ]) {
    testWidgets(
      'native group placement verifies its revision and roster and freezes uncertain intent: $fault',
      (t) async {
        final writes = <Map>[];
        var recovered = false;
        final candidate = {
          'id': 'enrollment',
          'player': 'لاعب تسكين اصطناعي',
          'playerCode': 'TEST-1',
          'reasons': <String>[],
        };
        final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
          if (p.contains('/placement-candidates')) {
            return {
              'total': 1,
              'pageSize': 100,
              'currentPlayers': 0,
              'capacity': 20,
              'conflicts': <String>[],
              'players': [candidate],
            };
          }
          if (p.endsWith('/preview')) {
            return {
              'fingerprint': 'review',
              'selected': 1,
              'effectiveDate': '2026-10-22',
              'conflicts': <String>[],
              'players': [candidate],
            };
          }
          if (m == 'POST') {
            writes.add(Map.from(b as Map));
            if (fault == 'lostCreate' && writes.length == 1) {
              throw const ApiFailure(0, 'تعذر الاتصال بعد إرسال التسكين.');
            }
            return {
              'id': 'revision',
              'placed': 1,
              'effectiveDate': '2026-10-22',
            };
          }
          if (p.endsWith('/placement-revisions/revision')) {
            if (fault == 'lostRead' && !recovered) {
              throw const ApiFailure(503, 'تعذر قراءة التسكين.');
            }
            return {
              'id': 'revision',
              'trainingGroupId': !recovered && fault == 'source'
                  ? 'other'
                  : 'group',
              'placed': !recovered && fault == 'count' ? 2 : 1,
              'effectiveDate': !recovered && fault == 'date'
                  ? '2026-10-23'
                  : '2026-10-22',
              'assignedEnrollmentIds': ['enrollment'],
            };
          }
          if (p.endsWith('/details')) {
            return {
              'group': {'id': 'group'},
              'players': !recovered && fault == 'roster'
                  ? <Object?>[]
                  : [
                      {'id': 'enrollment'},
                    ],
            };
          }
          throw StateError('Unexpected $m $p');
        });
        await t.pumpWidget(
          app(NativeGroupPlacement(auth: auth, groupId: 'group')),
        );
        await t.pumpAndSettle();
        await flow.tap(t, 'لاعب تسكين اصطناعي');
        await flow.tap(t, 'معاينة التسكين');
        await flow.tap(t, 'تأكيد التسكين');
        if (fault != 'success') {
          expect(find.text('تم تسكين 1 لاعب.'), findsNothing);
          expect(find.byType(CheckboxListTile), findsNothing);
          expect(
            find.widgetWithText(TextFormField, 'اسم اللاعب أو الكود'),
            findsNothing,
          );
          recovered = true;
          await flow.tap(t, 'إعادة التحقق من التسكين نفسه');
        }
        expect(find.text('تم تسكين 1 لاعب.'), findsOneWidget);
        expect(writes.length, fault == 'lostCreate' ? 2 : 1);
        expect(
          writes.every((w) => w.toString() == writes.first.toString()),
          true,
        );
        expect(writes.first, {
          'enrollmentIds': ['enrollment'],
          'fingerprint': 'review',
        });
        if (fault == 'success') {
          await flow.tap(t, 'تسكين لاعب آخر');
          expect(
            find.widgetWithText(TextFormField, 'اسم اللاعب أو الكود'),
            findsOneWidget,
          );
          expect(find.text('تم تسكين 1 لاعب.'), findsNothing);
          expect(writes, hasLength(1));
        }
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox.shrink());
        auth.dispose();
      },
    );
  }
}
