import 'package:flutter/material.dart';

import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import 'experience_ui.dart';

class PlayerRosterStatus extends StatelessWidget {
  const PlayerRosterStatus({super.key, required this.player});
  final Json player;
  @override
  Widget build(BuildContext context) {
    final s = player['subscription'] is Map
        ? obj(player['subscription'])
        : <String, dynamic>{};
    final left = s['remainingSessions'] as num?;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (s['expired'] == true)
          UiText(
            'الاشتراك انتهى',
            style: TextStyle(
              color: ExperienceColors.negative(context),
              fontSize: 11,
              fontWeight: FontWeight.w700,
            ),
          )
        else if (s['sessionBased'] == true && left != null && left <= 2)
          UiText(
            left == 2
                ? 'حصتان متبقيتان'
                : left == 1
                ? 'حصة واحدة متبقية'
                : 'لا توجد حصص متبقية',
            style: TextStyle(
              color: ExperienceColors.negative(context),
              fontSize: 11,
              fontWeight: FontWeight.w700,
            ),
          ),
        if (player['trainingSafetyNote'] is String &&
            (player['trainingSafetyNote'] as String).trim().isNotEmpty)
          Padding(
            padding: const EdgeInsets.only(top: 4),
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 5),
              decoration: BoxDecoration(
                color: ExperienceColors.warning(context).withValues(alpha: .12),
                borderRadius: BorderRadius.circular(8),
              ),
              child: UiText(
                'ملحوظة: ${player['trainingSafetyNote']}',
                style: TextStyle(
                  color: ExperienceColors.warning(context),
                  fontSize: 11,
                ),
              ),
            ),
          ),
      ],
    );
  }
}
