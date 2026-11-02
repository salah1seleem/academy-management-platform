import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

class NativeCashRegisters extends StatelessWidget {
  final AuthController auth;
  const NativeCashRegisters({super.key, required this.auth});
  @override
  Widget build(BuildContext context) => RemoteBody(
    auth: auth,
    path: '/api/v1/finance/cash-registers',
    builder: (context, value) => CashRegisterContent(data: obj(value)),
  );
}

// Read-only mobile oversight. Cashier delegation and irreversible reconciliation
// remain on the explicit, independently authorized Web workflow.
class CashRegisterContent extends StatelessWidget {
  final Json data;
  const CashRegisterContent({super.key, required this.data});
  @override
  Widget build(BuildContext context) {
    final items = rows(data['items']);
    return gList([
      GCard(
        child: Row(
          children: [
            Icon(
              Icons.account_balance_outlined,
              color: Theme.of(context).colorScheme.primary,
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(
                    localized(context, 'الصناديق والعهد', 'Cash registers'),
                  ),
                  UiText(
                    localized(
                      context,
                      data['cashEnabled'] == true
                          ? 'النقدي مفعل'
                          : 'النقدي متوقف',
                      data['cashEnabled'] == true
                          ? 'Cash enabled'
                          : 'Cash disabled',
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
      UiText(
        localized(
          context,
          'العهدة والتحويل ليسا إيرادًا. التفويض والإقفال من لوحة الإدارة.',
          'Float and transfers are not revenue. Delegation and closing use the dashboard.',
        ),
      ),
      const SizedBox(height: 12),
      if (items.isEmpty)
        const EmptyMessage('لا توجد صناديق متاحة ضمن صلاحياتك.'),
      for (final item in items)
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              UiText(
                textOf(item['name']),
                style: Theme.of(context).textTheme.titleMedium,
              ),
              UiText(textOf(item['branch'])),
              const SizedBox(height: 10),
              if (item['session'] != null) ...[
                UiText(localized(context, 'وردية مفتوحة', 'Open shift')),
                UiText(
                  money(obj(item['session'])['balance'], item['currency']),
                  style: Theme.of(context).textTheme.headlineSmall,
                ),
                UiText(
                  '${localized(context, 'العهدة الافتتاحية', 'Opening float')}: ${money(obj(item['session'])['opening'], item['currency'])}',
                ),
              ] else
                UiText(
                  localized(context, 'لا توجد وردية مفتوحة', 'No open shift'),
                ),
            ],
          ),
        ),
      if (rows(data['history']).isNotEmpty)
        UiText(
          localized(context, 'آخر الورديات المقفلة', 'Recently closed shifts'),
          style: Theme.of(context).textTheme.titleMedium,
        ),
      for (final shift in rows(data['history']))
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              UiText(
                textOf(
                  items
                      .where((r) => r['id'] == shift['registerId'])
                      .firstOrNull?['name'],
                ),
              ),
              UiText(day(shift['closedAtUtc'])),
              UiText(
                '${localized(context, 'المتوقع', 'Expected')}: ${money(shift['expectedAmount'], items.where((r) => r['id'] == shift['registerId']).firstOrNull?['currency'])}',
              ),
              UiText(
                '${localized(context, 'الفعلي', 'Counted')}: ${money(shift['countedAmount'], items.where((r) => r['id'] == shift['registerId']).firstOrNull?['currency'])}',
              ),
            ],
          ),
        ),
    ]);
  }
}
