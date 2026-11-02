import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../admin/native_operation_form.dart';
import '../guardian/guardian_ui.dart';
import '../owner/owner_followups.dart';
import 'private_media.dart';

class NativeContentEntry extends StatelessWidget {
  const NativeContentEntry({super.key, required this.auth});
  final AuthController auth;
  Widget link(BuildContext context) => GLink(
    'الأخبار والفعاليات والإعلانات',
    Icons.newspaper_outlined,
    () => openPage(context, NativeAcademyContent(auth: auth)),
  );
  @override
  Widget build(BuildContext context) =>
      auth.session?.selected?.role == 'AcademyOwner'
      ? link(context)
      : RemoteBody(
          auth: auth,
          path: '/api/v1/my-access',
          builder: (context, value) {
            final access = obj(value);
            if (access['useActionGrants'] == true) {
              return rows(access['actions'] ?? [])
                      .any((g) => g['module'] == 'news' && g['read'] == true)
                  ? link(context)
                  : const SizedBox.shrink();
            }
            return RemoteBody(
              auth: auth,
              path: '/api/v1/my-permissions',
              builder: (context, permissions) =>
                  (permissions as List).contains('content.media.manage')
                  ? link(context)
                  : const SizedBox.shrink(),
            );
          },
        );
}

const contentKinds = {
  'News': 'الأخبار',
  'Event': 'الفعاليات',
  'Announcement': 'إعلانات الأكاديمية',
};
const contentKindNames = {
  'News': 'خبر',
  'Event': 'فعالية',
  'Announcement': 'إعلان',
};
const contentRoles = {
  'AcademyAdmin': 'الإداريون',
  'Coach': 'المدربون',
  'Guardian': 'أولياء الأمور',
};
const contentStates = {
  'Draft': 'مسودة',
  'Scheduled': 'مجدول',
  'Published': 'يعرض الآن',
  'Expired': 'انتهى عرضه',
  'Archived': 'محذوف من العرض',
};

class NativeAcademyContent extends StatefulWidget {
  const NativeAcademyContent({super.key, required this.auth});
  final AuthController auth;
  @override
  State<NativeAcademyContent> createState() => _ContentListState();
}

class _ContentListState extends NativeOperationState<NativeAcademyContent> {
  @override
  AuthController get auth => widget.auth;
  Json? options, data;
  int number = 1;
  String kind = '';
  @override
  void initState() {
    super.initState();
    autoPage(
      hasMore: () =>
          rows(data?['items'] ?? []).length < (data?['total'] as num? ?? 0),
      next: () => load(more: true),
    );
    load();
  }

  Future<void> load({bool more = false}) => run(() async {
    final target = more ? number + 1 : 1;
    final o = obj(await auth.request('GET', '/api/v1/content/news/options'));
    final d = obj(
      await auth.request(
        'GET',
        '/api/v1/content/news/?paged=true&page=$target${kind.isEmpty ? '' : '&kind=$kind'}',
      ),
    );
    if (current) {
      setState(() {
        options = o;
        data = {
          ...d,
          'items': [
            if (more) ...rows(data?['items'] ?? []),
            ...rows(d['items']),
          ],
        };
        number = target;
      });
    }
  });
  Future<void> edit([Json? item]) async {
    final saved = await openPage<bool>(
      context,
      NativeContentEditor(
        auth: auth,
        initial: item,
        kind: kind.isEmpty ? 'News' : kind,
      ),
    );
    if (saved == true && current) await load();
  }

  Future<void> archive(Json item) async {
    final approved = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        title: const UiText('حذف المحتوى من العرض؟'),
        content: UiText(textOf(item['title'])),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(c, false),
            child: const UiText('رجوع'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(c, true),
            child: const UiText('حذف من العرض'),
          ),
        ],
      ),
    );
    if (approved != true || !current) return;
    await run(() async {
      final id = textOf(item['id']);
      await auth.request(
        'DELETE',
        '/api/v1/content/news/$id',
        body: {'expectedVersion': item['version']},
      );
      final read = obj(await auth.request('GET', '/api/v1/content/news/$id'));
      if (read['isActive'] != false) {
        throw const ApiFailure(
          0,
          'لم تتأكد قراءة حالة المحتوى. راجع القائمة قبل محاولة أخرى.',
        );
      }
      if (current) {
        setState(() {
          data = {
            ...data!,
            'items': rows(data!['items'])
                .map((n) => n['id'] == id ? read : n)
                .toList(),
          };
          message = 'حُذف المحتوى من العرض.';
        });
      }
    });
  }

  @override
  Widget build(BuildContext context) => page('الأخبار والفعاليات والإعلانات', [
    Wrap(
      spacing: 8,
      children: [
        for (final entry in {'': 'الكل', ...contentKinds}.entries)
          ChoiceChip(
            label: UiText(entry.value),
            selected: kind == entry.key,
            onSelected: busy
                ? null
                : (_) {
                    setState(() {
                      kind = entry.key;
                      number = 1;
                      data = null;
                    });
                    load();
                  },
          ),
      ],
    ),
    if (options?['canCreate'] == true)
      FilledButton.icon(
        onPressed: () => edit(),
        icon: const Icon(Icons.add),
        label: const UiText('إضافة محتوى'),
      ),
    if (data != null) ...[
      if (rows(data!['items']).isEmpty)
        const EmptyMessage('لا يوجد محتوى لهذا القسم.'),
      for (final item in rows(data!['items']))
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  Expanded(
                    child: UiText(
                      textOf(item['title']),
                      style: const TextStyle(fontWeight: FontWeight.w700),
                    ),
                  ),
                  Container(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 8,
                      vertical: 4,
                    ),
                    color:
                        (item['state'] == 'Published'
                                ? Colors.green
                                : item['state'] == 'Scheduled'
                                ? Colors.amber
                                : Theme.of(context).colorScheme.primary)
                            .withValues(alpha: .12),
                    child: UiText(
                      contentStates[item['state']] ?? 'حالة محفوظة',
                      style: Theme.of(context).textTheme.labelSmall,
                    ),
                  ),
                ],
              ),
              UiText(
                '${contentKinds[item['kind']] ?? 'محتوى'} · ${(item['audienceRoles'] as List? ?? []).map((r) => contentRoles[r] ?? 'جمهور مخول').join('، ')}',
                style: Theme.of(context).textTheme.bodySmall,
              ),
              UiText(
                (item['targetBranchIds'] as List? ?? []).isEmpty
                    ? 'كل الفروع'
                    : (item['targetBranchIds'] as List)
                          .map(
                            (id) =>
                                rows(options?['branches'] ?? [])
                                    .where((b) => b['id'] == id)
                                    .firstOrNull?['name'] ??
                                'فرع مؤرشف',
                          )
                          .join('، '),
                style: Theme.of(context).textTheme.bodySmall,
              ),
              if (item['localStartsOn'] != null)
                UiText(
                  '${day(item['localStartsOn'])} — ${day(item['localEndsOn'])}',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              Wrap(
                spacing: 12,
                children: [
                  if (options?['canEdit'] == true)
                    TextButton(
                      onPressed: () => edit(item),
                      child: const UiText('تعديل'),
                    ),
                  if (options?['canDelete'] == true && item['isActive'] == true)
                    TextButton(
                      onPressed: () => archive(item),
                      child: const UiText('حذف'),
                    ),
                ],
              ),
            ],
          ),
        ),
      const SizedBox.shrink(),
    ],
  ], screen: 'more');
}

class NativeContentEditor extends StatefulWidget {
  const NativeContentEditor({
    super.key,
    required this.auth,
    this.initial,
    this.kind = 'News',
  });
  final AuthController auth;
  final Json? initial;
  final String kind;
  @override
  State<NativeContentEditor> createState() => _ContentEditorState();
}

class _ContentEditorState extends NativeOperationState<NativeContentEditor> {
  @override
  AuthController get auth => widget.auth;
  final title = TextEditingController(),
      body = TextEditingController(),
      from = TextEditingController(),
      to = TextEditingController();
  final branches = <String>{}, roles = <String>{};
  Json? options, saved, frozen;
  String? imageId, imagePath;
  late String kind = widget.kind;
  late String requestId = followUpRequestId();
  bool allBranches = true,
      uncertain = false,
      published = true,
      reviewing = false;
  @override
  void initState() {
    super.initState();
    load();
  }

  @override
  void dispose() {
    title.dispose();
    body.dispose();
    from.dispose();
    to.dispose();
    super.dispose();
  }

  Future<void> load() => run(() async {
    final o = obj(await auth.request('GET', '/api/v1/content/news/options'));
    final item = widget.initial == null
        ? null
        : obj(
            await auth.request(
              'GET',
              '/api/v1/content/news/${widget.initial!['id']}',
            ),
          );
    if (!current) return;
    setState(() {
      options = o;
      saved = item;
      kind = textOf(item?['kind'] ?? widget.kind);
      title.text = textOf(item?['title'] ?? '');
      body.text = textOf(item?['body'] ?? '');
      from.text = textOf(item?['localStartsOn'] ?? o['today']);
      to.text = textOf(
        item?['localEndsOn'] ??
            DateTime.parse(from.text)
                .add(
                  Duration(
                    days: (item?['visibleDays'] as num? ?? 7).toInt() - 1,
                  ),
                )
                .toIso8601String()
                .substring(0, 10),
      );
      branches
        ..clear()
        ..addAll((item?['targetBranchIds'] as List? ?? []).cast<String>());
      roles
        ..clear()
        ..addAll(
          (item?['audienceRoles'] as List? ?? contentRoles.keys.toList())
              .cast<String>(),
        );
      frozen = null;
      uncertain = false;
      reviewing = false;
      allBranches = branches.isEmpty && o['allBranchesAllowed'] == true;
      imageId = item?['imageAssetId'];
      imagePath = item?['imageUrl'];
      published = item == null || item['publishedAtUtc'] != null;
    });
  });
  Future<void> upload() => run(() async {
    final asset = await pickAndUpload(auth, {
      'purpose': 'News',
    }, imageOnly: true);
    if (asset != null && current) {
      setState(() {
        imageId = textOf(asset['id']);
        imagePath = textOf(asset['url']);
      });
    }
  });
  bool matches(Json read, Json sent) =>
      read['title'] == sent['title'] &&
      read['body'] == sent['body'] &&
      read['kind'] == sent['kind'] &&
      read['imageAssetId'] == sent['imageAssetId'] &&
      read['isActive'] == true &&
      read['localStartsOn'] == sent['localStartsOn'] &&
      read['localEndsOn'] == sent['localEndsOn'] &&
      (read['publishedAtUtc'] != null) == sent['publish'] &&
      _same(read['targetBranchIds'], sent['targetBranchIds']) &&
      _same(read['audienceRoles'], sent['audienceRoles']);
  bool _same(Object? a, Object? b) {
    final x = (a as List).cast<String>().toList()..sort(),
        y = (b as List).cast<String>().toList()..sort();
    return x.join('|') == y.join('|');
  }

  void review() {
    FocusManager.instance.primaryFocus?.unfocus();
    if (!form.currentState!.validate() ||
        roles.isEmpty ||
        !allBranches && branches.isEmpty) {
      setState(() => error = 'أكمل العنوان والتفاصيل واختر الجمهور والفروع.');
      return;
    }
    final start = DateTime.tryParse(from.text),
        end = DateTime.tryParse(to.text);
    if (start == null ||
        end == null ||
        end.isBefore(start) ||
        end.difference(start).inDays >= 90) {
      setState(() => error = 'اختر فترة ظهور صحيحة حتى90 يومًا.');
      return;
    }
    frozen ??= {
      'title': title.text.trim(),
      'body': body.text.trim(),
      'kind': kind,
      'imageAssetId': imageId,
      'visibleDays': end.difference(start).inDays + 1,
      'publish': published,
      'isActive': true,
      'targetBranchIds': allBranches ? <String>[] : (branches.toList()..sort()),
      'audienceRoles': roles.toList()..sort(),
      'localStartsOn': from.text,
      'localEndsOn': to.text,
      'requestId': requestId,
      if (saved != null) 'expectedVersion': saved!['version'],
    };
    setState(() => reviewing = true);
  }

  Future<void> save({bool publish = true}) => run(() async {
    if (frozen == null) return;
    if (!uncertain) {
      published = publish;
      frozen = {...frozen!, 'publish': publish};
    }
    var written = false;
    try {
      // If an update response was lost, read the same record first. A creation
      // retry keeps its original request ID/body and the server returns one row.
      Json? read;
      if (uncertain && saved != null) {
        final value = obj(
          await auth.request('GET', '/api/v1/content/news/${saved!['id']}'),
        );
        if (matches(value, frozen!)) read = value;
      }
      if (read == null) {
        final response = obj(
          await auth.request(
            saved == null ? 'POST' : 'PUT',
            '/api/v1/content/news/${saved?['id'] ?? ''}',
            body: frozen,
          ),
        );
        written = true;
        if (current) setState(() => saved = response);
        read = obj(
          await auth.request('GET', '/api/v1/content/news/${response['id']}'),
        );
      }
      if (!matches(read, frozen!)) {
        throw const ApiFailure(
          0,
          'لم تتأكد قراءة المحتوى المحفوظ. راجع نفس الطلب قبل محاولة أخرى.',
        );
      }
      if (mounted && current) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: UiText(
              published
                  ? 'حُفظ النشر والجمهور والفترة.'
                  : 'حُفظ المحتوى دون نشر.',
            ),
          ),
        );
        Navigator.pop(context, true);
      }
    } catch (e) {
      if (current) {
        setState(() {
          uncertain =
              written || e is! ApiFailure || e.status == 0 || e.status >= 500;
          if (!uncertain) frozen = null;
        });
      }
      rethrow;
    }
  });
  @override
  Widget build(
    BuildContext context,
  ) => page(saved == null ? 'إضافة محتوى' : 'تعديل المحتوى', [
    if (options != null && !reviewing) ...[
      const UiText('الخطوة 1 من 2 · البيانات'),
      AbsorbPointer(
        absorbing: uncertain,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const UiText('النوع'),
            Wrap(
              spacing: 8,
              children: [
                for (final e in contentKindNames.entries)
                  ChoiceChip(
                    label: UiText(e.value),
                    selected: kind == e.key,
                    onSelected: (_) => setState(() => kind = e.key),
                  ),
              ],
            ),
            field('العنوان', title),
            if (imagePath != null)
              PrivateImage(
                auth: auth,
                path: imagePath!,
                height: 160,
                fit: BoxFit.contain,
              ),
            OutlinedButton.icon(
              onPressed: upload,
              icon: const Icon(Icons.add_photo_alternate_outlined),
              label: UiText(imageId == null ? 'رفع صورة' : 'تغيير الصورة'),
            ),
            field('المحتوى', body, maxLines: 5),
            dateField('بداية الظهور', from),
            dateField('نهاية الظهور', to),
            const Heading('الجمهور المستهدف'),
            Wrap(
              spacing: 8,
              children: [
                for (final e in contentRoles.entries)
                  FilterChip(
                    label: UiText(e.value),
                    selected: roles.contains(e.key),
                    onSelected: (v) => setState(
                      () => v ? roles.add(e.key) : roles.remove(e.key),
                    ),
                  ),
                FilterChip(
                  label: const UiText('الكل'),
                  selected: roles.length == contentRoles.length,
                  onSelected: (v) => setState(() {
                    roles.clear();
                    if (v) roles.addAll(contentRoles.keys);
                  }),
                ),
              ],
            ),
            const Heading('الفروع المستهدفة'),
            Wrap(
              spacing: 8,
              children: [
                if (options!['allBranchesAllowed'] == true)
                  FilterChip(
                    label: const UiText('كل الفروع'),
                    selected: allBranches,
                    onSelected: (v) => setState(() => allBranches = v),
                  ),
                for (final branch in rows(options!['branches']))
                  FilterChip(
                    label: UiText(textOf(branch['name'])),
                    selected: !allBranches && branches.contains(branch['id']),
                    onSelected: (v) => setState(() {
                      allBranches = false;
                      v
                          ? branches.add(textOf(branch['id']))
                          : branches.remove(branch['id']);
                    }),
                  ),
              ],
            ),
          ],
        ),
      ),
      FilledButton(onPressed: review, child: const UiText('معاينة')),
    ],
    if (options != null && reviewing) ...[
      const UiText('الخطوة 2 من 2 · المراجعة والتأكيد'),
      if (imagePath != null)
        PrivateImage(
          auth: auth,
          path: imagePath!,
          height: 160,
          fit: BoxFit.contain,
        ),
      Heading(title.text.trim()),
      UiText(body.text.trim()),
      UiText('النوع: ${contentKindNames[kind]}'),
      UiText('الفترة: ${from.text} — ${to.text}'),
      UiText('الجمهور: ${roles.map((r) => contentRoles[r]).join('، ')}'),
      UiText(
        'الفروع: ${allBranches ? 'كل الفروع' : rows(options!['branches']).where((b) => branches.contains(b['id'])).map((b) => b['name']).join('، ')}',
      ),
      UiText(
        'الحالة بعد النشر: ${from.text.compareTo(textOf(options!['today'])) > 0
            ? 'مجدول'
            : to.text.compareTo(textOf(options!['today'])) < 0
            ? 'انتهى عرضه'
            : 'يعرض الآن'}',
      ),
      const UiText('يظهر للجمهور المحدد خلال فترة الظهور فقط.'),
      if (uncertain)
        const UiText(
          'لم تتأكد نتيجة الحفظ. التفاصيل ثابتة؛ أعد التحقق من نفس الطلب.',
        ),
      if (error != null && !uncertain && saved != null)
        OutlinedButton(
          onPressed: load,
          child: const UiText('إعادة قراءة النسخة المحفوظة'),
        ),
      if (!uncertain)
        TextButton(
          onPressed: () => setState(() {
            reviewing = false;
            frozen = null;
          }),
          child: const UiText('تعديل البيانات'),
        ),
      FilledButton(
        onPressed: frozen == null ? null : () => save(publish: true),
        child: UiText(uncertain ? 'التحقق من نفس الطلب' : 'نشر'),
      ),
      if (!uncertain)
        TextButton(
          onPressed: () => save(publish: false),
          child: const UiText('حفظ دون نشر'),
        ),
    ],
  ], screen: 'more');
}
