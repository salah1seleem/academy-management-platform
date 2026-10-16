import 'package:academy_mobile/features/coach/device_check_in.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'coach_test.dart' show coach, shell;

class FakeDevice implements CheckInDevice {
  @override
  Future<bool> confirmBiometric() async => true;
  @override
  Future<CheckInFix> currentLocation() async => const CheckInFix(30.05, 31.3, 5);
}

void main() {
  testWidgets('Coach confirms device biometric and location for assigned branch only', (tester) async {
    final sent = <Object?>[];
    final auth = await coach((method, path, body, token) async {
      if (method == 'POST') {
        sent.add(body);
        return {'id': 'attendance'};
      }
      return [{'id': 'branch-1', 'name': 'فرع تجريبي', 'radiusMeters': 50}];
    });
    await tester.pumpWidget(shell(DeviceCheckIn(auth: auth, device: FakeDevice())));
    await tester.pumpAndSettle();
    await tester.tap(find.byType(DropdownButtonFormField<String>));
    await tester.pumpAndSettle();
    await tester.tap(find.text('فرع تجريبي — 50 مترًا').last);
    await tester.pumpAndSettle();
    await tester.tap(find.text('تأكيد البصمة والموقع'));
    await tester.pumpAndSettle();
    expect(sent.single, {
      'branchId': 'branch-1',
      'latitude': 30.05,
      'longitude': 31.3,
      'accuracyMeters': 5.0,
      'clientBiometricAcknowledged': true,
    });
    expect(find.text('تم تسجيل حضورك اليوم في نطاق الفرع.'), findsOneWidget);
  });
}
