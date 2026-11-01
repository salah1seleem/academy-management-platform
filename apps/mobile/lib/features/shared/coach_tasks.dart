import 'dart:convert';

import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../coach/coach_home.dart';
import '../guardian/guardian_ui.dart';
import '../owner/owner_followups.dart';
import 'staff_capabilities.dart';

const coachTaskKinds = {
  'Attendance': 'متابعة الحضور',
  'Evaluation': 'متابعة التقييم',
};
const coachTaskPermission = 'workflow.coach-tasks.assign';

class CoachTasksEntry extends StatelessWidget {
  const CoachTasksEntry({super.key, required this.auth});
  final AuthController auth;
  @override
  Widget build(BuildContext context) {
    final role = auth.session?.selected?.role;
    Widget link() => GLink(
      'مهام الحضور والتقييم',
      Icons.task_alt,
      () => openPage(context, CoachTasksPage(auth: auth)),
    );
    if (role == 'Coach' || role == 'AcademyOwner') return link();
    if (role != 'AcademyAdmin') return const SizedBox.shrink();
    return FutureBuilder<StaffCapabilities>(
      future: StaffCapabilities.load(auth),
      builder: (context, s) {
        final access = s.data;
        final leaf = rows(access?.scope['workflowActions'] ?? [])
            .where((g) => g['key'] == coachTaskPermission)
            .firstOrNull;
        return access?.explicitAction('followups', 'Edit') == true &&
                leaf?['hasExplicitGrant'] == true &&
                leaf?['allowed'] == true
            ? link()
            : const SizedBox.shrink();
      },
    );
  }
}

class CoachTasksPage extends StatelessWidget {
  const CoachTasksPage({
    super.key,
    required this.auth,
    this.id,
    this.create = false,
  });
  final AuthController auth;
  final String? id;
  final bool create;
  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: auth,
    builder: (context, _) => followUpContext(auth) == null
        ? const SizedBox.shrink()
        : _Tasks(
            key: ValueKey('${followUpContext(auth)}:$id:$create'),
            auth: auth,
            id: id,
            create: create,
          ),
  );
}

class _Tasks extends StatefulWidget {
  const _Tasks({super.key, required this.auth, this.id, required this.create});
  final AuthController auth;
  final String? id;
  final bool create;
  @override
  State<_Tasks> createState() => _TasksState();
}

class _TasksState extends State<_Tasks> with WidgetsBindingObserver {
  late final bool coach = widget.auth.session!.selected!.role == 'Coach';
  late final String base = coach
      ? '/api/v1/my-coach-tasks'
      : '/api/v1/manage/coach-tasks';
  Json? data, selection;
  String error = '',
      filter = 'Open',
      kind = 'Attendance',
      state = 'InProgress',
      search = '';
  String? due, signature, requestId;
  final note = TextEditingController(), reason = TextEditingController();
  int page = 1, generation = 0;
  bool busy = false, reading = false;
  final listScroll = ScrollController();
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    listScroll.addListener(() {
      if (widget.id == null &&
          listScroll.hasClients &&
          listScroll.position.extentAfter < 220 &&
          !reading &&
          !busy &&
          error.isEmpty &&
          data?['hasMore'] == true) {
        load(more: true);
      }
    });
    load();
  }

  @override
  void dispose() {
    generation++;
    listScroll.dispose();
    WidgetsBinding.instance.removeObserver(this);
    note.dispose();
    reason.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState value) {
    if (value == AppLifecycleState.resumed) load();
  }

  Future<void> load({bool more = false}) async {
    if (more && (reading || busy || data?['hasMore'] != true)) return;
    final target = more ? page + 1 : 1;
    final prior = more ? rows(data?['items'] ?? []) : <Json>[];
    final g = ++generation;
    setState(() {
      if (!more) data = null;
      reading = true;
      error = '';
    });
    try {
      final query = Uri(
        queryParameters: {
          'page': '$target',
          'pageSize': '25',
          if (widget.create) 'search': search else 'state': filter,
        },
      ).query;
      final d = obj(
        await widget.auth.request(
          'GET',
          widget.id != null
              ? '$base/${widget.id}'
              : widget.create
              ? '$base/options?$query'
              : '$base?$query',
        ),
      );
      if (mounted && generation == g) {
        setState(() {
          if (more) {
            final known = prior
                .map((r) => r['id'] ?? '${r['groupId']}:${r['coachId']}')
                .toSet();
            final added = rows(d['items'])
                .where(
                  (r) =>
                      known.add(r['id'] ?? '${r['groupId']}:${r['coachId']}'),
                )
                .toList();
            data = {
              ...d,
              'items': [...prior, ...added],
              if (added.isEmpty) 'hasMore': false,
            };
          } else {
            data = d;
          }
          page = target;
          if (widget.id != null) {
            note.text = d['note'] as String? ?? '';
            due = d['dueOn'] as String?;
          }
        });
      }
    } catch (e) {
      if (e is ApiFailure && [401, 403, 404].contains(e.status)) data = null;
      if (mounted && generation == g) {
        setState(
          () => error = e is ApiFailure
              ? e.message
              : 'تعذر تحميل المهام. أعد المحاولة.',
        );
      }
    } finally {
      if (mounted && generation == g) setState(() => reading = false);
    }
  }

  Future<void> date() async {
    try {
      final calendar = obj(
        await widget.auth.request('GET', '/api/v1/academy/calendar'),
      );
      if (!mounted) return;
      final today = DateTime.parse(textOf(calendar['today']));
      final d = await showDatePicker(
        context: context,
        initialDate: DateTime.tryParse(due ?? '') ?? today,
        firstDate: today,
        lastDate: today.add(const Duration(days: 3650)),
      );
      if (d != null && mounted) {
        setState(() => due = d.toIso8601String().split('T').first);
      }
    } catch (_) {
      if (mounted) {
        setState(() => error = 'تعذر قراءة تقويم الأكاديمية. أعد المحاولة.');
      }
    }
  }

  Future<void> save() async {
    if (busy || widget.create && selection == null) return;
    final body = widget.create
        ? <String, Object?>{
            'groupId': selection!['groupId'],
            'coachId': selection!['coachId'],
            'kind': kind,
            'dueOn': due,
            'note': note.text.trim(),
          }
        : <String, Object?>{
            'state': state,
            'version': data!['version'],
            'dueOn': due,
            'note': note.text.trim(),
            'resolutionReason': reason.text.trim(),
          };
    final value = jsonEncode(body);
    if (signature != value) {
      signature = value;
      requestId = followUpRequestId();
    }
    setState(() {
      busy = true;
      error = '';
    });
    try {
      final result = obj(
        await widget.auth.request(
          'POST',
          widget.create ? base : '$base/${widget.id}',
          body: {...body, 'requestId': requestId},
        ),
      );
      final id = widget.id ?? textOf(result['id']);
      final read = obj(await widget.auth.request('GET', '$base/$id'));
      if (widget.create
          ? read['assignedToMembershipId'] != selection!['coachId'] ||
                read['kind'] != kind
          : read['state'] != state) {
        throw StateError('read mismatch');
      }
      if (!mounted) return;
      if (widget.create) {
        Navigator.of(context).pop(id);
        return;
      }
      setState(() {
        data = read;
        signature = null;
        requestId = null;
      });
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: UiText('حُفظت المهمة وأُعيدت قراءتها.')),
      );
    } catch (e) {
      if (mounted) {
        setState(
          () => error = e is ApiFailure
              ? e.message
              : 'تعذر تأكيد حفظ المهمة. أعد المحاولة أو حدّث التفاصيل.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  Future<void> open(String? id, {bool create = false}) async {
    await openPage(
      context,
      CoachTasksPage(auth: widget.auth, id: id, create: create),
    );
    if (mounted) await load();
  }

  Widget summary(Json item) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Heading(coachTaskKinds[item['kind']] ?? 'مهمة'),
      UiText(
        widget.id == null
            ? textOf(item['group'])
            : textOf(obj(item['group'])['arabicName']),
      ),
      if (item['coach'] != null) UiText(textOf(item['coach'])),
      UiText(
        '${followUpStates[item['state']] ?? ''} · الموعد: ${day(item['dueOn'])}',
      ),
      if ((item['note'] as String? ?? '').isNotEmpty)
        UiText(textOf(item['note'])),
      if ((item['resolutionReason'] as String? ?? '').isNotEmpty)
        UiText(textOf(item['resolutionReason'])),
    ],
  );
  List<Widget> paging() => [if (reading) const LinearProgressIndicator()];
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: widget.create ? 'إسناد مهمة للمدرب' : 'مهام الحضور والتقييم',
    child: AbsorbPointer(
      absorbing: busy,
      child: followUpList(
        [
          const UiText(
            'المهمة متابعة للحضور أو التقييم داخل المجموعة. تُسجّل النتائج من إجراءات الحضور والتقييم المعتادة.',
          ),
          if (error.isNotEmpty) UiText(error),
          if (data == null) ...[
            if (error.isEmpty) const Center(child: CircularProgressIndicator()),
            TextButton(onPressed: load, child: const UiText('إعادة المحاولة')),
          ] else if (widget.create) ...[
            TextFormField(
              initialValue: search,
              decoration: const InputDecoration(
                labelText: 'بحث بالمجموعة أو المدرب',
              ),
              onFieldSubmitted: (v) {
                search = v;
                page = 1;
                load();
              },
            ),
            for (final row in rows(data!['items']))
              GCard(
                child: ListTile(
                  selected:
                      selection?['groupId'] == row['groupId'] &&
                      selection?['coachId'] == row['coachId'],
                  leading: Icon(
                    selection?['groupId'] == row['groupId'] &&
                            selection?['coachId'] == row['coachId']
                        ? Icons.radio_button_checked
                        : Icons.radio_button_off,
                  ),
                  title: UiText('${row['group']} · ${row['coach']}'),
                  subtitle: UiText('${row['branch']} · ${row['sport']}'),
                  onTap: () => setState(() => selection = row),
                ),
              ),
            ...paging(),
            if (selection != null)
              UiText(
                'المختار: ${selection!['group']} · ${selection!['coach']}',
              ),
            DropdownButtonFormField<String>(
              initialValue: kind,
              decoration: const InputDecoration(labelText: 'نوع المهمة'),
              items: [
                for (final k in coachTaskKinds.entries)
                  DropdownMenuItem(value: k.key, child: UiText(k.value)),
              ],
              onChanged: (v) => setState(() => kind = v!),
            ),
            ...form(),
          ] else if (widget.id != null) ...[
            GCard(child: summary(data!)),
            if (coach)
              OutlinedButton.icon(
                onPressed: () => openPage(
                  context,
                  CoachGroup(auth: widget.auth, group: obj(data!['group'])),
                ),
                icon: const Icon(Icons.groups_outlined),
                label: const UiText('فتح المجموعة للحضور والتقييم'),
              ),
            if (!['Closed', 'Rejected'].contains(data!['state'])) ...[
              DropdownButtonFormField<String>(
                initialValue: state,
                decoration: const InputDecoration(labelText: 'تحديث الحالة'),
                items: [
                  for (final k in [
                    'InProgress',
                    'Snoozed',
                    'Closed',
                    'Rejected',
                  ])
                    DropdownMenuItem(
                      value: k,
                      child: UiText(followUpStates[k]!),
                    ),
                ],
                onChanged: (v) => setState(() => state = v!),
              ),
              ...form(),
            ],
            const Heading('سجل المهمة'),
            for (final h in rows(data!['history']))
              GCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    UiText(taskTime(context, h['createdAtUtc'])),
                    const UiText(
                      'بتوقيت الجهاز',
                      style: TextStyle(fontSize: 12),
                    ),
                    for (final after in [
                      obj(jsonDecode(textOf(h['after']))),
                    ]) ...[
                      UiText(followUpStates[after['State']] ?? ''),
                      if (after['Note'] != null) UiText(textOf(after['Note'])),
                      if (after['ResolutionReason'] != null)
                        UiText(textOf(after['ResolutionReason'])),
                    ],
                  ],
                ),
              ),
          ] else ...[
            if (!coach)
              FilledButton.icon(
                onPressed: () => open(null, create: true),
                icon: const Icon(Icons.add),
                label: const UiText('إسناد مهمة للمدرب'),
              ),
            DropdownButtonFormField<String>(
              initialValue: filter,
              decoration: const InputDecoration(labelText: 'الحالة'),
              items: [
                const DropdownMenuItem(
                  value: 'Open',
                  child: UiText('المفتوحة'),
                ),
                const DropdownMenuItem(value: '', child: UiText('كل الحالات')),
              ],
              onChanged: (v) {
                filter = v!;
                page = 1;
                load();
              },
            ),
            if (rows(data!['items']).isEmpty)
              const UiText('لا توجد مهام ضمن الفلتر الحالي.'),
            for (final item in rows(data!['items']))
              GCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    summary(item),
                    TextButton(
                      onPressed: () => open(textOf(item['id'])),
                      child: const UiText('فتح المهمة'),
                    ),
                  ],
                ),
              ),
            ...paging(),
          ],
          if (busy) const Center(child: CircularProgressIndicator()),
          if (data != null)
            TextButton(onPressed: load, child: const UiText('تحديث المهام')),
        ],
        controller: listScroll,
        onNearEnd: () {
          if (widget.id == null &&
              !reading &&
              !busy &&
              error.isEmpty &&
              data?['hasMore'] == true) {
            load(more: true);
          }
        },
      ),
    ),
  );
  String taskTime(BuildContext context, Object? value) {
    final stamp = DateTime.tryParse(textOf(value))?.toLocal();
    if (stamp == null) return 'غير متاح';
    final locale = MaterialLocalizations.of(context);
    return '${locale.formatMediumDate(stamp)} · ${locale.formatTimeOfDay(TimeOfDay.fromDateTime(stamp))}';
  }

  List<Widget> form() => [
    TextField(
      controller: note,
      maxLength: 500,
      decoration: const InputDecoration(labelText: 'ملاحظة المتابعة'),
    ),
    if (!widget.create && ['Closed', 'Rejected'].contains(state))
      TextField(
        controller: reason,
        maxLength: 500,
        decoration: const InputDecoration(labelText: 'سبب الإغلاق أو الرفض'),
      ),
    TextButton(onPressed: date, child: UiText('الموعد: ${due ?? 'غير محدد'}')),
    if (due != null)
      TextButton(
        onPressed: () => setState(() => due = null),
        child: const UiText('مسح الموعد'),
      ),
    FilledButton(
      onPressed: widget.create && selection == null ? null : save,
      child: UiText(widget.create ? 'تأكيد الإسناد' : 'حفظ حالة المهمة'),
    ),
  ];
}
