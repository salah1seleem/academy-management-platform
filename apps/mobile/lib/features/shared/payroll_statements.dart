import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

class StaffPayrollStatements extends StatefulWidget {
  final AuthController auth;
  const StaffPayrollStatements({super.key, required this.auth});

  @override
  State<StaffPayrollStatements> createState() => _StaffPayrollStatementsState();
}

class _StaffPayrollStatementsState extends State<StaffPayrollStatements>
    with WidgetsBindingObserver {
  int revision = 0;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) refresh();
  }

  void refresh() => setState(() => revision++);

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'مستحقاتي والصرف', 'My approved dues'),
    child: RemoteBody(
      key: ValueKey(revision),
      auth: widget.auth,
      path: '/api/v1/staff/payroll/account',
      builder: (context, value) {
        final data = obj(value);
        if (data['configured'] == false) {
          return EmptyMessage(
            localized(
              context,
              'لم يربط سجل الموظف بحسابك بعد.',
              'No employee record is linked yet.',
            ),
          );
        }

        final items = rows(data['items']);
        final finals = rows(data['finalSettlements'] ?? const []);
        final opening = data['opening'] is Map ? obj(data['opening']) : null;
        final hasApprovedDues =
            items.isNotEmpty || opening != null || finals.isNotEmpty;
        final calendar = MaterialLocalizations.of(context);
        final date = DateTime.tryParse('${data['asOf']}');
        final english = Localizations.localeOf(context).languageCode == 'en';
        String copy(String ar, String en) => localized(context, ar, en);
        String amount(Object? value) => english
            ? '${amountText(value)} ${textOf(data['currency'])}'
            : money(value, data['currency']);
        Widget line(String ar, String en, Object? value) =>
            UiText('${copy(ar, en)}: ${amount(value)}');
        Widget title(String label) => Semantics(
          header: true,
          child: UiText(label, style: Theme.of(context).textTheme.titleMedium),
        );
        Widget card(List<Widget> children) => GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            spacing: 4,
            children: children,
          ),
        );

        return gList([
          Align(
            alignment: AlignmentDirectional.centerEnd,
            child: TextButton.icon(
              onPressed: refresh,
              icon: const Icon(Icons.refresh),
              label: UiText(copy('تحديث المستحقات', 'Refresh dues')),
            ),
          ),
          if (hasApprovedDues)
            card([
              title(copy('حساب مستحقاتك', 'Your approved account')),
              if (date != null)
                UiText(
                  '${copy('حتى', 'As of')} ${calendar.formatShortDate(date)}',
                ),
              line('إجمالي المعتمد', 'Total approved', data['totalDue']),
              line('إجمالي المسدد', 'Total settled', data['totalPaid']),
              line('المتبقي المعروف', 'Known remaining', data['remaining']),
              if ((data['priorRemaining'] as num? ?? 0) != 0)
                line('من أشهر سابقة', 'Prior months', data['priorRemaining']),
              if (data['currentMonthApproved'] == true &&
                  (data['priorRemaining'] as num? ?? 0) != 0)
                line(
                  'من الشهر الحالي',
                  'Current month',
                  data['currentRemaining'],
                ),
            ]),
          if (!hasApprovedDues)
            EmptyMessage(
              copy(
                'لا كشف معتمد بعد؛ هذا لا يعني أن مستحقاتك صفر.',
                'No approved statement yet; your dues are still unconfirmed.',
              ),
            )
          else if (data['currentMonthApproved'] == false)
            UiText(
              copy(
                'كشف الشهر الحالي لم يُعتمد بعد.',
                'This month’s statement is pending approval.',
              ),
            ),
          for (final s in items)
            card([
              title(
                _monthLabel(
                  calendar,
                  s['month'],
                  copy('كشف الشهر', 'Monthly statement'),
                ),
              ),
              line('المستحق', 'Due', s['netDue']),
              line('المسدد', 'Settled', s['paid']),
              line('المتبقي', 'Remaining', s['remaining']),
              line('الأساسي', 'Base pay', s['baseDue']),
              if ((s['deductions'] as num? ?? 0) != 0)
                line('الخصومات', 'Deductions', s['deductions']),
              if ((s['additions'] as num? ?? 0) != 0)
                line('الإضافات', 'Additions', s['additions']),
              for (final p in rows(s['payments']))
                ListTile(
                  dense: true,
                  contentPadding: EdgeInsets.zero,
                  leading: const Icon(Icons.receipt_long_outlined),
                  title: p['kind'] == 'AdvanceApplication'
                      ? line(
                          'تسوية من سلفة سابقة',
                          'Settled from prior advance',
                          p['amount'],
                        )
                      : line('صرف مسجل', 'Recorded payout', p['amount']),
                ),
            ]),
          if ((data['recordedAdvanceTotal'] as num? ?? 0) > 0)
            card([
              title(copy('السلف المسجلة', 'Recorded advances')),
              line('سبق صرفها', 'Already paid', data['recordedAdvanceTotal']),
              line('غير المخصص', 'Unallocated', data['unallocatedAdvances']),
              line(
                'المخصص من سلف سابقة',
                'Applied from prior advances',
                data['advanceApplied'],
              ),
              UiText(
                copy(
                  'تخصيص السلفة يسدد جزءًا من المستحق. السلفة غير المخصصة تظل منفصلة عن الراتب.',
                  'Applying an advance settles part of the amount due. Unallocated advances remain separate from salary.',
                ),
              ),
            ]),
          if (opening != null)
            card([
              title(copy('افتتاح موثق', 'Reviewed opening')),
              if (opening['originalAmount'] != null)
                line(
                  'أصل الافتتاح',
                  'Original opening',
                  opening['originalAmount'],
                ),
              for (final c in rows(opening['corrections'] ?? const []))
                line(
                  'تصحيح موثق — داخل الإجمالي وليس صرفًا',
                  'Reviewed correction — included, not cash',
                  c['delta'],
                ),
              line('الرصيد القديم', 'Prior balance', opening['amount']),
              line('المسدد', 'Settled', opening['paid']),
              line('المتبقي', 'Remaining', opening['remaining']),
              for (final p in rows(opening['payments']))
                p['kind'] == 'AdvanceApplication'
                    ? line(
                        'تسوية من سلفة سابقة',
                        'Settled from prior advance',
                        p['amount'],
                      )
                    : line('صرف افتتاح', 'Opening payout', p['amount']),
            ]),
          for (final s in finals)
            card([
              title(
                copy(
                  'عمولة بعد نهاية العقد — ليست راتب شهر',
                  'Post-contract commission — not monthly salary',
                ),
              ),
              line('المستحق', 'Due', s['amount']),
              line('المسدد', 'Settled', s['paid']),
              line('المتبقي', 'Remaining', s['remaining']),
              for (final p in rows(s['payments']))
                line('صرف مسجل', 'Recorded payout', p['amount']),
            ]),
          if ((data['legacyPaidMonthsExcluded'] as num? ?? 0) > 0)
            UiText(
              copy(
                'الأشهر المسددة بالمسار السابق مستبعدة من إجمالي هذا الكشف.',
                'Legacy paid months are excluded from this account total.',
              ),
            ),
          ExpansionTile(
            title: UiText(copy('نطاق الكشف', 'Statement scope')),
            childrenPadding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
            expandedCrossAxisAlignment: CrossAxisAlignment.start,
            children: [
              UiText(
                opening == null
                    ? copy(
                        'يعرض الكشف المستحقات المعتمدة. الأرصدة الافتتاحية والسلف التاريخية غير المستوردة تحتاج توثيقًا منفصلًا.',
                        'This account shows approved dues. Opening balances and unimported historical advances require separate verification.',
                      )
                    : copy(
                        'الرصيد الافتتاحي الموثق ضمن الإجمالي. السلف التاريخية غير المستوردة تحتاج توثيقًا منفصلًا.',
                        'The reviewed opening balance is included in the total. Unimported historical advances require separate verification.',
                      ),
              ),
            ],
          ),
        ]);
      },
    ),
  );

  static String _monthLabel(
    MaterialLocalizations calendar,
    Object? value,
    String fallback,
  ) {
    final month = DateTime.tryParse('$value');
    return month == null ? fallback : calendar.formatMonthYear(month);
  }
}
