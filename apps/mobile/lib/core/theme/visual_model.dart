import 'package:flutter/material.dart';

enum VisualModel {
  light('فاتح', Icons.light_mode_outlined),
  dark('أزرق', Icons.water_outlined),
  pitch('الملعب', Icons.sports_soccer_outlined),
  premium('داكن مميز', Icons.diamond_outlined);

  const VisualModel(this.label, this.icon);
  final String label;
  final IconData icon;
  bool get fixedPalette => this != light;
}
