import 'package:flutter/material.dart';
import 'package:video_player/video_player.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

class PageVideoButton extends StatefulWidget {
  const PageVideoButton({super.key, required this.auth, required this.screen});
  final AuthController auth;
  final String screen;
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

class _PageVideoButtonState extends State<PageVideoButton> {
  bool busy = false;
  Future<void> open() async {
    if (busy) return;
    setState(() => busy = true);
    final identity = widget.auth.session?.selected?.id;
    try {
      final d = obj(
        await widget.auth.request('GET', '/api/v1/page-help/${widget.screen}'),
      );
      if (!mounted || identity != widget.auth.session?.selected?.id) return;
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
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: UiText('تعذر فتح شرح الصفحة الآن.')),
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => IconButton(
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
  String? identity;
  bool failed = false;
  int generation = 0;
  String? get contextKey => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  @override
  void initState() {
    super.initState();
    identity = contextKey;
    widget.auth.addListener(changed);
    WidgetsBinding.instance.addObserver(this);
    load();
  }

  void changed() {
    if (identity != contextKey) {
      generation++;
      controller?.pause();
      if (mounted) setState(() => failed = true);
    }
  }

  Future<void> load() async {
    final g = ++generation;
    VideoPlayerController? pending;
    try {
      final current = obj(
        await widget.auth.request('GET', '/api/v1/page-help/${widget.screen}'),
      );
      if (current['available'] != true ||
          current['url'] != widget.data['url'] ||
          identity != contextKey) {
        throw StateError('unavailable');
      }
      final uri = Uri.parse(textOf(current['url']));
      if (uri.scheme != 'https' || uri.userInfo.isNotEmpty) {
        throw StateError('url');
      }
      pending = VideoPlayerController.networkUrl(uri);
      await pending.initialize();
      if (!mounted || g != generation || identity != contextKey) {
        await pending.dispose();
        return;
      }
      setState(() => controller = pending);
    } catch (_) {
      await pending?.dispose();
      if (mounted && g == generation) setState(() => failed = true);
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state != AppLifecycleState.resumed) controller?.pause();
  }

  @override
  void dispose() {
    generation++;
    widget.auth.removeListener(changed);
    WidgetsBinding.instance.removeObserver(this);
    controller?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'شرح الصفحة بالفيديو',
    child: gList([
      Heading(textOf(widget.data['title'])),
      if (failed)
        const EmptyMessage('الفيديو غير متاح الآن. راجع إدارة الأكاديمية.')
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
          onPressed: () {
            setState(() {
              controller!.value.isPlaying
                  ? controller!.pause()
                  : controller!.play();
            });
          },
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
