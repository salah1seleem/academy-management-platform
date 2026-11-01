import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:academy_mobile/core/theme/academy_brand.dart';
import 'package:academy_mobile/core/theme/academy_home_skin.dart';
import 'package:academy_mobile/core/theme/logo_palette.dart';
import 'package:academy_mobile/features/shared/experience_ui.dart';
import 'package:academy_mobile/features/shared/subscription_identity_card.dart';
import 'package:academy_mobile/features/owner/owner_action_rail.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_brand_experience_test.dart' show railApp;
import 'v2_role_workspaces_test.dart' as f;

Future<Uint8List> artwork({
  Color? background,
  bool wide = false,
  Color ink = Colors.red,
}) async {
  final recorder = ui.PictureRecorder();
  // A canonical image with deliberate upload margins; no processing of its bytes.
  final c = Canvas(recorder);
  if (background != null) c.drawColor(background, BlendMode.src);
  c.drawRect(
    Rect.fromLTWH(20, wide ? 40 : 15, 56, wide ? 16 : 66),
    Paint()..color = ink,
  );
  final picture = recorder.endRecording();
  final image = await picture.toImage(96, 96);
  final data = await image.toByteData(format: ui.ImageByteFormat.png);
  image.dispose();
  picture.dispose();
  return data!.buffer.asUint8List();
}

void main() {
  testWidgets(
    'canonical logo trims light/transparent upload margins, protects full-bleed dark artwork, rejects missing bytes',
    (t) async {
      await t.runAsync(() async {
        for (final background in [null, Colors.white]) {
          final bytes = await artwork(background: background),
              original = bytes.toList();
          final meta = (await inspectLogoArtwork(bytes))!;
          expect(meta.bounds.width, lessThan(65));
          expect(meta.bounds.height, lessThan(75));
          expect(meta.colors.single.r, greaterThan(.9));
          expect(bytes.toList(), original);
        }
        final wide = (await inspectLogoArtwork(
          await artwork(wide: true, ink: Colors.white),
        ))!;
        expect(wide.bounds.width / wide.bounds.height, greaterThan(2));
        expect(wide.colors, isEmpty);
        final dark = (await inspectLogoArtwork(
          await artwork(background: const Color(0xFF101622)),
        ))!;
        expect(dark.bounds, const Rect.fromLTWH(0, 0, 96, 96));
        expect(await inspectLogoArtwork(Uint8List(0)), isNull);
      });
    },
  );
  test('multicolored logo keeps distinct actual hues; neutrals do not invent an accent', () {
    final colors = paletteFromRgba(
      Uint8List.fromList([
        for (var n = 0; n < 40; n++) ...[230, 20, 40, 255],
        for (var n = 0; n < 30; n++) ...[20, 170, 210, 255],
        for (var n = 0; n < 80; n++) ...[255, 255, 255, 255],
      ]),
    );
    expect(colors.length, 2);
    expect(colors.first.r, greaterThan(.8));
    expect(colors.last.b, greaterThan(.7));
    expect(
      paletteFromRgba(Uint8List.fromList([0, 0, 0, 255, 255, 255, 255, 255])),
      isEmpty,
    );
  });
  for (final role in ['AcademyOwner', 'AcademyAdmin', 'Coach', 'Guardian']) {
    testWidgets(
      '$role Home removes the watermark and keeps controls fixed on 320/402 and 200% text',
      (t) async {
        final auth = await f.authenticate(
          role,
          (m, p, b, k) async => <String, dynamic>{},
        );
        final brand = AcademyBrand(auth);
        await brand.refresh(force: true);
        brand.name = 'أكاديمية العرض الرياضية المتكاملة';
        for (final width in [320.0, 402.0]) {
          t.view.physicalSize = Size(width, 900);
          t.view.devicePixelRatio = 1;
          for (final scale in [1.0, 2.0]) {
            for (final dark in [false, true]) {
              await t.pumpWidget(
                railApp(
                  BrandScope(
                    brand: brand,
                    child: ExperienceHeader(
                      auth: auth,
                      subtitle: role,
                      lesson: 'home',
                      home: true,
                      account: const Scaffold(),
                    ),
                  ),
                  scale: scale,
                  dark: dark,
                  reduced: true,
                ),
              );
              await t.pumpAndSettle();
              expect(
                find.byKey(const ValueKey('home-header-watermark-zone')),
                findsNothing,
              );
              expect(find.byTooltip('شرح الصفحة بالفيديو'), findsNothing);
              expect(
                t
                    .getSize(find.byKey(const ValueKey('home-primary-crest')))
                    .width,
                48,
              );
              expect(
                t.takeException(),
                isNull,
                reason: '$role $width $scale $dark',
              );
            }
          }
        }
        await t.pumpWidget(const SizedBox());
        brand.dispose();
        auth.dispose();
        t.view.reset();
      },
    );
  }
  testWidgets(
    'compact subscription hero retains real counts, status links and white-compatible surface without duplicate academy name',
    (t) async {
      var status = '', totals = 0;
      await t.pumpWidget(
        railApp(
          SubscriptionIdentityCard(
            data: const {
              'total': 305,
              'active': 255,
              'expiring': 55,
              'expired': 50,
              'players': 300,
              'asOf': '2026-10-26',
            },
            owner: true,
            onTotal: () => totals++,
            onStatus: (v) => status = v,
          ),
          reduced: true,
        ),
      );
      expect(
        t.getSize(find.byKey(const ValueKey('subscription-home-hero'))).height,
        lessThan(275),
      );
      expect(find.text('iAcademy One'), findsNothing);
      await t.tap(find.text('255'));
      expect(totals, 1);
      await t.tap(find.text('55'));
      expect(status, 'expiring');
      expect(find.text('255'), findsOneWidget);
      expect(t.takeException(), isNull);
    },
  );
  testWidgets('Home selected filters remain readable in light and dark', (
    t,
  ) async {
    for (final dark in [false, true]) {
      await t.pumpWidget(
        railApp(
          AcademyHomeSkin(
            child: ChoiceChip(
              label: const Text('كل الرياضات'),
              selected: true,
              onSelected: (_) {},
            ),
          ),
          dark: dark,
          reduced: true,
        ),
      );
      await t.pumpAndSettle();
      final e = t.element(find.text('كل الرياضات'));
      final foreground = DefaultTextStyle.of(e).style.color!,
          background = Theme.of(e).chipTheme.selectedColor!;
      final a = foreground.computeLuminance(),
          b = background.computeLuminance();
      expect(
        ((a > b ? a : b) + .05) / ((a < b ? a : b) + .05),
        greaterThan(4.5),
      );
      expect(t.takeException(), isNull);
    }
  });
  testWidgets(
    'moving action rail advances once and pauses when a route covers it',
    (t) async {
      t.view.physicalSize = const Size(402, 874);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.resetPhysicalSize);
      addTearDown(t.view.resetDevicePixelRatio);
      await t.pumpWidget(
        railApp(
          OwnerActionRail(
            actions: [
              for (var n = 0; n < 4; n++)
                OwnerRailAction('عملية $n', Icons.add, Colors.red, () {}),
            ],
          ),
        ),
      );
      await t.pumpAndSettle();
      await t.pump(const Duration(seconds: 5));
      await t.pump(const Duration(milliseconds: 800));
      final rail = t.widget<Scrollable>(
        find.descendant(
          of: find.byKey(const ValueKey('owner-shortcut-scroll')),
          matching: find.byType(Scrollable),
        ),
      );
      expect(rail.controller!.offset, greaterThan(0));
      for (var n = 0; n < 4; n++) {
        expect(find.text('عملية $n').evaluate().length, lessThanOrEqualTo(1));
      }
      final heldOffset = rail.controller!.offset;
      final touch = await t.startGesture(t.getCenter(find.text('عملية 2')));
      await t.pump(const Duration(seconds: 5));
      expect(rail.controller!.offset, heldOffset);
      await touch.up();
      Navigator.of(t.element(find.byType(OwnerActionRail))).push(
        MaterialPageRoute<void>(
          builder: (_) => const Scaffold(body: Text('التفاصيل')),
        ),
      );
      await t.pumpAndSettle();
      final coveredOffset = rail.controller!.offset;
      await t.pump(const Duration(seconds: 5));
      expect(rail.controller!.offset, coveredOffset);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
    },
  );
}
