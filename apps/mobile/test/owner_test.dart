import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';

import 'auth_controller_test.dart';

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
  home: page,
);

// Explicit new read contracts used only by Home tests. Other module tests keep
// their strict source handlers and never inherit invented production defaults.
Future<AuthController> homeOwner(Handler handler) => owner((m, p, b, t) async {
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
  'operations': {'groupAttention': <Object>[], 'coachPerformance': <Object>[]},
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
            'month': 1200,
            'monthUnspecified': 0.0,
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
      expect(
        find.textContaining('استرداد اشتراك · بتاريخ الصرف'),
        findsOneWidget,
      );
      expect(find.text('-40.25 ج.م'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox.shrink());
      auth.dispose();
    },
  );
  testWidgets('Payment category drill-down carries the displayed date range', (
    tester,
  ) async {
    String? requestPath;
    final auth = await homeOwner((m, p, b, t) async {
      if (p.contains('/structure/options')) return {'branches': []};
      if (p.endsWith('/news')) return [];
      if (p.contains('/manage/payment-requests')) {
        requestPath = p;
        return {'pending': 0, 'items': []};
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
      find.byKey(const ValueKey('owner-request-Product')),
      250,
      scrollable: find.byType(Scrollable).first,
    );
    await tester.tap(find.byKey(const ValueKey('owner-request-Product')));
    await tester.pumpAndSettle();
    final query = Uri.parse(requestPath!).queryParameters;
    expect(query['type'], 'Product');
    expect(query['from'], '2026-08-28');
    expect(query['to'], '2026-09-28');
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
          : summary(),
    );
    await tester.pumpWidget(
      shell(OwnerDashboard(auth: auth, accountPage: const SizedBox())),
    );
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.text('التحصيل اليومي'),
      250,
      scrollable: find.byType(Scrollable).first,
    );
    expect(find.text('التحصيل اليومي'), findsOneWidget);
    final daily = find
        .ancestor(of: find.text('التحصيل اليومي'), matching: find.byType(GCard))
        .first;
    expect(
      find.descendant(of: daily, matching: find.text('0 ج.م')),
      findsOneWidget,
    );
    await tester.scrollUntilVisible(
      find.text('فعالة'),
      250,
      scrollable: find.byType(Scrollable).first,
    );
    expect(find.text('فعالة'), findsOneWidget);
    expect(find.text('أحدث التحصيلات'), findsNothing);
    for (final forbidden in ['نمو', '%', 'متوقع']) {
      expect(find.textContaining(forbidden), findsNothing);
    }
    expect(tester.takeException(), isNull);
  });
  testWidgets(
    'Owner sees stored coach/family signals and opens private feedback',
    (tester) async {
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
      await tester.pumpWidget(
        shell(OwnerDashboard(auth: auth, accountPage: const SizedBox())),
      );
      await tester.pumpAndSettle();
      await tester.scrollUntilVisible(
        find.text('كل القرارات'),
        250,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.ensureVisible(
        find.widgetWithText(TextButton, 'كل القرارات'),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(TextButton, 'كل القرارات'));
      await tester.pumpAndSettle();
      final feedbackLink = find.text('شكاوى تحتاج متابعة');
      await tester.scrollUntilVisible(
        feedbackLink,
        250,
        scrollable: find.byType(Scrollable).first,
      );
      await tester.ensureVisible(feedbackLink);
      await tester.pumpAndSettle();
      await tester.tap(feedbackLink);
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
    },
  );
}
