import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/guardian/guardian_report.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart';
import 'premium_home_test.dart' show shell;
import 'guardian_home_workspace_test.dart' show response, app;
import 'guardian_test.dart' show logged;

void main() {
  testWidgets(
    '375-point carousel keeps the portrait on the right of the football radar',
    (t) async {
      await t.binding.setSurfaceSize(const Size(375, 812));
      addTearDown(() => t.binding.setSurfaceSize(null));
      final auth = AuthController(
        FakeApi((m, p, b, t) async => {}),
        MemoryStore(),
      );
      await t.pumpWidget(
        shell(
          Align(
            alignment: Alignment.topRight,
            child: SizedBox(
              width: (375 - 36) * .88 - 12,
              child: PremiumChildCard(
                auth: auth,
                today: '2026-10-26',
                onTap: () {},
                childData: {
                  'id': 'compact',
                  'arabicName': 'لاعب خيالي',
                  'sports': [
                    {
                      'id': 'football',
                      'sport': 'كرة القدم',
                      'performance': {
                        'isFootballReport': true,
                        'meanPerformance': 74,
                        'scoredCriteria': 6,
                        'applicableCriteria': 6,
                        'axes': [
                          for (final axis in [
                            'Passing',
                            'Dribbling',
                            'Speed',
                            'Defense',
                            'Physical',
                            'Shooting',
                          ])
                            {'axis': axis, 'value': 74},
                        ],
                      },
                    },
                  ],
                },
              ),
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      final portrait = t.getRect(find.byType(PlayerAvatar));
      final radar = t.getRect(find.byType(RadarChart));
      expect(portrait.center.dx, greaterThan(radar.center.dx));
      expect((portrait.top - radar.top).abs(), lessThan(40));
      expect(portrait.bottom, lessThan(radar.bottom + 50));
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'card gauge is the published criterion mean rather than composite power, with explicit incomplete coverage',
    (t) async {
      final auth = AuthController(
        FakeApi((m, p, b, t) async => {}),
        MemoryStore(),
      );
      await t.pumpWidget(
        shell(
          PremiumChildCard(
            auth: auth,
            today: '2026-10-25',
            onTap: () {},
            childData: {
              'id': 'p',
              'arabicName': 'لاعب خيالي',
              'dateOfBirth': '2016-01-01',
              'power': {'overallScore': 90},
              'sports': [
                {
                  'id': 'e',
                  'sport': 'السباحة',
                  'performance': {
                    'meanPerformance': 50,
                    'overallScore': 90,
                    'scoredCriteria': 2,
                    'applicableCriteria': 3,
                    'criteria': [
                      {'name': 'معيار أ', 'value': 100},
                      {'name': 'معيار ب', 'value': 0},
                    ],
                  },
                },
              ],
            },
          ),
        ),
      );
      expect(find.text('50'), findsOneWidget);
      expect(find.text('متوسط الأداء'), findsOneWidget);
      expect(find.text('التقييم غير مكتمل'), findsNothing);
      expect(find.textContaining('2 من 3 معايير'), findsOneWidget);
      expect(
        t
            .widget<LinearProgressIndicator>(
              find.byKey(const ValueKey('performance-mean-gauge-p')),
            )
            .value,
        .5,
      );
      expect(t.takeException(), isNull);
    },
  );
  testWidgets(
    'zero is valid while an unscored assessment has no invented mean',
    (t) async {
      final auth = AuthController(
        FakeApi((m, p, b, t) async => {}),
        MemoryStore(),
      );
      Future<void> show(Object? value) async => t.pumpWidget(
        shell(
          PremiumChildCard(
            key: ValueKey(value),
            auth: auth,
            today: '2026-10-25',
            onTap: () {},
            childData: {
              'id': 'p',
              'arabicName': 'لاعب خيالي',
              'sports': [
                {
                  'id': 'e',
                  'sport': 'السباحة',
                  'performance': {
                    'meanPerformance': value,
                    'scoredCriteria': value == null ? 0 : 1,
                    'applicableCriteria': 2,
                    'criteria': <Object>[],
                  },
                },
              ],
            },
          ),
        ),
      );
      await show(0);
      expect(find.text('0'), findsOneWidget);
      expect(
        t
            .widget<LinearProgressIndicator>(
              find.byKey(const ValueKey('performance-mean-gauge-p')),
            )
            .value,
        0,
      );
      await show(null);
      expect(
        find.byKey(const ValueKey('performance-mean-gauge-p')),
        findsNothing,
      );
      expect(
        find.text('بيانات الأداء غير كافية لحساب المتوسط'),
        findsOneWidget,
      );
      expect(find.text('—'), findsOneWidget);
    },
  );
  testWidgets(
    'child tabs and manually swiped cards share the selected sibling',
    (t) async {
      await t.binding.setSurfaceSize(const Size(390, 900));
      addTearDown(() => t.binding.setSurfaceSize(null));
      final auth = await logged((m, p, b, token) async => response(p));
      await t.pumpWidget(app(auth));
      await t.pumpAndSettle();
      await t.tap(find.byKey(const ValueKey('child-tab-b')));
      await t.pumpAndSettle();
      expect(
        t
            .widget<ChoiceChip>(find.byKey(const ValueKey('child-tab-b')))
            .selected,
        isTrue,
      );
      final pager = t.widget<PageView>(find.byType(PageView).first);
      expect(pager.controller!.page!.round(), 1);
      await t.drag(find.byType(PageView).first, const Offset(-350, 0));
      await t.pumpAndSettle();
      expect(pager.controller!.page, closeTo(0, .01));
      expect(
        t
            .widget<ChoiceChip>(find.byKey(const ValueKey('child-tab-a')))
            .selected,
        isTrue,
      );
      expect(t.takeException(), isNull);
    },
  );
}
