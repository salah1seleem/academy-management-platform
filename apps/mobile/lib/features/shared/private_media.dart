import 'package:academy_mobile/core/theme/ui_text.dart';

import 'dart:io';
import 'dart:typed_data';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:video_player/video_player.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/api/api_client.dart';
import '../../core/theme/appearance.dart';
import '../guardian/guardian_ui.dart';

bool isPrivateAsset(Object? reference) =>
    RegExp(r'^/api/v1/assets/[a-fA-F0-9-]{36}$').hasMatch('$reference');

class PrivateImage extends StatefulWidget {
  final AuthController auth;
  final String path;
  final double height;
  const PrivateImage({
    super.key,
    required this.auth,
    required this.path,
    this.height = 150,
  });
  @override
  State<PrivateImage> createState() => _PrivateImageState();
}

class _PrivateImageState extends State<PrivateImage> {
  late Future<Uint8List> bytes;
  @override
  void initState() {
    super.initState();
    bytes = widget.auth.asset(widget.path);
  }

  @override
  void didUpdateWidget(covariant PrivateImage old) {
    super.didUpdateWidget(old);
    if (old.path != widget.path || old.auth != widget.auth) {
      bytes = widget.auth.asset(widget.path);
    }
  }

  @override
  Widget build(BuildContext context) => FutureBuilder<Uint8List>(
    future: bytes,
    builder: (context, data) => SizedBox(
      height: widget.height,
      width: double.infinity,
      child: data.hasData
          ? Image.memory(
              data.data!,
              fit: BoxFit.cover,
              gaplessPlayback: false,
              errorBuilder: (_, error, stack) =>
                  const Icon(Icons.broken_image_outlined),
            )
          : Center(
              child: data.hasError
                  ? const Icon(Icons.image_not_supported_outlined)
                  : const CircularProgressIndicator(strokeWidth: 2),
            ),
    ),
  );
}

Future<Json?> pickAndUpload(
  AuthController auth,
  Map<String, String> fields, {
  bool imageOnly = false,
}) async {
  final file = await FilePicker.pickFile(
    type: FileType.custom,
    allowedExtensions: imageOnly
        ? ['jpg', 'jpeg', 'png', 'webp']
        : ['jpg', 'jpeg', 'png', 'webp', 'mp4', 'mov'],
  );
  if (file == null) return null;
  final type = const {
    'jpg': 'image/jpeg',
    'jpeg': 'image/jpeg',
    'png': 'image/png',
    'webp': 'image/webp',
    'mp4': 'video/mp4',
    'mov': 'video/quicktime',
  }[file.name.split('.').last.toLowerCase()];
  if (type == null) throw const ApiFailure(400, 'هذا النوع غير مدعوم.');
  final max = type.startsWith('image/') ? 8 * 1024 * 1024 : 50 * 1024 * 1024;
  final length = await file.length();
  if (length == null || length > max) {
    throw const ApiFailure(
      413,
      'الصورة حتى 8 ميجابايت والفيديو حتى 50 ميجابايت.',
    );
  }
  return obj(await auth.upload(await file.readAsBytes(), type, fields));
}

class UploadSessionMedia extends StatefulWidget {
  final AuthController auth;
  final String groupId;
  const UploadSessionMedia({
    super.key,
    required this.auth,
    required this.groupId,
  });
  @override
  State<UploadSessionMedia> createState() => _UploadSessionMediaState();
}

class _UploadSessionMediaState extends State<UploadSessionMedia> {
  bool busy = false;
  String? message;
  final caption = TextEditingController();
  @override
  void dispose() {
    caption.dispose();
    super.dispose();
  }

  Future<void> upload() async {
    if (busy) return;
    setState(() {
      busy = true;
      message = null;
    });
    try {
      final asset = await pickAndUpload(widget.auth, {
        'purpose': 'Media',
        'groupId': widget.groupId,
        'caption': caption.text.trim(),
      });
      if (mounted && asset != null) {
        setState(
          () => message = localized(
            context,
            'تم الرفع. ظهرت الوسائط لأولياء الأمور المرتبطين بالمجموعة.',
            'Uploaded. Media is now visible to linked guardians in this group.',
          ),
        );
      }
    } catch (e) {
      if (mounted) {
        setState(
          () => message = e is ApiFailure ? e.message : 'تعذر رفع الوسائط.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'صور وفيديوهات المجموعة', 'Group media'),
    child: gList([
      TextField(
        controller: caption,
        maxLength: 300,
        decoration: InputDecoration(
          labelText: localized(
            context,
            'وصف مختصر (اختياري)',
            'Caption (optional)',
          ),
        ),
      ),
      FilledButton.icon(
        onPressed: busy ? null : upload,
        icon: const Icon(Icons.add_photo_alternate_outlined),
        label: UiText(
          localized(
            context,
            busy ? 'جارٍ الرفع' : 'رفع صورة أو فيديو',
            busy ? 'Uploading' : 'Upload photo or video',
          ),
        ),
      ),
      if (message != null) UiText(message!),
      const SizedBox(height: 20),
      RemoteBody(
        auth: widget.auth,
        path: '/api/v1/assets/gallery?groupId=${widget.groupId}',
        builder: (context, v) => Column(
          children: [
            for (final a in rows(v)) MediaTile(auth: widget.auth, media: a),
          ],
        ),
      ),
    ]),
  );
}

class MediaTile extends StatelessWidget {
  final AuthController auth;
  final Json media;
  const MediaTile({super.key, required this.auth, required this.media});
  @override
  Widget build(BuildContext context) => GCard(
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (textOf(media['contentType']).startsWith('image/'))
          PrivateImage(auth: auth, path: media['url'])
        else
          InkWell(
            onTap: () =>
                openPage(context, PrivateVideo(auth: auth, path: media['url'])),
            child: Container(
              height: 140,
              alignment: Alignment.center,
              decoration: BoxDecoration(
                color: Theme.of(context).colorScheme.primary
                    .withValues(alpha: .1),
                borderRadius: BorderRadius.circular(16),
              ),
              child: const Icon(Icons.play_circle_outline, size: 60),
            ),
          ),
        if (media['caption'] != null) UiText(textOf(media['caption'])),
        UiText(
          day(media['createdAtUtc']),
          style: Theme.of(context).textTheme.labelSmall,
        ),
      ],
    ),
  );
}

class PrivateVideo extends StatefulWidget {
  final AuthController auth;
  final String path;
  const PrivateVideo({super.key, required this.auth, required this.path});
  @override
  State<PrivateVideo> createState() => _PrivateVideoState();
}

class _PrivateVideoState extends State<PrivateVideo> {
  VideoPlayerController? controller;
  Directory? directory;
  String? error;
  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> load() async {
    Directory? temporary;
    VideoPlayerController? pending;
    bool retained = false;
    try {
      final bytes = await widget.auth.asset(widget.path);
      if (!mounted) return;
      final temp = await Directory.systemTemp.createTemp(
        'academy-private-video-',
      );
      temporary = temp;
      final file = File('${temp.path}/video.mp4');
      await file.writeAsBytes(bytes, flush: true);
      if (!mounted) return;
      final player = VideoPlayerController.file(file);
      pending = player;
      await player.initialize();
      if (!mounted) return;
      directory = temp;
      controller = player;
      retained = true;
      setState(() {});
    } catch (_) {
      if (mounted) {
        setState(
          () => error = localized(
            context,
            'تعذر تشغيل الفيديو.',
            'Unable to play this video.',
          ),
        );
      }
    } finally {
      if (!retained) await cleanup(pending, temporary);
    }
  }

  Future<void> cleanup(VideoPlayerController? player, Directory? temp) async {
    try {
      await player?.dispose();
    } catch (_) {
      /* A cancelled initialization has no live player. */
    }
    try {
      if (temp != null && await temp.exists()) {
        await temp.delete(recursive: true);
      }
    } on FileSystemException {
      /* This route owns only its private temporary directory. */
    }
  }

  @override
  void dispose() {
    cleanup(controller, directory);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'فيديو التدريب', 'Training video'),
    child: Center(
      child: error != null
          ? UiText(error!)
          : controller?.value.isInitialized != true
          ? const CircularProgressIndicator()
          : Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                AspectRatio(
                  aspectRatio: controller!.value.aspectRatio,
                  child: VideoPlayer(controller!),
                ),
                IconButton(
                  iconSize: 48,
                  icon: Icon(
                    controller!.value.isPlaying
                        ? Icons.pause_circle
                        : Icons.play_circle,
                  ),
                  onPressed: () {
                    controller!.value.isPlaying
                        ? controller!.pause()
                        : controller!.play();
                    setState(() {});
                  },
                ),
              ],
            ),
    ),
  );
}
