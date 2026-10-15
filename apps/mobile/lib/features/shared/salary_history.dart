import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

class StaffSalaryHistory extends StatelessWidget {
  final AuthController auth;
  const StaffSalaryHistory({super.key, required this.auth});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'راتبي وتغييراته', 'My salary history'),
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/staff/payroll/salary-versions',
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
        return gList([
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                UiText(
                  localized(
                    context,
                    'الراتب الشهري الحالي — ليس صرفًا',
                    'Current monthly rate — not a payment',
                  ),
                ),
                UiText('${data['currentSalary']} ${data['currency']}'),
                UiText(
                  localized(
                    context,
                    'الاستحقاق الفعلي يتأثر بأيام العقد والإضافات والخصومات المعتمدة.',
                    'Actual dues depend on contract days and approved adjustments.',
                  ),
                ),
              ],
            ),
          ),
          if (data['hasVerifiedHistory'] != true)
            UiText(
              localized(
                context,
                'المرجع القديم غير مؤرخ؛ لا يعني أن تاريخ الرواتب السابق موثق.',
                'Legacy reference is undated; prior salary history is not verified.',
              ),
            ),
          for (final v in rows(data['items']))
            GCard(
              child: ListTile(
                leading: const Icon(Icons.history_outlined),
                title: UiText('${v['monthlySalary']} ${v['currency']}'),
                subtitle: UiText(
                  '${localized(context, 'يسري من', 'Effective from')} ${v['effectiveFrom']} · ${localized(context, 'نسخة', 'Version')} ${v['revision']}',
                ),
              ),
            ),
        ]);
      },
    ),
  );
}
