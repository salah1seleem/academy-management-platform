import 'package:academy_mobile/features/shared/receivable_balances.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

void main() {
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets('Posted decimal balances remain readable at $width', (
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
          home: Scaffold(
            body: Directionality(
              textDirection: TextDirection.rtl,
              child: ReceivableBalanceContent(
                data: {
                  'currency': 'EGP',
                  'outstanding': '1740.00',
                  'overdue': '1240.00',
                  'notYetDue': '500.00',
                  'overpaymentCredit': '0.00',
                  'items': [
                    {
                      'player': 'لاعب اختبار اصطناعي',
                      'description': 'مطالبة موثقة',
                      'charge': '540.00',
                      'outstanding': '340.00',
                      'currency': 'EGP',
                      'dueDate': '2026-09-20',
                      'overdue': true,
                    },
                  ],
                },
              ),
            ),
          ),
        ),
      );
      expect(find.text('1740.00 ج.م'), findsOneWidget);
      expect(find.text('1240.00 ج.م'), findsOneWidget);
      expect(
        find.text(
          'انتهاء الاشتراك ليس دينًا. هذه أرصدة مستندات مثبتة وليست طلبات دفع معلقة.',
        ),
        findsOneWidget,
      );
      expect(tester.takeException(), isNull);
    });
  }
}
