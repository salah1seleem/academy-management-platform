import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/academy_brand.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/core/theme/logo_palette.dart';
import 'package:academy_mobile/core/widgets/home_skeleton.dart';
import 'package:academy_mobile/features/shared/academy_reveal.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

// Explicit synthetic template previews, separate from the real API journey.
// These images are test marks, never customer identity or child imagery.
class _NoNetwork implements ApiTransport {
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) => throw StateError('Template preview cannot use network');
}

class _NoSession implements RefreshStore {
  @override
  Future<String?> read() async => null;
  @override
  Future<void> write(String token) async {}
  @override
  Future<void> clear() async {}
}

Future<Uint8List> mark(Color color, {bool wide = false}) async {
  final recorder = ui.PictureRecorder();
  final canvas = Canvas(recorder);
  final pen = Paint()
    ..color = color
    ..style = PaintingStyle.stroke
    ..strokeWidth = 10;
  if (wide) {
    canvas.drawRRect(
      RRect.fromRectAndRadius(
        const Rect.fromLTWH(10, 10, 340, 80),
        const Radius.circular(32),
      ),
      pen,
    );
    canvas.drawLine(
      const Offset(30, 50),
      const Offset(330, 50),
      Paint()
        ..color = const Color(0xFF17B8DC)
        ..strokeWidth = 8,
    );
  } else {
    canvas.drawCircle(const Offset(80, 80), 58, pen);
    for (var y = 60.0; y <= 100; y += 20) {
      final path = Path()
        ..moveTo(40, y)
        ..quadraticBezierTo(60, y - 12, 80, y)
        ..quadraticBezierTo(100, y + 12, 120, y);
      canvas.drawPath(path, pen..strokeWidth = 5);
    }
  }
  final picture = recorder.endRecording();
  final image = await picture.toImage(wide ? 360 : 160, wide ? 100 : 160);
  final bytes = await image.toByteData(format: ui.ImageByteFormat.png);
  image.dispose();
  picture.dispose();
  return bytes!.buffer.asUint8List();
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'one reveal template: swimming light, dark, wide marks and reduced motion',
    (t) async {
      Future<void> tick(int ms) async {
        for (var elapsed = 0; elapsed < ms; elapsed += 50) {
          await t.pump(const Duration(milliseconds: 50));
        }
      }

      for (final scene in [
        ('swimming-light', Colors.white, 'swimming', false, false),
        ('neutral-dark', const Color(0xFF0A1F44), 'general', false, false),
        ('neutral-wide', const Color(0xFF0A1F44), 'general', true, false),
        ('reduced-motion', Colors.white, 'swimming', false, true),
      ]) {
        final bytes = await mark(scene.$2, wide: scene.$4);
        final auth = AuthController(_NoNetwork(), _NoSession());
        final brand = AcademyBrand(auth)
          ..name =
              '${scene.$3 == 'swimming' ? 'أكاديمية الموج' : 'أكاديمية الرياضات'} · معاينة'
          ..sportTheme = scene.$3
          ..primary = const Color(0xFF17B8DC)
          ..logo = bytes
          ..logoBackground = await logoSurface(bytes);
        await t.pumpWidget(
          BrandScope(
            brand: brand,
            child: MaterialApp(
              locale: const Locale('ar'),
              supportedLocales: const [Locale('ar')],
              localizationsDelegates: GlobalMaterialLocalizations.delegates,
              theme: AcademyTheme.dark(),
              builder: (context, child) => MediaQuery(
                data: MediaQuery.of(context)
                    .copyWith(disableAnimations: scene.$5),
                child: child!,
              ),
              home: AcademyReveal(
                academyName: brand.name,
                child: const Scaffold(body: HomeSkeleton(role: 'Coach')),
              ),
            ),
          ),
        );
        await tick(scene.$5 ? 450 : 700);
        if (scene.$5) {
          expect(find.byKey(const ValueKey('academy-reveal')), findsNothing);
        }
        await binding.takeScreenshot('phase-startup-template-${scene.$1}');
        await tick(1600);
        expect(find.byKey(const ValueKey('academy-reveal')), findsNothing);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        brand.dispose();
        auth.dispose();
      }
    },
  );
}
