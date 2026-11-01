import 'dart:async';

import 'package:academy_mobile/core/auth/auth_controller.dart';
import 'package:academy_mobile/features/shared/page_video.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:video_player_platform_interface/video_player_platform_interface.dart';

import 'auth_controller_test.dart' as f;
import 'v2_role_workspaces_test.dart' as shell;

// This platform boundary never requests a network stream or supplies a real video.
class LessonVideoPlatform extends VideoPlayerPlatform {
  int created = 0;
  final played = <int>[], disposed = <int>[];
  final streams = <int, StreamController<VideoEvent>>{};
  @override
  Future<void> init() async {}
  @override
  Future<int?> createWithOptions(VideoCreationOptions options) async {
    final id = ++created;
    final stream = StreamController<VideoEvent>();
    streams[id] = stream;
    stream.add(
      VideoEvent(
        eventType: VideoEventType.initialized,
        duration: const Duration(seconds: 20),
        size: const Size(640, 360),
      ),
    );
    return id;
  }

  @override
  Stream<VideoEvent> videoEventsFor(int playerId) => streams[playerId]!.stream;
  @override
  Future<void> dispose(int playerId) async {
    disposed.add(playerId);
    await streams[playerId]!.close();
  }

  @override
  Future<void> play(int playerId) async {
    played.add(playerId);
  }

  @override
  Future<void> pause(int playerId) async {}
  @override
  Future<void> setLooping(int playerId, bool looping) async {}
  @override
  Future<void> setVolume(int playerId, double volume) async {}
  @override
  Future<void> setPlaybackSpeed(int playerId, double speed) async {}
  @override
  Future<void> seekTo(int playerId, Duration position) async {}
  @override
  Future<Duration> getPosition(int playerId) async => Duration.zero;
  @override
  Widget buildViewWithOptions(VideoViewOptions options) =>
      SizedBox(key: ValueKey('lesson-video-${options.playerId}'));
}

Map<String, dynamic> lesson({
  String role = 'AcademyOwner',
  String screen = 'reports',
  bool available = true,
  String url = 'https://video.example.test/reports.mp4',
}) => {
  'available': available,
  'screen': screen,
  'role': role,
  'title': 'شرح التقارير المصرح به',
  'url': available ? url : null,
};
Future<AuthController> login(
  f.Handler handler, {
  int Function()? revision,
}) async {
  final auth = AuthController(
    f.FakeApi((m, p, b, t) async {
      if (p.contains('/auth/')) {
        return {
          ...f.credentials(),
          'membershipId': 'owner',
          'memberships': [
            {
              'id': 'owner',
              'academyId': 'academy',
              'academyName': 'أكاديمية اختبار',
              'role': 'AcademyOwner',
              'contextRevision': revision?.call() ?? 0,
            },
          ],
        };
      }
      return handler(m, p, b, t);
    }),
    f.MemoryStore(),
  )..restoring = false;
  await auth.verify('phone', 'challenge', 'code');
  return auth;
}

void main() {
  late LessonVideoPlatform platform;
  late VideoPlayerPlatform previous;
  setUp(() {
    previous = VideoPlayerPlatform.instance;
    platform = LessonVideoPlatform();
    VideoPlayerPlatform.instance = platform;
  });
  tearDown(() {
    VideoPlayerPlatform.instance = previous;
  });
  Future<void> show(WidgetTester t, AuthController auth) async {
    await t.pumpWidget(
      shell.app(
        PageVideoPlayer(
          auth: auth,
          screen: 'reports',
          data: {...lesson(), 'title': 'عنوان لقطة قديمة لا يجب عرضه'},
        ),
      ),
    );
    await t.pumpAndSettle();
  }

  Future<void> disposed(WidgetTester t, int id) async {
    // Platform stream cancellation can finish outside the widget fake-async zone.
    for (var i = 0; i < 5 && !platform.disposed.contains(id); i++) {
      await t.runAsync(() => Future<void>.delayed(Duration.zero));
      await t.pump();
    }
    expect(platform.disposed, contains(id));
  }

  Future<void> finish(WidgetTester t, AuthController auth) async {
    await t.pumpWidget(const SizedBox());
    await t.pumpAndSettle();
    for (final id in platform.streams.keys) {
      await disposed(t, id);
    }
    auth.dispose();
  }

  testWidgets(
    'disabled configured page explains absence without initializing media',
    (t) async {
      final auth = await login((m, p, b, k) async => lesson(available: false));
      await t.pumpWidget(
        shell.app(
          Scaffold(
            body: PageVideoButton(
              auth: auth,
              screen: 'reports',
              forceVisible: true,
            ),
          ),
        ),
      );
      await t.tap(find.byTooltip('شرح الصفحة بالفيديو'));
      await t.pumpAndSettle();
      expect(
        find.text('لم تضف الأكاديمية فيديو لهذه الصفحة بعد.'),
        findsOneWidget,
      );
      expect(platform.created, 0);
      await finish(t, auth);
    },
  );
  for (final mismatch in ['role', 'screen']) {
    testWidgets(
      'button rejects mismatched $mismatch instead of opening another lesson',
      (t) async {
        final auth = await login(
          (m, p, b, k) async => lesson(
            role: mismatch == 'role' ? 'Coach' : 'AcademyOwner',
            screen: mismatch == 'screen' ? 'players' : 'reports',
          ),
        );
        await t.pumpWidget(
          shell.app(
            Scaffold(
              body: PageVideoButton(
                auth: auth,
                screen: 'reports',
                forceVisible: true,
              ),
            ),
          ),
        );
        await t.tap(find.byTooltip('شرح الصفحة بالفيديو'));
        await t.pumpAndSettle();
        expect(find.text('تعذر فتح شرح الصفحة الآن.'), findsOneWidget);
        expect(find.byType(PageVideoPlayer), findsNothing);
        expect(platform.created, 0);
        await finish(t, auth);
      },
    );
  }
  testWidgets(
    'late button response cannot cross revision change and return to same context',
    (t) async {
      var revision = 0;
      final pending = Completer<Object?>();
      final auth = await login(
        (m, p, b, k) => pending.future,
        revision: () => revision,
      );
      await t.pumpWidget(
        shell.app(
          Scaffold(
            body: PageVideoButton(
              auth: auth,
              screen: 'reports',
              forceVisible: true,
            ),
          ),
        ),
      );
      await t.tap(find.byTooltip('شرح الصفحة بالفيديو'));
      await t.pump();
      revision = 1;
      await auth.refresh();
      revision = 0;
      await auth.refresh();
      pending.complete(lesson());
      await t.pumpAndSettle();
      expect(find.byType(PageVideoPlayer), findsNothing);
      expect(platform.created, 0);
      expect(find.text('تعذر فتح شرح الصفحة الآن.'), findsNothing);
      await finish(t, auth);
    },
  );
  testWidgets('player uses freshly authorized title and never auto plays', (
    t,
  ) async {
    var reads = 0;
    final auth = await login((m, p, b, k) async {
      reads++;
      return lesson();
    });
    await show(t, auth);
    expect(find.text('شرح التقارير المصرح به'), findsOneWidget);
    expect(find.text('عنوان لقطة قديمة لا يجب عرضه'), findsNothing);
    expect(platform.created, 1);
    expect(platform.played, isEmpty);
    expect(reads, 1);
    await finish(t, auth);
    await disposed(t, 1);
    expect(platform.disposed, [1]);
  });
  for (final mismatch in ['role', 'screen', 'url', 'withdrawn']) {
    testWidgets(
      'player rejects $mismatch before creating media or revealing a title',
      (t) async {
        final auth = await login(
          (m, p, b, k) async => lesson(
            role: mismatch == 'role' ? 'Coach' : 'AcademyOwner',
            screen: mismatch == 'screen' ? 'players' : 'reports',
            available: mismatch != 'withdrawn',
            url: mismatch == 'url'
                ? 'https://video.example.test/other.mp4'
                : 'https://video.example.test/reports.mp4',
          ),
        );
        await show(t, auth);
        expect(
          find.text('الفيديو غير متاح الآن. راجع إدارة الأكاديمية.'),
          findsOneWidget,
        );
        expect(find.text('شرح التقارير المصرح به'), findsNothing);
        expect(platform.created, 0);
        await finish(t, auth);
      },
    );
  }
  testWidgets(
    'withdrawal before play disposes cached media without playing it',
    (t) async {
      var available = true, reads = 0;
      final auth = await login((m, p, b, k) async {
        reads++;
        return lesson(available: available);
      });
      await show(t, auth);
      available = false;
      await t.tap(find.byTooltip('تشغيل أو إيقاف'));
      await t.pumpAndSettle();
      expect(reads, 2);
      expect(platform.played, isEmpty);
      await disposed(t, 1);
      expect(platform.disposed, [1]);
      expect(find.text('شرح التقارير المصرح به'), findsNothing);
      expect(
        find.text('الفيديو غير متاح الآن. راجع إدارة الأكاديمية.'),
        findsOneWidget,
      );
      await finish(t, auth);
    },
  );
  testWidgets(
    'each play reauthorizes while pausing does not request another lesson',
    (t) async {
      var reads = 0;
      final auth = await login((m, p, b, k) async {
        reads++;
        return lesson();
      });
      await show(t, auth);
      await t.tap(find.byTooltip('تشغيل أو إيقاف'));
      await t.pumpAndSettle();
      expect(reads, 2);
      expect(platform.played, [1]);
      await t.tap(find.byTooltip('تشغيل أو إيقاف'));
      await t.pumpAndSettle();
      expect(reads, 2);
      await t.tap(find.byTooltip('تشغيل أو إيقاف'));
      await t.pumpAndSettle();
      expect(reads, 3);
      expect(platform.played, [1, 1]);
      await finish(t, auth);
    },
  );
  testWidgets(
    'leaving foreground clears frame and title; resume rechecks withdrawn lesson',
    (t) async {
      var available = true, reads = 0;
      final auth = await login((m, p, b, k) async {
        reads++;
        return lesson(available: available);
      });
      await show(t, auth);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      await t.pump();
      expect(find.text('شرح التقارير المصرح به'), findsNothing);
      expect(find.byKey(const ValueKey('lesson-video-1')), findsNothing);
      await disposed(t, 1);
      expect(platform.disposed, [1]);
      available = false;
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      await t.pumpAndSettle();
      expect(reads, 2);
      expect(platform.created, 1);
      expect(platform.played, isEmpty);
      expect(
        find.text('الفيديو غير متاح الآن. راجع إدارة الأكاديمية.'),
        findsOneWidget,
      );
      await finish(t, auth);
    },
  );
  testWidgets(
    'logout then same-member login cannot resurrect player or title',
    (t) async {
      final auth = await login((m, p, b, k) async => lesson());
      await show(t, auth);
      await auth.logout();
      await t.pumpAndSettle();
      await auth.verify('phone', 'challenge', 'code');
      await t.pumpAndSettle();
      expect(find.text('شرح التقارير المصرح به'), findsNothing);
      expect(find.byTooltip('تشغيل أو إيقاف'), findsNothing);
      await disposed(t, 1);
      expect(platform.disposed, [1]);
      await finish(t, auth);
    },
  );
  testWidgets(
    'a play check finishing after background cannot start abandoned controller',
    (t) async {
      var reads = 0;
      final pending = Completer<Object?>();
      final auth = await login((m, p, b, k) async {
        reads++;
        return reads == 1 ? lesson() : pending.future;
      });
      await show(t, auth);
      await t.tap(find.byTooltip('تشغيل أو إيقاف'));
      await t.pump();
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.inactive);
      await t.pump();
      pending.complete(lesson());
      await t.pump();
      expect(platform.played, isEmpty);
      await disposed(t, 1);
      expect(platform.disposed, [1]);
      await finish(t, auth);
      t.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
    },
  );
  testWidgets(
    'revision revocation disposes a loaded player and returning revision cannot revive it',
    (t) async {
      var revision = 0;
      final auth = await login(
        (m, p, b, k) async => lesson(),
        revision: () => revision,
      );
      await show(t, auth);
      revision = 1;
      await auth.refresh();
      await t.pumpAndSettle();
      await disposed(t, 1);
      revision = 0;
      await auth.refresh();
      await t.pumpAndSettle();
      expect(find.text('شرح التقارير المصرح به'), findsNothing);
      expect(find.byTooltip('تشغيل أو إيقاف'), findsNothing);
      expect(platform.created, 1);
      expect(platform.played, isEmpty);
      await finish(t, auth);
    },
  );
}
