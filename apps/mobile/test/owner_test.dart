import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:academy_mobile/features/owner/owner_feedback.dart';
import 'package:academy_mobile/features/owner/owner_home_scope.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart';
import 'support/visibility.dart';

Map<String, dynamic> ownerCredentials() => {
  'accessToken': 'owner-access',
  'refreshToken': 'owner-refresh',
  'displayName': 'مالك الاختبار',
  'accessExpiresAtUtc': '2100-01-01T00:00:00Z',
  'membershipId': 'owner',
  'memberships': [
    {
      'id': 'owner',
      'academyId': 'academy',
      'academyName': 'أكاديمية العرض',
      'role': 'AcademyOwner',
    },
  ],
};
Future<AuthController> owner(Handler handler) async {
  final auth = AuthController(
    FakeApi(
      (m, p, b, t) => p.contains('/auth/')
          ? Future.value(ownerCredentials())
          : handler(m, p, b, t),
    ),
    MemoryStore(),
  )..restoring = false;
  await auth.verify('phone', 'challenge', 'otp');
  return auth;
}

Widget shell(Widget page) => MaterialApp(
  locale: const Locale('ar'),
  supportedLocales: const [Locale('ar')],
  localizationsDelegates: GlobalMaterialLocalizations.delegates,
  theme: AcademyTheme.light(),
  builder: (context, child) => MediaQuery(
    data: MediaQuery.of(context).copyWith(disableAnimations: true),
    child: child!,
  ),
  home: page,
);

// Explicit new read contracts used only by Home tests. Other module tests keep
// their strict source handlers and never inherit invented production defaults.
Future<AuthController> homeOwner(Handler handler) => owner((m, p, b, t) async {
  if (p.startsWith('/api/v1/subscriptions/home?')) {
    return {
      ...summary()['subscriptions'] as Map<String, dynamic>,
      'asOf': '2026-09-28',
      'players': 0,
      'periodRecords': 0,
      'academy': 'أكاديمية العرض',
      'dimensions': <String>[],
      'branches': <Object>[],
      'sports': <Object>[],
    };
  }
  if (p == '/api/v1/academy/calendar') {
    return {'today': '2026-09-28', 'trailingMonthFrom': '2026-08-28'};
  }
  if (p.contains('/reports/owner-cockpit')) {
    return {
      'currency': 'EGP',
      'collections': {'net': 5000, 'changePercentage': null},
      'target': {'amount': null, 'achievement': null},
      'overdue': {'amount': 0, 'families': 0},
      'risk': {'players': 0},
      'attendance': {'percentage': null},
      'capacity': {'seats': 0},
      'members': {'players': 0, 'groups': 0},
    };
  }
  if (p.contains('/reports/operating-statement')) {
    return {
      'currency': 'EGP',
      'surplus': 5000,
      'income': <Object>[],
      'expenses': <Object>[],
      'totalIncome': 5000,
      'totalExpenses': 0,
    };
  }
  if (p.contains('/reports/vip-members') ||
      p.contains('/reports/featured-players')) {
    return {'items': <Object>[]};
  }
  if (p.startsWith('/api/v1/manage/decision-center')) {
    return {
      'expiring': 0,
      'expired': 0,
      'absence': 0,
      'seats': 0,
      'unassigned': 0,
      'openTasks': 0,
    };
  }
  if (p == '/api/v1/notifications') {
    return {'items': <Object>[], 'unread': 0, 'total': 0};
  }
  return handler(m, p, b, t);
});

Map<String, dynamic> summary() => {
  'from': '2026-08-28',
  'to': '2026-09-28',
  'localToday': '2026-09-28',
  'collectionDay': '2026-09-28',
  'collectionMonthFrom': '2026-09-01',
  'asOfDate': '2026-09-28',
  'currency': 'EGP',
  'collections': {
    'today': 0,
    'month': 1200,
    'total': 5000,
    'todayCash': 0,
    'todayOnline': 0,
    'todayUnspecified': 0,
    'monthCash': 1200,
    'monthOnline': 0,
    'monthUnspecified': 0,
  },
  'paymentRequests': {
    'total': 2,
    'newEnrollments': 1,
    'renewals': 0,
    'products': 1,
    'tournaments': 0,
  },
  'payments': {'pending': 2, 'failed': 1},
  'subscriptions': {'total': 27, 'active': 20, 'expiring': 3, 'expired': 7},
  'operations': {
    'groupAttention': <Object>[],
    'coachPerformance': <Object>[],
    'pendingPaymentQueueBacklog': 2,
  },
  'activePlayers': 30,
  'attendance': {'present': 0},
  'byBranch': [
    {'name': 'مدينة نصر', 'amount': 3000},
  ],
  'latest': [
    {'player': 'آدم', 'receiptNumber': 'R-1', 'amount': 900, 'currency': 'EGP'},
  ],
};

void main() {
  testWidgets(
    'Historical payout method is explicit and retains its drill-down',
    (tester) async {
      String? reportPath;
      final auth = await homeOwner((m, p, b, t) async {
        if (p.contains('/structure/options')) return {'branches': []};
        if (p.endsWith('/news')) return [];
        if (p.contains('/reports/financial')) {
          reportPath = p;
          return {
            'totalCount': 1,
            'totalAmount': 0,
            'totalRefunds': 40.25,
            'netAmount': -40.25,
            'currency': 'EGP',
            'items': [
              {
                'player': 'لاعب اختبار',
                'receiptNumber': 'استرداد — R1',
                'branch': 'فرع اختبار',
                'amount': 0,
                'netAmount': -40.25,
                'currency': 'EGP',
                'collectionType': 'Refund',
              },
            ],
          };
        }
        return {
          ...summary(),
          'collections': {
            'today': -40.25,
            'todayCash': 0,
            'todayOnline': 0,
            'todayUnspecified': -40.25,
            'month': -40.25,
            'monthCash': 0,
            'monthOnline': 0,
            'monthUnspecified': -40.25,
          },
        };
      });
      await tester.pumpWidget(
        shell(OwnerDashboard(auth: auth, accountPage: const SizedBox())),
      );
      await tester.pumpAndSettle();
      final link = find.textContaining('طريقة صرف قديمة غير موثقة');
      await tester.scrollUntilVisible(
        link,
        250,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.ensureVisible(link);
      await tester.pumpAndSettle();
      expect(
        link,
        findsOneWidget,
      ); // A documented zero in the period is not shown as unknown.
      await tester.tap(link);
      await tester.pumpAndSettle();
      expect(Uri.parse(reportPath!).queryParameters['provider'], 'Unspecified');
      await tester.scrollUntilVisible(
        find.textContaining('استرداد اشتراك · بتاريخ الصرف'),
        200,
        scrollable: find.byType(Scrollable).first,
      );
      expect(
        find.textContaining('استرداد اشتراك · بتاريخ الصرف'),
        findsOneWidget,
      );
      expect(find.text('-40.25 ج.م'), findsWidgets);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox.shrink());
      auth.dispose();
    },
  );
  testWidgets('Pending payment action opens the whole actionable backlog', (
    tester,
  ) async {
    String? requestPath;
    final auth = await homeOwner((m, p, b, t) async {
      if (p.contains('/structure/options')) return {'branches': []};
      if (p.endsWith('/news')) return [];
      if (p.contains('/manage/product-sales') ||
          p.contains('/manage/payment-requests')) {
        requestPath = p;
        return {'totalCount': 0, 'items': []};
      }
      return {
        ...summary(),
        'from': '2026-09-01',
        'to': '2026-09-28',
        'paymentRequests': {
          'total': 3,
          'newEnrollments': 1,
          'renewals': 1,
          'products': 1,
        },
      };
    });
    await tester.pumpWidget(
      shell(OwnerDashboard(auth: auth, accountPage: const SizedBox())),
    );
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.text('مراجعة الطلبات'),
      250,
      scrollable: find.byType(Scrollable).first,
    );
    await revealTestTarget(tester, find.text('مراجعة الطلبات'));
    await tester.tap(find.text('مراجعة الطلبات'));
    await tester.pumpAndSettle();
    final scope = Uri.parse(requestPath!).queryParameters;
    expect(Uri.parse(requestPath!).path, '/api/v1/manage/payment-requests');
    expect(scope['from'], isNull);
    expect(scope['to'], isNull);
    expect(scope['allDates'], 'true');
    expect(scope['status'], 'Pending');
    // The approved home links to the combined queue. Preserve the product
    // route's exact scope contract as well, without relying on a removed tile.
    await tester.pumpWidget(
      shell(
        NativePaymentRequests(
          auth: auth,
          filters: {...scope, 'type': 'Product'},
        ),
      ),
    );
    await tester.pumpAndSettle();
    final query = Uri.parse(requestPath!).queryParameters;
    expect(Uri.parse(requestPath!).path, '/api/v1/manage/product-sales');
    expect(query['from'], scope['from']);
    expect(query['to'], scope['to']);
    expect(query['status'], 'Pending');
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox.shrink());
    auth.dispose();
  });

  testWidgets('Owner dashboard preserves real zeroes and has no fake trends', (
    tester,
  ) async {
    final auth = await homeOwner(
      (m, p, b, t) async => p.contains('/structure/options')
          ? {'branches': []}
          : p.endsWith('/news')
          ? []
          : {
              ...summary(),
              'collections': {
                'month': 0,
                'monthCash': 0,
                'monthOnline': 0,
                'monthUnspecified': 0,
              },
            },
    );
    await tester.pumpWidget(
      shell(OwnerDashboard(auth: auth, accountPage: const SizedBox())),
    );
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.text('صافي التحصيل خلال الفترة'),
      250,
      scrollable: find.byType(Scrollable).first,
    );
    expect(find.text('صافي التحصيل خلال الفترة'), findsOneWidget);
    final daily = find
        .ancestor(
          of: find.text('صافي التحصيل خلال الفترة'),
          matching: find.byType(GCard),
        )
        .first;
    expect(
      find.descendant(of: daily, matching: find.text('0 ج.م')),
      findsOneWidget,
    );
    await tester.scrollUntilVisible(
      find.text('لا تنتهي قريبًا'),
      -250,
      scrollable: find.byType(Scrollable).first,
    );
    expect(find.text('لا تنتهي قريبًا'), findsOneWidget);
    expect(find.text('أحدث التحصيلات'), findsNothing);
    for (final forbidden in [
      '% أعلى من الفترة السابقة',
      '% أقل من الفترة السابقة',
      'متوقع',
    ]) {
      expect(find.textContaining(forbidden), findsNothing);
    }
    expect(tester.takeException(), isNull);
  });
  testWidgets(
    'Owner date presets are independent and branch changes preserve them',
    (tester) async {
      tester.view.physicalSize = const Size(390, 2300);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      final calls = <Uri>[];
      final auth = await owner((m, p, b, t) async {
        final uri = Uri.parse(p);
        calls.add(uri);
        switch (uri.path) {
          case '/api/v1/academy/calendar':
            return {'today': '2026-11-05', 'trailingMonthFrom': '2026-10-05'};
          case '/api/v1/manage/structure/options':
            return {
              'branches': [
                {'id': 'madinaty', 'arabicName': 'فرع مدينتي'},
              ],
            };
          case '/api/v1/subscriptions/home':
            return {
              ...summary()['subscriptions'] as Map<String, dynamic>,
              'asOf': '2026-11-05',
              'academy': 'أكاديمية العرض',
              'dimensions': <String>[],
              'sports': <Object>[],
              'branches': <Object>[],
              'newSubscriptions': 5,
              'players': 20,
            };
          case '/api/v1/reports/owner-cockpit':
            return {
              'currency': 'EGP',
              'collections': {'net': 5000, 'changePercentage': 20},
              'target': {'amount': 9000, 'achievement': 56},
              'overdue': {'amount': 500, 'players': 2},
              'attendance': {'percentage': 80},
              'capacity': {'seats': 10},
              'members': {'groups': 5, 'players': 20},
            };
          case '/api/v1/reports/owner-summary':
            return summary();
          case '/api/v1/reports/operating-statement':
            return {
              'from': uri.queryParameters['from'],
              'to': uri.queryParameters['to'],
              'currency': 'EGP',
              'surplus': 500,
              'income': <Object>[],
              'expenses': [
                {
                  'label': 'الصيانة والتشغيل',
                  'amount': 100,
                  'sourceUrl': '/dashboard/finance/operating-expenses?category=Maintenance&status=Posted&paid=true&dateBasis=payment',
                },
              ],
              'totalIncome': 600,
              'totalExpenses': 100,
            };
          case '/api/v1/finance/operating-expenses':
            return {'count': 0, 'items': <Object>[]};
          case '/api/v1/news':
            return <Object>[];
          case '/api/v1/manage/decision-center':
            return <String, dynamic>{};
          default:
            return {'items': <Object>[]};
        }
      });
      await tester.pumpWidget(
        shell(OwnerDashboard(auth: auth, accountPage: const SizedBox())),
      );
      await tester.pumpAndSettle();
      final summaryDay = find.descendant(
        of: find.byKey(const ValueKey('owner-period-summary')),
        matching: find.text('هذا اليوم'),
      );
      await tester.tap(summaryDay);
      await tester.pumpAndSettle();
      final summaryControl = find.byKey(const ValueKey('owner-period-summary'));
      final custom = find.descendant(
        of: summaryControl,
        matching: find.text('الفترة المخصصة'),
      );
      await revealTestTarget(tester, custom);
      final reportReads = calls
          .where((uri) => uri.path == '/api/v1/reports/owner-cockpit')
          .length;
      await tester.tap(custom);
      await tester.pumpAndSettle();
      expect(find.byType(DateRangePickerDialog), findsOneWidget);
      Navigator.of(tester.element(find.byType(DateRangePickerDialog))).pop();
      await tester.pumpAndSettle();
      expect(
        tester.widget<OwnerPeriodControls>(summaryControl).period.preset,
        OwnerPeriodPreset.today,
      );
      expect(
        calls
            .where((uri) => uri.path == '/api/v1/reports/owner-cockpit')
            .length,
        reportReads,
      );
      await revealTestTarget(tester, custom);
      await tester.tap(custom);
      await tester.pumpAndSettle();
      await tester.tap(find.text('تطبيق'));
      await tester.pumpAndSettle();
      expect(
        tester.widget<OwnerPeriodControls>(summaryControl).period.preset,
        OwnerPeriodPreset.range,
      );
      expect(
        calls
            .lastWhere((uri) => uri.path == '/api/v1/reports/owner-cockpit')
            .queryParameters,
        {'from': '2026-10-05', 'to': '2026-11-05'},
      );
      await revealTestTarget(tester, summaryDay);
      await tester.tap(summaryDay);
      await tester.pumpAndSettle();
      final collectionMonth = find.descendant(
        of: find.byKey(const ValueKey('owner-period-collections')),
        matching: find.text('من أول الشهر'),
      );
      await revealTestTarget(tester, collectionMonth);
      await tester.tap(collectionMonth);
      await tester.pumpAndSettle();
      final dashboard = find.byType(Scrollable).first;
      tester.state<ScrollableState>(dashboard).position.jumpTo(0);
      await tester.pumpAndSettle();
      await revealTestTarget(tester, find.text('فرع مدينتي'));
      await tester.tap(find.text('فرع مدينتي'));
      await tester.pumpAndSettle();
      Uri latest(String path) => calls.lastWhere((uri) => uri.path == path);
      expect(latest('/api/v1/reports/owner-cockpit').queryParameters, {
        'from': '2026-11-05',
        'to': '2026-11-05',
        'branchId': 'madinaty',
      });
      expect(
        latest('/api/v1/reports/owner-summary').queryParameters['from'],
        '2026-11-01',
      );
      expect(
        latest('/api/v1/reports/owner-summary').queryParameters['branchId'],
        'madinaty',
      );
      expect(
        latest('/api/v1/subscriptions/home').queryParameters['from'],
        '2026-10-05',
      );
      expect(
        latest('/api/v1/subscriptions/home').queryParameters['branchId'],
        'madinaty',
      );
      final statementDay = find.descendant(
        of: find.byKey(const ValueKey('owner-period-statement')),
        matching: find.text('هذا اليوم'),
      );
      await revealTestTarget(tester, statementDay);
      await tester.tap(statementDay);
      await tester.pumpAndSettle();
      final expense = find.text('الصيانة والتشغيل');
      await revealTestTarget(tester, expense);
      await tester.tap(expense);
      await tester.pumpAndSettle();
      expect(latest('/api/v1/finance/operating-expenses').queryParameters, {
        'from': '2026-11-05',
        'to': '2026-11-05',
        'branchId': 'madinaty',
        'dateBasis': 'payment',
        'paid': 'true',
        'category': 'Maintenance',
        'status': 'Posted',
        'page': '1',
        'pageSize': '25',
      });
      await tester.tap(find.byType(BackButton));
      await tester.pumpAndSettle();
      expect(find.byType(OwnerDashboard), findsOneWidget);
      expect(
        latest('/api/v1/reports/owner-summary').queryParameters['from'],
        '2026-11-01',
      );
      expect(
        latest('/api/v1/reports/owner-cockpit').queryParameters['from'],
        '2026-11-05',
      );
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox());
      auth.dispose();
    },
  );

  testWidgets('Owner reviews private feedback from the feedback inbox', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(390, 844);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    Map<String, dynamic>? resolutionBody;
    final auth = await homeOwner((m, p, b, t) async {
      if (p.contains('/structure/options')) return {'branches': []};
      if (p.endsWith('/news')) return [];
      if (p.endsWith('/manage/feedback/note-1/resolve') && m == 'POST') {
        resolutionBody = Map<String, dynamic>.from(b as Map);
        return {'id': 'note-1'};
      }
      if (p.contains('/manage/feedback')) {
        return [
          {
            'id': 'note-1',
            'kind': 'Complaint',
            'score': null,
            'message': 'تغيير موعد تجريبي',
            'status': 'Open',
            'playerName': 'لاعب تجريبي',
            'branchName': 'فرع تجريبي',
            'coachName': 'مدرب تجريبي',
          },
        ];
      }
      return {
        ...summary(),
        'operations': {
          'groupAttention': [],
          'attentionRule': 'غياب متكرر',
          'coachPerformance': [
            {
              'name': 'مدرب تجريبي',
              'groupCount': 2,
              'sessionCount': 7,
              'parentRating': 4.5,
              'parentRatingCount': 2,
            },
          ],
          'pendingExpenses': [],
          'accessChanges': 1,
          'openComplaints': 1,
          'openSuggestions': 0,
        },
      };
    });
    await tester.pumpWidget(shell(OwnerFeedbackInbox(auth: auth)));
    await tester.pumpAndSettle();
    expect(find.text('تغيير موعد تجريبي'), findsOneWidget);
    expect(find.text('تسجيل المراجعة'), findsOneWidget);
    await tester.ensureVisible(find.text('الرد والحل'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('الرد والحل'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byType(TextField).last,
      'تم تعديل موعد الإشعار للأسرة.',
    );
    await tester.tap(find.text('إرسال الرد وحل الملاحظة'));
    await tester.pumpAndSettle();
    expect(resolutionBody?['response'], 'تم تعديل موعد الإشعار للأسرة.');
    expect(tester.takeException(), isNull);
  });
}
