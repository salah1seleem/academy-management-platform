import 'package:academy_mobile/core/theme/academy_brand.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as fixtures;

void main() {
  for (final role in ['AcademyOwner', 'AcademyAdmin', 'Coach', 'Guardian']) {
    testWidgets(
      'competition navigation follows the academy capability for $role without deleting an enabled empty experience',
      (t) async {
        final auth = await fixtures.authenticate(role, (m, p, b, token) async {
          if (p.endsWith('/experience')) {
            return {
              'onboarding': {'features': <String>[]},
            };
          }
          if (p.endsWith('/my-access')) return <String, dynamic>{};
          if (p.endsWith('/my-match-access')) {
            return {'canRead': true, 'canAdd': true, 'canEdit': true};
          }
          if (p.endsWith('/my-permissions')) return ['structure.manage'];
          return [];
        });
        final brand = AcademyBrand(auth);
        await t.pump();
        await t.pumpWidget(
          fixtures.app(
            BrandScope(
              brand: brand,
              child: NativeRoleTabs(
                auth: auth,
                role: role,
                home: const SizedBox(),
                account: const SizedBox(),
              ),
            ),
          ),
        );
        await t.pumpAndSettle();
        expect(find.text('المباريات'), findsNothing);
        brand.features = ['matches'];
        brand.notifyListeners();
        await t.pumpAndSettle();
        if (role != 'AcademyAdmin') {
          expect(find.text('المباريات'), findsOneWidget);
        }
        if (role == 'AcademyAdmin') {
          await t.tap(find.text('المزيد'));
          await t.pumpAndSettle();
          expect(find.text('إدارة المباريات والنتائج'), findsOneWidget);
        }
        brand.features = [];
        brand.notifyListeners();
        await t.pumpAndSettle();
        expect(find.text('المباريات'), findsNothing);
        expect(find.text('إدارة المباريات والنتائج'), findsNothing);
        await t.pumpWidget(const SizedBox());
        brand.dispose();
        auth.dispose();
      },
    );
  }
}
