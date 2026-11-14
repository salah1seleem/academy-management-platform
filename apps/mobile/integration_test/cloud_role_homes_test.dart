import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/admin/admin_home.dart';
import 'package:academy_mobile/features/coach/coach_home.dart';
import 'package:academy_mobile/features/guardian/guardian_home.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:academy_mobile/features/owner/owner_quick_operations.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import '../test/introduction_test_helpers.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  const roles = [
    (
      'owner',
      'AcademyOwner',
      '01099900010',
      OwnerDashboard,
      'التقارير',
      'اشتراكات الأكاديمية',
    ),
    (
      'admin',
      'AcademyAdmin',
      '01099900011',
      AdminHome,
      'العمليات',
      'يوم الأكاديمية',
    ),
    ('coach', 'Coach', '01099900020', CoachHome, 'المجموعات', 'مجموعات مسندة'),
    (
      'guardian',
      'Guardian',
      '01099900100',
      GuardianHome,
      'التقييمات',
      'أبنائي',
    ),
  ];

  for (final (label, role, phone, home, tab, content) in roles) {
    testWidgets('$role opens its own home with the isolated Demo API', (
      tester,
    ) async {
      expect(const bool.fromEnvironment('ACADEMY_ISOLATED_IOS_QA'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5397');
      final transport = HttpApiTransport(AppConfig('Demo', base));
      addTearDown(transport.close);
      final auth = AuthController(
        transport,
        SecureRefreshStore('academy.ios-cloud.$label'),
      );
      await auth.restore();
      expect(auth.session, isNull);
      final challenge = await auth.requestOtp(phone);
      await auth.verify(phone, challenge, '246810');
      expect(auth.session?.selected?.role, role);
      expect(
        auth.session?.selected?.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      const academy = String.fromEnvironment('ACADEMY_IOS_QA_NAME');
      if (academy == 'Z Professional Academy') {
        expect(auth.session?.selected?.academyName, academy);
      }

      final appearance = Appearance(storage: const FlutterSecureStorage());
      await appearance.load();
      tester.platformDispatcher.accessibilityFeaturesTestValue =
          const FakeAccessibilityFeatures(disableAnimations: true);
      addTearDown(
        tester.platformDispatcher.clearAccessibilityFeaturesTestValue,
      );
      await tester.pumpWidget(
        AcademyApp(auth: auth, environment: 'Demo', appearance: appearance),
      );
      final nav = find.byType(NativeRoleTabs);
      final intro = find.byType(AcademyOnboarding);
      for (
        var tick = 0;
        tick < 120 && nav.evaluate().isEmpty && intro.evaluate().isEmpty;
        tick++
      ) {
        await tester.pump(const Duration(milliseconds: 150));
      }
      if (intro.evaluate().isNotEmpty) await finishRoleIntroduction(tester);
      final loaded = find.text(content);
      final retry = find.widgetWithText(OutlinedButton, 'إعادة المحاولة');
      var retried = false;
      final deadline = Stopwatch()..start();
      while (loaded.evaluate().isEmpty && deadline.elapsed.inSeconds < 60) {
        if (role == 'AcademyAdmin' && !retried && retry.evaluate().isNotEmpty) {
          await binding.takeScreenshot('phase-cloud-admin-initial-error');
          await tester.tap(retry.first);
          retried = true;
        }
        await tester.pump(const Duration(milliseconds: 150));
      }
      expect(nav, findsOneWidget);
      expect(tester.widget<NativeRoleTabs>(nav).role, role);
      expect(find.byType(home), findsOneWidget);
      expect(find.text(tab), findsWidgets);
      if (loaded.evaluate().isEmpty) {
        await binding.takeScreenshot('phase-cloud-$label-incomplete');
        if (role == 'AcademyAdmin') {
          final readStarted = Stopwatch()..start();
          try {
            final permissions = await auth.request(
              'GET',
              '/api/v1/my-permissions',
            );
            debugPrint(
              'Admin permission read: ${readStarted.elapsedMilliseconds} ms, list=${permissions is List}.',
            );
          } on ApiFailure catch (error) {
            debugPrint(
              'Admin permission read: ${readStarted.elapsedMilliseconds} ms, status=${error.status}.',
            );
          }
        }
      }
      expect(loaded, findsWidgets);
      if (academy == 'Z Professional Academy') {
        expect(find.text(academy).hitTestable(), findsWidgets);
      }
      if (role == 'AcademyAdmin') {
        final subscriptionHero = find.text('اشتراكات نطاقك');
        final operations = find.byType(NativeOperationActions);
        final pendingOperations = find.descendant(
          of: operations,
          matching: find.byType(CircularProgressIndicator),
        );
        final deadline = Stopwatch()..start();
        while ((subscriptionHero.hitTestable().evaluate().isEmpty ||
                pendingOperations.evaluate().isNotEmpty) &&
            deadline.elapsed.inSeconds < 60) {
          await tester.pump(const Duration(milliseconds: 150));
        }
        if (subscriptionHero.hitTestable().evaluate().isEmpty ||
            pendingOperations.evaluate().isNotEmpty) {
          await binding.takeScreenshot('phase-cloud-admin-incomplete');
        }
        expect(subscriptionHero.hitTestable(), findsOneWidget);
        expect(operations, findsOneWidget);
        expect(pendingOperations, findsNothing);
        expect(find.text('إعادة تحميل الإجراءات'), findsNothing);
      }
      if (role == 'Guardian') {
        final carousel = find.byType(GuardianChildCarousel);
        final profile = find.descendant(
          of: carousel,
          matching: find.byType(FilledButton),
        );
        final actions = find.text('العمليات السريعة');
        var laidOut = false;
        final deadline = Stopwatch()..start();
        while (!laidOut && deadline.elapsed.inSeconds < 15) {
          await tester.pump(const Duration(milliseconds: 150));
          laidOut =
              profile.evaluate().length == 1 &&
              tester.getBottomRight(profile).dy <=
                  tester.getBottomRight(carousel).dy &&
              (actions.evaluate().isEmpty ||
                  tester.getBottomRight(profile).dy <
                      tester.getTopLeft(actions).dy);
        }
        if (!laidOut) {
          await binding.takeScreenshot('phase-cloud-guardian-incomplete');
        }
        expect(laidOut, isTrue, reason: 'The child action must fit its card.');
      }
      await binding.takeScreenshot('phase-cloud-$label');
      if (role == 'AcademyAdmin') {
        final permissions = await auth.request('GET', '/api/v1/my-permissions');
        expect(permissions, contains('subscription.read'));
        final summary = await auth.request('GET', '/api/v1/subscriptions/home');
        expect(summary, isA<Map<String, dynamic>>());
        expect((summary as Map<String, dynamic>)['total'], isA<num>());
      }
      if (role == 'Coach') {
        final openGroup = find
            .descendant(
              of: find.byType(CoachGroupCard),
              matching: find.text('فتح المجموعة'),
            )
            .first;
        await tester.scrollUntilVisible(
          openGroup,
          160,
          scrollable: find
              .descendant(
                of: find.byType(CoachHome),
                matching: find.byType(Scrollable),
              )
              .first,
        );
        await tester.tap(openGroup);
        final groupCount = find.textContaining('في المجموعة');
        final groupDeadline = Stopwatch()..start();
        while (groupCount.evaluate().isEmpty &&
            groupDeadline.elapsed.inSeconds < 60) {
          await tester.pump(const Duration(milliseconds: 150));
        }
        expect(find.byType(CoachGroup), findsOneWidget);
        expect(groupCount, findsOneWidget);
        final roster = find.text('لاعبو المجموعة');
        final attendance = find.text('حضور حصة اليوم');
        expect(
          roster.evaluate().isNotEmpty || attendance.evaluate().isNotEmpty,
          isTrue,
        );
        await tester.tap(find.byType(BackButton).last);
        await tester.pump(const Duration(milliseconds: 400));
        expect(find.byType(CoachHome), findsOneWidget);
      }
      expect(tester.takeException(), isNull);

      await tester.pumpWidget(const SizedBox.shrink());
      await auth.logout();
      expect(auth.session, isNull);
      auth.dispose();
      appearance.dispose();
    });
  }
}
