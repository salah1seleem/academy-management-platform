import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import 'guardian_ui.dart';

Iterable<Json> compensationForEnrollment(Json profile, Object? enrollmentId) =>
    rows(profile['compensations'] ?? <Json>[]).where(
      (grant) =>
          enrollmentId != null && grant['sportEnrollmentId'] == enrollmentId,
    );

Set<Object> makeupSessionIds(Iterable<Json> grants) => {
  for (final grant in grants)
    for (final booking in rows(grant['bookings'] ?? <Json>[]))
      if (booking['state'] == 'Reserved' &&
          booking['trainingSessionId'] != null)
        booking['trainingSessionId']!,
};

class CompensationCard extends StatelessWidget {
  const CompensationCard({super.key, required this.data, this.today});

  final Json data;
  final String? today;

  String date(BuildContext context, Object? value) {
    final parsed = DateTime.tryParse('$value');
    if (parsed == null) {
      return localized(context, 'موعد غير محدد', 'Date unavailable');
    }
    if (Localizations.localeOf(context).languageCode == 'ar') {
      final format = DateFormat.yMMMd('ar')..useNativeDigits = false;
      return format.format(parsed);
    }
    return MaterialLocalizations.of(context).formatShortDate(parsed);
  }

  String amount(BuildContext context, int value) {
    final minutes = data['unit'] == 'Minute';
    if (Localizations.localeOf(context).languageCode != 'ar') {
      return '$value ${minutes ? (value == 1 ? 'minute' : 'minutes') : (value == 1 ? 'session' : 'sessions')}';
    }
    if (value == 1) return minutes ? 'دقيقة واحدة' : 'حصة واحدة';
    if (value == 2) return minutes ? 'دقيقتان' : 'حصتان';
    final unit = minutes
        ? (value <= 10 ? 'دقائق' : 'دقيقة')
        : (value <= 10 ? 'حصص' : 'حصة');
    return '$value $unit';
  }

  String bookingStatus(BuildContext context, Json booking) {
    if (booking['state'] == 'Cancelled') {
      return localized(context, 'أُلغي الحجز', 'Booking cancelled');
    }
    if (((booking['consumed'] as num?) ?? 0) > 0) {
      return localized(context, 'تم الحضور بالتعويض', 'Attended as makeup');
    }
    if (today != null && day(booking['date']).compareTo(today!) < 0) {
      return localized(context, 'انتهى الموعد', 'Session date passed');
    }
    return localized(context, 'موعد محجوز', 'Booked');
  }

  @override
  Widget build(BuildContext context) {
    final units = (data['units'] as num?)?.toInt() ?? 0;
    final allocated = (data['allocated'] as num?)?.toInt() ?? 0;
    final consumed = (data['consumed'] as num?)?.toInt() ?? 0;
    final remaining = (units - allocated).clamp(0, units).toInt();
    final start = data['validFrom'], end = data['validTo'];
    final upcoming =
        today != null && start is String && today!.compareTo(start) < 0;
    final expired = today != null && end is String && today!.compareTo(end) > 0;
    final availableLabel = today == null
        ? localized(context, 'غير محجوز', 'Unbooked')
        : localized(context, 'متاح للحجز', 'Available to book');
    final bookings = rows(data['bookings'] ?? <Json>[]).where(
      (booking) =>
          booking['state'] == 'Reserved' || booking['state'] == 'Cancelled',
    );
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Heading('${data['sport']} · ${amount(context, units)}'),
          UiText(
            '${localized(context, 'عن حصة', 'Source session')} ${date(context, data['sourceDate'])} · '
            '${localized(context, 'صالح حتى', 'Valid until')} ${date(context, end)}',
          ),
          const SizedBox(height: 6),
          UiText(
            expired
                ? localized(context, 'انتهت صلاحية الرصيد', 'Credit expired')
                : upcoming
                ? '${localized(context, 'يبدأ الحجز من', 'Booking starts')} ${date(context, start)}'
                : '$availableLabel ${amount(context, remaining)}',
            style: const TextStyle(fontWeight: FontWeight.w600),
          ),
          if (consumed > 0)
            UiText(
              '${localized(context, 'المستخدم في الحضور', 'Used for attendance')} ${amount(context, consumed)}',
            ),
          for (final booking in bookings) ...[
            const Divider(),
            UiText(textOf(booking['group'])),
            UiText(date(context, booking['date'])),
            if (booking['startTime'] is String && booking['endTime'] is String)
              UiText(
                trainingTimeRange(booking['startTime'], booking['endTime']),
              ),
            UiText(
              bookingStatus(context, booking),
              style: const TextStyle(fontWeight: FontWeight.w600),
            ),
          ],
        ],
      ),
    );
  }
}
