import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

class NativeSettlementComparisons extends StatefulWidget {
  final AuthController auth;
  const NativeSettlementComparisons({super.key, required this.auth});
  @override
  State<NativeSettlementComparisons> createState() =>
      _NativeSettlementComparisonsState();
}

class _NativeSettlementComparisonsState
    extends State<NativeSettlementComparisons> {
  int page = 1;
  @override
  Widget build(BuildContext context) => RemoteBody(
    key: ValueKey(page),
    auth: widget.auth,
    path: '/api/v1/finance/settlement-comparisons?page=$page',
    builder: (context, value) {
      final data = obj(value);
      int count(Object? x) => int.tryParse(x?.toString() ?? '') ?? 0;
      return SettlementComparisonContent(
        data: data,
        previous: page > 1 ? () => setState(() => page--) : null,
        next: page * count(data['pageSize']) < count(data['total'])
            ? () => setState(() => page++)
            : null,
      );
    },
  );
}

class SettlementComparisonContent extends StatelessWidget {
  final Json data;
  final VoidCallback? previous, next;
  const SettlementComparisonContent({
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
          const Icon(Icons.fact_check_outlined),
          UiText(
            localized(context, 'مطابقة كشوف التحصيل', 'Statement comparisons'),
            style: Theme.of(context).textTheme.titleLarge,
          ),
          UiText(
            localized(
              context,
              'مقارنات يدوية من لوحة المالك، لا اتصال ببنك ولا تأكيد دفع. الرسوم لا تغيّر إيصال العميل ولا تُرحّل مصروفًا تلقائيًا.',
              'Manual comparisons from the owner dashboard, not a bank connection or payment confirmation. Fees do not alter customer receipts or automatically post expenses.',
            ),
          ),
        ],
      ),
    ),
    if (rows(data['items']).isEmpty)
      EmptyMessage(
        localized(context, 'لا توجد مقارنات محفوظة.', 'No saved comparisons.'),
      ),
    for (final row in rows(data['items']))
      GCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            UiText(
              '${textOf(row['statementReference'])} · ${textOf(row['lineReference'])}',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            UiText(textOf(row['accountTag'])),
            UiText(_result(context, textOf(row['result']))),
            UiText(
              '${localized(context, 'الإجمالي', 'Gross')}: ${money(row['gross'], row['currency'])}',
            ),
            UiText(
              '${localized(context, 'الرسوم', 'Fees')}: ${money(row['fee'], row['currency'])}',
            ),
            UiText(
              '${localized(context, 'الصافي', 'Net')}: ${money(row['net'], row['currency'])}',
            ),
            UiText(day(row['settlementDate'])),
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
        UiText(textOf(data['page'])),
        TextButton(
          onPressed: next,
          child: UiText(localized(context, 'التالي', 'Next')),
        ),
      ],
    ),
  ]);
  String _result(BuildContext context, String code) {
    final pair =
        <String, List<String>>{
          'Matched': ['مطابق يدويًا', 'Manually matched'],
          'AmountMismatch': ['المبلغ مختلف', 'Amount mismatch'],
          'CurrencyMismatch': ['العملة مختلفة', 'Currency mismatch'],
          'NetMismatch': [
            'الصافي لا يساوي الإجمالي ناقص الرسوم',
            'Net differs from gross minus fees',
          ],
          'Unmatched': ['لا يوجد تحصيل مطابق للمرجع', 'No matching collection'],
          'Ambiguous': ['المرجع غير حاسم', 'Ambiguous reference'],
        }[code] ??
        ['تحتاج مراجعة', 'Needs review'];
    return localized(context, pair[0], pair[1]);
  }
}
