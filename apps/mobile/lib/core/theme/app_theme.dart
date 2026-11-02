import 'package:flutter/material.dart';

import 'visual_model.dart';
import 'ocean_palette.dart';

/// One coherent visual language for all roles, routes and appearance models.
abstract final class AcademyTheme {
  static const teal = Color(0xFF39CDB0), gold = Color(0xFFE4C77D);
  static const night = Color(0xFF0A1524),
      card = Color(0xFF132337),
      red = Color(0xFFD71935),
      blue = Color(0xFF397AF2);

  static ThemeData light({Color primary = red}) => _build(false, primary);
  static ThemeData dark({Color primary = red}) =>
      _build(true, OceanPalette.accent, model: VisualModel.dark);
  static ThemeData themed(VisualModel model) => _build(true, switch (model) {
    VisualModel.dark => OceanPalette.accent,
    VisualModel.pitch => const Color(0xFFB6ED5B),
    _ => gold,
  }, model: model);

  static Color _readable(Color color, Color background) {
    var result = color;
    for (var i = 0; i < 12; i++) {
      final a = result.computeLuminance(), b = background.computeLuminance();
      if (((a > b ? a : b) + .05) / ((a > b ? b : a) + .05) >= 4.5) break;
      result = Color.lerp(
        result,
        background.computeLuminance() < .3 ? Colors.white : Colors.black,
        .12,
      )!;
    }
    return result;
  }

  static ThemeData _build(bool dark, Color configured, {VisualModel? model}) {
    final pitch = model == VisualModel.pitch,
        premium = model == VisualModel.premium,
        ocean = model == VisualModel.dark;
    final background = ocean
        ? OceanPalette.background
        : pitch
        ? const Color(0xFF09271F)
        : premium
        ? const Color(0xFF111416)
        : dark
        ? night
        : const Color(0xFFF3F6FA);
    final surface = ocean
        ? OceanPalette.surface
        : pitch
        ? const Color(0xFF12392D)
        : premium
        ? const Color(0xFF1C2022)
        : dark
        ? card
        : Colors.white;
    final raised = ocean
        ? OceanPalette.raised
        : pitch
        ? const Color(0xFF1A4637)
        : premium
        ? const Color(0xFF292C2C)
        : dark
        ? const Color(0xFF1B3048)
        : const Color(0xFFEAF0F7);
    final ink = ocean
        ? OceanPalette.ink
        : dark
        ? const Color(0xFFF1F6FC)
        : const Color(0xFF14263D);
    final muted = ocean
        ? OceanPalette.muted
        : pitch
        ? const Color(0xFFA9C9B9)
        : premium
        ? const Color(0xFFB9B5A9)
        : dark
        ? const Color(0xFF9FB2C8)
        : const Color(0xFF61748A);
    final line = ocean
        ? OceanPalette.line
        : pitch
        ? const Color(0xFF2F5B49)
        : premium
        ? const Color(0xFF40453F)
        : dark
        ? const Color(0xFF2A3D53)
        : const Color(0xFFDDE6EF);
    final chosen = configured == red
        ? (dark ? const Color(0xFF61CEE0) : const Color(0xFF087D98))
        : configured;
    final accent = _readable(chosen, background);
    final onAccent = ocean
        ? OceanPalette.onAccent
        : accent.computeLuminance() > .36
        ? const Color(0xFF10231E)
        : Colors.white;
    final c =
        ColorScheme.fromSeed(
          seedColor: accent,
          brightness: dark ? Brightness.dark : Brightness.light,
        ).copyWith(
          primary: accent,
          onPrimary: onAccent,
          primaryContainer: accent.withValues(alpha: .12),
          onPrimaryContainer: accent,
          secondary: accent,
          onSecondary: onAccent,
          secondaryContainer: raised,
          onSecondaryContainer: ink,
          tertiary: accent,
          onTertiary: onAccent,
          tertiaryContainer: raised,
          onTertiaryContainer: ink,
          surface: surface,
          onSurface: ink,
          onSurfaceVariant: muted,
          surfaceContainerLowest: background,
          surfaceContainerLow: surface,
          surfaceContainer: surface,
          surfaceContainerHigh: raised,
          surfaceContainerHighest: raised,
          outline: line,
          outlineVariant: line,
          error: ocean
              ? OceanPalette.negative
              : dark
              ? const Color(0xFFFF8D96)
              : const Color(0xFFB62F45),
          errorContainer: dark
              ? const Color(0xFF452734)
              : const Color(0xFFFFEEF0),
          onErrorContainer: dark
              ? const Color(0xFFFFCAD0)
              : const Color(0xFF8D2035),
          shadow: const Color(0xFF112841),
          surfaceTint: Colors.transparent,
        );
    const family = 'AcademyArabic';
    final text = TextTheme(
      headlineLarge: TextStyle(
        fontFamily: family,
        fontSize: 32,
        height: 1.25,
        fontWeight: FontWeight.w800,
        color: ink,
      ),
      headlineMedium: TextStyle(
        fontFamily: family,
        fontSize: 28,
        height: 1.3,
        fontWeight: FontWeight.w800,
        color: ink,
      ),
      headlineSmall: TextStyle(
        fontFamily: family,
        fontSize: 24,
        height: 1.35,
        fontWeight: FontWeight.w800,
        color: ink,
      ),
      titleLarge: TextStyle(
        fontFamily: family,
        fontSize: 21,
        height: 1.4,
        fontWeight: FontWeight.w800,
        color: ink,
      ),
      titleMedium: TextStyle(
        fontFamily: family,
        fontSize: 16,
        height: 1.5,
        fontWeight: FontWeight.w700,
        color: ink,
      ),
      titleSmall: TextStyle(
        fontFamily: family,
        fontSize: 14,
        height: 1.5,
        fontWeight: FontWeight.w700,
        color: ink,
      ),
      bodyLarge: TextStyle(
        fontFamily: family,
        fontSize: 16,
        height: 1.6,
        color: ink,
      ),
      bodyMedium: TextStyle(
        fontFamily: family,
        fontSize: 14,
        height: 1.6,
        color: ink,
      ),
      bodySmall: TextStyle(
        fontFamily: family,
        fontSize: 12,
        height: 1.5,
        color: muted,
      ),
      labelLarge: TextStyle(
        fontFamily: family,
        fontSize: 14,
        height: 1.4,
        fontWeight: FontWeight.w700,
        color: ink,
      ),
      labelMedium: TextStyle(
        fontFamily: family,
        fontSize: 12,
        height: 1.4,
        fontWeight: FontWeight.w600,
        color: muted,
      ),
      labelSmall: TextStyle(
        fontFamily: family,
        fontSize: 11,
        height: 1.4,
        fontWeight: FontWeight.w600,
        color: muted,
      ),
    );
    final shape = RoundedRectangleBorder(
      borderRadius: BorderRadius.circular(22),
      side: BorderSide(color: line.withValues(alpha: .8)),
    );
    final buttonShape = RoundedRectangleBorder(
      borderRadius: BorderRadius.circular(15),
    );
    return ThemeData(
      useMaterial3: true,
      fontFamily: family,
      colorScheme: c,
      textTheme: text,
      scaffoldBackgroundColor: background,
      primaryColor: accent,
      visualDensity: VisualDensity.standard,
      appBarTheme: AppBarTheme(
        backgroundColor: background,
        foregroundColor: ink,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        scrolledUnderElevation: 0,
        centerTitle: false,
        titleSpacing: 20,
        titleTextStyle: text.titleLarge,
      ),
      cardTheme: CardThemeData(
        color: surface,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        margin: EdgeInsets.zero,
        shape: shape,
      ),
      dividerTheme: DividerThemeData(
        color: line.withValues(alpha: .8),
        thickness: 1,
        space: 24,
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: surface,
        contentPadding: const EdgeInsets.symmetric(
          horizontal: 16,
          vertical: 17,
        ),
        labelStyle: TextStyle(color: muted, fontSize: 14, fontFamily: family),
        floatingLabelStyle: TextStyle(
          color: accent,
          fontSize: 13,
          fontWeight: FontWeight.w700,
        ),
        hintStyle: TextStyle(color: muted.withValues(alpha: .8), fontSize: 14),
        errorMaxLines: 3,
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(15),
          borderSide: BorderSide(color: line),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(15),
          borderSide: BorderSide(color: line),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(15),
          borderSide: BorderSide(color: accent, width: 1.8),
        ),
        errorBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(15),
          borderSide: BorderSide(color: c.error),
        ),
        focusedErrorBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(15),
          borderSide: BorderSide(color: c.error, width: 1.8),
        ),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: accent,
          foregroundColor: onAccent,
          shape: buttonShape,
          minimumSize: const Size(48, 52),
          padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 14),
          textStyle: text.labelLarge,
        ),
      ),
      elevatedButtonTheme: ElevatedButtonThemeData(
        style: ElevatedButton.styleFrom(
          backgroundColor: accent,
          foregroundColor: onAccent,
          elevation: 0,
          shape: buttonShape,
          minimumSize: const Size(48, 52),
          textStyle: text.labelLarge,
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          shape: buttonShape,
          foregroundColor: ink,
          side: BorderSide(color: line),
          minimumSize: const Size(48, 50),
          padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 12),
          textStyle: text.labelLarge,
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          shape: buttonShape,
          foregroundColor: accent,
          minimumSize: const Size(44, 44),
          textStyle: text.labelLarge,
        ),
      ),
      iconButtonTheme: IconButtonThemeData(
        style: IconButton.styleFrom(
          minimumSize: const Size(44, 44),
          foregroundColor: ink,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(14),
          ),
        ),
      ),
      chipTheme: ChipThemeData(
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
        showCheckmark: false,
        backgroundColor: surface,
        selectedColor: Color.alphaBlend(accent.withValues(alpha: .12), surface),
        side: BorderSide(color: line),
        padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 7),
        labelStyle: text.labelMedium!.copyWith(color: ink),
      ),
      dialogTheme: DialogThemeData(
        backgroundColor: surface,
        surfaceTintColor: Colors.transparent,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(28)),
        titleTextStyle: text.titleLarge,
        contentTextStyle: text.bodyMedium,
      ),
      bottomSheetTheme: BottomSheetThemeData(
        backgroundColor: surface,
        surfaceTintColor: Colors.transparent,
        shape: const RoundedRectangleBorder(
          borderRadius: BorderRadius.vertical(top: Radius.circular(28)),
        ),
        showDragHandle: true,
        dragHandleColor: line,
      ),
      popupMenuTheme: PopupMenuThemeData(
        color: surface,
        surfaceTintColor: Colors.transparent,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(18)),
        textStyle: text.bodyMedium,
      ),
      listTileTheme: ListTileThemeData(
        contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
        iconColor: accent,
        textColor: ink,
        titleTextStyle: text.titleSmall,
        subtitleTextStyle: text.bodySmall,
        minVerticalPadding: 12,
      ),
      navigationBarTheme: NavigationBarThemeData(
        backgroundColor: surface,
        surfaceTintColor: Colors.transparent,
        indicatorColor: accent.withValues(alpha: .13),
        indicatorShape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(14),
        ),
        height: 76,
        labelTextStyle: WidgetStateProperty.resolveWith(
          (s) => text.labelSmall!.copyWith(
            color: s.contains(WidgetState.selected) ? accent : muted,
            fontWeight: s.contains(WidgetState.selected)
                ? FontWeight.w800
                : FontWeight.w500,
          ),
        ),
        iconTheme: WidgetStateProperty.resolveWith(
          (s) => IconThemeData(
            size: 23,
            color: s.contains(WidgetState.selected) ? accent : muted,
          ),
        ),
      ),
      progressIndicatorTheme: ProgressIndicatorThemeData(
        color: accent,
        linearTrackColor: raised,
        borderRadius: BorderRadius.circular(8),
      ),
      snackBarTheme: SnackBarThemeData(
        behavior: SnackBarBehavior.floating,
        backgroundColor: ink,
        contentTextStyle: text.bodyMedium!.copyWith(color: background),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      ),
      tooltipTheme: TooltipThemeData(
        decoration: BoxDecoration(
          color: ink,
          borderRadius: BorderRadius.circular(10),
        ),
        textStyle: text.bodySmall!.copyWith(color: background),
      ),
      dataTableTheme: DataTableThemeData(
        headingTextStyle: text.labelMedium!.copyWith(color: muted),
        dataTextStyle: text.bodyMedium,
        headingRowHeight: 52,
        dataRowMinHeight: 54,
        dividerThickness: 1,
      ),
      tabBarTheme: TabBarThemeData(
        labelColor: accent,
        unselectedLabelColor: muted,
        indicatorColor: accent,
        labelStyle: text.labelLarge,
        unselectedLabelStyle: text.labelMedium,
        dividerColor: line,
      ),
    );
  }
}
