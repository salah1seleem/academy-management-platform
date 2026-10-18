import 'package:academy_mobile/features/shared/player_skills.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_localizations/flutter_localizations.dart';

import 'owner_test.dart' as fixture;

Map<String, dynamic> plan({bool write = false}) => {
  'enrollment': {'id': 'e', 'player': 'لاعب اصطناعي', 'sport': 'كرة القدم'},
  'plan': {
    'id': 'p',
    'version': 7,
    'assignedOn': '2026-10-18',
    'curriculum': {
      'id': 'c',
      'title': 'منهج الكرة الاصطناعي',
      'levelNameSnapshot': 'مستوى أول',
      'revisionNumber': 1,
    },
  },
  'asOfDate': '2026-10-18',
  'canReview': write,
  'contextChanged': false,
  'progress': {
    'mastered': 0,
    'assessed': 0,
    'total': 2,
    'percentage': 0,
    'sourcePercentage': 0,
    'readyForTechnicalReview': false,
  },
  'skills': [
    {
      'requirement': {
        'id': 'r',
        'name': 'تحكم بالكرة',
        'unit': 'Checklist',
        'protocol': 'ملاحظة نفس التمرين',
      },
      'latest': null,
      'baseline': null,
    },
  ],
  'history': <Object>[],
};
void main() {
  testWidgets(
    'Family reads preserved level snapshots but never an internal staff note',
    (t) async {
      await t.binding.setSurfaceSize(const Size(360, 850));
      addTearDown(() => t.binding.setSurfaceSize(null));
      final auth = await fixture.owner(
        (m, p, b, k) async => {
          'items': [
            {
              'enrollment': {
                'id': 'e',
                'player': 'لاعب اصطناعي',
                'sport': 'كرة القدم',
              },
              'active': null,
              'history': <Object>[],
              'levelHistory': {
                'items': [
                  {
                    'id': 'r',
                    'revision': 1,
                    'fromLevelName': 'أساسيات الكرة',
                    'toLevelName': 'خطوة متقدمة',
                    'actorName': 'مسؤول فني اصطناعي',
                    'createdAtUtc': '2026-10-18T07:00:00Z',
                    'note': 'ملاحظة إدارية خاصة',
                  },
                ],
                'hasMore': false,
              },
            },
          ],
        },
      );
      await t.pumpWidget(
        fixture.shell(
          MediaQuery(
            data: MediaQueryData(
              size: const Size(360, 850),
              textScaler: TextScaler.linear(2),
            ),
            child: PlayerSkillsPage(auth: auth, playerId: 'child'),
          ),
        ),
      );
      await t.pumpAndSettle();
      final details = find.text('سجل مراجعات المستوى');
      await t.scrollUntilVisible(details, 200);
      await t.pumpAndSettle();
      await t.ensureVisible(details);
      await t.tap(details);
      await t.pumpAndSettle();
      expect(find.text('أساسيات الكرة ← خطوة متقدمة'), findsOneWidget);
      expect(find.text('ملاحظة إدارية خاصة'), findsNothing);
      expect(find.text('اعتماد المراجعة'), findsNothing);
      expect(t.takeException(), isNull);
      await t.binding.setSurfaceSize(null);
    },
  );
  testWidgets(
    'English controls preserve Arabic content and show an honest half-progress track',
    (t) async {
      final d = plan();
      (d['progress'] as Map)['mastered'] = 1;
      (d['progress'] as Map)['percentage'] = 50;
      final auth = await fixture.owner((m, p, b, k) async => d);
      await t.pumpWidget(
        MaterialApp(
          locale: const Locale('en'),
          supportedLocales: const [Locale('ar'), Locale('en')],
          localizationsDelegates: GlobalMaterialLocalizations.delegates,
          theme: ThemeData.dark(),
          home: PlayerSkillsPage(auth: auth, playerId: 'child', planId: 'p'),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('Player learning journey'), findsOneWidget);
      expect(find.text('منهج الكرة الاصطناعي'), findsOneWidget);
      final track = t.widget<LinearProgressIndicator>(
        find.byType(LinearProgressIndicator),
      );
      expect(track.value, .5);
      expect(track.backgroundColor, isNot(track.color));
      expect(
        Directionality.of(t.element(find.text('Player learning journey'))),
        TextDirection.ltr,
      );
      expect(t.takeException(), isNull);
    },
  );
  testWidgets(
    'Family has no write controls and null observations are not zero',
    (t) async {
      final auth = await fixture.owner((m, p, b, k) async => plan());
      await t.pumpWidget(
        fixture.shell(
          PlayerSkillsPage(auth: auth, playerId: 'child', planId: 'p'),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('منهج الكرة الاصطناعي'), findsOneWidget);
      expect(find.text('لم يُقيّم'), findsOneWidget);
      expect(find.text('حفظ ونشر الملاحظة للأسرة'), findsNothing);
      expect(t.takeException(), isNull);
    },
  );
  testWidgets(
    'Review sends exact requirement and null checklist measurement with stable request ID',
    (t) async {
      final commands = <Map<String, dynamic>>[];
      final auth = await fixture.owner((m, p, b, k) async {
        if (m == 'POST') {
          expect(p, '/api/v1/evaluations/skills/plans/p/reviews');
          commands.add(Map<String, dynamic>.from(b as Map));
          return {'id': 'review'};
        }
        return plan(write: true);
      });
      await t.pumpWidget(
        fixture.shell(PlayerSkillsPage(auth: auth, planId: 'p')),
      );
      await t.pumpAndSettle();
      await t.ensureVisible(find.byType(DropdownButtonFormField<String>).first);
      await t.tap(find.byType(DropdownButtonFormField<String>).first);
      await t.pumpAndSettle();
      await t.tap(find.text('تحكم بالكرة').last);
      await t.pumpAndSettle();
      final save = find.text('حفظ ونشر الملاحظة للأسرة');
      await t.ensureVisible(save);
      await t.pumpAndSettle();
      await t.tap(save);
      await t.pumpAndSettle();
      expect(commands, hasLength(1));
      expect(commands.single, containsPair('requirementId', 'r'));
      expect(commands.single, containsPair('measuredValue', null));
      expect(commands.single, containsPair('observedOn', '2026-10-18'));
      expect(commands.single['requestId'], matches(RegExp(r'^[a-f0-9-]{36}$')));
      expect(find.text('تم حفظ التحديث الفني.'), findsOneWidget);
      expect(t.takeException(), isNull);
    },
  );
  testWidgets(
    'Private skills disappear in background and revalidate before returning',
    (t) async {
      var allowed = true;
      final auth = await fixture.owner((m, p, b, k) async {
        if (!allowed) throw Exception('revoked');
        return plan();
      });
      await t.pumpWidget(
        fixture.shell(PlayerSkillsPage(auth: auth, planId: 'p')),
      );
      await t.pumpAndSettle();
      expect(find.text('منهج الكرة الاصطناعي'), findsOneWidget);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      await t.pumpAndSettle();
      expect(find.text('منهج الكرة الاصطناعي'), findsNothing);
      allowed = false;
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await t.pumpAndSettle();
      expect(find.text('منهج الكرة الاصطناعي'), findsNothing);
      expect(t.takeException(), isNull);
    },
  );
  testWidgets('Compact progress and evidence fit RTL at 200 percent', (
    t,
  ) async {
    for (final width in [360.0, 390.0, 430.0]) {
      await t.binding.setSurfaceSize(Size(width, 850));
      final auth = await fixture.owner((m, p, b, k) async => plan());
      await t.pumpWidget(
        fixture.shell(
          MediaQuery(
            data: MediaQueryData(
              size: Size(width, 850),
              textScaler: TextScaler.linear(2),
            ),
            child: PlayerSkillsPage(
              key: ValueKey(width),
              auth: auth,
              playerId: 'child',
              planId: 'p',
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('منهج الكرة الاصطناعي'), findsOneWidget);
      expect(t.takeException(), isNull);
    }
    await t.binding.setSurfaceSize(null);
  });
}
