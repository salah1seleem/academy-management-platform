import 'dart:typed_data';

import 'package:academy_mobile/core/api/api_client.dart';
import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/core/storage/refresh_store.dart';
import 'package:academy_mobile/core/theme/app_theme.dart';
import 'package:academy_mobile/features/coach/coach_home.dart';
import 'package:academy_mobile/features/guardian/guardian_content.dart';
import 'package:academy_mobile/features/guardian/guardian_ui.dart';
import 'package:academy_mobile/features/shared/private_media.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:video_player/video_player.dart';

class OsMediaTransport extends HttpApiTransport {
  OsMediaTransport(super.config);
  final uploaded = <Json>[];
  @override
  Future<Object?> upload(
    Uint8List bytes,
    String contentType,
    Map<String, String> fields,
    String token,
  ) async {
    final result = await super.upload(bytes, contentType, fields, token);
    final asset = obj(result);
    uploaded.add(asset);
    debugPrint(
      'Actual OS media upload: ${asset['id']} / $contentType / ${bytes.length} bytes / ${fields['caption']}',
    );
    return result;
  }
}

void main() {
  final binding = IntegrationTestWidgetsFlutterBinding.ensureInitialized();
  testWidgets(
    'coach publishes synthetic image and video through actual iOS picker, guardian plays protected video and unrelated family is denied',
    (t) async {
      expect(const bool.fromEnvironment('ISOLATED_DEMO'), isTrue);
      const base = String.fromEnvironment('API_BASE_URL');
      expect(base, 'http://127.0.0.1:5278');
      expect((t.view.physicalSize / t.view.devicePixelRatio).width, 375);
      Future<AuthController> login(
        String phone, {
        OsMediaTransport? transport,
      }) async {
        final auth = AuthController(
          transport ?? HttpApiTransport(AppConfig('Demo', base)),
          SecureRefreshStore('v45.5278.os-media.$phone'),
        );
        await auth.restore();
        if (auth.session == null) {
          final c = await auth.requestOtp(phone);
          await auth.verify(phone, c, '246810');
        }
        expect(
          auth.session!.selected!.academyId,
          '30000000-0000-0000-0000-000000000003',
        );
        return auth;
      }

      final transport = OsMediaTransport(AppConfig('Demo', base));
      final owner = await login('01099900010'),
          coach = await login('01099900020', transport: transport),
          family = await login('01099900100'),
          foreign = await login('01099900104');
      expect(coach.session!.selected!.role, 'Coach');
      final money = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      final ownIds = rows(
        obj(await family.request('GET', '/api/v1/guardian/home'))['children'],
      ).map((c) => c['id']).toSet();
      final foreignIds = rows(
        obj(await foreign.request('GET', '/api/v1/guardian/home'))['children'],
      ).map((c) => c['id']).toSet();
      final groups = rows(await coach.request('GET', '/api/v1/coach/groups'));
      Json? group;
      String? child;
      for (final g in groups) {
        final audience = obj(
          await coach.request(
            'GET',
            '/api/v1/assets/media-audience?groupId=${g['id']}',
          ),
        );
        final ids = rows(audience['players']).map((p) => p['id']).toSet();
        if (ids.intersection(ownIds).isNotEmpty &&
            ids.intersection(foreignIds).isEmpty) {
          group = g;
          child = ids.intersection(ownIds).first as String;
          break;
        }
      }
      expect(group, isNotNull);
      expect(child, isNotNull);
      Future<void> waitFor(Finder f) async {
        for (var i = 0; i < 300 && f.evaluate().isEmpty; i++) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(f, findsWidgets);
        await t.pumpAndSettle();
      }

      Future<void> show(Widget page, String key) async {
        await t.pumpWidget(
          MaterialApp(
            key: ValueKey(key),
            debugShowCheckedModeBanner: false,
            locale: const Locale('ar'),
            supportedLocales: const [Locale('ar')],
            localizationsDelegates: GlobalMaterialLocalizations.delegates,
            theme: AcademyTheme.dark(),
            home: page,
          ),
        );
        await t.pump(const Duration(milliseconds: 350));
      }

      Future<void> reach(Finder f) async {
        final scroll = find.byType(Scrollable).first;
        t.state<ScrollableState>(scroll).position.jumpTo(0);
        await t.pumpAndSettle();
        await t.scrollUntilVisible(f, 160, scrollable: scroll, maxScrolls: 100);
        await t.ensureVisible(f.last);
        await t.pumpAndSettle();
      }

      final assets = <Json>[];
      for (final kind in ['photo', 'video']) {
        final caption = kind == 'photo'
            ? 'صورة قبول اصطناعية من ملفات iOS V45'
            : 'فيديو قبول اصطناعي من ملفات iOS V45';
        final prior = rows(
          await coach.request(
            'GET',
            '/api/v1/assets/gallery?groupId=${group!['id']}',
          ),
        ).where((a) => a['caption'] == caption).toList();
        expect(prior.length, lessThanOrEqualTo(1));
        if (prior.isNotEmpty) {
          assets.add(prior.single);
          debugPrint('Resume existing actual OS upload: ${prior.single['id']}');
          continue;
        }
        await show(CoachGroup(auth: coach, group: group), 'coach-$kind');
        await waitFor(find.textContaining('لاعب في المجموعة'));
        final open = find.text('رفع صور وفيديوهات للحصة');
        await reach(open);
        await t.tap(open);
        await t.pumpAndSettle();
        await waitFor(find.byType(UploadSessionMedia));
        await waitFor(find.text('لا يوجد طفل يمكن التعرف عليه في هذه اللقطة'));
        final none = find.text('لا يوجد طفل يمكن التعرف عليه في هذه اللقطة');
        await reach(none);
        await t.tap(none);
        await t.pumpAndSettle();
        final captionField = find.widgetWithText(
          TextField,
          'وصف مختصر (اختياري)',
        );
        await reach(captionField);
        await t.enterText(captionField, caption);
        await t.pumpAndSettle();
        final privacy = find.text(
          'راجعت الملف؛ لا يظهر طفل آخر خارج القائمة المحددة. هذا ليس تصريح تسويق أو إعلان.',
        );
        await reach(privacy);
        await t.tap(privacy);
        await t.pumpAndSettle();
        FocusManager.instance.primaryFocus?.unfocus();
        await t.pumpAndSettle();
        final upload = find.text('رفع صورة أو فيديو');
        await reach(upload);
        await t.tap(upload);
        await t.pump(const Duration(milliseconds: 350));
        debugPrint(
          'NATIVE_MEDIA_PICKER_WAIT: $kind — select I-Academy-${kind == 'photo' ? 'Photo-Test.png' : 'Video-Test.mp4'} from On My iPhone',
        );
        final previous = transport.uploaded.length;
        for (
          var i = 0;
          i < 1800 && transport.uploaded.length == previous;
          i++
        ) {
          await t.pump(const Duration(milliseconds: 150));
        }
        expect(
          transport.uploaded.length,
          previous + 1,
          reason: 'Complete the actual OS file picker; an API error must not count as upload success.',
        );
        await waitFor(find.text('لا يوجد طفل يمكن التعرف عليه في هذه اللقطة'));
        await reach(find.textContaining('تم النشر للمخولين'));
        final created = transport.uploaded.last;
        final gallery = rows(
          await coach.request(
            'GET',
            '/api/v1/assets/gallery?groupId=${group['id']}',
          ),
        );
        final saved = gallery.singleWhere((a) => a['id'] == created['id']);
        expect(saved['caption'], caption);
        expect(
          saved['contentType'],
          kind == 'photo' ? 'image/png' : 'video/mp4',
        );
        assets.add(saved);
        await reach(find.textContaining('تم النشر للمخولين'));
        await binding.takeScreenshot('phase-approved-os-$kind-published-w375');
      }
      final ownGallery = rows(
        await family.request('GET', '/api/v1/assets/gallery?playerId=$child'),
      );
      for (final a in assets) {
        expect(ownGallery.any((m) => m['id'] == a['id']), isTrue);
        expect((await family.asset(textOf(a['url']))).isNotEmpty, isTrue);
        try {
          await foreign.asset(textOf(a['url']));
          fail('Unrelated family must not fetch group media bytes');
        } on ApiFailure catch (e) {
          expect(e.status, 404);
        }
      }
      final foreignGallery = rows(
        await foreign.request(
          'GET',
          '/api/v1/assets/gallery?playerId=${foreignIds.first}',
        ),
      );
      expect(
        foreignGallery.any((m) => assets.any((a) => a['id'] == m['id'])),
        isFalse,
      );
      await show(
        GalleryPage(auth: family, playerId: child!),
        'guardian-gallery',
      );
      await waitFor(find.byType(Scrollable));
      await reach(find.text(textOf(assets.first['caption'])));
      await binding.takeScreenshot(
        'phase-approved-os-photo-guardian-gallery-w375',
      );
      final video = assets.singleWhere((a) => a['contentType'] == 'video/mp4');
      final tile = find.byWidgetPredicate(
        (w) => w is MediaTile && w.media['id'] == video['id'],
      );
      await reach(tile);
      final play = find
          .descendant(of: tile, matching: find.byType(InkWell))
          .first;
      await t.tap(play);
      await t.pump(const Duration(milliseconds: 350));
      await waitFor(find.byType(VideoPlayer));
      final controller = t
          .widget<VideoPlayer>(find.byType(VideoPlayer))
          .controller;
      expect(controller.value.isInitialized, isTrue);
      expect(controller.value.duration, greaterThan(Duration.zero));
      await t.tap(find.byIcon(Icons.play_circle));
      await t.pump();
      for (
        var i = 0;
        i < 30 && controller.value.position == Duration.zero;
        i++
      ) {
        await t.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 100)),
        );
        await t.pump();
      }
      expect(controller.value.position, greaterThan(Duration.zero));
      expect(controller.value.isPlaying, isTrue);
      await binding.takeScreenshot(
        'phase-approved-os-video-playing-guardian-w375',
      );
      await t.tap(find.byIcon(Icons.pause_circle));
      await t.pump();
      expect(controller.value.isPlaying, isFalse);
      final after = obj(
        await owner.request(
          'GET',
          '/api/v1/reports/financial?allDates=true&pageSize=1',
        ),
      );
      expect(after['netAmount'], money['netAmount']);
      expect(after['totalCount'], money['totalCount']);
      expect(t.takeException(), isNull);
      await t.pumpWidget(const SizedBox());
      for (final a in [owner, coach, family, foreign]) {
        await a.logout();
        a.dispose();
      }
    },
  );
}
