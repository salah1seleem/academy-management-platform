import 'package:academy_mobile/features/admin/native_player_placement.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

void main() {
  for (final cause in [
    ('ApprovedAbsence', 'غياب أو اعتذار معتمد'),
    ('CancelledSession', 'حصة ملغاة'),
    ('CoachAbsent', 'غياب المدربين المسجل'),
  ]) {
    testWidgets(
      'native compensation history identifies its original occurrence and Arabic cause: ${cause.$1}',
      (t) async {
        final auth = await authenticate(
          'AcademyOwner',
          (m, p, b, token) async => [
            {
              'id': 'grant',
              'sport': 'كرة القدم',
              'cause': cause.$1,
              'unit': 'Session',
              'units': 1,
              'allocated': 0,
              'consumed': 0,
              'sourceDate': '2026-10-14',
              'validFrom': '2026-10-22',
              'validTo': '2026-10-29',
              'bookings': <Object?>[],
            },
          ],
        );
        await t.pumpWidget(
          app(
            NativePlayerCompensations(
              auth: auth,
              enrollment: const {'id': 'enrollment'},
              canManage: false,
            ),
          ),
        );
        await t.pumpAndSettle();
        expect(find.textContaining('الحصة الأصلية 2026-10-14'), findsOneWidget);
        expect(find.textContaining(cause.$2), findsOneWidget);
        expect(find.textContaining(cause.$1), findsNothing);
        expect(find.text('حجز حصة بديلة'), findsNothing);
        expect(find.text('اعتماد تعويض'), findsNothing);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox.shrink());
        auth.dispose();
      },
    );
  }
}
