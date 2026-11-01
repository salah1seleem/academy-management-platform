import 'package:flutter/material.dart';

import '../../core/theme/academy_brand.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';

/// The owner's logical destinations keep their established visual order.
class OwnerBrandNavigation extends StatelessWidget {
  const OwnerBrandNavigation({
    super.key,
    required this.selected,
    required this.onSelected,
    this.showMatches = true,
  });
  final int selected;
  final bool showMatches;
  final ValueChanged<int> onSelected;
  @override
  Widget build(BuildContext context) {
    final c = Theme.of(context).colorScheme;
    final scale = MediaQuery.textScalerOf(context).scale(1);
    final items = [
      (1, 'العمليات', Icons.bolt_rounded),
      if (showMatches)
        (
          2,
          BrandScope.maybeOf(context)?.sportTheme == 'swimming'
              ? 'المنافسات'
              : 'المباريات',
          Icons.emoji_events_outlined,
        ),
      (0, 'الرئيسية', Icons.home_rounded),
      (3, 'التقارير', Icons.bar_chart_rounded),
      (4, 'المزيد', Icons.grid_view_rounded),
    ];
    return SafeArea(
      top: false,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(14, 6, 14, 8),
        child: Container(
          decoration: BoxDecoration(
            color: c.surface,
            borderRadius: BorderRadius.circular(24),
            border: Border.all(color: c.outlineVariant.withValues(alpha: .8)),
            boxShadow: [
              BoxShadow(
                color: Colors.black.withValues(alpha: .035),
                blurRadius: 24,
                offset: const Offset(0, -3),
              ),
            ],
          ),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 8),
            child: Row(
              children: [
                for (final item in items)
                  Expanded(
                    child: Semantics(
                      button: true,
                      selected: selected == item.$1,
                      label: item.$2,
                      onTap: () => onSelected(item.$1),
                      child: InkWell(
                        key: ValueKey('owner-nav-${item.$1}'),
                        onTap: () => onSelected(item.$1),
                        excludeFromSemantics: true,
                        borderRadius: BorderRadius.circular(16),
                        child: ExcludeSemantics(
                          child: Padding(
                            padding: const EdgeInsets.symmetric(
                              vertical: 3,
                              horizontal: 1,
                            ),
                            child: Column(
                              mainAxisSize: MainAxisSize.min,
                              children: [
                                AnimatedContainer(
                                  duration: const Duration(milliseconds: 180),
                                  padding: const EdgeInsets.symmetric(
                                    horizontal: 16,
                                    vertical: 7,
                                  ),
                                  decoration: BoxDecoration(
                                    color: selected == item.$1
                                        ? c.primary.withValues(alpha: .12)
                                        : Colors.transparent,
                                    borderRadius: BorderRadius.circular(13),
                                  ),
                                  child:
                                      item.$1 == 0 &&
                                          BrandScope.maybeOf(context)?.logo !=
                                              null
                                      ? const SizedBox(
                                          width: 34,
                                          height: 34,
                                          child: CanonicalAcademyLogo(),
                                        )
                                      : Icon(
                                          item.$3,
                                          size: 23,
                                          color: selected == item.$1
                                              ? c.primary
                                              : c.onSurfaceVariant,
                                        ),
                                ),
                                const SizedBox(height: 4),
                                UiText(
                                  item.$2,
                                  maxLines: scale > 1.4 ? 2 : 1,
                                  overflow: TextOverflow.ellipsis,
                                  textAlign: TextAlign.center,
                                  style: TextStyle(
                                    fontSize: 10,
                                    fontWeight: selected == item.$1
                                        ? FontWeight.w800
                                        : FontWeight.w500,
                                    color: selected == item.$1
                                        ? c.primary
                                        : c.onSurfaceVariant,
                                  ),
                                ),
                              ],
                            ),
                          ),
                        ),
                      ),
                    ),
                  ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
