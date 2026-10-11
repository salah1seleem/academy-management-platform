import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

class StaffAmounts extends StatefulWidget {
  final AuthController auth;
  const StaffAmounts({super.key, required this.auth});
  @override
  State<StaffAmounts> createState() => _StaffAmountsState();
}

class _StaffAmountsState extends State<StaffAmounts> {
  final form = GlobalKey<FormState>();
  final amount = TextEditingController();
  String? reason;
  String operationKey = commandKey(), message = '';
  int revision = 0;
  bool busy = false;
  @override
  void dispose() {
    amount.dispose();
    super.dispose();
  }

  Future<void> save() async {
    if (busy || !form.currentState!.validate()) return;
    setState(() {
      busy = true;
      message = '';
    });
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/staff/payroll/requests',
        body: {'reasonId': reason, 'amount': double.parse(amount.text)},
        idempotencyKey: operationKey,
      );
      if (mounted) {
        setState(() {
          operationKey = commandKey();
          amount.clear();
          reason = null;
          revision++;
          message = localized(
            context,
            'تم إرسال الطلب للمراجعة.',
            'Request submitted for review.',
          );
        });
      }
    } catch (e) {
      if (mounted) {
        setState(
          () => message = e is ApiFailure
              ? e.message
              : localized(
                  context,
                  'تعذر إرسال الطلب.',
                  'Could not submit request.',
                ),
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'المبالغ الإضافية', 'Additional amounts'),
    child: RemoteBody(
      key: ValueKey(revision),
      auth: widget.auth,
      path: '/api/v1/staff/payroll/',
      builder: (context, value) {
        final data = obj(value);
        if (data['configured'] != true) {
          return gList([
            const UiText('لم يتم ربط سجل الموظف بحسابك بعد. تواصل مع الإدارة.'),
          ]);
        }
        final reasons = rows(data['reasons']);
        return gList([
          if (message.isNotEmpty) UiText(message),
          GCard(
            child: Form(
              key: form,
              child: Column(
                children: [
                  UiText(
                    localized(
                      context,
                      'طلب مبلغ إضافي',
                      'Request an additional amount',
                    ),
                  ),
                  TextFormField(
                    controller: amount,
                    enabled: !busy,
                    decoration: InputDecoration(
                      labelText: localized(context, 'المبلغ', 'Amount'),
                    ),
                    keyboardType: const TextInputType.numberWithOptions(
                      decimal: true,
                    ),
                    onChanged: (_) {
                      operationKey = commandKey();
                    },
                    validator: (v) =>
                        v != null &&
                            RegExp(r'^\d+(\.\d{1,2})?$').hasMatch(v.trim()) &&
                            (double.tryParse(v.trim()) ?? 0) > 0 &&
                            double.parse(v.trim()) <= 100000000
                        ? null
                        : localized(
                            context,
                            'أدخل مبلغًا صحيحًا بمنزلتين عشريتين كحد أقصى.',
                            'Enter a valid amount with up to two decimal places.',
                          ),
                  ),
                  const SizedBox(height: 12),
                  DropdownButtonFormField<String>(
                    initialValue: reason,
                    decoration: InputDecoration(
                      labelText: localized(context, 'السبب', 'Reason'),
                    ),
                    items: [
                      for (final r in reasons)
                        DropdownMenuItem(
                          value: textOf(r['id']),
                          child: UiText(textOf(r['name'])),
                        ),
                    ],
                    onChanged: busy
                        ? null
                        : (v) => setState(() {
                            reason = v;
                            operationKey = commandKey();
                          }),
                    validator: (v) => v == null
                        ? localized(
                            context,
                            'اختر سببًا معتمدًا.',
                            'Select an approved reason.',
                          )
                        : null,
                  ),
                  const SizedBox(height: 16),
                  FilledButton.icon(
                    onPressed: busy || reasons.isEmpty ? null : save,
                    icon: const Icon(Icons.send_outlined),
                    label: UiText(
                      localized(
                        context,
                        busy ? 'جارٍ الإرسال…' : 'إرسال للمراجعة',
                        busy ? 'Submitting…' : 'Submit for review',
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
          for (final r in rows(data['requests']))
            GCard(
              child: ListTile(
                title: UiText(
                  '${r['reasonName']} · ${r['amount']} ${r['currency']}',
                ),
                subtitle: UiText(
                  '${localized(context, switch (r['status']) {
                    'Approved' => 'مقبول',
                    'Rejected' => 'مرفوض',
                    _ => 'بانتظار المراجعة',
                  }, switch (r['status']) {
                    'Approved' => 'Approved',
                    'Rejected' => 'Rejected',
                    _ => 'Awaiting review',
                  })}${r['reviewNote'] == null ? '' : '\n${r['reviewNote']}'}',
                ),
                trailing: Icon(
                  r['status'] == 'Approved'
                      ? Icons.check_circle_outline
                      : r['status'] == 'Rejected'
                      ? Icons.cancel_outlined
                      : Icons.schedule,
                ),
              ),
            ),
        ]);
      },
    ),
  );
}
