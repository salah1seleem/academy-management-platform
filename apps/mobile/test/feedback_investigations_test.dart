import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/shared/feedback_investigations.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' as fixture;

void main() {
  testWidgets(
    'private findings keep retry intent and disappear on background',
    (t) async {
      var attempts = 0, saved = false;
      final bodies = <Object?>[];
      final auth = await fixture.owner((m, p, b, k) async {
        if (m == 'POST') {
          expect(p, '/api/v1/my-followups/investigations/opaque/transition');
          bodies.add(b);
          attempts++;
          if (attempts == 1) {
            throw const ApiFailure(503, 'تعذر حفظ النتيجة الآن.');
          }
          saved = true;
          return {'assignmentId': 'opaque'};
        }
        return {
          'assignmentId': 'opaque',
          'version': saved ? 8 : 7,
          'brief': 'موجز راجعه المالك دون أسماء',
          'status': 'Open',
          'dueOn': null,
          'canEdit': true,
          'findings': saved
              ? [
                  {'id': 'result', 'note': 'نتيجة خاصة للمالك'},
                ]
              : [],
        };
      });
      await t.pumpWidget(
        fixture.shell(
          PrivateInvestigationsPage(auth: auth, assignmentId: 'opaque'),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('موجز راجعه المالك دون أسماء'), findsOneWidget);
      expect(find.text('إرسال الرد وحل الملاحظة'), findsNothing);
      final field = find.widgetWithText(TextField, 'نتيجة التحقيق للمالك');
      await t.ensureVisible(field);
      await t.enterText(field, 'نتيجة خاصة للمالك');
      await t.testTextInput.receiveAction(TextInputAction.done);
      final button = find.text('حفظ النتيجة الخاصة');
      await t.ensureVisible(button);
      await t.tap(button);
      await t.pumpAndSettle();
      expect(saved, isFalse);
      expect(find.text('تعذر حفظ النتيجة الآن.'), findsOneWidget);
      await t.ensureVisible(button);
      await t.tap(button);
      await t.pumpAndSettle();
      expect(saved, isTrue);
      expect(bodies.length, 2);
      expect(bodies[0], bodies[1]);
      expect(bodies[0], containsPair('version', 7));
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      await t.pumpAndSettle();
      expect(find.text('موجز راجعه المالك دون أسماء'), findsNothing);
      expect(find.byType(TextField), findsNothing);
      expect(t.takeException(), isNull);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await t.pumpAndSettle();
    },
  );
  testWidgets(
    'owner reviews explicit brief and read-only delegate has no result action',
    (t) async {
      Object? sent;
      final auth = await fixture.owner((m, p, b, k) async {
        if (m == 'POST') {
          sent = b;
          return {'id': 'f'};
        }
        return {
          'items': [
            {'id': 'admin', 'name': 'إداري مخول اصطناعي'},
          ],
        };
      });
      await t.pumpWidget(
        fixture.shell(
          OwnerInvestigationAssignment(
            auth: auth,
            item: {'id': 'f', 'version': 7},
            saved: () {},
          ),
        ),
      );
      await t.pumpAndSettle();
      expect(
        t.widget<TextField>(find.byType(TextField)).controller!.text,
        isEmpty,
      );
      await t.tap(find.byType(DropdownButtonFormField<String>));
      await t.pumpAndSettle();
      await t.tap(find.text('إداري مخول اصطناعي').last);
      await t.pumpAndSettle();
      await t.enterText(find.byType(TextField), 'راجع تنظيم وقت التدريب');
      await t.tap(find.text('حفظ تكليف التحقيق'));
      await t.pumpAndSettle();
      expect(sent, containsPair('brief', 'راجع تنظيم وقت التدريب'));
      expect(sent, containsPair('investigatorMembershipId', 'admin'));
      final readOnly = await fixture.owner(
        (m, p, b, k) async => {
          'assignmentId': 'opaque',
          'brief': 'موجز مستقل',
          'status': 'Open',
          'canEdit': false,
          'findings': [],
        },
      );
      await t.pumpWidget(
        fixture.shell(
          PrivateInvestigationsPage(auth: readOnly, assignmentId: 'opaque'),
        ),
      );
      await t.pumpAndSettle();
      expect(find.text('للقراءة فقط وفق صلاحياتك الحالية.'), findsOneWidget);
      expect(find.text('حفظ النتيجة الخاصة'), findsNothing);
      expect(t.takeException(), isNull);
    },
  );
}
