import 'package:academy_mobile/features/owner/provider_quarantine.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

void main() {
  for (final width in [360.0, 430.0]) {
    testWidgets('Owner discrepancy review has no payment approval at $width', (
      tester,
    ) async {
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
              child: ProviderQuarantineContent(
                data: {
                  'page': 1,
                  'total': 1,
                  'pageSize': 25,
                  'items': [
                    {
                      'player': 'لاعب اصطناعي',
                      'branch': 'فرع تجريبي',
                      'reasonCode': 'amount-mismatch',
                      'expectedAmount': '900.00',
                      'receivedAmount': '925.00',
                      'expectedCurrency': 'EGP',
                      'receivedCurrency': 'EGP',
                      'createdAtUtc': '2026-09-28T08:00:00Z',
                      'paymentStatus': 'Confirmed',
                    },
                  ],
                },
              ),
            ),
          ),
        ),
      );
      expect(find.text('لاعب اصطناعي'), findsOneWidget);
      expect(find.text('المبلغ مختلف'), findsOneWidget);
      expect(find.textContaining('900'), findsOneWidget);
      expect(find.textContaining('925'), findsOneWidget);
      expect(
        find.textContaining('الطلب مؤكد بإشارة صحيحة أخرى'),
        findsOneWidget,
      );
      expect(find.text('قبول الدفع'), findsNothing);
      expect(tester.takeException(), isNull);
    });
  }
}
