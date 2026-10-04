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
  testWidgets('Owner dashboard preserves real zeroes and has no fake trends', (
    tester,
  ) async {
    final auth = await owner((m, p, b, t) async => summary());
    await tester.pumpWidget(
      shell(OwnerDashboard(auth: auth, accountPage: const SizedBox())),
    );
    await tester.pumpAndSettle();
    expect(find.text('تحصيل اليوم'), findsOneWidget);
    expect(find.text('0 ج.م'), findsOneWidget);
    expect(find.text('الاشتراكات الفعالة'), findsOneWidget);
    await tester.scrollUntilVisible(find.text('أحدث التحصيلات'), 300);
    expect(find.text('أحدث التحصيلات'), findsOneWidget);
    for (final forbidden in ['نمو', '%', 'متوقع']) {
      expect(find.textContaining(forbidden), findsNothing);
    }
    expect(tester.takeException(), isNull);
  });
}
