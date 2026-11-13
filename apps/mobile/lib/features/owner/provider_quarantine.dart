import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../../core/widgets/page_status.dart';
import '../admin/native_renewal.dart';
import '../guardian/guardian_payments.dart' show ReceiptPage;
import '../guardian/guardian_ui.dart';
import '../shared/experience_ui.dart' show ScopeDateTiles;

const _root = '/api/v1/finance/provider-quarantine';
const _pageSize = 10;

int _count(Object? value) => int.tryParse(value?.toString() ?? '') ?? 0;
String _field(Object? value) => value is String ? value : '';

class NativeProviderQuarantine extends StatefulWidget {
  const NativeProviderQuarantine({super.key, required this.auth});
  final AuthController auth;

  @override
  State<NativeProviderQuarantine> createState() =>
      _NativeProviderQuarantineState();
}

class _NativeProviderQuarantineState extends State<NativeProviderQuarantine> {
  var page = 1;
  var from = '', to = '';
  var defaultMonth = true;

  void select(String fromDate, String toDate, {int index = 1}) {
    setState(() {
      from = fromDate;
      to = toDate;
      page = index;
      defaultMonth = false;
    });
  }

  @override
  Widget build(BuildContext context) {
    final query = Uri(
      queryParameters: {
        'page': '$page',
        'pageSize': '$_pageSize',
        if (defaultMonth) 'monthToDate': 'true',
        if (from.isNotEmpty) 'from': from,
        if (to.isNotEmpty) 'to': to,
      },
    ).query;
    return GuardianPage(
      title: 'اختلافات الدفع',
      onBack: () => Navigator.of(context).pop(),
      child: RemoteBody(
        key: ValueKey(query),
        auth: widget.auth,
        path: '$_root?$query',
        autoPaginate: false,
        builder: (context, value) {
          final data = obj(value);
          final current = _count(data['page']);
          final size = _count(data['pageSize']);
          final start = _field(data['from']);
          final end = _field(data['to']);
          return ProviderQuarantineContent(
            data: data,
            filters: ScopeDateTiles(
              from: start,
              to: end,
              clearLabel: 'مسح الفترة',
              onChanged: select,
              onClear: start.isEmpty && end.isEmpty
                  ? null
                  : () => select('', ''),
            ),
            previous: current > 1
                ? () => select(start, end, index: current - 1)
                : null,
            next: current * size < _count(data['total'])
                ? () => select(start, end, index: current + 1)
                : null,
            onOpen: (id) => openPage(
              context,
              NativeProviderIssue(auth: widget.auth, id: id),
            ),
          );
        },
      ),
    );
  }
}

class ProviderQuarantineContent extends StatelessWidget {
  const ProviderQuarantineContent({
    super.key,
    required this.data,
    this.filters,
    this.previous,
    this.next,
    this.onOpen,
  });
  final Json data;
  final Widget? filters;
  final VoidCallback? previous, next;
  final ValueChanged<String>? onOpen;

  @override
  Widget build(BuildContext context) {
    final items = rows(data['items']);
    final total = _count(data['total']);
    final first = (_count(data['page']) - 1) * _count(data['pageSize']) + 1;
    final last = first + items.length - 1;
    return Column(
      children: [
        Expanded(
          child: gList([
            if (filters != null) ...[filters!, const SizedBox(height: 12)],
            const Padding(
              padding: EdgeInsets.only(bottom: 12),
              child: UiText('الإشارات في هذا السجل لم تؤكد تحصيلًا.'),
            ),
            if (items.isEmpty)
              const EmptyMessage('لا توجد اختلافات في هذه الفترة.'),
            for (final item in items)
              _IssueCard(
                key: ValueKey('provider-issue-${item['id']}'),
                item: item,
                onTap: _field(item['id']).isEmpty || onOpen == null
                    ? null
                    : () => onOpen!(_field(item['id'])),
              ),
          ]),
        ),
        if (items.isNotEmpty)
          PageActionBar(
            child: Row(
              children: [
                IconButton(
                  tooltip: 'الصفحة السابقة',
                  onPressed: previous,
                  icon: const Icon(Icons.chevron_right),
                ),
                Expanded(
                  child: UiText(
                    '\u2066$first–$last\u2069 من $total',
                    semanticsLabel: 'من $first إلى $last من $total سجل',
                    textAlign: TextAlign.center,
                  ),
                ),
                IconButton(
                  tooltip: 'الصفحة التالية',
                  onPressed: next,
                  icon: const Icon(Icons.chevron_left),
                ),
              ],
            ),
          ),
      ],
    );
  }
}

class NativeProviderIssue extends StatelessWidget {
  const NativeProviderIssue({super.key, required this.auth, required this.id});
  final AuthController auth;
  final String id;

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'تفاصيل اختلاف الدفع',
    onBack: () => Navigator.of(context).pop(),
    child: RemoteBody(
      auth: auth,
      path: '$_root/${Uri.encodeComponent(id)}',
      notFound: const EmptyMessage('اختلاف الدفع غير متاح.'),
      builder: (context, value) {
        final item = obj(value);
        if (item['id'] != id) {
          return const EmptyMessage('اختلاف الدفع غير متاح.');
        }
        final receiptId = _field(item['receiptId']);
        final paymentId = _field(item['paymentRequestId']);
        return gList([
          _IssueCard(item: item),
          const Padding(
            padding: EdgeInsets.only(bottom: 12),
            child: UiText(
              'هذه الإشارة لم تؤكد الدفع. راجع سجل مزود الدفع قبل أي إجراء.',
            ),
          ),
          if (receiptId.isNotEmpty)
            GLink('عرض إيصال التحصيل', Icons.receipt_long_outlined, () {
              openPage(
                context,
                ReceiptPage(
                  auth: auth,
                  id: receiptId,
                  staff: true,
                  onBack: () => Navigator.of(context).pop(),
                ),
              );
            }),
          if (paymentId.isNotEmpty)
            GLink('فتح طلب الدفع', Icons.description_outlined, () {
              openPage(
                context,
                NativeRenewalPayment(auth: auth, id: paymentId, readOnly: true),
              );
            }),
          _SignalReferences(item: item),
        ]);
      },
    ),
  );
}

class _IssueCard extends StatelessWidget {
  const _IssueCard({super.key, required this.item, this.onTap});
  final Json item;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final secondary = theme.textTheme.bodySmall?.copyWith(
      color: theme.colorScheme.onSurfaceVariant,
    );
    return GCard(
      padding: const EdgeInsets.all(14),
      onTap: onTap,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: UiText(
                  _reason(item['reasonCode']),
                  style: theme.textTheme.titleMedium,
                ),
              ),
              if (onTap != null) const Icon(Icons.chevron_left),
            ],
          ),
          UiText(
            '${textOf(item['player'])} · ${textOf(item['branch'])}',
            style: secondary,
          ),
          const SizedBox(height: 10),
          UiText(
            _paymentStatus(item['paymentStatus']),
            style: theme.textTheme.labelLarge,
          ),
          const SizedBox(height: 8),
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: _Amount(
                  label: 'المبلغ المتوقع',
                  value: item['expectedAmount'],
                  currency: item['expectedCurrency'],
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: _Amount(
                  label: 'المبلغ الوارد',
                  value: item['receivedAmount'],
                  currency: item['receivedCurrency'],
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          UiText(_observedAt(context, item), style: secondary),
        ],
      ),
    );
  }
}

class _Amount extends StatelessWidget {
  const _Amount({
    required this.label,
    required this.value,
    required this.currency,
  });
  final String label;
  final Object? value, currency;

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      UiText(label, style: Theme.of(context).textTheme.bodySmall),
      MoneyText(value, currency, size: 18),
    ],
  );
}

class _SignalReferences extends StatelessWidget {
  const _SignalReferences({required this.item});
  final Json item;

  @override
  Widget build(BuildContext context) {
    final expected = item['expectedReference'];
    final received = item['receivedReference'];
    final same = expected == received;
    return Card(
      clipBehavior: Clip.antiAlias,
      child: ExpansionTile(
        title: const UiText('تفاصيل الإشارة والمراجع'),
        expandedCrossAxisAlignment: CrossAxisAlignment.stretch,
        childrenPadding: const EdgeInsets.fromLTRB(16, 0, 16, 12),
        children: [
          UiText(
            item['provider'] == 'InternalTest'
                ? 'بوابة الدفع التجريبية'
                : 'مزود الدفع المسجّل',
          ),
          UiText(switch (item['receivedStatus']) {
            'succeeded' => 'أبلغت الإشارة بنجاح الدفع',
            'failed' => 'أبلغت الإشارة بفشل الدفع',
            'cancelled' => 'أبلغت الإشارة بإلغاء الدفع',
            _ => 'حالة الإشارة غير معروفة',
          }),
          _reference(
            context,
            same ? 'مرجع الطلب والإشارة' : 'مرجع الطلب',
            expected,
          ),
          if (!same) _reference(context, 'المرجع الوارد', received),
          _reference(context, 'رقم الحدث لدى المزود', item['providerEventId']),
        ],
      ),
    );
  }

  Widget _reference(BuildContext context, String label, Object? value) =>
      Padding(
        padding: const EdgeInsets.only(top: 10),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            UiText(label, style: Theme.of(context).textTheme.labelLarge),
            SelectableText(
              _field(value).isEmpty ? '—' : _field(value),
              textDirection: TextDirection.ltr,
            ),
          ],
        ),
      );
}

String _observedAt(BuildContext context, Json item) {
  final local = _field(item['observedAtCairo']);
  final time = local.isNotEmpty
      ? DateTime.tryParse('${local}Z')
      : DateTime.tryParse(_field(item['createdAtUtc']))?.toUtc();
  if (time == null) return 'وقت الرصد غير متاح';
  final format = MaterialLocalizations.of(context);
  final clock = format.formatTimeOfDay(TimeOfDay.fromDateTime(time));
  final zone = local.isNotEmpty ? 'بتوقيت القاهرة' : 'بالتوقيت العالمي';
  return '${format.formatShortDate(time)} · $clock · $zone';
}

String _reason(Object? code) => switch (code) {
  'amount-mismatch' => 'المبلغ مختلف',
  'currency-mismatch' => 'العملة مختلفة',
  'reference-mismatch' => 'مرجع الدفع مختلف',
  'provider-mismatch' => 'مصدر الدفع مختلف',
  'request-terms-mismatch' => 'تفاصيل الطلب غير متطابقة',
  'event-reuse' => 'رقم الحدث مستخدم بتفاصيل أخرى',
  'unsupported-status' => 'حالة الإشارة غير معروفة',
  'registration-format-conflict' => 'نوع التسجيل غير متوافق',
  _ => 'اختلاف يتطلب المتابعة',
};

String _paymentStatus(Object? status) => switch (status) {
  'Pending' => 'بانتظار تأكيد الدفع',
  'Confirmed' => 'تم تأكيد الطلب',
  'Failed' => 'لم يتم الدفع',
  'Cancelled' => 'أُلغي الطلب',
  'Expired' => 'انتهت صلاحية الطلب',
  'Created' => 'طلب جديد',
  _ => 'حالة الطلب تحتاج مراجعة',
};
