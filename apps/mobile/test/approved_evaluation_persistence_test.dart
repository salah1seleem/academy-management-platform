import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/coach/coach_evaluation.dart';
import 'package:academy_mobile/features/coach/coach_player.dart';
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
    'replacedDraft',
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
          if (p == '/api/v1/academy/calendar') return {'today': '2026-09-28'};
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
            if (fault == 'replacedDraft' && saved != null) score = 99;
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
        await tap('مراجعة النشر');
        await tap('نشر لولي الأمر');
        if (fault == 'replacedDraft') {
          await tap('عرض النسخة المحفوظة');
          await tap('عرض النسخة');
          expect(t.widgetList<Slider>(find.byType(Slider)).first.value, 99);
          expect(find.text('تم نشر التقييم لولي الأمر.'), findsNothing);
          expect(updates, 1);
          expect(publications, 0);
          await t.pumpWidget(const SizedBox());
          auth.dispose();
          return;
        }
        if (['saveRead', 'publishResponse', 'wrongScore'].contains(fault)) {
          expect(find.text('تم نشر التقييم لولي الأمر.'), findsNothing);
          expect(
            t
                .widgetList<Slider>(find.byType(Slider))
                .every((s) => s.onChanged == null),
            isTrue,
          );
          await tap('إعادة التحقق من التقييم نفسه');
          if (fault == 'saveRead' || fault == 'wrongScore') {
            expect(publications, 0);
            expect(find.text('تم حفظ المسودة.'), findsOneWidget);
            await tap('مراجعة النشر');
            await tap('نشر لولي الأمر');
          }
        }
        expect(find.text('تم نشر التقييم لولي الأمر.'), findsOneWidget);
        expect(find.text('مراجعة النشر'), findsNothing);
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
    final reads = <String>[];
    final auth = await fixtures.authenticate('Coach', (m, p, b, token) async {
      if (m == 'GET') {
        reads.add(p);
        if (p == '/api/v1/academy/calendar') return {'today': '2026-11-05'};
        if (p ==
            '/api/v1/evaluations?playerId=child&sportEnrollmentId=enrollment') {
          return [
            {
              'id': 'saved',
              'sportEnrollmentId': 'enrollment',
              'status': 'Draft',
              'evaluationDate': '2026-11-05',
            },
            {
              'id': 'foreign',
              'sportEnrollmentId': 'other',
              'status': 'Draft',
              'evaluationDate': '2026-11-05',
            },
          ];
        }
      }
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
            'playerId': 'child',
            'player': 'طفل اصطناعي',
            'group': 'مجموعة اختبار',
          },
        ),
      ),
    );

    await t.pumpAndSettle();
    expect(find.text('بدء تقييم جديد'), findsNothing);
    expect(creates, 1);
    expect(find.text('تعذر تأكيد إنشاء المسودة.'), findsOneWidget);
    await t.tap(find.text('عرض تقييمات اللاعب'));
    await t.pumpAndSettle();
    expect(
      find.byKey(const ValueKey('evaluation-history-saved')),
      findsOneWidget,
    );
    expect(
      find.byKey(const ValueKey('evaluation-history-foreign')),
      findsNothing,
    );
    expect(reads, [
      '/api/v1/academy/calendar',
      '/api/v1/evaluations?playerId=child&sportEnrollmentId=enrollment',
    ]);
    expect(creates, 1);
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
          CoachPlayerEvaluations(
            auth: auth,
            enrollment: const {'id': 'enrollment', 'playerId': 'child'},
            canEvaluate: true,
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(
        find.byKey(const ValueKey('evaluation-history-other')),
        findsNothing,
      );
      await t.tap(find.byKey(const ValueKey('evaluation-history-mine')));
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
      expect(find.text('منشور لولي الأمر'), findsOneWidget);
      await t.scrollUntilVisible(
        find.text('لم يُقيّم'),
        120,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.text('0 من 100'), findsOneWidget);
      expect(find.text('لم يُقيّم'), findsOneWidget);
      expect(find.byType(Slider), findsNothing);
      expect(find.text('نشر التقييم لولي الأمر'), findsNothing);
      expect(find.byType(TextField), findsNothing);
      expect(writes, 0);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'the coach opens a saved internal evaluation from the player profile',
    (t) async {
      var historyReads = 0, recordReads = 0;
      final auth = await fixtures.authenticate('Coach', (
        m,
        path,
        body,
        token,
      ) async {
        expect(m, 'GET');
        if (path == '/api/v1/evaluations/options') {
          return {
            'canEvaluate': false,
            'enrollments': [
              {
                'id': 'swim-enrollment',
                'playerId': 'player',
                'player': 'لاعب اصطناعي',
                'sport': 'السباحة',
                'group': 'مجموعة السباحة',
                'groupId': 'group',
              },
            ],
          };
        }
        if (path ==
            '/api/v1/evaluations?playerId=player&sportEnrollmentId=swim-enrollment') {
          historyReads++;
          return [
            for (final (id, enrollment, status) in [
              ('internal', 'swim-enrollment', 'PublishedInternal'),
              ('public', 'swim-enrollment', 'Published'),
              ('draft', 'swim-enrollment', 'Draft'),
              ('other-sport', 'football-enrollment', 'Published'),
              ('superseded', 'swim-enrollment', 'Superseded'),
            ])
              {
                'id': id,
                'sportEnrollmentId': enrollment,
                'status': status,
                'evaluationDate': '2026-11-04',
                'group': 'مجموعة السباحة',
              },
          ];
        }
        if (path == '/api/v1/evaluations/internal') {
          recordReads++;
          return {
            'id': 'internal',
            'sportEnrollmentId': 'swim-enrollment',
            'status': 'PublishedInternal',
            'version': 2,
            'player': 'لاعب اصطناعي',
            'sport': 'السباحة',
            'group': 'مجموعة السباحة',
            'evaluationDate': '2026-11-04',
            'generalNotes': 'متابعة وضع الجسم',
            'scores': [
              {
                'criterionId': 'zero',
                'name': 'الانزلاق',
                'score': 0,
                'notes': 'تدريب على الدفع',
              },
              {'criterionId': 'none', 'name': 'التنفس', 'score': null},
            ],
          };
        }
        throw StateError('Unexpected read: $path');
      });
      await t.pumpWidget(
        fixtures.app(
          CoachPlayerPage(
            auth: auth,
            enrollmentId: 'swim-enrollment',
            groupId: 'group',
          ),
        ),
      );
      await t.pumpAndSettle();
      final history = find.text('تقييمات اللاعب');
      await t.ensureVisible(history);
      await t.tap(history);
      await t.pumpAndSettle();

      final internal = find.byKey(
        const ValueKey('evaluation-history-internal'),
      );
      expect(internal, findsOneWidget);
      expect(
        find.byKey(const ValueKey('evaluation-history-public')),
        findsOneWidget,
      );
      for (final id in ['draft', 'other-sport', 'superseded']) {
        expect(find.byKey(ValueKey('evaluation-history-$id')), findsNothing);
      }
      await t.tap(internal);
      await t.pumpAndSettle();
      expect(find.text('للاستخدام الداخلي'), findsOneWidget);
      expect(find.text('منشور لولي الأمر'), findsNothing);
      expect(find.text('0 من 100'), findsOneWidget);
      expect(find.text('لم يُقيّم'), findsOneWidget);
      expect(find.text('تدريب على الدفع'), findsOneWidget);
      expect(find.byType(Slider), findsNothing);
      await t.tap(find.byType(BackButton));
      await t.pumpAndSettle();
      expect(internal, findsOneWidget);
      expect(historyReads, 1);
      expect(recordReads, 1);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  for (final status in [404, 503]) {
    testWidgets(
      'saved evaluation read failure $status is handled without writes',
      (t) async {
        var reads = 0;
        final auth = await fixtures.authenticate('Coach', (
          method,
          path,
          body,
          token,
        ) async {
          expect(method, 'GET');
          expect(path, '/api/v1/evaluations/record');
          if (++reads == 1) {
            throw ApiFailure(status, 'unexpected transport detail');
          }
          return {
            'id': 'record',
            'sportEnrollmentId': 'enrollment',
            'player': 'لاعب اصطناعي',
            'status': 'Published',
            'version': 1,
            'scores': [],
          };
        });
        await t.pumpWidget(
          fixtures.app(
            CoachEvaluation(
              auth: auth,
              evaluationId: 'record',
              enrollment: const {'id': 'enrollment'},
            ),
          ),
        );
        await t.pumpAndSettle();
        expect(find.text('unexpected transport detail'), findsNothing);
        final retry = find.text('إعادة تحميل التقييم');
        if (status == 404) {
          expect(retry, findsNothing);
          expect(find.text('هذا التقييم غير متاح لحسابك.'), findsOneWidget);
          expect(reads, 1);
        } else {
          expect(find.text('تعذر تحميل التقييم.'), findsOneWidget);
          await t.tap(retry);
          await t.pumpAndSettle();
          expect(find.text('منشور لولي الأمر'), findsOneWidget);
          expect(reads, 2);
        }
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
}
