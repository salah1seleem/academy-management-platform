import 'package:flutter/material.dart';

/// Only inserted by a pending Home read, never by a timer. Mirrors the role's
/// actions/hero/content hierarchy; contains no invented counts or progress.
class HomeSkeleton extends StatefulWidget {
  const HomeSkeleton({super.key, required this.role});
  final String role;
  @override
  State<HomeSkeleton> createState() => _HomeSkeletonState();
}

class _HomeSkeletonState extends State<HomeSkeleton>
    with SingleTickerProviderStateMixin, WidgetsBindingObserver {
  late final motion = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 1600),
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
        TickerMode.valuesOf(context).enabled &&
        !MediaQuery.disableAnimationsOf(context) &&
        !MediaQuery.accessibleNavigationOf(context);
    if (active && !motion.isAnimating) motion.repeat();
    if (!active) motion.stop();
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
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

  Widget block(double height, {double? width}) => Container(
    height: height,
    width: width,
    decoration: BoxDecoration(
      color: Theme.of(context).colorScheme.onSurface.withValues(alpha: .055),
      borderRadius: BorderRadius.circular(14),
    ),
  );
  @override
  Widget build(BuildContext context) => Semantics(
    label: 'جارٍ تحميل الرئيسية',
    liveRegion: true,
    child: ExcludeSemantics(
      child: RepaintBoundary(
        child: AnimatedBuilder(
          animation: motion,
          builder: (context, child) => ShaderMask(
            blendMode: BlendMode.srcATop,
            shaderCallback: (bounds) => LinearGradient(
              begin: Alignment(-3 + motion.value * 6, 0),
              end: Alignment(-1 + motion.value * 6, 0),
              colors: [
                Colors.transparent,
                Colors.white.withValues(alpha: .045),
                Colors.transparent,
              ],
            ).createShader(bounds),
            child: child,
          ),
          child: ListView(
            key: ValueKey('home-skeleton-${widget.role}'),
            padding: const EdgeInsets.all(18),
            children: [
              if (widget.role == 'AcademyOwner' ||
                  widget.role == 'AcademyAdmin') ...[
                Row(
                  children: [
                    for (var i = 0; i < 3; i++) ...[
                      if (i > 0) const SizedBox(width: 10),
                      Expanded(child: block(54)),
                    ],
                  ],
                ),
                const SizedBox(height: 18),
              ],
              Align(
                alignment: AlignmentDirectional.centerStart,
                child: block(18, width: 116),
              ),
              const SizedBox(height: 14),
              block(
                widget.role == 'Guardian'
                    ? 360
                    : widget.role == 'Coach'
                    ? 200
                    : 250,
              ),
              const SizedBox(height: 18),
              Row(
                children: [
                  for (var i = 0; i < 3; i++) ...[
                    if (i > 0) const SizedBox(width: 10),
                    Expanded(child: block(62)),
                  ],
                ],
              ),
              const SizedBox(height: 24),
              Align(
                alignment: AlignmentDirectional.centerStart,
                child: block(18, width: 140),
              ),
              const SizedBox(height: 14),
              block(110),
            ],
          ),
        ),
      ),
    ),
  );
}
