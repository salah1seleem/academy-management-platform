import 'support/visibility.dart';

import 'package:academy_mobile/features/owner/native_staff_management.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

Map<String, dynamic> access() => {
  'allBranches': false,
  'allSports': false,
  'selectedBranches': ['b1'],
  'selectedSports': ['s1'],
  'branches': [
    {'id': 'b1', 'name': 'فرع أول'},
    {'id': 'b2', 'name': 'فرع ثان'},
  ],
  'sports': [
    {'id': 's1', 'name': 'كرة القدم'},
  ],
  'modules': ['attendance', 'finance', 'players', 'subscriptions'],
  'actions': [
    for (final m in ['attendance', 'finance', 'players', 'subscriptions'])
      {
        'module': m,
        'read': false,
        'add': false,
        'edit': false,
        'delete': false,
      },
  ],
  'workflowActions': [
    {
      'key': 'workflow.attendance.staff',
      'label': 'تسجيل حضور الموظفين',
      'module': 'attendance',
      'operation': 'Edit',
      'allowed': false,
    },
    {
      'key': 'workflow.subscriptions.exceptional-discount',
      'label': 'منح خصم استثنائي',
      'module': 'subscriptions',
      'operation': 'Add',
      'allowed': false,
    },
    {
      'key': 'workflow.subscriptions.refund',
      'label': 'الاسترداد المالي للاشتراكات',
      'module': 'subscriptions',
      'operation': 'Edit',
      'allowed': false,
    },
  ],
  'sensitivePermissions': <String>[],
  'selectedSensitivePermissions': <String>[],
};
void main() {
  testWidgets(
    'Attendance-only grant persists its exact branch/sport and does not grant finance, refunds or exceptional discounts',
    (t) async {
      await t.binding.setSurfaceSize(const Size(390, 844));
      addTearDown(() => t.binding.setSurfaceSize(null));
      Map<String, dynamic> saved = access();
      Map<String, dynamic>? body;
      var reads = 0;
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        if (m == 'GET') {
          reads++;
          return saved;
        }
        body = b as Map<String, dynamic>;
        saved = {
          ...saved,
          ...body!,
          'selectedBranches': body!['branches'],
          'selectedSports': body!['sports'],
          'selectedSensitivePermissions': body!['sensitivePermissions'],
          'workflowActions': (saved['workflowActions'] as List)
              .map(
                (a) => {
                  ...a as Map<String, dynamic>,
                  'allowed': (body!['workflowActions'] as List).contains(
                    a['key'],
                  ),
                },
              )
              .toList(),
        };
        return null;
      });
      await t.pumpWidget(
        app(
          NativeStaffAccessEditor(
            auth: auth,
            membershipId: 'admin',
            name: 'إداري عرض',
          ),
        ),
      );
      await t.pumpAndSettle();
      final attendance = find.widgetWithText(ExpansionTile, 'الحضور');
      await revealTestTarget(t, attendance);
      await t.tap(attendance);
      await t.pumpAndSettle();
      final checkbox = find.widgetWithText(
        CheckboxListTile,
        'تسجيل حضور الموظفين',
      );
      await revealTestTarget(t, checkbox);
      await t.tap(checkbox);
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('اعتماد الصلاحيات'),
        200,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(find.text('اعتماد الصلاحيات'));
      await t.pumpAndSettle();
      expect(find.text('اعتماد صلاحيات الموظف؟'), findsOneWidget);
      await t.tap(find.text('تأكيد الاعتماد'));
      await t.pumpAndSettle();
      expect(reads, 2);
      expect(body!['branches'], ['b1']);
      expect(body!['sports'], ['s1']);
      expect(body!['allBranches'], isFalse);
      expect(body!['allSports'], isFalse);
      expect(body!['workflowActions'], ['workflow.attendance.staff']);
      final grants = (body!['actions'] as List).cast<Map<String, dynamic>>();
      expect(
        grants.singleWhere((g) => g['module'] == 'attendance')['edit'],
        isTrue,
      );
      expect(
        grants
            .where((g) => g['module'] != 'attendance')
            .every(
              (g) =>
                  g['read'] == false && g['add'] == false && g['edit'] == false,
            ),
        isTrue,
      );
      expect(find.text('حُفظت الصلاحيات وأُعيد التحقق منها.'), findsOneWidget);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Financial refund and exceptional discount are separate selectable actions',
    (t) async {
      final auth = await authenticate(
        'AcademyOwner',
        (m, p, b, token) async => access(),
      );
      await t.pumpWidget(
        app(
          NativeStaffAccessEditor(
            auth: auth,
            membershipId: 'admin',
            name: 'إداري عرض',
          ),
        ),
      );
      await t.pumpAndSettle();
      final subscription = find.widgetWithText(
        ExpansionTile,
        'الباقات والاشتراكات',
      );
      await revealTestTarget(t, subscription);
      await t.tap(subscription);
      await t.pumpAndSettle();
      final refund = find.widgetWithText(
        CheckboxListTile,
        'الاسترداد المالي للاشتراكات',
      );
      await revealTestTarget(t, refund);
      await t.tap(refund);
      await t.pumpAndSettle();
      expect(t.widget<CheckboxListTile>(refund).value, isTrue);
      await revealTestTarget(
        t,
        find.widgetWithText(CheckboxListTile, 'منح خصم استثنائي'),
      );
      expect(
        t
            .widget<CheckboxListTile>(
              find.widgetWithText(CheckboxListTile, 'منح خصم استثنائي'),
            )
            .value,
        isFalse,
      );
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('اعتماد الصلاحيات'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
