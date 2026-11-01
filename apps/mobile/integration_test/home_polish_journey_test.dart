import 'startup_motion_journey_test.dart' show StartupReadTransport;

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/academy_brand.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/shared/experience_ui.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:academy_mobile/features/owner/owner_action_rail.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets('four real Homes: identity, light/dark and live action travel', (
    t,
  ) async {
    const base = String.fromEnvironment('API_BASE_URL');
    expect(base, 'http://127.0.0.1:5378');
    expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
    final api = StartupReadTransport(AppConfig('Demo', base));
    Future<void> tick(int ms) async {
      for (var n = 0; n < ms; n += 50) {
        await t.pump(const Duration(milliseconds: 50));
      }
    }

    Future<void> wait(Finder f) async {
      for (var n = 0; n < 400 && f.evaluate().isEmpty; n++) {
        await t.pump(const Duration(milliseconds: 100));
      }
      expect(f, findsWidgets);
    }

    final results = <String, Object?>{};
    for (final item in [
      ('AcademyOwner', '01084720300', 'owner'),
      ('AcademyAdmin', '01084720301', 'admin'),
      ('Coach', '01526189023', 'coach'),
      ('Guardian', '01526189023', 'guardian'),
    ]) {
      debugPrint('home-polish: mounting ${item.$3}');
      final auth = AuthController(
        api,
        SecureRefreshStore('home-polish-5378-${item.$2}'),
      );
      await auth.restore();
      if (auth.session == null) {
        final challenge = await auth.requestOtp(item.$2);
        await auth.verify(item.$2, challenge, '246810');
      }
      const academy = '507a3fde-42f9-43fa-bbbb-5a6009365d35';
      if (auth.session?.selected?.academyId != academy ||
          auth.session?.selected?.role != item.$1) {
        await auth.selectRole(
          auth.session!.memberships
              .firstWhere((m) => m.academyId == academy && m.role == item.$1)
              .id,
        );
      }
      // This is a Home-only visual rehearsal; first-use flow has separate evidence.
      final prefs = Appearance()..dark = false;
      prefs.bindIntroUser(auth.session!.userId);
      await prefs.completeRoleIntro(item.$1, 'neutral');
      auth.restoring = false;
      await t.pumpWidget(
        AcademyApp(
          key: ValueKey(item.$3),
          auth: auth,
          environment: 'Demo',
          appearance: prefs,
        ),
      );
      await wait(find.byType(NativeRoleTabs));
      await wait(
        item.$3 == 'owner'
            ? find.text('305')
            : item.$3 == 'admin'
            ? find.text('100')
            : item.$3 == 'coach'
            ? find.textContaining('مجموعة الكرة 01')
            : find.text('لاعب خيالي 001'),
      );
      await wait(find.byType(OwnerActionRail));
      await tick(1700);
      final brand = BrandScope.maybeOf(
        t.element(find.byType(ExperienceHeader)),
      )!;
      expect(brand.logo, isNotNull);
      expect(brand.artwork, isNotNull);
      final zone = t.getRect(
        find.byKey(const ValueKey('home-header-watermark-zone')),
      );
      final header = t.getRect(
        find.byKey(const ValueKey('academy-branded-header')),
      );
      expect(zone.center.dx, closeTo(header.center.dx, 1));
      expect(
        t.getSize(find.byKey(const ValueKey('home-primary-crest'))).width,
        52,
      );
      final controller = t
          .widget<ListView>(find.byKey(const ValueKey('owner-action-rail')))
          .controller!;
      final start = controller.offset;
      await binding.takeScreenshot('phase-home-${item.$3}-light');
      await tick(3200);
      final end = controller.offset;
      if (controller.position.maxScrollExtent > 0) expect(end, isNot(start));
      results[item.$3] = {
        'start': start,
        'end': end,
        'extent': controller.position.maxScrollExtent,
        'colors': brand.artwork!.colors.map((c) => c.toARGB32()).toList(),
      };
      await binding.takeScreenshot('phase-home-${item.$3}-motion');
      await prefs.update(night: true);
      await tick(600);
      await binding.takeScreenshot('phase-home-${item.$3}-dark');
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
      prefs.dispose();
    }
    debugPrint('home-polish: motion results $results');
    binding.reportData ??= <String, dynamic>{};
    binding.reportData!['source'] = 'Home-only rehearsal, real API 5378, four authorized synthetic demo roles; no business writes; light/dark and motion observed; not a first-use or physical-device test';
    binding.reportData!['homeMotion'] = results;
  });
}
