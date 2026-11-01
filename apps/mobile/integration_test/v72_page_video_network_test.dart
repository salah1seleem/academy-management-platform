import '../test/introduction_test_helpers.dart';

import 'package:academy_mobile/app/app.dart';
import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/appearance.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/owner/owner_dashboard.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:video_player/video_player.dart';

const sample =
    'https://flutter.github.io/assets-for-api-docs/assets/videos/butterfly.mp4';
const originalUrl = 'https://video.example.test/owner-reports.mp4';
const originalTitle = 'شرح اصطناعي لاختبار حفظ الإعداد';
const testTitle = 'مقطع تقني محايد لاختبار التشغيل — ليس شرح الأكاديمية';

class PageVideoDemoTransport extends HttpApiTransport {
  PageVideoDemoTransport(super.config);
  @override
  Future<Object?> send(
    String method,
    String path, {
    Object? body,
    String? token,
    String? idempotencyKey,
  }) {
    if (method != 'GET' && !path.startsWith('/api/v1/mobile/auth/')) {
      final data = body is Map ? body : <String, Object?>{};
      final sampleSetting = data['url'] == sample && data['title'] == testTitle;
      final originalSetting =
          data['url'] == originalUrl &&
          data['title'] == originalTitle &&
          data['isActive'] == false;
      if (method != 'PUT' ||
          path != '/api/v1/manage/page-help' ||
          data['role'] != 'AcademyOwner' ||
          data['screen'] != 'reports' ||
          !(sampleSetting || originalSetting)) {
        throw StateError(
          'Network-video acceptance cannot mutate other business data.',
        );
      }
    }
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
    'real iOS network player plays and pauses a neutral sample then rejects withdrawn configuration and restores demo',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), true);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      final auth = AuthController(
        PageVideoDemoTransport(AppConfig('Demo', base)),
        SecureRefreshStore('v47.5278.decision-assignment.owner10'),
      );
      await auth.restore();
      expect(auth.session?.selected?.role, 'AcademyOwner');
      expect(
        auth.session?.selected?.academyId,
        '30000000-0000-0000-0000-000000000003',
      );
      Future<Json> configured() async =>
          rows(
            obj(await auth.request('GET', '/api/v1/manage/page-help'))['items'],
          ).singleWhere(
            (v) => v['role'] == 'AcademyOwner' && v['screen'] == 'reports',
          );
      final before = await configured();
      expect(before['title'], originalTitle);
      expect(before['url'], originalUrl);
      expect(before['isActive'], false);
      final original = <String, Object?>{
        'role': before['role'],
        'screen': before['screen'],
        'title': before['title'],
        'url': before['url'],
        'isActive': before['isActive'],
      };
      Future<void> save(Map<String, Object?> value) async {
        await auth.request('PUT', '/api/v1/manage/page-help', body: value);
        final after = await configured();
        for (final key in value.keys) {
          expect(after[key], value[key], reason: 'Saved $key must read back');
        }
      }

      Future<void> wait(Finder f) async {
        for (var i = 0; i < 320 && f.evaluate().isEmpty; i++) {
          if (find.text('تخطي').evaluate().isNotEmpty) {
            await finishRoleIntroduction(t);
          }
          if (find
              .text('الفيديو غير متاح الآن. راجع إدارة الأكاديمية.')
              .evaluate()
              .isNotEmpty) {
            fail(
              'Actual network player failed to initialize the HTTPS sample.',
            );
          }
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      try {
        await save({
          ...original,
          'title': testTitle,
          'url': sample,
          'isActive': true,
        });
        final effect = obj(
          await auth.request('GET', '/api/v1/page-help/reports'),
        );
        expect(effect['available'], true);
        expect(effect['url'], sample);
        await t.pumpWidget(
          AcademyApp(auth: auth, environment: 'Demo', appearance: Appearance()),
        );
        await wait(find.byType(OwnerDashboard));
        await t.tap(
          find.descendant(
            of: find.byType(NavigationBar),
            matching: find.text('التقارير'),
          ),
        );
        await t.pumpAndSettle();
        await wait(find.byTooltip('شرح الصفحة بالفيديو'));
        await t.tap(find.byTooltip('شرح الصفحة بالفيديو'));
        await t.pump();
        await wait(find.byType(VideoPlayer));
        final controller = t
            .widget<VideoPlayer>(find.byType(VideoPlayer))
            .controller;
        expect(controller.dataSource, sample);
        expect(controller.httpHeaders, isEmpty);
        expect(controller.value.isInitialized, true);
        expect(controller.value.duration, greaterThan(Duration.zero));
        expect(controller.value.isPlaying, false);
        expect(find.text(testTitle), findsOneWidget);
        await t.tap(find.byTooltip('تشغيل أو إيقاف'));
        await t.pump();
        for (
          var i = 0;
          i < 100 &&
              controller.value.position < const Duration(milliseconds: 350);
          i++
        ) {
          await t.pump(const Duration(milliseconds: 100));
        }
        expect(controller.value.isPlaying, true);
        expect(
          controller.value.position,
          greaterThan(const Duration(milliseconds: 300)),
        );
        await binding.takeScreenshot(
          'phase-approved-page-video-network-playing',
        );
        await t.tap(find.byTooltip('تشغيل أو إيقاف'));
        await t.pumpAndSettle();
        expect(controller.value.isPlaying, false);
        final stopped = controller.value.position;
        await t.pump(const Duration(milliseconds: 500));
        expect(controller.value.position, stopped);
        await binding.takeScreenshot(
          'phase-approved-page-video-network-paused',
        );
        await save({
          ...original,
          'title': testTitle,
          'url': sample,
          'isActive': false,
        });
        await t.tap(find.byTooltip('تشغيل أو إيقاف'));
        await t.pumpAndSettle();
        expect(find.byType(VideoPlayer), findsNothing);
        expect(find.text(testTitle), findsNothing);
        expect(
          find.text('الفيديو غير متاح الآن. راجع إدارة الأكاديمية.'),
          findsOneWidget,
        );
        await binding.takeScreenshot(
          'phase-approved-page-video-network-withdrawn',
        );
        expect(t.takeException(), isNull);
      } finally {
        await t.pumpWidget(const SizedBox());
        final current = await configured();
        expect([sample, originalUrl], contains(current['url']));
        expect([testTitle, originalTitle], contains(current['title']));
        await save(original);
        final restored = obj(
          await auth.request('GET', '/api/v1/page-help/reports'),
        );
        expect(restored['available'], false);
        expect(restored['url'], null);
        auth.dispose();
      }
    },
  );
}
