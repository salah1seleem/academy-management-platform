import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/owner/owner_action_rail.dart';
import 'package:academy_mobile/features/shared/owner_brand_navigation.dart';
import 'package:academy_mobile/features/shared/branded_header_surface.dart';
import 'package:academy_mobile/features/admin/native_enrollment.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

class BrandRailReadTransport extends HttpApiTransport {
  BrandRailReadTransport(super.config);
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    if (method != 'GET' && !path.contains('/auth/')) {
      throw StateError(
        'Read-only acceptance does not submit business commands.',
      );
    }
    return super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'real owner logo header, slow rail, central crest and larger summary watermark',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final auth = AuthController(
        BrandRailReadTransport(AppConfig('Demo', base)),
        SecureRefreshStore('v45.5278.os-media-entry.01099900010'),
      );
      await auth.restore();
      if (auth.session == null) {
        final challenge = await auth.requestOtp('01099900010');
        await auth.verify('01099900010', challenge, '246810');
      }
      expect(
        auth.session!.selected!.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      expect(auth.session!.selected!.role, 'AcademyOwner');
      final appearance = Appearance();
      await appearance.update(night: false);
      for (final sport in ['football', 'general']) {
        await appearance.completeRoleIntro('AcademyOwner', sport);
      }
      await t.pumpWidget(
        AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
      );
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 200 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(f, findsWidgets);
      }

      await wait(find.byType(OwnerActionRail));
      await wait(find.byKey(const ValueKey('summary-watermark')));
      expect(find.byType(BrandedHeaderSurface), findsOneWidget);
      final nav = find.byType(OwnerBrandNavigation);
      expect(t.widget<OwnerBrandNavigation>(nav).selected, 0);
      expect(
        t.getCenter(find.byKey(const ValueKey('owner-nav-0'))).dx,
        closeTo((t.view.physicalSize / t.view.devicePixelRatio).width / 2, 1),
      );
      final list = find.byKey(const ValueKey('owner-action-rail'));
      final controller = t.widget<ListView>(list).controller!;
      final before = controller.offset;
      for (var i = 0; i < 40; i++) {
        await t.pump(const Duration(milliseconds: 100));
      }
      expect(controller.offset - before, inInclusiveRange(30, 55));
      await binding.takeScreenshot('phase-approved-owner-brand-rail-top');
      // Stop autonomous motion for deterministic route/visual inspection.
      t.platformDispatcher.accessibilityFeaturesTestValue =
          const FakeAccessibilityFeatures(disableAnimations: true);
      await t.pumpAndSettle();
      controller.jumpTo(0);
      await t.pumpAndSettle();
      await t.tap(find.text('اشتراك جديد').first);
      await t.pumpAndSettle();
      await wait(find.byType(NativeNewEnrollment));
      await t.tap(find.byType(BackButton));
      await t.pumpAndSettle();
      await t.tap(find.byKey(const ValueKey('owner-nav-3')));
      await t.pumpAndSettle();
      expect(t.widget<OwnerBrandNavigation>(nav).selected, 3);
      await t.tap(find.byKey(const ValueKey('owner-nav-0')));
      await t.pumpAndSettle();
      await wait(find.byType(OwnerActionRail));
      final vertical = find
          .byWidgetPredicate(
            (w) => w is Scrollable && w.axisDirection == AxisDirection.down,
          )
          .first;
      await t.drag(vertical, const Offset(0, -360));
      await t.pumpAndSettle();
      await binding.takeScreenshot(
        'phase-approved-owner-brand-large-watermark',
      );
      await appearance.update(night: true);
      await t.pumpAndSettle();
      await binding.takeScreenshot(
        'phase-approved-owner-brand-large-watermark-dark',
      );
      final position = t.state<ScrollableState>(vertical).position;
      position.jumpTo(0);
      await t.pumpAndSettle();
      await binding.takeScreenshot('phase-approved-owner-brand-rail-top-dark');
      await appearance.update(night: false);
      await t.pumpAndSettle();
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      await t.pumpAndSettle();
      auth.dispose();
      t.platformDispatcher.clearAccessibilityFeaturesTestValue();
    },
  );
}
