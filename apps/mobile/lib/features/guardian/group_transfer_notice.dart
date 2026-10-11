import 'package:flutter/material.dart';

import '../../core/theme/ui_text.dart';
import '../../core/theme/appearance.dart';

class GroupTransferNotice extends StatelessWidget {
  final String group, date;
  final bool occurrence;
  const GroupTransferNotice({
    super.key,
    required this.group,
    required this.date,
    this.occurrence = false,
  });
  @override
  Widget build(BuildContext context) => Container(
    margin: const EdgeInsets.symmetric(vertical: 8),
    padding: const EdgeInsets.all(12),
    decoration: BoxDecoration(
      color: Theme.of(context).colorScheme.primary.withValues(alpha: .1),
      borderRadius: BorderRadius.circular(14),
    ),
    child: Row(
      children: [
        const Icon(Icons.swap_horiz_rounded),
        const SizedBox(width: 10),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              UiText(
                localized(
                  context,
                  occurrence ? 'حصة بديلة' : 'نقل مجموعة مجدول',
                  occurrence
                      ? 'Replacement session'
                      : 'Scheduled group transfer',
                ),
                style: const TextStyle(fontWeight: FontWeight.bold),
              ),
              Text('$group · $date'),
              UiText(
                localized(
                  context,
                  occurrence
                      ? 'هذا الموعد فقط؛ المجموعة الأساسية كما هي.'
                      : 'المواعيد الجديدة تبدأ في هذا التاريخ.',
                  occurrence
                      ? 'This session only; the regular group is unchanged.'
                      : 'New sessions start on this date.',
                ),
              ),
            ],
          ),
        ),
      ],
    ),
  );
}
