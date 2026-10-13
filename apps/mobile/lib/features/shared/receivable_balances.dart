import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

class ReceivableBalancesPage extends StatelessWidget {
  final AuthController auth;
  const ReceivableBalancesPage({super.key, required this.auth});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'أرصدة الأسرة', 'Family balances'),
    child: ReceivableBalances(auth: auth),
  );
}

class ReceivableBalances extends StatelessWidget {
  final AuthController auth;
  final bool staff;
  const ReceivableBalances({super.key, required this.auth, this.staff = false});
  @override
  Widget build(BuildContext context) => RemoteBody(
    auth: auth,
    path: staff
        ? '/api/v1/finance/receivables'
        : '/api/v1/guardian/receivables',
    builder: (context, value) => ReceivableBalanceContent(data: obj(value)),
  );
}

class ReceivableBalanceContent extends StatelessWidget {
  final Json data;
  const ReceivableBalanceContent({super.key, required this.data});
  @override
  Widget build(BuildContext context) {
    final items = rows(data['items']);
    final currency = data['currency'];
    return gList([
      GCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(
              Icons.account_balance_wallet_outlined,
              color: Theme.of(context).colorScheme.primary,
              size: 32,
            ),
            const SizedBox(height: 12),
            UiText(localized(context, 'الرصيد القائم', 'Outstanding balance')),
            UiText(
              money(data['outstanding'], currency),
              style: Theme.of(context).textTheme.headlineMedium,
            ),
            const Divider(height: 28),
            balanceLine(
              context,
              'متأخر مستحق',
              'Overdue',
              data['overdue'],
              currency,
            ),
            balanceLine(
              context,
              'غير مستحق بعد',
              'Not yet due',
              data['notYetDue'],
              currency,
            ),
            balanceLine(
              context,
              'رصيد دائن',
              'Overpayment credit',
              data['overpaymentCredit'],
              currency,
            ),
          ],
        ),
      ),
      UiText(
        localized(
          context,
          'انتهاء الاشتراك ليس دينًا. هذه أرصدة مستندات مثبتة وليست طلبات دفع معلقة.',
          'An expired subscription is not a debt. These are posted balances, not pending payments.',
        ),
      ),
      const SizedBox(height: 16),
      if (items.isEmpty) const EmptyMessage('لا توجد مطالبات مثبتة.'),
      for (final item in items)
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              UiText(
                textOf(item['player']),
                style: Theme.of(context).textTheme.titleMedium,
              ),
              UiText(textOf(item['description'])),
              const SizedBox(height: 8),
              balanceLine(
                context,
                'المطالبة',
                'Charge',
                item['charge'],
                item['currency'],
              ),
              balanceLine(
                context,
                'رصيد قائم',
                'Balance',
                item['outstanding'],
                item['currency'],
              ),
              if (item['creditReceived'] != null &&
                  !RegExp(r'^0(?:\.0+)?$')
                      .hasMatch(textOf(item['creditReceived'])))
                balanceLine(
                  context,
                  'تسوية برصيد دائن',
                  'Credit applied',
                  item['creditReceived'],
                  item['currency'],
                ),
              if (item['creditUsed'] != null &&
                  !RegExp(r'^0(?:\.0+)?$').hasMatch(textOf(item['creditUsed'])))
                balanceLine(
                  context,
                  'رصيد دائن مستخدم',
                  'Credit used',
                  item['creditUsed'],
                  item['currency'],
                ),
              UiText(
                '${localized(context, 'الاستحقاق', 'Due')}: ${day(item['dueDate'])}',
              ),
              if (item['overdue'] == true)
                UiText(
                  localized(context, 'متأخر', 'Overdue'),
                  style: TextStyle(color: Theme.of(context).colorScheme.error),
                ),
            ],
          ),
        ),
    ]);
  }

  Widget balanceLine(
    BuildContext context,
    String ar,
    String en,
    Object? amount,
    Object? currency,
  ) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 4),
    child: Wrap(
      spacing: 12,
      runSpacing: 4,
      children: [
        UiText(localized(context, ar, en)),
        UiText(money(amount, currency)),
      ],
    ),
  );
}
