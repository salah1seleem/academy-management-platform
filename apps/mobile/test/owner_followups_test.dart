import 'dart:async';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/features/owner/owner_followups.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' as fixture;

Map<String, dynamic> options() => {
  'asOfDate': '2026-10-17',
  'owners': [
    {'id': 'owner', 'name': 'مالك اصطناعي'},
  ],
  'branches': <Object>[],
  'candidates': <Object>[],
  'rule': 'تسجيلات مسندة وليست ربحًا',
};
Map<String, dynamic> item() => {
  'id': 'followup',
  'version': 5,
  'trainingGroupId': 'group',
  'condition': 'GroupLowOccupancy',
  'state': 'Closed',
  'assignedToMembershipId': null,
  'dueOn': null,
  'resolutionReason': 'أغلقت المتابعة فقط',
  'conditionActive': true,
  'source': {
    'name': 'مجموعة متابعة اصطناعية',
    'enrollments': 3,
    'capacity': 20,
  },
  'evidence': {'observedAtUtc': '2026-10-17T02:30:00Z'},
};
void main() {
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets(
      'Recorded absence summary at $width and 200% never infers unknown attendance or a penalty',
      (t) async {
        t.view.physicalSize = Size(width, 900);
        t.view.devicePixelRatio = 1;
        addTearDown(t.view.resetPhysicalSize);
        addTearDown(t.view.resetDevicePixelRatio);
        final auth = await fixture.owner(
          (m, p, b, token) async => {
            'item': {
              ...item(),
              'condition': 'GroupRecordedAbsences',
              'source': {
                ...Map<String, dynamic>.from(item()['source'] as Map),
                'recordedAbsences': {
                  'playerCount': 1,
                  'enrollmentCount': 2,
                  'fromDate': '2026-10-10',
                  'toDate': '2026-10-12',
                },
              },
              'evidence': {
                'absenceEvidence': [
                  {'SessionId': 's1', 'SessionDate': '2026-10-10'},
                ],
              },
            },
            'owners': options()['owners'],
            'history': <Object>[],
          },
        );
        await t.pumpWidget(
          fixture.shell(
            MediaQuery(
              data: MediaQueryData(
                size: Size(width, 900),
                textScaler: const TextScaler.linear(2),
              ),
              child: OwnerFollowUpPage(auth: auth, id: 'followup'),
            ),
          ),
        );
        await t.pumpAndSettle();
        expect(find.textContaining('1 لاعب فريد · 2 تسجيل'), findsOneWidget);
        expect(
          find.textContaining('دون رسالة أو عقوبة تلقائية'),
          findsOneWidget,
        );
        await t.scrollUntilVisible(
          find.text('مراجعة كشف 2026-10-10'),
          240,
          scrollable: find.byType(Scrollable).first,
        );
        await t.pumpAndSettle();
        expect(find.text('مراجعة كشف 2026-10-10'), findsOneWidget);
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
      },
    );
  }
  testWidgets(
    'Draft evidence uses actual source, hides on logout and reauthorizes on resume',
    (t) async {
      var allowed = true;
      final paths = <String>[];
      final auth = await fixture.owner((method, path, body, token) async {
        expect(method, 'GET');
        paths.add(path);
        if (!allowed) {
          throw const ApiFailure(403, 'ليس لديك صلاحية قراءة المصدر.');
        }
        return {
          'player': 'لاعب دليل اصطناعي',
          'group': 'مجموعة اصطناعية',
          'sport': 'كرة القدم',
          'status': 'Draft',
          'evaluationDate': '2026-09-20',
          'generalNotes': 'ملاحظة خاصة للاختبار',
          'scores': [
            {'name': 'التمرير', 'score': 70},
          ],
        };
      });
      await t.pumpWidget(
        fixture.shell(FollowUpEvaluationSource(auth: auth, id: 'draft-1')),
      );
      await t.pumpAndSettle();
      expect(find.text('لاعب دليل اصطناعي'), findsOneWidget);
      expect(find.text('مسودة غير منشورة'), findsOneWidget);
      expect(find.text('نشر'), findsNothing);
      // Inactive is the renderable transition before the OS pauses frames.
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      await t.pumpAndSettle();
      expect(find.text('ملاحظة خاصة للاختبار'), findsNothing);
      allowed = false;
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await t.pumpAndSettle();
      expect(paths, [
        '/api/v1/evaluations/draft-1',
        '/api/v1/evaluations/draft-1',
      ]);
      expect(find.text('ملاحظة خاصة للاختبار'), findsNothing);
      expect(find.text('ليس لديك صلاحية قراءة المصدر.'), findsOneWidget);
      await auth.logout();
      await t.pumpAndSettle();
      expect(find.text('مصدر متابعة التقييم'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
    },
  );
  testWidgets(
    'Draft review distinguishes drafts and unique players and opens saved evidence',
    (t) async {
      final auth = await fixture.owner(
        (m, p, b, token) async => {
          'item': {
            ...item(),
            'condition': 'GroupEvaluationDrafts',
            'source': {
              ...Map<String, dynamic>.from(item()['source'] as Map),
              'evaluationDrafts': {'draftCount': 2, 'playerCount': 1},
            },
            'evidence': {
              'evaluationIds': ['draft-1'],
            },
          },
          'owners': options()['owners'],
          'history': <Object>[],
        },
      );
      await t.pumpWidget(
        fixture.shell(OwnerFollowUpPage(auth: auth, id: 'followup')),
      );
      await t.pumpAndSettle();
      expect(find.textContaining('2 مسودة · 1 لاعب فريد'), findsOneWidget);
      expect(find.textContaining('ليست حكم كفاءة'), findsOneWidget);
      expect(find.textContaining('عرض تقييم من دليل الإنشاء'), findsOneWidget);
      expect(find.text('نشر'), findsNothing);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
    },
  );
  testWidgets(
    'Coverage follow-up shows actual session times without manufacturing absence or pay changes',
    (t) async {
      final current = {
        ...item(),
        'condition': 'GroupSessionCoverage',
        'source': {
          ...Map<String, dynamic>.from(item()['source'] as Map),
          'coverageGaps': [
            {
              'id': 's',
              'sessionDate': '2026-10-18',
              'startTime': '17:00:00',
              'endTime': '18:00:00',
            },
          ],
        },
      };
      final auth = await fixture.owner(
        (m, p, b, token) async => {
          'item': current,
          'owners': options()['owners'],
          'history': <Object>[],
        },
      );
      await t.pumpWidget(
        fixture.shell(OwnerFollowUpPage(auth: auth, id: 'followup')),
      );
      await t.pumpAndSettle();
      expect(find.text('حصص قادمة بلا مدرب مسند مخول'), findsOneWidget);
      expect(
        find.textContaining('ليست غيابًا مسجلًا أو خصمًا'),
        findsOneWidget,
      );
      expect(find.text('2026-10-18 · 17:00–18:00'), findsOneWidget);
      expect(
        Directionality.of(t.element(find.text('2026-10-18 · 17:00–18:00'))),
        TextDirection.ltr,
      );
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
    },
  );
  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets(
      'Follow-ups are readable at $width with 200% text and clearing really requests all dates',
      (tester) async {
        tester.view.physicalSize = Size(width, 900);
        tester.view.devicePixelRatio = 1;
        addTearDown(tester.view.resetPhysicalSize);
        addTearDown(tester.view.resetDevicePixelRatio);
        final paths = <String>[];
        final auth = await fixture.owner((m, p, b, t) async {
          paths.add(p);
          return p.endsWith('/options')
              ? options()
              : {'items': <Object>[], 'hasMore': false};
        });
        await tester.pumpWidget(
          fixture.shell(
            MediaQuery(
              data: MediaQueryData(
                size: Size(width, 900),
                textScaler: const TextScaler.linear(2),
              ),
              child: OwnerFollowUpsPage(auth: auth),
            ),
          ),
        );
        await tester.pumpAndSettle();
        expect(
          paths.any(
            (p) => p.contains('from=2026-10-01') && p.contains('to=2026-10-17'),
          ),
          isTrue,
        );
        await tester.ensureVisible(find.text('مسح'));
        await tester.tap(find.text('مسح'));
        await tester.pumpAndSettle();
        expect(
          paths.any((p) => p.contains('allDates=true') && !p.contains('from=')),
          isTrue,
        );
        expect(tester.takeException(), isNull);
      },
    );
  }
  testWidgets(
    'Closed human follow-up keeps live problem and requires explicit save, with logout hiding it',
    (tester) async {
      final auth = await fixture.owner(
        (m, p, b, t) async => {
          'item': item(),
          'owners': options()['owners'],
          'history': <Object>[],
        },
      );
      await tester.pumpWidget(
        fixture.shell(OwnerFollowUpPage(auth: auth, id: 'followup')),
      );
      await tester.pumpAndSettle();
      expect(
        find.textContaining('الحالة الأصلية ما زالت قائمة'),
        findsOneWidget,
      );
      await tester.scrollUntilVisible(
        find.text('سبب إغلاق أو رفض المتابعة'),
        250,
        scrollable: find.byType(Scrollable).first,
      );
      expect(find.text('سبب إغلاق أو رفض المتابعة'), findsOneWidget);
      await auth.logout();
      await tester.pumpAndSettle();
      expect(find.text('مجموعة متابعة اصطناعية'), findsNothing);
    },
  );
  testWidgets('Late list response after logout cannot disclose group data', (
    tester,
  ) async {
    final pending = Completer<Object?>();
    final auth = await fixture.owner(
      (m, p, b, t) async => p.endsWith('/options') ? options() : pending.future,
    );
    await tester.pumpWidget(fixture.shell(OwnerFollowUpsPage(auth: auth)));
    await tester.pump();
    await auth.logout();
    pending.complete({
      'items': [item()],
      'hasMore': false,
    });
    await tester.pumpAndSettle();
    expect(find.text('مجموعة متابعة اصطناعية'), findsNothing);
    expect(tester.takeException(), isNull);
  });
  test('Request identifiers are independent valid UUID-shaped keys', () {
    final a = followUpRequestId();
    expect(a, matches(RegExp(r'^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$')));
    expect(a, isNot(followUpRequestId()));
  });
}
