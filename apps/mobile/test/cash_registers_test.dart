import 'package:academy_mobile/features/owner/cash_registers.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

void main() {
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets(
      'Scoped cash oversight is readable without write controls $width',
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
                child: CashRegisterContent(
                  data: {
                    'cashEnabled': false,
                    'items': [
                      {
                        'id': 'one',
                        'name': 'صندوق اختباري',
                        'branch': 'فرع تجريبي',
                        'currency': 'EGP',
                        'session': {'balance': '150.25', 'opening': '50.00'},
                      },
                    ],
                    'history': [
                      {
                        'id': 'closed',
                        'registerId': 'one',
                        'closedAtUtc': '2026-09-28T14:00:00Z',
                        'expectedAmount': '100.00',
                        'countedAmount': '90.00',
                      },
                    ],
                  },
                ),
              ),
            ),
          ),
        );
        expect(find.text('150.25 ج.م'), findsOneWidget);
        expect(find.text('النقدي متوقف'), findsOneWidget);
        expect(find.text('الفعلي: 90.00 ج.م'), findsOneWidget);
        expect(find.byType(ElevatedButton), findsNothing);
        expect(tester.takeException(), isNull);
      },
    );
  }
}
