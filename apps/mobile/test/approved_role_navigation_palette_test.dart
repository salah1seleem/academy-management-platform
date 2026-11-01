import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/shared/experience_ui.dart';
import 'package:academy_mobile/features/shared/owner_brand_navigation.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as f;

ThemeData modelTheme(VisualModel m) => switch (m) {
  VisualModel.light => AcademyTheme.light(),
  VisualModel.dark => AcademyTheme.dark(),
  _ => AcademyTheme.themed(m),
};
void main() {
  for (final role in ['AcademyOwner', 'AcademyAdmin', 'Coach', 'Guardian']) {
    for (final model in VisualModel.values) {
      for (final width in [375.0, 440.0]) {
        testWidgets('V5.14 navigation $role $model $width at 200 percent', (
          t,
        ) async {
          t.view.physicalSize = Size(width, 956);
          t.view.devicePixelRatio = 1;
          addTearDown(t.view.reset);
          final auth = await f.authenticate(
            role,
            (m, p, b, k) async => p.contains('/guardian/home')
                ? {'children': []}
                : p.endsWith('/my-access')
                ? <String, dynamic>{}
                : [],
          );
          await t.pumpWidget(
            MaterialApp(
              locale: const Locale('ar'),
              supportedLocales: const [Locale('ar')],
              localizationsDelegates: GlobalMaterialLocalizations.delegates,
              theme: modelTheme(model),
              builder: (context, child) => MediaQuery(
                data: MediaQuery.of(context).copyWith(
                  textScaler: const TextScaler.linear(2),
                  disableAnimations: true,
                ),
                child: child!,
              ),
              home: NativeRoleTabs(
                auth: auth,
                role: role,
                home: const Scaffold(body: Text('رئيسية الدور')),
                account: const Scaffold(body: Text('حسابي')),
              ),
            ),
          );
          await t.pumpAndSettle();
          expect(find.byType(ExperienceHeader), findsOneWidget);
          expect(find.byTooltip('شرح الصفحة بالفيديو'), findsNothing);
          expect(
            find.byKey(const ValueKey('home-primary-crest')),
            findsOneWidget,
          );
          if (role == 'AcademyOwner') {
            final home = find.byKey(const ValueKey('owner-nav-0'));
            expect(t.getCenter(home).dx, closeTo(width / 2, 1));
            final first = t
                .getTopLeft(find.byKey(const ValueKey('owner-nav-1')))
                .dx;
            expect(
              first,
              greaterThan(
                t.getTopLeft(find.byKey(const ValueKey('owner-nav-4'))).dx,
              ),
            );
            await t.tap(find.byKey(const ValueKey('owner-nav-2')));
            await t.pumpAndSettle();
            expect(
              t
                  .widget<OwnerBrandNavigation>(
                    find.byType(OwnerBrandNavigation),
                  )
                  .selected,
              2,
            );
            await t.tap(home);
            await t.pumpAndSettle();
          } else {
            final bar = t.widget<NavigationBar>(find.byType(NavigationBar));
            expect(bar.destinations.length, 4);
            expect(bar.selectedIndex, 0);
            final destinations = bar.destinations
                .cast<NavigationDestination>()
                .toList();
            expect(
              (destinations.first.selectedIcon as Icon).icon,
              Icons.home_rounded,
            );
            final matches = destinations.indexWhere(
              (d) => d.label == 'المباريات',
            );
            if (matches >= 0) {
              await t.tap(
                find.descendant(
                  of: find.byType(NavigationBar),
                  matching: find.text('المباريات'),
                ),
              );
              await t.pumpAndSettle();
              expect(
                t
                    .widget<NavigationBar>(find.byType(NavigationBar))
                    .selectedIndex,
                matches,
              );
              await t.tap(
                find.descendant(
                  of: find.byType(NavigationBar),
                  matching: find.text('الرئيسية'),
                ),
              );
              await t.pumpAndSettle();
            }
            final destination = find.byType(NavigationDestination).first;
            expect(t.getSize(destination).width, greaterThanOrEqualTo(44));
            expect(t.getSize(destination).height, greaterThanOrEqualTo(44));
          }
          expect(find.text('رئيسية الدور'), findsOneWidget);
          expect(t.takeException(), isNull);
          await t.pumpWidget(const SizedBox());
          auth.dispose();
        });
      }
    }
  }
}
