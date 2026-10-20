import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/admin/admin_home.dart';
import 'package:academy_mobile/features/coach/coach_home.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:academy_mobile/features/owner/owner_operations.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'owner_test.dart' show owner, homeOwner, shell, summary;
import 'v2_role_workspaces_test.dart' show authenticate;

Json session(int id) => {
  'id': 's$id',
  'group': 'مجموعة تجريبية $id',
  'startTime': '${14 + id}:00:00',
  'endTime': '${15 + id}:00:00',
  'sessionDate': '2026-10-16',
  'branch': 'فرع العرض',
  'sport': 'كرة القدم',
  'playerCount': 5,
  'status': 'Scheduled',
};
Json operations({int complaints = 1}) => {
  'pendingPaymentQueueBacklog': 9,
  'pendingPaymentBacklog': 10,
  'openComplaints': complaints,
  'openSuggestions': 0,
  'overdueFeedback': 0,
  'groupAttention': [
    {
      'id': 'g1',
      'name': 'مجموعة تحتاج متابعة',
      'hasCoach': false,
      'notRecorded': 3,
      'absent': 0,
    },
  ],
  'coachPerformance': [
    {
      'id': 'c1',
      'name': 'مدرب العرض',
      'groupCount': 1,
      'sessionCount': 3,
      'parentRating': null,
      'parentRatingCount': 0,
    },
  ],
  'accessChanges': 2,
  'pendingExpenses': [],
  'attentionRule': 'مدرب غير مسند أو سجلات لم تسجل',
};
Future<void> reach(WidgetTester tester, Finder f) async {
  await tester.scrollUntilVisible(
    f,
    220,
    scrollable: find.byType(Scrollable).first,
  );
  await tester.ensureVisible(f);
  await tester.pumpAndSettle();
}

void main() {
  testWidgets(
    'Decision return refresh survives token rotation in the same context',
    (tester) async {
      var reads = 0;
      final auth = await owner((m, p, b, t) async {
        if (p.contains('/payment-requests')) return {'pending': 9, 'items': []};
        if (p.contains("/reports/owner-summary")) reads++;
        return {
          'operations': {
            ...operations(),
            'groupAttention': [],
            'coachPerformance': [],
          },
        };
      });
      await tester.pumpWidget(shell(OwnerDecisionsPage(auth: auth)));
      await tester.pumpAndSettle();
      await tester.tap(find.text('راجع طلبات الدفع'));
      await tester.pumpAndSettle();
      final before = auth.session!.selected;
      await auth.refresh();
      expect(identical(before, auth.session!.selected), isFalse);
      await tester.tap(find.byType(BackButton).last);
      await tester.pumpAndSettle();
      expect(reads, 2);
      await auth.logout();
      await tester.pumpAndSettle();
      expect(find.text('راجع طلبات الدفع'), findsNothing);
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'Admin loads only authorized operational sources, orders actual sessions and opens their roster',
    (tester) async {
      final calls = <String>[];
      final auth = await authenticate('AcademyAdmin', (m, p, b, t) async {
        calls.add(p);
        if (p.endsWith('/my-permissions')) return ['attendance.read'];
        if (p.endsWith('/s0/players')) {
          return {
            'canManageAttendance': false,
            'session': session(0),
            'players': [],
          };
        }
        return [
          session(3),
          session(1),
          session(0),
          session(2),
          {...session(4), 'status': 'Cancelled'},
        ];
      });
      await tester.pumpWidget(shell(AdminHome(auth: auth)));
      await tester.pumpAndSettle();
      expect(calls, [
        '/api/v1/my-permissions',
        '/api/v1/attendance/sessions',
        '/api/v1/notifications',
      ]);
      expect(find.text('جدول اليوم · 4 حصة'), findsOneWidget);
      expect(
        tester
            .widget<CoachSessionCard>(find.byType(CoachSessionCard).first)
            .session['id'],
        's0',
      );
      expect(find.text('مجموعة تجريبية 4'), findsNothing);
      expect(find.text('طلبات الدفع'), findsNothing);
      await reach(tester, find.text('تفاصيل الحصة').first);
      await tester.tap(find.text('تفاصيل الحصة').first);
      await tester.pumpAndSettle();
      expect(calls.last, '/api/v1/attendance/sessions/s0/players');
      expect(find.text('حفظ الحضور'), findsNothing);
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets(
    'One failed admin section is not zero and does not hide authorized sessions; retry then logout is safe',
    (tester) async {
      var fail = true;
      var gets = 0;
      final auth = await authenticate('AcademyAdmin', (m, p, b, t) async {
        if (m == 'GET') gets++;
        if (p.endsWith('/my-permissions')) {
          return ['attendance.read', 'payment.read'];
        }
        if (p.contains('/payment-requests')) {
          if (fail) throw const ApiFailure(503, 'تعذر عرض الطلبات.');
          return {'pending': 4, 'items': []};
        }
        return [session(0)];
      });
      await tester.pumpWidget(shell(AdminHome(auth: auth)));
      await tester.pumpAndSettle();
      await reach(tester, find.text('تعذر عرض الطلبات.'));
      expect(find.text('0 طلب بانتظار المراجعة'), findsNothing);
      fail = false;
      await reach(tester, find.text('إعادة المحاولة'));
      await tester.tap(find.text('إعادة المحاولة'));
      await tester.pumpAndSettle();
      await reach(tester, find.text('4 طلب بانتظار المراجعة'));
      final before = gets;
      await auth.logout();
      await tester.pumpAndSettle();
      expect(find.text('4 طلب بانتظار المراجعة'), findsNothing);
      expect(gets, before);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'Owner backlog uses exact payment queue grain and all periods, preserving branch not chart dates',
    (tester) async {
      String? path;
      final auth = await owner((m, p, b, t) async {
        path = p;
        return {'pending': 9, 'items': []};
      });
      await tester.pumpWidget(
        shell(
          Scaffold(
            body: ListView(
              children: [
                OwnerDecisionsPanel(
                  auth: auth,
                  operations: operations(),
                  branchId: 'b1',
                ),
              ],
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('9 طلب معلق عبر كل الفترات'), findsOneWidget);
      expect(find.text('10 طلب معلق عبر كل الفترات'), findsNothing);
      await tester.tap(find.text('راجع طلبات الدفع'));
      await tester.pumpAndSettle();
      final q = Uri.parse(path!).queryParameters;
      expect(q, {'branchId': 'b1', 'allDates': 'true', 'status': 'Pending'});
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'Coach detail retains period and branch; group leads to its actual session, without writes',
    (tester) async {
      final calls = <String>[];
      final auth = await owner((m, p, b, t) async {
        expect(m, 'GET');
        calls.add(p);
        if (p.contains('/coach-performance/')) {
          return {
            'name': 'مدرب العرض',
            'from': '2026-09-01',
            'to': '2026-09-28',
            'sessionCount': 3,
            'publishedEvaluations': 2,
            'parentRating': null,
            'parentRatingCount': 0,
            'groups': [
              {
                'id': 'g1',
                'name': 'مجموعة المصدر',
                'branch': 'مدينة نصر',
                'sessions': 3,
              },
            ],
          };
        }
        if (p.endsWith('/g1/details')) {
          return {
            'group': {'arabicName': 'مجموعة المصدر', 'capacity': 10},
            'players': [],
            'coaches': [],
            'sessions': [
              {...session(0), 'id': 'real'},
            ],
          };
        }
        return {
          'canManageAttendance': false,
          'session': session(0),
          'players': [],
        };
      });
      await tester.pumpWidget(
        shell(
          OwnerCoachOperations(
            auth: auth,
            id: 'c1',
            filters: const {
              'from': '2026-09-01',
              'to': '2026-09-28',
              'branchId': 'b1',
            },
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(Uri.parse(calls.first).queryParameters, {
        'from': '2026-09-01',
        'to': '2026-09-28',
        'branchId': 'b1',
      });
      expect(find.text('لم يصل تقييم أسرة بعد.'), findsOneWidget);
      await reach(tester, find.textContaining('مجموعة المصدر ·'));
      await tester.tap(find.textContaining('مجموعة المصدر ·'));
      await tester.pumpAndSettle();
      expect(calls.last, '/api/v1/manage/groups/g1/details');
      await reach(tester, find.textContaining('2026-10-16 ·'));
      await tester.tap(find.textContaining('2026-10-16 ·'));
      await tester.pumpAndSettle();
      expect(calls.last, '/api/v1/attendance/sessions/real/players');
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets(
    'Resolving a source note refreshes the Owner Home on return without financial writes',
    (tester) async {
      var open = true;
      var reads = 0;
      Json? body;
      final auth = await homeOwner((m, p, b, t) async {
        if (p.contains('/structure/options')) return {'branches': []};
        if (p.endsWith('/news') || p.endsWith('/my-matches')) return [];
        if (m == 'POST') {
          expect(p, '/api/v1/manage/feedback/n1/resolve');
          body = b as Json;
          open = false;
          return {'id': 'n1'};
        }
        if (p.contains('/manage/feedback')) {
          return open
              ? [
                  {
                    'id': 'n1',
                    'kind': 'Complaint',
                    'status': 'Open',
                    'playerName': 'طفل تجريبي',
                    'branchName': 'فرع العرض',
                    'message': 'رسالة الأسرة التجريبية',
                    'ownerResponse': '',
                  },
                ]
              : [];
        }
        if (p.contains("/reports/owner-summary")) reads++;
        return {
          ...summary(),
          'from': '2026-09-01',
          'to': '2026-09-28',
          'operations': {
            ...operations(complaints: open ? 1 : 0),
            'pendingPaymentQueueBacklog': 0,
            'groupAttention': [],
            'coachPerformance': [],
          },
        };
      });
      await tester.pumpWidget(
        shell(OwnerDashboard(auth: auth, accountPage: const SizedBox())),
      );
      await tester.pumpAndSettle();
      await reach(tester, find.text('كل القرارات'));
      await tester.tap(find.text('كل القرارات'));
      await tester.pumpAndSettle();
      await reach(tester, find.text('شكاوى تحتاج متابعة'));
      await tester.tap(find.text('شكاوى تحتاج متابعة'));
      await tester.pumpAndSettle();
      await reach(tester, find.text('الرد والحل'));
      await tester.tap(find.text('الرد والحل'));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byType(TextField),
        'رد اصطناعي بعد مراجعة الموعد',
      );
      await tester.tap(find.text('إرسال الرد وحل الملاحظة'));
      await tester.pumpAndSettle();
      expect(body, {'response': 'رد اصطناعي بعد مراجعة الموعد'});
      await tester.tap(find.byType(BackButton).last);
      await tester.pumpAndSettle();
      expect(reads, 3);
      await reach(
        tester,
        find.text('لا توجد أولويات معلقة حسب القواعد الحالية.'),
      );
      expect(find.text('شكاوى تحتاج متابعة'), findsNothing);
      await tester.tap(find.byType(BackButton).last);
      await tester.pumpAndSettle();
      expect(reads, 4);
      await auth.logout();
      await tester.pumpAndSettle();
      expect(find.text('تحصيل اليوم'), findsNothing);
      expect(tester.takeException(), isNull);
    },
  );

  for (final width in [360.0, 390.0, 430.0]) {
    testWidgets(
      'Operational homes and decisions at $width, RTL/English, dark/light with 200% text',
      (tester) async {
        tester.view.physicalSize = Size(width, 1000);
        tester.view.devicePixelRatio = 1;
        addTearDown(tester.view.resetPhysicalSize);
        addTearDown(tester.view.resetDevicePixelRatio);
        final admin = await authenticate(
          'AcademyAdmin',
          (m, p, b, t) async => p.endsWith('/my-permissions')
              ? ['attendance.read', 'payment.read']
              : p.contains('/payment-requests')
              ? {'pending': 2, 'items': []}
              : [session(0)],
        );
        final own = await owner((m, p, b, t) async => []);
        for (final dark in [false, true]) {
          for (final locale in ['ar', 'en']) {
            for (final page in [
              AdminHome(auth: admin),
              Scaffold(
                body: ListView(
                  children: [
                    OwnerDecisionsPanel(auth: own, operations: operations()),
                  ],
                ),
              ),
            ]) {
              await tester.pumpWidget(const SizedBox());
              await tester.pumpWidget(
                MaterialApp(
                  locale: Locale(locale),
                  supportedLocales: const [Locale('ar'), Locale('en')],
                  localizationsDelegates: GlobalMaterialLocalizations.delegates,
                  theme: dark ? guardianTheme() : AcademyTheme.light(),
                  builder: (context, child) => MediaQuery(
                    data: MediaQuery.of(context)
                        .copyWith(textScaler: TextScaler.linear(2)),
                    child: child!,
                  ),
                  home: page,
                ),
              );
              await tester.pumpAndSettle();
              expect(tester.takeException(), isNull);
              await tester.drag(
                find.byType(ListView).first,
                const Offset(0, -650),
              );
              await tester.pumpAndSettle();
              expect(tester.takeException(), isNull);
            }
          }
        }
      },
    );
  }
}
