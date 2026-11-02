import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as fixtures;

void main() {
  test('approved palettes keep readable accents and offline Arabic font', () {
    for (final theme in [AcademyTheme.dark(), AcademyTheme.light()]) {
      expect(theme.textTheme.bodyMedium!.fontFamily, 'AcademyArabic');
      expect(
        (theme.cardTheme.shape! as RoundedRectangleBorder).borderRadius,
        BorderRadius.circular(22),
      );
    }
    expect(
      AcademyTheme.dark().scaffoldBackgroundColor,
      const Color(0xFF10479D),
    );
    expect(
      AcademyTheme.light().scaffoldBackgroundColor,
      const Color(0xFFF3F6FA),
    );
    final custom = AcademyTheme.dark(primary: const Color(0xFF127A68));
    final c = custom.colorScheme.primary;
    final l = [
      c.computeLuminance(),
      custom.colorScheme.surface.computeLuminance(),
    ]..sort();
    expect((l.last + .05) / (l.first + .05), greaterThanOrEqualTo(4.5));
    expect(c.g, greaterThan(c.r));
  });
  testWidgets(
    'persistent header opens the current tab help and account without onboarding',
    (t) async {
      final paths = <String>[];
      final auth = await fixtures.authenticate('AcademyOwner', (
        m,
        p,
        b,
        token,
      ) async {
        paths.add(p);
        if (p == '/api/v1/notifications') return {'unread': 2, 'items': []};
        if (p.startsWith('/api/v1/page-help/')) {
          return {
            'available': false,
            'role': 'AcademyOwner',
            'screen': p.split('/').last,
            'url': null,
          };
        }
        return [];
      });
      await t.pumpWidget(
        fixtures.app(
          NativeRoleTabs(
            auth: auth,
            role: 'AcademyOwner',
            home: const Text('رئيسية فعلية'),
            account: const Scaffold(body: Text('حساب العضوية')),
          ),
        ),
      );
      await t.pumpAndSettle();
      for (final tab in ['العمليات', 'المزيد', 'الرئيسية']) {
        await t.tap(find.text(tab).last);
        await t.pumpAndSettle();
        expect(find.byTooltip('حسابي'), findsOneWidget);
        expect(find.byTooltip('التنبيهات'), findsOneWidget);
        expect(find.byTooltip('شرح الصفحة بالفيديو'), findsNothing);
        expect(paths.any((p) => p.startsWith('/api/v1/page-help/')), isFalse);
      }
      await t.tap(find.byTooltip('حسابي'));
      await t.pumpAndSettle();
      expect(find.text('حساب العضوية'), findsOneWidget);
      expect(paths.any((p) => p.contains('onboarding')), isFalse);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
