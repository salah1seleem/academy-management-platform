import 'package:flutter/material.dart';

import 'academy_brand.dart';
import 'ocean_palette.dart';
import 'appearance.dart' show CanonicalAcademyLogo, AppearanceScope;

Color academyIdentityBase(BuildContext context) =>
    OceanPalette.active(Theme.of(context))
    ? OceanPalette.surface
    : Theme.of(context).brightness == Brightness.light
    ? Colors.white
    : const Color(0xFF191C22);
LinearGradient academyIdentityGradient(BuildContext context) {
  if (AppearanceScope.of(context)?.model.fixedPalette == true) {
    final c = Theme.of(context).colorScheme;
    return LinearGradient(colors: [c.surface, c.surfaceContainerHighest]);
  }
  final brand = BrandScope.maybeOf(context),
      dark = Theme.of(context).brightness == Brightness.dark;
  final colors = brand?.artwork?.colors ?? <Color>[];
  final primary =
      colors.firstOrNull ??
      brand?.logoColor ??
      brand?.primary ??
      Theme.of(context).colorScheme.primary;
  final secondary = colors.length > 1
      ? colors[1]
      : brand?.artwork?.ink ?? primary;
  final base = academyIdentityBase(context);
  return LinearGradient(
    begin: Alignment.topRight,
    end: Alignment.bottomLeft,
    stops: const [0, .46, 1],
    colors: [
      Color.alphaBlend(primary.withValues(alpha: dark ? .16 : .105), base),
      Color.alphaBlend(primary.withValues(alpha: dark ? .035 : .018), base),
      Color.alphaBlend(secondary.withValues(alpha: dark ? .09 : .055), base),
    ],
  );
}

Color academyAccent(BuildContext context) =>
    Theme.of(context).colorScheme.primary;

Color academyInk(BuildContext context) =>
    Theme.of(context).brightness == Brightness.dark
    ? Color.lerp(academyAccent(context), Colors.white, .65)!
    : academyAccent(context);

class AcademyHomeSkin extends StatelessWidget {
  const AcademyHomeSkin({super.key, required this.child});
  final Widget child;
  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context), accent = academyAccent(context);
    return Theme(
      data: theme.copyWith(
        cardTheme: theme.cardTheme.copyWith(color: theme.colorScheme.surface),
        colorScheme: theme.colorScheme.copyWith(primary: accent),
      ),
      child: child,
    );
  }
}

class AcademyLogoMark extends StatelessWidget {
  const AcademyLogoMark({super.key, this.opacity = .12});
  final double opacity;
  @override
  Widget build(BuildContext context) {
    final brand = BrandScope.maybeOf(context);
    final logo = brand?.logo;
    if (logo == null && brand?.signedIn == true) return const SizedBox.shrink();
    return IgnorePointer(
      child: ExcludeSemantics(
        child: Opacity(
          opacity: opacity,
          child: logo == null
              ? Image.asset(
                  'assets/branding/professional-academy.png',
                  fit: BoxFit.contain,
                )
              : brand?.artwork?.transparent == true &&
                    brand!.artwork!.colors.isEmpty
              ? ColorFiltered(
                  colorFilter: ColorFilter.mode(
                    Theme.of(context).colorScheme.onSurface,
                    BlendMode.srcIn,
                  ),
                  child: const CanonicalAcademyLogo(),
                )
              : const CanonicalAcademyLogo(),
        ),
      ),
    );
  }
}
