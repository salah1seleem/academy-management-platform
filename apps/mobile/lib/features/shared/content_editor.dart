import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/api/api_client.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../owner/owner_followups.dart';
import 'private_media.dart';
import 'experience_ui.dart';

class AcademyContentEditor extends StatefulWidget {
  const AcademyContentEditor({
    super.key,
    required this.auth,
    this.ad = false,
    this.news,
  });
  final AuthController auth;
  final bool ad;
  final Json? news;
  @override
  State<AcademyContentEditor> createState() => _AcademyContentEditorState();
}

class _AcademyContentEditorState extends State<AcademyContentEditor> {
  late final title = TextEditingController(text: widget.news?['title'] ?? ''),
      body = TextEditingController(text: widget.news?['body'] ?? '');
  final days = TextEditingController(text: '7');
  final branches = <String>{};
  String start = isoDay(DateTime.now()),
      end = isoDay(DateTime.now().add(const Duration(days: 7)));
  String? asset, path, error;
  bool busy = false;
  final request = followUpRequestId();
  @override
  void initState() {
    super.initState();
    path = widget.news?['imageUrl'];
    if (isPrivateAsset(path)) asset = path!.split('/').last;
    days.text = '${widget.news?['visibleDays'] ?? 7}';
  }

  @override
  void dispose() {
    title.dispose();
    body.dispose();
    days.dispose();
    super.dispose();
  }

  Future<void> upload() async {
    if (busy) return;
    setState(() {
      busy = true;
      error = null;
    });
    try {
      final file = await pickAndUpload(widget.auth, {
        'purpose': widget.ad ? 'Campaign' : 'News',
      }, imageOnly: true);
      if (file != null && mounted) {
        setState(() {
          asset = textOf(file['id']);
          path = textOf(file['url']);
        });
      }
    } catch (e) {
      if (mounted) {
        setState(
          () => error = e is ApiFailure ? e.message : 'تعذر رفع الصورة.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  Future<void> save(bool publish) async {
    if (title.text.trim().isEmpty ||
        body.text.trim().isEmpty ||
        widget.ad && branches.isEmpty) {
      setState(() => error = 'أدخل العنوان والتفاصيل واختر الفروع للإعلان.');
      return;
    }
    if (widget.ad && (asset == null || start.compareTo(end) > 0)) {
      setState(() => error = 'ارفع صورة الإعلان وحدد بداية ونهاية صحيحتين.');
      return;
    }
    final visible = int.tryParse(days.text);
    if (!widget.ad && (visible == null || visible < 1 || visible > 90)) {
      setState(() => error = 'مدة الخبر بين يوم و90 يومًا.');
      return;
    }
    setState(() {
      busy = true;
      error = null;
    });
    try {
      if (widget.ad) {
        await widget.auth.request(
          'POST',
          '/api/v1/manage/community-campaigns/',
          body: {
            'internalName': title.text.trim(),
            'advertiser': 'الأكاديمية',
            'title': title.text.trim(),
            'body': body.text.trim(),
            'buttonText': 'عرض التفاصيل',
            'imageAlt': title.text.trim(),
            'startsAtUtc': '${start}T00:00:00Z',
            'endsAtUtc': '${end}T23:59:59Z',
            'localStartsOn': start,
            'localEndsOn': end,
            'priority': 0,
            'branchIds': branches.toList(),
            'requestId': request,
            'imageAssetId': asset,
          },
        );
      } else {
        await widget.auth.request(
          widget.news == null ? 'POST' : 'PUT',
          '/api/v1/content/news/${widget.news?['id'] ?? ''}',
          body: {
            'title': title.text.trim(),
            'body': body.text.trim(),
            'imageAssetId': asset,
            'visibleDays': visible,
            'publish': publish,
            'isActive': true,
          },
        );
      }
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: UiText(
              widget.ad
                  ? 'حُفظ الإعلان كمسودة؛ راجع موافقات الفروع.'
                  : publish
                  ? 'تم نشر الخبر.'
                  : 'حُفظ الخبر كمسودة.',
            ),
          ),
        );
        Navigator.pop(context, true);
      }
    } catch (e) {
      if (mounted) {
        setState(
          () => error = e is ApiFailure ? e.message : 'تعذر حفظ المحتوى.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: widget.ad
        ? 'إضافة إعلان'
        : widget.news == null
        ? 'إضافة خبر'
        : 'تعديل الخبر',
    child: gList([
      TextField(
        controller: title,
        maxLength: widget.ad ? 100 : 180,
        enabled: !busy,
        decoration: const InputDecoration(labelText: 'العنوان'),
      ),
      TextField(
        controller: body,
        maxLines: 5,
        maxLength: widget.ad ? 1000 : 10000,
        enabled: !busy,
        decoration: const InputDecoration(labelText: 'التفاصيل'),
      ),
      const SizedBox(height: 12),
      if (path != null)
        PrivateImage(
          auth: widget.auth,
          path: path!,
          height: 180,
          fit: BoxFit.contain,
        ),
      OutlinedButton.icon(
        onPressed: busy ? null : upload,
        icon: const Icon(Icons.add_photo_alternate_outlined),
        label: UiText(asset == null ? 'رفع صورة' : 'تغيير الصورة'),
      ),
      if (widget.ad) ...[
        const SizedBox(height: 12),
        ScopeDateTiles(
          from: start,
          to: end,
          onChanged: (a, b) => setState(() {
            start = a;
            end = b;
          }),
        ),
        RemoteBody(
          auth: widget.auth,
          path: '/api/v1/manage/community-campaigns/options',
          builder: (context, value) => Column(
            children: [
              const Heading('الفروع المستهدفة'),
              for (final b in rows(obj(value)['branches']))
                CheckboxListTile(
                  value: branches.contains(b['id']),
                  title: UiText(textOf(b['name'])),
                  onChanged: busy
                      ? null
                      : (v) => setState(() {
                          v == true
                              ? branches.add(textOf(b['id']))
                              : branches.remove(b['id']);
                        }),
                ),
            ],
          ),
        ),
        const UiText(
          'حفظ المسودة لا ينشر الإعلان. يلزم تقديمها للمراجعة واعتماد الفروع المختارة.',
          style: TextStyle(fontSize: 11),
        ),
      ] else
        TextField(
          controller: days,
          keyboardType: TextInputType.number,
          enabled: !busy,
          decoration: const InputDecoration(labelText: 'مدة الظهور بالأيام'),
        ),
      if (error != null) UiText(error!),
      const SizedBox(height: 16),
      FilledButton(
        onPressed: busy ? null : () => save(false),
        child: UiText(widget.ad ? 'حفظ مسودة الإعلان' : 'حفظ كمسودة'),
      ),
      if (!widget.ad)
        TextButton(
          onPressed: busy ? null : () => save(true),
          child: const UiText('نشر الخبر'),
        ),
    ]),
  );
}

class AcademyNewsManagement extends StatefulWidget {
  const AcademyNewsManagement({super.key, required this.auth});
  final AuthController auth;
  @override
  State<AcademyNewsManagement> createState() => _AcademyNewsManagementState();
}

class _AcademyNewsManagementState extends State<AcademyNewsManagement> {
  int revision = 0;
  Future<void> edit([Json? item]) async {
    final saved = await openPage<bool>(
      context,
      AcademyContentEditor(auth: widget.auth, news: item),
    );
    if (saved == true && mounted) setState(() => revision++);
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'الأخبار',
    child: RemoteBody(
      key: ValueKey(revision),
      auth: widget.auth,
      path: '/api/v1/content/news',
      builder: (context, value) => gList([
        FilledButton.icon(
          onPressed: () => edit(),
          icon: const Icon(Icons.add),
          label: const UiText('إضافة خبر'),
        ),
        const SizedBox(height: 14),
        if (rows(value).isEmpty) const EmptyMessage('لا توجد أخبار.'),
        for (final n in rows(value))
          GCard(
            onTap: () => edit(n),
            child: ListTile(
              contentPadding: EdgeInsets.zero,
              title: UiText(textOf(n['title'])),
              subtitle: UiText(
                n['publishedAtUtc'] == null
                    ? 'مسودة'
                    : 'منشور · ${day(n['publishedAtUtc'])}',
                style: const TextStyle(fontSize: 11),
              ),
              trailing: const Icon(Icons.chevron_left),
            ),
          ),
      ]),
    ),
  );
}
