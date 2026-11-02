import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../owner/owner_operations.dart' show OwnerScopedRead;

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
  final Map<String, String> filters;
  const ReceivableBalances({
    super.key,
    required this.auth,
    this.staff = false,
    this.filters = const {},
  });
  @override
  Widget build(BuildContext context) => RemoteBody(
    auth: auth,
    path: staff
        ? '/api/v1/finance/receivables?${Uri(queryParameters: {
            for (final key in ['branchId', 'playerId', 'state', 'asOf'])
              if (filters[key]?.isNotEmpty == true) key: filters[key]!,
          }).query}'
        : '/api/v1/guardian/receivables',
    builder: (context, value) =>
        ReceivableBalanceContent(data: obj(value), auth: auth, staff: staff),
  );
}

class ReceivableBalanceContent extends StatelessWidget {
  final Json data;
  final AuthController? auth;
  final bool staff;
  const ReceivableBalanceContent({
    super.key,
    required this.data,
    this.auth,
    this.staff = false,
  });
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
            if (data['asOf'] != null)
              UiText(
                'رصيد حتى ${day(data['asOf'])} · بالتوقيت العالمي',
                style: const TextStyle(fontSize: 12),
              ),
            UiText(
              localized(context, 'إجمالي المستحقات', 'Outstanding balance'),
            ),
            UiText(
              money(data['outstanding'], currency),
              style: Theme.of(context).textTheme.headlineMedium,
            ),
            const Divider(height: 28),
            balanceLine(
              context,
              'المتأخر',
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
              'رصيد لصالح العميل',
              'Overpayment credit',
              data['overpaymentCredit'],
              currency,
            ),
          ],
        ),
      ),
      ExpansionTile(
        title: const UiText('كيف تُحسب المستحقات؟'),
        children: [
          UiText(
            localized(
              context,
              'انتهاء الاشتراك ليس دينًا. هذه أرصدة مستندات مثبتة وليست طلبات دفع معلقة.',
              'An expired subscription is not a debt. These are posted balances, not pending payments.',
            ),
          ),
        ],
      ),
      const SizedBox(height: 16),
      if (items.isEmpty) const EmptyMessage('لا توجد مطالبات مثبتة.'),
      for (final item in items)
        GCard(
          onTap: staff && auth != null
              ? () => openPage(
                  context,
                  ReceivableDocumentPage(auth: auth!, id: textOf(item['id'])),
                )
              : null,
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
              if (staff && auth != null)
                const UiText(
                  'عرض المستند وحركاته',
                  style: TextStyle(fontSize: 12),
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

class ReceivableDocumentPage extends StatelessWidget {
  const ReceivableDocumentPage({
    super.key,
    required this.auth,
    required this.id,
  });
  final AuthController auth;
  final String id;
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'مستند المستحقات',
    child: OwnerScopedRead(
      auth: auth,
      path: '/api/v1/finance/receivables/$id',
      builder: (context, value) {
        final data = obj(value),
            document = obj(data['document']),
            balance = obj(data['balance']);
        if (document['id'] != id || balance['id'] != id) {
          return const EmptyMessage(
            'لم تتأكد هوية مستند المستحقات. أعد فتحه من القائمة.',
          );
        }
        final currency = balance['currency'];
        return gList([
          Heading(textOf(balance['player'])),
          UiText(textOf(balance['description'])),
          if (data['asOf'] != null)
            UiText(
              'رصيد حتى ${day(data['asOf'])} · بالتوقيت العالمي',
              style: const TextStyle(fontSize: 12),
            ),
          Heading(
            textOf(balance['outstanding']).startsWith('-')
                ? 'رصيد لصالح العميل'
                : 'الرصيد القائم',
          ),
          MoneyText(balance['outstanding'], currency),
          Facts([
            ('أصل المطالبة', money(balance['charge'], currency)),
            ('تحصيل مخصص', money(balance['allocated'], currency)),
            ('إشعار دائن', money(balance['credits'], currency)),
            ('رصيد دائن مستخدم', money(balance['creditUsed'], currency)),
            ('تسوية برصيد دائن', money(balance['creditReceived'], currency)),
            ('الفرع', textOf(balance['branch'])),
            ('تاريخ الاستحقاق', day(balance['dueDate'])),
            (
              'مصدر المستند',
              balance['source'] == 'OpeningBalance'
                  ? 'رصيد افتتاحي موثق'
                  : 'مطالبة مسجلة',
            ),
          ]),
          const Heading('مرجع المستند'),
          SelectableText(id),
          const Heading('حركات المستند'),
          if (rows(data['movements']).isEmpty)
            const UiText('لا توجد حركات مسجلة.'),
          for (final movement in rows(data['movements']))
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Heading(
                    movement['kind'] == 'PaymentAllocation'
                        ? 'تخصيص تحصيل محفوظ'
                        : 'إشعار دائن',
                  ),
                  MoneyText(movement['amount'], movement['currency'], size: 22),
                  UiText(textOf(movement['reason'])),
                  UiText(
                    'بتاريخ ${day(movement['createdAtUtc'])} · بالتوقيت العالمي',
                  ),
                  if (movement['collectionId'] != null) ...[
                    const UiText('مرجع التحصيل المخصص'),
                    SelectableText(textOf(movement['collectionId'])),
                  ],
                ],
              ),
            ),
          for (final credit in rows(data['creditApplications']))
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Heading('تسوية برصيد دائن موثقة'),
                  MoneyText(credit['amount'], currency, size: 22),
                  UiText(
                    'بتاريخ ${day(credit['createdAtUtc'])} · بالتوقيت العالمي',
                  ),
                  UiText('المستند المصدر: ${credit['sourceDocumentId']}'),
                  UiText('المستند المستفيد: ${credit['targetDocumentId']}'),
                ],
              ),
            ),
        ]);
      },
    ),
  );
}
