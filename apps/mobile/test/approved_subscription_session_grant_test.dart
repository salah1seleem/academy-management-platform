import 'support/visibility.dart';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/shared/staff_capabilities.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart' show FakeApi;
import 'v2_role_workspaces_test.dart' show authenticate, app;

const period = {
  'id': 'period',
  'player': 'طفل اصطناعي',
  'sport': 'كرة القدم',
  'branch': 'فرع اختبار',
  'plan': 'باقة حصص اختبار',
  'planType': 'Combined',
  'status': 'Active',
  'startDate': '2026-10-01',
  'endDate': '2026-10-30',
  'initialSessions': 10,
  'remainingSessions': 3,
  'version': 12,
  'canGrantSessions': true,
  'priceSnapshot': 900,
  'currencySnapshot': 'EGP',
  'adjustments': <Object>[],
};

void main() {
  for (final fault in ['none', 'lostRead', 'wrongAudit', 'unknownResponse']) {
    testWidgets(
      'native session grant $fault verifies count and whole-period end and retains the original request',
      (t) async {
        var posts = 0, changed = false, fail = fault != 'none';
        Map? body;
        final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
          if (p.endsWith('/calendar')) return {'today': '2026-10-21'};
          if (m == 'POST') {
            expect(p, '/api/v1/subscriptions/periods/period/sessions');
            posts++;
            body = b as Map;
            changed = true;
            if (fault == 'unknownResponse' && fail) {
              fail = false;
              throw const ApiFailure(0, 'تعذر تأكيد الرد');
            }
            return {'adjustmentId': 'audit'};
          }
          if (changed && fail && fault == 'lostRead') {
            fail = false;
            throw const ApiFailure(0, 'تعذر تأكيد القراءة');
          }
          final wrong = changed && fail && fault == 'wrongAudit';
          if (wrong) fail = false;
          return {
            ...period,
            if (changed) ...{
              'remainingSessions': 7,
              'endDate': '2026-11-05',
              'adjustments': [
                {
                  'id': 'audit',
                  'type': 'SessionsAdded',
                  'sessionsDelta': wrong ? 99 : 4,
                  'oldRemainingSessions': 3,
                  'newRemainingSessions': 7,
                  'newEndDate': '2026-11-05',
                  'effectiveDate': '2026-10-21',
                  'reason': 'سبب اصطناعي',
                },
              ],
            },
          };
        });
        await t.pumpWidget(
          app(
            NativeSubscriptionPeriod(
              auth: auth,
              id: 'period',
              access: const StaffCapabilities(
                owner: true,
                permissions: [],
                scope: {},
              ),
            ),
          ),
        );
        await t.pumpAndSettle();
        Future<void> reach(Finder f) async {
          await revealTestTarget(t, f);
        }

        Future<void> tap(String label) async {
          final f = find.text(label).last;
          await reach(f);
          await t.tap(f);
          await t.pumpAndSettle();
        }

        await t.tap(
          find.widgetWithText(DropdownButtonFormField<String>, 'الإجراء'),
        );
        await t.pumpAndSettle();
        expect(find.text('إضافة أيام'), findsNothing);
        await t.tap(find.text('إضافة حصص وتحديد نهاية الاشتراك').last);
        await t.pumpAndSettle();
        final count = find.widgetWithText(TextFormField, 'عدد الحصص الإضافية');
        await reach(count);
        await t.enterText(count, '4');
        final date = find.widgetWithText(
          TextFormField,
          'نهاية الاشتراك الجديدة',
        );
        await reach(date);
        await t.tap(date);
        await t.pumpAndSettle();
        final local = MaterialLocalizations.of(
          t.element(find.byType(DatePickerDialog)),
        );
        await t.tap(find.byIcon(Icons.edit_outlined));
        await t.pumpAndSettle();
        await t.enterText(
          find.descendant(
            of: find.byType(DatePickerDialog),
            matching: find.byType(TextField),
          ),
          local.formatCompactDate(DateTime(2026, 11, 5)),
        );
        await t.tap(find.text(local.okButtonLabel));
        await t.pumpAndSettle();
        final reason = find.widgetWithText(TextFormField, 'سبب التعديل');
        await reach(reason);
        await t.enterText(reason, 'سبب اصطناعي');
        await tap('راجعت أثر التعديل على الاشتراك وأؤكد تنفيذه.');
        await tap('تأكيد تعديل الاشتراك');
        if (fault != 'none') {
          expect(
            find.text('حُفظ التعديل وأُعيدت قراءة سجل الاشتراك.'),
            findsNothing,
          );
          expect(
            t
                .widgetList<AbsorbPointer>(find.byType(AbsorbPointer))
                .any((p) => p.absorbing),
            isTrue,
          );
          await tap('إعادة تأكيد التعديل نفسه');
        }
        expect(
          find.text('حُفظ التعديل وأُعيدت قراءة سجل الاشتراك.'),
          findsOneWidget,
        );
        expect(body, {
          'sessions': 4,
          'endDate': '2026-11-05',
          'reason': 'سبب اصطناعي',
          'expectedVersion': 12,
        });
        expect(posts, fault == 'unknownResponse' ? 2 : 1);
        final api = auth.transport as FakeApi;
        final keys = [
          for (var i = 0; i < api.paths.length; i++)
            if (api.paths[i].endsWith('/sessions')) api.keys[i],
        ];
        expect(keys.toSet(), hasLength(1));
        expect(keys.first, isNotNull);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  for (final grant in [false, true]) {
    testWidgets(
      'native administrator can see session grant only with current server eligibility $grant',
      (t) async {
        final auth = await authenticate(
          'AcademyAdmin',
          (m, p, b, token) async => p.endsWith('/calendar')
              ? {'today': '2026-10-21'}
              : {...period, 'canGrantSessions': grant},
        );
        await t.pumpWidget(
          app(
            NativeSubscriptionPeriod(
              auth: auth,
              id: 'period',
              access: const StaffCapabilities(
                owner: false,
                permissions: ['subscription.adjustment.manage'],
                scope: {
                  'useActionGrants': true,
                  'actions': [
                    {'module': 'subscriptions', 'read': true, 'edit': true},
                  ],
                  'workflowActions': [
                    {'key': 'workflow.subscriptions.sessions', 'allowed': true},
                  ],
                },
              ),
            ),
          ),
        );
        await t.pumpAndSettle();
        final choice = find.widgetWithText(
          DropdownButtonFormField<String>,
          'الإجراء',
        );
        if (choice.evaluate().isNotEmpty) {
          await t.tap(choice);
          await t.pumpAndSettle();
        }
        expect(
          find.text('إضافة حصص وتحديد نهاية الاشتراك'),
          grant ? findsOneWidget : findsNothing,
        );
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
}
