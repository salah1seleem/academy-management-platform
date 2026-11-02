import 'dart:ui' as ui;

import 'package:academy_mobile/features/shared/self_profile.dart';
import 'package:academy_mobile/features/owner/home_destinations.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';

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

class Ui54Transport extends HttpApiTransport {
  Ui54Transport(super.config);
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    if (method != 'GET' &&
        !path.startsWith('/api/v1/mobile/auth/') &&
        !(method == 'PUT' && path == '/api/v1/me/profile')) {
      throw StateError('UI rehearsal cannot write business records');
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
    'V54 personal headers, persistent self portrait, filtered players and four real Homes',
    (t) async {
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5378');
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      final api = Ui54Transport(AppConfig('Demo', base));
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
        debugPrint('ui54: mounting ${item.$3}');
        final auth = AuthController(
          api,
          SecureRefreshStore('ui54-preview-5378-${item.$2}'),
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
              ? find.text('255')
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
        expect(
          find.byKey(const ValueKey('home-header-watermark-zone')),
          findsNothing,
        );
        expect(find.byTooltip('شرح الصفحة بالفيديو'), findsNothing);
        expect(find.byType(SelfProfileAvatar), findsOneWidget);
        await brand.loadProfile();
        expect(brand.profileName, isNotNull);
        expect(
          t.getSize(find.byKey(const ValueKey('home-primary-crest'))).width,
          52,
        );
        final controller = t
            .widget<ListView>(find.byKey(const ValueKey('owner-action-rail')))
            .controller!;
        final start = controller.offset;
        await binding.takeScreenshot('phase-refine-${item.$3}-light');
        await tick(3200);
        final end = controller.offset;
        if (controller.position.maxScrollExtent > 0) expect(end, isNot(start));
        results[item.$3] = {
          'start': start,
          'end': end,
          'extent': controller.position.maxScrollExtent,
          'colors': brand.artwork!.colors.map((c) => c.toARGB32()).toList(),
        };
        await binding.takeScreenshot('phase-refine-${item.$3}-motion');
        await t.tap(find.byKey(const ValueKey('header-appearance-toggle')));
        await tick(600);
        await binding.takeScreenshot('phase-refine-${item.$3}-dark');
        await t.tap(find.byTooltip('حسابي'));
        await tick(600);
        await wait(find.text('تغيير صورتي الشخصية'));
        expect(find.text('ابدأ من هنا'), findsNothing);
        expect(find.text('أيقونة التطبيق على الجهاز'), findsNothing);
        expect(find.byType(SwitchListTile), findsNothing);
        await binding.takeScreenshot('phase-refine-${item.$3}-profile');
        if (item.$3 == 'owner') {
          // Synthetic portrait, uploaded through the same authenticated path as the picker.
          final recorder = ui.PictureRecorder(), canvas = Canvas(recorder);
          canvas.drawColor(const Color(0xffd9e9f2), BlendMode.src);
          canvas.drawCircle(
            const Offset(64, 56),
            30,
            Paint()..color = const Color(0xffe9b48a),
          );
          canvas.drawOval(
            const Rect.fromLTWH(18, 85, 92, 100),
            Paint()..color = const Color(0xff245978),
          );
          canvas.drawCircle(
            const Offset(54, 55),
            3,
            Paint()..color = Colors.black,
          );
          canvas.drawCircle(
            const Offset(75, 55),
            3,
            Paint()..color = Colors.black,
          );
          final image = await recorder.endRecording().toImage(128, 128);
          final bytes = (await image.toByteData(
            format: ui.ImageByteFormat.png,
          ))!.buffer.asUint8List();
          await auth.upload(bytes, 'image/png', {'purpose': 'SelfPhoto'});
          image.dispose();
          await brand.loadProfile();
          await tick(500);
          final stored = brand.profilePhoto;
          expect(stored, isNotNull);
          final restored = AcademyBrand(auth);
          await restored.loadProfile();
          expect(restored.profilePhoto, stored);
          restored.dispose();
          await binding.takeScreenshot('phase-refine-owner-profile-saved');
        }
        Navigator.of(t.element(find.byType(SelfProfileCard))).pop();
        await tick(500);
        if (item.$3 == 'owner') {
          final state = Navigator.of(t.element(find.byType(ExperienceHeader)));
          state.push(
            MaterialPageRoute<void>(
              builder: (_) => HomeSubscriptionPlayers(
                auth: auth,
                uniquePlayers: true,
                filters: const {'from': '2026-09-26', 'asOf': '2026-10-26'},
              ),
            ),
          );
          await tick(600);
          await wait(find.text('ملف اللاعب'));
          await binding.takeScreenshot('phase-refine-owner-players');
          expect(find.byType(PlayerAvatar), findsWidgets);
          state.pop();
          await tick(500);
          await t.scrollUntilVisible(
            find.byKey(const ValueKey('collection-target-button')),
            200,
            scrollable: find.byType(Scrollable).first,
            maxScrolls: 12,
          );
          await tick(500);
          await binding.takeScreenshot('phase-refine-owner-summary');
          await t.scrollUntilVisible(
            find.byKey(const ValueKey('owner-honors-rail')),
            250,
            scrollable: find.byType(Scrollable).first,
            maxScrolls: 30,
          );
          await tick(500);
          await binding.takeScreenshot('phase-refine-owner-stars');
        }
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
        prefs.dispose();
      }
      debugPrint('ui54: motion results $results');
      binding.reportData ??= <String, dynamic>{};
      binding.reportData!['source'] = 'V54 Home/profile rehearsal, real isolated API 5378 and four authorized synthetic demo roles; synthetic own portrait upload only, no business writes. Not first-use or physical device; gallery picker not automated.';
      binding.reportData!['homeMotion'] = results;
    },
  );
}
