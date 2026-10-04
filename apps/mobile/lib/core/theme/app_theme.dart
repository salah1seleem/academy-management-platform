import 'package:flutter/material.dart';

abstract final class AcademyTheme {
  static const teal = Color(0xFF106F66), gold = Color(0xFFE1B451);
  static const night = Color(0xFF0C101B),
      card = Color(0xFF242330),
      red = Color(0xFFE3072D);
  static ThemeData light() => ThemeData(
    useMaterial3: true,
    fontFamily: 'Cairo',
    colorScheme: ColorScheme.fromSeed(seedColor: teal),
    scaffoldBackgroundColor: const Color(0xFFF2F7F5),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: Colors.white,
      border: OutlineInputBorder(borderRadius: BorderRadius.circular(14)),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        minimumSize: const Size.fromHeight(50),
        backgroundColor: teal,
      ),
    ),
    cardTheme: CardThemeData(
      elevation: 0,
      color: Colors.white,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(18)),
    ),
  );
}
