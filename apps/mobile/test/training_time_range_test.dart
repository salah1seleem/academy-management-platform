import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('A training range identifies both Arabic endpoints including noon and midnight', () {
    expect(
      trainingTimeRange('16:30:00', '17:30:00'),
      'من 4:30 مساءً إلى 5:30 مساءً',
    );
    expect(
      trainingTimeRange('11:00', '12:00'),
      'من 11:00 صباحًا إلى 12:00 مساءً',
    );
    expect(
      trainingTimeRange('00:00', '01:00'),
      'من 12:00 صباحًا إلى 1:00 صباحًا',
    );
    expect(sessionDateLabel('2026-10-29'), contains('الخميس'));
  });
}
