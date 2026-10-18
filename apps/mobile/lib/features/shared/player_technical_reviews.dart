import 'dart:convert';

import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../guardian/guardian_ui.dart';
import 'player_goals.dart' show goalCommandId;

class PlayerTechnicalReviewsPage extends StatefulWidget {
  final AuthController auth;
  final String? planId, playerId, requestId;
  const PlayerTechnicalReviewsPage({
    super.key,
    required this.auth,
    this.planId,
    this.playerId,
    this.requestId,
  });
  @override
  State<PlayerTechnicalReviewsPage> createState() =>
      _PlayerTechnicalReviewsState();
}

class _PlayerTechnicalReviewsState extends State<PlayerTechnicalReviewsPage>
    with WidgetsBindingObserver {
  static const root = '/api/v1/evaluations/technical-reviews';
  String? get identity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final original = identity;
  int revision = 0;
  bool foreground = true, busy = false, all = false;
  String message = '', outcome = 'NeedsPractice';
  String? recommended, signature, command;
  final note = TextEditingController();
  Json map(Object? v) => obj(v ?? <String, dynamic>{});
  List<Json> list(Object? v) => rows(v ?? <Json>[]);
  String l(String ar, String en) => localized(context, ar, en);
  String get path => widget.requestId != null
      ? '$root/${widget.requestId}'
      : widget.planId != null
      ? widget.playerId != null
            ? '/api/v1/guardian/children/${widget.playerId}/skills/plans/${widget.planId}/technical-reviews'
            : '/api/v1/evaluations/skills/plans/${widget.planId}/technical-reviews'
      : '$root/inbox?includeDecided=$all';
  String label(Object? value) => switch (value) {
    'ReadyForPlacement' => l(
      'مستعد لمراجعة التسكين',
      'Ready for placement review',
    ),
    'NeedsPractice' => l('يحتاج مزيدًا من التدريب', 'Needs more practice'),
    'ContextChanged' => l(
      'أغلق لتغير الخطة أو الأدلة',
      'Closed: plan or evidence changed',
    ),
    _ => l('بانتظار مراجعة فنية بشرية', 'Awaiting human technical review'),
  };
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    note.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState s) {
    setState(() {
      foreground = s == AppLifecycleState.resumed;
      revision++;
      recommended = null;
      outcome = 'NeedsPractice';
      note.clear();
      message = '';
    });
  }

  Future<void> save(String url, Json body) async {
    if (busy) return;
    final encoded = jsonEncode({'path': url, 'body': body});
    if (signature != encoded) {
      signature = encoded;
      command = goalCommandId();
    }
    setState(() {
      busy = true;
      message = '';
    });
    try {
      await widget.auth.request(
        'POST',
        url,
        body: {...body, 'requestId': command},
      );
      if (!mounted || identity != original) return;
      signature = null;
      command = null;
      setState(() {
        revision++;
        note.clear();
        recommended = null;
        outcome = 'NeedsPractice';
        message = l(
          'تم حفظ المراجعة؛ لم يتغير مستوى اللاعب أو مجموعته.',
          'Review saved; player level and group are unchanged.',
        );
      });
    } catch (e) {
      if (mounted && identity == original) {
        setState(
          () => message = e is ApiFailure
              ? e.message
              : l(
                  'تعذر الحفظ. لم نسجل نجاحًا.',
                  'Could not save. No success was recorded.',
                ),
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  void open(String id) => openPage(
    context,
    PlayerTechnicalReviewsPage(auth: widget.auth, requestId: id),
  );
  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (!foreground || identity == null || identity != original) {
        return const SizedBox.shrink();
      }
      return GuardianPage(
        title: widget.planId == null && widget.requestId == null
            ? l('طلبات المراجعة الفنية', 'Technical review inbox')
            : l('المراجعة الفنية للمستوى', 'Level technical review'),
        child: RemoteBody(
          key: ValueKey('$identity:$revision:$all'),
          auth: widget.auth,
          path: path,
          builder: (context, value) {
            final d = map(value);
            return gList([
              Text(
                l(
                  'قرار فني بشري بأدلة محفوظة. لا ترقية أو نقل أو شهادة تلقائية.',
                  'A human decision with preserved evidence. No automatic promotion, transfer or certificate.',
                ),
              ),
              if (message.isNotEmpty) Text(message),
              if (widget.requestId != null) ...detail(d),
              if (widget.planId != null) ...plan(d),
              if (widget.requestId == null && widget.planId == null)
                ...inbox(d),
            ]);
          },
        ),
      );
    },
  );
  List<Widget> plan(Json d) {
    final e = map(d['enrollment']), s = map(d['source']), p = map(d['plan']);
    return [
      GCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              '${e['player']} · ${e['sport']}',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            const SizedBox(height: 12),
            Text(
              '${s['mastered']}/${s['requirements']} ${l('متطلبات متقنة', 'requirements mastered')}',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 12),
            if (widget.playerId == null && d['canRequest'] == true)
              FilledButton.icon(
                onPressed: busy
                    ? null
                    : () => save(path, {
                        'planVersion': p['version'],
                        'sourceReviewSequence': s['head'],
                        'previousRequestId': d['latestRequestId'],
                      }),
                icon: const Icon(Icons.fact_check_outlined),
                label: Text(
                  l(
                    'طلب مراجعة فنية للمستوى',
                    'Request level technical review',
                  ),
                ),
              ),
            if (widget.playerId == null && d['canRequest'] != true)
              Text(
                l(
                  'يلزم استيفاء المتطلبات الحالية وإغلاق السابق ودليل أحدث قبل طلب آخر.',
                  'Meet current requirements, close the previous request and record newer evidence before requesting again.',
                ),
              ),
          ],
        ),
      ),
      if (list(d['items']).isEmpty)
        Text(
          l('لا توجد طلبات لهذه الخطة بعد.', 'No requests for this plan yet.'),
        ),
      for (final entry in list(d['items'])) entryCard(entry, s),
      if (d['hasMore'] == true)
        Text(l('تظهر أحدث20 مراجعة.', 'Showing the latest20 reviews.')),
    ];
  }

  Widget entryCard(Json entry, Json? source) {
    final r = map(entry['request']), d = map(entry['decision']);
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            '${l('مراجعة', 'Review')} ${r['sequence']} · ${textOf(r['levelName'])}',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 8),
          Text(
            '${r['requirements']} ${l('متطلبات بأدلة عند الطلب', 'requirements evidenced when requested')}',
          ),
          Text('${textOf(r['actorName'])} · ${day(r['createdAtUtc'])}'),
          const SizedBox(height: 12),
          decision(d),
          if (source != null &&
              (source['eligible'] != true ||
                  source['head'] != r['sourceReviewSequence']))
            Text(
              l(
                'سجل تاريخي؛ الأدلة أو سياق الخطة تغيرا منذ الطلب.',
                'Historical record; evidence or plan context has changed since this request.',
              ),
            ),
          if (widget.playerId == null)
            GLink(
              l('الأدلة وقرار المراجعة', 'Evidence and review decision'),
              Icons.fact_check_outlined,
              () => open(textOf(r['id'])),
            ),
        ],
      ),
    );
  }

  Widget decision(Json d) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(
        label(d['outcome']),
        style: const TextStyle(fontWeight: FontWeight.bold),
      ),
      if (d.isNotEmpty) ...[
        Text('${textOf(d['actorName'])} · ${day(d['createdAtUtc'])}'),
        if (d['recommendedLevelName'] != null)
          Text(
            '${l('المستوى الموصى به', 'Recommended level')}: ${d['recommendedLevelName']}',
          ),
        if (d['familyNote'] != null) Text(textOf(d['familyNote'])),
      ],
    ],
  );
  List<Widget> inbox(Json d) {
    final contexts = map(d['contexts']);
    return [
      SwitchListTile(
        title: Text(l('إظهار ما صدر قراره أيضًا', 'Include decided requests')),
        value: all,
        onChanged: (v) => setState(() => all = v),
      ),
      if (list(d['items']).isEmpty)
        Text(l('لا توجد طلبات ضمن نطاقك.', 'No requests within your scope.')),
      for (final entry in list(d['items'])) ...[
        Text(
          '${textOf(map(contexts[textOf(map(entry['request'])['sportEnrollmentId'])])['player'])} · ${textOf(map(contexts[textOf(map(entry['request'])['sportEnrollmentId'])])['branch'])}',
          style: Theme.of(context).textTheme.titleMedium,
        ),
        entryCard(entry, null),
      ],
      if (d['hasMore'] == true)
        Text(
          l('تظهر أول100 طلب حسب الأقدم.', 'Showing the oldest100 requests.'),
        ),
    ];
  }

  List<Widget> detail(Json d) {
    final r = map(d['request']),
        e = map(d['enrollment']),
        dec = map(d['decision']);
    final levels = list(d['levels']);
    final choices = <String>[
      'NeedsPractice',
      if (d['currentEligible'] == true) 'ReadyForPlacement',
      if (d['contextChanged'] == true) 'ContextChanged',
    ];
    final chosen = choices.contains(outcome) ? outcome : 'NeedsPractice';
    return [
      GCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              '${e['player']} · ${r['levelName']}',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            Text(textOf(r['curriculumTitle'])),
            const SizedBox(height: 12),
            decision(dec),
            if (d['contextChanged'] == true)
              Text(
                l(
                  'تغيرت الأدلة أو الخطة. لا يعتمد الاستعداد على الطلب القديم.',
                  'Evidence or plan changed. Do not approve readiness from this outdated request.',
                ),
              ),
          ],
        ),
      ),
      for (final v in list(d['evidence']))
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                textOf(v['name']),
                style: Theme.of(context).textTheme.titleMedium,
              ),
              Text(textOf(v['protocol'])),
              Text(
                '${l('أتقن', 'Mastered')}${v['measuredValue'] == null ? '' : ' · ${v['measuredValue']} ${unit(v['unit'])}'}',
              ),
              Text('${v['actorName']} · ${day(v['observedOn'])}'),
            ],
          ),
        ),
      if (d['canDecide'] == true)
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              DropdownButtonFormField<String>(
                key: ValueKey('outcome:$revision:$chosen'),
                initialValue: chosen,
                isExpanded: true,
                decoration: InputDecoration(
                  labelText: l('قرار المراجعة', 'Review decision'),
                ),
                items: choices
                    .map(
                      (x) => DropdownMenuItem(
                        value: x,
                        child: Text(label(x), overflow: TextOverflow.ellipsis),
                      ),
                    )
                    .toList(),
                onChanged: busy ? null : (v) => setState(() => outcome = v!),
              ),
              const SizedBox(height: 16),
              if (chosen == 'ReadyForPlacement')
                DropdownButtonFormField<String>(
                  key: ValueKey('level:$revision'),
                  initialValue: levels.any((x) => x['id'] == recommended)
                      ? recommended
                      : null,
                  isExpanded: true,
                  decoration: InputDecoration(
                    labelText: l('المستوى الموصى به', 'Recommended level'),
                  ),
                  items: levels
                      .map(
                        (x) => DropdownMenuItem(
                          value: textOf(x['id']),
                          child: Text(
                            textOf(x['name']),
                            overflow: TextOverflow.ellipsis,
                          ),
                        ),
                      )
                      .toList(),
                  onChanged: busy
                      ? null
                      : (v) => setState(() => recommended = v),
                ),
              const SizedBox(height: 16),
              TextField(
                controller: note,
                maxLength: 500,
                maxLines: 3,
                enabled: !busy,
                decoration: InputDecoration(
                  labelText: l(
                    'ملخص فني للأسرة (اختياري)',
                    'Technical family summary (optional)',
                  ),
                ),
              ),
              Text(
                l(
                  'الملخص يظهر للأسرة؛ لا تكتب معلومات صحية أو ملاحظات إدارية خاصة.',
                  'The summary is shared with the family. Do not include health or private administrative information.',
                ),
              ),
              const SizedBox(height: 12),
              FilledButton.icon(
                onPressed:
                    busy || chosen == 'ReadyForPlacement' && recommended == null
                    ? null
                    : () => save('$root/${widget.requestId}/decision', {
                        'version': r['version'],
                        'outcome': chosen,
                        'recommendedLevelId': chosen == 'ReadyForPlacement'
                            ? recommended
                            : null,
                        'familyNote': note.text.trim().isEmpty
                            ? null
                            : note.text.trim(),
                      }),
                icon: const Icon(Icons.verified_outlined),
                label: Text(
                  l(
                    'حفظ القرار ونشر الملخص',
                    'Save decision and share summary',
                  ),
                ),
              ),
            ],
          ),
        ),
    ];
  }

  String unit(Object? u) => switch (u) {
    'Count' => l('عدد', 'count'),
    'Seconds' => l('ثانية', 'seconds'),
    'Metres' => l('متر', 'metres'),
    _ => '',
  };
}
