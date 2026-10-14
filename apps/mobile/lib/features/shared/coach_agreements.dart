import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

// Read-only approved terms for the selected coach membership. Never accepts an
// employee/coach ID from navigation and offers no rate, salary or approval edit.
class CoachAgreementsPage extends StatelessWidget {
  final AuthController auth;
  const CoachAgreementsPage({super.key, required this.auth});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(
      context,
      'اتفاقات أجر الخاص',
      'Private coaching agreements',
    ),
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/staff/payroll/agreements',
      builder: (context, value) {
        final items = rows(obj(value)['items']);
        return gList([
          UiText(
            localized(
              context,
              'شروط معتمدة للعرض فقط. الاتفاق ليس رصيدًا مستحقًا أو إثبات سداد.',
              'Approved terms only. An agreement is not earned compensation or payment.',
            ),
          ),
          if (items.isEmpty)
            EmptyMessage(
              localized(
                context,
                'لا يوجد اتفاق خاص معتمد لحسابك.',
                'No approved private agreement for your account.',
              ),
            ),
          for (final a in items)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      const Icon(Icons.verified_outlined),
                      const SizedBox(width: 8),
                      Expanded(child: UiText('${a['branch']} · ${a['sport']}')),
                    ],
                  ),
                  const SizedBox(height: 12),
                  UiText(
                    a['variableMethod'] == 'Percent'
                        ? '${a['variableValue']}%'
                        : '${a['variableValue']} ${a['currency']} ${localized(context, 'لكل حصة', 'per session')}',
                  ),
                  UiText(
                    a['eligibility'] == 'DeliveredCollected'
                        ? localized(
                            context,
                            'تنفيذ معتمد وتحصيل صافٍ',
                            'Approved delivery and net collection',
                          )
                        : localized(
                            context,
                            'تنفيذ معتمد ولو لم يحصل',
                            'Approved delivery even if uncollected',
                          ),
                  ),
                  UiText(
                    a['baseCoverage'] == 'IncludesPrivate'
                        ? localized(
                            context,
                            'الخاص مشمول بالأساسي',
                            'Private work included in base salary',
                          )
                        : localized(
                            context,
                            'الخاص إضافي إلى الأساسي',
                            'Private work additional to base salary',
                          ),
                  ),
                  const SizedBox(height: 8),
                  UiText(
                    '${a['effectiveFrom']} — ${a['effectiveTo'] ?? localized(context, 'دون نهاية محددة', 'Open-ended')}',
                  ),
                  UiText(
                    '${localized(context, 'نسخة معتمدة', 'Approved version')} ${a['version']}',
                  ),
                ],
              ),
            ),
        ]);
      },
    ),
  );
}
