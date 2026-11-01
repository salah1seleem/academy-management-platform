import 'package:flutter_test/flutter_test.dart';
import 'package:flutter/material.dart';

/// Advances the actual manual introduction; never bypasses its completion callback.
Future<void> finishRoleIntroduction(WidgetTester t) async {
  for (var page = 0; page < 8; page++) {
    final next = find.text('التالي').evaluate().isNotEmpty
        ? find.text('التالي')
        : find.text('Next');
    if (next.evaluate().isEmpty) break;
    await t.ensureVisible(next);
    await t.pumpAndSettle();
    await t.tap(next);
    await t.pumpAndSettle();
  }
  final start = find.widgetWithText(FilledButton, 'ابدأ').evaluate().isNotEmpty
      ? find.widgetWithText(FilledButton, 'ابدأ')
      : find.widgetWithText(FilledButton, 'Start now');
  expect(start, findsOneWidget);
  await t.ensureVisible(start);
  await t.pumpAndSettle();
  await t.tap(start);
  await t.pump();
}
