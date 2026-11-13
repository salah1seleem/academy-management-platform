import 'package:academy_mobile/features/guardian/compensation_card.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  for (final locale in ['ar', 'en']) {
    testWidgets(
      'Compensation is a read-only dated entitlement in $locale at large text',
      (tester) async {
        tester.view.physicalSize = const Size(360, 960);
        tester.view.devicePixelRatio = 1;
        addTearDown(tester.view.reset);
        await tester.pumpWidget(
          MaterialApp(
            locale: Locale(locale),
            supportedLocales: const [Locale('ar'), Locale('en')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            home: MediaQuery(
              data: const MediaQueryData(textScaler: TextScaler.linear(2)),
              child: Scaffold(
                body: SingleChildScrollView(
                  child: CompensationCard(
                    today: '2026-11-08',
                    data: const {
                      'sport': 'السباحة',
                      'unit': 'Minute',
                      'units': 120,
                      'allocated': 60,
                      'consumed': 0,
                      'sourceDate': '2026-09-28',
                      'validTo': '2026-11-10',
                      'bookings': [
                        {
                          'state': 'Reserved',
                          'group': 'موعد بديل',
                          'date': '2026-11-11',
                          'startTime': '17:00:00',
                          'endTime': '18:00:00',
                        },
                        {
                          'state': 'Cancelled',
                          'group': 'حجز محرر',
                          'date': '2026-10-20',
                        },
                      ],
                    },
                  ),
                ),
              ),
            ),
          ),
        );
        expect(find.textContaining('120'), findsOneWidget);
        expect(find.textContaining('موعد بديل'), findsOneWidget);
        expect(
          find.text(locale == 'ar' ? 'أُلغي الحجز' : 'Booking cancelled'),
          findsOneWidget,
        );
        expect(
          find.text(
            locale == 'ar'
                ? 'متاح للحجز 60 دقيقة'
                : 'Available to book 60 minutes',
          ),
          findsOneWidget,
        );
        expect(find.byType(ElevatedButton), findsNothing);
        expect(tester.takeException(), isNull);
      },
    );
  }
}
