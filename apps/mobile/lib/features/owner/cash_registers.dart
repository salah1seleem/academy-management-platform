import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

class NativeCashRegisters extends StatefulWidget {
  final AuthController auth;
  const NativeCashRegisters({super.key, required this.auth});
  @override
  State<NativeCashRegisters> createState() => _NativeCashRegistersState();
}

class _NativeCashRegistersState extends State<NativeCashRegisters> {
  int revision = 0;

  @override
  Widget build(BuildContext context) => RemoteBody(
    key: ValueKey(revision),
    auth: widget.auth,
    path: '/api/v1/finance/cash-registers',
    builder: (context, value) => CashRegisterContent(
      data: obj(value),
      onRefresh: () => setState(() => revision++),
    ),
  );
}

// Read-only mobile oversight. Cashier delegation and irreversible reconciliation
// remain on the explicit, independently authorized Web workflow.
class CashRegisterContent extends StatelessWidget {
  final Json data;
  final VoidCallback? onRefresh;
  const CashRegisterContent({super.key, required this.data, this.onRefresh});
  @override
  Widget build(BuildContext context) {
    final rawItems = data['items'];
    final rawHistory = data['history'];
    if (rawItems is! List || rawHistory is! List) {
      return gList([
        const EmptyMessage('تعذر قراءة قائمة الصناديق والمطابقات.'),
        if (onRefresh != null)
          TextButton.icon(
            onPressed: onRefresh,
            icon: const Icon(Icons.refresh),
            label: const UiText('إعادة المحاولة'),
          ),
      ]);
    }
    final items = rawItems.whereType<Json>();
    final history = rawHistory.whereType<Json>();
    final registers = {for (final item in items) item['id']: item};
    final status = switch (data['cashEnabled']) {
      true => localized(context, 'النقدي مفعل', 'Cash enabled'),
      false => localized(context, 'النقدي متوقف', 'Cash disabled'),
      _ => localized(
        context,
        'تعذر التحقق من حالة النقدي',
        'Cash status unavailable',
      ),
    };
    return gList([
      GCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            UiText(status, style: Theme.of(context).textTheme.titleMedium),
            if (data['cashEnabled'] == false)
              UiText(
                localized(
                  context,
                  'تظل الورديات وأرصدتها متاحة للمراجعة.',
                  'Shifts and balances remain available for review.',
                ),
              ),
            if (onRefresh != null)
              TextButton.icon(
                onPressed: onRefresh,
                icon: const Icon(Icons.refresh),
                label: UiText(localized(context, 'تحديث الصناديق', 'Refresh')),
              ),
          ],
        ),
      ),
      if (items.isEmpty)
        const EmptyMessage('لا توجد صناديق متاحة ضمن صلاحياتك.'),
      for (final item in items) _RegisterCard(item: item),
      if (history.isNotEmpty)
        Padding(
          padding: const EdgeInsets.only(top: 8),
          child: UiText(
            localized(
              context,
              'آخر الورديات المقفلة',
              'Recently closed shifts',
            ),
            style: Theme.of(context).textTheme.titleMedium,
          ),
        ),
      ...history.map((shift) {
        final register = registers[shift['registerId']];
        final currency = register?['currency'];
        final expected = _cashCents(shift['expectedAmount']);
        final counted = _cashCents(shift['countedAmount']);
        final difference = expected == null || counted == null
            ? null
            : counted - expected;
        final unavailable = localized(context, 'غير متاح', 'Unavailable');
        final status = switch (difference) {
          null => localized(
            context,
            'فرق المطابقة غير متاح',
            'Variance unavailable',
          ),
          0 => localized(context, 'متطابق', 'Matched'),
          < 0 =>
            '${localized(context, 'عجز', 'Shortage')} ${money(difference.abs() / 100, currency)}',
          _ =>
            '${localized(context, 'زيادة', 'Overage')} ${money(difference / 100, currency)}',
        };
        return GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              UiText(
                textOf(register?['name']),
                style: Theme.of(context).textTheme.titleSmall,
              ),
              UiText(day(shift['closedAtUtc'])),
              UiText(
                '${localized(context, 'المتوقع', 'Expected')}: ${expected == null ? unavailable : money(shift['expectedAmount'], currency)}',
              ),
              UiText(
                '${localized(context, 'الفعلي', 'Counted')}: ${counted == null ? unavailable : money(shift['countedAmount'], currency)}',
              ),
              const SizedBox(height: 4),
              UiText(status, style: Theme.of(context).textTheme.titleSmall),
            ],
          ),
        );
      }),
    ]);
  }
}

class _RegisterCard extends StatelessWidget {
  const _RegisterCard({required this.item});
  final Json item;

  @override
  Widget build(BuildContext context) {
    final session = item['session'];
    final balance = session is Map ? session['balance'] : null;
    final opening = session is Map ? session['opening'] : null;
    final unavailable = localized(context, 'غير متاح', 'Unavailable');
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          UiText(
            textOf(item['name']),
            style: Theme.of(context).textTheme.titleMedium,
          ),
          UiText(textOf(item['branch'])),
          const SizedBox(height: 10),
          if (session is Map) ...[
            UiText(localized(context, 'وردية مفتوحة', 'Open shift')),
            UiText(localized(context, 'الرصيد الدفتري', 'Book balance')),
            UiText(
              _cashCents(balance) == null
                  ? unavailable
                  : money(balance, item['currency']),
              style: Theme.of(context).textTheme.headlineSmall,
            ),
            UiText(
              '${localized(context, 'العهدة الافتتاحية', 'Opening float')}: ${_cashCents(opening) == null ? unavailable : money(opening, item['currency'])}',
            ),
          ] else
            UiText(localized(context, 'لا توجد وردية مفتوحة', 'No open shift')),
        ],
      ),
    );
  }
}

int? _cashCents(Object? value) {
  final parts = RegExp(r'^(-?)(\d{1,16})(?:\.(\d{1,2}))?$')
      .firstMatch('$value');
  if (parts == null) return null;
  final cents =
      int.parse(parts[2]!) * 100 + int.parse((parts[3] ?? '').padRight(2, '0'));
  return parts[1] == '-' ? -cents : cents;
}
