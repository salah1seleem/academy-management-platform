import 'package:flutter/material.dart';

abstract final class AcademyTheme {
  static const teal = Color(0xFF12B8B2), gold = Color(0xFFE8B94E);
  static const night = Color(0xFF0C101B),
      card = Color(0xFF242330),
      raised = Color(0xFF302E3C),
      red = Color(0xFFE3072D);

  static ThemeData app() => ThemeData(
    brightness: Brightness.dark,
    useMaterial3: true,
    fontFamily: 'Cairo',
    colorScheme: const ColorScheme.dark(
      primary: gold,
      secondary: red,
      tertiary: teal,
      surface: card,
      error: Color(0xFFFF6673),
    ),
    scaffoldBackgroundColor: night,
    appBarTheme: const AppBarTheme(
      backgroundColor: night,
      foregroundColor: Colors.white,
      centerTitle: true,
      elevation: 0,
      surfaceTintColor: Colors.transparent,
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: card,
      hintStyle: const TextStyle(color: Colors.white54),
      border: OutlineInputBorder(
        borderRadius: BorderRadius.circular(16),
        borderSide: BorderSide.none,
      ),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        minimumSize: const Size.fromHeight(50),
        backgroundColor: red,
        foregroundColor: Colors.white,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(15)),
      ),
    ),
    cardTheme: CardThemeData(
      elevation: 0,
      color: card,
      surfaceTintColor: Colors.transparent,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(18)),
    ),
    navigationBarTheme: const NavigationBarThemeData(
      backgroundColor: Color(0xFF171A25),
      indicatorColor: Color(0x33E8B94E),
      labelTextStyle: WidgetStatePropertyAll(TextStyle(fontSize: 11)),
      iconTheme: WidgetStatePropertyAll(IconThemeData(color: gold)),
    ),
    dividerColor: Colors.white12,
  );

  @Deprecated('الهوية الموحدة أصبحت داكنة لكل الأدوار')
  static ThemeData light() => app();
}
