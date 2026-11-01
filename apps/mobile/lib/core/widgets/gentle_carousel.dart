import 'dart:async';

import 'package:flutter/material.dart';

/// Slow RTL-aware travel. Pauses for touch, reduced motion, backgrounding and
/// a pushed route; never duplicates interactive cards for an endless loop.
class GentleCarousel extends StatefulWidget {
  const GentleCarousel({
    super.key,
    required this.count,
    required this.width,
    required this.height,
    required this.itemBuilder,
  });
  final int count;
  final double width, height;
  final IndexedWidgetBuilder itemBuilder;
  @override
  State<GentleCarousel> createState() => _GentleCarouselState();
}

class _GentleCarouselState extends State<GentleCarousel>
    with WidgetsBindingObserver {
  final scroll = ScrollController();
  Timer? timer;
  bool forward = true, foreground = true;
  DateTime resumeAt = DateTime.fromMillisecondsSinceEpoch(0);
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    timer?.cancel();
    if (!MediaQuery.disableAnimationsOf(context) &&
        !MediaQuery.accessibleNavigationOf(context)) {
      timer = Timer.periodic(const Duration(seconds: 4), (_) => advance());
    }
  }

  void advance() {
    if (!mounted ||
        !foreground ||
        DateTime.now().isBefore(resumeAt) ||
        ModalRoute.of(context)?.isCurrent == false ||
        !scroll.hasClients ||
        widget.count < 2) {
      return;
    }
    final end = scroll.position.maxScrollExtent;
    if (end <= 0) {
      return;
    }
    if (scroll.offset >= end - 1) {
      forward = false;
    }
    if (scroll.offset <= 1) {
      forward = true;
    }
    scroll.animateTo(
      (scroll.offset + (forward ? 1 : -1) * (widget.width + 12)).clamp(0, end),
      duration: const Duration(milliseconds: 650),
      curve: Curves.easeInOutCubic,
    );
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    foreground = state == AppLifecycleState.resumed;
  }

  @override
  void dispose() {
    timer?.cancel();
    WidgetsBinding.instance.removeObserver(this);
    scroll.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Listener(
    onPointerDown: (_) =>
        resumeAt = DateTime.now().add(const Duration(seconds: 8)),
    child: SizedBox(
      height: widget.height,
      child: ListView.separated(
        controller: scroll,
        scrollDirection: Axis.horizontal,
        itemCount: widget.count,
        separatorBuilder: (_, _) => const SizedBox(width: 12),
        itemBuilder: (context, index) => SizedBox(
          width: widget.width,
          child: widget.itemBuilder(context, index),
        ),
      ),
    ),
  );
}
