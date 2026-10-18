import 'dart:convert';

import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../guardian/guardian_ui.dart';
import 'player_goals.dart' show goalCommandId;

class PlayerSkillsPage extends StatefulWidget {
  final AuthController auth;
  final String? enrollmentId, playerId, planId;
  const PlayerSkillsPage({
    super.key,
    required this.auth,
    this.enrollmentId,
    this.playerId,
    this.planId,
  });
  @override
  State<PlayerSkillsPage> createState() => _PlayerSkillsState();
}

class _PlayerSkillsState extends State<PlayerSkillsPage>
    with WidgetsBindingObserver {
  static const root = '/api/v1/evaluations/skills';
  String? get identity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final original = identity;
  int revision = 0;
  bool busy = false, foreground = true;
  String message = '', state = 'Learning';
  String? requirement, date, curriculum, signature, requestId;
  final value = TextEditingController(), note = TextEditingController();
  final form = GlobalKey<FormState>();
  String l(String ar, String en) => localized(context, ar, en);
  List<Json> list(Object? v) => rows(v ?? <Json>[]);
  Json map(Object? v) => obj(v ?? <String, dynamic>{});
  String status(Object? s) => switch (s) {
    'Mastered' => l('أتقن', 'Mastered'),
    'Learning' => l('قيد التعلم', 'Learning'),
    _ => l('لم يُقيّم', 'Not assessed'),
  };
  String unit(Object? s) => switch (s) {
    'Count' => l('عدد', 'Count'),
    'Seconds' => l('ثانية', 'Seconds'),
    'Metres' => l('متر', 'Metres'),
    _ => l('مراجعة فنية', 'Technical review'),
  };
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    value.dispose();
    note.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState s) {
    setState(() {
      foreground = s == AppLifecycleState.resumed;
      revision++;
      requirement = null;
      curriculum = null;
      date = null;
      note.clear();
      value.clear();
      message = '';
    });
  }

  String get path => widget.planId != null
      ? (widget.playerId != null
            ? '/api/v1/guardian/children/${widget.playerId}/skills/plans/${widget.planId}'
            : '$root/plans/${widget.planId}')
      : widget.playerId != null
      ? '/api/v1/guardian/children/${widget.playerId}/skills'
      : '$root/enrollments/${widget.enrollmentId}/journey';
  Future<void> save(String path, Json body) async {
    if (busy || identity == null || identity != original) return;
    final encoded = jsonEncode({'path': path, 'body': body});
    if (signature != encoded) {
      signature = encoded;
      requestId = goalCommandId();
    }
    setState(() {
      busy = true;
      message = '';
    });
    try {
      await widget.auth.request(
        'POST',
        path,
        body: {...body, 'requestId': requestId},
      );
      if (!mounted || identity != original) return;
      signature = null;
      requestId = null;
      setState(() {
        revision++;
        message = l('تم حفظ التحديث الفني.', 'Technical update saved.');
        note.clear();
        value.clear();
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

  void openPlan(String id) => openPage(
    context,
    PlayerSkillsPage(auth: widget.auth, playerId: widget.playerId, planId: id),
  );
  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (!foreground || identity == null || identity != original) {
        return const SizedBox.shrink();
      }
      return GuardianPage(
        title: l('خطوات تعلم اللاعب', 'Player learning journey'),
        child: RemoteBody(
          key: ValueKey('$identity:$revision'),
          auth: widget.auth,
          path: path,
          builder: (context, v) {
            final d = map(v);
            return gList([
              Text(
                l(
                  'مهارات بأدلة فعلية · الإتقان لا يغيّر المستوى أو المجموعة تلقائيًا.',
                  'Evidence-based skills · mastery does not automatically change level or group.',
                ),
              ),
              if (message.isNotEmpty) Text(message),
              if (widget.planId != null) ...plan(d),
              if (widget.planId == null && widget.playerId == null)
                ...journey(d),
              if (widget.planId == null && widget.playerId != null)
                for (final j in list(d['items'])) ...journey(j),
            ]);
          },
        ),
      );
    },
  );
  List<Widget> journey(Json d) {
    final active = map(d['active']), enrollment = map(d['enrollment']);
    final old = list(d['history']);
    final choices = list(d['availableCurricula']);
    if (curriculum != null && !choices.any((c) => c['id'] == curriculum)) {
      curriculum = null;
    }
    return [
      Heading(
        '${textOf(enrollment['player'])} · ${textOf(enrollment['sport'])}',
      ),
      if (active.isEmpty)
        EmptyMessage(
          l(
            'لم تُسند خطة مهارات بعد؛ لا نسبة افتراضية.',
            'No skills plan assigned yet; no assumed progress.',
          ),
        ),
      if (active.isNotEmpty) ...[
        progress(active),
        if (!busy)
          GLink(
            l('المهارات وملاحظات المدرب', 'Skills and coach observations'),
            Icons.insights_outlined,
            () => openPlan(textOf(map(active['plan'])['id'])),
          ),
      ],
      if (widget.playerId == null && d['canAssign'] == true)
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(
                l(
                  'المستوى الحالي: ${enrollment['level'] ?? 'لم يحدد فنيًا'}',
                  'Current level: ${enrollment['level'] ?? 'Not reviewed'}',
                ),
              ),
              DropdownButtonFormField<String>(
                isExpanded: true,
                initialValue: curriculum,
                decoration: InputDecoration(
                  labelText: l(
                    'نسخة المستوى الحالي',
                    'Current level curriculum',
                  ),
                ),
                items: choices
                    .map(
                      (c) => DropdownMenuItem(
                        value: textOf(c['id']),
                        child: Text(
                          '${c['title']} · ${c['revisionNumber']}',
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                    )
                    .toList(),
                onChanged: busy ? null : (v) => setState(() => curriculum = v),
              ),
              if (choices.isEmpty)
                Text(
                  l(
                    'يراجع المسؤول المستوى ثم ينشر منهجه من لوحة الويب.',
                    'An authorized administrator reviews the level and publishes its curriculum in the web dashboard.',
                  ),
                ),
              FilledButton(
                onPressed: busy || curriculum == null
                    ? null
                    : () async {
                        if (active.isNotEmpty) {
                          final okay = await showDialog<bool>(
                            context: context,
                            builder: (c) => AlertDialog(
                              title: Text(
                                l(
                                  'استبدال الخطة مع حفظ التاريخ؟',
                                  'Replace the plan and preserve history?',
                                ),
                              ),
                              actions: [
                                TextButton(
                                  onPressed: () => Navigator.pop(c, false),
                                  child: Text(l('رجوع', 'Back')),
                                ),
                                FilledButton(
                                  onPressed: () => Navigator.pop(c, true),
                                  child: Text(l('تأكيد', 'Confirm')),
                                ),
                              ],
                            ),
                          );
                          if (okay != true) return;
                        }
                        await save(
                          '$root/enrollments/${enrollment['id']}/skill-plan',
                          {
                            'curriculumId': curriculum,
                            'previousPlanId': active.isEmpty
                                ? null
                                : map(active['plan'])['id'],
                            'previousVersion': active.isEmpty
                                ? 0
                                : map(active['plan'])['version'],
                          },
                        );
                      },
                child: Text(
                  active.isEmpty
                      ? l('إسناد خطة المهارات', 'Assign skills plan')
                      : l('استبدال الخطة', 'Replace plan'),
                ),
              ),
            ],
          ),
        ),
      if (old.isNotEmpty)
        GCard(
          child: ExpansionTile(
            title: Text(l('الخطط السابقة', 'Previous plans')),
            children: [
              for (final p in old)
                ListTile(
                  title: Text(textOf(map(p['curriculum'])['title'])),
                  subtitle: Text(
                    '${p['assignedOn']} · ${map(p['curriculum'])['revisionNumber']}',
                  ),
                  trailing: const Icon(Icons.chevron_left),
                  onTap: () => openPlan(textOf(p['id'])),
                ),
            ],
          ),
        ),
      if (d['historyHasMore'] == true)
        Text(
          l('تظهر أحدث20 خطة سابقة.', 'Showing the latest 20 previous plans.'),
        ),
      GCard(
        child: ExpansionTile(
          title: Text(l('سجل مراجعات المستوى', 'Level review history')),
          children: [
            if (list(map(d['levelHistory'])['items']).isEmpty)
              Text(
                l(
                  'لا توجد مراجعات مؤرخة محفوظة بعد؛ لا نفترض تاريخ ترقية من المستوى المسجل وحده.',
                  'No dated reviews saved yet; a recorded level alone does not prove a promotion date.',
                ),
              ),
            for (final r in list(map(d['levelHistory'])['items']))
              ListTile(
                title: Text(
                  '${r['fromLevelName'] ?? l('لم يحدد', 'Not set')} ← ${r['toLevelName'] ?? l('لم يحدد', 'Not set')}',
                ),
                subtitle: Text(
                  '${day(r['reviewedOn'] ?? r['createdAtUtc'])} · ${r['actorName']} · ${r['revision']}',
                ),
              ),
            if (map(d['levelHistory'])['hasMore'] == true)
              Text(
                l(
                  'تظهر أحدث20 مراجعة؛ راجع الإدارة للسجل الأقدم.',
                  'Showing the latest 20 reviews; contact administration for older records.',
                ),
              ),
          ],
        ),
      ),
    ];
  }

  Widget progress(Json d) {
    final p = map(d['progress']), c = map(map(d['plan'])['curriculum']);
    final total = (p['total'] as num? ?? 0).toDouble(),
        mastered = (p['mastered'] as num? ?? 0).toDouble();
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(Icons.auto_graph_rounded),
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  textOf(c['title']),
                  style: Theme.of(context).textTheme.titleMedium,
                ),
              ),
            ],
          ),
          Text(
            '${c['levelNameSnapshot']} · ${l('نسخة', 'Version')} ${c['revisionNumber']}',
          ),
          const SizedBox(height: 12),
          Text(
            '${p['mastered']}/${p['total']} · ${p['percentage'] ?? '—'}%',
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          if (p['percentage'] != null && total > 0)
            LinearProgressIndicator(
              value: mastered / total,
              minHeight: 7,
              borderRadius: BorderRadius.circular(8),
              color: Theme.of(context).colorScheme.primary,
              backgroundColor: Theme.of(context).colorScheme.primary
                  .withValues(alpha: .14),
              semanticsLabel: l('متطلبات متقنة', 'Mastered requirements'),
            ),
          const SizedBox(height: 8),
          Text(
            l(
              'الأوزان متساوية · لم يُقيّم لا يعني صفرًا.',
              'Equal weights · not assessed is not zero.',
            ),
          ),
          if (d['contextChanged'] == true)
            Text(
              l(
                'خطة سابقة أو مستوى تغير. النتيجة التاريخية ${p['sourcePercentage'] ?? '—'}% ليست نسبة المستوى الحالي.',
                'Previous or changed plan. Historical result ${p['sourcePercentage'] ?? '—'}% is not current-level progress.',
              ),
            ),
          if (p['readyForTechnicalReview'] == true)
            Text(
              l(
                'استوفى المتطلبات · يحتاج مراجعة فنية بشرية، لا ترقية تلقائية.',
                'Requirements met · needs human technical review, not automatic promotion.',
              ),
            ),
        ],
      ),
    );
  }

  Widget observation(Json r, Object? u) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(
        '${status(r['state'])}${r['measuredValue'] == null ? '' : ' · ${r['measuredValue']} ${unit(u)}'}',
      ),
      Text('${day(r['observedOn'])} · ${textOf(r['actorName'])}'),
      if (r['note'] != null) Text(textOf(r['note'])),
    ],
  );
  List<Widget> plan(Json d) {
    final skills = list(d['skills']);
    final p = map(d['plan']);
    Json chosen = {};
    for (final s in skills) {
      if (map(s['requirement'])['id'] == requirement) {
        chosen = map(s['requirement']);
      }
    }
    final u = chosen['unit'];
    return [
      progress(d),
      if (widget.playerId == null && d['canReview'] == true)
        Form(
          key: form,
          child: GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Heading(
                  l(
                    'ملاحظة فنية للأسرة',
                    'Technical observation for the family',
                  ),
                ),
                DropdownButtonFormField<String>(
                  isExpanded: true,
                  initialValue: requirement,
                  decoration: InputDecoration(labelText: l('المهارة', 'Skill')),
                  validator: (v) =>
                      v == null ? l('اختر المهارة', 'Select a skill') : null,
                  items: skills.map((s) {
                    final r = map(s['requirement']);
                    return DropdownMenuItem(
                      value: textOf(r['id']),
                      child: Text(
                        textOf(r['name']),
                        overflow: TextOverflow.ellipsis,
                      ),
                    );
                  }).toList(),
                  onChanged: busy
                      ? null
                      : (v) => setState(() {
                          requirement = v;
                          value.clear();
                        }),
                ),
                if (chosen.isNotEmpty) Text(textOf(chosen['protocol'])),
                DropdownButtonFormField<String>(
                  initialValue: state,
                  decoration: InputDecoration(
                    labelText: l('الحالة الفنية', 'Learning status'),
                  ),
                  items: ['NotAssessed', 'Learning', 'Mastered']
                      .map(
                        (s) =>
                            DropdownMenuItem(value: s, child: Text(status(s))),
                      )
                      .toList(),
                  onChanged: busy ? null : (v) => setState(() => state = v!),
                ),
                if (u != null && u != 'Checklist' && state != 'NotAssessed')
                  TextFormField(
                    controller: value,
                    keyboardType: const TextInputType.numberWithOptions(
                      decimal: true,
                    ),
                    decoration: InputDecoration(
                      labelText: '${l('القياس', 'Measurement')} · ${unit(u)}',
                    ),
                    validator: (v) {
                      final n = double.tryParse(v ?? '');
                      return n == null ||
                              !n.isFinite ||
                              n < 0 ||
                              n > 1000000 ||
                              u == 'Count' && n != n.roundToDouble()
                          ? l(
                              'أدخل قيمة صحيحة بوحدة البروتوكول',
                              'Enter a valid value in the protocol unit',
                            )
                          : null;
                    },
                  ),
                OutlinedButton.icon(
                  icon: const Icon(Icons.calendar_month_outlined),
                  label: Text(
                    '${l('تاريخ الملاحظة', 'Observation date')} · ${date ?? d['asOfDate']}',
                  ),
                  onPressed: busy
                      ? null
                      : () async {
                          final first = DateTime.parse(
                                p['assignedOn'] as String,
                              ),
                              last = DateTime.parse(d['asOfDate'] as String);
                          final result = await showDatePicker(
                            context: context,
                            initialDate: date == null
                                ? last
                                : DateTime.parse(date!),
                            firstDate: first,
                            lastDate: last,
                          );
                          if (result != null && mounted) {
                            setState(
                              () => date = result.toIso8601String().substring(
                                0,
                                10,
                              ),
                            );
                          }
                        },
                ),
                TextFormField(
                  controller: note,
                  maxLength: 500,
                  maxLines: 3,
                  decoration: InputDecoration(
                    labelText: l(
                      'ملاحظة للأسرة (اختياري)',
                      'Family note (optional)',
                    ),
                  ),
                ),
                Text(
                  l(
                    'التصحيح بملاحظة جديدة؛ لا نعيد كتابة السجل السابق.',
                    'Corrections append a new observation; previous evidence stays unchanged.',
                  ),
                ),
                FilledButton.icon(
                  icon: const Icon(Icons.publish_outlined),
                  label: Text(
                    l('حفظ ونشر الملاحظة للأسرة', 'Save and share observation'),
                  ),
                  onPressed: busy
                      ? null
                      : () {
                          if (form.currentState!.validate()) {
                            save('$root/plans/${widget.planId}/reviews', {
                              'requirementId': requirement,
                              'state': state,
                              'measuredValue':
                                  u == 'Checklist' || state == 'NotAssessed'
                                  ? null
                                  : num.parse(value.text),
                              'observedOn': date ?? d['asOfDate'],
                              'note': note.text.trim().isEmpty
                                  ? null
                                  : note.text.trim(),
                            });
                          }
                        },
                ),
              ],
            ),
          ),
        ),
      for (final s in skills)
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Heading(textOf(map(s['requirement'])['name'])),
              Text(textOf(map(s['requirement'])['protocol'])),
              const SizedBox(height: 8),
              if (s['latest'] == null)
                Text(l('لم يُقيّم', 'Not assessed'))
              else
                observation(map(s['latest']), map(s['requirement'])['unit']),
              if (s['baseline'] != null &&
                  map(s['baseline'])['id'] != map(s['latest'])['id'])
                ExpansionTile(
                  title: Text(l('أول ملاحظة', 'First observation')),
                  children: [
                    observation(
                      map(s['baseline']),
                      map(s['requirement'])['unit'],
                    ),
                  ],
                ),
            ],
          ),
        ),
      GCard(
        child: ExpansionTile(
          title: Text(l('سجل الملاحظات الفنية', 'Observation history')),
          children: [
            for (final r in list(d['history']))
              Padding(
                padding: const EdgeInsets.all(8),
                child: observation(
                  r,
                  skills
                      .where(
                        (s) =>
                            map(s['requirement'])['id'] == r['requirementId'],
                      )
                      .firstOrNull?['requirement']?['unit'],
                ),
              ),
            if (d['historyHasMore'] == true)
              Text(
                l(
                  'تظهر أحدث20 ملاحظة؛ راجع الإدارة للسجل الأقدم.',
                  'Showing the latest 20 observations; contact administration for older records.',
                ),
              ),
          ],
        ),
      ),
    ];
  }
}
