import 'package:academy_mobile/features/owner/owner_quick_operations.dart';
import 'package:academy_mobile/features/admin/admin_home.dart';
import 'package:academy_mobile/features/admin/native_enrollment.dart';
import 'package:academy_mobile/features/admin/native_renewal.dart';
import 'package:academy_mobile/features/admin/native_structure_operations.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:academy_mobile/features/shared/owner_brand_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as fixtures;

Object source(String path) {
  if (path.contains('/structure/options')) {
    return {
      'groups': [
        {'id': 'group', 'arabicName': 'مجموعة اصطناعية'},
      ],
      'coaches': [
        {'id': 'coach', 'arabicName': 'مدرب اصطناعي'},
      ],
      'sports': [],
      'branches': [],
      'categories': [],
    };
  }
  if (path.endsWith('/discount-rules/')) return {'items': []};
  if (path.contains('/evaluations/options')) return {'enrollments': []};
  if (path.endsWith('/my-access')) return {};
  return [];
}

void main() {
  testWidgets(
    'all core operations use native pages and never invoke the browser',
    (t) async {
      final auth = await fixtures.authenticate(
        'AcademyOwner',
        (m, p, b, token) async => source(p),
      );
      var browserCalls = 0;
      await t.pumpWidget(
        fixtures.app(
          OwnerQuickOperations(
            auth: auth,
            openDashboard: (_) async {
              browserCalls++;
              return true;
            },
          ),
        ),
      );
      await t.pumpAndSettle();
      for (final action in [
        ('اشتراك جديد', NativeNewEnrollment),
        ('تجديد اشتراك', NativeAdminRenewal),
        ('إضافة مدرب', NativeCoachCreate),
        ('إنشاء تقييم', NativeEvaluationPicker),
        ('تسجيل الحضور', NativeSessions),
        ('إسناد مدرب لمجموعة', NativeCoachAssignment),
        ('تسكين لاعب', NativeGroupPicker),
        ('تعديل مجموعة', NativeGroupPicker),
      ]) {
        final scroll = find
            .byWidgetPredicate(
              (w) =>
                  w is Scrollable &&
                  (w.axisDirection == AxisDirection.down ||
                      w.axisDirection == AxisDirection.up),
            )
            .first;
        t.state<ScrollableState>(scroll).position.jumpTo(0);
        await t.pumpAndSettle();
        await t.scrollUntilVisible(
          find.text(action.$1),
          180,
          scrollable: find.byType(Scrollable).first,
        );
        await t.ensureVisible(find.text(action.$1));
        await t.tap(find.text(action.$1));
        await t.pumpAndSettle();
        expect(find.byType(action.$2), findsOneWidget);
        await t.tap(
          find.byTooltip(
            MaterialLocalizations.of(t.element(find.byType(action.$2)))
                .backButtonTooltip,
          ),
        );
        await t.pumpAndSettle();
      }
      expect(browserCalls, 0);
      expect(find.textContaining('لوحة الويب'), findsNothing);
      expect(find.text('التقرير المالي'), findsNothing);
      expect(find.text('طلبات الدفع'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'five owner destinations retain Operations alongside matches reports and more',
    (t) async {
      final auth = await fixtures.authenticate(
        'AcademyOwner',
        (m, p, b, token) async => source(p),
      );
      await t.pumpWidget(
        fixtures.app(
          NativeRoleTabs(
            auth: auth,
            role: 'AcademyOwner',
            home: const Text('الرئيسية الأصلية'),
            account: const Text('الحساب'),
          ),
        ),
      );
      await t.tap(find.text('العمليات'));
      await t.pumpAndSettle();
      expect(find.byType(OwnerQuickOperations), findsOneWidget);
      expect(find.text('اشتراك جديد'), findsOneWidget);
      expect(
        t
            .widgetList<Semantics>(
              find.descendant(
                of: find.byType(OwnerBrandNavigation),
                matching: find.byType(Semantics),
              ),
            )
            .where((s) => s.properties.button == true)
            .map((s) => s.properties.label),
        ['العمليات', 'المباريات', 'الرئيسية', 'التقارير', 'المزيد'],
      );
      await t.tap(find.text('الرئيسية'));
      await t.pumpAndSettle();
      expect(find.text('الرئيسية الأصلية'), findsOneWidget);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'attendance-only administrator sees no financial or player creation actions',
    (t) async {
      final auth = await fixtures.authenticate('AcademyAdmin', (
        m,
        p,
        b,
        token,
      ) async {
        if (p.endsWith('/my-permissions')) return ['attendance.manage'];
        if (p.endsWith('/my-access')) {
          return {
            'useActionGrants': true,
            'actions': [
              {'module': 'attendance', 'read': true, 'edit': true},
            ],
            'workflowActions': [
              {
                'key': 'workflow.attendance.players',
                'hasExplicitGrant': true,
                'allowed': true,
              },
            ],
          };
        }
        return source(p);
      });
      await t.pumpWidget(fixtures.app(OwnerQuickOperations(auth: auth)));
      await t.pumpAndSettle();
      expect(find.text('تسجيل الحضور'), findsOneWidget);
      expect(find.text('اشتراك جديد'), findsNothing);
      expect(find.text('تجديد اشتراك'), findsNothing);
      expect(find.text('إضافة مدرب'), findsNothing);
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('تسجيل الحضور'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets('non-management roles cannot see operational commands', (
    t,
  ) async {
    for (final role in ['Coach', 'Guardian']) {
      final auth = await fixtures.authenticate(
        role,
        (m, p, b, token) async => source(p),
      );
      await t.pumpWidget(
        fixtures.app(OwnerQuickOperations(key: ValueKey(role), auth: auth)),
      );
      await t.pumpAndSettle();
      expect(find.text('اشتراك جديد'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    }
  });
}
