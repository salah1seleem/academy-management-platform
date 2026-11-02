import 'package:flutter/material.dart';

import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

/// Never guess format or replacement from a name, client role, or attendance.
class SessionContextBadges extends StatelessWidget {
  final Json session;
  final bool showSource;
  const SessionContextBadges({
    super.key,
    required this.session,
    this.showSource = false,
  });

  @override
  Widget build(BuildContext context) {
    final format = session['trainingFormatSource'] == 'CurrentGroup'
        ? switch (session['trainingFormat']) {
            'Group' => localized(context, 'جماعية', 'Group'),
            'Private' => localized(context, 'برايفت', 'Private'),
            'SemiPrivate' => localized(context, 'شبه خاصة', 'Semi-private'),
            _ => null,
          }
        : null;
    final substitute = session['isSubstitute'] == true;
    if (format == null && !substitute) return const SizedBox.shrink();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            if (format != null)
              Chip(
                avatar: const Icon(Icons.sports_outlined, size: 18),
                label: UiText(format),
              ),
            if (substitute)
              Chip(
                avatar: const Icon(Icons.swap_horiz, size: 18),
                label: UiText(
                  localized(context, 'مدرب بديل', 'Substitute coach'),
                ),
              ),
          ],
        ),
        if (showSource && format != null)
          UiText(
            localized(
              context,
              'نوع التدريب حسب إعداد المجموعة الحالي',
              'Training format from the current group configuration',
            ),
            style: const TextStyle(fontSize: 14),
          ),
      ],
    );
  }
}
