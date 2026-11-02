import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

class VisitHistoryPage extends StatelessWidget {
  final AuthController auth;
  final String bookingId;
  const VisitHistoryPage({
    super.key,
    required this.auth,
    required this.bookingId,
  });
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'سجل الدخول والخروج', 'Entry and exit history'),
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/facilities/bookings/$bookingId/visits',
      builder: (context, value) {
        final visits = rows(obj(value)['visits']);
        return gList([
          UiText(
            localized(
              context,
              'سجل الزيارة لا يثبت حضور الحصة. التوقيت بتوقيت الجهاز.',
              'A visit is not session attendance. Times use your device timezone.',
            ),
          ),
          for (final v in visits)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(textOf(v['name'])),
                  UiText(
                    v['personKind'] == 'Companion'
                        ? localized(
                            context,
                            'مرافق غير متدرب',
                            'Non-training companion',
                          )
                        : localized(context, 'مشارك', 'Participant'),
                  ),
                  UiText(
                    '${localized(context, 'الدخول', 'Entry')}: ${stamp(v['enteredAtUtc'])}',
                  ),
                  UiText(
                    v['exitedAtUtc'] != null
                        ? '${localized(context, 'الخروج', 'Exit')}: ${stamp(v['exitedAtUtc'])}'
                        : v['staleOpen'] == true
                        ? localized(
                            context,
                            'تجاوز الموعد دون خروج · يحتاج مراجعة',
                            'Past booking time without checkout · review required',
                          )
                        : localized(
                            context,
                            'بالداخل وفق السجل · تقدير',
                            'Inside according to the record · estimate',
                          ),
                  ),
                ],
              ),
            ),
          if (visits.isEmpty)
            EmptyMessage(
              localized(
                context,
                'لا توجد زيارات مسجلة.',
                'No recorded visits.',
              ),
            ),
        ]);
      },
    ),
  );
  String stamp(Object? value) {
    final d = DateTime.tryParse(textOf(value))?.toLocal();
    return d == null
        ? '—'
        : '${d.year}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')} ${d.hour.toString().padLeft(2, '0')}:${d.minute.toString().padLeft(2, '0')}';
  }
}
