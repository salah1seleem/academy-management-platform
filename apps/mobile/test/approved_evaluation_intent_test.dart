import 'package:academy_mobile/features/coach/coach_evaluation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as fixtures;

void main() {
  for (final explicitlyConfirm in [false, true]) {
    testWidgets(
      explicitlyConfirm
          ? 'explicit confirmation can assess 50 without changing other criteria'
          : 'untouched 50 stays null and a deliberately adjusted zero is genuine',
      (t) async {
        Map<String, dynamic>? saved;
        var publishes = 0;
        final auth = await fixtures.authenticate('Coach', (
          m,
          p,
          b,
          token,
        ) async {
          if (m == 'POST' && p == '/api/v1/evaluations') {
            return {'id': 'evaluation'};
          }
          if (m == 'GET' && p == '/api/v1/evaluations/evaluation') {
            return {
              'id': 'evaluation',
              'sportEnrollmentId': 'enrollment',
              'status': publishes == 0 ? 'Draft' : 'PublishedInternal',
              'generalNotes': saved?['generalNotes'],
              'player': 'طفل اصطناعي',
              'group': 'مجموعة تجريبية',
              'reportingPeriod': 'دوري',
              'version': 1,
              'scores': [
                {
                  'criterionId': 'a',
                  'name': 'التوازن',
                  'score': saved == null
                      ? null
                      : (saved!['scores'] as List)[0]['score'],
                },
                {'criterionId': 'b', 'name': 'التركيز', 'score': null},
              ],
            };
          }
          if (m == 'PUT') {
            saved = Map<String, dynamic>.from(b as Map);
            return {'version': 2};
          }
          if (p.endsWith('/publish')) {
            publishes++;
            return {};
          }
          throw StateError('Unexpected $m $p');
        });
        await t.pumpWidget(
          fixtures.app(
            CoachEvaluation(
              auth: auth,
              enrollment: const {
                'id': 'enrollment',
                'player': 'طفل اصطناعي',
                'group': 'مجموعة تجريبية',
              },
            ),
          ),
        );

        await t.pumpAndSettle();
        expect(t.widgetList<Slider>(find.byType(Slider)).map((s) => s.value), [
          50,
          50,
        ]);
        await t.ensureVisible(find.text('نشر التقييم لولي الأمر'));
        await t.tap(find.text('نشر التقييم لولي الأمر'));
        await t.pumpAndSettle();
        expect(publishes, 0);
        expect(saved, isNull);
        if (explicitlyConfirm) {
          await t.ensureVisible(
            find.byKey(const ValueKey('evaluation-assess-a')),
          );
          await t.tap(find.byKey(const ValueKey('evaluation-assess-a')));
          await t.pumpAndSettle();
        } else {
          final slider = find.byType(Slider).first;
          await t.ensureVisible(slider);
          t.widget<Slider>(slider).onChanged!(0);
          await t.pump();
        }
        await t.ensureVisible(find.text('نشر التقييم بدون ولي الأمر'));
        await t.tap(find.text('نشر التقييم بدون ولي الأمر'));
        await t.pumpAndSettle();
        final scores = (saved!['scores'] as List).cast<Map>();
        expect(scores[0]['score'], explicitlyConfirm ? 50 : 0);
        expect(scores[1]['score'], isNull);
        expect(publishes, 1);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
}
