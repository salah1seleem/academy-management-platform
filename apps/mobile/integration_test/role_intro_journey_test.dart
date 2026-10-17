import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

// Separate tutorial keys on the dedicated QA simulator, never clear user storage.
class JourneyAppearance extends Appearance {
  final String run;
  JourneyAppearance(this.run) : super(storage: const FlutterSecureStorage());
  @override
  String roleIntroKey(String role, String sportTheme) =>
      'qa.$run.${super.roleIntroKey(role, sportTheme)}';
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'four authenticated roles finish or skip, persist through remount and replay their own introduction',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5223',
      );
      final transport = HttpApiTransport(
        AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
      );
      final auth = AuthController(
        transport,
        SecureRefreshStore('academy.v3.role-intro.journey'),
      );
      await auth.restore();
      final namespace = 'role-intro-${DateTime.now().microsecondsSinceEpoch}';
      const roles = [
        (
          'Guardian',
          '01099900102',
          'مواعيد ابنك قدامك',
          'خدماتك بسهولة',
          'guardian',
        ),
        ('Coach', '01099900022', 'حصتك الجاية جاهزة', 'تقدّم يتشاف', 'coach'),
        (
          'AcademyAdmin',
          '01099900011',
          'ابدأ بحصص الآن',
          'طلباتك في مكان واحد',
          'admin',
        ),
        (
          'AcademyOwner',
          '01099900010',
          'فلوسك واضحة',
          'قرارات من بياناتك',
          'owner',
        ),
      ];
      Future<void> wait(Finder f) async {
        for (var i = 0; i < 180 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      for (final role in roles) {
        if (auth.session != null) await auth.logout();
        Future<T> authCall<T>(Future<T> Function() call) async {
          for (var attempt = 0; ; attempt++) {
            try {
              return await call();
            } on ApiFailure catch (e) {
              if (e.status != 429 || attempt >= 2) rethrow;
              await Future<void>.delayed(const Duration(seconds: 31));
            }
          }
        }

        final challenge = await authCall(() => auth.requestOtp(role.$2));
        await authCall(() => auth.verify(role.$2, challenge, '246810'));
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        expect(auth.session!.selected!.role, role.$1);
        final prefs = JourneyAppearance(namespace);
        await t.pumpWidget(
          AcademyApp(auth: auth, appearance: prefs, environment: 'Demo'),
        );
        await wait(find.text(role.$3));
        expect(find.text('تخطي'), findsOneWidget);
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-intro-${role.$5}');
        }
        if (role.$1 == 'Coach') {
          await t.tap(find.text('تخطي'));
        } else {
          await t.tap(find.text('التالي'));
          await t.pumpAndSettle();
          await t.tap(find.text('التالي'));
          await t.pumpAndSettle();
          expect(find.text(role.$4), findsOneWidget);
          await t.tap(find.text('ابدأ الآن'));
        }
        await wait(find.byType(NavigationBar));
        expect(await prefs.hasRoleIntro(role.$1, 'football'), isTrue);
        await t.pumpWidget(const SizedBox());
        final reopened = JourneyAppearance(namespace);
        await t.pumpWidget(
          AcademyApp(auth: auth, appearance: reopened, environment: 'Demo'),
        );
        await wait(find.byType(NavigationBar));
        expect(find.byType(AcademyOnboarding), findsNothing);
        await t.tap(find.text('المزيد'));
        await t.pumpAndSettle();
        await t.tap(find.text('ابدأ من هنا'));
        await wait(find.text(role.$3));
        await t.tap(find.text('تخطي'));
        await t.pumpAndSettle();
        expect(find.byType(AcademyOnboarding), findsNothing);
        await t.pumpWidget(const SizedBox());
        prefs.dispose();
        reopened.dispose();
      }
      await auth.logout();
      auth.dispose();
      transport.close();
    },
    timeout: const Timeout(Duration(minutes: 3)),
  );
}
