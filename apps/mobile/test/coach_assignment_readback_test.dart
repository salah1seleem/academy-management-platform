import 'package:academy_mobile/features/admin/native_structure_operations.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_operations.dart';
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
  testWidgets(
    'Coach assignment opens from the operational coach card in its branch',
    (t) async {
      var saved = false;
      final auth = await fixture.homeOwner((m, p, b, token) async {
        if (p.endsWith('/structure/options')) return options;
        if (m == 'POST') {
          expect(b, {'membershipId': 'coach'});
          saved = true;
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
          GuardianPage(
            title: 'المدربون',
            child: OwnerDecisionsPanel(
              auth: auth,
              operations: {
                'underAssignedCoaches': [
                  {
                    'id': 'coach',
                    'name': 'مدرب الاختبار',
                    'groupCount': 2,
                    'target': 3,
                  },
                ],
              },
              branchId: 'branch',
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      await tap(t, find.text('مدرب الاختبار · 2 مجموعات من أصل 3'));
      expect(
        t
            .widget<NativeCoachAssignment>(find.byType(NativeCoachAssignment))
            .branchId,
        'branch',
      );
      await chooseGroup(t);
      await tap(t, find.text('حفظ الإسناد'));
      expect(saved, isTrue);
      expect(find.text('تم حفظ الإسناد.'), findsOneWidget);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
