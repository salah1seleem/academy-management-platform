import 'package:academy_mobile/features/coach/coach_evaluation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as fixtures;

void main() {
  for (final width in [375.0, 440.0]) {
    for (final note in <String?>[null, '', 'ملاحظة مدرب اصطناعية']) {
      testWidgets(
        'resumed draft preserves optional notes and true zero/null criteria at width=$width note=$note',
        (t) async {
          t.view.physicalSize = Size(width, 900);
          t.view.devicePixelRatio = 1;
          addTearDown(t.view.resetPhysicalSize);
          addTearDown(t.view.resetDevicePixelRatio);
          Map<String, dynamic>? saved;
          var puts = 0, publishes = 0;
          final auth = await fixtures.authenticate('Coach', (
            method,
            path,
            body,
            token,
          ) async {
            if (method == 'PUT' && path == '/api/v1/evaluations/existing') {
              puts++;
              saved = Map<String, dynamic>.from(body as Map);
              return {'version': 2};
            }
            if (method == 'GET' && path == '/api/v1/evaluations/existing') {
              final scoreRows = saved?['scores'] as List?;
              return {
                'id': 'existing',
                'sportEnrollmentId': 'enrollment',
                'status': publishes == 0 ? 'Draft' : 'PublishedInternal',
                'reportingPeriod': 'تقييم دوري',
                'player': 'لاعب اصطناعي',
                'group': 'مجموعة اختبار',
                'version': saved == null ? 1 : 2,
                'generalNotes': saved == null
                    ? note
                    : (saved!['generalNotes'] == ''
                          ? null
                          : saved!['generalNotes']),
                'scores': [
                  {
                    'criterionId': 'zero',
                    'name': 'التوازن',
                    'notes': 'ملاحظة المعيار المحفوظة',
                    'score': scoreRows == null
                        ? 0
                        : (scoreRows[0] as Map)['score'],
                  },
                  {
                    'criterionId': 'untouched',
                    'name': 'التركيز',
                    'score': scoreRows == null
                        ? null
                        : (scoreRows[1] as Map)['score'],
                  },
                ],
              };
            }
            if (method == 'POST' && path.endsWith('/publish')) {
              publishes++;
              expect((body as Map)['shareWithGuardian'], false);
              return {};
            }
            throw StateError('Unexpected $method $path');
          });
          await t.pumpWidget(
            fixtures.app(
              CoachEvaluation(
                auth: auth,
                evaluationId: 'existing',
                enrollment: const {'id': 'enrollment'},
              ),
              scale: 2,
            ),
          );
          await t.pumpAndSettle();
          final notes = find.widgetWithText(TextField, 'ملاحظات المدرب');
          await t.scrollUntilVisible(
            notes,
            180,
            scrollable: find.byType(Scrollable).first,
          );
          expect(t.widget<TextField>(notes).controller!.text, note ?? '');
          final editedNote = note?.isNotEmpty == true ? '' : 'ملاحظة محدثة';
          await t.enterText(notes, editedNote);
          await t.pump();
          await t.tap(find.text('مراجعة النشر'));
          await t.pumpAndSettle();
          await t.tap(find.text('اعتماد داخلي'));
          await t.pumpAndSettle();
          expect(puts, 1);
          expect(publishes, 1);
          expect(saved!['generalNotes'], editedNote);
          expect(saved!['scores'], [
            {
              'criterionId': 'zero',
              'score': 0,
              'notes': 'ملاحظة المعيار المحفوظة',
            },
            {'criterionId': 'untouched', 'score': null, 'notes': null},
          ]);
          expect(find.text('حُفظ التقييم للاستخدام الداخلي.'), findsOneWidget);
          expect(find.text('إعادة التحقق من التقييم نفسه'), findsNothing);
          expect(t.takeException(), isNull);
          await t.pumpWidget(const SizedBox());
          auth.dispose();
        },
      );
    }
  }
}
