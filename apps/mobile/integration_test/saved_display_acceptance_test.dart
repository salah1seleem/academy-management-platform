import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/guardian/guardian_content.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/guardian/sport_store.dart';
import 'package:academy_mobile/features/owner/native_academy_import.dart';
import 'package:academy_mobile/features/shared/private_media.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:video_player/video_player.dart';

import 'v45_single_renewal_saved_receipt_test.dart'
    show SavedRenewalReadTransport;

class SavedDisplayAppearance extends Appearance {
  @override
  Future<bool> hasRoleIntro(String role, String sportTheme) async => true;
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'saved photo, import, family media and order at440 without writes',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 440);
      t.platformDispatcher.accessibilityFeaturesTestValue =
          const FakeAccessibilityFeatures(disableAnimations: true);
      addTearDown(t.platformDispatcher.clearAccessibilityFeaturesTestValue);
      final transport = SavedRenewalReadTransport(AppConfig('Demo', base));
      final appearance = SavedDisplayAppearance();
      final controllers = <AuthController>[];
      Future<AuthController> login(String role, String phone) async {
        final a = AuthController(
          transport,
          SecureRefreshStore('v73.5278.approved-intro.$role'),
        );
        controllers.add(a);
        await a.restore();
        if (a.session == null) {
          final c = await a.requestOtp(phone);
          await a.verify(phone, c, '246810');
        }
        expect(a.session!.selected!.role, role);
        expect(
          a.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return a;
      }

      Future<void> wait(Finder f) async {
        for (var i = 0; i < 240 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> reach(Finder f) async {
        if (f.evaluate().isEmpty) {
          final scroll = find.byType(Scrollable).first;
          t.state<ScrollableState>(scroll).position.jumpTo(0);
          await t.pumpAndSettle();
          await t.scrollUntilVisible(
            f,
            160,
            scrollable: scroll,
            maxScrolls: 100,
          );
        }
        await t.ensureVisible(f.first);
        await t.pumpAndSettle();
      }

      Future<void> capture(String name) async {
        for (var i = 0; i < 150; i++) {
          if (t
              .widgetList<RawImage>(find.byType(RawImage))
              .every((im) => im.image != null)) {
            break;
          }
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(
          t
              .widgetList<RawImage>(find.byType(RawImage))
              .every((im) => im.image != null),
          true,
        );
        expect(t.takeException(), isNull);
        await binding.takeScreenshot('phase-approved-saved-display-440-$name');
      }

      Future<void> app(AuthController auth, bool dark) async {
        appearance.dark = dark;
        await t.pumpWidget(const SizedBox.shrink());
        await t.pumpAndSettle();
        await t.pumpWidget(
          AcademyApp(auth: auth, appearance: appearance, environment: 'Demo'),
        );
        await wait(find.byType(NativeRoleTabs));
      }

      Future<void> open(Widget page) async {
        // The actual production page is mounted on the app's own Navigator.
        // Entry navigation has separate acceptance; this covers saved contents.
        openPage(t.element(find.byType(NativeRoleTabs)), page);
        await t.pump();
      }

      try {
        final owner = await login('AcademyOwner', '01099900010');
        final money = obj(
          await owner.request(
            'GET',
            '/api/v1/reports/financial?allDates=true&pageSize=1',
          ),
        );
        const player = '90445c63-4f72-9320-a1d1-aa99990f6d02';
        final saved = obj(
          await owner.request('GET', '/api/v1/people/players/$player'),
        );
        // The earlier upload acceptance deliberately restored the original null
        // reference. Do not reattach the retained asset for a screenshot.
        expect(saved['photoReference'], isNull);
        await app(owner, false);
        await open(NativePlayerWorkspace(auth: owner, id: player));
        await wait(find.text(textOf(saved['arabicName'])));
        await wait(find.text('تغيير صورة اللاعب'));
        await capture('player-original-profile-light');

        await app(owner, false);
        final jobs = rows(
          await owner.request('GET', '/api/v1/people/import/jobs'),
        );
        expect(jobs, isNotEmpty);
        expect(jobs.first['status'], 'Completed');
        await open(NativeAcademyImport(auth: owner));
        await wait(find.text('اكتمل الاستيراد'));
        await reach(find.text('اكتمل الاستيراد'));
        await capture('import-summary-light');

        final family = await login('Guardian', '01099900100');
        const sale = '53edec50-6ba6-4d60-8c67-af755bb52b4a';
        final order = obj(
          await family.request('GET', '/api/v1/guardian/product-sales/$sale'),
        );
        expect(order['status'], 'Confirmed');
        expect(order['sizeSnapshot'], 'M');
        expect(order['shirtNumberSnapshot'], 7);
        await app(family, true);
        await open(GuardianProductOrder(auth: family, id: sale));
        await wait(find.text('رقم القميص'));
        expect(find.text('7'), findsWidgets);
        expect(find.text('M'), findsWidgets);
        await capture('confirmed-product-dark');

        final children = rows(
          obj(await family.request('GET', '/api/v1/guardian/home'))['children'],
        );
        String? child;
        Json? photo, video;
        for (final c in children) {
          final gallery = rows(
            await family.request(
              'GET',
              '/api/v1/assets/gallery?playerId=${c['id']}',
            ),
          );
          photo = gallery
              .where((m) => m['id'] == 'a63e8a27-0acc-47bd-98bc-7012304e08e6')
              .firstOrNull;
          video = gallery
              .where((m) => m['id'] == 'fe9d2e68-58c6-4321-84c7-a0d5abc0663f')
              .firstOrNull;
          if (photo != null && video != null) {
            child = textOf(c['id']);
            break;
          }
        }
        expect(
          child,
          isNotNull,
          reason: 'Reuse the previously published assets.',
        );
        await app(family, true);
        await open(GalleryPage(auth: family, playerId: child!));
        final photoTile = find.byWidgetPredicate(
          (w) => w is MediaTile && w.media['id'] == photo!['id'],
        );
        await reach(photoTile);
        await capture('saved-gallery-dark');
        final videoTile = find.byWidgetPredicate(
          (w) => w is MediaTile && w.media['id'] == video!['id'],
        );
        await reach(videoTile);
        await t.tap(
          find.descendant(of: videoTile, matching: find.byType(InkWell)).first,
        );
        await t.pump();
        await wait(find.byType(VideoPlayer));
        final controller = t
            .widget<VideoPlayer>(find.byType(VideoPlayer))
            .controller;
        expect(controller.value.isInitialized, true);
        expect(controller.value.duration, greaterThan(Duration.zero));
        await t.tap(find.byIcon(Icons.play_circle));
        await t.pump();
        for (
          var i = 0;
          i < 30 && controller.value.position == Duration.zero;
          i++
        ) {
          await t.runAsync(
            () => Future<void>.delayed(const Duration(milliseconds: 100)),
          );
          await t.pump();
        }
        expect(controller.value.position, greaterThan(Duration.zero));
        await capture('saved-video-playing-dark');
        await t.tap(find.byIcon(Icons.pause_circle));
        await t.pumpAndSettle();
        final after = obj(
          await owner.request(
            'GET',
            '/api/v1/reports/financial?allDates=true&pageSize=1',
          ),
        );
        expect(after['totalCount'], money['totalCount']);
        expect(after['netAmount'], money['netAmount']);
        expect(
          obj(
            await family.request('GET', '/api/v1/guardian/product-sales/$sale'),
          ),
          order,
        );
        expect(
          await owner.request('GET', '/api/v1/people/players/$player'),
          saved,
        );
      } finally {
        await t.pumpWidget(const SizedBox.shrink());
        await t.pumpAndSettle();
        for (final a in controllers) {
          a.dispose();
        }
        appearance.dispose();
      }
    },
  );
}
