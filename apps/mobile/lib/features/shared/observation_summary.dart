import 'package:flutter/material.dart';

import '../../core/theme/appearance.dart';
import '../guardian/guardian_ui.dart';

class ObservationSummary extends StatelessWidget {
  final Json value;
  const ObservationSummary({super.key, required this.value});
  @override
  Widget build(BuildContext context) {
    final needsReview = value['needsReview'] == true;
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: needsReview ? const Color(0xFF422E11) : const Color(0xFF12392D),
        borderRadius: BorderRadius.circular(14),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            '${value['count']} ${localized(context, 'ملحوظ فعليًا', 'observed')}',
            style: const TextStyle(
              color: Colors.white,
              fontWeight: FontWeight.bold,
            ),
          ),
          Text(
            '${localized(context, value['expectedBasis'] == 'Roster' ? 'متوقع' : 'مقاعد محجوزة', value['expectedBasis'] == 'Roster' ? 'Expected' : 'Reserved seats')}: ${value['expectedSnapshot']}',
            style: const TextStyle(color: Colors.white),
          ),
          Text(
            '${localized(context, 'حاضر مسجل وقت الملاحظة', 'Recorded present at observation')}: ${value['recordedPresentSnapshot'] ?? '—'}',
            style: const TextStyle(color: Colors.white),
          ),
          Text(
            localized(
              context,
              needsReview ? 'فرق يحتاج تحققًا' : 'متطابق وقت الملاحظة',
              needsReview
                  ? 'Difference needs verification'
                  : 'Matched at observation',
            ),
            style: const TextStyle(color: Colors.white),
          ),
          if (DateTime.tryParse(textOf(value['observedAtUtc']))
              case final DateTime stamp)
            Text(
              '${localized(context, 'وقت الملاحظة بتوقيت الجهاز', 'Observed at, device time')}: ${TimeOfDay.fromDateTime(stamp.toLocal()).format(context)}',
              style: const TextStyle(color: Colors.white),
            ),
          if (value['outsideBookedHours'] == true)
            Text(
              localized(
                context,
                'العد خارج ساعات الحجز؛ راجع توقيته',
                'Outside booked hours; check the timing',
              ),
              style: const TextStyle(color: Colors.white),
            ),
        ],
      ),
    );
  }
}
