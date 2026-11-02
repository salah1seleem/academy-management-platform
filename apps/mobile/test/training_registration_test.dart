import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('Registration formats remain distinct and old profiles stay group-compatible', () {
    expect(trainingRegistrationLabel('Private'), 'تدريب خاص');
    expect(trainingRegistrationLabel('SemiPrivate'), 'تدريب شبه خاص');
    expect(trainingRegistrationLabel('Group'), 'تدريب جماعي');
    expect(trainingRegistrationLabel(null), 'تدريب جماعي');
  });
  testWidgets('Registration vocabulary follows the selected English locale', (
    tester,
  ) async {
    await tester.pumpWidget(
      const MaterialApp(locale: Locale('en'), home: UiText('تدريب شبه خاص')),
    );
    expect(find.text('Semi-private training'), findsOneWidget);
  });
}
