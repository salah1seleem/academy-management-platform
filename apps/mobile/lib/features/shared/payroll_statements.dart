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
      path: '/api/v1/staff/payroll/statements',
      builder: (context, value) {
        final data = obj(value);
        final items = rows(data['items']);
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
              'الكشوف المعتمدة فقط، لآخر 24 شهرًا معتمدًا. الرصيد السابق والسلف غير موثقين هنا.',
              'Approved statements only, latest 24 approved months. Opening balances and advances are not verified here.',
            ),
          ),
          if (items.isEmpty)
            EmptyMessage(
              localized(
                context,
                'لا كشف معتمد بعد؛ هذا لا يعني أن مستحقاتك صفر.',
                'No approved statement yet; this does not mean your dues are zero.',
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
                    '${localized(context, 'المستحق', 'Due')}: ${s['netDue']} ${s['currency']}',
                  ),
                  UiText(
                    '${localized(context, 'المدفوع', 'Paid')}: ${s['paid']} ${s['currency']}',
                  ),
                  UiText(
                    '${localized(context, 'المتبقي', 'Remaining')}: ${s['remaining']} ${s['currency']}',
                  ),
                  UiText(
                    '${localized(context, 'أساسي / خصومات / إضافات', 'Base / deductions / additions')}: ${s['baseDue']} / ${s['deductions']} / ${s['additions']}',
                  ),
                  for (final p in rows(s['payments']))
                    ListTile(
                      leading: const Icon(Icons.receipt_long_outlined),
                      title: UiText(
                        '${localized(context, 'صرف مسجل', 'Recorded payout')}: ${p['amount']} ${s['currency']}',
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
