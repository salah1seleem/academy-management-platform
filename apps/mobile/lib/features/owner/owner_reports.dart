import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/widgets/app_widgets.dart';
import '../guardian/guardian_ui.dart';
import '../shared/receivable_balances.dart';
import 'renewal_opportunities.dart';
import 'cash_registers.dart';
import 'provider_quarantine.dart';
import 'settlement_comparisons.dart';
import 'owner_cockpit.dart';
import 'external_agreements.dart';

class OwnerReports extends StatefulWidget {
  final AuthController auth;
  final Map<String, String> initialFilters;
  final String initialReport;
  const OwnerReports({
    super.key,
    required this.auth,
    this.initialFilters = const {},
    this.initialReport = 'financial',
  });
  @override
  State<OwnerReports> createState() => _OwnerReportsState();
}

class _OwnerReportsState extends State<OwnerReports> {
  late String report = widget.initialReport;
  @override
  Widget build(BuildContext context) => AppScaffold(
    title: 'التقارير التنفيذية',
    child: Column(
      children: [
        Padding(
          padding: const EdgeInsets.all(16),
          child: Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              for (final item in const {
                'financial': 'المال',
                'operating': 'الفائض والعجز',
                'external': 'الاتفاقيات والتأجير',
                'attendance': 'الحضور',
                'receivables': 'الأرصدة',
                'cash': 'الصناديق',
                'provider-quarantine': 'اختلافات الدفع',
                'settlement-comparisons': 'مطابقة الكشوف',
                'renewals': 'فرص التجديد',
                'subscriptions': 'الاشتراكات',
              }.entries)
                ChoiceChip(
                  label: UiText(item.value),
                  selected: report == item.key,
                  onSelected: (_) => setState(() => report = item.key),
                ),
            ],
          ),
        ),
        Expanded(
          child: report == 'external'
              ? NativeExternalAgreements(
                  auth: widget.auth,
                  branch: widget.initialFilters['branchId'] ?? '',
                )
              : report == 'operating'
              ? NativeOperatingStatement(
                  auth: widget.auth,
                  filters: widget.initialFilters,
                )
              : report == 'settlement-comparisons'
              ? NativeSettlementComparisons(auth: widget.auth)
              : report == 'provider-quarantine'
              ? NativeProviderQuarantine(auth: widget.auth)
              : report == 'cash'
              ? NativeCashRegisters(auth: widget.auth)
              : report == 'renewals'
              ? NativeRenewalOpportunities(
                  auth: widget.auth,
                  branchId: widget.initialFilters['branchId'] ?? '',
                )
              : report == 'receivables'
              ? ReceivableBalances(
                  auth: widget.auth,
                  staff: true,
                  filters: {
                    if (widget.initialFilters['branchId'] != null)
                      'branchId': widget.initialFilters['branchId']!,
                    if (widget.initialFilters['state'] != null)
                      'state': widget.initialFilters['state']!,
                  },
                )
              : report == 'subscriptions'
              ? _subscription(widget.auth)
              : _report(widget.auth, report),
        ),
      ],
    ),
  );

  Widget _subscription(AuthController auth) => RemoteBody(
    auth: auth,
    path: '/api/v1/reports/owner-summary',
    builder: (context, value) {
      final d = obj(obj(value)['subscriptions']);
      return ListView(
        padding: const EdgeInsets.all(18),
        children: [
          const UiText('ملخص قراءة فقط من فترات الاشتراك المخزنة.'),
          AppCard(
            child: Column(
              children: [
                _row('فعالة', d['active']),
                _row('تنتهي قريبًا', d['expiring']),
                _row('منتهية', d['expired']),
              ],
            ),
          ),
        ],
      );
    },
  );
  Widget _report(AuthController auth, String kind) => RemoteBody(
    key: ValueKey(kind),
    auth: auth,
    path: kind == 'financial'
        ? '/api/v1/reports/financial?${Uri(queryParameters: {'pageSize': '20', ...widget.initialFilters}).query}'
        : '/api/v1/reports/attendance?${Uri(queryParameters: {'subject': 'players', 'pageSize': '20', ...widget.initialFilters}).query}',
    builder: (context, value) {
      final d = obj(value), items = rows(d['items']);
      return ListView(
        padding: const EdgeInsets.all(18),
        children: [
          UiText(
            kind == 'financial' ? 'ملخص التحصيلات' : 'ملخص الحضور المخزن',
            style: Theme.of(context).textTheme.titleLarge,
          ),
          UiText('إجمالي السجلات: ${d['totalCount']}'),
          if (kind == 'financial')
            UiText('الإجمالي: ${money(d['totalAmount'], d['currency'])}'),
          if (kind == 'financial')
            UiText(
              'الاستردادات: ${money(d['totalRefunds'] ?? 0, d['currency'])} · الصافي: ${money(d['netAmount'] ?? d['totalAmount'], d['currency'])}',
            ),
          for (final item in items)
            Card(
              child: ListTile(
                onTap:
                    kind == 'financial' && item['collectionType'] == 'External'
                    ? () => openPage(
                        context,
                        NativeExternalAgreementDetail(
                          auth: auth,
                          id: textOf(item['receiptId']),
                        ),
                      )
                    : null,
                title: UiText(
                  textOf(kind == 'financial' ? item['player'] : item['player']),
                ),
                subtitle: UiText(
                  kind == 'financial'
                      ? '${item['collectionType'] == 'Void'
                            ? 'عكس إيصال نقدي · الفترة ملغاة · '
                            : item['collectionType'] == 'Refund'
                            ? 'استرداد اشتراك · بتاريخ الصرف · '
                            : ''}${item['receiptNumber']} · ${item['branch']}'
                      : '${day(item['date'])} · ${item['group']}',
                ),
                trailing: UiText(
                  kind == 'financial'
                      ? money(
                          item['netAmount'] ?? item['amount'],
                          item['currency'],
                        )
                      : label(item['status']),
                ),
              ),
            ),
        ],
      );
    },
  );
  Widget _row(String name, Object? value) =>
      ListTile(title: UiText(name), trailing: UiText(textOf(value)));
}
