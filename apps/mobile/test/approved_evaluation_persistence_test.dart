import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/coach/coach_evaluation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' as fixtures;

void main() {
  for (final fault in [
    'none',
    'createRead',
    'saveRead',
    'publishResponse',
    'wrongScore',
  ]) {
    testWidgets(
      'evaluation $fault verifies the exact stored assessment without duplicate writes',
      (t) async {
        var creates = 0, updates = 0, publications = 0, reads = 0;
        var published = false, fail = fault != 'none';
        Map<String, dynamic>? saved;
        final auth = await fixtures.authenticate('Coach', (
          m,
          p,
          b,
          token,
        ) async {
          if (m == 'POST' && p == '/api/v1/evaluations') {
            creates++;
            return {'id': 'evaluation'};
          }
          if (m == 'PUT') {
            updates++;
            saved = Map<String, dynamic>.from(b as Map);
            return {'id': 'evaluation', 'version': 2};
          }
          if (m == 'POST' && p.endsWith('/publish')) {
            publications++;
            published = true;
            if (fault == 'publishResponse' && fail) {
              fail = false;
              throw const ApiFailure(0, 'تعذر تأكيد النشر');
            }
            return {'id': 'evaluation', 'status': 'Published'};
          }
          if (m == 'GET' && p.endsWith('/evaluation')) {
            reads++;
            if (fail &&
                (fault == 'createRead' && creates == 1 && saved == null ||
                    fault == 'saveRead' && saved != null)) {
              fail = false;
              throw const ApiFailure(0, 'تعذر تأكيد القراءة');
            }
            var score = saved == null
                ? null
                : (saved!['scores'] as List)[0]['score'];
            if (fault == 'wrongScore' && fail && saved != null) {
              score = 99;
              fail = false;
            }
            return {
              'id': 'evaluation',
              'sportEnrollmentId': 'enrollment',
              'player': 'طفل اصطناعي',
              'group': 'مجموعة اختبار',
              'status': published ? 'Published' : 'Draft',
              'reportingPeriod': 'تقييم دوري',
              'generalNotes': saved?['generalNotes'],
              'version': saved == null ? 1 : 2,
              'scores': [
                {'criterionId': 'a', 'name': 'التوازن', 'score': score},
                {'criterionId': 'b', 'name': 'التركيز', 'score': null},
              ],
            };
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
                'group': 'مجموعة اختبار',
              },
            ),
          ),
        );
        Future<void> tap(String label) async {
          final f = find.text(label);
          await t.ensureVisible(f);
          await t.tap(f);
          await t.pumpAndSettle();
        }

        await t.pumpAndSettle();
        if (fault == 'createRead') {
          expect(find.byType(Slider), findsNothing);
          await tap('إعادة التحقق من المسودة نفسها');
        }
        await t.ensureVisible(
          find.byKey(const ValueKey('evaluation-assess-a')),
        );
        await t.tap(find.byKey(const ValueKey('evaluation-assess-a')));
        await t.pumpAndSettle();
        await tap('نشر التقييم لولي الأمر');
        if (['saveRead', 'publishResponse', 'wrongScore'].contains(fault)) {
          expect(find.text('تم نشر التقييم لولي الأمر.'), findsNothing);
          expect(
            t
                .widgetList<Slider>(find.byType(Slider))
                .every((s) => s.onChanged == null),
            isTrue,
          );
          await tap('إعادة التحقق من التقييم نفسه');
        }
        expect(find.text('تم نشر التقييم لولي الأمر.'), findsOneWidget);
        expect(find.text('نشر التقييم لولي الأمر'), findsNothing);
        expect(creates, 1);
        expect(updates, 1);
        expect(publications, 1);
        expect(reads, greaterThanOrEqualTo(3));
        expect((saved!['scores'] as List)[0]['score'], 50);
        expect((saved!['scores'] as List)[1]['score'], isNull);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }

  testWidgets('unknown evaluation creation prevents another POST', (t) async {
    var creates = 0;
    final auth = await fixtures.authenticate('Coach', (m, p, b, token) async {
      if (m == 'POST' && p == '/api/v1/evaluations') {
        creates++;
        throw const ApiFailure(0, 'تعذر تأكيد الإنشاء');
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
            'group': 'مجموعة اختبار',
          },
        ),
      ),
    );

    await t.pumpAndSettle();
    final button = find.widgetWithText(FilledButton, 'بدء تقييم جديد');
    expect(t.widget<FilledButton>(button).onPressed, isNull);
    expect(creates, 1);
    expect(
      find.text('راجع قائمة التقييمات للتأكد من المسودة قبل إنشاء أخرى.'),
      findsOneWidget,
    );
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });

  testWidgets(
    'existing draft list retains only this sport enrollment and opens the same ID without POST',
    (t) async {
      var writes = 0;
      final reads = <String>[];
      final auth = await fixtures.authenticate('Coach', (m, p, b, token) async {
        if (p.contains('/auth/')) return {};
        if (m != 'GET') {
          writes++;
          throw StateError('Read only journey');
        }
        reads.add(p);
        if (p.startsWith('/api/v1/evaluations?')) {
          return [
            {
              'id': 'mine',
              'sportEnrollmentId': 'enrollment',
              'status': 'Draft',
              'sport': 'كرة القدم',
              'group': 'مجموعة اختبار',
              'evaluationDate': '2026-10-21',
              'scoredCriteria': 1,
            },
            {
              'id': 'other',
              'sportEnrollmentId': 'another-sport',
              'status': 'Draft',
              'sport': 'سباحة',
              'group': 'مجموعة أخرى',
              'evaluationDate': '2026-10-21',
              'scoredCriteria': 0,
            },
          ];
        }
        if (p == '/api/v1/evaluations/mine') {
          return {
            'id': 'mine',
            'sportEnrollmentId': 'enrollment',
            'player': 'طفل اصطناعي',
            'group': 'مجموعة اختبار',
            'status': 'Draft',
            'version': 2,
            'reportingPeriod': 'تقييم دوري',
            'generalNotes': 'ملاحظة محفوظة',
            'scores': [
              {'criterionId': 'a', 'name': 'التوازن', 'score': 0},
            ],
          };
        }
        throw StateError('Unexpected $m $p');
      });
      await t.pumpWidget(
        fixtures.app(
          NativeEvaluationDrafts(
            auth: auth,
            enrollment: const {'id': 'enrollment', 'playerId': 'child'},
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(
        find.byKey(const ValueKey('evaluation-draft-other')),
        findsNothing,
      );
      await t.tap(find.byKey(const ValueKey('evaluation-draft-mine')));
      await t.pumpAndSettle();
      expect(t.widget<Slider>(find.byType(Slider)).value, 0);
      expect(
        t.widget<TextField>(find.byType(TextField)).controller!.text,
        'ملاحظة محفوظة',
      );
      expect(reads, contains('/api/v1/evaluations/mine'));
      expect(writes, 0);
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('طفل اصطناعي'), findsNothing);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    '375px evaluation remains readable at 200% and published report cannot be submitted again',
    (t) async {
      t.view.physicalSize = const Size(375, 667);
      t.view.devicePixelRatio = 1;
      addTearDown(t.view.resetPhysicalSize);
      addTearDown(t.view.resetDevicePixelRatio);
      var writes = 0;
      final auth = await fixtures.authenticate('Coach', (m, p, b, token) async {
        if (m != 'GET') {
          writes++;
          throw StateError('Published history is read only');
        }
        return {
          'id': 'published',
          'sportEnrollmentId': 'enrollment',
          'player': 'طفل اصطناعي باسم طويل للاختبار',
          'group': 'مجموعة اختبار',
          'status': 'Published',
          'reportingPeriod': 'دوري',
          'version': 3,
          'scores': [
            {
              'criterionId': 'a',
              'name': 'التحكم بالكرة والتوازن الحركي',
              'score': 0,
            },
            {
              'criterionId': 'b',
              'name': 'التركيز والانضباط خلال التدريب',
              'score': null,
            },
          ],
        };
      });
      await t.pumpWidget(
        fixtures.app(
          CoachEvaluation(
            auth: auth,
            evaluationId: 'published',
            enrollment: const {'id': 'enrollment'},
          ),
          scale: 2,
        ),
      );
      await t.pumpAndSettle();
      expect(find.textContaining('تقييم منشور'), findsOneWidget);
      await t.scrollUntilVisible(
        find.byType(TextField),
        150,
        scrollable: find.byType(Scrollable).first,
      );
      expect(
        t
            .widgetList<Slider>(find.byType(Slider))
            .every((s) => s.onChanged == null),
        isTrue,
      );
      expect(find.text('نشر التقييم لولي الأمر'), findsNothing);
      expect(t.widget<TextField>(find.byType(TextField)).readOnly, isTrue);
      expect(writes, 0);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
}
