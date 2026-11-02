import 'package:flutter/material.dart';

/// The approved Ocean Blue model. Academy artwork does not recolor this palette.
/// VisualModel.dark remains the persisted key for existing installations.
abstract final class OceanPalette {
  static const background = Color(0xFF10479D),
      surface = Color(0xFF1550A0),
      raised = Color(0xFF205AAB),
      line = Color(0xFF5B8FD0),
      ink = Color(0xFFF5FAFF),
      muted = Color(0xFFD6E9FF),
      accent = Color(0xFF95E4FF),
      onAccent = Color(0xFF09366C),
      positive = Color(0xFFA6F5D8),
      warning = Color(0xFFFFE4AA),
      negative = Color(0xFFFFD2DB);

  static bool active(ThemeData theme) =>
      theme.scaffoldBackgroundColor == background;
}
