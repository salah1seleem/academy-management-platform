import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as f;

Map<String, dynamic> sport(String id, {bool swim = false}) => {
  'id': id,
  'sport': swim ? 'السباحة' : 'كرة القدم',
  'branch': 'فرع العرض',
  'group': 'مجموعة العرض',
  'period': {
    'plan': 'باقة 12 حصة',
    'status': 'Active',
    'remainingSessions': 8,
    'startDate': '2026-10-01',
    'endDate': '2026-10-31',
  },
  'coaches': [
    {'id': 'coach', 'name': 'مدرب العرض', 'groupId': 'group'},
  ],
  'performance': {
    'meanPerformance': 80,
    'scoredCriteria': swim ? 5 : 6,
    'applicableCriteria': swim ? 5 : 6,
    'isFootballReport': !swim,
    'axes': [
      if (!swim)
        for (final name in [
          'التمرير',
          'المراوغة',
          'السرعة',
          'الدفاع',
          'البدني',
          'التسديد',
        ])
          {'axis': name, 'value': 80},
    ],
    'criteria': [
      if (swim)
        for (final name in ['التنفس', 'الحرة', 'الظهر', 'الانطلاق', 'التحمل'])
          {'name': name, 'value': 80},
    ],
  },
};

void main() {
  for (final size in [(360.0, 1.0), (440.0, 1.0), (375.0, 2.0)]) {
    testWidgets(
      'Mixed-sport cards align their footers without a blank footer gap: $size',
      (t) async {
        t.view.physicalSize = Size(size.$1, 956);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.reset);
        final auth = await f.authenticate('Guardian', (m, p, b, k) async => []);
        await t.pumpWidget(
          MaterialApp(
            locale: const Locale('ar'),
            supportedLocales: const [Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            theme: AcademyTheme.light(),
            builder: (c, child) => MediaQuery(
              data: MediaQuery.of(c)
                  .copyWith(textScaler: TextScaler.linear(size.$2)),
              child: child!,
            ),
            home: Scaffold(
              body: SingleChildScrollView(
                child: Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 20),
                  child: GuardianChildCarousel(
                    auth: auth,
                    children: [
                      {
                        'id': 'a',
                        'arabicName': 'يوسف الخيالي',
                        'dateOfBirth': '2013-03-01',
                        'sports': [sport('football')],
                      },
                      {
                        'id': 'b',
                        'arabicName': 'ليلى الخيالية',
                        'dateOfBirth': '2017-03-01',
                        'sports': [
                          sport('swimming', swim: true),
                          sport('other', swim: true),
                        ],
                      },
                    ],
                    selectedPlayerId: 'a',
                    today: '2026-10-29',
                    onSelected: (_) {},
                    onOpen: (_) {},
                  ),
                ),
              ),
            ),
          ),
        );
        await t.pumpAndSettle();
        final firstHeight = t
            .getSize(find.byKey(const ValueKey('child-a')))
            .height;
        final firstFooter = t
            .getBottomLeft(
              find.widgetWithText(FilledButton, 'ملف يوسف والتقرير'),
            )
            .dy;
        await t.drag(find.byType(PageView), Offset(size.$1 * .8, 0));
        await t.pumpAndSettle();
        final secondHeight = t
            .getSize(find.byKey(const ValueKey('child-b')))
            .height;
        final secondFooter = t
            .getBottomLeft(
              find.widgetWithText(FilledButton, 'ملف ليلى والتقرير'),
            )
            .dy;
        expect(secondHeight, closeTo(firstHeight, 1));
        expect(secondFooter, closeTo(firstFooter, 1));
        if (size.$2 == 1) {
          final footerTop = t
              .getTopLeft(
                find.widgetWithText(FilledButton, 'ملف ليلى والتقرير'),
              )
              .dy;
          final shortcuts = find.ancestor(
            of: find.text('مكتبة التغذية').last,
            matching: find.byType(IntrinsicHeight),
          );
          final shortcutBottom = t.getBottomLeft(shortcuts).dy;
          final coachBottom = t
              .getBottomLeft(
                find.widgetWithText(TextButton, 'المدرب  مدرب العرض').last,
              )
              .dy;
          expect(shortcutBottom, greaterThan(coachBottom));
          expect(footerTop - shortcutBottom, inInclusiveRange(0, 32));
        }
        for (final name in ['التنفس', 'الحرة', 'الظهر', 'الانطلاق', 'التحمل']) {
          expect(find.text('$name · 80'), findsOneWidget);
        }
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
}
