import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
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

Map<String, dynamic> summary() => {
  'asOfDate': '2026-09-28',
  'currency': 'EGP',
  'collections': {'today': 0, 'month': 1200, 'total': 5000},
  'payments': {'pending': 2, 'failed': 1},
  'subscriptions': {'active': 20, 'expiring': 3, 'expired': 7},
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
      final auth = await owner((m, p, b, t) async {
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
      await tester.scrollUntilVisible(link, 250);
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
    final auth = await owner((m, p, b, t) async {
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
    await tester.ensureVisible(
      find.byKey(const ValueKey('owner-request-Product')),
    );
    await tester.tap(find.byKey(const ValueKey('owner-request-Product')));
    await tester.pumpAndSettle();
    final query = Uri.parse(requestPath!).queryParameters;
    expect(query['type'], 'Product');
    expect(query['from'], '2026-09-01');
    expect(query['to'], '2026-09-28');
    expect(query['status'], 'Pending');
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox.shrink());
    auth.dispose();
  });

  testWidgets('Owner dashboard preserves real zeroes and has no fake trends', (
    tester,
  ) async {
    final auth = await owner(
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
    expect(find.text('تحصيل اليوم'), findsOneWidget);
    expect(find.text('0 ج.م'), findsOneWidget);
    await tester.scrollUntilVisible(find.text('الاشتراكات الفعالة'), 250);
    expect(find.text('الاشتراكات الفعالة'), findsOneWidget);
    expect(find.text('أحدث التحصيلات'), findsNothing);
    for (final forbidden in ['نمو', '%', 'متوقع']) {
      expect(find.textContaining(forbidden), findsNothing);
    }
    expect(tester.takeException(), isNull);
  });
}
