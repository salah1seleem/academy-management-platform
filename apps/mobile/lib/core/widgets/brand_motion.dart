import 'dart:math' as math;
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_svg/flutter_svg.dart';

const platformNavy = Color(0xFF0A1F44);
const platformCyan = Color(0xFF17B8DC);

/// The public platform identity is independent of any authenticated academy.
class PlatformMark extends StatelessWidget {
  const PlatformMark({super.key, this.size = 56});
  final double size;
  @override
  Widget build(BuildContext context) => Semantics(
    label: 'iAcademy One',
    image: true,
    child: SvgPicture.asset(
      'assets/branding/iacademy-symbol-on-dark.svg',
      width: size,
      height: size,
      excludeFromSemantics: true,
    ),
  );
}

/// One canonical image, contained without tinting. The neutral surface is
/// selected from visible pixels rather than assuming the academy accent fits.
class AcademyLogoPlate extends StatelessWidget {
  const AcademyLogoPlate({
    super.key,
    required this.logo,
    this.size = 160,
    this.wide = false,
    this.surface = Colors.white,
  });
  final Uint8List? logo;
  final double size;
  final bool wide;
  final Color surface;
  @override
  Widget build(BuildContext context) => Container(
    width: wide ? size * 1.55 : size,
    height: size,
    padding: EdgeInsets.all(size * .12),
    decoration: BoxDecoration(
      borderRadius: BorderRadius.circular(size * .22),
      color: surface,
      border: Border.all(color: Colors.white.withValues(alpha: .6)),
      boxShadow: [
        BoxShadow(color: Colors.black.withValues(alpha: .16), blurRadius: 36),
      ],
    ),
    child: logo == null
        ? const Icon(Icons.school_outlined, color: platformNavy, size: 48)
        : Image.memory(
            logo!,
            fit: BoxFit.contain,
            gaplessPlayback: false,
            errorBuilder: (_, _, _) => const Icon(
              Icons.school_outlined,
              color: platformNavy,
              size: 48,
            ),
          ),
  );
}

/// Paint-only movement: no video, network requests, particles, sound or text.
/// Pauses with route/lifecycle ticker mode and with OS reduced-motion settings.
class BrandAtmosphere extends StatefulWidget {
  const BrandAtmosphere({
    super.key,
    this.accent = platformCyan,
    this.sport = 'general',
    this.moving = true,
  });
  final Color accent;
  final String sport;
  final bool moving;
  @override
  State<BrandAtmosphere> createState() => _BrandAtmosphereState();
}

class _BrandAtmosphereState extends State<BrandAtmosphere>
    with SingleTickerProviderStateMixin, WidgetsBindingObserver {
  late final motion = AnimationController(
    vsync: this,
    duration: const Duration(seconds: 16),
  );
  bool resumed = true;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  void sync() {
    final active =
        resumed &&
        widget.moving &&
        TickerMode.valuesOf(context).enabled &&
        !MediaQuery.disableAnimationsOf(context) &&
        !MediaQuery.accessibleNavigationOf(context);
    if (active && !motion.isAnimating && motion.value < 1) motion.forward();
    if (!active) motion.stop();
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    sync();
  }

  @override
  void didUpdateWidget(BrandAtmosphere old) {
    super.didUpdateWidget(old);
    sync();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    resumed = state == AppLifecycleState.resumed;
    sync();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    motion.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => ExcludeSemantics(
    child: ClipRect(
      child: RepaintBoundary(
        child: CustomPaint(
          painter: _AtmospherePainter(motion, widget.accent, widget.sport),
          size: Size.infinite,
        ),
      ),
    ),
  );
}

class _AtmospherePainter extends CustomPainter {
  _AtmospherePainter(this.motion, this.accent, this.sport)
    : super(repaint: motion);
  final Animation<double> motion;
  final Color accent;
  final String sport;
  @override
  void paint(Canvas canvas, Size size) {
    final bounds = Offset.zero & size;
    canvas.drawRect(bounds, Paint()..color = platformNavy);
    final glow = Offset(
      size.width * (.8 - motion.value * .14),
      size.height * .2,
    );
    canvas.drawRect(
      bounds,
      Paint()
        ..shader =
            RadialGradient(
              colors: [
                accent.withValues(alpha: .23),
                platformNavy.withValues(alpha: 0),
              ],
            ).createShader(
              Rect.fromCircle(center: glow, radius: size.longestSide * .7),
            ),
    );
    final pen = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1
      ..color = Colors.white.withValues(alpha: .07);
    if (sport == 'swimming') {
      for (var row = 0; row < 7; row++) {
        final path = Path();
        for (var x = 0.0; x <= size.width + 4; x += 4) {
          final y =
              size.height * (.16 + row * .115) +
              math.sin(x / 74 + motion.value * .8 + row * .4) * 14;
          if (x == 0) {
            path.moveTo(x, y);
          } else {
            path.lineTo(x, y);
          }
        }
        canvas.drawPath(path, pen);
      }
    } else {
      for (var i = 0; i < 5; i++) {
        canvas.drawOval(
          Rect.fromCenter(
            center: Offset(size.width * 1.12, size.height * .62),
            width: size.width * (1.5 + i * .32) + motion.value * 12,
            height: size.height * (.56 + i * .18),
          ),
          pen,
        );
      }
    }
  }

  @override
  bool shouldRepaint(_AtmospherePainter old) =>
      old.accent != accent || old.sport != sport || old.motion != motion;
}
