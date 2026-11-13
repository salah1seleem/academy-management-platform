import 'package:academy_mobile/features/owner/cash_registers.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

void main() {
  for (final width in [320.0, 360.0, 390.0, 430.0]) {
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
                        'expectedAmount': 310,
                        'countedAmount': '300.25',
                      },
                      {
                        'id': 'equal',
                        'registerId': 'one',
                        'expectedAmount': '100',
                        'countedAmount': 100.0,
                      },
                      {
                        'id': 'overage',
                        'registerId': 'one',
                        'expectedAmount': '0.01',
                        'countedAmount': '0.03',
                      },
                      {
                        'id': 'unreadable',
                        'registerId': 'one',
                        'expectedAmount': '100.001',
                        'countedAmount': null,
                      },
                    ],
                  },
                ),
              ),
            ),
          ),
        );
        expect(find.text('150.25 ج.م'), findsOneWidget);
        expect(find.text('الرصيد الدفتري'), findsOneWidget);
        expect(find.text('النقدي متوقف'), findsOneWidget);
        expect(find.text('المتوقع: 310 ج.م'), findsOneWidget);
        expect(find.text('الفعلي: 300.25 ج.م'), findsOneWidget);
        expect(find.text('عجز 9.75 ج.م'), findsOneWidget);
        expect(find.text('متطابق'), findsOneWidget);
        expect(find.text('زيادة 0.02 ج.م'), findsOneWidget);
        expect(find.text('فرق المطابقة غير متاح'), findsOneWidget);
        expect(find.text('الفعلي: غير متاح'), findsOneWidget);
        expect(find.byType(ElevatedButton), findsNothing);
        expect(tester.takeException(), isNull);
      },
    );
  }
  testWidgets('Distinguishes confirmed zero from missing cash data', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        locale: const Locale('ar'),
        supportedLocales: const [Locale('ar')],
        localizationsDelegates: GlobalMaterialLocalizations.delegates,
        home: const Scaffold(
          body: CashRegisterContent(
            data: {
              'items': [
                {
                  'id': 'zero',
                  'name': 'صندوق بصفر مؤكد',
                  'branch': 'فرع تجريبي',
                  'currency': 'EGP',
                  'session': {'balance': '0.00', 'opening': '0.00'},
                },
                {
                  'id': 'missing',
                  'name': 'صندوق ببيانات ناقصة',
                  'branch': 'فرع تجريبي',
                  'currency': 'EGP',
                  'session': {'opening': 'invalid'},
                },
              ],
              'history': [],
            },
          ),
        ),
      ),
    );
    expect(find.text('تعذر التحقق من حالة النقدي'), findsOneWidget);
    expect(find.text('النقدي متوقف'), findsNothing);
    expect(find.text('0 ج.م'), findsOneWidget);
    expect(find.text('العهدة الافتتاحية: 0 ج.م'), findsOneWidget);
    expect(find.text('غير متاح'), findsOneWidget);
    expect(find.text('العهدة الافتتاحية: غير متاح'), findsOneWidget);
  });
}
