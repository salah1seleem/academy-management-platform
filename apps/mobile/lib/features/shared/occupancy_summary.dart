import 'package:flutter/material.dart';

import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';

class OccupancySummary extends StatelessWidget {
  final Map<String, dynamic> value;
  const OccupancySummary({super.key, required this.value});
  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      UiText(
        '${value['participants'] ?? 0} ${localized(context, 'مشارك', 'participants')} · ${value['companions'] ?? 0} ${localized(context, 'مرافق', 'companions')}',
      ),
      UiText(
        localized(
          context,
          'زيارات مفتوحة · إشغال تقديري وليس عدًا فعليًا',
          'Open visits · estimated occupancy, not a physical count',
        ),
      ),
      if ((value['staleOpen'] as num? ?? 0) > 0)
        UiText(
          '${value['staleOpen']} ${localized(context, 'زيارة تجاوزت الموعد دون خروج؛ تحتاج مراجعة', 'visits past their time without checkout; review required')}',
        ),
    ],
  );
}
