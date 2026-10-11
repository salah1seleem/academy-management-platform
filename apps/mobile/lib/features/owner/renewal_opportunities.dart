import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

class NativeRenewalOpportunities extends StatelessWidget {
  final AuthController auth;
  final String branchId;
  const NativeRenewalOpportunities({
    super.key,
    required this.auth,
    this.branchId = '',
  });
  @override
  Widget build(BuildContext context) => RemoteBody(
    key: ValueKey(branchId),
    auth: auth,
    path:
        '/api/v1/finance/renewal-opportunities?${Uri(queryParameters: {if (branchId.isNotEmpty) 'branchId': branchId}).query}',
    builder: (context, value) => RenewalOpportunityContent(data: obj(value)),
  );
}

class RenewalOpportunityContent extends StatelessWidget {
  final Json data;
  const RenewalOpportunityContent({super.key, required this.data});
  @override
  Widget build(BuildContext context) => gList([
    GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(
            Icons.trending_up_rounded,
            size: 32,
            color: Theme.of(context).colorScheme.primary,
          ),
          const SizedBox(height: 12),
          UiText(
            localized(
              context,
              'فرص التجديد الحالية',
              'Current renewal opportunities',
            ),
          ),
          UiText(
            money(data['potential'], data['currency']),
            style: Theme.of(context).textTheme.headlineMedium,
          ),
          UiText(
            '${data['count']} ${localized(context, 'تسجيلًا منتهيًا', 'expired enrollments')}',
          ),
          UiText(
            localized(
              context,
              'ليست دينًا أو تحصيلًا. السعر الحالي يخضع لفحص الأهلية عند التجديد.',
              'Not debt or cash. Current pricing is rechecked when renewing.',
            ),
          ),
          if (data['unpricedCount'] is num && data['unpricedCount'] > 0)
            UiText(
              '${data['unpricedCount']} ${localized(context, 'تحتاج اختيار باقة — غير مشمولة بالإجمالي', 'require plan selection — excluded from the total')}',
            ),
        ],
      ),
    ),
    const SizedBox(height: 12),
    if (rows(data['items']).isEmpty)
      const EmptyMessage('لا توجد فرص تجديد في هذا النطاق.'),
    for (final item in rows(data['items']))
      GCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            UiText(
              textOf(item['player']),
              style: Theme.of(context).textTheme.titleMedium,
            ),
            UiText('${textOf(item['branch'])} · ${textOf(item['sport'])}'),
            UiText(textOf(item['group'])),
            const SizedBox(height: 8),
            UiText(
              item['quote'] == null
                  ? localized(
                      context,
                      'تحتاج اختيار باقة',
                      'Plan selection required',
                    )
                  : money(item['quote'], item['currency']),
              style: Theme.of(context).textTheme.titleLarge,
            ),
            if (item['discountReason'] != null)
              UiText(textOf(item['discountReason'])),
            UiText(
              item['lastEndDate'] == null
                  ? localized(context, 'نفدت الحصص', 'Sessions used up')
                  : '${localized(context, 'انتهاء الخدمة', 'Service ended')}: ${day(item['lastEndDate'])}',
            ),
          ],
        ),
      ),
  ]);
}
