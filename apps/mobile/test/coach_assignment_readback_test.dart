import 'package:academy_mobile/features/admin/native_structure_operations.dart';
import 'package:academy_mobile/features/shared/subscription_home.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:academy_mobile/features/owner/owner_operations.dart';
import 'package:academy_mobile/features/owner/refined_home_panels.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/visibility.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;
import 'owner_test.dart' as fixture;

const options = {
  'branches': [
    {'id': 'branch', 'arabicName': 'فرع الاختبار'},
  ],
  'coaches': [
    {'id': 'coach', 'arabicName': 'مدرب الاختبار'},
  ],
  'groups': [
    {'id': 'group', 'arabicName': 'مجموعة الفرع', 'branchId': 'branch'},
    {'id': 'other', 'arabicName': 'مجموعة فرع آخر', 'branchId': 'outside'},
  ],
};
Future<void> tap(WidgetTester t, Finder f) async {
  await revealTestTarget(t, f);
  await t.tap(f);
  await t.pumpAndSettle();
}

Future<void> chooseGroup(WidgetTester t) async {
  await t.tap(find.widgetWithText(DropdownButtonFormField<String>, 'المجموعة'));
  await t.pumpAndSettle();
  expect(find.text('مجموعة فرع آخر'), findsNothing);
  await t.tap(find.text('مجموعة الفرع').last);
  await t.pumpAndSettle();
}

void main() {
  for (final mismatch in [
    '',
    'id',
    'trainingGroupId',
    'academyMembershipId',
    'isActive',
  ]) {
    testWidgets(
      'Assignment confirms exact scoped references before success; mismatch=$mismatch',
      (t) async {
        final calls = <String>[];
        final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
          calls.add('$m $p');
          if (p.endsWith('/options')) return options;
          if (m == 'POST') {
            expect(b, {'membershipId': 'coach'});
            return 'assignment';
          }
          return {
            'id': 'assignment',
            'trainingGroupId': 'group',
            'academyMembershipId': 'coach',
            'isActive': true,
            if (mismatch.isNotEmpty)
              mismatch: mismatch == 'isActive' ? false : 'wrong',
          };
        });
        await t.pumpWidget(
          app(
            NativeCoachAssignment(
              auth: auth,
              coachId: 'coach',
              branchId: 'branch',
            ),
          ),
        );
        await t.pumpAndSettle();
        await chooseGroup(t);
        await tap(t, find.text('حفظ الإسناد'));
        expect(
          calls,
          contains('GET /api/v1/manage/groups/group/coaches/coach'),
        );
        expect(
          find.text('تم حفظ الإسناد.'),
          mismatch.isEmpty ? findsOneWidget : findsNothing,
        );
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  for (final allDecisions in [false, true]) {
    testWidgets(
      'Owner assignment return refreshes saved decision and keeps branch/date scope; all=$allDecisions',
      (t) async {
        var stored = false;
        var reads = 0;
        final calls = <String>[];
        final auth = await fixture.homeOwner((m, p, b, token) async {
          calls.add(p);
          if (p.contains('/reports/owner-summary')) {
            reads++;
            return {
              ...fixture.summary(),
              'operations': {
                'coachPerformance': <Object>[],
                'groupAttention': <Object>[],
                'underAssignedCoaches': stored
                    ? <Object>[]
                    : [
                        {
                          'id': 'coach',
                          'name': 'مدرب الاختبار',
                          'groupCount': 2,
                          'target': 3,
                        },
                      ],
              },
            };
          }
          if (p.endsWith('/structure/options')) return options;
          if (p.endsWith('/news')) return <Object>[];
          if (m == 'POST') {
            expect(b, {'membershipId': 'coach'});
            stored = true;
            return 'assignment';
          }
          if (p == '/api/v1/manage/groups/group/coaches/coach') {
            return {
              'id': 'assignment',
              'trainingGroupId': 'group',
              'academyMembershipId': 'coach',
              'isActive': true,
            };
          }
          return {};
        });
        await t.pumpWidget(
          fixture.shell(
            OwnerDashboard(auth: auth, accountPage: const SizedBox()),
          ),
        );
        await t.pumpAndSettle();
        t
            .widget<SubscriptionHomePanel>(find.byType(SubscriptionHomePanel))
            .onBranchChanged!('branch');
        await t.pumpAndSettle();
        if (allDecisions) {
          await tap(t, find.text('كل القرارات'));
          expect(
            t
                .widget<OwnerDecisionsPage>(find.byType(OwnerDecisionsPage))
                .filters,
            {'from': '2026-08-28', 'to': '2026-09-28', 'branchId': 'branch'},
          );
          expect(
            calls.lastWhere((p) => p.contains('/reports/owner-summary')),
            contains('branchId=branch'),
          );
          await tap(t, find.text('مدرب الاختبار · 2 مجموعات من أصل 3'));
        } else {
          await t.scrollUntilVisible(
            find.byType(HomeDecisionCards),
            220,
            scrollable: find.byType(Scrollable).first,
            maxScrolls: 80,
          );
          await t.pumpAndSettle();
          await tap(
            t,
            find.descendant(
              of: find.byType(HomeDecisionCards),
              matching: find.text('إسناد مدرب لمجموعة'),
            ),
          );
        }
        final page = t.widget<NativeCoachAssignment>(
          find.byType(NativeCoachAssignment),
        );
        expect(page.coachId, 'coach');
        expect(page.branchId, 'branch');
        await chooseGroup(t);
        await tap(t, find.text('حفظ الإسناد'));
        expect(find.text('تم حفظ الإسناد.'), findsOneWidget);
        final before = reads;
        Navigator.of(t.element(find.byType(NativeCoachAssignment))).pop();
        await t.pumpAndSettle();
        if (allDecisions) {
          Navigator.of(t.element(find.byType(OwnerDecisionsPage))).pop();
          await t.pumpAndSettle();
        }
        expect(reads, greaterThan(before));
        await t.scrollUntilVisible(
          find.byType(SubscriptionHomePanel),
          -220,
          scrollable: find.byType(Scrollable).first,
          maxScrolls: 80,
        );
        await t.pumpAndSettle();
        expect(
          t
              .widget<SubscriptionHomePanel>(find.byType(SubscriptionHomePanel))
              .branchId,
          'branch',
        );
        await t.scrollUntilVisible(
          find.byType(HomeDecisionCards),
          220,
          scrollable: find.byType(Scrollable).first,
          maxScrolls: 80,
        );
        await t.pumpAndSettle();
        expect(
          t
              .widget<HomeDecisionCards>(find.byType(HomeDecisionCards))
              .data['underAssignedCoaches'],
          isEmpty,
        );
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
}
