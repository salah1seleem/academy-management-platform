import 'package:flutter/material.dart';

import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import 'guardian_ui.dart';

class CompensationCard extends StatelessWidget {
  final Json data;
  const CompensationCard({super.key, required this.data});
  @override
  Widget build(BuildContext context) {
    final units = (data['units'] as num?)?.toInt() ?? 0;
    final allocated = (data['allocated'] as num?)?.toInt() ?? 0;
    final consumed = (data['consumed'] as num?)?.toInt() ?? 0;
    final unit = data['unit'] == 'Minute'
        ? localized(context, 'دقيقة', 'minutes')
        : localized(context, 'حصة', 'session');
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(Icons.event_available_outlined),
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  '${data['sport']} · $units $unit',
                  style: const TextStyle(fontWeight: FontWeight.bold),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          UiText(
            '${localized(context, 'عن حصة', 'Source session')} ${day(data['sourceDate'])} · ${localized(context, 'حتى', 'valid until')} ${day(data['validTo'])}',
          ),
          Wrap(
            spacing: 8,
            children: [
              Chip(
                label: UiText(
                  '${localized(context, 'استُخدم', 'Used')} $consumed',
                ),
              ),
              Chip(
                label: UiText(
                  '${localized(context, 'غير محجوز', 'Unbooked')} ${units - allocated}',
                ),
              ),
            ],
          ),
          for (final booking in rows(
            data['bookings'],
          ).where((b) => b['state'] == 'Reserved'))
            Padding(
              padding: const EdgeInsets.only(top: 4),
              child: Text('${booking['group']} · ${day(booking['date'])}'),
            ),
        ],
      ),
    );
  }
}
