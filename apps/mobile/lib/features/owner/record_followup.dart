import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'owner_followups.dart';
import 'owner_operations.dart';

const recordFollowUpKinds = {
  'Player': 'لاعب',
  'Employee': 'موظف',
  'Guardian': 'ولي أمر',
  'Request': 'طلب أسرة',
  'Receivable': 'مطالبة مالية',
};
String recordKey(Json s) =>
    '${s['id']}/${s['branchId'] ?? ''}/${s['sportId'] ?? ''}';

class OwnerRecordFollowUpCreate extends StatefulWidget {
  const OwnerRecordFollowUpCreate({super.key, required this.auth});
  final AuthController auth;
  @override
  State<OwnerRecordFollowUpCreate> createState() =>
      _OwnerRecordFollowUpCreateState();
}

class _OwnerRecordFollowUpCreateState extends State<OwnerRecordFollowUpCreate> {
  String kind = 'Player',
      search = '',
      selection = '',
      owner = '',
      due = '',
      note = '',
      error = '';
  int priority = 1;
  bool busy = false;
  Json? selected;
  String? attemptSignature, attemptId;
  late final identity = followUpContext(widget.auth);
  Future<void> save() async {
    if (busy || selected == null || identity != followUpContext(widget.auth)) {
      return;
    }
    final body = {
      'kind': kind,
      'sourceId': selected!['id'],
      'branchId': selected!['branchId'],
      'sportId': selected!['sportId'],
      'assignedToMembershipId': owner.isEmpty ? null : owner,
      'dueOn': due.isEmpty ? null : due,
      'note': note.trim().isEmpty ? null : note.trim(),
      'priority': priority,
    };
    final signature = body.toString();
    if (attemptSignature != signature) {
      attemptSignature = signature;
      attemptId = followUpRequestId();
    }
    setState(() {
      busy = true;
      error = '';
    });
    try {
      final result = obj(
        await widget.auth.request(
          'POST',
          '/api/v1/manage/owner-followups/records',
          body: {...body, 'requestId': attemptId},
        ),
      );
      if (mounted && identity == followUpContext(widget.auth)) {
        Navigator.of(context).pop(textOf(result['id']));
      }
    } catch (e) {
      if (mounted) {
        setState(
          () => error = e is ApiFailure ? e.message : 'تعذر حفظ المتابعة.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final q = Uri(
      queryParameters: {
        'kind': kind,
        'search': search,
        if (selected != null) 'sourceId': textOf(selected!['id']),
        if (selected?['branchId'] != null)
          'branchId': textOf(selected!['branchId']),
        if (selected?['sportId'] != null)
          'sportId': textOf(selected!['sportId']),
      },
    ).query;
    return GuardianPage(
      title: 'إنشاء متابعة لسجل',
      child: OwnerScopedRead(
        auth: widget.auth,
        path: '/api/v1/manage/owner-followups/records?$q',
        builder: (context, value) {
          final data = obj(value), items = rows(data['items']);
          return followUpList([
            if (error.isNotEmpty) UiText(error),
            DropdownButtonFormField<String>(
              initialValue: kind,
              decoration: const InputDecoration(labelText: 'نوع السجل'),
              items: [
                for (final e in recordFollowUpKinds.entries)
                  DropdownMenuItem(value: e.key, child: UiText(e.value)),
              ],
              onChanged: busy
                  ? null
                  : (v) => setState(() {
                      kind = v!;
                      selection = '';
                      selected = null;
                      owner = '';
                    }),
            ),
            TextFormField(
              initialValue: search,
              decoration: const InputDecoration(
                labelText: 'البحث بالاسم أو الهاتف',
                suffixIcon: Icon(Icons.search),
              ),
              textInputAction: TextInputAction.search,
              onFieldSubmitted: busy
                  ? null
                  : (v) => setState(() {
                      search = v;
                      selection = '';
                      selected = null;
                      owner = '';
                    }),
            ),
            DropdownButtonFormField<String>(
              key: ValueKey('$kind:$search:$selection'),
              initialValue: selection,
              isExpanded: true,
              decoration: const InputDecoration(labelText: 'السجل المرتبط'),
              items: [
                const DropdownMenuItem(value: '', child: UiText('اختر السجل')),
                for (final s in items)
                  DropdownMenuItem(
                    value: recordKey(s),
                    child: UiText(
                      '${s['name']}${s['branch'] == null ? '' : ' · ${s['branch']}'}${s['sport'] == null ? '' : ' · ${s['sport']}'}',
                      maxLines: 2,
                    ),
                  ),
              ],
              onChanged: busy
                  ? null
                  : (v) => setState(() {
                      selection = v ?? '';
                      selected = items
                          .where((s) => recordKey(s) == selection)
                          .firstOrNull;
                      owner = '';
                    }),
            ),
            if (items.isEmpty) const UiText('لا سجلات مطابقة. عدل البحث.'),
            if (selected != null) ...[
              DropdownButtonFormField<String>(
                key: ValueKey('$selection:$owner'),
                initialValue: owner,
                decoration: const InputDecoration(labelText: 'المسؤول'),
                items: [
                  const DropdownMenuItem(
                    value: '',
                    child: UiText('غير مسند الآن'),
                  ),
                  for (final o in rows(data['owners']))
                    DropdownMenuItem(
                      value: textOf(o['id']),
                      child: UiText(textOf(o['name'])),
                    ),
                ],
                onChanged: busy ? null : (v) => setState(() => owner = v ?? ''),
              ),
              DropdownButtonFormField<int>(
                initialValue: priority,
                decoration: const InputDecoration(labelText: 'الأولوية'),
                items: const [
                  DropdownMenuItem(value: 1, child: UiText('عادية')),
                  DropdownMenuItem(value: 2, child: UiText('عالية')),
                ],
                onChanged: busy ? null : (v) => setState(() => priority = v!),
              ),
              OutlinedButton.icon(
                icon: const Icon(Icons.event_outlined),
                label: UiText(due.isEmpty ? 'موعد المتابعة — اختياري' : due),
                onPressed: busy
                    ? null
                    : () async {
                        final d = await showDatePicker(
                          context: context,
                          firstDate: DateTime(2000),
                          lastDate: DateTime(2100),
                          initialDate: DateTime.now(),
                        );
                        if (d != null && mounted) {
                          setState(() => due = day(d.toIso8601String()));
                        }
                      },
              ),
              if (due.isNotEmpty)
                TextButton(
                  onPressed: busy ? null : () => setState(() => due = ''),
                  child: const UiText('مسح الموعد'),
                ),
              TextFormField(
                initialValue: note,
                maxLength: 500,
                maxLines: 3,
                decoration: const InputDecoration(
                  labelText: 'الملاحظة — اختياري',
                ),
                enabled: !busy,
                onChanged: (v) => note = v,
              ),
              FilledButton(
                onPressed: busy ? null : save,
                child: UiText(busy ? 'جارٍ الحفظ…' : 'حفظ المتابعة'),
              ),
            ],
            UiText(textOf(data['notice'])),
          ]);
        },
      ),
    );
  }
}

class OwnerRecordSource extends StatelessWidget {
  const OwnerRecordSource({
    super.key,
    required this.auth,
    required this.kind,
    required this.id,
  });
  final AuthController auth;
  final String kind, id;
  @override
  Widget build(BuildContext context) {
    final path = switch (kind) {
      'Employee' => '/api/v1/manage/employees/$id',
      'Guardian' => '/api/v1/people/guardians/$id',
      'Receivable' => '/api/v1/finance/receivables/$id',
      _ => '/api/v1/people/players/$id',
    };
    return GuardianPage(
      title: 'السجل المرتبط بالمتابعة',
      child: OwnerScopedRead(
        auth: auth,
        path: path,
        builder: (context, value) {
          final d = obj(value),
              record = kind == 'Receivable' ? obj(d['document']) : d;
          return gList([
            Heading(
              textOf(
                record['name'] ??
                    record['arabicName'] ??
                    record['displayName'] ??
                    record['playerNameSnapshot'],
              ),
            ),
            if (kind == 'Player')
              Facts([
                ('كود اللاعب', textOf(record['playerCode'])),
                ('تاريخ الميلاد', day(record['dateOfBirth'])),
              ]),
            if (kind == 'Guardian') ...[
              Facts([('الهاتف', textOf(record['contactPhone']))]),
              for (final child in rows(record['children']))
                GLink(
                  textOf(child['arabicName']),
                  Icons.person_outline,
                  () => openPage(
                    context,
                    OwnerRecordSource(
                      auth: auth,
                      kind: 'Player',
                      id: textOf(child['id']),
                    ),
                  ),
                ),
            ],
            if (kind == 'Employee')
              Facts([
                ('الهاتف', textOf(record['phone'])),
                ('الوظيفة', textOf(record['jobTitle'])),
                ('الفرع', textOf(record['branchName'])),
                (
                  'الراتب الساري',
                  money(record['monthlySalary'], record['currency']),
                ),
              ]),
            if (kind == 'Receivable')
              Facts([
                ('الوصف', textOf(record['description'])),
                ('المبلغ المثبت', money(record['amount'], record['currency'])),
                (
                  'المتبقي',
                  money(obj(d['balance'])['outstanding'], record['currency']),
                ),
                ('تاريخ الاستحقاق', day(record['dueDate'])),
              ]),
            const UiText(
              'المتابعة لا تعدل هذا السجل. الإجراءات الكاملة متاحة في ملفه داخل لوحة الويب بحسب الصلاحية.',
            ),
          ]);
        },
      ),
    );
  }
}
