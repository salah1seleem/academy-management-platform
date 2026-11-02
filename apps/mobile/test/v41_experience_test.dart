import 'package:academy_mobile/features/shared/experience_ui.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_reports.dart';
import 'package:academy_mobile/features/shared/report_export.dart';
import 'package:flutter/material.dart';

import 'owner_test.dart' as fixtures;

void main() {
  testWidgets(
    'report export retains its displayed period and omits an empty branch GUID',
    (tester) async {
      final auth = await fixtures.homeOwner(
        (m, p, b, t) async => p.contains('structure/options')
            ? {'branches': <Object>[]}
            : {
                'items': <Object>[],
                'totalAmount': 0,
                'netAmount': 0,
                'currency': 'EGP',
                'totalCount': 0,
              },
      );
      await tester.pumpWidget(
        fixtures.shell(OwnerReports(auth: auth, initialReport: 'financial')),
      );
      await tester.pumpAndSettle();
      final button = tester.widget<ReportExportButton>(
        find.byType(ReportExportButton),
      );
      final query = Uri.parse(button.path).queryParameters;
      expect(query['from'], '2026-08-28');
      expect(query['to'], '2026-09-28');
      expect(query.containsKey('branchId'), isFalse);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
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
