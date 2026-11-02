import 'package:academy_mobile/features/shared/observation_summary.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  for (final lang in ['ar', 'en']) {
    testWidgets(
      'Observed count is read-only and distinct from recorded count in $lang at 200% text',
      (tester) async {
        await tester.pumpWidget(
          MaterialApp(
            locale: Locale(lang),
            supportedLocales: const [Locale('ar'), Locale('en')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            home: MediaQuery(
              data: const MediaQueryData(textScaler: TextScaler.linear(2)),
              child: const Scaffold(
                body: SingleChildScrollView(
                  child: ObservationSummary(
                    value: {
                      'count': 7,
                      'expectedSnapshot': 6,
                      'recordedPresentSnapshot': 5,
                      'expectedBasis': 'Roster',
                      'needsReview': true,
                      'outsideBookedHours': true,
                    },
                  ),
                ),
              ),
            ),
          ),
        );
        expect(
          find.textContaining(lang == 'ar' ? '7 ملحوظ فعليًا' : '7 observed'),
          findsOneWidget,
        );
        expect(
          find.textContaining(
            lang == 'ar' ? 'فرق يحتاج تحققًا' : 'Difference needs verification',
          ),
          findsOneWidget,
        );
        expect(find.byType(ElevatedButton), findsNothing);
        expect(tester.takeException(), isNull);
      },
    );
  }
}
