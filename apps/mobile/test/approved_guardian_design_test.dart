import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/guardian/guardian_report.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'guardian_test.dart' show logged;
import 'premium_home_test.dart' show shell;

void main() {
  testWidgets(
    'Large RTL sibling carousel leaves a next-card cue and changes active child by swipe',
    (t) async {
      await t.binding.setSurfaceSize(const Size(390, 844));
      addTearDown(() => t.binding.setSurfaceSize(null));
      final auth = await logged((m, p, b, token) async => []);
      String? active;
      await t.pumpWidget(
        shell(
          GuardianChildCarousel(
            auth: auth,
            today: '2026-10-21',
            children: [
              {
                'id': 'a',
                'arabicName': 'ابن اصطناعي أول',
                'dateOfBirth': '2017-01-01',
                'sports': <Object>[],
              },
              {
                'id': 'b',
                'arabicName': 'ابن اصطناعي ثان',
                'dateOfBirth': '2018-01-01',
                'sports': <Object>[],
              },
            ],
            onSelected: (id) => active = id,
            onOpen: (_) {},
          ),
        ),
      );
      await t.pumpAndSettle();
      final first = t.getRect(find.byKey(const ValueKey('child-a')));
      final second = t.getRect(find.byKey(const ValueKey('child-b')));
      expect(first.width, greaterThan(300));
      expect(second.right, greaterThan(0));
      expect(second.right, lessThan(first.width));
      await t.drag(find.byType(PageView), const Offset(320, 0));
      await t.pumpAndSettle();
      expect(active, 'b');
      final cards = t.widget<GuardianChildCarousel>(
        find.byType(GuardianChildCarousel),
      );
      // The long home list disposes offscreen cards. Recreating that section
      // must display the child still selected in the surrounding workspace.
      await t.pumpWidget(const SizedBox());
      await t.pumpWidget(
        shell(
          GuardianChildCarousel(
            auth: auth,
            today: cards.today,
            children: cards.children,
            selectedPlayerId: active,
            onSelected: (id) => active = id,
            onOpen: (_) {},
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(
        t.widget<PageView>(find.byType(PageView)).controller!.page!.round(),
        1,
      );
      expect(
        t.getRect(find.byKey(const ValueKey('child-b'))).center.dx,
        inInclusiveRange(0, 390),
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Changing enrollment removes football axes and shows assessed swimming zero without filling missing criteria',
    (t) async {
      final auth = await logged((m, p, b, token) async => []);
      await t.pumpWidget(
        shell(
          PremiumChildCard(
            auth: auth,
            today: '2026-10-21',
            onTap: () {},
            childData: {
              'id': 'a',
              'arabicName': 'طفل متعدد الرياضات اصطناعي',
              'sports': [
                {
                  'id': 'football',
                  'sport': 'كرة القدم',
                  'branch': 'فرع عرض',
                  'performance': {
                    'isFootballReport': true,
                    'overallScore': 80,
                    'axes': [
                      {'axis': 'Passing', 'value': 80},
                      {'axis': 'Shooting', 'value': null},
                    ],
                  },
                },
                {
                  'id': 'swim',
                  'sport': 'السباحة',
                  'branch': 'فرع عرض',
                  'performance': {
                    'isFootballReport': false,
                    'criteria': [
                      {'name': 'الانسياب', 'value': 0},
                      {'name': 'غير مقيم', 'value': null},
                    ],
                  },
                },
              ],
            },
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.byType(RadarChart), findsOneWidget);
      expect(find.text('التسديد'), findsNothing);
      await t.tap(find.byType(DropdownButtonFormField<String>));
      await t.pumpAndSettle();
      await t.tap(find.text('السباحة · فرع عرض').last);
      await t.pumpAndSettle();
      expect(find.byType(RadarChart), findsNothing);
      expect(find.text('الانسياب · 0'), findsOneWidget);
      expect(find.textContaining('غير مقيم'), findsNothing);
      expect(find.text('80'), findsNothing);
      expect(find.text('50'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Home media uses selected-child protected gallery and a denial does not render video',
    (t) async {
      final paths = <String>[];
      final auth = await logged((m, p, b, token) async {
        paths.add(p);
        if (p.contains('denied')) {
          throw const ApiFailure(404, 'الوسائط غير متاحة.');
        }
        return [
          {
            'id': 'synthetic',
            'contentType': 'video/mp4',
            'caption': 'تمرين اصطناعي مصرح',
            'url': '/api/v1/assets/00000000-0000-0000-0000-000000000001',
          },
        ];
      });
      await t.pumpWidget(
        shell(GuardianHomeMedia(auth: auth, playerId: 'linked')),
      );
      await t.pumpAndSettle();
      expect(paths, ['/api/v1/assets/gallery?playerId=linked']);
      expect(find.text('تمرين اصطناعي مصرح'), findsOneWidget);
      await t.pumpWidget(
        shell(
          GuardianHomeMedia(
            key: const ValueKey('denied'),
            auth: auth,
            playerId: 'denied',
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('تمرين اصطناعي مصرح'), findsNothing);
      expect(find.byIcon(Icons.play_circle_outline), findsNothing);
      expect(find.text('الوسائط غير متاحة.'), findsOneWidget);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
