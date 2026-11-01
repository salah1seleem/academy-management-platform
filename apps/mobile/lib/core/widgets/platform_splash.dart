import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_svg/flutter_svg.dart';

import 'brand_motion.dart';

class PlatformWordmark extends StatelessWidget {
  const PlatformWordmark({super.key});
  @override
  Widget build(BuildContext context) => Semantics(
    label: 'I Academy One',
    image: true,
    child: ExcludeSemantics(
      child: Directionality(
        textDirection: TextDirection.ltr,
        child: IntrinsicWidth(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Icon(Icons.sports_soccer, size: 16, color: platformCyan),
              const Text(
                'I Academy',
                textScaler: TextScaler.noScaling,
                style: TextStyle(
                  fontFamily: 'Roboto',
                  fontSize: 40,
                  height: 1.08,
                  fontWeight: FontWeight.w900,
                  color: Colors.white,
                ),
              ),
              Row(
                children: [
                  SizedBox(
                    width: 92,
                    height: 105,
                    child: SvgPicture.asset(
                      'assets/branding/iacademy-one-letter.svg',
                      fit: BoxFit.contain,
                    ),
                  ),
                  const Text(
                    'ne',
                    textScaler: TextScaler.noScaling,
                    style: TextStyle(
                      fontFamily: 'Roboto',
                      fontSize: 110,
                      height: 1,
                      fontWeight: FontWeight.w900,
                      color: Colors.white,
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    ),
  );
}

class PlatformSplash extends StatefulWidget {
  const PlatformSplash({super.key});
  @override
  State<PlatformSplash> createState() => _PlatformSplashState();
}

class _PlatformSplashState extends State<PlatformSplash>
    with SingleTickerProviderStateMixin {
  late final motion = AnimationController(
    vsync: this,
    duration: const Duration(seconds: 6),
  );
  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (MediaQuery.disableAnimationsOf(context)) {
      motion.stop();
    } else if (!motion.isAnimating) {
      motion.repeat();
    }
  }

  @override
  void dispose() {
    motion.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final reduced = MediaQuery.disableAnimationsOf(context);
    return AnnotatedRegion<SystemUiOverlayStyle>(
      value: SystemUiOverlayStyle.light,
      child: Scaffold(
        backgroundColor: platformNavy,
        body: Stack(
          fit: StackFit.expand,
          children: [
            const DecoratedBox(
              decoration: BoxDecoration(
                gradient: LinearGradient(
                  begin: Alignment.topLeft,
                  end: Alignment.bottomRight,
                  colors: [Color(0xFF071B32), platformNavy, Color(0xFF0B344A)],
                ),
              ),
            ),
            ExcludeSemantics(
              child: AnimatedBuilder(
                animation: motion,
                builder: (_, _) =>
                    CustomPaint(painter: _Bubbles(reduced ? 0 : motion.value)),
              ),
            ),
            Center(
              child: TweenAnimationBuilder<double>(
                tween: Tween(begin: reduced ? 1 : 0, end: 1),
                duration: Duration(milliseconds: reduced ? 0 : 1300),
                curve: Curves.easeOutCubic,
                builder: (_, v, child) => Opacity(
                  opacity: v,
                  child: Transform.translate(
                    offset: Offset(0, (1 - v) * 70),
                    child: child,
                  ),
                ),
                child: const Padding(
                  padding: EdgeInsets.all(40),
                  child: FittedBox(
                    fit: BoxFit.scaleDown,
                    child: PlatformWordmark(),
                  ),
                ),
              ),
            ),
            Positioned(
              left: 24,
              right: 24,
              bottom: 56,
              child: Text(
                'لكل موهبة رحلة',
                textAlign: TextAlign.center,
                style: TextStyle(
                  fontFamily: 'AcademyArabic',
                  fontSize: 14,
                  fontWeight: FontWeight.w500,
                  color: Colors.white.withValues(alpha: .65),
                  letterSpacing: .2,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Bubbles extends CustomPainter {
  const _Bubbles(this.phase);
  final double phase;
  @override
  void paint(Canvas c, Size s) {
    for (var i = 0; i < 16; i++) {
      final x = s.width * (.08 + (i * .173) % .86),
          y = s.height * (1 - ((i * .137 + phase) % 1));
      c.drawCircle(
        Offset(x + math.sin(phase * math.pi * 2 + i) * 12, y),
        3 + (i % 4) * 2.0,
        Paint()
          ..color = platformCyan.withValues(alpha: .1 + (i % 3) * .035)
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1.2,
      );
    }
  }

  @override
  bool shouldRepaint(_Bubbles old) => phase != old.phase;
}
