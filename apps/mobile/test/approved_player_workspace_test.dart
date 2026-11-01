import 'dart:async';

import 'package:academy_mobile/features/admin/native_player_workspace.dart';
import 'package:academy_mobile/features/shared/staff_capabilities.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_role_workspaces_test.dart' show authenticate, app;

final player = {
  'id': 'player',
  'arabicName': 'لاعب اختبار',
  'dateOfBirth': '2016-01-01',
  'playerCode': 'P01',
  'enrollments': [
    {
      'id': 'enrollment',
      'sportName': 'كرة القدم',
      'sportId': 'football',
      'branchName': 'فرع اختبار',
      'branchId': 'branch',
      'groupName': 'مجموعة اختبار',
      'trainingGroupId': 'group',
      'status': 'Active',
      'subscription': {
        'id': 'period',
        'plan': 'باقة اختبار',
        'startDate': '2026-10-01',
        'endDate': '2026-10-30',
        'status': 'Active',
      },
    },
  ],
};
const period = {
  'id': 'period',
  'player': 'لاعب اختبار',
  'plan': 'باقة اختبار',
  'planType': 'Duration',
  'status': 'Active',
  'startDate': '2026-10-01',
  'endDate': '2026-10-30',
  'remainingSessions': 3,
  'version': 12,
  'priceSnapshot': 1000,
  'currencySnapshot': 'EGP',
  'adjustments': <Object>[],
};
Future<void> tap(WidgetTester t, String label) async {
  final f = find.text(label).last;
  await t.scrollUntilVisible(f, 160, scrollable: find.byType(Scrollable).first);
  await t.tap(f);
  await t.pumpAndSettle();
}

void main() {
  for (final mismatch in [false, true]) {
    testWidgets(
      'Player edit verifies nullable optional fields; mismatch=$mismatch',
      (t) async {
        var writes = 0;
        final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
          if (m == 'PUT') {
            writes++;
            expect((b as Map)['englishName'], '');
            expect(b['address'], '');
            return null;
          }
          return {
            ...player,
            'englishName': null,
            'address': mismatch ? 'عنوان آخر غير مطابق' : null,
          };
        });
        await t.pumpWidget(app(NativePlayerEdit(auth: auth, player: player)));
        await t.pumpAndSettle();
        await tap(t, 'حفظ بيانات اللاعب');
        expect(writes, 1);
        expect(
          find.text('حُفظت بيانات اللاعب وأُعيدت قراءتها.'),
          mismatch ? findsNothing : findsOneWidget,
        );
        expect(
          find.text('تعذر حفظ العملية. راجع الاتصال وحاول مجددًا.'),
          mismatch ? findsOneWidget : findsNothing,
        );
        await t.pumpWidget(const SizedBox());
        auth.dispose();
      },
    );
  }
  testWidgets(
    'Session-only periods never offer duration adjustments or freezing',
    (t) async {
      final auth = await authenticate(
        'AcademyOwner',
        (m, p, b, token) async => p.endsWith('/calendar')
            ? {'today': '2026-10-21'}
            : {...period, 'planType': 'Sessions'},
      );
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
      await t.tap(
        find.widgetWithText(DropdownButtonFormField<String>, 'الإجراء'),
      );
      await t.pumpAndSettle();
      expect(find.text('إضافة أيام'), findsNothing);
      expect(find.text('خصم أيام'), findsNothing);
      expect(find.text('تجميد الاشتراك'), findsNothing);
      expect(find.text('استئناف الاشتراك'), findsNothing);
      expect(find.text('إلغاء الاشتراك'), findsWidgets);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Attendance-only staff retain the player identity and attendance but never load or show financial tabs',
    (t) async {
      final calls = <String>[];
      final auth = await authenticate('AcademyAdmin', (m, p, b, token) async {
        calls.add(p);
        if (p.endsWith('my-permissions')) {
          return ['people.manage', 'attendance.read'];
        }
        if (p.endsWith('my-access')) {
          return {
            'useActionGrants': true,
            'actions': [
              {'module': 'players', 'read': true},
              {'module': 'attendance', 'read': true},
            ],
          };
        }
        return player;
      });
      await t.pumpWidget(app(NativePlayerWorkspace(auth: auth, id: 'player')));
      await t.pumpAndSettle();
      expect(find.text('لاعب اختبار'), findsOneWidget);
      expect(find.text('التدريب والحضور'), findsOneWidget);
      expect(find.text('الاشتراكات والماليات'), findsNothing);
      expect(find.text('تعديل بيانات اللاعب'), findsNothing);
      expect(
        calls.any(
          (p) =>
              p.contains('/periods/') ||
              p.contains('/financial') ||
              p.contains('/receivables'),
        ),
        isFalse,
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Named day adjustment uses the reviewed version and records only after reading persisted history',
    (t) async {
      Map? write;
      var changed = false;
      final auth = await authenticate('AcademyOwner', (m, p, b, token) async {
        if (p.endsWith('/calendar')) return {'today': '2026-10-21'};
        if (m == 'POST') {
          write = b as Map;
          changed = true;
          return {'adjustmentId': 'adjustment'};
        }
        return {
          ...period,
          'adjustments': changed
              ? [
                  {
                    'id': 'adjustment',
                    'type': 'DaysDeducted',
                    'effectiveDate': '2026-10-21',
                    'daysDelta': -2,
                    'reason': 'اختبار',
                  },
                ]
              : [],
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
      await t.tap(
        find.widgetWithText(DropdownButtonFormField<String>, 'الإجراء'),
      );
      await t.pumpAndSettle();
      await t.tap(find.text('خصم أيام').last);
      await t.pumpAndSettle();
      await t.enterText(find.widgetWithText(TextFormField, 'عدد الأيام'), '2');
      await tap(t, 'راجعت أثر التعديل على الاشتراك وأؤكد تنفيذه.');
      await tap(t, 'تأكيد تعديل الاشتراك');
      expect(write, containsPair('expectedVersion', 12));
      expect(write, containsPair('direction', 'Deduct'));
      expect(write, containsPair('days', 2));
      expect(
        find.text('حُفظ التعديل وأُعيدت قراءة سجل الاشتراك.'),
        findsOneWidget,
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets(
    'Explicitly denied cancellation and days never appear even with broad subscription CRUD',
    (t) async {
      final auth = await authenticate(
        'AcademyAdmin',
        (m, p, b, token) async =>
            p.endsWith('/calendar') ? {'today': '2026-10-21'} : period,
      );
      final access = StaffCapabilities(
        owner: false,
        permissions: ['subscription.adjustment.manage'],
        scope: {
          'useActionGrants': true,
          'actions': [
            {'module': 'subscriptions', 'read': true, 'edit': true},
          ],
          'workflowActions': [
            for (final key in [
              'workflow.subscriptions.cancel',
              'workflow.subscriptions.days',
              'workflow.subscriptions.pause',
            ])
              {'key': key, 'hasExplicitGrant': true, 'allowed': false},
          ],
        },
      );
      await t.pumpWidget(
        app(NativeSubscriptionPeriod(auth: auth, id: 'period', access: access)),
      );
      await t.pumpAndSettle();
      expect(find.text('الإجراء'), findsNothing);
      expect(find.textContaining('1000'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );
  testWidgets('Logout during player read never displays the previous child', (
    t,
  ) async {
    final pending = Completer<Object?>();
    final auth = await authenticate(
      'AcademyOwner',
      (m, p, b, token) => pending.future,
    );
    await t.pumpWidget(app(NativePlayerWorkspace(auth: auth, id: 'player')));
    await t.pump();
    await auth.logout();
    pending.complete(player);
    await t.pumpAndSettle();
    expect(find.text('لاعب اختبار'), findsNothing);
    expect(t.takeException(), isNull);
    await t.pumpWidget(const SizedBox());
    auth.dispose();
  });
}
