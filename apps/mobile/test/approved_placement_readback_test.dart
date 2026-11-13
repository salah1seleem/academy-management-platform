import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/admin/native_player_placement.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'approved_player_placement_test.dart' as flow;
import 'v2_role_workspaces_test.dart' show authenticate, app;

void main() {
  for (final action in PlayerPlacementAction.values) {
    for (final fault in [
      'success',
      'source',
      'details',
      'lostRead',
      'lostWrite',
    ]) {
      testWidgets(
        'placement ${action.name} confirms its exact source and retries acknowledged reads only: $fault',
        (t) async {
          var posts = 0, recovered = false;
          final auth = await authenticate('AcademyOwner', (
            m,
            p,
            b,
            token,
          ) async {
            if (p.endsWith('/structure/options')) {
              return {
                'groups': [flow.group],
              };
            }
            if (p.endsWith('/calendar')) return {'today': '2026-10-21'};
            if (p.contains('/session-transfer/options')) {
              return {
                'original': [flow.original],
                'alternative': [flow.target],
              };
            }
            if (p.endsWith('/compensation/options')) {
              return {
                'today': '2026-10-21',
                'sources': [flow.original],
                'targets': [flow.target],
              };
            }
            if (p.endsWith('/preview')) {
              return {
                'fingerprint': 'reviewed',
                'conflicts': [],
                'sourceGroupId': 'old',
                'sourceGroup': 'الأصلية',
                'targetGroup': 'مجموعة بديلة',
                'effectiveDate': '2026-10-21',
                'removedSessions': [],
                'addedSessions': [],
                'changes': [
                  {
                    'source': flow.original,
                    'target': flow.target,
                    'expectedPlayers': 4,
                    'capacity': 8,
                  },
                ],
                'units': 1,
                'available': 1,
              };
            }
            if (m == 'POST') {
              posts++;
              if (fault == 'lostWrite') {
                throw const ApiFailure(503, 'تعذر تأكيد الحفظ.');
              }
              return {'id': 'saved'};
            }
            if (posts == 0) throw StateError('Unexpected initial read $p');
            if (fault == 'lostRead' && !recovered) {
              throw const ApiFailure(503, 'تعذر قراءة السجل.');
            }
            final wrongSource = fault == 'source' && !recovered,
                wrongDetails = fault == 'details' && !recovered;
            return switch (action) {
              PlayerPlacementAction.group => [
                {
                  'id': 'saved',
                  'sourceGroupId': 'old',
                  'targetGroupId': wrongSource ? 'other' : 'new',
                  'effectiveDate': wrongDetails ? '2026-10-22' : '2026-10-21',
                },
              ],
              PlayerPlacementAction.sessions => {
                'enrollments': [
                  {
                    'id': 'enrollment',
                    'sessionMoves': [
                      {
                        'batchId': wrongSource ? 'other' : 'saved',
                        'sourceSessionId': 'original',
                        'targetSessionId': wrongDetails ? 'other' : 'target',
                      },
                    ],
                  },
                ],
              },
              PlayerPlacementAction.compensation => [
                {
                  'id': 'saved',
                  'sportEnrollmentId': 'enrollment',
                  'sourceSessionId': wrongSource ? 'other' : 'original',
                  'cause': 'ApprovedAbsence',
                  'unit': 'Session',
                  'units': wrongDetails ? 2 : 1,
                  'validFrom': '2026-10-21',
                  'validTo': '2026-10-21',
                  'bookings': [],
                },
              ],
              PlayerPlacementAction.booking => [
                {
                  'id': wrongSource ? 'other-grant' : 'grant',
                  'bookings': [
                    {
                      'id': 'saved',
                      'trainingSessionId': wrongDetails ? 'other' : 'target',
                      'units': 1,
                      'state': 'Reserved',
                    },
                  ],
                },
              ],
            };
          });
          await t.pumpWidget(
            app(
              NativePlayerPlacement(
                auth: auth,
                enrollment: flow.enrollment,
                action: action,
                grant: action == PlayerPlacementAction.booking
                    ? const {'id': 'grant'}
                    : null,
              ),
            ),
          );
          await t.pumpAndSettle();
          if (action == PlayerPlacementAction.group ||
              action == PlayerPlacementAction.sessions) {
            await flow.choose(t, 'المجموعة البديلة', 'مجموعة بديلة');
          }
          if (action == PlayerPlacementAction.sessions ||
              action == PlayerPlacementAction.compensation) {
            await flow.choose(
              t,
              'الحصة الأصلية',
              'الأصلية · 2026-10-22 · 16:00:00 — 17:00:00',
            );
          }
          if (action == PlayerPlacementAction.sessions ||
              action == PlayerPlacementAction.booking) {
            await flow.choose(
              t,
              'الحصة البديلة',
              'البديلة · 2026-10-23 · 18:00:00 — 19:00:00',
            );
          }
          if (action == PlayerPlacementAction.sessions) {
            await flow.tap(t, 'إضافة إلى الحصص المختارة');
          }
          final groupTransfer = action == PlayerPlacementAction.group;
          final sessionTransfer = action == PlayerPlacementAction.sessions;
          final transfer = groupTransfer || sessionTransfer;
          final booking = action == PlayerPlacementAction.booking;
          await flow.tap(
            t,
            groupTransfer
                ? 'معاينة نقل المجموعة'
                : sessionTransfer
                ? 'معاينة نقل الحصص'
                : booking
                ? 'معاينة حجز التعويض'
                : 'معاينة استحقاق التعويض',
          );
          await flow.tap(
            t,
            groupTransfer
                ? 'تأكيد نقل المجموعة'
                : sessionTransfer
                ? 'تأكيد نقل الحصص'
                : booking
                ? 'تأكيد حجز التعويض'
                : 'تأكيد استحقاق التعويض',
          );
          final success = groupTransfer
              ? 'تم تسجيل نقل المجموعة'
              : sessionTransfer
              ? 'تم تسجيل نقل الحصص المختارة.'
              : booking
              ? 'تم حجز الحصة التعويضية.'
              : 'تم اعتماد التعويض.';
          if (fault != 'success') {
            expect(find.textContaining(success), findsNothing);
            expect(posts, 1);
            recovered = true;
            await flow.tap(
              t,
              transfer ? 'تحقّق من سجل النقل' : 'تحقّق من سجل التعويض',
            );
          }
          expect(find.textContaining(success), findsOneWidget);
          expect(
            posts,
            1,
            reason: 'An acknowledged placement only retries reading its exact source.',
          );
          expect(t.takeException(), isNull);
          await t.pumpWidget(const SizedBox.shrink());
          auth.dispose();
        },
      );
    }
  }
}
