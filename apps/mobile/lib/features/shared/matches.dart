import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';

class NextMatchStrip extends StatelessWidget {
  final AuthController auth;
  const NextMatchStrip({super.key, required this.auth});
  @override
  Widget build(BuildContext context) => RemoteBody(
    auth: auth,
    path: '/api/v1/my-matches',
    builder: (context, value) {
      final matches = rows(value)
          .where((x) => x['isUpcoming'] == true)
          .take(3)
          .toList();
      if (matches.isEmpty) return const SizedBox.shrink();
      return Column(
        children: [
          for (final m in matches)
            GCard(
              onTap: () => openPage(context, MatchSchedule(auth: auth)),
              child: Row(
                children: [
                  Container(
                    width: 52,
                    height: 66,
                    decoration: BoxDecoration(
                      color: Theme.of(context).colorScheme.primary
                          .withValues(alpha: .14),
                      borderRadius: BorderRadius.circular(14),
                    ),
                    child: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        UiText(
                          day(m['matchDate']).split('-').last,
                          style: TextStyle(
                            fontSize: 22,
                            fontWeight: FontWeight.w800,
                            color: Theme.of(context).colorScheme.primary,
                          ),
                        ),
                        UiText(
                          day(m['matchDate'])
                              .split('-')
                              .take(2)
                              .toList()
                              .reversed
                              .join('/'),
                          style: const TextStyle(fontSize: 10),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        UiText(
                          '${m['team']} × ${m['opponent']}',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: const TextStyle(
                            fontSize: 13,
                            fontWeight: FontWeight.w800,
                          ),
                        ),
                        UiText(
                          '${m['tournament']} · ${m['startTime'].toString().substring(0, 5)}',
                          style: Theme.of(context).textTheme.labelSmall,
                        ),
                        UiText(
                          textOf(m['venue']),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: Theme.of(context).textTheme.labelSmall,
                        ),
                      ],
                    ),
                  ),
                  const Icon(Icons.chevron_left, size: 18),
                ],
              ),
            ),
        ],
      );
    },
  );
}

class MatchSchedule extends StatelessWidget {
  final AuthController auth;
  const MatchSchedule({super.key, required this.auth});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'المباريات', 'Matches'),
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/my-matches',
      builder: (context, v) => gList([
        if (rows(v).isEmpty)
          const EmptyMessage('لا توجد مباريات منشورة تخصك حاليًا.'),
        for (final m in rows(v))
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Heading('${m['team']} × ${m['opponent']}'),
                UiText(
                  '${m['tournament']} · ${day(m['matchDate'])} · ${m['startTime']}',
                ),
                UiText(textOf(m['venue'])),
                if (m['notes'] != null) UiText(textOf(m['notes'])),
                if (m['ourScore'] != null)
                  UiText(
                    '${localized(context, 'النتيجة', 'Result')}: ${m['ourScore']} — ${m['opponentScore']}',
                  ),
              ],
            ),
          ),
      ]),
    ),
  );
}
