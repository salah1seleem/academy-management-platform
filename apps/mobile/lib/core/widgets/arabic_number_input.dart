import 'package:flutter/services.dart';

/// Canonical digits without rounding, stripping punctuation, or losing zeros.
String canonicalNumericInput(String value) => String.fromCharCodes(
  value.runes.map((r) {
    if (r >= 0x0660 && r <= 0x0669) return 0x30 + r - 0x0660;
    if (r >= 0x06f0 && r <= 0x06f9) return 0x30 + r - 0x06f0;
    return r == 0x066b ? 0x2e : r;
  }),
);

class ArabicNumberInputFormatter extends TextInputFormatter {
  const ArabicNumberInputFormatter();
  @override
  TextEditingValue formatEditUpdate(
    TextEditingValue oldValue,
    TextEditingValue newValue,
  ) => newValue.copyWith(text: canonicalNumericInput(newValue.text));
}
