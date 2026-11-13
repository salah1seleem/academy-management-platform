import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show DateFormat;

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/widgets/guided_form.dart';
import '../guardian/guardian_ui.dart';
import 'player_goals.dart' show goalCommandId;
import 'player_technical_reviews.dart';

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
  int revision = 0, scrollEpoch = 0;
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
        scrollEpoch++;
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

  String displayDate(Object? value) {
    final raw = day(value);
    final parsed = DateTime.tryParse(raw);
    return parsed == null
        ? raw
        : DateFormat.yMMMd(Localizations.localeOf(context).toString())
              .format(parsed);
  }

  Future<void> openPlan(String id) async {
    await openPage(
      context,
      PlayerSkillsPage(
        auth: widget.auth,
        playerId: widget.playerId,
        planId: id,
      ),
    );
    if (mounted && foreground && identity == original) {
      setState(() => revision++);
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
        title: widget.planId == null
            ? l('خطط المهارات', 'Skills plans')
            : l('المهارات والملاحظات', 'Skills and observations'),
        onBack: () => Navigator.of(context).maybePop(),
        child: RemoteBody(
          key: ValueKey('$identity:$revision'),
          auth: widget.auth,
          path: path,
          builder: (context, v) {
            final d = map(v);
            final journeys = list(d['items']);
            return gList([
              if (message.isNotEmpty)
                Semantics(liveRegion: true, child: Text(message)),
              if (widget.planId != null) ...plan(d),
              if (widget.planId == null && widget.playerId == null)
                ...journey(d),
              if (widget.planId == null && widget.playerId != null) ...[
                if (journeys.isEmpty)
                  EmptyMessage(
                    l(
                      'لا توجد خطط مهارات لهذا اللاعب بعد.',
                      'No skills plans for this player yet.',
                    ),
                  ),
                for (final j in journeys) ...journey(j),
              ],
            ], key: PageStorageKey('$identity:$path:$scrollEpoch'));
          },
        ),
      );
    },
  );
  List<Widget> journey(Json d) {
    final active = map(d['active']), enrollment = map(d['enrollment']);
    final old = list(d['history']);
    final choices = list(d['availableCurricula']);
    final levelHistory = map(d['levelHistory']);
    final levelReviews = list(levelHistory['items']);
    if (curriculum != null && !choices.any((c) => c['id'] == curriculum)) {
      curriculum = null;
    }
    return [
      Heading(
        '${textOf(enrollment['player'])} · ${textOf(enrollment['sport'])}',
      ),
      if (active.isEmpty)
        EmptyMessage(
          l('لم تُسند خطة مهارات بعد.', 'No skills plan assigned yet.'),
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
            key: PageStorageKey('plans:${enrollment['id']}'),
            title: Text(l('الخطط السابقة', 'Previous plans')),
            children: [
              for (final p in old)
                ListTile(
                  title: Text(textOf(map(p['curriculum'])['title'])),
                  subtitle: Text(
                    '${displayDate(p['assignedOn'])} · ${l('نسخة', 'Version')} ${map(p['curriculum'])['revisionNumber']}',
                  ),
                  trailing: const Icon(Icons.arrow_forward_ios, size: 16),
                  onTap: () => openPlan(textOf(p['id'])),
                ),
            ],
          ),
        ),
      if (d['historyHasMore'] == true)
        Text(
          l('تظهر أحدث 20 خطة سابقة.', 'Showing the latest 20 previous plans.'),
        ),
      if (levelReviews.isNotEmpty)
        GCard(
          child: ExpansionTile(
            key: PageStorageKey('level-reviews:${enrollment['id']}'),
            title: Text(l('سجل مراجعات المستوى', 'Level review history')),
            children: [
              for (final r in levelReviews)
                ListTile(
                  title: Text(
                    '${r['fromLevelName'] ?? l('لم يحدد', 'Not set')} ← ${r['toLevelName'] ?? l('لم يحدد', 'Not set')}',
                  ),
                  subtitle: Text(
                    '${displayDate(r['reviewedOn'] ?? r['createdAtUtc'])} · ${r['actorName']}',
                  ),
                ),
              if (levelHistory['hasMore'] == true)
                Text(
                  l('تظهر أحدث 20 مراجعة.', 'Showing the latest 20 reviews.'),
                ),
            ],
          ),
        ),
    ];
  }

  Widget progress(Json d) {
    final p = map(d['progress']), c = map(map(d['plan'])['curriculum']);
    final enrollment = map(d['enrollment']);
    final total = p['total'] as num? ?? 0;
    final mastered = p['mastered'] as num? ?? 0;
    final assessed = p['assessed'] as num? ?? 0;
    final historical = d['contextChanged'] == true;
    final showTrack =
        !historical && assessed > 0 && total > 0 && p['percentage'] != null;
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            textOf(c['title']),
            style: Theme.of(context).textTheme.titleMedium,
          ),
          if (widget.planId != null)
            Text(
              '${textOf(enrollment['player'])} · ${textOf(enrollment['sport'])}',
            ),
          Text(
            '${c['levelNameSnapshot']} · ${l('نسخة', 'Version')} ${c['revisionNumber']}',
          ),
          const SizedBox(height: 12),
          if (historical) Text(l('خطة غير سارية', 'Inactive plan')),
          if (assessed == 0)
            Text(l('لم تُراجع أي مهارة بعد.', 'No skills reviewed yet.'))
          else ...[
            Text(
              '${l('الإتقان $mastered من $total', '$mastered of $total mastered')}${showTrack ? " · ${p['percentage']}%" : ""}',
              style: Theme.of(context).textTheme.titleSmall,
            ),
            if (showTrack) ...[
              const SizedBox(height: 6),
              LinearProgressIndicator(
                value: mastered / total,
                minHeight: 7,
                borderRadius: BorderRadius.circular(8),
                color: Theme.of(context).colorScheme.primary,
                backgroundColor: Theme.of(context).colorScheme.primary
                    .withValues(alpha: .14),
                semanticsLabel: l('المهارات المتقنة', 'Mastered skills'),
              ),
            ],
            if (historical && p['sourcePercentage'] != null)
              Text(
                l(
                  'الإتقان وقت الخطة ${p['sourcePercentage']}%',
                  'Mastery under this plan ${p['sourcePercentage']}%',
                ),
              ),
            Text(
              l(
                'تمت مراجعة $assessed من $total',
                '$assessed of $total reviewed',
              ),
            ),
          ],
          if (p['readyForTechnicalReview'] == true)
            Text(
              l(
                'اكتملت مهارات الخطة وهي جاهزة للمراجعة الفنية.',
                'The plan’s skills are complete and ready for technical review.',
              ),
            ),
        ],
      ),
    );
  }

  Widget observation(Json r, Object? u) {
    final measurement = r['measuredValue'];
    final label = [
      status(r['state']),
      if (measurement != null)
        u == 'Count'
            ? l('العدد $measurement', 'Count $measurement')
            : '$measurement ${unit(u)}',
    ].join(' · ');
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(label, style: Theme.of(context).textTheme.titleSmall),
        if (r['note'] != null) Text(textOf(r['note'])),
        Text('${textOf(r['actorName'])} · ${displayDate(r['observedOn'])}'),
      ],
    );
  }

  List<Widget> plan(Json d) {
    final skills = list(d['skills']);
    final history = list(d['history']);
    final requirements = {
      for (final skill in skills)
        map(skill['requirement'])['id']: map(skill['requirement']),
    };
    final p = map(d['plan']);
    final chosen = requirements[requirement] ?? const <String, dynamic>{};
    final u = chosen['unit'];
    return [
      progress(d),
      GLink(
        l('المراجعة الفنية للمستوى', 'Level technical review'),
        Icons.fact_check_outlined,
        () => openPage(
          context,
          PlayerTechnicalReviewsPage(
            auth: widget.auth,
            planId: widget.planId,
            playerId: widget.playerId,
          ),
        ),
      ),
      if (widget.playerId == null && d['canReview'] == true)
        GuidedForm(
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
                if (chosen.isNotEmpty) ...[
                  const SizedBox(height: 8),
                  Text(textOf(chosen['protocol'])),
                ],
                const SizedBox(height: 12),
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
                if (u != null &&
                    u != 'Checklist' &&
                    state != 'NotAssessed') ...[
                  const SizedBox(height: 12),
                  TextFormField(
                    controller: value,
                    enabled: !busy,
                    keyboardType: TextInputType.numberWithOptions(
                      decimal: u != 'Count',
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
                              n !=
                                  double.parse(
                                    n.toStringAsFixed(u == 'Count' ? 0 : 3),
                                  )
                          ? l(
                              u == 'Count'
                                  ? 'أدخل عددًا صحيحًا من 0 إلى 1000000.'
                                  : 'أدخل قياسًا من 0 إلى 1000000 حتى 3 منازل عشرية.',
                              u == 'Count'
                                  ? 'Enter a whole number from 0 to 1000000.'
                                  : 'Enter a measurement from 0 to 1000000 with up to 3 decimal places.',
                            )
                          : null;
                    },
                  ),
                ],
                const SizedBox(height: 12),
                OutlinedButton.icon(
                  icon: const Icon(Icons.calendar_month_outlined),
                  label: Text(
                    '${l('تاريخ الملاحظة', 'Observation date')} · ${displayDate(date ?? d['asOfDate'])}',
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
                const SizedBox(height: 12),
                TextFormField(
                  controller: note,
                  enabled: !busy,
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
                    'تظهر الملاحظة لولي الأمر المرتبط باللاعب. يُسجّل أي تصحيح كملاحظة جديدة.',
                    'The linked guardian can see this note. Corrections are recorded as new observations.',
                  ),
                ),
                const SizedBox(height: 12),
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
              const SizedBox(height: 8),
              if (s['latest'] == null)
                Text(l('لم يُقيّم', 'Not assessed'))
              else
                observation(map(s['latest']), map(s['requirement'])['unit']),
              if (map(s['requirement'])['protocol'] != null)
                ExpansionTile(
                  key: PageStorageKey(
                    'protocol:${map(s['requirement'])['id']}',
                  ),
                  title: Text(l('طريقة القياس', 'How it is measured')),
                  children: [Text(textOf(map(s['requirement'])['protocol']))],
                ),
              if (s['baseline'] != null &&
                  map(s['baseline'])['id'] != map(s['latest'])['id'])
                ExpansionTile(
                  key: PageStorageKey(
                    'first-review:${map(s['requirement'])['id']}',
                  ),
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
      if (history.isNotEmpty)
        GCard(
          child: ExpansionTile(
            key: const PageStorageKey('observation-history'),
            title: Text(l('سجل الملاحظات الفنية', 'Observation history')),
            children: [
              for (final r in history)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 8),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Text(
                        textOf(
                          requirements[r['requirementId']]?['name'] ??
                              l('ملاحظة فنية', 'Technical observation'),
                        ),
                        style: Theme.of(context).textTheme.titleMedium,
                      ),
                      observation(r, requirements[r['requirementId']]?['unit']),
                    ],
                  ),
                ),
              if (d['historyHasMore'] == true)
                Text(
                  l(
                    'تظهر أحدث 20 ملاحظة.',
                    'Showing the latest 20 observations.',
                  ),
                ),
            ],
          ),
        ),
    ];
  }
}
