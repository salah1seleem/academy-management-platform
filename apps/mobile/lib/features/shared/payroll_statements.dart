import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

class StaffPayrollStatements extends StatelessWidget {
  final AuthController auth;
  const StaffPayrollStatements({super.key, required this.auth});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'مستحقاتي والصرف', 'My approved dues'),
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/staff/payroll/account',
      builder: (context, value) {
        final data = obj(value);
        final items = rows(data['items']);
        final finals = rows(data['finalSettlements'] ?? const []);
        final opening = data['opening'] is Map ? obj(data['opening']) : null;
        if (data['configured'] == false) {
          return EmptyMessage(
            localized(
              context,
              'لم يربط سجل الموظف بحسابك بعد.',
              'No employee record is linked yet.',
            ),
          );
        }
        return gList([
          UiText(
            localized(
              context,
              opening == null
                  ? 'كل المصادر المعتمدة حتى ${data['asOf'] ?? ''}. الأرصدة الافتتاحية والسلف التاريخية غير المستوردة والأشهر غير المعتمدة غير موثقة هنا، وليست صفرًا.'
                  : 'افتتاح موثق قبل ${opening['asOf']}. داخل الإجمالي وليس راتب شهر. السلف التاريخية غير المستوردة والأشهر غير المعتمدة ليست صفرًا.',
              opening == null
                  ? 'All approved sources as of ${data['asOf'] ?? ''}. Opening balances, unimported historic advances and unapproved months are not verified here, not zero.'
                  : 'Reviewed opening before ${opening['asOf']}. Included in totals, not monthly salary. Unimported advances and unapproved months are not assumed zero.',
            ),
          ),
          if (items.isNotEmpty || opening != null || finals.isNotEmpty)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(
                    localized(
                      context,
                      'حساب مستحقاتك',
                      'Your approved account',
                    ),
                  ),
                  UiText(
                    '${localized(context, 'إجمالي المعتمد', 'Total approved')}: ${data['totalDue']} ${data['currency']}',
                  ),
                  UiText(
                    '${localized(context, 'إجمالي المسدد', 'Total settled')}: ${data['totalPaid']} ${data['currency']}',
                  ),
                  UiText(
                    '${localized(context, 'المتبقي المعروف', 'Known remaining')}: ${data['remaining']} ${data['currency']}',
                  ),
                  UiText(
                    '${localized(context, 'من أشهر سابقة', 'Prior months')}: ${data['priorRemaining']} ${data['currency']}',
                  ),
                  UiText(
                    '${localized(context, 'من الشهر المختار', 'Selected month')}: ${data['currentRemaining']} ${data['currency']}',
                  ),
                ],
              ),
            ),
          if (data['currentMonthApproved'] == false)
            UiText(
              localized(
                context,
                'لا كشف معتمد لهذا الشهر حتى التاريخ المختار.',
                'No approved statement for this month as of the selected date.',
              ),
            ),
          if (data['recordedAdvanceTotal'] != null)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(
                    localized(context, 'السلف المسجلة', 'Recorded advances'),
                  ),
                  UiText(
                    '${localized(context, 'سبق صرفها', 'Already paid')}: ${data['recordedAdvanceTotal']} ${data['currency']}',
                  ),
                  UiText(
                    '${localized(context, 'غير المخصص', 'Unallocated')}: ${data['unallocatedAdvances']} ${data['currency']}',
                  ),
                  UiText(
                    '${localized(context, 'تخصيص من سلف سابقة', 'Applied from prior advances')}: ${data['advanceApplied']} ${data['currency']}',
                  ),
                  UiText(
                    localized(
                      context,
                      'التخصيص يسدد المستحق وليس صرفًا جديدًا. غير المخصص لا يخصم تلقائيًا من راتبك.',
                      'Allocation settles dues, not a new payout. Unallocated advances are not automatically deducted.',
                    ),
                  ),
                ],
              ),
            ),
          if ((data['legacyPaidMonthsExcluded'] as num? ?? 0) > 0)
            UiText(
              localized(
                context,
                'الأشهر المسددة بالمسار السابق لم تُعَد ترحيلها إلى هذا الحساب.',
                'Previously settled legacy months are not reposted into this account.',
              ),
            ),
          if (opening != null)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(localized(context, 'افتتاح موثق', 'Reviewed opening')),
                  UiText(
                    '${localized(context, 'الرصيد القديم', 'Prior balance')}: ${opening['amount']} ${data['currency']}',
                  ),
                  UiText(
                    '${localized(context, 'المسدد', 'Settled')}: ${opening['paid']} ${data['currency']}',
                  ),
                  UiText(
                    '${localized(context, 'المتبقي', 'Remaining')}: ${opening['remaining']} ${data['currency']}',
                  ),
                  for (final p in rows(opening['payments']))
                    UiText(
                      '${p['kind'] == 'AdvanceApplication' ? localized(context, 'تخصيص سلفة سابقة — ليس صرفًا جديدًا', 'Prior advance allocation — not new cash') : localized(context, 'صرف افتتاح', 'Opening payout')}: ${p['amount']} ${data['currency']}',
                    ),
                ],
              ),
            ),
          if (items.isEmpty && opening == null && finals.isEmpty)
            EmptyMessage(
              localized(
                context,
                'لا كشف معتمد بعد؛ هذا لا يعني أن مستحقاتك صفر.',
                'No approved statement yet; this does not mean your dues are zero.',
              ),
            ),
          for (final s in finals)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(
                    localized(
                      context,
                      'عمولة بعد نهاية العقد — ليست راتب شهر',
                      'Post-contract commission — not monthly salary',
                    ),
                  ),
                  UiText(
                    '${localized(context, 'المستحق', 'Due')}: ${s['amount']} ${data['currency']}',
                  ),
                  UiText(
                    '${localized(context, 'المسدد', 'Settled')}: ${s['paid']} ${data['currency']}',
                  ),
                  UiText(
                    '${localized(context, 'المتبقي', 'Remaining')}: ${s['remaining']} ${data['currency']}',
                  ),
                  for (final p in rows(s['payments']))
                    UiText(
                      '${localized(context, 'صرف مسجل', 'Recorded payout')}: ${p['amount']} ${data['currency']}',
                    ),
                ],
              ),
            ),
          for (final s in items)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(
                    '${localized(context, 'كشف', 'Statement')} ${s['month']}',
                  ),
                  UiText(
                    '${localized(context, 'المستحق', 'Due')}: ${s['netDue']} ${data['currency']}',
                  ),
                  UiText(
                    '${localized(context, 'المسدد', 'Settled')}: ${s['paid']} ${data['currency']}',
                  ),
                  UiText(
                    '${localized(context, 'المتبقي', 'Remaining')}: ${s['remaining']} ${data['currency']}',
                  ),
                  UiText(
                    '${localized(context, 'أساسي / خصومات / إضافات', 'Base / deductions / additions')}: ${s['baseDue']} / ${s['deductions']} / ${s['additions']}',
                  ),
                  for (final p in rows(s['payments']))
                    ListTile(
                      leading: const Icon(Icons.receipt_long_outlined),
                      title: UiText(
                        '${p['kind'] == 'AdvanceApplication' ? localized(context, 'تخصيص سلفة سابقة — ليس صرفًا جديدًا', 'Prior advance allocation — not a new payout') : localized(context, 'صرف مسجل', 'Recorded payout')}: ${p['amount']} ${data['currency']}',
                      ),
                    ),
                ],
              ),
            ),
        ]);
      },
    ),
  );
}
