import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/guardian/coach_feedback.dart';
import 'package:academy_mobile/features/owner/feedback_triage.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' as owner;
import 'guardian_test.dart' as guardian;
import 'auth_controller_test.dart' show FakeApi;

void main() {
  testWidgets(
    'owner human triage retries the same optional-note intent without publishing a reply',
    (t) async {
      var attempts = 0, saved = false;
      final bodies = <Object?>[];
      final auth = await owner.owner((m, p, b, token) async {
        expect(p, '/api/v1/manage/feedback/f/triage');
        bodies.add(b);
        attempts++;
        if (attempts == 1) {
          throw const ApiFailure(503, 'تعذر حفظ الفرز الآن.');
        }
        return {'id': 'f'};
      });
      await t.pumpWidget(
        owner.shell(
          Scaffold(
            body: SingleChildScrollView(
              child: FeedbackHumanTriage(
                auth: auth,
                item: {
                  'id': 'f',
                  'version': 7,
                  'subject': 'Water',
                  'urgency': 'Safety',
                  'safety': true,
                },
                saved: () => saved = true,
              ),
            ),
          ),
        ),
      );
      await t.pumpAndSettle();
      final dd = find.widgetWithText(
        DropdownButtonFormField<String>,
        'درجة الاستعجال المراجعة',
      );
      await t.ensureVisible(dd);
      await t.tap(dd);
      await t.pumpAndSettle();
      await t.tap(find.text('عادية').last);
      await t.pumpAndSettle();
      final submit = find.text('تأكيد الفرز البشري');
      await t.ensureVisible(submit);
      await t.tap(submit);
      await t.pumpAndSettle();
      expect(saved, isFalse);
      expect(find.text('تعذر حفظ الفرز الآن.'), findsOneWidget);
      await t.ensureVisible(submit);
      await t.tap(submit);
      await t.pumpAndSettle();
      expect(saved, isTrue);
      expect(bodies.length, 2);
      expect(bodies[0], bodies[1]);
      expect(bodies[0], containsPair('note', null));
      expect(bodies[0], containsPair('urgency', 'Routine'));
      expect(t.takeException(), isNull);
    },
  );
  testWidgets(
    'guardian reports safety without accusing a coach and retries with the same request key',
    (t) async {
      var attempts = 0;
      final sent = <Object?>[];
      final auth = await guardian.logged((m, p, b, token) async {
        if (m == 'POST') {
          sent.add(b);
          attempts++;
          if (attempts == 1) {
            throw const ApiFailure(503, 'تعذر الإرسال الآن.');
          }
          return {'id': 'f'};
        }
        return {
          'asOfDate': '2026-10-19',
          'groups': [
            {
              'id': 'g',
              'name': 'مجموعة اصطناعية',
              'sport': 'كرة القدم',
              'coaches': <Object>[],
            },
          ],
          'mine': <Object>[],
        };
      });
      await t.pumpWidget(
        guardian.shell(CoachFeedbackPage(auth: auth, playerId: 'child')),
      );
      await t.pumpAndSettle();
      Future<void> select(String label, String option) async {
        final dd = find.widgetWithText(DropdownButtonFormField<String>, label);
        await t.ensureVisible(dd);
        await t.tap(dd);
        await t.pumpAndSettle();
        await t.tap(find.text(option).last);
        await t.pumpAndSettle();
      }

      await select('المجموعة', 'مجموعة اصطناعية · كرة القدم');
      await select('نوع الملاحظة', 'شكوى');
      await select('موضوع الملاحظة', 'المياه');
      await select('درجة الاستعجال', 'سلامة أو إساءة');
      final field = find.widgetWithText(TextFormField, 'اكتب الملاحظة');
      await t.ensureVisible(field);
      await t.enterText(field, 'بلاغ اصطناعي للمراجعة البشرية فقط');
      await t.testTextInput.receiveAction(TextInputAction.done);
      await t.pumpAndSettle();
      final submit = find.text('إرسال للإدارة');
      await t.ensureVisible(submit);
      await t.tap(submit);
      await t.pumpAndSettle();
      expect(attempts, 1);
      await t.ensureVisible(submit);
      await t.tap(submit);
      await t.pumpAndSettle();
      expect(attempts, 2);
      expect(sent[0], sent[1]);
      expect(sent[0], containsPair('urgency', 'Safety'));
      expect(sent[0], containsPair('subject', 'Water'));
      expect(sent[0], containsPair('coachMembershipId', null));
      final keys = (auth.transport as FakeApi).keys
          .whereType<String>()
          .toList();
      expect(keys.length, 2);
      expect(keys[0], keys[1]);
      expect(find.textContaining('وصل بلاغ السلامة'), findsOneWidget);
      expect(t.takeException(), isNull);
    },
  );
}
