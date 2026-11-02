import 'package:academy_mobile/features/owner/swipe_card_deck.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as f;

void main() {
  testWidgets(
    'decisions preview the next card, move slowly and pause on touch',
    (t) async {
      await t.pumpWidget(
        f.app(
          Builder(
            builder: (context) => MediaQuery(
              data: MediaQuery.of(context).copyWith(disableAnimations: false),
              child: const Scaffold(
                body: SwipeCardDeck(
                  height: 220,
                  children: [
                    ColoredBox(color: Colors.red, child: Text('قرار أول')),
                    ColoredBox(color: Colors.blue, child: Text('قرار ثانٍ')),
                  ],
                ),
              ),
            ),
          ),
        ),
      );
      await t.pump();
      await t.pump();
      final rail = find.byKey(const ValueKey('owner-decisions-rail'));
      final controller = t.widget<ListView>(rail).controller!;
      final first = t.getRect(find.text('قرار أول').first);
      final second = t.getRect(find.text('قرار ثانٍ').first);
      expect(first.overlaps(t.getRect(rail)), isTrue);
      expect(second.overlaps(t.getRect(rail)), isTrue);
      final start = controller.offset;
      for (var i = 0; i < 60; i++) {
        await t.pump(const Duration(milliseconds: 50));
      }
      expect(controller.offset - start, 0);
      final touch = await t.startGesture(t.getCenter(rail));
      final stopped = controller.offset;
      for (var i = 0; i < 20; i++) {
        await t.pump(const Duration(milliseconds: 50));
      }
      expect(controller.offset, stopped);
      await touch.up();
      for (var i = 0; i < 110; i++) {
        await t.pump(const Duration(milliseconds: 50));
      }
      expect(controller.offset, stopped);
      await t.pumpWidget(const SizedBox());
      expect(t.takeException(), isNull);
    },
  );
}
