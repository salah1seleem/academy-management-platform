import 'dart:ui' show FrameTiming;

import '../test/introduction_test_helpers.dart';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/auth/login_page.dart';
import 'package:academy_mobile/features/shared/academy_reveal.dart';
import 'package:academy_mobile/features/shared/role_navigation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

/// First-use rehearsal never clears ordinary device tokens/preferences.
class StartupJourneyStore implements RefreshStore {
  String? token;
  @override
  Future<String?> read() async => token;
  @override
  Future<void> write(String value) async {
    token = value;
  }

  @override
  Future<void> clear() async {
    token = null;
  }
}

/// Actual demo HTTP reads/authentication; business writes prohibited.
class StartupReadTransport extends HttpApiTransport {
  StartupReadTransport(super.config);
  final reads = <String>[];
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    if (method != 'GET' && !path.startsWith('/api/v1/mobile/auth/')) {
      throw StateError('Startup rehearsal cannot write business records');
    }
    if (method == 'GET') reads.add(path);
    return super.send(
      method,
      path,
      body: body,
      token: token,
      idempotencyKey: idempotencyKey,
    );
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'real demo OTP to onboarding, reveal, four Homes and restored session',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      final frames = <FrameTiming>[];
      void timings(List<FrameTiming> batch) {
        frames.addAll(batch.take((4000 - frames.length).clamp(0, 4000)));
      }

      binding.addTimingsCallback(timings);
      addTearDown(() => binding.removeTimingsCallback(timings));
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final api = StartupReadTransport(AppConfig('Demo', base));
      final store = StartupJourneyStore();
      final auth = AuthController(api, store);
      final prefs = Appearance();
      await t.pumpWidget(
        AcademyApp(auth: auth, environment: 'Demo', appearance: prefs),
      );
      debugPrint('startup: restoring empty test store');
      await auth.restore();
      debugPrint('startup: restore finished');
      Future<void> tick(int milliseconds) async {
        for (var elapsed = 0; elapsed < milliseconds; elapsed += 50) {
          await t.pump(const Duration(milliseconds: 50));
        }
      }

      Future<void> wait(Finder target) async {
        for (var i = 0; i < 240 && target.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(target, findsWidgets);
      }

      await wait(find.byType(LoginPage));
      debugPrint('startup: login visible');
      await tick(500);
      debugPrint('startup: capturing login');
      await binding.takeScreenshot('phase-startup-platform-login');
      debugPrint('startup: login captured');
      await t.enterText(find.byType(TextFormField), '01099900100');
      await t.ensureVisible(find.byKey(const ValueKey('login-submit')));
      await t.tap(find.byKey(const ValueKey('login-submit')));
      await wait(find.byKey(const ValueKey('login-six-code-boxes')));
      await tick(400);
      await binding.takeScreenshot('phase-startup-platform-otp');
      await t.enterText(find.byType(TextFormField), '246810');
      await t.ensureVisible(find.byKey(const ValueKey('login-submit')));
      await t.tap(find.byKey(const ValueKey('login-submit')));
      await wait(find.byType(AcademyOnboarding));
      expect(auth.session?.selected?.role, 'Guardian');
      FocusManager.instance.primaryFocus?.unfocus();
      await tick(400);
      await binding.takeScreenshot('phase-startup-guardian-onboarding');
      await finishRoleIntroduction(t);
      await wait(find.byKey(const ValueKey('academy-reveal')));
      await tick(650);
      expect(api.reads, contains('/api/v1/guardian/home'));
      await binding.takeScreenshot('phase-startup-academy-reveal');
      await tick(1600);
      expect(find.byKey(const ValueKey('academy-reveal')), findsNothing);
      await wait(find.text('أبنائي'));
      await tick(2000);
      await binding.takeScreenshot('phase-startup-guardian-home');
      debugPrint('startup: guardian loaded');
      await t.pumpWidget(const SizedBox());
      // New application tree/controller reading the same remembered token.
      // Process-level cold launch is recorded separately using the normal main.
      final returning = AuthController(api, store);
      await t.pumpWidget(
        AcademyApp(auth: returning, environment: 'Demo', appearance: prefs),
      );
      await returning.restore();
      await wait(find.byType(AcademyReveal));
      expect(find.byType(AcademyOnboarding), findsNothing);
      expect(find.byType(LoginPage), findsNothing);
      await tick(650);
      await binding.takeScreenshot('phase-startup-returning-reveal');
      await tick(3000);
      expect(find.byKey(const ValueKey('academy-reveal')), findsNothing);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await t.pump(const Duration(milliseconds: 200));
      expect(find.byKey(const ValueKey('academy-reveal')), findsNothing);
      await t.pumpWidget(const SizedBox());
      returning.dispose();
      auth.dispose();
      for (final item in [
        ('AcademyOwner', '01099900010', 'owner'),
        ('AcademyAdmin', '01099900011', 'admin'),
        ('Coach', '01099900020', 'coach'),
      ]) {
        final roleAuth = AuthController(api, StartupJourneyStore());
        final challenge = await roleAuth.requestOtp(item.$2);
        await roleAuth.verify(item.$2, challenge, '246810');
        roleAuth.restoring = false;
        expect(roleAuth.session?.selected?.role, item.$1);
        await t.pumpWidget(
          AcademyApp(auth: roleAuth, environment: 'Demo', appearance: prefs),
        );
        await wait(find.byType(AcademyOnboarding));
        await finishRoleIntroduction(t);
        await wait(find.byType(NativeRoleTabs));
        await tick(5000);
        expect(find.byKey(const ValueKey('academy-reveal')), findsNothing);
        await binding.takeScreenshot('phase-startup-${item.$3}-home');
        debugPrint('startup: ${item.$3} loaded');
        expect(t.takeException(), isNull);
        await t.pumpWidget(const SizedBox());
        roleAuth.dispose();
      }
      if (frames.isNotEmpty) {
        double percentile(List<int> samples, double p) {
          samples.sort();
          return (samples[((samples.length - 1) * p).round()] / 10).round() /
              100;
        }

        binding.reportData ??= <String, dynamic>{};
        binding.reportData!['frameMetrics'] = {
          'environment':
              'iOS Simulator debug; observed render timings, not device FPS',
          'sampledFrames': frames.length,
          'buildP95Ms': percentile(
            frames.map((f) => f.buildDuration.inMicroseconds).toList(),
            .95,
          ),
          'rasterP95Ms': percentile(
            frames.map((f) => f.rasterDuration.inMicroseconds).toList(),
            .95,
          ),
          'totalP95Ms': percentile(
            frames.map((f) => f.totalSpan.inMicroseconds).toList(),
            .95,
          ),
          'maxTotalMs': percentile(
            frames.map((f) => f.totalSpan.inMicroseconds).toList(),
            1,
          ),
          'reportedRefreshRate':
              binding.platformDispatcher.views.first.display.refreshRate,
        };
      }
      prefs.dispose();
      expect(t.takeException(), isNull);
    },
  );
}
