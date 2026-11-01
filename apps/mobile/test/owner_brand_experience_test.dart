import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:academy_mobile/core/theme/academy_brand.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/core/theme/logo_palette.dart';
import 'package:academy_mobile/features/owner/owner_action_rail.dart';
import 'package:academy_mobile/features/owner/owner_quick_operations.dart';
import 'package:academy_mobile/features/admin/native_enrollment.dart';
import 'package:academy_mobile/features/admin/native_renewal.dart';
import 'package:academy_mobile/features/admin/native_structure_operations.dart';
import 'package:academy_mobile/features/shared/branded_header_surface.dart';
import 'package:academy_mobile/features/shared/owner_brand_navigation.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

import 'v2_role_workspaces_test.dart' as f;

Widget railApp(
  Widget child, {
  bool reduced = false,
  double scale = 1,
  bool dark = false,
}) => MaterialApp(
  locale: const Locale('ar'),
  supportedLocales: const [Locale('ar')],
  localizationsDelegates: GlobalMaterialLocalizations.delegates,
  theme: dark ? AcademyTheme.dark() : AcademyTheme.light(),
  builder: (context, child) => MediaQuery(
    data: MediaQuery.of(context).copyWith(
      disableAnimations: reduced,
      textScaler: TextScaler.linear(scale),
    ),
    child: child!,
  ),
  home: Scaffold(
    body: Directionality(
      textDirection: TextDirection.rtl,
      child: SingleChildScrollView(child: child),
    ),
  ),
);

void main() {
  test('dominant logo accent ignores white, black and transparent pixels; bright colors retain white contrast', () {
    final red = Uint8List.fromList([
      for (var i = 0; i < 100; i++) ...[255, 255, 255, 255],
      for (var i = 0; i < 50; i++) ...[0, 0, 0, 255],
      for (var i = 0; i < 40; i++) ...[220, 15, 35, 255],
      for (var i = 0; i < 10; i++) ...[10, 70, 220, 255],
      for (var i = 0; i < 100; i++) ...[10, 220, 70, 0],
    ]);
    expect(accentFromRgba(red), const Color(0xFFDC0F23));
    expect(
      accentFromRgba(Uint8List.fromList([255, 255, 255, 255, 0, 0, 0, 255])),
      isNull,
    );
    for (final color in [
      Colors.yellow,
      Colors.red,
      Colors.cyan,
      Colors.green,
      Colors.white,
    ]) {
      expect(
        1.05 / (headerTone(color).computeLuminance() + .05),
        greaterThanOrEqualTo(4.5),
      );
    }
  });
  testWidgets(
    'two tenant headers change with the logo accent and reset on logout',
    (t) async {
      final auth = await f.authenticate(
        'AcademyOwner',
        (_, _, _, _) async => {},
      );
      final brand = AcademyBrand(auth);
      await brand.refresh(force: true);
      for (final color in [const Color(0xFFDA1535), const Color(0xFF087D71)]) {
        await t.pumpWidget(const SizedBox());
        brand.logoColor = color;
        await t.pumpWidget(
          f.app(
            BrandScope(
              brand: brand,
              child: const BrandedHeaderSurface(
                child: Text('رئيسية الأكاديمية'),
              ),
            ),
          ),
        );
        final surface = t.widget<Container>(
          find.byKey(const ValueKey('academy-branded-header')),
        );
        final tint = (surface.decoration as BoxDecoration).color!;
        expect(tint.computeLuminance(), greaterThan(.7));
        expect(tint, AcademyTheme.light().scaffoldBackgroundColor);
      }
      await auth.logout();
      expect(brand.logoColor, isNull);
      await t.pumpWidget(const SizedBox());
      brand.dispose();
      auth.dispose();
    },
  );
  testWidgets(
    'quick actions remain fixed while idle and retain one active target',
    (t) async {
      var taps = 0;
      await t.pumpWidget(
        railApp(
          OwnerActionRail(
            actions: [
              for (var i = 0; i < 7; i++)
                OwnerRailAction(
                  'عملية $i',
                  Icons.person_add,
                  Colors.red,
                  () => taps++,
                ),
            ],
          ),
        ),
      );
      await t.pumpAndSettle();
      final start = t.getTopLeft(find.text('عملية 0'));
      await t.pump(const Duration(seconds: 10));
      expect(t.getTopLeft(find.text('عملية 0')), start);
      await t.tap(find.text('عملية 1'));
      expect(taps, 1);
      await t.tap(find.text('كل الإجراءات · 7'));
      await t.pumpAndSettle();
      for (var i = 0; i < 7; i++) {
        expect(find.text('عملية $i'), findsOneWidget);
      }
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
    },
  );
  for (final width in [375.0, 440.0]) {
    for (final dark in [false, true]) {
      testWidgets(
        'center crest returns home and owner actions retain native routes $width dark=$dark',
        (t) async {
          t.view.physicalSize = Size(width, 956);
          t.view.devicePixelRatio = 1;
          addTearDown(t.view.reset);
          final auth = await f.authenticate('AcademyOwner', (
            m,
            p,
            b,
            token,
          ) async {
            if (p == '/api/v1/notifications') return {'unread': 0};
            if (p.contains('/structure/options')) {
              return {
                'branches': [],
                'sports': [],
                'groups': [],
                'coaches': [],
              };
            }
            if (p.contains('/manage/employees')) return {'items': []};
            if (p.contains('/discount-rules/')) return {'items': <Object>[]};
            if (p.contains('/manage/staff')) return {'members': []};
            return [];
          });
          await t.pumpWidget(
            f.app(
              Theme(
                data: dark ? AcademyTheme.dark() : AcademyTheme.light(),
                child: NativeRoleTabs(
                  auth: auth,
                  role: 'AcademyOwner',
                  home: const Text('رئيسية اختباري'),
                  account: const SizedBox(),
                ),
              ),
              scale: 2,
            ),
          );
          await t.pumpAndSettle();
          final semantics = t.ensureSemantics();
          expect(
            t
                .getSemantics(find.bySemanticsLabel('الرئيسية'))
                .getSemanticsData()
                .hasAction(ui.SemanticsAction.tap),
            isTrue,
          );
          semantics.dispose();
          final home = find.byKey(const ValueKey('owner-nav-0'));
          expect(t.getCenter(home).dx, closeTo(width / 2, 1));
          for (final tab in [1, 2, 3, 4]) {
            await t.tap(find.byKey(ValueKey('owner-nav-$tab')));
            await t.pumpAndSettle();
            expect(
              t
                  .widget<OwnerBrandNavigation>(
                    find.byType(OwnerBrandNavigation),
                  )
                  .selected,
              tab,
            );
            await t.tap(home);
            await t.pumpAndSettle();
            expect(find.text('رئيسية اختباري'), findsOneWidget);
          }
          expect(t.takeException(), isNull);
          await t.pumpWidget(
            railApp(
              NativeOperationActions(auth: auth, compact: true, carousel: true),
              reduced: true,
              scale: 2,
              dark: dark,
            ),
          );
          await t.pumpAndSettle();
          final routes = <String, Type>{
            'اشتراك جديد': NativeNewEnrollment,
            'تجديد اشتراك': NativeAdminRenewal,
            'إضافة مدرب': NativeCoachCreate,
            'إسناد مدرب لمجموعة': NativeCoachAssignment,
          };
          for (final route in routes.entries) {
            final item = find.text(route.key);
            if (item.evaluate().isEmpty &&
                find.textContaining('كل الإجراءات').evaluate().isNotEmpty) {
              await t.ensureVisible(find.textContaining('كل الإجراءات'));
              await t.tap(find.textContaining('كل الإجراءات'));
              await t.pumpAndSettle();
            }
            await t.scrollUntilVisible(
              item,
              100,
              scrollable: find.byType(Scrollable).first,
              maxScrolls: 20,
            );
            await t.tap(item);
            await t.pumpAndSettle();
            expect(find.byType(route.value), findsOneWidget);
            expect(find.byType(BackButton), findsOneWidget, reason: route.key);
            await t.tap(find.byType(BackButton));
            await t.pumpAndSettle();
          }
          expect(t.takeException(), isNull);
          await t.pumpWidget(const SizedBox());
          auth.dispose();
        },
      );
    }
  }
  for (final role in ['Coach', 'Guardian', 'AcademyAdmin']) {
    testWidgets('$role cannot receive the owner-only permissions shortcut', (
      t,
    ) async {
      final auth = await f.authenticate(
        role,
        (m, p, b, token) async =>
            p.endsWith('/my-permissions') ? [] : <String, dynamic>{},
      );
      await t.pumpWidget(
        f.app(
          NativeOperationActions(auth: auth, carousel: true, compact: true),
        ),
      );
      await t.pumpAndSettle();
      expect(find.byType(OwnerActionRail), findsNothing);
      expect(find.text('الموظفون والصلاحيات'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    });
  }
}
