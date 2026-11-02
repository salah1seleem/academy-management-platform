import 'package:academy_mobile/features/shared/player_technical_reviews.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

import 'owner_test.dart' as fixture;

Map<String, dynamic> technicalRequest() => {
  'id': 'r',
  'planId': 'p',
  'sportEnrollmentId': 'e',
  'sequence': 1,
  'sourceReviewSequence': 5,
  'levelName': 'مستوى أول',
  'curriculumTitle': 'مهارات أصلية',
  'requirements': 2,
  'actorName': 'مدرب اصطناعي',
  'createdAtUtc': '2026-10-18T07:00:00Z',
  'version': 7,
};
Map<String, dynamic> technicalPlan() => {
  'plan': {'id': 'p', 'version': 3},
  'enrollment': {'player': 'لاعب اصطناعي', 'sport': 'كرة القدم'},
  'source': {'eligible': true, 'head': 5, 'requirements': 2, 'mastered': 2},
  'latestRequestId': null,
  'canRequest': true,
  'canDecide': false,
  'items': <Object>[],
};
Map<String, dynamic> technicalDetail() => {
  'request': technicalRequest(),
  'enrollment': {'player': 'لاعب اصطناعي'},
  'decision': null,
  'evidence': [
    {
      'name': 'تحكم',
      'protocol': 'نفس التمرين',
      'unit': 'Checklist',
      'measuredValue': null,
      'observedOn': '2026-10-18',
      'actorName': 'مدرب اصطناعي',
    },
  ],
  'currentEligible': true,
  'contextChanged': false,
  'canDecide': true,
  'levels': [
    {'id': 'next', 'name': 'مستوى تالٍ'},
  ],
};
void main() {
  testWidgets(
    'Request uses exact current plan version and evidence head without changing a player',
    (t) async {
      final writes = <Object?>[];
      final auth = await fixture.owner((m, p, b, k) async {
        if (m == 'POST') {
          expect(p, '/api/v1/evaluations/skills/plans/p/technical-reviews');
          writes.add(b);
          return {'id': 'r'};
        }
        return technicalPlan();
      });
      await t.pumpWidget(
        fixture.shell(PlayerTechnicalReviewsPage(auth: auth, planId: 'p')),
      );
      await t.pumpAndSettle();
      await t.tap(find.text('طلب مراجعة فنية للمستوى'));
      await t.pumpAndSettle();
      expect(writes.single, containsPair('planVersion', 3));
      expect(writes.single, containsPair('sourceReviewSequence', 5));
      expect(writes.single, containsPair('previousRequestId', null));
      expect(
        find.text('تم حفظ المراجعة؛ لم يتغير مستوى اللاعب أو مجموعته.'),
        findsOneWidget,
      );
    },
  );
  testWidgets(
    'Family reads saved recommendation without any request or decision controls',
    (t) async {
      final auth = await fixture.owner((m, p, b, k) async {
        expect(m, 'GET');
        expect(
          p,
          '/api/v1/guardian/children/child/skills/plans/p/technical-reviews',
        );
        return {
          ...technicalPlan(),
          'items': [
            {
              'request': technicalRequest(),
              'decision': {
                'outcome': 'ReadyForPlacement',
                'recommendedLevelName': 'مستوى تالٍ',
                'familyNote': 'ملخص للأسرة',
                'actorName': 'مراجع مخول',
                'createdAtUtc': '2026-10-18',
              },
            },
          ],
        };
      });
      await t.pumpWidget(
        fixture.shell(
          PlayerTechnicalReviewsPage(
            auth: auth,
            planId: 'p',
            playerId: 'child',
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('طلب مراجعة فنية للمستوى'), findsNothing);
      await t.scrollUntilVisible(find.text('ملخص للأسرة'), 200);
      expect(find.text('ملخص للأسرة'), findsOneWidget);
      expect(find.text('الأدلة وقرار المراجعة'), findsNothing);
    },
  );
  testWidgets(
    'Scoped reviewer submits a recommendation and optional summary only to decision endpoint',
    (t) async {
      final commands = <Map<String, dynamic>>[];
      final auth = await fixture.owner((m, p, b, k) async {
        if (m == 'POST') {
          expect(p, '/api/v1/evaluations/technical-reviews/r/decision');
          commands.add(Map<String, dynamic>.from(b as Map));
          return {'id': 'd'};
        }
        return technicalDetail();
      });
      await t.pumpWidget(
        fixture.shell(PlayerTechnicalReviewsPage(auth: auth, requestId: 'r')),
      );
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.byType(DropdownButtonFormField<String>).first,
        200,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(find.byType(DropdownButtonFormField<String>).first);
      await t.pumpAndSettle();
      await t.tap(find.text('مستعد لمراجعة التسكين').last);
      await t.pumpAndSettle();
      await t.tap(find.byType(DropdownButtonFormField<String>).last);
      await t.pumpAndSettle();
      await t.tap(find.text('مستوى تالٍ').last);
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('حفظ القرار ونشر الملخص'),
        200,
        scrollable: find.byType(Scrollable).first,
      );
      await t.tap(find.text('حفظ القرار ونشر الملخص'));
      await t.pumpAndSettle();
      expect(commands.single, containsPair('recommendedLevelId', 'next'));
      expect(commands.single, containsPair('outcome', 'ReadyForPlacement'));
      expect(commands.single, containsPair('familyNote', null));
      expect(commands.single, containsPair('version', 7));
    },
  );
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets(
      'Technical review English200% at $width preserves readable source',
      (t) async {
        await t.binding.setSurfaceSize(Size(width, 850));
        addTearDown(() => t.binding.setSurfaceSize(null));
        final auth = await fixture.owner(
          (m, p, b, k) async => technicalDetail(),
        );
        await t.pumpWidget(
          MaterialApp(
            locale: const Locale('en'),
            supportedLocales: const [Locale('ar'), Locale('en')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            home: MediaQuery(
              data: const MediaQueryData(textScaler: TextScaler.linear(2)),
              child: PlayerTechnicalReviewsPage(auth: auth, requestId: 'r'),
            ),
          ),
        );
        await t.pumpAndSettle();
        await t.scrollUntilVisible(find.text('نفس التمرين'), 200);
        expect(t.takeException(), isNull);
        expect(find.text('نفس التمرين'), findsOneWidget);
      },
    );
  }
  testWidgets('Background and logout hide preserved evidence', (t) async {
    final auth = await fixture.owner((m, p, b, k) async => technicalPlan());
    await t.pumpWidget(
      fixture.shell(PlayerTechnicalReviewsPage(auth: auth, planId: 'p')),
    );
    await t.pumpAndSettle();
    expect(find.text('لاعب اصطناعي · كرة القدم'), findsOneWidget);
    t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
    await t.pumpAndSettle();
    expect(find.text('لاعب اصطناعي · كرة القدم'), findsNothing);
    t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    await t.pumpAndSettle();
    await auth.logout();
    await t.pumpAndSettle();
    expect(find.text('لاعب اصطناعي · كرة القدم'), findsNothing);
  });
}
