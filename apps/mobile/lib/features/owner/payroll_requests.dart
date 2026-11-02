import '../../core/widgets/guided_form.dart';
import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../admin/native_operation_form.dart';
import '../guardian/guardian_ui.dart';

String payrollRequestStatus(Json row) => switch (row['status']) {
  'Paid' => 'تم تسجيل الصرف',
  'Approved' => row['kind'] == 'Advance' ? 'معتمدة · بانتظار الصرف' : 'معتمد',
  'Rejected' => 'مرفوض',
  _ => 'بانتظار المراجعة',
};

num? payrollAmount(String value) {
  var normalized = value.trim().replaceAll('٫', '.');
  for (var i = 0; i < 10; i++) {
    normalized = normalized
        .replaceAll('٠١٢٣٤٥٦٧٨٩'[i], '$i')
        .replaceAll('۰۱۲۳۴۵۶۷۸۹'[i], '$i');
  }
  if (!RegExp(r'^\d+(\.\d{1,2})?$').hasMatch(normalized)) return null;
  final amount = num.tryParse(normalized);
  return amount != null && amount > 0 && amount <= 100000000 ? amount : null;
}

class OwnerPayrollRequests extends StatefulWidget {
  const OwnerPayrollRequests({super.key, required this.auth});
  final AuthController auth;
  @override
  State<OwnerPayrollRequests> createState() => _OwnerPayrollRequestsState();
}

class _OwnerPayrollRequestsState
    extends NativeOperationState<OwnerPayrollRequests> {
  @override
  AuthController get auth => widget.auth;
  List<Json>? requests;
  List<Json> reasons = [];
  bool settings = false;
  String filter = 'Pending';
  @override
  void initState() {
    super.initState();
    load();
  }

  Future<void> read() async {
    final result = await Future.wait([
      auth.request('GET', '/api/v1/manage/payroll/requests'),
      auth.request('GET', '/api/v1/manage/payroll/reasons'),
    ]);
    if (current) {
      setState(() {
        requests = rows(result[0]);
        reasons = rows(result[1]);
      });
    }
  }

  Future<void> load() => run(read);

  Future<void> addReason() async {
    final name = TextEditingController();
    final key = GlobalKey<FormState>();
    final saved = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const UiText('سبب طلب جديد'),
        content: GuidedForm(
          key: key,
          child: TextFormField(
            controller: name,
            maxLength: 100,
            decoration: const InputDecoration(labelText: 'اسم السبب'),
            validator: (v) =>
                v == null || v.trim().isEmpty ? 'اكتب السبب.' : null,
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const UiText('إلغاء'),
          ),
          FilledButton(
            onPressed: () {
              if (key.currentState!.validate()) {
                Navigator.pop(context, name.text.trim());
              }
            },
            child: const UiText('إضافة السبب'),
          ),
        ],
      ),
    );
    // A closing dialog keeps its field alive until the route animation ends.
    await Future<void>.delayed(const Duration(milliseconds: 250));
    name.dispose();
    if (saved == null || !current) return;
    await run(() async {
      try {
        await auth.request(
          'POST',
          '/api/v1/manage/payroll/reasons',
          body: {'name': saved},
        );
      } catch (_) {
        await read();
        if (!reasons.any((r) => r['name'] == saved)) rethrow;
      }
      await read();
      if (current) message = 'تمت إضافة السبب.';
    });
  }

  Future<void> review(Json row) async {
    final advance = row['kind'] == 'Advance';
    final amount = TextEditingController(
      text: advance ? textOf(row['requestedAmount']) : '',
    );
    final note = TextEditingController();
    final key = GlobalKey<FormState>();
    Json? calendar;
    await run(() async {
      calendar = obj(await auth.request('GET', '/api/v1/academy/calendar'));
    });
    if (!mounted || !current || calendar == null) {
      amount.dispose();
      note.dispose();
      return;
    }
    var date = DateTime.tryParse(textOf(calendar!['today']));
    if (date == null) {
      amount.dispose();
      note.dispose();
      setState(() => error = 'تعذر تحديد تاريخ الأكاديمية.');
      return;
    }
    var accept = true;
    final body = await showDialog<Json>(
      context: context,
      builder: (context) => StatefulBuilder(
        builder: (context, update) => AlertDialog(
          title: UiText('مراجعة ${advance ? 'السلفة' : 'المبلغ الإضافي'}'),
          content: SingleChildScrollView(
            child: GuidedForm(
              key: key,
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  UiText(
                    textOf(row['name']),
                    style: const TextStyle(fontWeight: FontWeight.w700),
                  ),
                  UiText(textOf(row['reasonName'])),
                  const SizedBox(height: 16),
                  SegmentedButton<bool>(
                    segments: const [
                      ButtonSegment(value: true, label: UiText('اعتماد')),
                      ButtonSegment(value: false, label: UiText('رفض')),
                    ],
                    selected: {accept},
                    onSelectionChanged: (v) => update(() => accept = v.single),
                  ),
                  if (accept) ...[
                    const SizedBox(height: 16),
                    TextFormField(
                      controller: amount,
                      keyboardType: const TextInputType.numberWithOptions(
                        decimal: true,
                      ),
                      decoration: InputDecoration(
                        labelText: 'المبلغ المعتمد (${row['currency']})',
                      ),
                      validator: (v) => payrollAmount(v ?? '') == null
                          ? 'أدخل مبلغًا صحيحًا.'
                          : null,
                    ),
                    TextButton.icon(
                      onPressed: () async {
                        final picked = await showDatePicker(
                          context: context,
                          initialDate: date!,
                          firstDate: DateTime(2000),
                          lastDate: DateTime(2100),
                        );
                        if (picked != null) update(() => date = picked);
                      },
                      icon: const Icon(Icons.calendar_month_outlined),
                      label: UiText(
                        'تاريخ الاستحقاق · ${date!.day}/${date!.month}/${date!.year}',
                      ),
                    ),
                    if (advance)
                      const UiText(
                        'اعتماد السلفة لا يسجل صرفها. تأكيد الصرف خطوة مستقلة.',
                      ),
                  ],
                  const SizedBox(height: 12),
                  TextFormField(
                    controller: note,
                    maxLength: 500,
                    decoration: const InputDecoration(
                      labelText: 'ملاحظة (اختياري)',
                    ),
                  ),
                ],
              ),
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context),
              child: const UiText('إلغاء'),
            ),
            FilledButton(
              onPressed: () {
                if (!accept || key.currentState!.validate()) {
                  Navigator.pop(context, <String, dynamic>{
                    'accept': accept,
                    'amount': accept ? payrollAmount(amount.text) : null,
                    'dueDate': accept
                        ? date!.toIso8601String().split('T').first
                        : null,
                    'note': note.text.trim(),
                  });
                }
              },
              child: const UiText('حفظ القرار'),
            ),
          ],
        ),
      ),
    );
    await Future<void>.delayed(const Duration(milliseconds: 250));
    amount.dispose();
    note.dispose();
    if (body == null || !current) return;
    await run(() async {
      try {
        await auth.request(
          'POST',
          '/api/v1/manage/payroll/requests/${row['id']}/review',
          body: body,
        );
      } catch (_) {
        await read();
        final saved = requests!.where((r) => r['id'] == row['id']).firstOrNull;
        if (saved == null ||
            saved['status'] !=
                (body['accept'] == true ? 'Approved' : 'Rejected') ||
            (body['accept'] == true &&
                (!storedAmountMatches(saved['amount'], body['amount']) ||
                    saved['dueDate'] != body['dueDate'])) ||
            textOf(saved['reviewNote']) != textOf(body['note'])) {
          rethrow;
        }
      }
      await read();
      if (current) message = 'تم حفظ القرار وإشعار الموظف.';
    });
  }

  Future<void> recordAdvance(Json row) async {
    final reference = TextEditingController();
    final key = GlobalKey<FormState>();
    final result = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const UiText('تأكيد صرف السلفة'),
        content: GuidedForm(
          key: key,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              UiText('${row['name']} · ${row['amount']} ${row['currency']}'),
              const SizedBox(height: 12),
              const UiText(
                'أكد فقط بعد تسليم المبلغ فعليًا. سيُسجل في حساب سلف الموظف مرة واحدة.',
              ),
              const SizedBox(height: 12),
              TextFormField(
                controller: reference,
                maxLength: 120,
                decoration: const InputDecoration(labelText: 'مرجع الصرف'),
                validator: (v) => (v?.trim().length ?? 0) < 3
                    ? 'أدخل مرجعًا واضحًا للصرف.'
                    : null,
              ),
            ],
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const UiText('إلغاء'),
          ),
          FilledButton(
            onPressed: () {
              if (key.currentState!.validate()) {
                Navigator.pop(context, reference.text.trim());
              }
            },
            child: const UiText('تسجيل الصرف'),
          ),
        ],
      ),
    );
    await Future<void>.delayed(const Duration(milliseconds: 250));
    reference.dispose();
    if (result == null || !current) return;
    await run(() async {
      try {
        await auth.request(
          'POST',
          '/api/v1/manage/payroll/requests/${row['id']}/record-advance',
          body: {'expectedAmount': row['amount'], 'reference': result},
          idempotencyKey: 'advance-request-${row['id']}',
        );
      } catch (_) {
        await read();
        if (!requests!.any(
          (r) =>
              r['id'] == row['id'] &&
              r['status'] == 'Paid' &&
              storedAmountMatches(r['amount'], row['amount']),
        )) {
          rethrow;
        }
      }
      await read();
      if (current) message = 'تم تسجيل الصرف وإشعار الموظف.';
    });
  }

  @override
  Widget build(BuildContext context) => page('السلف والمبالغ الإضافية', [
    SegmentedButton<bool>(
      segments: const [
        ButtonSegment(
          value: false,
          icon: Icon(Icons.inbox_outlined),
          label: UiText('الطلبات'),
        ),
        ButtonSegment(
          value: true,
          icon: Icon(Icons.tune),
          label: UiText('الأسباب'),
        ),
      ],
      selected: {settings},
      onSelectionChanged: (v) => setState(() => settings = v.single),
    ),
    const SizedBox(height: 16),
    if (requests != null && settings) ...[
      FilledButton.icon(
        onPressed: addReason,
        icon: const Icon(Icons.add),
        label: const UiText('إضافة سبب'),
      ),
      const SizedBox(height: 12),
      for (final row in reasons)
        GCard(
          child: ListTile(
            leading: const Icon(Icons.notes_rounded),
            title: UiText(textOf(row['name'])),
          ),
        ),
      if (reasons.isEmpty)
        const GCard(
          child: UiText('أضف الأسباب المتاحة للموظفين عند تقديم الطلب.'),
        ),
    ],
    if (requests != null && !settings) ...[
      Wrap(
        spacing: 8,
        runSpacing: 8,
        children: [
          for (final entry in const {
            'Pending': 'جديدة',
            'Approved': 'معتمدة',
            '': 'الكل',
          }.entries)
            ChoiceChip(
              label: UiText(entry.value),
              selected: filter == entry.key,
              onSelected: (_) => setState(() => filter = entry.key),
            ),
        ],
      ),
      const SizedBox(height: 12),
      if (!requests!.any((r) => filter.isEmpty || r['status'] == filter))
        const GCard(child: UiText('لا توجد طلبات في هذه القائمة.')),
      for (final row in requests!.where(
        (r) => filter.isEmpty || r['status'] == filter,
      ))
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  Icon(
                    row['kind'] == 'Advance'
                        ? Icons.account_balance_wallet_outlined
                        : Icons.add_card_outlined,
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: UiText(
                      textOf(row['name']),
                      style: const TextStyle(
                        fontSize: 16,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 8),
              UiText(
                '${row['kind'] == 'Advance' ? 'سلفة' : 'مبلغ إضافي'} · ${row['reasonName']}',
              ),
              const SizedBox(height: 8),
              UiText(payrollRequestStatus(row)),
              if (row['amount'] != null || row['requestedAmount'] != null)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 8),
                  child: MoneyText(
                    row['amount'] ?? row['requestedAmount'],
                    row['currency'],
                    size: 24,
                  ),
                ),
              if (textOf(row['reviewNote']).isNotEmpty)
                UiText(textOf(row['reviewNote'])),
              if (row['status'] == 'Pending')
                FilledButton(
                  onPressed: () => review(row),
                  child: const UiText('مراجعة الطلب'),
                ),
              if (row['kind'] == 'Advance' && row['status'] == 'Approved')
                OutlinedButton(
                  onPressed: () => recordAdvance(row),
                  child: const UiText('تسجيل صرف السلفة'),
                ),
            ],
          ),
        ),
    ],
  ], onRetry: load);
}
