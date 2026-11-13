import '../test/introduction_test_helpers.dart';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/shared/role_intro_gate.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:academy_mobile/features/shared/academy_reveal.dart';
import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

import 'v45_single_renewal_saved_receipt_test.dart'
    show SavedRenewalReadTransport;

// Separate QA introduction keys; no clearing or changes to ordinary preferences.
class JourneyAppearance extends Appearance {
  final String run;
  JourneyAppearance(this.run) : super(storage: const FlutterSecureStorage());
  @override
  String roleIntroKey(String role, String sportTheme) =>
      'qa.$run.${super.roleIntroKey(role, sportTheme)}';
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  const selectedRole = String.fromEnvironment('INTRO_ROLE');
  const selectedAppearance = String.fromEnvironment('INTRO_APPEARANCE');
  const loginFlow = bool.fromEnvironment('ENTRY_LOGIN_FLOW');
  const welcomeOnly = bool.fromEnvironment('ENTRY_WELCOME_ONLY');
  const loginAppearance = String.fromEnvironment(
    'ENTRY_LOGIN_APPEARANCE',
    defaultValue: 'light',
  );
  testWidgets(
    'approved entry role=${selectedRole.isEmpty ? 'all' : selectedRole} appearance=${selectedAppearance.isEmpty ? 'both' : selectedAppearance} login=$loginFlow persists and replays without changing normal flags',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      const width = int.fromEnvironment(
        'INTRO_ACCEPTANCE_WIDTH',
        defaultValue: 440,
      );
      expect([375, 440], contains(width));
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, width);
      const run = String.fromEnvironment('INTRO_RUN');
      expect(run, isNotEmpty);
      const roles = [
        (
          'Guardian',
          '01099900100',
          'تقييمات المدرب',
          'تجديد الاشتراك',
          'guardian',
        ),
        ('Coach', '01099900020', 'جدول مجموعاتك', 'حضور اللاعبين', 'coach'),
        (
          'AcademyAdmin',
          '01099900011',
          'التسجيل والتجديد',
          'اشتراكات اللاعبين',
          'admin',
        ),
        (
          'AcademyOwner',
          '01099900010',
          'الاشتراكات والتحصيلات',
          'إدارة الأكاديمية',
          'owner',
        ),
      ];
      Future<void> wait(Finder finder, {bool settle = true}) async {
        for (var i = 0; i < 240 && finder.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(finder, findsWidgets);
        if (settle) {
          await t.pumpAndSettle();
        } else {
          // The real owner home intentionally animates continuously.
          await t.pump(const Duration(milliseconds: 400));
        }
      }

      Future<void> loadedScreenshot(String name) async {
        // A frame can be idle while an asynchronous image codec is still busy.
        // Never accept a white photo/crest placeholder as a loaded screenshot.
        for (var i = 0; i < 200; i++) {
          final images = t.widgetList<RawImage>(find.byType(RawImage)).toList();
          if (images.isNotEmpty && images.every((im) => im.image != null)) {
            await t.pump(const Duration(milliseconds: 100));
            await binding.takeScreenshot(name);
            return;
          }
          await t.pump(const Duration(milliseconds: 100));
        }
        fail('Visible introduction images did not finish decoding.');
      }

      if (loginFlow) {
        expect(['light', 'dark'], contains(loginAppearance));
        final loginTransport = SavedRenewalReadTransport(
          AppConfig('Demo', base),
        );
        final loginAuth = AuthController(
          loginTransport,
          SecureRefreshStore('approved.entry.login.$run.$width'),
        )..restoring = false;
        final loginPrefs = JourneyAppearance('$run.login.$width')
          ..dark = loginAppearance == 'dark';
        final normalKey =
            'academy.role-intro.${Appearance.roleIntroVersion}.Guardian.general';
        final before = await const FlutterSecureStorage().read(key: normalKey);
        await t.pumpWidget(
          AcademyApp(
            auth: loginAuth,
            appearance: loginPrefs,
            environment: 'Demo',
          ),
        );
        await wait(find.byKey(const ValueKey('login-submit')));
        await loadedScreenshot(
          'phase-approved-entry-phone-$width-$loginAppearance',
        );
        await t.enterText(find.byType(TextFormField), '01099900100');
        await t.pumpAndSettle();
        await t.ensureVisible(find.byKey(const ValueKey('login-submit')));
        await t.pumpAndSettle();
        await t.tap(find.byKey(const ValueKey('login-submit')));
        await wait(find.byKey(const ValueKey('login-six-code-boxes')));
        await t.enterText(find.byType(TextFormField), '246810');
        await t.pumpAndSettle();
        await loadedScreenshot(
          'phase-approved-entry-code-$width-$loginAppearance',
        );
        await t.ensureVisible(find.byKey(const ValueKey('login-submit')));
        await t.pumpAndSettle();
        await t.tap(find.byKey(const ValueKey('login-submit')));
        await wait(find.text('تقييمات المدرب'));
        expect(loginAuth.session!.selected!.role, 'Guardian');
        expect(
          loginAuth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        await finishRoleIntroduction(t);
        await wait(find.byType(AcademyReveal), settle: false);
        await t.pump(const Duration(milliseconds: 2050));
        await wait(find.byType(NativeRoleTabs), settle: false);
        expect(await loginPrefs.hasRoleIntro('Guardian', 'general'), true);
        expect(await const FlutterSecureStorage().read(key: normalKey), before);
        debugPrint(
          'entry: real six-digit Demo phone/OTP form reached trusted Guardian home; normal flag unchanged',
        );
        await t.pumpWidget(const SizedBox());
        loginAuth.dispose();
        loginPrefs.dispose();
        loginTransport.close();
      }

      expect(['', ...roles.map((r) => r.$1)], contains(selectedRole));
      // Role/appearance filters only batch captures on constrained Macs; all
      // selected roles keep the same flow, persistence and isolation assertions.
      for (final role in roles.where(
        (r) => selectedRole.isEmpty || r.$1 == selectedRole,
      )) {
        final transport = SavedRenewalReadTransport(AppConfig('Demo', base));
        final auth = AuthController(
          transport,
          SecureRefreshStore('v73.5278.approved-intro.${role.$1}'),
        );
        await auth.restore();
        if (auth.session == null) {
          final challenge = await auth.requestOtp(role.$2);
          await auth.verify(role.$2, challenge, '246810');
        }
        debugPrint('intro ${role.$1}: authenticated trusted membership');
        expect(auth.session!.selected!.role, role.$1);
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        final experience = await auth.request(
          'GET',
          '/api/v1/academy/experience',
        ) as Map<String, dynamic>;
        final metadata = experience['onboarding'] as Map<String, dynamic>;
        final sportTheme = metadata['sportTheme'] as String;
        expect(
          sportTheme,
          'general',
          reason: 'The acceptance academy has multiple active sports.',
        );
        expect(metadata['features'] as List, contains('training'));
        final normalKey =
            'academy.role-intro.${Appearance.roleIntroVersion}.${role.$1}.$sportTheme';
        final normalBefore = await const FlutterSecureStorage().read(
          key: normalKey,
        );
        expect(['', 'light', 'dark'], contains(selectedAppearance));
        for (final dark in [false, true].where(
          (dark) =>
              selectedAppearance.isEmpty ||
              selectedAppearance == (dark ? 'dark' : 'light'),
        )) {
          final mode = dark ? 'dark' : 'light',
              namespace = '$run.$width.${dark ? 'dark' : 'light'}';
          final prefs = JourneyAppearance(namespace)..dark = dark;
          expect(
            await prefs.hasRoleIntro(role.$1, sportTheme),
            false,
            reason: 'Use a fresh isolated INTRO_RUN instead of resetting user keys.',
          );
          await t.pumpWidget(
            AcademyApp(auth: auth, appearance: prefs, environment: 'Demo'),
          );
          await wait(find.text(role.$3));
          if (!welcomeOnly) {
            for (var step = 0; step < 3; step++) {
              await loadedScreenshot(
                'phase-approved-entry-intro-${role.$5}-$width-$mode-${step + 1}',
              );
              expect(t.takeException(), isNull);
              if (step < 2) {
                await t.tap(find.text('التالي'));
                await t.pumpAndSettle();
              }
            }
            debugPrint('intro ${role.$1} $mode: three pages captured');
            expect(find.text(role.$4), findsOneWidget);
            await t.tap(
              find.text(role.$1 == 'Coach' && dark ? 'تخطي' : 'ابدأ'),
            );
          } else {
            await finishRoleIntroduction(t);
          }
          await wait(find.byType(AcademyReveal), settle: false);
          await t.pump(const Duration(milliseconds: 450));
          await binding.takeScreenshot(
            'phase-approved-entry-reveal-${role.$5}-$width-$mode',
          );
          await t.pump(const Duration(milliseconds: 2050));
          await wait(find.byType(NativeRoleTabs), settle: false);
          expect(await prefs.hasRoleIntro(role.$1, sportTheme), true);
          await t.pumpWidget(const SizedBox());
          prefs.dispose();
          final reopened = JourneyAppearance(namespace)..dark = dark;
          await t.pumpWidget(
            AcademyApp(auth: auth, appearance: reopened, environment: 'Demo'),
          );
          await wait(find.byType(NativeRoleTabs), settle: false);
          expect(find.byType(AcademyOnboarding), findsNothing);
          await t.tap(find.byTooltip('حسابي'));
          await t.pumpAndSettle();
          await t.scrollUntilVisible(
            find.text('ابدأ من هنا'),
            180,
            scrollable: find.byType(Scrollable).last,
            maxScrolls: 30,
          );
          await t.tap(find.text('ابدأ من هنا'));
          await wait(find.text(role.$3));
          expect(find.byType(RoleIntroReplay), findsOneWidget);
          await finishRoleIntroduction(t);
          await t.pumpAndSettle();
          expect(find.byType(RoleIntroReplay), findsNothing);
          expect(await reopened.hasRoleIntro(role.$1, sportTheme), true);
          expect(
            await const FlutterSecureStorage().read(key: normalKey),
            normalBefore,
          );
          expect(t.takeException(), isNull);
          debugPrint(
            'intro ${role.$1} $mode: persisted, reopened and replayed; normal flag unchanged',
          );
          await t.pumpWidget(const SizedBox());
          reopened.dispose();
        }
        auth.dispose();
        transport.close();
      }
    },
    timeout: const Timeout(Duration(minutes: 6)),
  );
}
