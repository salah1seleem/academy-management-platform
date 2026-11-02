import 'package:academy_mobile/features/shared/occupancy_summary.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  for (final lang in ['ar', 'en']) {
    testWidgets('Visit participants and companions stay distinct in $lang', (
      tester,
    ) async {
      await tester.pumpWidget(
        MaterialApp(
          locale: Locale(lang),
          supportedLocales: const [Locale('ar'), Locale('en')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          home: MediaQuery(
            data: const MediaQueryData(textScaler: TextScaler.linear(2)),
            child: const Scaffold(
              body: SingleChildScrollView(
                child: OccupancySummary(
                  value: {'participants': 6, 'companions': 2, 'staleOpen': 1},
                ),
              ),
            ),
          ),
        ),
      );
      expect(
        find.textContaining(
          lang == 'ar' ? '6 مشارك · 2 مرافق' : '6 participants · 2 companions',
        ),
        findsOneWidget,
      );
      expect(
        find.textContaining(
          lang == 'ar' ? 'إشغال تقديري' : 'estimated occupancy',
        ),
        findsOneWidget,
      );
      expect(
        find.textContaining(lang == 'ar' ? 'تحتاج مراجعة' : 'review required'),
        findsOneWidget,
      );
      expect(find.byType(ElevatedButton), findsNothing);
      expect(tester.takeException(), isNull);
    });
  }
}
