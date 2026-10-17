import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/help_center.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:video_player/video_player.dart';

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'Real Guardian OTP opens published private video, plays it, rechecks on resume and preserves written guidance',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      expect(const String.fromEnvironment('APP_ENV'), 'Demo');
      expect(
        const String.fromEnvironment('API_BASE_URL'),
        'http://127.0.0.1:5232',
      );
      final transport = HttpApiTransport(
        AppConfig('Demo', const String.fromEnvironment('API_BASE_URL')),
      );
      final auth = AuthController(
        transport,
        SecureRefreshStore('academity.v3.help.video.guardian'),
      );
      await auth.restore();
      if (auth.session != null) await auth.logout();
      try {
        final challenge = await auth.requestOtp('01099900101');
        await auth.verify('01099900101', challenge, '246810');
        expect(auth.session!.selected!.role, 'Guardian');
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        final detail = obj(
          await auth.request('GET', '/api/v1/help/lessons/family-renewal'),
        );
        expect(detail['videoStatus'], 'Published');
        expect(obj(detail['video'])['durationSeconds'], 2);
        Future<void> wait(Finder finder) async {
          for (var i = 0; i < 200 && finder.evaluate().isEmpty; i++) {
            await t.pump(const Duration(milliseconds: 100));
          }
          expect(finder, findsWidgets);
        }

        await t.pumpWidget(
          MaterialApp(
            locale: const Locale('ar'),
            supportedLocales: const [Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            theme: guardianTheme(),
            home: HelpPage(auth: auth, id: 'family-renewal'),
          ),
        );
        await wait(find.text('شاهد الشرح بالفيديو'));
        await t.ensureVisible(find.text('شاهد الشرح بالفيديو'));
        await t.tap(find.text('شاهد الشرح بالفيديو'));
        await wait(find.byType(VideoPlayer));
        final player = t
            .widget<VideoPlayer>(find.byType(VideoPlayer))
            .controller;
        expect(player.value.isInitialized, true);
        expect(player.value.duration.inSeconds, 2);
        expect(player.value.isPlaying, false);
        await t.tap(find.byTooltip('تشغيل أو إيقاف الفيديو'));
        for (
          var i = 0;
          i < 50 && player.value.position.inMilliseconds < 100;
          i++
        ) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(player.value.position.inMilliseconds, greaterThan(0));
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-help-video-playing-ios');
        }
        binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
        await t.pump();
        expect(find.byType(VideoPlayer), findsNothing);
        binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
        await wait(find.byType(VideoPlayer));
        final resumed = t
            .widget<VideoPlayer>(find.byType(VideoPlayer))
            .controller;
        expect(resumed.value.isInitialized, true);
        expect(resumed.value.isPlaying, false);
        // Session withdrawal prevents replay of previously downloaded media.
        await auth.logout();
        await t.pump();
        expect(find.byType(VideoPlayer), findsNothing);
        expect(find.textContaining('الفيديو غير متاح'), findsOneWidget);
        if (const bool.fromEnvironment('CAPTURE_VISUALS')) {
          await binding.takeScreenshot('phase-help-video-session-revoked-ios');
        }
        expect(t.takeException(), isNull);
      } finally {
        await t.pumpWidget(const SizedBox.shrink());
        if (auth.session != null) await auth.logout();
        auth.dispose();
        transport.close();
      }
    },
  );
}
