import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

class StaffContractHistory extends StatelessWidget {
  final AuthController auth;
  const StaffContractHistory({super.key, required this.auth});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'عقدي وتغييراته', 'My employment contract'),
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/staff/payroll/contract-versions',
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
        String end(Object? date) =>
            date?.toString() ?? localized(context, 'دائم', 'Open-ended');
        return gList([
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                UiText(
                  localized(
                    context,
                    'مدة عقدي الحالية',
                    'Current employment dates',
                  ),
                ),
                UiText(
                  '${data['contractStart']} — ${end(data['contractEnd'])}',
                ),
                UiText(
                  localized(
                    context,
                    'تؤثر مدة العقد في أيام الاستحقاق، ولا تعني صرف الراتب.',
                    'Contract dates affect eligible days, not salary payment.',
                  ),
                ),
              ],
            ),
          ),
          if (data['hasVerifiedHistory'] != true)
            UiText(
              localized(
                context,
                'التاريخ السابق غير مؤرخ؛ لم نفترض سجلًا سابقًا موثقًا.',
                'Prior contract history has not been verified.',
              ),
            ),
          for (final v in rows(data['items']))
            GCard(
              child: ListTile(
                leading: const Icon(Icons.description_outlined),
                title: UiText(
                  '${v['contractStart']} — ${end(v['contractEnd'])}',
                ),
                subtitle: UiText(
                  '${localized(context, 'يسري من', 'Effective from')} ${v['effectiveFrom']}',
                ),
              ),
            ),
        ]);
      },
    ),
  );
}
