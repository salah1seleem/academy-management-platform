import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../guardian/guardian_ui.dart';
import 'player_skills.dart';
import 'player_badges.dart';
import 'player_certificates.dart';

class PlayerMonthlyPage extends StatefulWidget {
  final AuthController auth;
  final String? playerId, enrollmentId;
  const PlayerMonthlyPage({
    super.key,
    required this.auth,
    this.playerId,
    this.enrollmentId,
  });
  @override
  State<PlayerMonthlyPage> createState() => _MonthlyState();
}

class _MonthlyState extends State<PlayerMonthlyPage>
    with WidgetsBindingObserver {
  String? month, selected;
  int page = 0, revision = 0;
  bool foreground = true, busy = false;
  String message = '';
  String? get identity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final original = identity;
  String get base => widget.playerId != null
      ? '/api/v1/guardian/children/${widget.playerId}/monthly'
      : '/api/v1/evaluations/monthly/enrollments/${widget.enrollmentId}';
  String path({String? format, Json? data}) =>
      '$base?${Uri(queryParameters: {'page': '$page', 'month': ?month, 'enrollmentId': ?selected, 'format': ?format, if (data != null) 'month': textOf(data['month']), if (data != null) 'enrollmentId': textOf(obj(data['enrollment'])['id'])}).query}';
  @override
  void initState() {
    super.initState();
    selected = widget.enrollmentId;
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState s) {
    setState(() {
      foreground = s == AppLifecycleState.resumed;
      revision++;
      message = '';
    });
  }

  String state(String value) => localized(
    context,
    {
          'Mastered': 'أتقن',
          'Learning': 'قيد التعلم',
          'NotAssessed': 'لم يقيّم',
          'Granted': 'شارة ممنوحة',
          'Revoked': 'شارة مسحوبة',
          'Issued': 'شهادة صادرة',
          'Withdrawn': 'شهادة مسحوبة',
        }[value] ??
        'راجع المصدر',
    {
          'Mastered': 'Mastered',
          'Learning': 'Learning',
          'NotAssessed': 'Not assessed',
          'Granted': 'Badge granted',
          'Revoked': 'Badge revoked',
          'Issued': 'Certificate issued',
          'Withdrawn': 'Certificate withdrawn',
        }[value] ??
        'Review source',
  );
  String unit(String value) => localized(
    context,
    {
          'Checklist': 'قائمة تحقق',
          'Count': 'عدد',
          'Seconds': 'ثانية',
          'Metres': 'متر',
          'Encouragement': 'نقاط تشجيعية غير مالية',
        }[value] ??
        'قياس — راجع المصدر',
    {
          'Checklist': 'Checklist',
          'Count': 'Count',
          'Seconds': 'Seconds',
          'Metres': 'Metres',
          'Encouragement': 'Nonfinancial encouragement points',
        }[value] ??
        'Measurement — review source',
  );
  Future<void> copy(Json d) async {
    if (busy) return;
    setState(() {
      busy = true;
      message = '';
    });
    try {
      final r = obj(
        await widget.auth.request('GET', path(format: 'text', data: d)),
      );
      if (!mounted || identity != original || !foreground) return;
      final text = r['text'];
      if (text is! String || text.isEmpty) {
        throw StateError('Unconfirmed export');
      }
      await Clipboard.setData(ClipboardData(text: text));
      if (mounted && identity == original && foreground) {
        setState(
          () => message = localized(
            context,
            'تم نسخ تقرير الشهر. الحافظة تحتوي بيانات خاصة بطفلك؛ لا تشاركها دون حاجة.',
            'Month copied. Clipboard contains your child’s private data; share carefully.',
          ),
        );
      }
    } catch (_) {
      if (mounted && identity == original && foreground) {
        setState(
          () => message = localized(
            context,
            'تعذر تأكيد التصدير؛ لم ننسخ تقريرًا ناقصًا. أعد مراجعة الصلاحية والشهر.',
            'Export not confirmed; no partial report copied. Recheck access and month.',
          ),
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
        title: localized(context, 'تقرير التطور الشهري', 'Monthly development'),
        child: RemoteBody(
          key: ValueKey(revision),
          auth: widget.auth,
          path: path(),
          builder: (context, value) {
            final d = obj(value),
                e = obj(d['enrollment']),
                counts = obj(d['counts']);
            return gList([
              GCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const Icon(Icons.auto_graph_rounded),
                    Text(
                      textOf(e['player']),
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                    Text('${e['sport']} · ${d['month']}'),
                    Text('${d['from']} — ${d['to']}'),
                    Text(
                      localized(
                        context,
                        'قراءة حية للأدلة، لا قوة لاعب أو توقع احتراف.',
                        'Live evidence, not player power or predicted success.',
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
                      child: const Icon(Icons.chevron_right),
                    ),
                  ),
                  Expanded(child: Center(child: Text(textOf(d['month'])))),
                  Expanded(
                    child: OutlinedButton(
                      onPressed:
                          busy ||
                              textOf(d['month']) ==
                                  textOf(d['asOfDate']).substring(0, 7)
                          ? null
                          : () => shift(d, 1),
                      child: const Icon(Icons.chevron_left),
                    ),
                  ),
                ],
              ),
              if (widget.playerId != null)
                DropdownButtonFormField<String>(
                  initialValue: textOf(e['id']),
                  isExpanded: true,
                  decoration: InputDecoration(
                    labelText: localized(
                      context,
                      'التسجيل الرياضي',
                      'Sport enrollment',
                    ),
                  ),
                  items: rows(d['enrollments'])
                      .map(
                        (x) => DropdownMenuItem(
                          value: textOf(x['id']),
                          child: Text(
                            '${x['sport']} · ${x['branch']}',
                            overflow: TextOverflow.ellipsis,
                          ),
                        ),
                      )
                      .toList(),
                  onChanged: busy
                      ? null
                      : (v) => setState(() {
                          selected = v;
                          page = 0;
                          message = '';
                        }),
                ),
              Text(
                textOf(d['notice']),
                style: Theme.of(context).textTheme.bodySmall,
              ),
              GCard(
                child: Wrap(
                  spacing: 18,
                  runSpacing: 12,
                  children: [
                    Text('${counts['skills']} مراجعة مهارة'),
                    Text('${counts['badges']} شارة مسجلة'),
                    Text('${counts['certificates']} شهادة مسجلة'),
                  ],
                ),
              ),
              OutlinedButton.icon(
                onPressed: busy ? null : () => copy(d),
                icon: const Icon(Icons.copy_all_outlined),
                label: Text(
                  localized(
                    context,
                    'نسخ تقرير الشهر كاملًا',
                    'Copy full month',
                  ),
                ),
              ),
              Text(
                localized(
                  context,
                  'التصدير يعيد فحص الصلاحية؛ لا يسترجع سحب لاحق نسخة نُسخت سابقًا.',
                  'Export rechecks access; later withdrawal cannot recall a copied report.',
                ),
                style: Theme.of(context).textTheme.bodySmall,
              ),
              if (message.isNotEmpty) Text(message),
              if (rows(d['items']).isEmpty)
                Text(
                  localized(
                    context,
                    'لا توجد أدلة في هذه الصفحة؛ هذا ليس حكمًا على قدرات الطفل.',
                    'No evidence on this page; this is not a judgement of ability.',
                  ),
                ),
              for (final r in rows(d['items']))
                GCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Text(
                        textOf(r['title']),
                        style: Theme.of(context).textTheme.titleMedium,
                      ),
                      Text('${r['on']} · ${state(textOf(r['state']))}'),
                      if (r['value'] != null)
                        Text('${r['value']} ${unit(textOf(r['unit']))}'),
                      if (r['note'] != null) Text(textOf(r['note'])),
                      Text('سجلها: ${r['actor']}'),
                      TextButton(
                        onPressed: () => openPage(
                          context,
                          r['kind'] == 'Skill'
                              ? PlayerSkillsPage(
                                  auth: widget.auth,
                                  playerId: widget.playerId,
                                  planId: textOf(r['planId']),
                                )
                              : r['kind'] == 'Certificate'
                              ? PlayerCertificatesPage(
                                  auth: widget.auth,
                                  playerId: widget.playerId,
                                  id: textOf(r['id']),
                                )
                              : PlayerBadgesPage(
                                  auth: widget.auth,
                                  playerId: widget.playerId,
                                  enrollmentId: textOf(e['id']),
                                ),
                        ),
                        child: Text(
                          localized(
                            context,
                            'فتح المصدر وسجله',
                            'Open source and history',
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
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
                      child: Text(
                        localized(context, 'الصفحة السابقة', 'Previous page'),
                      ),
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
                      child: Text(
                        localized(context, 'الصفحة التالية', 'Next page'),
                      ),
                    ),
                  ),
                ],
              ),
              Text(
                localized(
                  context,
                  'حتى25 دليلًا من كل نوع لكل صفحة. لا يجمع التقرير الصور أو الحضور أو المال.',
                  'Up to25 per kind per page. No photos, attendance or finances in this report.',
                ),
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ]);
          },
        ),
      );
    },
  );
  void shift(Json d, int amount) {
    final date = DateTime.parse('${d['month']}-01');
    final next = DateTime(date.year, date.month + amount);
    if (next.year < 1900) return;
    setState(() {
      month = '${next.year}-${next.month.toString().padLeft(2, '0')}';
      page = 0;
      message = '';
    });
  }
}
