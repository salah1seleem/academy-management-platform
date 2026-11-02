import 'package:flutter/material.dart';

/// The academy belongs in the header; the task remains the visual focus.
class BrandedHeaderSurface extends StatelessWidget {
  const BrandedHeaderSurface({
    super.key,
    required this.child,
    this.premium = true,
  });
  final Widget child;
  final bool premium;
  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context), c = t.colorScheme;
    return Container(
      key: const ValueKey('academy-branded-header'),
      decoration: BoxDecoration(color: t.scaffoldBackgroundColor),
      child: Theme(
        data: t.copyWith(
          iconButtonTheme: IconButtonThemeData(
            style: IconButton.styleFrom(
              foregroundColor: c.onSurface,
              backgroundColor: c.surface,
              minimumSize: const Size(44, 44),
              fixedSize: const Size(44, 44),
              tapTargetSize: MaterialTapTargetSize.shrinkWrap,
              padding: const EdgeInsets.all(7),
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(14),
                side: BorderSide(color: c.outlineVariant.withValues(alpha: .8)),
              ),
            ),
          ),
        ),
        child: child,
      ),
    );
  }
}
