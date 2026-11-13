import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../../core/widgets/page_status.dart';
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

const _balanceStates = {
  '': 'كل المطالبات',
  'open': 'قائمة',
  'overdue': 'متأخرة',
  'future': 'غير مستحقة بعد',
  'settled': 'مسددة',
  'credit': 'رصيد دائن',
};

String _balanceDay(BuildContext context, Object? value) {
  final date = DateTime.tryParse(textOf(value));
  return date == null
      ? 'غير محدد'
      : MaterialLocalizations.of(context).formatShortDate(date);
}

String? _balanceTime(BuildContext context, Object? value) {
  final instant = DateTime.tryParse(textOf(value))?.toLocal();
  if (instant == null) return null;
  final locale = MaterialLocalizations.of(context);
  final date = locale.formatShortDate(instant);
  final time = locale.formatTimeOfDay(TimeOfDay.fromDateTime(instant));
  return '$date، $time';
}

num? _amount(Object? value) => num.tryParse(textOf(value));

class ReceivableBalances extends StatefulWidget {
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
  State<ReceivableBalances> createState() => _ReceivableBalancesState();
}

class _ReceivableBalancesState extends State<ReceivableBalances> {
  late String state = _initialState();
  int revision = 0;

  String _initialState() {
    final filter = widget.filters['state'];
    return _balanceStates.containsKey(filter) ? filter! : '';
  }

  @override
  void didUpdateWidget(ReceivableBalances oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.filters['state'] != widget.filters['state'] ||
        oldWidget.auth != widget.auth) {
      state = _initialState();
    }
  }

  @override
  Widget build(BuildContext context) => RemoteBody(
    key: ValueKey(revision),
    auth: widget.auth,
    path: widget.staff
        ? '/api/v1/finance/receivables?${Uri(queryParameters: {
            for (final key in ['branchId', 'playerId', 'asOf'])
              if (widget.filters[key]?.isNotEmpty == true) key: widget.filters[key]!,
          }).query}'
        : '/api/v1/guardian/receivables',
    builder: (context, value) {
      if (!_validReceivableLedger(value)) {
        return PageStatus(
          child: StatusPanel(
            message: widget.staff
                ? 'تعذر تحميل المطالبات في هذا النطاق.'
                : 'تعذر تحميل أرصدة الأسرة.',
            icon: Icons.warning_amber_rounded,
            actionLabel: 'إعادة المحاولة',
            onAction: () => setState(() => revision++),
          ),
        );
      }
      return ReceivableBalanceContent(
        data: obj(value),
        auth: widget.auth,
        staff: widget.staff,
        balanceScope: widget.staff
            ? (widget.filters['branchId']?.isNotEmpty == true
                  ? 'ملخص رصيد الفرع'
                  : 'ملخص رصيد كل الفروع')
            : null,
        state: widget.staff ? state : '',
        onStateChanged: widget.staff
            ? (next) => setState(() => state = next)
            : null,
      );
    },
  );
}

bool _validReceivableLedger(Object? value) {
  bool currency(Object? field) =>
      field is String && RegExp(r'^[A-Z]{3}$').hasMatch(field);
  if (value is! Json ||
      value['items'] is! List ||
      !currency(value['currency']) ||
      DateTime.tryParse('${value['asOf']}') == null) {
    return false;
  }
  bool amount(Object? field) => _amount(field)?.isFinite == true;
  return [
        'outstanding',
        'overdue',
        'notYetDue',
        'overpaymentCredit',
      ].every((key) => amount(value[key])) &&
      (value['items'] as List).every(
        (item) =>
            item is Json &&
            item['id'] is String &&
            (item['id'] as String).trim().isNotEmpty &&
            item['player'] is String &&
            item['overdue'] is bool &&
            currency(item['currency']) &&
            amount(item['outstanding']) &&
            amount(item['charge']),
      );
}

class ReceivableBalanceContent extends StatelessWidget {
  final Json data;
  final AuthController? auth;
  final bool staff;
  final String? balanceScope;
  final String state;
  final ValueChanged<String>? onStateChanged;
  const ReceivableBalanceContent({
    super.key,
    required this.data,
    this.auth,
    this.staff = false,
    this.balanceScope,
    this.state = '',
    this.onStateChanged,
  });

  bool _matches(Json item) {
    final amount = _amount(item['outstanding']);
    return switch (state) {
      'open' => amount != null && amount > 0,
      'overdue' => item['overdue'] == true,
      'future' => amount != null && amount > 0 && item['overdue'] != true,
      'settled' => amount == 0,
      'credit' => amount != null && amount < 0,
      _ => true,
    };
  }

  @override
  Widget build(BuildContext context) {
    final items = rows(data['items']).where(_matches);
    final currency = data['currency'];
    final timestamp = _balanceTime(context, data['asOf']);
    final colors = Theme.of(context).colorScheme;
    return gList([
      if (onStateChanged != null)
        Padding(
          padding: const EdgeInsets.only(bottom: 8),
          child: InputDecorator(
            decoration: const InputDecoration(labelText: 'حالة الرصيد'),
            child: DropdownButtonHideUnderline(
              child: DropdownButton<String>(
                isExpanded: true,
                value: state,
                items: [
                  for (final option in _balanceStates.entries)
                    DropdownMenuItem(
                      value: option.key,
                      child: UiText(option.value),
                    ),
                ],
                onChanged: (next) => onStateChanged!(next!),
              ),
            ),
          ),
        ),
      GCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            if (balanceScope case final String scope)
              Padding(
                padding: const EdgeInsets.only(bottom: 6),
                child: UiText(
                  scope,
                  style: Theme.of(context).textTheme.titleSmall,
                ),
              ),
            UiText(
              localized(context, 'إجمالي المستحقات', 'Outstanding balance'),
            ),
            UiText(
              money(data['outstanding'], currency),
              style: Theme.of(context).textTheme.headlineMedium,
            ),
            const Divider(height: 20),
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
            if ((_amount(data['overpaymentCredit']) ?? 0) > 0)
              balanceLine(
                context,
                'رصيد دائن للأسرة',
                'Overpayment credit',
                data['overpaymentCredit'],
                currency,
              ),
            if (timestamp != null)
              UiText(
                'رصيد حتى $timestamp',
                style: TextStyle(fontSize: 12, color: colors.onSurfaceVariant),
              ),
          ],
        ),
      ),
      ExpansionTile(
        dense: true,
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
      if (items.isEmpty)
        EmptyMessage(
          state.isEmpty
              ? staff
                    ? 'لا توجد مطالبات مثبتة في هذا النطاق.'
                    : 'لا توجد مطالبات مثبتة للأسرة.'
              : 'لا توجد مطالبات بهذه الحالة في النطاق المحدد.',
        ),
      for (final item in items) _card(context, item),
    ]);
  }

  Widget _card(BuildContext context, Json item) {
    final balance = _amount(item['outstanding']);
    final credit = balance != null && balance < 0;
    final overdue = item['overdue'] == true;
    final colors = Theme.of(context).colorScheme;
    final canOpen = staff && auth != null;
    return GCard(
      onTap: canOpen
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
          if (staff && item['branch'] != null)
            UiText(
              textOf(item['branch']),
              style: TextStyle(color: colors.onSurfaceVariant),
            ),
          const SizedBox(height: 6),
          if (balance == 0)
            UiText(staff ? 'مطالبة مسددة' : 'أُقفل الرصيد')
          else
            balanceLine(
              context,
              credit ? 'رصيد دائن' : 'رصيد قائم',
              credit ? 'Credit balance' : 'Balance',
              balance?.abs() ?? item['outstanding'],
              item['currency'],
            ),
          balanceLine(
            context,
            'أصل المطالبة',
            'Original charge',
            item['charge'],
            item['currency'],
          ),
          if ((_amount(item['creditReceived']) ?? 0) != 0)
            balanceLine(
              context,
              'تسوية برصيد دائن',
              'Credit applied',
              item['creditReceived'],
              item['currency'],
            ),
          if ((_amount(item['creditUsed']) ?? 0) != 0)
            balanceLine(
              context,
              'رصيد دائن مستخدم',
              'Credit used',
              item['creditUsed'],
              item['currency'],
            ),
          if (balance != null && balance > 0)
            UiText(
              'الاستحقاق ${_balanceDay(context, item['dueDate'])}${overdue ? ' · متأخرة' : ''}',
              style: TextStyle(
                color: overdue ? colors.error : colors.onSurfaceVariant,
              ),
            ),
          if (canOpen)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: UiText(
                'عرض المستند وحركاته',
                style: TextStyle(
                  color: colors.primary,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
        ],
      ),
    );
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
        final outstanding = _amount(balance['outstanding']);
        final timestamp = _balanceTime(context, data['asOf']);
        final movements = rows(data['movements']);
        final credits = rows(data['creditApplications'] ?? []);
        return gList([
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Heading(textOf(balance['description'])),
                UiText('${balance['player']} · ${balance['branch']}'),
                const SizedBox(height: 10),
                UiText(
                  outstanding != null && outstanding < 0
                      ? 'رصيد لصالح العميل'
                      : outstanding == 0
                      ? 'مطالبة مسددة'
                      : 'الرصيد القائم',
                ),
                MoneyText(
                  outstanding?.abs() ?? balance['outstanding'],
                  currency,
                ),
                if (timestamp != null)
                  UiText(
                    'رصيد حتى $timestamp',
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                const Divider(height: 22),
                UiText('الاستحقاق ${_balanceDay(context, balance['dueDate'])}'),
                UiText(
                  balance['source'] == 'OpeningBalance'
                      ? 'رصيد افتتاحي موثق'
                      : 'مطالبة مسجلة',
                ),
              ],
            ),
          ),
          Facts([
            ('أصل المطالبة', money(balance['charge'], currency)),
            if ((_amount(balance['allocated']) ?? 0) != 0)
              ('تحصيل مخصص', money(balance['allocated'], currency)),
            if ((_amount(balance['credits']) ?? 0) != 0)
              ('إشعار دائن', money(balance['credits'], currency)),
            if ((_amount(balance['creditUsed']) ?? 0) != 0)
              ('رصيد دائن مستخدم', money(balance['creditUsed'], currency)),
            if ((_amount(balance['creditReceived']) ?? 0) != 0)
              ('تسوية برصيد دائن', money(balance['creditReceived'], currency)),
          ]),
          ExpansionTile(
            dense: true,
            title: const UiText('مرجع المستند'),
            children: [SelectableText(id)],
          ),
          const Heading('حركات المستند'),
          if (movements.isEmpty && credits.isEmpty)
            const UiText('لا توجد حركات مسجلة.'),
          for (final movement in movements)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Heading(
                    movement['kind'] == 'PaymentAllocation'
                        ? 'تخصيص تحصيل محفوظ'
                        : 'إشعار دائن',
                  ),
                  MoneyText(
                    movement['amount'],
                    movement['currency'] ?? currency,
                    size: 22,
                  ),
                  UiText(textOf(movement['reason'])),
                  if (_balanceTime(context, movement['createdAtUtc'])
                      case final String date)
                    UiText(date),
                  if (movement['collectionId'] != null)
                    ExpansionTile(
                      dense: true,
                      title: const UiText('مرجع التحصيل المخصص'),
                      children: [
                        SelectableText(textOf(movement['collectionId'])),
                      ],
                    ),
                ],
              ),
            ),
          for (final credit in credits)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Heading('تسوية برصيد دائن موثقة'),
                  MoneyText(credit['amount'], currency, size: 22),
                  if (_balanceTime(context, credit['createdAtUtc'])
                      case final String date)
                    UiText(date),
                  if (credit['reason'] != null)
                    UiText(textOf(credit['reason'])),
                  TextButton(
                    onPressed: () => openPage(
                      context,
                      ReceivableDocumentPage(
                        auth: auth,
                        id: textOf(
                          credit['sourceDocumentId'] == id
                              ? credit['targetDocumentId']
                              : credit['sourceDocumentId'],
                        ),
                      ),
                    ),
                    child: const UiText('عرض المطالبة المرتبطة'),
                  ),
                ],
              ),
            ),
        ]);
      },
    ),
  );
}
