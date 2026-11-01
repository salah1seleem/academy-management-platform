import 'dart:async';

import 'package:flutter/material.dart';
import 'package:video_player/video_player.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

class PageVideoButton extends StatefulWidget {
  const PageVideoButton({
    super.key,
    required this.auth,
    required this.screen,
    this.forceVisible = false,
  });
  final AuthController auth;
  final String screen;
  final bool forceVisible;
  @override
  State<PageVideoButton> createState() => _PageVideoButtonState();
}

class PageVideoLibrary extends StatelessWidget {
  const PageVideoLibrary({super.key, required this.auth});
  final AuthController auth;
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'فيديوهات شرح الصفحات',
    child: gList([
      for (final page in const [
        ('home', 'الرئيسية'),
        ('operations', 'العمليات'),
        ('more', 'المزيد'),
        ('reports', 'التقارير'),
        ('matches', 'المباريات'),
        ('players', 'اللاعبون'),
        ('attendance', 'الحضور'),
        ('nutrition', 'التغذية والصحة'),
        ('evaluations', 'التقييمات'),
      ])
        GCard(
          child: ListTile(
            title: UiText(page.$2),
            trailing: PageVideoButton(auth: auth, screen: page.$1),
          ),
        ),
    ]),
  );
}

String? _pageVideoContext(AuthController auth) {
  final membership = auth.session?.selected;
  return membership == null
      ? null
      : '${membership.academyId}:${membership.id}:${membership.role}:${membership.contextRevision}';
}

class _PageVideoButtonState extends State<PageVideoButton> {
  bool busy = false;
  int generation = 0;
  String? identity;

  @override
  void initState() {
    super.initState();
    identity = _pageVideoContext(widget.auth);
    widget.auth.addListener(changed);
  }

  void changed() {
    final next = _pageVideoContext(widget.auth);
    if (identity == next) return;
    generation++;
    identity = next;
    if (mounted) setState(() => busy = false);
  }

  @override
  void didUpdateWidget(PageVideoButton oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.auth != widget.auth) {
      oldWidget.auth.removeListener(changed);
      widget.auth.addListener(changed);
    }
    if (oldWidget.auth != widget.auth || oldWidget.screen != widget.screen) {
      generation++;
      identity = _pageVideoContext(widget.auth);
      busy = false;
    }
  }

  @override
  void dispose() {
    generation++;
    widget.auth.removeListener(changed);
    super.dispose();
  }

  Future<void> open() async {
    if (busy) return;
    setState(() => busy = true);
    final request = ++generation;
    final scope = _pageVideoContext(widget.auth);
    bool current() =>
        mounted &&
        request == generation &&
        scope != null &&
        scope == _pageVideoContext(widget.auth);
    try {
      final d = obj(
        await widget.auth.request('GET', '/api/v1/page-help/${widget.screen}'),
      );
      if (!mounted || !current()) return;
      if (d['screen'] != widget.screen ||
          d['role'] != widget.auth.session?.selected?.role) {
        throw StateError('unavailable');
      }
      if (d['available'] != true) {
        await showDialog<void>(
          context: context,
          builder: (c) => AlertDialog(
            title: const UiText('شرح الصفحة بالفيديو'),
            content: const UiText('لم تضف الأكاديمية فيديو لهذه الصفحة بعد.'),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(c),
                child: const UiText('حسنًا'),
              ),
            ],
          ),
        );
      } else {
        await openPage(
          context,
          PageVideoPlayer(auth: widget.auth, screen: widget.screen, data: d),
        );
      }
    } catch (_) {
      if (mounted && current()) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: UiText('تعذر فتح شرح الصفحة الآن.')),
        );
      }
    } finally {
      if (current()) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => !widget.forceVisible
      ? const SizedBox.shrink()
      : IconButton(
          tooltip: 'شرح الصفحة بالفيديو',
          onPressed: busy ? null : open,
          icon: const Icon(Icons.play_circle_outline_rounded),
        );
}

class PageVideoPlayer extends StatefulWidget {
  const PageVideoPlayer({
    super.key,
    required this.auth,
    required this.screen,
    required this.data,
  });
  final AuthController auth;
  final String screen;
  final Json data;
  @override
  State<PageVideoPlayer> createState() => _PageVideoPlayerState();
}

class _PageVideoPlayerState extends State<PageVideoPlayer>
    with WidgetsBindingObserver {
  VideoPlayerController? controller;
  String? identity, title;
  bool failed = false, busy = true, invalidated = false;
  int generation = 0;
  bool get authorizedContext =>
      !invalidated &&
      identity != null &&
      identity == _pageVideoContext(widget.auth);

  @override
  void initState() {
    super.initState();
    identity = _pageVideoContext(widget.auth);
    widget.auth.addListener(changed);
    WidgetsBinding.instance.addObserver(this);
    load();
  }

  Future<void> release(VideoPlayerController? player) async {
    try {
      await player?.dispose();
    } catch (_) {
      // The platform can already have released a cancelled initialization.
    }
  }

  void clear() {
    generation++;
    final old = controller;
    controller = null;
    title = null;
    unawaited(release(old));
  }

  void changed() {
    if (!authorizedContext) {
      invalidated = true;
      clear();
      if (mounted) setState(() {});
    }
  }

  @override
  void didUpdateWidget(PageVideoPlayer oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.auth != widget.auth) {
      oldWidget.auth.removeListener(changed);
      widget.auth.addListener(changed);
    }
    if (oldWidget.auth != widget.auth ||
        oldWidget.screen != widget.screen ||
        oldWidget.data['url'] != widget.data['url']) {
      invalidated = true;
      clear();
    }
  }

  Future<Json> readAuthorization() async {
    if (!authorizedContext) throw StateError('unavailable');
    final current = obj(
      await widget.auth.request('GET', '/api/v1/page-help/${widget.screen}'),
    );
    if (!authorizedContext ||
        current['available'] != true ||
        current['url'] != widget.data['url'] ||
        current['screen'] != widget.screen ||
        current['role'] != widget.auth.session?.selected?.role) {
      throw StateError('unavailable');
    }
    final uri = Uri.parse(textOf(current['url']));
    if (uri.scheme != 'https' || uri.userInfo.isNotEmpty) {
      throw StateError('url');
    }
    return current;
  }

  Future<void> load() async {
    clear();
    if (!authorizedContext) return;
    final request = generation;
    if (mounted) {
      setState(() {
        busy = true;
        failed = false;
      });
    }
    VideoPlayerController? pending;
    bool retained = false;
    try {
      final current = await readAuthorization();
      if (!mounted || request != generation || !authorizedContext) return;
      pending = VideoPlayerController.networkUrl(
        Uri.parse(textOf(current['url'])),
      );
      await pending.initialize();
      if (!mounted || request != generation || !authorizedContext) return;
      controller = pending;
      retained = true;
      setState(() {
        title = textOf(current['title']);
        busy = false;
      });
    } catch (_) {
      if (mounted && request == generation && authorizedContext) {
        setState(() {
          failed = true;
          busy = false;
        });
      }
    } finally {
      if (!retained) await release(pending);
    }
  }

  Future<void> toggle() async {
    final player = controller;
    if (player == null || busy || !authorizedContext) return;
    final request = generation;
    setState(() => busy = true);
    try {
      if (player.value.isPlaying) {
        await player.pause();
      } else {
        final current = await readAuthorization();
        if (!mounted ||
            request != generation ||
            !authorizedContext ||
            controller != player) {
          return;
        }
        await player.play();
        if (mounted && request == generation && authorizedContext) {
          title = textOf(current['title']);
        }
      }
      if (mounted && request == generation && authorizedContext) {
        setState(() => busy = false);
      }
    } catch (_) {
      if (mounted && request == generation && authorizedContext) {
        clear();
        setState(() {
          failed = true;
          busy = false;
        });
      }
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      if (authorizedContext) unawaited(load());
    } else {
      clear();
      if (mounted) setState(() => busy = true);
    }
  }

  @override
  void dispose() {
    widget.auth.removeListener(changed);
    WidgetsBinding.instance.removeObserver(this);
    clear();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => !authorizedContext
      ? const SizedBox.shrink()
      : GuardianPage(
          title: 'شرح الصفحة بالفيديو',
          child: gList([
            if (title != null) Heading(title!),
            if (failed)
              const EmptyMessage(
                'الفيديو غير متاح الآن. راجع إدارة الأكاديمية.',
              )
            else if (controller == null)
              const Center(child: CircularProgressIndicator())
            else ...[
              AspectRatio(
                aspectRatio: controller!.value.aspectRatio,
                child: VideoPlayer(controller!),
              ),
              IconButton(
                tooltip: 'تشغيل أو إيقاف',
                iconSize: 52,
                onPressed: busy ? null : toggle,
                icon: Icon(
                  controller!.value.isPlaying
                      ? Icons.pause_circle
                      : Icons.play_circle,
                ),
              ),
            ],
          ]),
        );
}
