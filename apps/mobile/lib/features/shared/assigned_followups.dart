import 'dart:convert';

import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../owner/owner_followups.dart';

class AssignedFollowUpsPage extends StatelessWidget {
  final AuthController auth;
  final String? id;
  const AssignedFollowUpsPage({super.key, required this.auth, this.id});
  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: auth,
    builder: (context, _) => auth.session?.selected == null
        ? const GuardianPage(
            title: 'متابعاتي المسندة',
            child: Center(child: UiText('سجل الدخول لعرض متابعاتك.')),
          )
        : _AssignedReader(
            key: ValueKey('${followUpContext(auth)}:$id'),
            auth: auth,
            id: id,
          ),
  );
}

class _AssignedReader extends StatefulWidget {
  final AuthController auth;
  final String? id;
  const _AssignedReader({super.key, required this.auth, this.id});
  @override
  State<_AssignedReader> createState() => _AssignedReaderState();
}

class _AssignedReaderState extends State<_AssignedReader>
    with WidgetsBindingObserver {
  Json? data;
  String error = '', state = 'Open';
  String? from, to;
  bool initialized = false;
  int generation = 0;
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    load();
  }

  @override
  void dispose() {
    generation++;
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState value) {
    if (value == AppLifecycleState.resumed) load();
  }

  Future<void> load() async {
    final request = ++generation;
    setState(() {
      data = null;
      error = '';
    });
    try {
      final query = Uri(
        queryParameters: {
          'from': ?from,
          'to': ?to,
          if (initialized && from == null && to == null) 'allDates': 'true',
          'state': state,
        },
      ).query;
      final value = obj(
        await widget.auth.request(
          'GET',
          widget.id == null
              ? '/api/v1/my-followups?$query'
              : '/api/v1/my-followups/${widget.id}',
        ),
      );
      if (!mounted || request != generation) return;
      setState(() {
        data = value;
        if (!initialized) {
          from = value['from'] as String?;
          to = value['to'] as String?;
          initialized = true;
        }
      });
    } catch (_) {
      if (mounted && request == generation) {
        setState(
          () => error = 'المتابعات غير متاحة ضمن إسنادك وصلاحياتك الحالية.',
        );
      }
    }
  }

  Future<void> date(bool start) async {
    final d = await showDatePicker(
      context: context,
      initialDate:
          DateTime.tryParse((start ? from : to) ?? '') ?? DateTime.now(),
      firstDate: DateTime(2000),
      lastDate: DateTime(2100),
    );
    if (d == null || !mounted) return;
    setState(() {
      if (start) {
        from = d.toIso8601String().split('T').first;
      } else {
        to = d.toIso8601String().split('T').first;
      }
    });
    await load();
  }

  Future<void> open(Json item) async {
    await openPage(
      context,
      AssignedFollowUpsPage(auth: widget.auth, id: textOf(item['id'])),
    );
    if (mounted) await load();
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: widget.id == null ? 'متابعاتي المسندة' : 'تفاصيل المتابعة المسندة',
    child: followUpList([
      const UiText(
        'مهامك داخل نطاق فرعك ورياضتك. تحديثها لا يغيّر الحضور أو الإسناد أو أي مبلغ.',
      ),
      if (widget.id == null)
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Wrap(
                spacing: 12,
                children: [
                  TextButton(
                    onPressed: () => date(true),
                    child: UiText('من تاريخ: ${from ?? "الكل"}'),
                  ),
                  TextButton(
                    onPressed: () => date(false),
                    child: UiText('إلى تاريخ: ${to ?? "الكل"}'),
                  ),
                ],
              ),
              DropdownButtonFormField<String>(
                key: ValueKey(state),
                initialValue: state,
                decoration: const InputDecoration(labelText: 'الحالة'),
                items: [
                  const DropdownMenuItem(
                    value: '',
                    child: UiText('كل الحالات'),
                  ),
                  const DropdownMenuItem(
                    value: 'Open',
                    child: UiText('المفتوحة'),
                  ),
                  for (final entry in followUpStates.entries)
                    DropdownMenuItem(
                      value: entry.key,
                      child: UiText(entry.value),
                    ),
                ],
                onChanged: (v) {
                  setState(() => state = v ?? '');
                  load();
                },
              ),
              TextButton(
                onPressed: () {
                  setState(() {
                    from = null;
                    to = null;
                    state = '';
                    initialized = true;
                  });
                  load();
                },
                child: const UiText('مسح الفلاتر'),
              ),
            ],
          ),
        ),
      if (error.isNotEmpty)
        UiText(error)
      else if (data == null)
        const Center(child: CircularProgressIndicator())
      else if (widget.id != null) ...[
        _Summary(item: obj(data!['item'])),
        if (data!['canEdit'] == true)
          _Update(
            key: ValueKey('${widget.id}:${obj(data!['item'])['version']}'),
            auth: widget.auth,
            item: obj(data!['item']),
            saved: () async {
              ScaffoldMessenger.of(context).showSnackBar(
                const SnackBar(
                  content: UiText('تم تحديث المتابعة وتسجيل الإجراء.'),
                ),
              );
              await load();
            },
          )
        else
          const UiText(
            'للقراءة فقط أو متابعة مغلقة. الإسناد وإعادة الفتح لدى المالك.',
          ),
        const Heading('سجل المتابعة'),
        for (final h in rows(data!['history'])) _History(change: h),
      ] else ...[
        if (rows(data!['items']).isEmpty)
          const UiText('لا توجد متابعات مسندة إليك ضمن الفلتر.'),
        for (final item in rows(data!['items']))
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                _Summary(item: item),
                TextButton(
                  onPressed: () => open(item),
                  child: const UiText('فتح المتابعة'),
                ),
              ],
            ),
          ),
        if (data!['hasMore'] == true)
          const UiText('تظهر أول 100 متابعة؛ ضيق الفترة لعرض الباقي.'),
      ],
      TextButton(onPressed: load, child: const UiText('تحديث المتابعات')),
    ]),
  );
}

class _History extends StatelessWidget {
  final Json change;
  const _History({required this.change});
  @override
  Widget build(BuildContext context) {
    final after = obj(jsonDecode(textOf(change['after'])));
    final stamp = DateTime.tryParse(textOf(change['createdAtUtc']))?.toLocal();
    String two(int value) => value.toString().padLeft(2, '0');
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          UiText(followUpStates[after['State']] ?? ''),
          const UiText('بتوقيت الجهاز', style: TextStyle(fontSize: 12)),
          Directionality(
            textDirection: TextDirection.ltr,
            child: UiText(
              stamp == null
                  ? '—'
                  : '${stamp.year}-${two(stamp.month)}-${two(stamp.day)} · ${two(stamp.hour)}:${two(stamp.minute)}',
              style: const TextStyle(fontSize: 13),
            ),
          ),
          for (final key in ['Note', 'ResolutionReason'])
            if (after[key] is String &&
                (after[key] as String).trim().isNotEmpty)
              UiText(after[key] as String),
        ],
      ),
    );
  }
}

class _Summary extends StatelessWidget {
  final Json item;
  const _Summary({required this.item});
  @override
  Widget build(BuildContext context) {
    final source = obj(item['source']);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        UiText(
          '${followUpStates[item['state']] ?? ''}${item['overdue'] == true ? ' · متأخرة' : ''}',
          style: TextStyle(color: Theme.of(context).colorScheme.primary),
        ),
        Heading(textOf(source['name'])),
        UiText(followUpConditions[item['condition']] ?? 'متابعة تشغيلية'),
        if (item['condition'] == 'GroupEvaluationDrafts')
          evaluationDraftSummary(source),
        if (item['condition'] == 'GroupLowOccupancy')
          UiText(
            'تسجيلات فعالة مسندة: ${source['enrollments']}، السعة: ${source['capacity'] ?? 'غير محددة'}',
          ),
        if (item['condition'] == 'GroupSessionCoverage')
          coverageSummary(source),
        UiText('${source['branch']} · ${source['sport']}'),
        UiText('موعد المتابعة: ${item['dueOn'] ?? 'غير محدد'}'),
        UiText(
          item['conditionActive'] == true
              ? 'الحالة الأصلية ما زالت قائمة؛ إغلاق المتابعة لا يحلها تلقائيًا.'
              : item['conditionActive'] == false
              ? 'الحالة الأصلية لم تعد تنطبق أو المصدر متوقف.'
              : 'الحالة غير متاحة؛ لا نفترض أنها حُلت.',
        ),
        if (item['note'] != null) UiText(textOf(item['note'])),
        if (item['resolutionReason'] != null)
          UiText('السبب: ${item['resolutionReason']}'),
      ],
    );
  }
}

class _Update extends StatefulWidget {
  final AuthController auth;
  final Json item;
  final Future<void> Function() saved;
  const _Update({
    super.key,
    required this.auth,
    required this.item,
    required this.saved,
  });
  @override
  State<_Update> createState() => _UpdateState();
}

class _UpdateState extends State<_Update> {
  final note = TextEditingController(), reason = TextEditingController();
  String state = 'InProgress', error = '';
  String? due, draft, requestId;
  bool busy = false;
  @override
  void initState() {
    super.initState();
    note.text = textOf(widget.item['note']);
    due = widget.item['dueOn'] as String?;
  }

  @override
  void dispose() {
    note.dispose();
    reason.dispose();
    super.dispose();
  }

  Future<void> save() async {
    if (busy) return;
    if ((state == 'Closed' || state == 'Rejected') &&
        reason.text.trim().isEmpty) {
      setState(() => error = 'اذكر سبب الإغلاق أو الرفض.');
      return;
    }
    final body = {
          'state': state,
          'version': widget.item['version'],
          'dueOn': due,
          'note': note.text,
          'resolutionReason': reason.text.trim().isEmpty
              ? null
              : reason.text.trim(),
        },
        serialized = jsonEncode(body);
    if (draft != serialized) {
      draft = serialized;
      requestId = followUpRequestId();
    }
    setState(() {
      busy = true;
      error = '';
    });
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/my-followups/${widget.item['id']}',
        body: {...body, 'requestId': requestId},
      );
      if (mounted) await widget.saved();
    } catch (_) {
      if (mounted) {
        setState(
          () => error =
              'تعذر الحفظ. ربما تغيرت المتابعة أو صلاحياتك؛ حدّث التفاصيل.',
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GCard(
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const Heading('تحديث عملي'),
        DropdownButtonFormField<String>(
          initialValue: state,
          decoration: const InputDecoration(labelText: 'حالة المتابعة'),
          items: [
            for (final s in ['InProgress', 'Snoozed', 'Closed', 'Rejected'])
              DropdownMenuItem(value: s, child: UiText(followUpStates[s]!)),
          ],
          onChanged: busy ? null : (v) => setState(() => state = v!),
        ),
        TextButton(
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
        TextField(
          controller: note,
          enabled: !busy,
          maxLength: 500,
          decoration: const InputDecoration(labelText: 'ملاحظة اختيارية'),
        ),
        if (state == 'Closed' || state == 'Rejected')
          TextField(
            controller: reason,
            enabled: !busy,
            maxLength: 500,
            decoration: const InputDecoration(
              labelText: 'سبب إغلاق أو رفض المتابعة',
            ),
          ),
        FilledButton(
          onPressed: busy ? null : save,
          child: UiText(busy ? 'جارٍ الحفظ…' : 'حفظ تحديث المتابعة'),
        ),
        if (error.isNotEmpty) UiText(error),
      ],
    ),
  );
}
