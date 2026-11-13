import 'package:academy_mobile/features/coach/coach_evaluation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as fixtures;

Map<String, dynamic> draft() => {
  'id': 'draft',
  'sportEnrollmentId': 'en',
  'player': 'لاعب اصطناعي',
  'sport': 'السباحة',
  'evaluationDate': '2026-11-05',
  'status': 'Draft',
  'reportingPeriod': 'نوفمبر 2026',
  'version': 1,
  'scores': [
    {
      'criterionId': 'a',
      'name': 'تقنية التنفس',
      'score': null,
      'notes': 'ملاحظة معيار محفوظة',
    },
    {'criterionId': 'b', 'name': 'الانزلاق', 'score': null},
  ],
};

Future<void> tap(WidgetTester t, String label) async {
  final target = find.text(label);
  await t.ensureVisible(target);
  await t.tap(target);
  await t.pumpAndSettle();
}

void main() {
  testWidgets(
    'saving a draft preserves zero and notes without sharing, then review requires a choice',
    (t) async {
      final writes = <String>[];
      var stored = draft();
      final auth = await fixtures.authenticate('Coach', (
        method,
        path,
        body,
        _,
      ) async {
        if (method == 'GET') {
          expect(path, '/api/v1/evaluations/draft');
          return stored;
        }
        writes.add('$method $path');
        final write = body as Map;
        if (method == 'PUT') {
          final scores = write['scores'] as List;
          expect(scores.first, containsPair('score', 0));
          expect(scores.first, containsPair('notes', 'ملاحظة معيار محفوظة'));
          expect(scores.last, containsPair('score', null));
          expect(write['generalNotes'], 'متابعة وضع الجسم');
          stored = {
            ...stored,
            'version': 2,
            'generalNotes': write['generalNotes'],
            'scores': [
              {...stored['scores'][0] as Map<String, dynamic>, 'score': 0},
              stored['scores'][1],
            ],
          };
          return {'version': 2};
        }
        expect(write, {'version': 2, 'shareWithGuardian': false});
        stored = {...stored, 'status': 'PublishedInternal'};
        return {'status': 'PublishedInternal'};
      });
      await t.pumpWidget(
        fixtures.app(
          CoachEvaluation(
            auth: auth,
            evaluationId: 'draft',
            enrollment: const {'id': 'en'},
          ),
        ),
      );
      await t.pumpAndSettle();
      final save = find.widgetWithText(OutlinedButton, 'حفظ المسودة');
      expect(t.widget<OutlinedButton>(save).onPressed, isNull);
      t.widget<Slider>(find.byType(Slider).first).onChanged!(0);
      await t.pumpAndSettle();
      final notes = find.byType(TextField);
      await t.scrollUntilVisible(
        notes,
        120,
        scrollable: find.byType(Scrollable).first,
      );
      await t.enterText(notes, 'متابعة وضع الجسم');
      await t.pumpAndSettle();
      await tap(t, 'حفظ المسودة');
      expect(writes, ['PUT /api/v1/evaluations/draft']);
      expect(find.text('تم حفظ المسودة.'), findsOneWidget);
      expect(t.widget<OutlinedButton>(save).onPressed, isNull);
      await tap(t, 'مراجعة النشر');
      expect(find.text('نشر لولي الأمر'), findsOneWidget);
      expect(find.text('اعتماد داخلي'), findsOneWidget);
      await tap(t, 'إلغاء');
      expect(writes, hasLength(1));
      await tap(t, 'مراجعة النشر');
      await tap(t, 'اعتماد داخلي');
      expect(writes, [
        'PUT /api/v1/evaluations/draft',
        'POST /api/v1/evaluations/draft/publish',
      ]);
      expect(find.text('حُفظ التقييم للاستخدام الداخلي.'), findsOneWidget);
      expect(find.text('حفظ المسودة'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets('back requires a choice while the draft has unsaved changes', (
    t,
  ) async {
    var reads = 0;
    final auth = await fixtures.authenticate('AcademyOwner', (
      method,
      path,
      _,
      _,
    ) async {
      expect(method, 'GET');
      expect(path, '/api/v1/evaluations/draft');
      reads++;
      return draft();
    });
    await t.pumpWidget(
      fixtures.app(
        Scaffold(
          body: Builder(
            builder: (context) => FilledButton(
              onPressed: () => Navigator.of(context).push(
                MaterialPageRoute(
                  builder: (_) => CoachEvaluation(
                    auth: auth,
                    evaluationId: 'draft',
                    enrollment: const {'id': 'en'},
                  ),
                ),
              ),
              child: const Text('فتح التقييم'),
            ),
          ),
        ),
      ),
    );
    await tap(t, 'فتح التقييم');
    t.widget<Slider>(find.byType(Slider).first).onChanged!(0);
    await t.pumpAndSettle();
    await t.tap(find.byType(BackButton));
    await t.pumpAndSettle();
    expect(find.text('تغييرات غير محفوظة'), findsOneWidget);
    await tap(t, 'البقاء في التقييم');
    expect(t.widget<Slider>(find.byType(Slider).first).value, 0);
    await t.tap(find.byType(BackButton));
    await t.pumpAndSettle();
    await tap(t, 'خروج دون حفظ');
    expect(find.text('فتح التقييم'), findsOneWidget);
    expect(find.byType(CoachEvaluation), findsNothing);
    expect(reads, 1);
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
}
