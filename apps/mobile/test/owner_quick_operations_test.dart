import 'package:academy_mobile/features/owner/owner_quick_operations.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as fixtures;

void main() {
  testWidgets(
    'owner Operations opens existing secure workflows without tokens',
    (t) async {
      final auth = await fixtures.authenticate(
        'AcademyOwner',
        (m, p, b, token) async => [],
      );
      final opened = <Uri>[];
      await t.pumpWidget(
        fixtures.app(
          OwnerQuickOperations(
            auth: auth,
            webBaseUrl: 'https://academy.example.test',
            openDashboard: (uri) async {
              opened.add(uri);
              return true;
            },
          ),
        ),
      );
      await t.pumpAndSettle();
      for (final title in [
        'اشتراك جديد · تسجيل لاعب',
        'تجديد اشتراك',
        'إضافة مدرب',
        'إنشاء تقييم',
      ]) {
        await t.ensureVisible(find.text(title));
        await t.tap(find.text(title));
        await t.pumpAndSettle();
      }
      expect(opened.map((u) => u.toString()), [
        'https://academy.example.test/dashboard/players/new',
        'https://academy.example.test/dashboard/subscriptions/renew/new',
        'https://academy.example.test/dashboard/academy/coaches/create',
        'https://academy.example.test/dashboard/evaluations/new',
      ]);
      expect(
        opened.every(
          (u) => u.query.isEmpty && u.userInfo.isEmpty && u.fragment.isEmpty,
        ),
        isTrue,
      );
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'navigation adds Operations without replacing matches reports or more',
    (t) async {
      final auth = await fixtures.authenticate(
        'AcademyOwner',
        (m, p, b, token) async => [],
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
      expect(find.text('اشتراك جديد · تسجيل لاعب'), findsOneWidget);
      expect(
        t.widget<NavigationBar>(find.byType(NavigationBar)).selectedIndex,
        1,
      );
      await t.tap(find.text('الرئيسية'));
      await t.pumpAndSettle();
      expect(find.text('الرئيسية الأصلية'), findsOneWidget);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'blocked launcher is visible and HTTP is forbidden in production',
    (t) async {
      final auth = await fixtures.authenticate(
        'AcademyOwner',
        (m, p, b, token) async => [],
      );
      var called = false;
      await t.pumpWidget(
        fixtures.app(
          OwnerQuickOperations(
            auth: auth,
            webBaseUrl: 'http://academy.example.test',
            openDashboard: (uri) async {
              called = true;
              return true;
            },
          ),
        ),
      );
      await t.tap(find.text('اشتراك جديد · تسجيل لاعب'));
      await t.pumpAndSettle();
      expect(called, isFalse);
      expect(find.textContaining('تعذر فتح لوحة الويب'), findsOneWidget);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets('no owner operations after logout or for another role', (
    t,
  ) async {
    for (final role in ['Coach', 'Guardian', 'AcademyAdmin', 'AcademyOwner']) {
      final auth = await fixtures.authenticate(
        role,
        (m, p, b, token) async => [],
      );
      await t.pumpWidget(
        fixtures.app(OwnerQuickOperations(key: ValueKey(role), auth: auth)),
      );
      await t.pumpAndSettle();
      expect(
        find.text('اشتراك جديد · تسجيل لاعب'),
        role == 'AcademyOwner' ? findsOneWidget : findsNothing,
      );
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('اشتراك جديد · تسجيل لاعب'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    }
  });
}
