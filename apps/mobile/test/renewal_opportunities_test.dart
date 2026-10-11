import 'package:academy_mobile/features/owner/renewal_opportunities.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets(
      'Renewal potential is distinct from debt and missing is not zero at $width',
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
                child: RenewalOpportunityContent(
                  data: {
                    'potential': '12700.00',
                    'currency': 'EGP',
                    'count': 20,
                    'unpricedCount': 1,
                    'items': [
                      {
                        'player': 'لاعب تجريبي',
                        'branch': 'فرع تجريبي',
                        'sport': 'السباحة',
                        'group': 'مجموعة أ',
                        'quote': null,
                      },
                    ],
                  },
                ),
              ),
            ),
          ),
        );
        expect(find.text('12700.00 ج.م'), findsOneWidget);
        expect(find.text('تحتاج اختيار باقة'), findsOneWidget);
        expect(
          find.text(
            'ليست دينًا أو تحصيلًا. السعر الحالي يخضع لفحص الأهلية عند التجديد.',
          ),
          findsOneWidget,
        );
        expect(tester.takeException(), isNull);
      },
    );
  }
}
