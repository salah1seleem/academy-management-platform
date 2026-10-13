import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import 'guardian_ui.dart';

class NativeFamilyRenewalQuote extends StatelessWidget {
  final AuthController auth;
  const NativeFamilyRenewalQuote({super.key, required this.auth});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'مراجعة تجديد الأسرة', 'Review family renewals'),
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/subscriptions/family/options',
      builder: (context, value) => FamilyRenewalChoices(
        enrollments: rows(obj(value)['items']),
        onQuote: (lines) async => obj(
          await auth.request(
            'POST',
            '/api/v1/guardian/subscriptions/family/quote',
            body: {'lines': lines},
          ),
        ),
      ),
    ),
  );
}

class FamilyRenewalChoices extends StatefulWidget {
  final List<Json> enrollments;
  final Future<Json> Function(List<Json>) onQuote;
  const FamilyRenewalChoices({
    super.key,
    required this.enrollments,
    required this.onQuote,
  });
  @override
  State<FamilyRenewalChoices> createState() => _FamilyRenewalChoicesState();
}

class _FamilyRenewalChoicesState extends State<FamilyRenewalChoices> {
  final Map<String, String> selection = {};
  Json? quote;
  bool busy = false;
  String? error;
  String l(String ar, String en) => localized(context, ar, en);
  void change(String id, String? value) => setState(() {
    error = null;
    quote = null;
    if (value == null) {
      selection.remove(id);
    } else {
      selection[id] = value;
    }
  });
  Future<void> review() async {
    if (busy) return;
    setState(() {
      busy = true;
      error = null;
    });
    try {
      final result = await widget.onQuote(
        selection.entries
            .map(
              (e) => <String, dynamic>{
                'sportEnrollmentId': e.key,
                'subscriptionPlanId': e.value,
              },
            )
            .toList(),
      );
      if (mounted) setState(() => quote = result);
    } catch (_) {
      if (mounted) {
        setState(
          () => error = l(
            'تعذر مراجعة الباقات. حدّث الاختيارات وحاول مجددًا؛ لم يُنشأ طلب دفع.',
            'Could not review plans. Update selections and try again; no payment request was created.',
          ),
        );
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  Widget notice() => GCard(
    child: UiText(
      l(
        'معاينة السعر فقط؛ لم يُنشأ طلب دفع أو إيصال ولم يُفعّل اشتراك. الإثبات الواحد والاعتماد الجماعي قيد الاستكمال. التجديد الفردي متاح كما هو.',
        'Price preview only: no payment, receipt or activation. Single-proof approval is being completed; individual renewal remains available.',
      ),
    ),
  );
  @override
  Widget build(BuildContext context) {
    if (quote != null) {
      return gList([
        Heading(l('إجمالي تجديد الأسرة', 'Family renewal total')),
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              UiText(
                '${l('قبل الخصم', 'Before discount')}: ${money(quote!['originalAmount'], quote!['currency'])}',
              ),
              UiText(
                '${l('الخصم المؤهل', 'Eligible discount')}: ${money(quote!['discountAmount'], quote!['currency'])}',
              ),
              const SizedBox(height: 8),
              Heading(money(quote!['totalAmount'], quote!['currency'])),
            ],
          ),
        ),
        for (final line in rows(quote!['items']))
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Heading(textOf(line['player'])),
                UiText(
                  '${line['sport']} · ${line['plan']} · ${line['branch']}',
                ),
                if (line['discountReason'] != null)
                  UiText(textOf(line['discountReason'])),
                UiText(money(line['finalAmount'], line['currency'])),
              ],
            ),
          ),
        notice(),
        OutlinedButton(
          onPressed: () => setState(() => quote = null),
          child: UiText(
            l('تعديل التسجيلات والباقات', 'Edit enrollments and plans'),
          ),
        ),
      ]);
    }
    return gList([
      Heading(l('اختر الأبناء والرياضات', 'Choose children and sports')),
      UiText(
        l(
          'باقة مناسبة لكل تسجيل، وإجمالي واحد محسوب على الخادم.',
          'One eligible plan per enrollment, with a server-calculated total.',
        ),
      ),
      if (widget.enrollments.isEmpty)
        EmptyMessage(
          l(
            'لا توجد تسجيلات متاحة لأبنائك.',
            'No eligible enrollments for your children.',
          ),
        ),
      if (error != null)
        UiText(
          error!,
          style: TextStyle(color: Theme.of(context).colorScheme.error),
        ),
      for (final e in widget.enrollments)
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              CheckboxListTile(
                contentPadding: EdgeInsets.zero,
                controlAffinity: ListTileControlAffinity.leading,
                title: UiText(textOf(e['player'])),
                subtitle: UiText('${e['sport']} · ${e['branch']}'),
                value: selection.containsKey(e['id']),
                onChanged: busy || rows(e['plans']).isEmpty
                    ? null
                    : (checked) => change(e['id'], checked == true ? '' : null),
              ),
              if (rows(e['plans']).isEmpty)
                UiText(
                  l(
                    'لا توجد باقة منشورة مناسبة.',
                    'No eligible published plan.',
                  ),
                ),
              if (selection.containsKey(e['id']))
                DropdownButtonFormField<String>(
                  key: ValueKey('${e['id']}:${selection[e['id']]}'),
                  initialValue: selection[e['id']]!.isEmpty
                      ? null
                      : selection[e['id']],
                  isExpanded: true,
                  decoration: InputDecoration(labelText: l('الباقة', 'Plan')),
                  items: rows(e['plans'])
                      .map(
                        (p) => DropdownMenuItem<String>(
                          value: p['id'],
                          child: UiText(
                            '${p['name']} · ${money(p['amount'], p['currency'])}',
                            overflow: TextOverflow.ellipsis,
                          ),
                        ),
                      )
                      .toList(),
                  onChanged: busy
                      ? null
                      : (value) => change(e['id'], value ?? ''),
                ),
            ],
          ),
        ),
      FilledButton(
        onPressed:
            busy ||
                selection.isEmpty ||
                selection.length > 20 ||
                selection.values.any((x) => x.isEmpty)
            ? null
            : review,
        child: UiText(
          busy
              ? l('جارٍ حساب الإجمالي…', 'Calculating…')
              : l(
                  'مراجعة إجمالي ${selection.length} تسجيل',
                  'Review ${selection.length} enrollments',
                ),
        ),
      ),
      notice(),
    ]);
  }
}
