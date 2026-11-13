import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../guardian/guardian_ui.dart';
import 'player_goals.dart' show goalCommandId;
import 'technical_promotion.dart';

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
  bool foreground = true, busy = false, all = false, needsRead = false;
  String message = '', error = '';
  String? outcome, recommended, signature, command;
  bool get locked => busy || needsRead;
  bool get isInbox => widget.planId == null && widget.requestId == null;
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
      'أُغلق لتغيّر الخطة أو الأدلة',
      'Closed: plan or evidence changed',
    ),
    _ => l('بانتظار القرار الفني', 'Awaiting technical decision'),
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
      outcome = null;
      note.clear();
      message = '';
    });
  }

  Future<void> save(String url, Json body) async {
    if (locked) return;
    final encoded = jsonEncode({'path': url, 'body': body});
    if (signature != encoded) {
      signature = encoded;
      command = goalCommandId();
    }
    setState(() {
      busy = true;
      message = '';
      error = '';
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
        outcome = null;
        message = widget.requestId == null
            ? l('تم إرسال طلب المراجعة.', 'Review request sent.')
            : l(
                'تم حفظ القرار ونشره لولي الأمر المرتبط.',
                'Decision saved and shared with the linked guardian.',
              );
      });
    } catch (e) {
      if (mounted && identity == original) {
        final status = e is ApiFailure ? e.status : 0;
        final uncertain = status == 0 || status >= 500;
        final changed = [401, 403, 404, 409].contains(status);
        setState(() {
          needsRead = uncertain || changed;
          error = uncertain
              ? l(
                  'تعذر تأكيد الحفظ. تحقّق من حالة المراجعة قبل إعادة الإرسال.',
                  'Save could not be confirmed. Check the review before sending again.',
                )
              : changed
              ? l(
                  'تغيّرت البيانات أو الصلاحية. تحقّق من حالة المراجعة.',
                  'Data or access changed. Check the review status.',
                )
              : (e as ApiFailure).message;
        });
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  Future<void> open(Widget page) async {
    await openPage(context, page);
    if (mounted && identity == original) setState(() => revision++);
  }

  String date(Object? value) {
    final raw = day(value);
    final parsed = DateTime.tryParse(raw);
    return parsed == null
        ? raw
        : DateFormat.yMMMd(Localizations.localeOf(context).toString())
              .format(parsed);
  }

  Widget meta(String value) =>
      Text(value, style: Theme.of(context).textTheme.bodySmall);

  List<Widget> writeStatus() => [
    if (message.isNotEmpty) Semantics(liveRegion: true, child: Text(message)),
    if (error.isNotEmpty) Semantics(liveRegion: true, child: Text(error)),
    if (needsRead)
      OutlinedButton(
        onPressed: () => setState(() {
          needsRead = false;
          error = '';
          revision++;
        }),
        child: Text(l('تحقّق من حالة المراجعة', 'Check review status')),
      ),
  ];

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (!foreground || identity == null || identity != original) {
        return const SizedBox.shrink();
      }
      return GuardianPage(
        title: isInbox
            ? l('طلبات المراجعة الفنية', 'Technical review inbox')
            : widget.requestId != null
            ? l('قرار المراجعة الفنية', 'Technical review decision')
            : l('المراجعة الفنية للمستوى', 'Level technical review'),
        onBack: () => Navigator.of(context).maybePop(),
        child: Column(
          children: [
            if (isInbox)
              SwitchListTile(
                title: Text(
                  l(
                    'إظهار الطلبات التي صدر قرارها أيضًا',
                    'Include decided requests',
                  ),
                ),
                value: all,
                onChanged: (value) => setState(() => all = value),
              ),
            Expanded(
              child: RemoteBody(
                key: ValueKey('$identity:$revision:$all'),
                auth: widget.auth,
                path: path,
                builder: (context, value) {
                  final data = map(value);
                  return gList([
                    if (widget.requestId != null) ...detail(data),
                    if (widget.planId != null) ...plan(data),
                    if (isInbox) ...inbox(data),
                  ], key: PageStorageKey('technical:$identity:$path'));
                },
              ),
            ),
          ],
        ),
      );
    },
  );
  List<Widget> plan(Json data) {
    final enrollment = map(data['enrollment']);
    final source = map(data['source']);
    final plan = map(data['plan']);
    final entries = list(data['items']);
    final latest = entries.isEmpty ? null : entries.first;
    final latestRequest = map(latest?['request']);
    final family = widget.playerId != null;
    return [
      ...writeStatus(),
      GCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              '${enrollment['player']} · ${enrollment['sport']}',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            if (!family) ...[
              const SizedBox(height: 8),
              Text(
                '${source['mastered']}/${source['requirements']} ${l('متطلبات متقنة', 'requirements mastered')}',
              ),
              if (data['canRequest'] == true) ...[
                const SizedBox(height: 12),
                FilledButton(
                  onPressed: locked
                      ? null
                      : () => save(path, {
                          'planVersion': plan['version'],
                          'sourceReviewSequence': source['head'],
                          'previousRequestId': data['latestRequestId'],
                        }),
                  child: Text(
                    busy
                        ? l('جارٍ إرسال الطلب…', 'Sending request…')
                        : l(
                            'طلب مراجعة فنية للمستوى',
                            'Request level technical review',
                          ),
                  ),
                ),
              ] else if (latest != null && latest['decision'] == null)
                Text(
                  l(
                    'يوجد طلب بانتظار القرار.',
                    'A request is awaiting a decision.',
                  ),
                )
              else if (source['eligible'] == true &&
                  latest != null &&
                  (source['head'] as num) <=
                      (latestRequest['sourceReviewSequence'] as num))
                Text(
                  l(
                    'يلزم تسجيل دليل فني أحدث لإرسال طلب آخر.',
                    'Record newer evidence before sending another request.',
                  ),
                )
              else if ((source['mastered'] as num) <
                  (source['requirements'] as num))
                Text(
                  l(
                    'يمكن طلب المراجعة بعد إتقان متطلبات الخطة الحالية.',
                    'Request a review after mastering the current plan requirements.',
                  ),
                ),
            ],
            if (entries.isEmpty)
              Text(
                l(
                  'لا توجد طلبات لهذه الخطة بعد.',
                  'No requests for this plan yet.',
                ),
              ),
          ],
        ),
      ),
      for (final entry in entries) entryCard(entry, source: source),
      if (data['hasMore'] == true)
        Text(l('تظهر أحدث 20 مراجعة.', 'Showing the latest 20 reviews.')),
    ];
  }

  Widget entryCard(Json entry, {Json? source, Json? scope}) {
    final request = map(entry['request']);
    final changed =
        source != null &&
        (source['eligible'] != true ||
            source['head'] != request['sourceReviewSequence']);
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            scope == null
                ? '${l('مراجعة', 'Review')} ${request['sequence']} · ${textOf(request['levelName'])}'
                : textOf(scope['player']),
            style: Theme.of(context).textTheme.titleMedium,
          ),
          if (scope != null)
            Text(
              [scope['branch'], scope['sport'], request['levelName']]
                  .whereType<String>()
                  .where((value) => value.isNotEmpty)
                  .join(' · '),
            ),
          const SizedBox(height: 8),
          ...decision(entry, contextChanged: changed),
          meta(
            '${l('طلبها', 'Requested by')} ${request['actorName']} · ${date(request['createdAtUtc'])}',
          ),
          if (widget.playerId == null)
            GLink(
              l('الأدلة وقرار المراجعة', 'Evidence and review decision'),
              Icons.fact_check_outlined,
              () => open(
                PlayerTechnicalReviewsPage(
                  auth: widget.auth,
                  requestId: textOf(request['id']),
                ),
              ),
            ),
        ],
      ),
    );
  }

  List<Widget> decision(Json entry, {bool contextChanged = false}) {
    final decision = map(entry['decision']);
    final application = map(entry['application']);
    final applied = application.isNotEmpty;
    return [
      Text(
        applied
            ? l('نُفّذت التوصية', 'Recommendation applied')
            : label(decision['outcome']),
        style: const TextStyle(fontWeight: FontWeight.bold),
      ),
      if (applied)
        Text(
          '${l('المستوى المنفّذ', 'Applied level')}: ${application['toLevelName']} · ${date(application['createdAtUtc'])}',
        )
      else if (decision['recommendedLevelName'] != null) ...[
        Text(
          '${l('المستوى الموصى به', 'Recommended level')}: ${decision['recommendedLevelName']}',
        ),
        meta(
          contextChanged
              ? l(
                  'تغيّرت الخطة أو الأدلة بعد صدور التوصية.',
                  'The plan or evidence changed after the recommendation.',
                )
              : l(
                  'التنفيذ يحتاج مراجعة إدارية.',
                  'Applying the recommendation requires administrative review.',
                ),
        ),
      ],
      if (decision['familyNote'] != null) Text(textOf(decision['familyNote'])),
      if (decision.isNotEmpty)
        meta('${decision['actorName']} · ${date(decision['createdAtUtc'])}'),
    ];
  }

  List<Widget> inbox(Json data) {
    final contexts = map(data['contexts']);
    final entries = list(data['items']);
    return [
      if (entries.isEmpty)
        EmptyMessage(
          all
              ? l(
                  'لا توجد طلبات مراجعة ضمن نطاقك.',
                  'No review requests within your scope.',
                )
              : l(
                  'لا توجد طلبات بانتظار القرار.',
                  'No requests are awaiting a decision.',
                ),
        ),
      for (final entry in entries)
        entryCard(
          entry,
          scope: map(contexts[map(entry['request'])['sportEnrollmentId']]),
        ),
      if (data['hasMore'] == true)
        Text(
          l('تظهر أول 100 طلب حسب الأقدم.', 'Showing the oldest 100 requests.'),
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
      if (d['currentEligible'] == true && levels.isNotEmpty)
        'ReadyForPlacement',
      if (d['contextChanged'] == true) 'ContextChanged',
    ];
    final chosen = choices.contains(outcome) ? outcome : null;
    final chosenLevel = levels.any((x) => x['id'] == recommended)
        ? recommended
        : null;
    final applied = d['application'] != null;
    final changed = d['contextChanged'] == true;
    final noteLabel = l(
      'ملخص فني للأسرة (اختياري)',
      'Technical family summary (optional)',
    );
    return [
      GCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              '${e['player']} · ${r['levelName']}',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            Text(
              [
                e['sport'],
                r['curriculumTitle'],
              ].whereType<String>().join(' · '),
            ),
            const SizedBox(height: 12),
            ...decision(d, contextChanged: changed),
            meta(
              '${l('طلبها', 'Requested by')} ${r['actorName']} · ${date(r['createdAtUtc'])}',
            ),
            if (changed && dec.isEmpty)
              Text(
                l(
                  'تغيّرت الخطة أو الأدلة بعد إرسال الطلب. يلزم دليل حديث قبل التوصية بمستوى أعلى.',
                  'The plan or evidence changed after submission. A higher level requires current evidence.',
                ),
              ),
            if (d['canReviewPlacement'] == true && (applied || !changed))
              GLink(
                applied
                    ? l(
                        'سجل تنفيذ التوصية',
                        'Recommendation application record',
                      )
                    : l(
                        'معاينة وتنفيذ التوصية',
                        'Preview and apply recommendation',
                      ),
                Icons.move_up_outlined,
                () => open(
                  TechnicalPromotionPage(
                    auth: widget.auth,
                    requestId: widget.requestId!,
                  ),
                ),
              ),
          ],
        ),
      ),
      Text(
        l('الأدلة عند إرسال الطلب', 'Evidence at submission'),
        style: Theme.of(context).textTheme.titleMedium,
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
              Text(
                v['measuredValue'] == null
                    ? l('أتقن المهارة', 'Skill mastered')
                    : '${v['measuredValue']} ${unit(v['unit'])}',
              ),
              meta('${v['actorName']} · ${date(v['observedOn'])}'),
              if (textOf(v['protocol']).isNotEmpty) Text(textOf(v['protocol'])),
            ],
          ),
        ),
      if (d['canDecide'] != true) ...writeStatus(),
      if (d['canDecide'] == true)
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (d['currentEligible'] == true && levels.isEmpty) ...[
                Text(
                  l(
                    'لا يوجد مستوى أعلى مفعّل لهذه الرياضة.',
                    'There is no active higher level for this sport.',
                  ),
                ),
                const SizedBox(height: 12),
              ],
              DropdownButtonFormField<String>(
                key: ValueKey('outcome:$revision:$chosen'),
                initialValue: chosen,
                isExpanded: true,
                isDense: false,
                decoration: InputDecoration(
                  labelText: l('قرار المراجعة', 'Review decision'),
                ),
                items: choices
                    .map(
                      (x) => DropdownMenuItem(value: x, child: Text(label(x))),
                    )
                    .toList(),
                onChanged: locked
                    ? null
                    : (value) => setState(() {
                        outcome = value;
                        recommended = null;
                      }),
              ),
              const SizedBox(height: 16),
              if (chosen == 'ReadyForPlacement')
                DropdownButtonFormField<String>(
                  key: ValueKey('level:$revision'),
                  initialValue: chosenLevel,
                  isExpanded: true,
                  isDense: false,
                  decoration: InputDecoration(
                    labelText: l('المستوى الموصى به', 'Recommended level'),
                  ),
                  items: levels
                      .map(
                        (x) => DropdownMenuItem(
                          value: textOf(x['id']),
                          child: Text(textOf(x['name'])),
                        ),
                      )
                      .toList(),
                  onChanged: locked
                      ? null
                      : (v) => setState(() => recommended = v),
                ),
              const SizedBox(height: 16),
              ExcludeSemantics(child: Text(noteLabel)),
              const SizedBox(height: 8),
              Semantics(
                label: noteLabel,
                child: TextField(
                  controller: note,
                  maxLength: 500,
                  maxLines: 3,
                  enabled: !locked,
                ),
              ),
              Text(
                l(
                  'يظهر القرار والملخص لولي الأمر المرتبط. استبعد البيانات الصحية والملاحظات الإدارية الخاصة.',
                  'The linked guardian sees the decision and summary. Leave out health or private administrative information.',
                ),
              ),
              if (chosen == 'ReadyForPlacement')
                Text(
                  l(
                    'يُنفَّذ تغيير المستوى في خطوة إدارية منفصلة.',
                    'Changing the level requires a separate administrative step.',
                  ),
                ),
              const SizedBox(height: 12),
              ...writeStatus(),
              FilledButton(
                onPressed:
                    locked ||
                        chosen == null ||
                        chosen == 'ReadyForPlacement' && chosenLevel == null
                    ? null
                    : () => save('$root/${widget.requestId}/decision', {
                        'version': r['version'],
                        'outcome': chosen,
                        'recommendedLevelId': chosen == 'ReadyForPlacement'
                            ? chosenLevel
                            : null,
                        'familyNote': note.text.trim().isEmpty
                            ? null
                            : note.text.trim(),
                      }),
                child: Text(
                  busy
                      ? l('جارٍ حفظ القرار…', 'Saving decision…')
                      : l(
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
    'Count' => l('مرة', 'reps'),
    'Seconds' => l('ثانية', 'seconds'),
    'Metres' => l('متر', 'metres'),
    _ => '',
  };
}
