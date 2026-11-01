import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../shared/private_media.dart';
import 'guardian_ui.dart';

/// Each gallery read keeps its existing linked-child authorization. Shared
/// group assets are shown once, with all eligible siblings named on the tile.
class FamilyTrainingMedia extends StatefulWidget {
  const FamilyTrainingMedia({
    super.key,
    required this.auth,
    required this.children,
    this.all = false,
  });
  final AuthController auth;
  final List<Json> children;
  final bool all;
  @override
  State<FamilyTrainingMedia> createState() => _FamilyTrainingMediaState();
}

class _FamilyTrainingMediaState extends State<FamilyTrainingMedia> {
  String? get identity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final opened = identity;
  late Future<({List<Json> items, bool failed})> future = load();
  Future<({List<Json> items, bool failed})> load() async {
    final results = await Future.wait(
      widget.children.map((child) async {
        try {
          return (
            child: child,
            items: rows(
              await widget.auth.request(
                'GET',
                '/api/v1/assets/gallery?playerId=${Uri.encodeQueryComponent(textOf(child['id']))}',
              ),
            ),
            failed: false,
          );
        } catch (_) {
          return (child: child, items: <Json>[], failed: true);
        }
      }),
    );
    final unique = <String, Json>{};
    for (final result in results) {
      for (final asset in result.items) {
        final key = textOf(asset['id']);
        final item = unique.putIfAbsent(
          key,
          () => {...asset, 'familyNames': <String>[]},
        );
        final names = item['familyNames'] as List<String>;
        final name = textOf(result.child['arabicName']);
        if (!names.contains(name)) {
          names.add(name);
        }
      }
    }
    return (
      items: unique.values.toList()
        ..sort(
          (a, b) => (b['createdAtUtc']?.toString() ?? '').compareTo(
            a['createdAtUtc']?.toString() ?? '',
          ),
        ),
      failed: results.any((r) => r.failed),
    );
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (identity == null || identity != opened) {
        return const SizedBox.shrink();
      }
      return FutureBuilder(
        future: future,
        builder: (context, snapshot) {
          if (!snapshot.hasData) {
            return const SizedBox.shrink();
          }
          final data = snapshot.data!;
          if (data.items.isEmpty && !data.failed) {
            return widget.all
                ? const EmptyMessage('لا توجد وسائط منشورة للأبناء حاليًا.')
                : const SizedBox.shrink();
          }
          final items = widget.all ? data.items : data.items.take(4).toList();
          return Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (!widget.all) const Heading('صور وفيديوهات التمرين'),
              if (data.failed)
                TextButton(
                  onPressed: () => setState(() => future = load()),
                  child: const UiText('تحديث صور وفيديوهات الأبناء'),
                ),
              LayoutBuilder(
                builder: (context, bounds) {
                  final wide = MediaQuery.textScalerOf(context).scale(1) > 1.4;
                  final width = wide
                      ? bounds.maxWidth
                      : (bounds.maxWidth - 12) / 2;
                  return Wrap(
                    spacing: 12,
                    runSpacing: 12,
                    children: [
                      for (final item in items)
                        SizedBox(
                          width: width,
                          child: GCard(
                            onTap: () => openPage(
                              context,
                              GuardianPage(
                                title: textOf(
                                  item['caption'] ?? 'وسائط التدريب',
                                ),
                                child: gList([
                                  MediaTile(auth: widget.auth, media: item),
                                ]),
                              ),
                            ),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.stretch,
                              children: [
                                if (textOf(item['contentType'])
                                    .startsWith('image/'))
                                  PrivateImage(
                                    auth: widget.auth,
                                    path: textOf(item['url']),
                                    height: 130,
                                  )
                                else
                                  Container(
                                    height: 130,
                                    color: Theme.of(context)
                                        .colorScheme
                                        .surfaceContainerHighest,
                                    child: const Icon(
                                      Icons.play_circle_outline_rounded,
                                      size: 48,
                                    ),
                                  ),
                                const SizedBox(height: 8),
                                UiText(
                                  textOf(item['caption'] ?? 'من التمرين'),
                                  maxLines: 2,
                                  overflow: TextOverflow.ellipsis,
                                  style: const TextStyle(
                                    fontSize: 13,
                                    fontWeight: FontWeight.w700,
                                  ),
                                ),
                                const SizedBox(height: 4),
                                UiText(
                                  (item['familyNames'] as List<String>).join(
                                    ' · ',
                                  ),
                                  style: const TextStyle(fontSize: 11),
                                ),
                              ],
                            ),
                          ),
                        ),
                    ],
                  );
                },
              ),
              if (!widget.all && data.items.isNotEmpty)
                TextButton(
                  onPressed: () => openPage(
                    context,
                    GuardianPage(
                      title: 'صور وفيديوهات أبنائي',
                      child: gList([
                        FamilyTrainingMedia(
                          auth: widget.auth,
                          children: widget.children,
                          all: true,
                        ),
                      ]),
                    ),
                  ),
                  child: const UiText('عرض كل الصور والفيديوهات'),
                ),
            ],
          );
        },
      );
    },
  );
}
