import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../owner/owner_followups.dart';
import '../shared/experience_ui.dart';

class CoachMonthlyStars extends StatefulWidget {
  const CoachMonthlyStars({super.key, required this.auth});
  final AuthController auth;
  @override
  State<CoachMonthlyStars> createState() => _CoachMonthlyStarsState();
}

class _CoachMonthlyStarsState extends State<CoachMonthlyStars> {
  int revision = 0;
  bool busy = false;
  String? error;
  final requests = <String, String>{};
  Future<void> grant(Json team, Json player) async {
    final yes = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const UiText('منح نجمة شهرية'),
        content: UiText(
          'منح ${player['arabicName']} نجمة من رصيد ${team['name']}؟ المتبقي ${team['remaining']}.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const UiText('رجوع'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const UiText('تأكيد'),
          ),
        ],
      ),
    );
    if (yes != true || !mounted || busy) return;
    final key = '${team['id']}:${player['id']}';
    setState(() {
      busy = true;
      error = null;
    });
    try {
      await widget.auth.request(
        'POST',
        '/api/v1/coach/monthly-stars',
        body: {
          'teamId': team['id'],
          'playerId': player['id'],
          'requestId': requests.putIfAbsent(key, followUpRequestId),
        },
      );
      if (mounted) {
        requests.remove(key);
        setState(() => revision++);
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: UiText('تم منح النجمة وإبلاغ ولي الأمر.')),
        );
      }
    } catch (_) {
      if (mounted) {
        setState(() => error = 'تعذر منح النجمة؛ حدّث الرصيد أو حاول مجددًا.');
      }
    } finally {
      if (mounted) setState(() => busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'نجوم فريقي الشهرية',
    child: RemoteBody(
      key: ValueKey(revision),
      auth: widget.auth,
      path: '/api/v1/coach/monthly-stars',
      builder: (context, value) {
        final data = obj(value);
        return gList([
          UiText(
            'شهر ${textOf(data['month']).substring(0, 7)} · 3 نجوم لكل فريق',
            style: const TextStyle(fontSize: 12),
          ),
          if (error != null) UiText(error!),
          if (rows(data['teams']).isEmpty)
            const EmptyMessage('لا توجد فرق مسندة إليك.'),
          for (final team in rows(data['teams'])) ...[
            ExperienceSection(
              '${team['name']} · ${team['remaining']} نجوم متبقية',
              Icons.star_rounded,
              color: ExperienceColors.gold,
            ),
            if (rows(team['players']).isEmpty)
              const EmptyMessage(
                'لاعبو المباريات المنشورة يظهرون هنا بعد ضمهم لقائمة الفريق.',
              ),
            for (final player in rows(team['players']))
              GCard(
                child: ListTile(
                  contentPadding: EdgeInsets.zero,
                  leading: PlayerAvatar(
                    textOf(player['arabicName']),
                    auth: widget.auth,
                    reference: player['photoReference'],
                    size: 48,
                  ),
                  title: UiText(
                    textOf(player['arabicName']),
                    style: const TextStyle(fontSize: 13),
                  ),
                  subtitle: UiText(
                    '${player['stars']} ★ هذا الشهر',
                    style: const TextStyle(
                      color: ExperienceColors.gold,
                      fontSize: 11,
                    ),
                  ),
                  trailing: IconButton(
                    key: ValueKey('star-${team['id']}-${player['id']}'),
                    tooltip: 'منح نجمة',
                    onPressed: busy || (team['remaining'] as num) <= 0
                        ? null
                        : () => grant(team, player),
                    icon: const Icon(
                      Icons.star_border_rounded,
                      color: ExperienceColors.gold,
                    ),
                  ),
                ),
              ),
          ],
        ]);
      },
    ),
  );
}
