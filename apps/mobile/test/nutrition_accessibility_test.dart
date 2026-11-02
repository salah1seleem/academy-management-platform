import 'package:academy_mobile/features/guardian/guardian_content.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'guardian_test.dart' show logged, shell;

void main() {
  testWidgets(
    'small phone with large Arabic text can scroll the dated plan and change meals without overflow',
    (t) async {
      await t.binding.setSurfaceSize(const Size(320, 568));
      addTearDown(() => t.binding.setSurfaceSize(null));
      final paths = <String>[];
      final auth = await logged((m, p, b, k) async {
        paths.add(p);
        expect(m, 'GET');
        if (p.contains('/recommendations')) {
          return {
            'date': '2026-10-26',
            'isTrainingDay': false,
            'disclaimer': 'معلومات عامة وليست وصفة طبية.',
            for (final meal in ['breakfast', 'lunch', 'dinner'])
              meal: {
                'id': meal,
                'arabicName': 'وجبة خيالية للتجربة',
                'imageReference': '',
              },
          };
        }
        return [
          {'id': 'meal', 'arabicName': 'وجبة خيالية', 'imageReference': ''},
        ];
      });
      await t.pumpWidget(
        shell(
          MediaQuery(
            data: const MediaQueryData(
              size: Size(320, 568),
              textScaler: TextScaler.linear(2),
            ),
            child: NutritionLibrary(
              auth: auth,
              playerId: 'child',
              today: '2026-10-25',
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('خطة اليوم'), findsOneWidget);
      await t.tap(find.text('خطة اليوم'));
      await t.pumpAndSettle();
      expect(find.text('اقتراح نظام غذائي ليوم 2026-10-26'), findsOneWidget);
      expect(find.text('معلومات عامة وليست وصفة طبية.'), findsOneWidget);
      await t.tap(find.text('خطة اليوم'));
      await t.pumpAndSettle();
      expect(t.takeException(), isNull);
      final lunch = find.widgetWithText(ChoiceChip, 'الغداء');
      await t.scrollUntilVisible(
        lunch,
        180,
        scrollable: find.byType(Scrollable).first,
      );
      await t.ensureVisible(lunch);
      await t.pumpAndSettle();
      expect(lunch.hitTestable(), findsOneWidget);
      await t.tap(lunch);
      await t.pumpAndSettle();
      expect(paths, contains('/api/v1/guardian/nutrition?category=Lunch'));
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
