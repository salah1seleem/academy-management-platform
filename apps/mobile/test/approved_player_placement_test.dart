import 'support/infinite_list.dart';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/admin/native_player_placement.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

const enrollment = {
  'id': 'enrollment',
  'playerId': 'player',
  'branchId': 'branch',
  'sportId': 'football',
  'trainingGroupId': 'old',
  'sportName': 'كرة القدم',
  'branchName': 'فرع اختبار',
  'registrationFormat': 'Group',
};
const group = {
  'id': 'new',
  'branchId': 'branch',
  'sportId': 'football',
  'arabicName': 'مجموعة بديلة',
  'registrationFormat': 'Group',
};
const original = {
  'id': 'original',
  'group': 'الأصلية',
  'date': '2026-10-22',
  'start': '16:00:00',
  'end': '17:00:00',
};
const target = {
  'id': 'target',
  'group': 'البديلة',
  'date': '2026-10-23',
  'start': '18:00:00',
  'end': '19:00:00',
};
Future<void> tap(WidgetTester t, String label) async {
  final scroll = find.byType(Scrollable).first;
  t.state<ScrollableState>(scroll).position.jumpTo(0);
  await t.pumpAndSettle();
  final f = find.text(label);
  await t.scrollUntilVisible(f, 150, scrollable: scroll);
  await t.ensureVisible(f.last);
  await t.pumpAndSettle();
  await t.ensureVisible(f.last);
  await t.tap(f.last);
  await t.pumpAndSettle();
}

Future<void> choose(WidgetTester t, String label, String value) async {
  final f = find.widgetWithText(DropdownButtonFormField<String>, label);
  await t.ensureVisible(f);
  await t.pumpAndSettle();
  await t.tap(f);
  await t.pumpAndSettle();
  await t.tap(find.text(value).last);
  await t.pumpAndSettle();
}

void main() {
  testWidgets(
    'Transfer conflict prevents confirmation; refreshed preview uses its fingerprint and verifies saved history',
    (t) async {
      var conflict = true, saved = false;
      Map? write;
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        if (p.endsWith('/options')) {
          return {
            'groups': [group],
          };
        }
        if (p.endsWith('/calendar')) return {'today': '2026-10-21'};
        if (p.endsWith('/preview')) {
          return {
            'fingerprint': 'reviewed',
            'conflicts': conflict ? ['المجموعة مكتملة'] : [],
            'sourceGroupId': 'old',
            'sourceGroup': 'الأصلية',
            'targetGroup': 'مجموعة بديلة',
            'effectiveDate': '2026-10-21',
            'removedSessions': [],
            'addedSessions': [],
          };
        }
        if (m == 'POST') {
          write = b as Map;
          saved = true;
          return {'id': 'transfer'};
        }
        return saved
            ? [
                {
                  'id': 'transfer',
                  'sourceGroupId': 'old',
                  'targetGroupId': 'new',
                  'effectiveDate': '2026-10-21',
                },
              ]
            : [];
      });
      await t.pumpWidget(
        app(
          NativePlayerPlacement(
            auth: auth,
            enrollment: enrollment,
            action: PlayerPlacementAction.group,
          ),
        ),
      );
      await t.pumpAndSettle();
      await choose(t, 'المجموعة البديلة', 'مجموعة بديلة');
      await tap(t, 'معاينة الأثر والتعارضات');
      expect(find.text('المجموعة مكتملة'), findsOneWidget);
      expect(find.text('تأكيد الإجراء'), findsNothing);
      conflict = false;
      await tap(t, 'معاينة الأثر والتعارضات');
      await tap(t, 'راجعت الأثر والحصص وأؤكد اعتماد الإجراء.');
      await tap(t, 'تأكيد الإجراء');
      expect(write, {
        'groupId': 'new',
        'effectiveDate': '2026-10-21',
        'fingerprint': 'reviewed',
      });
      expect(
        find.text('حُفظ الإجراء وأُعيدت قراءة سجله الفعلي.'),
        findsOneWidget,
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Selected occurrence move preserves exact pair and retries same payload after an uncertain response',
    (t) async {
      final writes = <Map>[];
      var saved = false;
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        if (p.endsWith('/structure/options')) {
          return {
            'groups': [group],
          };
        }
        if (p.endsWith('/calendar')) return {'today': '2026-10-21'};
        if (p.contains('/session-transfer/options')) {
          return {
            'original': [original],
            'alternative': p.contains('?') ? [target] : [],
          };
        }
        if (p.endsWith('/preview')) {
          return {
            'fingerprint': 'pair-review',
            'conflicts': [],
            'changes': [
              {
                'source': original,
                'target': target,
                'expectedPlayers': 4,
                'capacity': 8,
              },
            ],
          };
        }
        if (m == 'POST') {
          writes.add(Map.from(b as Map));
          if (writes.length == 1) throw const ApiFailure(0, 'تعذر الاتصال');
          saved = true;
          return {'id': 'batch', 'moved': 1};
        }
        return {
          'enrollments': [
            {
              'id': 'enrollment',
              'sessionMoves': saved
                  ? [
                      {
                        'batchId': 'batch',
                        'sourceSessionId': 'original',
                        'targetSessionId': 'target',
                      },
                    ]
                  : [],
            },
          ],
        };
      });
      await t.pumpWidget(
        app(
          NativePlayerPlacement(
            auth: auth,
            enrollment: enrollment,
            action: PlayerPlacementAction.sessions,
          ),
        ),
      );
      await t.pumpAndSettle();
      await choose(t, 'المجموعة البديلة', 'مجموعة بديلة');
      await choose(
        t,
        'الحصة الأصلية',
        'الأصلية · 2026-10-22 · 16:00:00 — 17:00:00',
      );
      await choose(
        t,
        'الحصة البديلة',
        'البديلة · 2026-10-23 · 18:00:00 — 19:00:00',
      );
      await tap(t, 'إضافة إلى الحصص المختارة');
      await tap(t, 'معاينة الأثر والتعارضات');
      await tap(t, 'راجعت الأثر والحصص وأؤكد اعتماد الإجراء.');
      await tap(t, 'تأكيد الإجراء');
      expect(
        find.text('حُفظ الإجراء وأُعيدت قراءة سجله الفعلي.'),
        findsNothing,
      );
      await tap(t, 'إعادة تأكيد الإجراء نفسه');
      expect(writes, hasLength(2));
      expect(writes[0], writes[1]);
      expect(writes[0], {
        'pairs': [
          {'sourceSessionId': 'original', 'targetSessionId': 'target'},
        ],
        'fingerprint': 'pair-review',
      });
      expect(
        find.text('حُفظ الإجراء وأُعيدت قراءة سجله الفعلي.'),
        findsOneWidget,
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Compensation booking requires reviewed available units and reads persisted booking before success',
    (t) async {
      Map? write;
      var saved = false;
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        if (p.endsWith('/options')) {
          return {
            'today': '2026-10-21',
            'sources': [],
            'targets': [target],
          };
        }
        if (p.endsWith('/preview')) {
          return {
            'fingerprint': 'booking-review',
            'units': 1,
            'available': 1,
            'expectedPlayers': 5,
            'capacity': 8,
            'target': target,
            'conflicts': [],
          };
        }
        if (m == 'POST') {
          write = b as Map;
          saved = true;
          return {'id': 'booking'};
        }
        return [
          {
            'id': 'grant',
            'bookings': saved
                ? [
                    {
                      'id': 'booking',
                      'trainingSessionId': 'target',
                      'units': 1,
                      'state': 'Reserved',
                    },
                  ]
                : [],
          },
        ];
      });
      await t.pumpWidget(
        app(
          NativePlayerPlacement(
            auth: auth,
            enrollment: enrollment,
            action: PlayerPlacementAction.booking,
            grant: const {'id': 'grant'},
          ),
        ),
      );
      await t.pumpAndSettle();
      await choose(
        t,
        'الحصة البديلة',
        'البديلة · 2026-10-23 · 18:00:00 — 19:00:00',
      );
      await tap(t, 'معاينة الأثر والتعارضات');
      await tap(t, 'راجعت الأثر والحصص وأؤكد اعتماد الإجراء.');
      await tap(t, 'تأكيد الإجراء');
      expect(write, {
        'trainingSessionId': 'target',
        'fingerprint': 'booking-review',
      });
      expect(
        find.text('حُفظ الإجراء وأُعيدت قراءة سجله الفعلي.'),
        findsOneWidget,
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Native player directory uses server pagination, one row per player and scoped search',
    (t) async {
      final calls = <String>[];
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        calls.add(p);
        final page = Uri.parse(p).queryParameters['page'];
        return {
          'total': 2,
          'items': [
            {
              'id': page,
              'arabicName': page == '2'
                  ? 'لاعب الصفحة الثانية'
                  : 'لاعب الصفحة الأولى',
              'playerCode': 'P$page',
              'enrollmentSummaries': [],
            },
          ],
        };
      });
      await t.pumpWidget(app(NativePlayerDirectory(auth: auth)));
      await t.pumpAndSettle();
      expect(find.text('لاعب الصفحة الأولى'), findsOneWidget);
      await scrollForNextPage(t);
      expect(find.text('لاعب الصفحة الثانية'), findsOneWidget);
      expect(calls.last, contains('page=2'));
      expect(calls.last, contains('pageSize=25'));
      await t.enterText(find.byType(TextFormField), 'P27');
      await tap(t, 'بحث اللاعبين');
      expect(calls.last, contains('search=P27'));
      expect(calls.last, contains('page=1'));
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
