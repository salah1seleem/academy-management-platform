import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/core/theme/academy_brand.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/experience_ui.dart';
import 'package:academy_mobile/features/shared/page_video.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as f;

void main() {
  for (final light in [true, false]) {
    for (final width in [375.0, 440.0]) {
      testWidgets(
        'one root header retains page actions and pushed route help, light=$light width=$width',
        (t) async {
          t.view.physicalSize = Size(width, 956);
          t.view.devicePixelRatio = 1;
          addTearDown(t.view.resetPhysicalSize);
          addTearDown(t.view.resetDevicePixelRatio);
          final auth = await f.authenticate(
            'AcademyOwner',
            (m, p, b, token) async => [],
          );
          final brand = AcademyBrand(auth);
          await brand.refresh(force: true);
          var actions = 0;
          await t.pumpWidget(
            MaterialApp(
              locale: const Locale('ar'),
              supportedLocales: const [Locale('ar')],
              localizationsDelegates: GlobalMaterialLocalizations.delegates,
              theme: light ? AcademyTheme.light() : AcademyTheme.dark(),
              builder: (context, child) => MediaQuery(
                data: MediaQuery.of(context)
                    .copyWith(textScaler: const TextScaler.linear(2)),
                child: BrandScope(brand: brand, child: child!),
              ),
              home: NativeRoleTabs(
                auth: auth,
                role: 'AcademyOwner',
                account: const Scaffold(),
                home: Builder(
                  builder: (context) => GuardianPage(
                    title: 'مساحة عمل اللاعب والتفاصيل المحفوظة للأكاديمية',
                    actions: [
                      IconButton(
                        tooltip: 'إجراء الصفحة',
                        onPressed: () => actions++,
                        icon: const Icon(Icons.add),
                      ),
                      PageVideoButton(auth: auth, screen: 'home'),
                    ],
                    child: TextButton(
                      onPressed: () => openPage(
                        context,
                        GuardianPage(
                          title: 'تفاصيل سجل محفوظ',
                          actions: [
                            PageVideoButton(auth: auth, screen: 'players'),
                          ],
                          child: const Text('محتوى محفوظ'),
                        ),
                      ),
                      child: const Text('فتح التفاصيل'),
                    ),
                  ),
                ),
              ),
            ),
          );
          await t.pumpAndSettle();
          expect(find.byType(ExperienceHeader), findsOneWidget);
          expect(find.byType(CanonicalAcademyLogo), findsOneWidget);
          expect(find.byType(AppBar), findsNothing);
          expect(find.byTooltip('شرح الصفحة بالفيديو'), findsNothing);
          await t.tap(find.byTooltip('إجراء الصفحة'));
          expect(actions, 1);
          await t.tap(find.text('فتح التفاصيل'));
          await t.pumpAndSettle();
          expect(find.text('محتوى محفوظ'), findsOneWidget);
          expect(find.byType(AppBar), findsOneWidget);
          expect(find.byType(BackButton), findsOneWidget);
          expect(find.byType(CanonicalAcademyLogo), findsOneWidget);
          expect(find.byTooltip('شرح الصفحة بالفيديو'), findsNothing);
          expect(find.byType(ExperienceHeader), findsOneWidget);
          await t.tap(find.byType(BackButton));
          await t.pumpAndSettle();
          expect(find.byTooltip('إجراء الصفحة'), findsOneWidget);
          expect(find.byType(ExperienceHeader), findsOneWidget);
          expect(t.takeException(), isNull);
          await t.pumpWidget(const SizedBox());
          brand.dispose();
          auth.dispose();
        },
      );
    }
  }
}
