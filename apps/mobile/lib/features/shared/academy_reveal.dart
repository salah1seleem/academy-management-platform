import 'package:flutter/material.dart';

import '../../core/theme/academy_brand.dart';
import '../../core/widgets/brand_motion.dart';

/// Mounted once per trusted session navigator, after onboarding. Home mounts
/// immediately underneath and owns its real requests/errors. Completion never
/// depends on the network, and normal resumes/tab changes cannot restart it.
class AcademyReveal extends StatefulWidget {
  const AcademyReveal({
    super.key,
    required this.academyName,
    required this.child,
    this.skip = false,
  });
  final String academyName;
  final Widget child;
  final bool skip;
  @override
  State<AcademyReveal> createState() => _AcademyRevealState();
}

class _AcademyRevealState extends State<AcademyReveal>
    with SingleTickerProviderStateMixin {
  late final clock = AnimationController(vsync: this);
  bool started = false, finished = false, reduced = false;
  @override
  void initState() {
    super.initState();
    clock.addStatusListener((status) {
      if (status == AnimationStatus.completed && mounted) {
        setState(() => finished = true);
      }
    });
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final nextReduced =
        MediaQuery.disableAnimationsOf(context) ||
        MediaQuery.accessibleNavigationOf(context);
    if (!started) {
      started = true;
      reduced = nextReduced;
      clock.duration = Duration(milliseconds: reduced ? 350 : 2000);
      if (!widget.skip) clock.forward();
    } else if (nextReduced && !reduced) {
      reduced = true;
      // A live accessibility change ends scale/contour movement immediately.
      clock.forward(from: .8);
    }
  }

  @override
  void dispose() {
    clock.dispose();
    super.dispose();
  }

  static double scaleAt(double milliseconds) {
    if (milliseconds <= 150) return .68;
    if (milliseconds < 700) {
      return .68 +
          .36 * Curves.easeOutCubic.transform((milliseconds - 150) / 550);
    }
    if (milliseconds < 850) {
      return 1.04 -
          .04 * Curves.easeOutCubic.transform((milliseconds - 700) / 150);
    }
    return 1;
  }

  @override
  Widget build(BuildContext context) {
    // Keep the same Stack position when the overlay disappears: Home state,
    // requests and navigation must survive the end of the reveal.
    final brand = BrandScope.maybeOf(context);
    final showing = !finished && !widget.skip;
    return Stack(
      fit: StackFit.expand,
      children: [
        TickerMode(
          enabled: !showing,
          child: ExcludeSemantics(
            excluding: showing,
            child: IgnorePointer(ignoring: showing, child: widget.child),
          ),
        ),
        if (showing)
          Positioned.fill(
            child: AnimatedBuilder(
              animation: clock,
              builder: (context, _) {
                final ms = clock.value * (reduced ? 350 : 2000);
                final fade = reduced
                    ? (1 - ((ms - 200) / 150).clamp(0, 1)).toDouble()
                    : (1 - ((ms - 1600) / 400).clamp(0, 1)).toDouble();
                final appear = reduced
                    ? 1.0
                    : (ms / 250).clamp(0, 1).toDouble();
                return Opacity(
                  opacity: fade,
                  child: Material(
                    key: const ValueKey('academy-reveal'),
                    color: platformNavy,
                    child: Stack(
                      fit: StackFit.expand,
                      children: [
                        Opacity(
                          opacity: appear,
                          child: BrandAtmosphere(
                            accent: brand?.primary ?? platformCyan,
                            sport: brand?.sportTheme ?? 'general',
                            moving: !reduced,
                          ),
                        ),
                        SafeArea(
                          child: Center(
                            child: SingleChildScrollView(
                              padding: const EdgeInsets.all(28),
                              child: Column(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  Transform.scale(
                                    key: const ValueKey('academy-reveal-scale'),
                                    scale: reduced ? 1 : scaleAt(ms),
                                    child: Opacity(
                                      opacity: reduced
                                          ? 1
                                          : ((ms - 150) / 250)
                                                .clamp(0, 1)
                                                .toDouble(),
                                      child: AcademyLogoPlate(
                                        logo: brand?.logo,
                                        wide: true,
                                        surface:
                                            brand?.logoBackground ??
                                            Colors.white,
                                      ),
                                    ),
                                  ),
                                  const SizedBox(height: 28),
                                  Text(
                                    brand?.name ?? widget.academyName,
                                    textAlign: TextAlign.center,
                                    style: const TextStyle(
                                      color: Colors.white,
                                      fontSize: 24,
                                      height: 1.5,
                                      fontWeight: FontWeight.w700,
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                );
              },
            ),
          ),
      ],
    );
  }
}
