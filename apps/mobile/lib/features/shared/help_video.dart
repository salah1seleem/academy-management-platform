import 'dart:io';

import 'package:flutter/material.dart';
import 'package:video_player/video_player.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

// Existing authenticated transport, private temporary media, no public URLs.
class HelpVideoPage extends StatefulWidget {
  final AuthController auth;
  final String lesson;
  final Map<String, dynamic> video;
  const HelpVideoPage({
    super.key,
    required this.auth,
    required this.lesson,
    required this.video,
  });
  @override
  State<HelpVideoPage> createState() => _HelpVideoPageState();
}

class _HelpVideoPageState extends State<HelpVideoPage>
    with WidgetsBindingObserver {
  VideoPlayerController? player;
  Directory? directory;
  bool busy = true, failed = false;
  int generation = 0;
  late String identity;
  String currentIdentity() {
    final s = widget.auth.session?.selected;
    return '${s?.academyId}:${s?.id}:${s?.contextRevision}';
  }

  @override
  void initState() {
    super.initState();
    identity = currentIdentity();
    WidgetsBinding.instance.addObserver(this);
    widget.auth.addListener(authChanged);
    load();
  }

  void authChanged() {
    if (identity != currentIdentity()) {
      clear();
      if (mounted) {
        setState(() {
          failed = true;
          busy = false;
        });
      }
    }
  }

  Future<void> disposeMedia(VideoPlayerController? p, Directory? d) async {
    try {
      await p?.dispose();
    } catch (_) {
      /* Cancelled initialization. */
    }
    try {
      if (d != null && await d.exists()) await d.delete(recursive: true);
    } on FileSystemException {
      /* This route's temporary folder only. */
    }
  }

  void clear() {
    generation++;
    final old = player, dir = directory;
    player = null;
    directory = null;
    disposeMedia(old, dir);
  }

  Future<bool> authorized() async {
    if (identity != currentIdentity() || widget.auth.session == null) {
      return false;
    }
    final value = obj(
      await widget.auth.request(
        'GET',
        '/api/v1/help/lessons/${Uri.encodeComponent(widget.lesson)}',
      ),
    );
    final published = obj(value['video']);
    final id = textOf(widget.video['id']);
    final path = '/api/v1/help/lessons/${widget.lesson}/media/$id/video';
    return value['id'] == widget.lesson &&
        value['videoStatus'] == 'Published' &&
        published['id'] == id &&
        published['url'] == path &&
        widget.video['url'] == path &&
        RegExp(r'^[a-fA-F0-9-]{36}$').hasMatch(id);
  }

  Future<void> load() async {
    clear();
    final request = generation;
    if (mounted) {
      setState(() {
        busy = true;
        failed = false;
      });
    }
    Directory? temp;
    VideoPlayerController? pending;
    bool retained = false;
    try {
      if (!await authorized()) throw StateError('unavailable');
      final bytes = await widget.auth.asset(textOf(widget.video['url']));
      if (!mounted || request != generation) return;
      temp = await Directory.systemTemp.createTemp('academity-help-video-');
      final file = File('${temp.path}/video.mp4');
      await file.writeAsBytes(bytes, flush: true);
      pending = VideoPlayerController.file(file);
      await pending.initialize();
      if (!mounted || request != generation || identity != currentIdentity()) {
        return;
      }
      player = pending;
      directory = temp;
      retained = true;
      setState(() => busy = false);
    } catch (_) {
      if (mounted && request == generation) {
        setState(() {
          failed = true;
          busy = false;
        });
      }
    } finally {
      if (!retained) await disposeMedia(pending, temp);
    }
  }

  Future<void> play() async {
    final p = player;
    if (p == null || busy) return;
    if (p.value.isPlaying) {
      await p.pause();
      if (mounted) setState(() {});
      return;
    }
    final request = generation;
    setState(() => busy = true);
    try {
      if (!await authorized()) throw StateError('unavailable');
      if (mounted && generation == request && p == player) {
        await p.play();
        setState(() => busy = false);
      }
    } catch (_) {
      if (mounted && generation == request) {
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
      load();
    } else {
      clear();
      if (mounted) setState(() => busy = true);
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    widget.auth.removeListener(authChanged);
    clear();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'شرح بالفيديو', 'Video guide'),
    child: gList([
      Heading(textOf(widget.video['title'])),
      UiText(textOf(widget.video['description'])),
      if (failed) ...[
        UiText(
          localized(
            context,
            'الفيديو غير متاح الآن. حدّث الدليل أو اقرأ خطواته المكتوبة.',
            'Video unavailable. Refresh the guide or read its written steps.',
          ),
        ),
        TextButton(
          onPressed: load,
          child: UiText(localized(context, 'إعادة المحاولة', 'Retry')),
        ),
      ] else if (player != null) ...[
        AspectRatio(
          aspectRatio: player!.value.aspectRatio,
          child: VideoPlayer(player!),
        ),
        IconButton(
          tooltip: localized(
            context,
            'تشغيل أو إيقاف الفيديو',
            'Play or pause video',
          ),
          onPressed: busy ? null : play,
          iconSize: 48,
          icon: Icon(
            player!.value.isPlaying ? Icons.pause_circle : Icons.play_circle,
          ),
        ),
      ] else
        const Center(child: CircularProgressIndicator()),
      UiText(textOf(widget.video['alternativeText'])),
      UiText(
        localized(
          context,
          'إصدار ${widget.video['version']} · ${widget.video['durationSeconds']} ثانية',
          'Version ${widget.video['version']} · ${widget.video['durationSeconds']} seconds',
        ),
      ),
    ]),
  );
}
