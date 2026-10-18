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
  final BoxFit fit;
  final Widget? fallback;
  const PrivateImage({
    super.key,
    required this.auth,
    required this.path,
    this.height = 150,
    this.fit = BoxFit.cover,
    this.fallback,
  });
  @override
  State<PrivateImage> createState() => _PrivateImageState();
}

String? _mediaIdentity(AuthController auth) => auth.session?.selected == null
    ? null
    : '${auth.session!.selected!.id}:${auth.session!.selected!.contextRevision}';

class _PrivateImageState extends State<PrivateImage>
    with WidgetsBindingObserver {
  Future<Uint8List>? bytes;
  late String? original;
  bool foreground = true;
  void refresh() {
    if (!mounted) return;
    setState(() {
      bytes =
          foreground &&
              original != null &&
              _mediaIdentity(widget.auth) == original
          ? widget.auth.asset(widget.path)
          : null;
    });
  }

  void authorityChanged() {
    if (_mediaIdentity(widget.auth) != original) refresh();
  }

  @override
  void initState() {
    super.initState();
    original = _mediaIdentity(widget.auth);
    WidgetsBinding.instance.addObserver(this);
    widget.auth.addListener(authorityChanged);
    bytes = original == null ? null : widget.auth.asset(widget.path);
  }

  @override
  void didUpdateWidget(covariant PrivateImage old) {
    super.didUpdateWidget(old);
    if (old.path != widget.path || old.auth != widget.auth) {
      old.auth.removeListener(authorityChanged);
      widget.auth.addListener(authorityChanged);
      original = _mediaIdentity(widget.auth);
      refresh();
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState s) {
    foreground = s == AppLifecycleState.resumed;
    refresh();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    widget.auth.removeListener(authorityChanged);
    bytes = null;
    super.dispose();
  }

  @override
  Widget build(BuildContext context) =>
      !foreground || original == null || _mediaIdentity(widget.auth) != original
      ? const SizedBox.shrink()
      : FutureBuilder<Uint8List>(
          future: bytes,
          builder: (context, data) => SizedBox(
            height: widget.height,
            width: double.infinity,
            child: data.hasData
                ? Image.memory(
                    data.data!,
                    fit: widget.fit,
                    gaplessPlayback: false,
                    errorBuilder: (_, error, stack) =>
                        widget.fallback ??
                        const Icon(Icons.broken_image_outlined),
                  )
                : Center(
                    child: data.hasError
                        ? widget.fallback ??
                              const Icon(Icons.image_not_supported_outlined)
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

class _UploadSessionMediaState extends State<UploadSessionMedia>
    with WidgetsBindingObserver {
  bool busy = false;
  String? message;
  String eventOn = '';
  String sessionId = '';
  Set<String> subjects = {};
  bool noChildren = false, confirmed = false, foreground = true;
  int revision = 0;
  String? get identity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final original = identity;
  final caption = TextEditingController();
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState s) {
    setState(() {
      foreground = s == AppLifecycleState.resumed;
      revision++;
      subjects = {};
      confirmed = false;
      noChildren = false;
      message = null;
    });
  }

  @override
  void dispose() {
    caption.dispose();
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  Future<void> upload(Json audience) async {
    if (busy) return;
    if (!confirmed || (!noChildren && subjects.isEmpty)) {
      setState(
        () => message = localized(
          context,
          'راجع الأطفال الظاهرين وأكد خصوصية الملف قبل الرفع.',
          'Review visible children and confirm privacy before uploading.',
        ),
      );
      return;
    }
    setState(() {
      busy = true;
      message = null;
    });
    try {
      final asset = await pickAndUpload(widget.auth, {
        'purpose': 'Media',
        'groupId': widget.groupId,
        'caption': caption.text.trim(),
        'eventOn': eventOn.isEmpty ? textOf(audience['eventOn']) : eventOn,
        if (sessionId.isNotEmpty) 'sessionId': sessionId,
        'subjectPlayerIds': subjects.join(','),
        'noRecognizableChildren': '$noChildren',
        'privacyConfirmed': '$confirmed',
      });
      if (mounted && asset != null) {
        setState(() {
          revision++;
          confirmed = false;
          subjects = {};
          noChildren = false;
          caption.clear();
          message = localized(
            context,
            'تم النشر للمخولين وفق جمهور الواقعة والموافقات، دون توريثها للأعضاء الجدد.',
            'Published for the authorized event audience and consent, never inherited by later joiners.',
          );
        });
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
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (!foreground || identity == null || identity != original) {
        return const SizedBox.shrink();
      }
      return GuardianPage(
        title: localized(context, 'صور وفيديوهات المجموعة', 'Group media'),
        child: RemoteBody(
          key: ValueKey('audience:$revision:$eventOn:$sessionId'),
          auth: widget.auth,
          path:
              '/api/v1/assets/media-audience?groupId=${widget.groupId}${eventOn.isEmpty ? '' : '&eventOn=$eventOn'}${sessionId.isEmpty ? '' : '&sessionId=$sessionId'}',
          builder: (context, value) {
            final audience = obj(value);
            return gList([
              Text(textOf(audience['notice'])),
              OutlinedButton.icon(
                onPressed: busy
                    ? null
                    : () async {
                        final today = DateTime.parse(
                          textOf(audience['asOfDate']),
                        );
                        final selected = await showDatePicker(
                          context: context,
                          initialDate: DateTime.parse(
                            eventOn.isEmpty
                                ? textOf(audience['eventOn'])
                                : eventOn,
                          ),
                          firstDate: DateTime(1900),
                          lastDate: today,
                        );
                        if (selected != null && mounted) {
                          setState(() {
                            eventOn = selected
                                .toIso8601String()
                                .split('T')
                                .first;
                            sessionId = '';
                            subjects = {};
                            noChildren = false;
                            confirmed = false;
                          });
                        }
                      },
                icon: const Icon(Icons.calendar_month),
                label: Text(
                  '${localized(context, 'تاريخ الواقعة', 'Event date')} · ${eventOn.isEmpty ? audience['eventOn'] : eventOn}',
                ),
              ),
              DropdownButtonFormField<String>(
                key: ValueKey('session:$sessionId:$eventOn'),
                initialValue: sessionId,
                isExpanded: true,
                decoration: InputDecoration(
                  labelText: localized(
                    context,
                    'الحصة (اختياري)',
                    'Session (optional)',
                  ),
                ),
                items: [
                  DropdownMenuItem(
                    value: '',
                    child: Text(
                      localized(
                        context,
                        'المجموعة دون حصة',
                        'Group without a session',
                      ),
                    ),
                  ),
                  for (final s in rows(audience['sessions'] ?? <Json>[]))
                    DropdownMenuItem(
                      value: textOf(s['id']),
                      child: Text(
                        '${textOf(s['startTime']).substring(0, 5)} — ${textOf(s['endTime']).substring(0, 5)}',
                      ),
                    ),
                ],
                onChanged: busy
                    ? null
                    : (value) => setState(() {
                        sessionId = value ?? '';
                        subjects = {};
                        noChildren = false;
                        confirmed = false;
                      }),
              ),
              const SizedBox(height: 16),
              CheckboxListTile(
                value: noChildren,
                onChanged: busy
                    ? null
                    : (v) => setState(() {
                        noChildren = v == true;
                        subjects = {};
                        confirmed = false;
                      }),
                title: Text(
                  localized(
                    context,
                    'لا يوجد طفل يمكن التعرف عليه في هذه اللقطة',
                    'No identifiable children in this media',
                  ),
                ),
              ),
              for (final p in rows(audience['players']))
                CheckboxListTile(
                  value: subjects.contains(p['id']),
                  onChanged: busy || noChildren || p['consented'] != true
                      ? null
                      : (v) => setState(() {
                          v == true
                              ? subjects.add(textOf(p['id']))
                              : subjects.remove(textOf(p['id']));
                          confirmed = false;
                        }),
                  title: Text(textOf(p['name'])),
                  subtitle: Text(
                    localized(
                      context,
                      p['consented'] == true
                          ? 'موافقة مستوفاة'
                          : 'الموافقة غير مستوفاة — لا تنشر ظهوره',
                      p['consented'] == true
                          ? 'Consent complete'
                          : 'Missing consent — do not publish their appearance',
                    ),
                  ),
                ),
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
                onPressed: busy ? null : () => upload(audience),
                icon: const Icon(Icons.add_photo_alternate_outlined),
                label: UiText(
                  localized(
                    context,
                    busy ? 'جارٍ الرفع' : 'رفع صورة أو فيديو',
                    busy ? 'Uploading' : 'Upload photo or video',
                  ),
                ),
              ),
              CheckboxListTile(
                value: confirmed,
                onChanged: busy
                    ? null
                    : (v) => setState(() => confirmed = v == true),
                title: Text(
                  localized(
                    context,
                    'راجعت الملف؛ لا يظهر طفل آخر خارج القائمة المحددة. هذا ليس تصريح تسويق أو إعلان.',
                    'I reviewed the file; no other child is identifiable outside the selected list. This is not marketing or advertising consent.',
                  ),
                ),
              ),
              if (message != null) UiText(message!),
              const SizedBox(height: 20),
              RemoteBody(
                key: ValueKey('gallery:$revision'),
                auth: widget.auth,
                path: '/api/v1/assets/gallery?groupId=${widget.groupId}',
                builder: (context, v) => Column(
                  children: [
                    for (final a in rows(v))
                      MediaTile(auth: widget.auth, media: a),
                  ],
                ),
              ),
            ]);
          },
        ),
      );
    },
  );
}

class MediaTile extends StatelessWidget {
  final AuthController auth;
  final Json media;
  final Widget? footer;
  const MediaTile({
    super.key,
    required this.auth,
    required this.media,
    this.footer,
  });
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
          day(media['mediaEventOn'] ?? media['createdAtUtc']),
          style: Theme.of(context).textTheme.labelSmall,
        ),
        if (footer != null) ...[const SizedBox(height: 12), footer!],
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

class _PrivateVideoState extends State<PrivateVideo>
    with WidgetsBindingObserver {
  VideoPlayerController? controller;
  Directory? directory;
  String? error;
  late final String? original = _mediaIdentity(widget.auth);
  bool foreground = true;
  int generation = 0;
  bool get authorized =>
      mounted &&
      foreground &&
      original != null &&
      _mediaIdentity(widget.auth) == original;
  void authorityChanged() {
    if (_mediaIdentity(widget.auth) != original) invalidate();
  }

  void invalidate() {
    generation++;
    final p = controller, d = directory;
    controller = null;
    directory = null;
    cleanup(p, d);
    if (mounted) setState(() => error = null);
  }

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    widget.auth.addListener(authorityChanged);
    load();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState s) {
    foreground = s == AppLifecycleState.resumed;
    invalidate();
    if (authorized) load();
  }

  Future<void> load() async {
    if (!authorized) return;
    final token = ++generation;
    Directory? temporary;
    VideoPlayerController? pending;
    bool retained = false;
    try {
      final bytes = await widget.auth.asset(widget.path);
      if (!authorized || token != generation) return;
      final temp = await Directory.systemTemp.createTemp(
        'academy-private-video-',
      );
      temporary = temp;
      final file = File('${temp.path}/video.mp4');
      await file.writeAsBytes(bytes, flush: true);
      if (!authorized || token != generation) return;
      final player = VideoPlayerController.file(file);
      pending = player;
      await player.initialize();
      if (!authorized || token != generation) return;
      directory = temp;
      controller = player;
      retained = true;
      setState(() {});
    } catch (_) {
      if (authorized && token == generation) {
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
    generation++;
    WidgetsBinding.instance.removeObserver(this);
    widget.auth.removeListener(authorityChanged);
    cleanup(controller, directory);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => !authorized
      ? const SizedBox.shrink()
      : GuardianPage(
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
