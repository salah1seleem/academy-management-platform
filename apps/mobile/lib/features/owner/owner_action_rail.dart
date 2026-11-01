import 'dart:async';

import 'package:flutter/material.dart';

import '../../core/theme/ui_text.dart';

class OwnerRailAction {
  const OwnerRailAction(
    this.label,
    this.icon,
    this.color,
    this.onTap, {
    this.key,
  });
  final String label;
  final IconData icon;
  final Color color;
  final VoidCallback onTap;
  final Key? key;
}

/// Theme-aware shortcuts. Motion yields to touch, accessibility and navigation.
class OwnerActionRail extends StatefulWidget {
  const OwnerActionRail({
    super.key,
    required this.actions,
    this.automatic = true,
    this.onAll,
  });
  final bool automatic;
  final List<OwnerRailAction> actions;
  final VoidCallback? onAll;
  @override
  State<OwnerActionRail> createState() => _OwnerActionRailState();
}

class _OwnerActionRailState extends State<OwnerActionRail>
    with WidgetsBindingObserver {
  final scroll = ScrollController();
  Timer? timer, resume;
  bool cooling = false;
  bool touching = false, foreground = true;
  double extent = 174;
  bool forward = true;
  bool get reduced =>
      MediaQuery.disableAnimationsOf(context) ||
      MediaQuery.accessibleNavigationOf(context);

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    syncMotion();
  }

  @override
  void didUpdateWidget(OwnerActionRail old) {
    super.didUpdateWidget(old);
    if (old.automatic != widget.automatic ||
        old.actions.length != widget.actions.length) {
      syncMotion();
    }
  }

  void syncMotion() {
    timer?.cancel();
    if (widget.automatic &&
        !reduced &&
        widget.actions.length > 2 &&
        foreground) {
      timer = Timer.periodic(const Duration(seconds: 5), (_) => advance());
    }
  }

  void pause() {
    cooling = true;
    resume?.cancel();
    resume = Timer(const Duration(seconds: 5), () {
      cooling = false;
    });
  }

  void advance() {
    if (!mounted ||
        !scroll.hasClients ||
        touching ||
        cooling ||
        !foreground ||
        ModalRoute.of(context)?.isCurrent == false) {
      return;
    }
    final maximum = scroll.position.maxScrollExtent;
    if (maximum <= 0) return;
    final next = (scroll.offset + (forward ? extent : -extent)).clamp(
      0.0,
      maximum,
    );
    if (next >= maximum) forward = false;
    if (next <= 0) forward = true;
    scroll.animateTo(
      next,
      duration: const Duration(milliseconds: 750),
      curve: Curves.easeInOutCubic,
    );
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    foreground = state == AppLifecycleState.resumed;
    syncMotion();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    timer?.cancel();
    resume?.cancel();
    scroll.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (widget.actions.isEmpty) return const SizedBox.shrink();
    final c = Theme.of(context).colorScheme;
    final large = MediaQuery.textScalerOf(context).scale(1) > 1.4;
    return Padding(
      padding: const EdgeInsets.only(top: 8, bottom: 18),
      child: Column(
        key: const ValueKey('owner-action-rail'),
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              const Expanded(
                child: UiText(
                  'العمليات السريعة',
                  style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700),
                ),
              ),
              if (widget.onAll != null)
                TextButton.icon(
                  onPressed: widget.onAll,
                  icon: const Icon(Icons.grid_view_rounded, size: 17),
                  label: const UiText('كل العمليات'),
                ),
            ],
          ),
          LayoutBuilder(
            builder: (context, bounds) {
              extent = large ? 216 : 174;
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
                child: NotificationListener<UserScrollNotification>(
                  onNotification: (_) {
                    pause();
                    return false;
                  },
                  child: SizedBox(
                    height: large ? 126 : 82,
                    child: ListView.builder(
                      key: const ValueKey('owner-shortcut-scroll'),
                      controller: scroll,
                      scrollDirection: Axis.horizontal,
                      itemExtent: extent,
                      itemCount: widget.actions.length,
                      itemBuilder: (context, index) {
                        final a = widget.actions[index % widget.actions.length];
                        return Padding(
                          padding: const EdgeInsetsDirectional.only(end: 10),
                          child: Material(
                            color: c.surface,
                            shape: RoundedRectangleBorder(
                              borderRadius: BorderRadius.circular(20),
                              side: BorderSide(color: c.outlineVariant),
                            ),
                            clipBehavior: Clip.antiAlias,
                            child: InkWell(
                              key: index < widget.actions.length ? a.key : null,
                              onTap: a.onTap,
                              child: Padding(
                                padding: const EdgeInsets.symmetric(
                                  horizontal: 12,
                                  vertical: 12,
                                ),
                                child: Row(
                                  children: [
                                    Container(
                                      width: 40,
                                      height: 40,
                                      decoration: BoxDecoration(
                                        color: c.primary.withValues(alpha: .12),
                                        borderRadius: BorderRadius.circular(14),
                                      ),
                                      child: Icon(
                                        a.icon,
                                        color: c.primary,
                                        size: 23,
                                      ),
                                    ),
                                    const SizedBox(width: 10),
                                    Expanded(
                                      child: UiText(
                                        a.label,
                                        maxLines: large ? 3 : 2,
                                        style: const TextStyle(
                                          fontSize: 12,
                                          fontWeight: FontWeight.w700,
                                          height: 1.45,
                                        ),
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                            ),
                          ),
                        );
                      },
                    ),
                  ),
                ),
              );
            },
          ),
        ],
      ),
    );
  }
}
