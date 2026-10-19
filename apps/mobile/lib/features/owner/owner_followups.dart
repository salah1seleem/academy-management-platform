import 'dart:convert';

import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../coach/coach_attendance.dart';
import 'owner_operations.dart';
import 'owner_feedback_detail.dart';
import 'record_followup.dart';

const followUpStates = {
  'New': 'جديد',
  'Assigned': 'مسند',
  'InProgress': 'جارٍ',
  'Snoozed': 'مؤجل',
  'Closed': 'مغلق',
  'Rejected': 'مرفوض',
};
const followUpConditions = {
  'RecordFollowUp': 'متابعة سجل',
  'GroupCoachMissing': 'مجموعة بلا مدرب مخول',
  'GroupLowOccupancy': 'إشغال أقل من نصف السعة',
  'GroupSessionCoverage': 'حصص قادمة بلا مدرب مسند مخول',
  'GroupEvaluationDrafts': 'مسودات تقييم منذ 7 أيام',
  'GroupRecordedAbsences': 'غياب متكرر مسجل',
  'GroupOverdueFeedback': 'ملاحظات تجاوزت موعد المتابعة',
};
Widget overdueFeedbackSummary(Json source) {
  final d = source['overdueFeedback'];
  return Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      UiText(
        d is! Map
            ? 'بيانات الملاحظات غير متاحة حاليًا.'
            : '${d['complaintCount']} شكوى · ${d['suggestionCount']} اقتراح${d['requestCount'] == null ? '' : ' · ${d['requestCount']} طلب'} · ${d['submitterCount']} حساب مقدم فريد',
      ),
      if (d is Map)
        UiText('أقدم موعد متابعة: ${d['oldestDueOn'] ?? 'غير محدد'}'),
      const UiText(
        'تجاوزت موعد متابعة يدويًا أو من سياسة منشورة؛ ليست اتهامًا أو رسالة أو تسوية مالية.',
      ),
    ],
  );
}

Widget recordedAbsenceSummary(Json source) {
  final d = source['recordedAbsences'];
  return Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      UiText(
        d is! Map
            ? 'بيانات الغياب غير متاحة حاليًا.'
            : '${d['playerCount']} لاعب فريد · ${d['enrollmentCount']} تسجيل له غياب في آخر 3 حصص مؤهلة مسجلة داخل المجموعة.',
      ),
      if (d is Map && d['fromDate'] != null)
        Directionality(
          textDirection: TextDirection.ltr,
          child: UiText('${d['fromDate']} — ${d['toDate']}'),
        ),
      const UiText(
        'الملغاة وغير المسجلة والمستقبلية لا تُحسب. مراجعة بشرية دون رسالة أو عقوبة تلقائية.',
      ),
    ],
  );
}

Widget evaluationDraftSummary(Json source) {
  final d = source['evaluationDrafts'];
  return UiText(
    d is! Map
        ? 'بيانات مراجعة التقييمات غير متاحة حاليًا.'
        : '${d['draftCount']} مسودة · ${d['playerCount']} لاعب فريد. مسودات غير منشورة منذ 7 أيام على الأقل؛ ليست حكم كفاءة أو موعد استحقاق تعاقدي.',
  );
}

Widget coverageSummary(Json source) {
  final gaps = rows(source['coverageGaps'] ?? <Json>[]);
  return Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      UiText(
        '${gaps.length} حصة قادمة بلا إسناد مدرب مخول خلال 7 أيام؛ ليست غيابًا مسجلًا أو خصمًا.',
      ),
      for (final s in gaps.take(3))
        Directionality(
          textDirection: TextDirection.ltr,
          child: UiText(
            '${s['sessionDate']} · ${textOf(s['startTime']).substring(0, 5)}–${textOf(s['endTime']).substring(0, 5)}',
          ),
        ),
      const UiText(
        'راجع جدول المجموعة لإسناد المدرب. الأوقات محلية للأكاديمية.',
      ),
    ],
  );
}

Widget followUpList(List<Widget> children) => gList([
  for (final child in children)
    Padding(padding: const EdgeInsets.only(bottom: 14), child: child),
]);
String followUpRequestId() {
  final h = commandKey().substring(0, 32);
  return '${h.substring(0, 8)}-${h.substring(8, 12)}-${h.substring(12, 16)}-${h.substring(16, 20)}-${h.substring(20)}';
}

String? followUpContext(AuthController auth) => auth.session?.selected == null
    ? null
    : '${auth.session!.selected!.id}:${auth.session!.selected!.contextRevision}';

class FollowUpEvaluationSource extends StatefulWidget {
  final AuthController auth;
  final String id;
  const FollowUpEvaluationSource({
    super.key,
    required this.auth,
    required this.id,
  });
  @override
  State<FollowUpEvaluationSource> createState() =>
      _FollowUpEvaluationSourceState();
}

class _FollowUpEvaluationSourceState extends State<FollowUpEvaluationSource>
    with WidgetsBindingObserver {
  late final identity = followUpContext(widget.auth);
  bool foreground = true;
  int revision = 0;
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
    // Discard child evidence off-screen. Resume must reauthorize against the
    // actual source instead of redisplaying a previously authorized draft.
    setState(() {
      foreground = state == AppLifecycleState.resumed;
      revision++;
    });
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (!foreground ||
          identity == null ||
          identity != followUpContext(widget.auth)) {
        return const SizedBox.shrink();
      }
      return GuardianPage(
        title: 'مصدر متابعة التقييم',
        child: RemoteBody(
          key: ValueKey('$identity:$revision'),
          auth: widget.auth,
          path: '/api/v1/evaluations/${widget.id}',
          builder: (context, value) {
            final e = obj(value);
            return followUpList([
              Heading(textOf(e['player'])),
              UiText('${e['group']} · ${e['sport']}'),
              UiText(
                e['status'] == 'Draft'
                    ? 'مسودة غير منشورة'
                    : e['status'] == 'Published'
                    ? 'تقييم منشور'
                    : 'تقييم مستبدل',
              ),
              UiText(textOf(e['evaluationDate'])),
              if (e['generalNotes'] is String &&
                  (e['generalNotes'] as String).isNotEmpty)
                UiText(e['generalNotes'] as String),
              for (final score in rows(e['scores']))
                GCard(
                  child: UiText(
                    '${score['name']}: ${score['score'] ?? 'لم يُقيّم'}',
                  ),
                ),
              const UiText(
                'قراءة من المصدر الفعلي؛ تحديث أو إغلاق المتابعة لا يعدل هذا التقييم.',
              ),
            ]);
          },
        ),
      );
    },
  );
}

class OwnerFollowUpsPage extends StatefulWidget {
  final AuthController auth;
  const OwnerFollowUpsPage({super.key, required this.auth});
  @override
  State<OwnerFollowUpsPage> createState() => _OwnerFollowUpsPageState();
}

class _OwnerFollowUpsPageState extends State<OwnerFollowUpsPage> {
  late final identity = followUpContext(widget.auth);
  Json? options, data;
  String? from, to;
  String state = 'Open', branch = '', error = '';
  int generation = 0;
  bool initialized = false, loading = true;
  @override
  void initState() {
    super.initState();
    load();
  }

  bool valid(int g) =>
      mounted &&
      g == generation &&
      identity != null &&
      identity == followUpContext(widget.auth);
  Future<void> load() async {
    final g = ++generation;
    setState(() => loading = true);
    try {
      final o = obj(
        await widget.auth.request(
          'GET',
          '/api/v1/manage/owner-followups/options',
        ),
      );
      if (!valid(g)) {
        return;
      }
      if (!initialized) {
        to = textOf(o['asOfDate']);
        from = '${to!.substring(0, 7)}-01';
        initialized = true;
      }
      final query = Uri(
        queryParameters: {
          'from': ?from,
          'to': ?to,
          if (from == null && to == null) 'allDates': 'true',
          'state': state,
          if (branch.isNotEmpty) 'branchId': branch,
        },
      ).query;
      final d = obj(
        await widget.auth.request(
          'GET',
          '/api/v1/manage/owner-followups?$query',
        ),
      );
      if (valid(g)) {
        setState(() {
          options = o;
          data = d;
          error = '';
          loading = false;
        });
      }
    } catch (e) {
      if (valid(g)) {
        setState(() {
          error = e is ApiFailure ? e.message : 'تعذر تحميل المتابعات.';
          loading = false;
        });
      }
    }
  }

  Future<void> open(String id) async {
    await Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => OwnerFollowUpPage(auth: widget.auth, id: id),
      ),
    );
    if (mounted && identity == followUpContext(widget.auth)) {
      await load();
    }
  }

  Future<void> dateFilter(bool start) async {
    final picked = await showDatePicker(
      context: context,
      initialDate:
          DateTime.tryParse((start ? from : to) ?? '') ?? DateTime.now(),
      firstDate: DateTime(2000),
      lastDate: DateTime(2100),
    );
    if (picked == null ||
        !mounted ||
        identity != followUpContext(widget.auth)) {
      return;
    }
    setState(() {
      if (start) {
        from = picked.toIso8601String().split('T').first;
      } else {
        to = picked.toIso8601String().split('T').first;
      }
    });
    await load();
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (identity == null || identity != followUpContext(widget.auth)) {
        return const SizedBox.shrink();
      }
      return GuardianPage(
        title: 'المتابعات واتخاذ القرار',
        child: followUpList([
          const UiText(
            'متابعة بشرية مرتبطة بالمصدر، وليست تسوية مالية. يسندها المالك لمسؤول مخول داخل نطاق المجموعة.',
          ),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              OutlinedButton(
                onPressed: () => dateFilter(true),
                child: UiText('من: ${from ?? 'كل التواريخ'}'),
              ),
              OutlinedButton(
                onPressed: () => dateFilter(false),
                child: UiText('إلى: ${to ?? 'كل التواريخ'}'),
              ),
              TextButton(
                onPressed: () {
                  setState(() {
                    from = null;
                    to = null;
                    state = '';
                    branch = '';
                  });
                  load();
                },
                child: const UiText('مسح'),
              ),
              IconButton(
                onPressed: load,
                tooltip: 'تحديث',
                icon: const Icon(Icons.refresh),
              ),
            ],
          ),
          DropdownButtonFormField<String>(
            initialValue: state,
            isExpanded: true,
            decoration: const InputDecoration(labelText: 'الحالة'),
            items: [
              const DropdownMenuItem(value: '', child: UiText('كل الحالات')),
              const DropdownMenuItem(
                value: 'Open',
                child: UiText('المتابعات المفتوحة'),
              ),
              for (final e in followUpStates.entries)
                DropdownMenuItem(value: e.key, child: UiText(e.value)),
            ],
            onChanged: (v) {
              setState(() => state = v ?? '');
              load();
            },
            key: ValueKey('state:$state'),
          ),
          if (options != null)
            DropdownButtonFormField<String>(
              initialValue: branch,
              isExpanded: true,
              decoration: const InputDecoration(labelText: 'الفرع'),
              items: [
                const DropdownMenuItem(value: '', child: UiText('كل الفروع')),
                for (final b in rows(options!['branches']))
                  DropdownMenuItem(
                    value: textOf(b['id']),
                    child: UiText(textOf(b['name'])),
                  ),
              ],
              onChanged: (v) {
                setState(() => branch = v ?? '');
                load();
              },
              key: ValueKey('branch:$branch'),
            ),
          if (error.isNotEmpty) UiText(error),
          if (loading) const Center(child: CircularProgressIndicator()),
          if (!loading && data != null) ...[
            const UiText(
              'المتابعات واتخاذ القرار',
              style: TextStyle(fontWeight: FontWeight.bold, fontSize: 20),
            ),
            if (rows(data!['items']).isEmpty)
              const UiText('لا توجد متابعات ضمن الفلتر'),
            for (final f in rows(data!['items']))
              Card(
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      UiText(followUpConditions[f['condition']] ?? 'متابعة'),
                      UiText(
                        textOf(obj(f['source'] ?? <String, dynamic>{})['name']),
                      ),
                      UiText(
                        '${followUpStates[f['state']]}${f['overdue'] == true ? ' · متأخرة' : ''}',
                      ),
                      UiText(
                        f['conditionActive'] == true
                            ? 'الحالة الأصلية ما زالت قائمة'
                            : 'الحالة لم تعد تنطبق أو غير متاحة',
                      ),
                      FilledButton(
                        onPressed: () => open(textOf(f['id'])),
                        child: const UiText('إدارة المتابعة وسجلها'),
                      ),
                    ],
                  ),
                ),
              ),
            if (data!['hasMore'] == true)
              const UiText('أحدث 100 متابعة؛ ضيق الفترة أو الفرع لعرض الباقي.'),
          ],
          if (options != null)
            GLink('إنشاء متابعة', Icons.add_task, () async {
              final id = await Navigator.of(context).push<String>(
                MaterialPageRoute(
                  builder: (_) => OwnerFollowUpCreatePage(auth: widget.auth),
                ),
              );
              if (id != null &&
                  mounted &&
                  identity == followUpContext(widget.auth)) {
                await open(id);
              } else if (mounted) {
                await load();
              }
            }, subtitle: 'اختر مصدرًا ومسؤولًا وموعدًا في نموذج واحد'),
        ]),
      );
    },
  );
}

class OwnerFollowUpPage extends StatelessWidget {
  final AuthController auth;
  final String id;
  const OwnerFollowUpPage({super.key, required this.auth, required this.id});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'إدارة المتابعة',
    child: OwnerScopedRead(
      auth: auth,
      path: '/api/v1/manage/owner-followups/$id',
      builder: (context, value) => _FollowUpForm(
        key: ValueKey(obj(value)['item']['version']),
        auth: auth,
        data: obj(value),
      ),
    ),
  );
}

class _FollowUpForm extends StatefulWidget {
  final AuthController auth;
  final Json data;
  const _FollowUpForm({super.key, required this.auth, required this.data});
  @override
  State<_FollowUpForm> createState() => _FollowUpFormState();
}

class _FollowUpFormState extends State<_FollowUpForm> {
  late final identity = followUpContext(widget.auth),
      item = obj(widget.data['item']);
  late String state = textOf(item['state']);
  late String? owner = item['assignedToMembershipId'] as String?,
      due = item['dueOn'] as String?;
  late final note = TextEditingController(text: item['note'] as String? ?? ''),
      reason = TextEditingController(
        text: item['resolutionReason'] as String? ?? '',
      );
  String error = '';
  bool busy = false;
  String? draft, requestId;
  @override
  void dispose() {
    note.dispose();
    reason.dispose();
    super.dispose();
  }

  Future<void> save() async {
    if (busy) {
      return;
    }
    final body = {
          'state': state,
          'version': item['version'],
          'assignedToMembershipId': owner,
          'dueOn': due,
          'note': note.text,
          'resolutionReason': reason.text,
        },
        serialized = jsonEncode(body);
    if (draft != serialized) {
      draft = serialized;
      requestId = followUpRequestId();
    }
    setState(() => busy = true);
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/manage/owner-followups/${item['id']}',
        body: {...body, 'requestId': requestId},
      );
      if (mounted && identity == followUpContext(widget.auth)) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: UiText('تم حفظ المتابعة دون تعديل المصدر.')),
        );
        Navigator.pop(context);
      }
    } catch (e) {
      if (mounted && identity == followUpContext(widget.auth)) {
        setState(
          () => error = e is ApiFailure ? e.message : 'تعذر حفظ المتابعة.',
        );
      }
    } finally {
      if (mounted) {
        setState(() => busy = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final source = item['source'] is Map
            ? obj(item['source'])
            : <String, dynamic>{},
        owners = rows(widget.data['owners']);
    return followUpList([
      UiText(followUpConditions[item['condition']] ?? 'متابعة'),
      UiText(textOf(source['name'])),
      if (item['condition'] == 'GroupOverdueFeedback') ...[
        overdueFeedbackSummary(source),
        for (final id
            in (obj(item['evidence'])['feedbackIds'] as List? ?? []).take(3))
          TextButton(
            onPressed: () => openPage(
              context,
              OwnerFeedbackDetail(auth: widget.auth, id: id.toString()),
            ),
            child: const UiText('فتح ملاحظة من دليل الإنشاء'),
          ),
        const UiText(
          'أول 3 ملاحظات من دليل الإنشاء؛ إغلاق المتابعة لا يحل الملاحظة أو يرسل ردًا.',
        ),
      ],
      if (item['condition'] == 'GroupRecordedAbsences') ...[
        recordedAbsenceSummary(source),
        const UiText('دليل الإنشاء ثابت؛ إغلاق المتابعة لا يغير حضور أي لاعب.'),
        for (final record in {
          for (final r in rows(
            obj(item['evidence'])['absenceEvidence'] ?? <Json>[],
          ))
            r['SessionId']: r,
        }.values.take(3))
          TextButton(
            onPressed: () => openPage(
              context,
              CoachAttendance(
                auth: widget.auth,
                sessionId: textOf(record['SessionId']),
                title: 'كشف الحضور من دليل المتابعة',
              ),
            ),
            child: UiText('مراجعة كشف ${record['SessionDate']}'),
          ),
      ],
      if (item['condition'] == 'GroupEvaluationDrafts') ...[
        evaluationDraftSummary(source),
        const UiText(
          'تقييمات دليل الإنشاء؛ لا تُنشر أو تتغير عند إغلاق المتابعة.',
        ),
        if (source['evaluationDrafts'] != null)
          for (final id
              in (obj(item['evidence'])['evaluationIds'] as List? ?? []).take(
                3,
              ))
            TextButton(
              onPressed: () => openPage(
                context,
                FollowUpEvaluationSource(auth: widget.auth, id: id.toString()),
              ),
              child: const UiText('عرض تقييم من دليل الإنشاء'),
            ),
      ],
      if (item['condition'] == 'GroupSessionCoverage') coverageSummary(source),
      if (item['condition'] == 'GroupSessionCoverage' &&
          obj(item['evidence'] ?? <String, dynamic>{})['source'] is Map)
        ExpansionTile(
          title: const UiText('مواعيد الدليل وقت إنشاء المتابعة'),
          children: [
            coverageSummary({
              'coverageGaps': [
                for (final gap in rows(
                  obj(obj(item['evidence'])['source'])['CoverageGaps'] ??
                      <Json>[],
                ))
                  {
                    'id': gap['Id'],
                    'sessionDate': gap['SessionDate'],
                    'startTime': gap['StartTime'],
                    'endTime': gap['EndTime'],
                  },
              ],
            }),
          ],
        ),
      if (item['condition'] != 'RecordFollowUp')
        UiText(
          'تسجيلات مسندة حاليًا: ${source['enrollments'] ?? 'غير معروف'}، السعة: ${source['capacity'] ?? 'غير محددة'}',
        ),
      if (item['condition'] != 'RecordFollowUp')
        UiText(
          item['conditionActive'] == true
              ? 'الحالة الأصلية ما زالت قائمة — إغلاق المتابعة لا يحلها تلقائيًا.'
              : item['conditionActive'] == false
              ? 'الحالة الأصلية لم تعد تنطبق أو المصدر متوقف.'
              : 'حالة المصدر غير متاحة؛ ليست محلولة.',
        ),
      if (item['condition'] == 'RecordFollowUp')
        GLink(
          'فتح السجل المرتبط',
          Icons.open_in_new,
          () => openPage(
            context,
            item['sourceKind'] == 'Request'
                ? OwnerFeedbackDetail(
                    auth: widget.auth,
                    id: textOf(item['sourceId']),
                  )
                : OwnerRecordSource(
                    auth: widget.auth,
                    kind: textOf(item['sourceKind']),
                    id: textOf(item['sourceId']),
                  ),
          ),
        ),
      if (item['condition'] != 'RecordFollowUp')
        OutlinedButton(
          onPressed: () => Navigator.of(context).push(
            MaterialPageRoute<void>(
              builder: (_) => OwnerGroupOperations(
                auth: widget.auth,
                id: textOf(item['trainingGroupId']),
              ),
            ),
          ),
          child: const UiText('فتح المجموعة نفسها'),
        ),
      const UiText('لا يغيّر الحفظ المدرب أو اللاعبين أو السداد.'),
      DropdownButtonFormField<String>(
        initialValue: state,
        isExpanded: true,
        decoration: const InputDecoration(labelText: 'حالة المتابعة'),
        items: [
          for (final e in followUpStates.entries)
            DropdownMenuItem(value: e.key, child: UiText(e.value)),
        ],
        onChanged: busy
            ? null
            : (v) {
                setState(() {
                  state = v!;
                  if (state == 'New') {
                    owner = null;
                  }
                });
              },
      ),
      DropdownButtonFormField<String>(
        key: ValueKey(owner),
        initialValue: owner ?? '',
        isExpanded: true,
        decoration: const InputDecoration(labelText: 'المسؤول'),
        items: [
          const DropdownMenuItem(value: '', child: UiText('غير مسند')),
          for (final o in owners)
            DropdownMenuItem(
              value: textOf(o['id']),
              child: UiText(textOf(o['name'])),
            ),
          if (owner != null && !owners.any((o) => o['id'] == owner))
            DropdownMenuItem(
              value: owner,
              child: const UiText('مالك سابق / غير فعال'),
            ),
        ],
        onChanged: busy || state == 'New'
            ? null
            : (v) => setState(() => owner = v == '' ? null : v),
      ),
      OutlinedButton(
        onPressed: busy
            ? null
            : () async {
                final d = await showDatePicker(
                  context: context,
                  initialDate: DateTime.tryParse(due ?? '') ?? DateTime.now(),
                  firstDate: DateTime(2000),
                  lastDate: DateTime(2100),
                );
                if (d != null && mounted) {
                  setState(() => due = d.toIso8601String().split('T').first);
                }
              },
        child: UiText('موعد المتابعة: ${due ?? 'غير محدد'}'),
      ),
      TextButton(
        onPressed: busy ? null : () => setState(() => due = null),
        child: const UiText('مسح موعد المتابعة'),
      ),
      TextField(
        controller: note,
        enabled: !busy,
        maxLength: 500,
        maxLines: 3,
        decoration: const InputDecoration(labelText: 'ملاحظة اختيارية'),
      ),
      if (state == 'Closed' || state == 'Rejected')
        TextField(
          controller: reason,
          enabled: !busy,
          maxLength: 500,
          maxLines: 3,
          decoration: const InputDecoration(
            labelText: 'سبب إغلاق أو رفض المتابعة',
          ),
        ),
      if (error.isNotEmpty) UiText(error),
      FilledButton(
        onPressed: busy ? null : save,
        child: UiText(busy ? 'جارٍ الحفظ…' : 'حفظ المتابعة'),
      ),
      const UiText(
        'سجل المتابعة',
        style: TextStyle(fontWeight: FontWeight.bold, fontSize: 20),
      ),
      for (final h in rows(widget.data['history'])) ...[
        UiText(
          '${followUpStates[obj(jsonDecode(textOf(h['after'])))['State']]} · ${textOf(h['actorName'])}',
        ),
        UiText(textOf(h['createdAtUtc'])),
        if (obj(jsonDecode(textOf(h['after'])))['ResolutionReason'] != null)
          UiText(
            textOf(obj(jsonDecode(textOf(h['after'])))['ResolutionReason']),
          ),
      ],
    ]);
  }
}

class OwnerFollowUpCreatePage extends StatelessWidget {
  final AuthController auth;
  const OwnerFollowUpCreatePage({super.key, required this.auth});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'إنشاء متابعة',
    child: Column(
      children: [
        Padding(
          padding: const EdgeInsets.all(16),
          child: GLink(
            'متابعة لاعب أو موظف أو ولي أمر أو طلب أو مطالبة',
            Icons.playlist_add,
            () async {
              final id = await Navigator.of(context).push<String>(
                MaterialPageRoute(
                  builder: (_) => OwnerRecordFollowUpCreate(auth: auth),
                ),
              );
              if (id != null && context.mounted) Navigator.of(context).pop(id);
            },
          ),
        ),
        Expanded(
          child: OwnerScopedRead(
            auth: auth,
            path: '/api/v1/manage/owner-followups/options',
            builder: (context, value) =>
                _NewFollowUpForm(auth: auth, data: obj(value)),
          ),
        ),
      ],
    ),
  );
}

class _NewFollowUpForm extends StatefulWidget {
  final AuthController auth;
  final Json data;
  const _NewFollowUpForm({required this.auth, required this.data});
  @override
  State<_NewFollowUpForm> createState() => _NewFollowUpFormState();
}

class _NewFollowUpFormState extends State<_NewFollowUpForm> {
  late final identity = followUpContext(widget.auth);
  final note = TextEditingController();
  String selected = '', owner = '', error = '';
  String? due, signature, requestId;
  bool busy = false;
  @override
  void dispose() {
    note.dispose();
    super.dispose();
  }

  Future<void> save(Json candidate) async {
    if (busy) return;
    final source = obj(candidate['source']),
        body = {
          'trainingGroupId': source['groupId'],
          'condition': candidate['condition'],
          'assignedToMembershipId': owner.isEmpty ? null : owner,
          'dueOn': due,
          'note': note.text.trim().isEmpty ? null : note.text.trim(),
        },
        encoded = jsonEncode(body);
    if (signature != encoded) {
      signature = encoded;
      requestId = followUpRequestId();
    }
    setState(() => busy = true);
    try {
      final saved = obj(
        await widget.auth.request(
          'POST',
          '/api/v1/manage/owner-followups/',
          body: {...body, 'requestId': requestId},
        ),
      );
      if (mounted && identity == followUpContext(widget.auth)) {
        Navigator.of(context).pop(textOf(saved['id']));
      }
    } catch (e) {
      if (mounted && identity == followUpContext(widget.auth)) {
        setState(
          () => error = e is ApiFailure
              ? e.message
              : 'تعذر تأكيد حفظ المتابعة؛ أعد نفس الطلب.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final candidates = rows(widget.data['candidates']),
        candidate = candidates
            .where(
              (c) =>
                  '${obj(c['source'])['groupId']}:${c['condition']}' ==
                  selected,
            )
            .firstOrNull;
    return followUpList([
      if (error.isNotEmpty) UiText(error),
      DropdownButtonFormField<String>(
        initialValue: selected,
        decoration: const InputDecoration(labelText: 'مصدر المتابعة'),
        items: [
          const DropdownMenuItem(
            value: '',
            child: UiText('اختر حالة تشغيل فعلية'),
          ),
          for (final c in candidates)
            DropdownMenuItem(
              value: '${obj(c['source'])['groupId']}:${c['condition']}',
              child: UiText(
                '${obj(c['source'])['name']} · ${followUpConditions[c['condition']]}',
              ),
            ),
        ],
        onChanged: busy
            ? null
            : (v) => setState(() {
                selected = v ?? '';
                owner = '';
              }),
      ),
      if (candidates.isEmpty)
        const UiText('لا توجد حالات تشغيل تنطبق عليها القواعد الحالية.'),
      if (candidate != null) ...[
        if (candidate['activeFollowUpId'] != null)
          FilledButton(
            onPressed: () =>
                Navigator.of(context)
                    .pop(textOf(candidate['activeFollowUpId'])),
            child: const UiText('فتح المتابعة القائمة'),
          )
        else ...[
          DropdownButtonFormField<String>(
            key: ValueKey(selected),
            initialValue: owner,
            decoration: const InputDecoration(labelText: 'المسؤول'),
            items: [
              const DropdownMenuItem(value: '', child: UiText('غير مسند الآن')),
              for (final o in rows(widget.data['owners']))
                DropdownMenuItem(
                  value: textOf(o['id']),
                  child: UiText(textOf(o['name'])),
                ),
            ],
            onChanged: busy ? null : (v) => setState(() => owner = v ?? ''),
          ),
          OutlinedButton(
            onPressed: busy
                ? null
                : () async {
                    final date = await showDatePicker(
                      context: context,
                      initialDate: DateTime.now(),
                      firstDate: DateTime(2000),
                      lastDate: DateTime(2100),
                    );
                    if (date != null &&
                        mounted &&
                        identity == followUpContext(widget.auth)) {
                      setState(
                        () => due = date.toIso8601String().split('T').first,
                      );
                    }
                  },
            child: UiText('موعد المتابعة: ${due ?? 'لم يحدد'}'),
          ),
          TextField(
            controller: note,
            maxLength: 500,
            maxLines: 3,
            enabled: !busy,
            decoration: const InputDecoration(labelText: 'ملاحظة (اختياري)'),
          ),
          FilledButton(
            onPressed: busy ? null : () => save(candidate),
            child: UiText(busy ? 'جارٍ الحفظ…' : 'حفظ المتابعة'),
          ),
        ],
      ],
      const UiText('لا تحصيل أو تغيير حضور أو نشر تقييم من إنشاء متابعة.'),
    ]);
  }
}
