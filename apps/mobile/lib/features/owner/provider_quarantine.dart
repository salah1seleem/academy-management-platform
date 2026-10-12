import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

int _count(Object? value) => int.tryParse(value?.toString() ?? '') ?? 0;

class NativeProviderQuarantine extends StatefulWidget {
  final AuthController auth;
  const NativeProviderQuarantine({super.key, required this.auth});
  @override
  State<NativeProviderQuarantine> createState() =>
      _NativeProviderQuarantineState();
}

class _NativeProviderQuarantineState extends State<NativeProviderQuarantine> {
  int page = 1;
  @override
  Widget build(BuildContext context) => RemoteBody(
    key: ValueKey(page),
    auth: widget.auth,
    path: '/api/v1/finance/provider-quarantine?page=$page',
    builder: (context, value) {
      final data = obj(value);
      return ProviderQuarantineContent(
        data: data,
        previous: page > 1 ? () => setState(() => page--) : null,
        next: page * _count(data['pageSize']) < _count(data['total'])
            ? () => setState(() => page++)
            : null,
      );
    },
  );
}

class ProviderQuarantineContent extends StatelessWidget {
  final Json data;
  final VoidCallback? previous, next;
  const ProviderQuarantineContent({
    super.key,
    required this.data,
    this.previous,
    this.next,
  });
  @override
  Widget build(BuildContext context) => gList([
    GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Icon(Icons.shield_outlined),
          UiText(
            localized(
              context,
              'متابعة اختلافات الدفع',
              'Payment discrepancies',
            ),
            style: Theme.of(context).textTheme.titleLarge,
          ),
          UiText(
            localized(
              context,
              'هذه الإشارات لا تثبت قبضًا أو تفعّل اشتراكًا. المتابعة من لوحة المالك؛ لا اعتماد يدوي هنا.',
              'These events do not confirm collection or activate subscriptions. Follow up in the owner dashboard; no manual approval here.',
            ),
          ),
        ],
      ),
    ),
    if (rows(data['items']).isEmpty)
      EmptyMessage(
        localized(
          context,
          'لا توجد اختلافات مسجلة.',
          'No recorded discrepancies.',
        ),
      ),
    for (final item in rows(data['items']))
      GCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            UiText(
              textOf(item['player']),
              style: Theme.of(context).textTheme.titleMedium,
            ),
            UiText(textOf(item['branch'])),
            const SizedBox(height: 8),
            UiText(_reason(context, textOf(item['reasonCode']))),
            UiText(
              '${localized(context, 'المتوقع', 'Expected')}: ${money(item['expectedAmount'], item['expectedCurrency'])}',
            ),
            UiText(
              '${localized(context, 'الوارد', 'Received')}: ${money(item['receivedAmount'], item['receivedCurrency'])}',
            ),
            UiText(
              '${localized(context, 'وقت الرصد', 'Recorded')}: ${day(item['createdAtUtc'])}',
            ),
            UiText(
              localized(
                context,
                item['paymentStatus'] == 'Confirmed'
                    ? 'الطلب مؤكد بإشارة صحيحة أخرى؛ الاختلاف محفوظ.'
                    : 'راجع الحالة الحالية وتفاصيل الطلب من لوحة المالك.',
                item['paymentStatus'] == 'Confirmed'
                    ? 'Confirmed by another valid event; discrepancy retained.'
                    : 'Check current request details in the owner dashboard.',
              ),
            ),
          ],
        ),
      ),
    Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        TextButton(
          onPressed: previous,
          child: UiText(localized(context, 'السابق', 'Previous')),
        ),
        UiText('${_count(data['page'])}'),
        TextButton(
          onPressed: next,
          child: UiText(localized(context, 'التالي', 'Next')),
        ),
      ],
    ),
  ]);
  String _reason(BuildContext context, String code) {
    final labels = <String, List<String>>{
      'amount-mismatch': ['المبلغ مختلف', 'Amount mismatch'],
      'currency-mismatch': ['العملة مختلفة', 'Currency mismatch'],
      'reference-mismatch': ['مرجع الدفع مختلف', 'Reference mismatch'],
      'provider-mismatch': ['مصدر الدفع مختلف', 'Provider mismatch'],
      'request-terms-mismatch': [
        'تفاصيل الطلب غير متطابقة',
        'Request terms mismatch',
      ],
      'event-reuse': [
        'رقم الحدث مستخدم بتفاصيل أخرى',
        'Event ID reused with different details',
      ],
      'unsupported-status': ['حالة الدفع غير معروفة', 'Unknown payment status'],
      'registration-format-conflict': [
        'نوع التسجيل غير متوافق',
        'Registration format conflict',
      ],
    };
    final label =
        labels[code] ??
        ['اختلاف يتطلب المتابعة', 'Discrepancy requiring follow-up'];
    return localized(context, label[0], label[1]);
  }
}
