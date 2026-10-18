import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../guardian/guardian_ui.dart';
import 'private_media.dart';

class MediaMemoriesPage extends StatefulWidget {
  final AuthController auth;
  final String? playerId, groupId;
  const MediaMemoriesPage({
    super.key,
    required this.auth,
    this.playerId,
    this.groupId,
  });
  @override
  State<MediaMemoriesPage> createState() => _MemoriesState();
}

class _MemoriesState extends State<MediaMemoriesPage>
    with WidgetsBindingObserver {
  String? month;
  int page = 0, revision = 0;
  bool foreground = true, busy = false;
  String message = '';
  String? get identity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final original = identity;
  String l(String ar, String en) => localized(context, ar, en);
  String path({String? format, String? chosenMonth}) =>
      '/api/v1/assets/memories?${Uri(queryParameters: {'playerId': ?widget.playerId, 'groupId': ?widget.groupId, 'month': ?(chosenMonth ?? month), 'page': '$page', 'format': ?format}).query}';
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    setState(() {
      foreground = state == AppLifecycleState.resumed;
      revision++;
      message = '';
    });
  }

  Future<void> copy(Json d) async {
    if (busy) return;
    setState(() {
      busy = true;
      message = '';
    });
    try {
      final r = obj(
        await widget.auth.request(
          'GET',
          path(format: 'text', chosenMonth: textOf(d['month'])),
        ),
      );
      if (!mounted || !foreground || identity != original) return;
      final text = r['text'];
      if (text is! String || text.isEmpty) {
        throw StateError('Unconfirmed index');
      }
      await Clipboard.setData(ClipboardData(text: text));
      if (mounted && foreground && identity == original) {
        setState(
          () => message = l(
            'تم نسخ فهرس الشهر دون الصور أو روابط مشاركة. الحافظة خاصة؛ لا تسترجع السحوبات نسخًا سابقة.',
            'Month index copied without files or sharing links. Keep the clipboard private; later withdrawals cannot recall it.',
          ),
        );
      }
    } catch (_) {
      if (mounted && foreground && identity == original) {
        setState(
          () => message = l(
            'لم نؤكد نسخ الفهرس. راجع الشهر والصلاحية؛ لم نصدر فهرسًا ناقصًا.',
            'Index copy not confirmed. Recheck month and access; no partial index exported.',
          ),
        );
      }
    } finally {
      if (mounted) {
        setState(() => busy = false);
      }
    }
  }

  void shift(Json d, int delta) {
    final old = DateTime.parse('${d['month']}-01'),
        next = DateTime(
          DateTime.parse('${d['month']}-01').year,
          DateTime.parse('${d['month']}-01').month + delta,
        );
    if (next.year < 1900 || next == old) return;
    setState(() {
      month = '${next.year}-${next.month.toString().padLeft(2, '0')}';
      page = 0;
      message = '';
    });
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (!foreground || identity == null || identity != original) {
        return const SizedBox.shrink();
      }
      return GuardianPage(
        title: l('ذكريات الشهر', 'Monthly memories'),
        child: RemoteBody(
          key: ValueKey(revision),
          auth: widget.auth,
          path: path(),
          builder: (context, value) {
            final d = obj(value);
            return gList([
              GCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const Icon(Icons.collections_outlined),
                    Text(
                      l('كل لحظة في مكانها', 'Every moment in its place'),
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                    Text('${d['month']} · ${d['from']} — ${d['to']}'),
                    Text(
                      l(
                        'المعرض الخاص نفسه، حسب تاريخ الواقعة لا تاريخ الرفع.',
                        'The same private gallery, by event date not upload date.',
                      ),
                    ),
                  ],
                ),
              ),
              Row(
                children: [
                  Expanded(
                    child: OutlinedButton(
                      onPressed: busy ? null : () => shift(d, -1),
                      child: Text(l('الشهر السابق', 'Previous month')),
                    ),
                  ),
                  const SizedBox(width: 8),
                  Expanded(
                    child: OutlinedButton(
                      onPressed:
                          busy ||
                              textOf(d['month']) ==
                                  textOf(d['asOfDate']).substring(0, 7)
                          ? null
                          : () => shift(d, 1),
                      child: Text(l('الشهر التالي', 'Next month')),
                    ),
                  ),
                ],
              ),
              GCard(
                child: ExpansionTile(
                  title: Text(
                    l(
                      'خصوصية الذكريات وحدود الفهرس',
                      'Privacy and index limits',
                    ),
                  ),
                  children: [
                    Padding(
                      padding: const EdgeInsets.all(12),
                      child: Text(textOf(d['notice'])),
                    ),
                  ],
                ),
              ),
              GCard(
                child: Wrap(
                  spacing: 16,
                  runSpacing: 8,
                  children: [
                    Text(
                      l(
                        '${d['total']} نسخة متاحة',
                        '${d['total']} available copies',
                      ),
                    ),
                    Text(l('${d['images']} صورة', '${d['images']} images')),
                    Text(l('${d['videos']} فيديو', '${d['videos']} videos')),
                  ],
                ),
              ),
              OutlinedButton.icon(
                onPressed: busy ? null : () => copy(d),
                icon: const Icon(Icons.copy_all_outlined),
                label: Text(
                  l(
                    'نسخ فهرس الشهر دون الملفات',
                    'Copy month index without files',
                  ),
                ),
              ),
              if (message.isNotEmpty) Text(message),
              if (rows(d['items']).isEmpty)
                Text(
                  l(
                    'لا توجد ذكريات متاحة في هذه الصفحة. قد يتغير العرض مع الموافقة أو السحب أو انتهاء الاحتفاظ.',
                    'No available memories on this page. Consent, withdrawal and retention may change availability.',
                  ),
                ),
              for (final item in rows(d['items']))
                MediaTile(auth: widget.auth, media: item),
              Row(
                children: [
                  Expanded(
                    child: TextButton(
                      onPressed: page == 0
                          ? null
                          : () => setState(() {
                              page--;
                              message = '';
                            }),
                      child: Text(l('الصفحة السابقة', 'Previous page')),
                    ),
                  ),
                  Text('${page + 1}'),
                  Expanded(
                    child: TextButton(
                      onPressed: d['hasMore'] != true
                          ? null
                          : () => setState(() {
                              page++;
                              message = '';
                            }),
                      child: Text(l('الصفحة التالية', 'Next page')),
                    ),
                  ),
                ],
              ),
              Text(
                l(
                  'حتى25 نسخة لكل صفحة. الفهرس وصفي فقط؛ ليس ألبومًا عامًا أو إذن نشر خارجي أو نسخة احتياطية.',
                  'Up to25 copies per page. The index has metadata only; not a public album, redistribution permission or backup.',
                ),
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ]);
          },
        ),
      );
    },
  );
}
