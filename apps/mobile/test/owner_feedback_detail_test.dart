import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/owner/owner_feedback_detail.dart';
import 'package:academy_mobile/features/owner/owner_followups.dart';
import 'package:academy_mobile/features/shared/feedback_state.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' as fixture;

Map<String, dynamic> detail() => {
  'id': 'f1',
  'kind': 'Complaint',
  'message': 'شكوى اصطناعية خاصة',
  'status': 'Open',
  'dueOn': '2026-10-10',
  'overdue': true,
  'ownerResponse': null,
  'playerName': 'طفل اصطناعي',
  'groupName': 'مجموعة اصطناعية',
  'branchName': 'فرع اصطناعي',
  'asOfDate': '2026-10-18',
};
void main() {
  test(
    'private history time has Latin digits and an explicit UTC conversion',
    () {
      expect(
        feedbackUtcTime('2026-10-19T01:05:00+03:00'),
        '2026/10/18 · 22:05',
      );
      expect(feedbackUtcTime(null), 'وقت غير متاح');
    },
  );
  testWidgets(
    'private stage retries keep intent and never masquerade as a family response',
    (t) async {
      var attempts = 0, saved = false;
      final bodies = <Object?>[];
      final auth = await fixture.owner((m, p, b, k) async {
        if (m == 'POST') {
          expect(p, '/api/v1/manage/feedback/f1/transition');
          bodies.add(b);
          attempts++;
          if (attempts == 1) {
            throw const ApiFailure(503, 'تعذر حفظ المعالجة الآن.');
          }
          saved = true;
          return {'id': 'f1'};
        }
        return {
          ...detail(),
          'version': saved ? 8 : 7,
          'status': saved ? 'InProgress' : 'Open',
          'history': saved
              ? [
                  {
                    'id': 'c1',
                    'action': 'Transition',
                    'fromStatus': 'Open',
                    'toStatus': 'InProgress',
                    'internalNote': 'تحقيق داخلي فقط',
                    'actorName': 'مالك اصطناعي',
                    'createdAtUtc': '2026-10-19T07:00:00Z',
                  },
                ]
              : [],
        };
      });
      await t.pumpWidget(
        fixture.shell(OwnerFeedbackDetail(auth: auth, id: 'f1')),
      );
      await t.pumpAndSettle();
      await t.scrollUntilVisible(
        find.text('الخطوة التالية'),
        300,
        scrollable: find.byType(Scrollable).first,
      );
      final step = find.widgetWithText(
        DropdownButtonFormField<String>,
        'الخطوة التالية',
      );
      await t.ensureVisible(step);
      await t.pumpAndSettle();
      await t.tap(step);
      await t.pumpAndSettle();
      await t.tap(find.text('قيد المعالجة').last);
      await t.pumpAndSettle();
      final field = find.widgetWithText(TextField, 'ملاحظة داخلية (اختياري)');
      await t.ensureVisible(field);
      await t.enterText(field, 'تحقيق داخلي فقط');
      await t.testTextInput.receiveAction(TextInputAction.done);
      await t.pumpAndSettle();
      final button = find.text('حفظ مرحلة المعالجة');
      await t.ensureVisible(button);
      await t.tap(button);
      await t.pumpAndSettle();
      expect(saved, isFalse);
      expect(find.widgetWithText(TextField, 'تحقيق داخلي فقط'), findsOneWidget);
      await t.ensureVisible(button);
      await t.tap(button);
      await t.pumpAndSettle();
      expect(saved, isTrue);
      expect(bodies.length, 2);
      expect(bodies[1], bodies[0]);
      expect(bodies[0], containsPair('version', 7));
      await t.scrollUntilVisible(
        find.text('ملاحظة داخلية: تحقيق داخلي فقط'),
        250,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.text('ملاحظة داخلية: تحقيق داخلي فقط'), findsOneWidget);
      expect(t.takeException(), isNull);
    },
  );
  testWidgets(
    'specific family note saves a source reply, not a follow-up state or money',
    (t) async {
      var saved = false;
      final auth = await fixture.owner((m, p, b, k) async {
        if (m == 'POST') {
          expect(p, '/api/v1/manage/feedback/f1/resolve');
          expect(b, {'response': 'رد اصطناعي محفوظ للأسرة'});
          saved = true;
          return {'id': 'f1'};
        }
        expect(p, '/api/v1/manage/feedback/f1');
        return {
          ...detail(),
          if (saved) 'status': 'Resolved',
          if (saved) 'ownerResponse': 'رد اصطناعي محفوظ للأسرة',
        };
      });
      await t.pumpWidget(
        fixture.shell(OwnerFeedbackDetail(auth: auth, id: 'f1')),
      );
      await t.pumpAndSettle();
      expect(find.text('شكوى اصطناعية خاصة'), findsOneWidget);
      final field = find.widgetWithText(TextField, 'الرد على الملاحظة');
      await t.ensureVisible(field);
      await t.enterText(field, 'رد اصطناعي محفوظ للأسرة');
      await t.testTextInput.receiveAction(TextInputAction.done);
      await t.pumpAndSettle();
      final button = find.text('إرسال الرد وحل الملاحظة');
      await t.ensureVisible(button);
      await t.pumpAndSettle();
      await t.tap(button);
      await t.pumpAndSettle();
      expect(saved, isTrue);
      expect(find.text('رد الإدارة: رد اصطناعي محفوظ للأسرة'), findsOneWidget);
      expect(find.text('إرسال الرد وحل الملاحظة'), findsNothing);
      expect(t.takeException(), isNull);
    },
  );
  testWidgets(
    'family text is discarded in background and reauthorized on return, then hidden at logout',
    (t) async {
      var allowed = true;
      final auth = await fixture.owner((m, p, b, k) async {
        if (!allowed) {
          throw const ApiFailure(403, 'الملاحظة غير متاحة.');
        }
        return detail();
      });
      await t.pumpWidget(
        fixture.shell(OwnerFeedbackDetail(auth: auth, id: 'f1')),
      );
      await t.pumpAndSettle();
      expect(find.text('شكوى اصطناعية خاصة'), findsOneWidget);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      await t.pumpAndSettle();
      expect(find.text('شكوى اصطناعية خاصة'), findsNothing);
      allowed = false;
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await t.pumpAndSettle();
      expect(find.text('الملاحظة غير متاحة.'), findsOneWidget);
      expect(find.text('شكوى اصطناعية خاصة'), findsNothing);
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('متابعة ملاحظة الأسرة'), findsNothing);
    },
  );
  testWidgets(
    'overdue summary counts notes and unique accounts without naming a family',
    (t) async {
      await t.pumpWidget(
        fixture.shell(
          Scaffold(
            body: overdueFeedbackSummary({
              'overdueFeedback': {
                'complaintCount': 1,
                'suggestionCount': 1,
                'submitterCount': 1,
                'oldestDueOn': '2026-10-10',
              },
            }),
          ),
        ),
      );
      expect(find.text('1 شكوى · 1 اقتراح · 1 حساب مقدم فريد'), findsOneWidget);
      expect(find.textContaining('ليست اتهامًا'), findsOneWidget);
      expect(find.byType(TextButton), findsNothing);
      expect(t.takeException(), isNull);
    },
  );
}
