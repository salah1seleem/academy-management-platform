import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'salary_history.dart';
import 'payroll_statements.dart';

class StaffAmounts extends StatefulWidget {
  final AuthController auth;
  const StaffAmounts({super.key, required this.auth});
  @override
  State<StaffAmounts> createState() => _StaffAmountsState();
}

class _StaffAmountsState extends State<StaffAmounts> {
  final form = GlobalKey<FormState>();
  final otherReason = TextEditingController();
  String? reason;
  bool isOther = false;
  String operationKey = commandKey(), message = '';
  int revision = 0;
  bool busy = false;
  @override
  void dispose() {
    otherReason.dispose();
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
        body: {
          'reasonId': reason,
          if (isOther) 'otherReason': otherReason.text.trim(),
        },
        idempotencyKey: operationKey,
      );
      if (mounted) {
        setState(() {
          operationKey = commandKey();
          otherReason.clear();
          reason = null;
          isOther = false;
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
          GLink(
            localized(context, 'مستحقاتي والصرف', 'My approved dues'),
            Icons.account_balance_wallet_outlined,
            () => openPage(context, StaffPayrollStatements(auth: widget.auth)),
          ),
          GLink(
            localized(context, 'راتبي وتغييراته', 'My salary history'),
            Icons.history_outlined,
            () => openPage(context, StaffSalaryHistory(auth: widget.auth)),
          ),
          if (message.isNotEmpty) UiText(message),
          GCard(
            child: Form(
              key: form,
              child: Column(
                children: [
                  UiText(
                    localized(
                      context,
                      'طلب مبلغ إضافي — الإدارة تحدد القيمة',
                      'Additional amount request — management sets the amount',
                    ),
                  ),
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
                            isOther = reasons.any(
                              (r) =>
                                  textOf(r['id']) == v &&
                                  textOf(r['name']) == 'سبب آخر',
                            );
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
                  if (isOther) ...[
                    const SizedBox(height: 12),
                    TextFormField(
                      controller: otherReason,
                      enabled: !busy,
                      maxLength: 80,
                      decoration: InputDecoration(
                        labelText: localized(
                          context,
                          'تفاصيل السبب',
                          'Reason details',
                        ),
                      ),
                      onChanged: (_) => operationKey = commandKey(),
                      validator: (v) => v != null && v.trim().isNotEmpty
                          ? null
                          : localized(
                              context,
                              'اكتب تفاصيل السبب.',
                              'Enter reason details.',
                            ),
                    ),
                  ],
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
                  '${r['reasonName']}${r['status'] == 'Approved' ? ' · ${r['amount']} ${r['currency']}' : ''}',
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
