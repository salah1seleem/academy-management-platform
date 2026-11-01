import 'package:flutter/material.dart';
import 'package:flutter/scheduler.dart';

/// Continuous slow cards with a preview of the next card and touch pause.
class SwipeCardDeck extends StatefulWidget {
  const SwipeCardDeck({
    super.key,
    required this.children,
    required this.height,
  });
  final List<Widget> children;
  final double height;
  @override
  State<SwipeCardDeck> createState() => _SwipeCardDeckState();
}

class _SwipeCardDeckState extends State<SwipeCardDeck>
    with SingleTickerProviderStateMixin, WidgetsBindingObserver {
  final scroll = ScrollController();
  late final Ticker ticker;
  Duration? last;
  Duration resumeAt = Duration.zero;
  bool touching = false, foreground = true;
  double extent = 300;
  @override
  void initState() {
    super.initState();
    ticker = createTicker(tick);
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (ticker.isActive) ticker.stop();
    last = null;
  }

  void pause() =>
      resumeAt = (last ?? Duration.zero) + const Duration(seconds: 4);
  void tick(Duration elapsed) {
    final delta = last == null
        ? 0.0
        : (elapsed - last!).inMicroseconds / 1000000;
    last = elapsed;
    if (!foreground ||
        touching ||
        elapsed < resumeAt ||
        !scroll.hasClients ||
        widget.children.length < 2 ||
        ModalRoute.of(context)?.isCurrent == false) {
      return;
    }
    final cycle = extent * widget.children.length;
    final next = scroll.offset + delta.clamp(0, .1) * 11;
    scroll.jumpTo(next >= cycle * 2 ? next - cycle : next);
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    foreground = state == AppLifecycleState.resumed;
    last = null;
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    ticker.dispose();
    scroll.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (widget.children.isEmpty) return const SizedBox.shrink();
    return LayoutBuilder(
      builder: (context, bounds) {
        extent = bounds.maxWidth * .86;
        return Listener(
          onPointerDown: (_) {
            touching = true;
            pause();
          },
          onPointerUp: (_) {
            touching = false;
            pause();
          },
          onPointerCancel: (_) {
            touching = false;
            pause();
          },
          child: SizedBox(
            height: widget.height,
            child: ListView.builder(
              key: const ValueKey('owner-decisions-rail'),
              controller: scroll,
              scrollDirection: Axis.horizontal,
              itemExtent: extent,
              itemCount: widget.children.length,
              itemBuilder: (context, index) => Padding(
                padding: const EdgeInsetsDirectional.only(end: 12),
                child: widget.children[index % widget.children.length],
              ),
            ),
          ),
        );
      },
    );
  }
}
