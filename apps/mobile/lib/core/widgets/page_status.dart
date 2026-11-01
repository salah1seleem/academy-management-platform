import 'package:flutter/material.dart';

import '../theme/ui_text.dart';

class StatusPanel extends StatelessWidget {
  const StatusPanel({
    super.key,
    required this.message,
    this.title,
    this.icon = Icons.inbox_outlined,
    this.actionLabel,
    this.onAction,
    this.urgent = false,
  });
  final String message;
  final String? title, actionLabel;
  final IconData icon;
  final VoidCallback? onAction;
  final bool urgent;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final accent = urgent ? colors.error : colors.primary;
    return Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 440),
        child: Semantics(
          liveRegion: urgent,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Center(
                child: Container(
                  width: 64,
                  height: 64,
                  decoration: BoxDecoration(
                    color: accent.withValues(alpha: .09),
                    borderRadius: BorderRadius.circular(20),
                  ),
                  child: Icon(icon, size: 28, color: accent),
                ),
              ),
              const SizedBox(height: 18),
              if (title != null) ...[
                Semantics(
                  header: true,
                  child: UiText(
                    title!,
                    textAlign: TextAlign.center,
                    style: const TextStyle(
                      fontSize: 18,
                      fontWeight: FontWeight.w800,
                    ),
                  ),
                ),
                const SizedBox(height: 10),
              ],
              UiText(
                message,
                textAlign: TextAlign.center,
                style: TextStyle(
                  fontSize: 14,
                  height: 1.6,
                  color: colors.onSurfaceVariant,
                ),
              ),
              if (onAction != null && actionLabel != null) ...[
                const SizedBox(height: 20),
                FilledButton.icon(
                  onPressed: onAction,
                  icon: const Icon(Icons.refresh_rounded),
                  label: UiText(actionLabel!),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

/// Error/permission states remain reachable with large text or a keyboard.
class PageStatus extends StatelessWidget {
  const PageStatus({super.key, required this.child});
  final Widget child;
  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, bounds) => SingleChildScrollView(
      physics: const AlwaysScrollableScrollPhysics(),
      child: ConstrainedBox(
        constraints: BoxConstraints(
          minHeight: bounds.hasBoundedHeight ? bounds.maxHeight : 0,
        ),
        child: Padding(padding: const EdgeInsets.all(24), child: child),
      ),
    ),
  );
}

/// Neutral placeholders communicate loading without inventing records.
class PageLoading extends StatelessWidget {
  const PageLoading({super.key});
  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    Widget line(double width, double height) => Container(
      width: width,
      height: height,
      decoration: BoxDecoration(
        color: colors.surfaceContainerHighest.withValues(alpha: .65),
        borderRadius: BorderRadius.circular(7),
      ),
    );
    return Semantics(
      label: 'جارٍ التحميل',
      liveRegion: true,
      child: ExcludeSemantics(
        child: SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(20, 16, 20, 24),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              for (var i = 0; i < 3; i++)
                Padding(
                  padding: const EdgeInsets.only(bottom: 14),
                  child: Container(
                    padding: const EdgeInsets.all(18),
                    decoration: BoxDecoration(
                      color: colors.surface,
                      border: Border.all(
                        color: colors.outlineVariant.withValues(alpha: .55),
                      ),
                      borderRadius: BorderRadius.circular(20),
                    ),
                    child: LayoutBuilder(
                      builder: (context, bounds) => Row(
                        children: [
                          Container(
                            width: 42,
                            height: 42,
                            decoration: BoxDecoration(
                              color: colors.surfaceContainerHighest,
                              borderRadius: BorderRadius.circular(14),
                            ),
                          ),
                          const SizedBox(width: 14),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                line(bounds.maxWidth * .5, 13),
                                const SizedBox(height: 10),
                                line(bounds.maxWidth * .34, 10),
                              ],
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
      ),
    );
  }
}

class PageActionBar extends StatelessWidget {
  const PageActionBar({super.key, required this.child});
  final Widget child;
  @override
  Widget build(BuildContext context) => AnimatedPadding(
    duration: MediaQuery.disableAnimationsOf(context)
        ? Duration.zero
        : const Duration(milliseconds: 180),
    curve: Curves.easeOut,
    padding: EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
    child: DecoratedBox(
      decoration: BoxDecoration(
        color: Theme.of(context).colorScheme.surface,
        border: Border(
          top: BorderSide(
            color: Theme.of(context).colorScheme.outlineVariant
                .withValues(alpha: .6),
          ),
        ),
      ),
      child: SafeArea(
        top: false,
        child: Center(
          heightFactor: 1,
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 650),
            child: Padding(
              padding: const EdgeInsets.fromLTRB(20, 12, 20, 12),
              child: TextFieldTapRegion(
                child: SizedBox(width: double.infinity, child: child),
              ),
            ),
          ),
        ),
      ),
    ),
  );
}
