import 'package:academy_mobile/features/shared/experience_ui.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';

void main() {
  test('money uses readable Latin digits and preserves cents', () {
    expect(money(27300.0, 'EGP'), '27,300 ج.م');
    expect(money(-1200.25, 'EGP'), '-1,200.25 ج.م');
    expect(money(0, 'EGP'), '0 ج.م');
  });
  test(
    'rolling calendar month clamps end-of-month without fixed demo dates',
    () {
      expect(isoDay(trailingMonth(DateTime(2026, 10, 18))), '2026-09-18');
      expect(isoDay(trailingMonth(DateTime(2026, 3, 31))), '2026-02-28');
      expect(isoDay(trailingMonth(DateTime(2028, 3, 31))), '2028-02-29');
      expect(isoDay(trailingMonth(DateTime(2026, 1, 31))), '2025-12-31');
    },
  );
}
