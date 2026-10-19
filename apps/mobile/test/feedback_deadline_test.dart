import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:academy_mobile/features/shared/feedback_deadline.dart';

void main() {
  testWidgets(
    'deadline evidence is captured, legacy and malformed stay explicit',
    (tester) async {
      Future<void> show(String? value) => tester.pumpWidget(
        MaterialApp(
          home: Scaffold(body: FeedbackDeadlineEvidence(evidence: value)),
        ),
      );
      await show(null);
      expect(find.textContaining('لم تحفظ سياسة'), findsOneWidget);
      await show(
        '{"revision":7,"days":2,"receivedOn":"2026-10-19","dueOn":"2026-10-21","timeZone":"Africa/Cairo"}',
      );
      expect(find.textContaining('الإصدار 7'), findsOneWidget);
      expect(find.textContaining('2026-10-21'), findsOneWidget);
      await show('broken');
      expect(find.textContaining('تعذر عرض دليل'), findsOneWidget);
    },
  );
}
