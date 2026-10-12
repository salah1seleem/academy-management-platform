import 'package:academy_mobile/features/owner/settlement_comparisons.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

void main() {
  for (final width in [360.0, 430.0]) {
    testWidgets(
      'Owner statement review distinguishes gross fees and net at $width',
      (tester) async {
        tester.view.physicalSize = Size(width, 1800);
        tester.view.devicePixelRatio = 1;
        addTearDown(tester.view.resetPhysicalSize);
        addTearDown(tester.view.resetDevicePixelRatio);
        await tester.pumpWidget(
          MaterialApp(
            locale: const Locale('ar'),
            supportedLocales: const [Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            theme: guardianTheme(),
            home: const Scaffold(
              body: Directionality(
                textDirection: TextDirection.rtl,
                child: SettlementComparisonContent(
                  data: {
                    'page': 1,
                    'total': 1,
                    'pageSize': 25,
                    'items': [
                      {
                        'statementReference': 'كشف اصطناعي',
                        'lineReference': '1',
                        'accountTag': 'DEMO',
                        'result': 'Matched',
                        'gross': '900.00',
                        'fee': '5.00',
                        'net': '895.00',
                        'currency': 'EGP',
                        'settlementDate': '2026-09-28',
                      },
                    ],
                  },
                ),
              ),
            ),
          ),
        );
        expect(find.text('مطابق يدويًا'), findsOneWidget);
        expect(find.textContaining('900.00'), findsOneWidget);
        expect(find.textContaining('895.00'), findsOneWidget);
        expect(find.text('تأكيد الدفع'), findsNothing);
        expect(tester.takeException(), isNull);
      },
    );
  }
}
