import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

/// Traverse the visible vertical page; all actions still go through the UI.
Future<void> revealTestTarget(WidgetTester tester, Finder target) async {
  bool present() {
    try {
      return target.evaluate().isNotEmpty;
    } on StateError {
      return false; // .first/.last on a lazily unbuilt control.
    }
  }

  if (!present()) {
    final vertical = find.byWidgetPredicate(
      (w) =>
          w is Scrollable &&
          (w.axisDirection == AxisDirection.down ||
              w.axisDirection == AxisDirection.up),
    );
    expect(
      vertical,
      findsWidgets,
      reason: 'The requested control must exist in the current route.',
    );
    final root = vertical.first;
    Future<void> moveDown() async {
      for (var i = 0; i < 60 && !present(); i++) {
        final position = tester.state<ScrollableState>(root).position;
        if (position.extentAfter < .5) break;
        await tester.drag(root, const Offset(0, -180));
        await tester.pumpAndSettle();
      }
    }

    await moveDown();
    if (!present()) {
      tester.state<ScrollableState>(root).position.jumpTo(0);
      await tester.pumpAndSettle();
      await moveDown();
    }
  }
  expect(target, findsOneWidget);
  await tester.ensureVisible(target);
  await tester.pumpAndSettle();
}

Future<void> expandChildDetails(WidgetTester tester) async {
  final target = find.byKey(const ValueKey('child-home-details'));
  await revealTestTarget(tester, target);
  if (find.byKey(const ValueKey('child-section-0')).evaluate().isEmpty) {
    await tester.tap(target);
    await tester.pumpAndSettle();
  }
  final root = find
      .descendant(
        of: find.byKey(const ValueKey('guardian-home-scroll')),
        matching: find.byType(Scrollable),
      )
      .first;
  tester.state<ScrollableState>(root).position.jumpTo(0);
  await tester.pumpAndSettle();
}
