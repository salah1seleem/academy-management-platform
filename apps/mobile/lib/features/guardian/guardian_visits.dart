import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import 'guardian_ui.dart';

class GuardianVisits extends StatelessWidget {
  final AuthController auth;
  final String playerId;
  const GuardianVisits({super.key, required this.auth, required this.playerId});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'الدخول والخروج', 'Entry and exit'),
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/children/$playerId/visits',
      builder: (context, value) {
        final visits = rows(obj(value)['visits']);
        String stamp(Object? value) {
          final d = DateTime.tryParse(textOf(value))?.toLocal();
          return d == null
              ? '—'
              : '${d.year}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')} ${d.hour.toString().padLeft(2, '0')}:${d.minute.toString().padLeft(2, '0')}';
        }

        return gList([
          UiText(
            localized(
              context,
              'أحدث100 زيارة؛ السجل مستقل عن حضور الحصة. التوقيت بتوقيت الجهاز.',
              'Latest 100 visits; independent of session attendance. Device timezone.',
            ),
          ),
          for (final v in visits)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText('${v['facility']} · ${v['resource']}'),
                  UiText(
                    '${localized(context, 'الدخول', 'Entry')}: ${stamp(v['enteredAtUtc'])}',
                  ),
                  UiText(
                    v['exitedAtUtc'] != null
                        ? '${localized(context, 'الخروج', 'Exit')}: ${stamp(v['exitedAtUtc'])}'
                        : v['staleOpen'] == true
                        ? localized(
                            context,
                            'لم يسجل خروج بعد انتهاء الموعد · بانتظار مراجعة الأكاديمية',
                            'No checkout after booking time · awaiting academy review',
                          )
                        : localized(
                            context,
                            'زيارة مفتوحة وفق السجل · ليست عدًا فعليًا',
                            'Open visit record · not a physical count',
                          ),
                  ),
                ],
              ),
            ),
          if (visits.isEmpty)
            EmptyMessage(
              localized(
                context,
                'لا توجد زيارات مسجلة للطفل.',
                'No recorded visits for your child.',
              ),
            ),
        ]);
      },
    ),
  );
}
