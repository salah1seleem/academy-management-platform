import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/coach/coach_evaluation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as fixtures;

const enrollment = {
  'id': 'en',
  'sportId': 'swim',
  'sport': 'السباحة',
  'player': 'لاعب اصطناعي',
  'group': 'مجموعة العرض',
  'criteriaReady': false,
};

const storedDraft = {
  'id': 'evaluation',
  'sportEnrollmentId': 'en',
  'status': 'Draft',
  'version': 1,
  'player': 'لاعب اصطناعي',
  'group': 'مجموعة العرض',
  'scores': [
    {'criterionId': 'a', 'name': 'تقنية التنفس', 'score': null},
  ],
};

Future<void> tap(WidgetTester tester, String label) async {
  final target = find.text(label);
  await tester.ensureVisible(target);
  await tester.tap(target);
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('a replacement coach reads readiness from the assigned session', (
    t,
  ) async {
    var reads = 0;
    final draftBodies = <Object?>[];
    final auth = await fixtures.authenticate('Coach', (
      method,
      path,
      body,
      _,
    ) async {
      if (method == 'GET' &&
          path == '/api/v1/attendance/sessions/replacement/players') {
        return {
          'canEvaluate': true,
          'session': {
            'id': 'replacement',
            'sportId': 'swim',
            'criteriaState': ++reads == 1 ? 'Inactive' : 'Ready',
          },
          'players': [
            {'sportEnrollmentId': 'en'},
          ],
        };
      }
      if (method == 'POST' && path == '/api/v1/evaluations') {
        draftBodies.add(body);
        return {'id': 'evaluation'};
      }
      if (method == 'GET' && path == '/api/v1/evaluations/evaluation') {
        return storedDraft;
      }
      throw StateError('Unexpected $method $path');
    });
    await t.pumpWidget(
      fixtures.app(
        CoachEvaluation(
          auth: auth,
          enrollment: const {
            ...enrollment,
            'sessionId': 'replacement',
            'evaluationDate': '2026-11-04',
          },
        ),
      ),
    );
    await t.pumpAndSettle();
    expect(find.text('معايير الرياضة متوقفة'), findsOneWidget);
    expect(find.text('عرض المعايير المقترحة'), findsNothing);
    await tap(t, 'إعادة قراءة المعايير');
    expect(find.text('المعايير جاهزة'), findsOneWidget);
    expect(draftBodies, isEmpty);
    await tap(t, 'بدء التقييم');
    expect(draftBodies, hasLength(1));
    expect(
      draftBodies.single,
      containsPair('trainingSessionId', 'replacement'),
    );
    expect(draftBodies.single, containsPair('evaluationDate', '2026-11-04'));
    expect(find.byType(Slider), findsOneWidget);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  testWidgets(
    'an uncertain criteria save is verified by reading before starting a draft',
    (t) async {
      final writes = <String>[];
      final auth = await fixtures.authenticate('AcademyOwner', (
        method,
        path,
        body,
        _,
      ) async {
        if (path == '/api/v1/academy/calendar') return {'today': '2026-11-05'};
        if (method == 'GET' &&
            path == '/api/v1/evaluations/criteria?sportId=swim') {
          return writes.isEmpty
              ? []
              : [
                  {'sportId': 'swim', 'isActive': true},
                ];
        }
        if (method == 'GET' && path.endsWith('/defaults?sportId=swim')) {
          return {
            'sportId': 'swim',
            'sport': 'السباحة',
            'existing': false,
            'criteria': [
              {'arabicName': 'تقنية التنفس'},
            ],
          };
        }
        if (method == 'POST') {
          writes.add(path);
          if (path.endsWith('/defaults')) {
            throw const ApiFailure(0, 'تعذر الاتصال');
          }
          return {'id': 'evaluation'};
        }
        if (path.endsWith('/evaluation')) return storedDraft;
        throw StateError('Unexpected $method $path');
      });
      await t.pumpWidget(
        fixtures.app(CoachEvaluation(auth: auth, enrollment: enrollment)),
      );
      await t.pumpAndSettle();
      await tap(t, 'عرض المعايير المقترحة');
      await tap(t, 'إضافة المعايير وبدء التقييم');
      expect(find.text('عرض المعايير المقترحة'), findsNothing);
      await tap(t, 'التحقق من الحفظ');
      expect(writes, ['/api/v1/evaluations/criteria/defaults']);
      expect(find.text('المعايير جاهزة'), findsOneWidget);
      await tap(t, 'بدء التقييم');
      expect(writes, [
        '/api/v1/evaluations/criteria/defaults',
        '/api/v1/evaluations',
      ]);
      expect(find.byType(Slider), findsOneWidget);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets('an administrative grants failure leaves a read-only retry', (
    t,
  ) async {
    var permissionReads = 0;
    final auth = await fixtures.authenticate('AcademyAdmin', (
      method,
      path,
      _,
      _,
    ) async {
      expect(method, 'GET');
      if (path.endsWith('/my-permissions')) {
        if (++permissionReads == 1) throw const ApiFailure(503, 'تعذر الاتصال');
        return ['evaluation.criteria.manage'];
      }
      if (path.endsWith('/my-access')) return <String, dynamic>{};
      return [];
    });
    await t.pumpWidget(
      fixtures.app(CoachEvaluation(auth: auth, enrollment: enrollment)),
    );
    await t.pumpAndSettle();
    expect(find.text('تعذر التحقق من صلاحية تجهيز المعايير.'), findsOneWidget);
    expect(find.text('عرض المعايير المقترحة'), findsNothing);
    await tap(t, 'إعادة قراءة المعايير');
    expect(find.text('عرض المعايير المقترحة'), findsOneWidget);
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  testWidgets('a stale session cannot start a draft for a removed player', (
    t,
  ) async {
    final auth = await fixtures.authenticate('Coach', (
      method,
      path,
      _,
      _,
    ) async {
      expect(method, 'GET');
      expect(path, '/api/v1/attendance/sessions/replacement/players');
      return {
        'canEvaluate': true,
        'session': {
          'id': 'replacement',
          'sportId': 'swim',
          'criteriaState': 'Ready',
        },
        'players': [
          {'sportEnrollmentId': 'other-enrollment'},
        ],
      };
    });
    await t.pumpWidget(
      fixtures.app(
        CoachEvaluation(
          auth: auth,
          enrollment: const {...enrollment, 'sessionId': 'replacement'},
        ),
      ),
    );
    await t.pumpAndSettle();
    expect(find.text('التقييم غير متاح لهذا اللاعب في الحصة.'), findsOneWidget);
    expect(find.text('لاعب اصطناعي'), findsNothing);
    expect(find.text('بدء التقييم'), findsNothing);
    expect(find.text('إعادة قراءة المعايير'), findsNothing);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
}
