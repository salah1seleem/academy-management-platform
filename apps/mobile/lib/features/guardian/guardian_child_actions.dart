import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/academy_brand.dart';
import '../../core/theme/ui_text.dart';
import '../shared/matches.dart';
import 'guardian_content.dart';
import 'guardian_profile.dart';
import 'guardian_ui.dart';

class GuardianChildActions extends StatefulWidget {
  const GuardianChildActions({
    super.key,
    required this.auth,
    required this.playerId,
    required this.playerName,
    required this.nutritionDate,
    this.enrollmentId,
    this.nextSession,
  });

  final AuthController auth;
  final String playerId, playerName, nutritionDate;
  final String? enrollmentId;
  final Json? nextSession;

  @override
  State<GuardianChildActions> createState() => _GuardianChildActionsState();
}

class _GuardianChildActionsState extends State<GuardianChildActions> {
  Future<Json?>? matchRequest;

  @override
  void didUpdateWidget(GuardianChildActions oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.auth != widget.auth ||
        oldWidget.playerId != widget.playerId) {
      matchRequest = null;
    }
  }

  Future<Json?> readMatch() async {
    final matches =
        rows(
          await widget.auth.request(
            'GET',
            '/api/v1/my-matches?upcomingOnly=true&playerId=${Uri.encodeQueryComponent(widget.playerId)}',
          ),
        ).where((match) => match['isUpcoming'] == true).toList()..sort(
          (a, b) => '${a['matchDate']} ${a['startTime']}'.compareTo(
            '${b['matchDate']} ${b['startTime']}',
          ),
        );
    return matches.firstOrNull;
  }

  String appointment(Object? date, Object? time) => [
    day(date),
    textOf(time).split(':').take(2).join(':'),
  ].where((value) => value.isNotEmpty).join(' · ');

  @override
  Widget build(BuildContext context) {
    final matchesEnabled =
        BrandScope.maybeOf(context)?.features.contains('matches') ?? true;
    return FutureBuilder<Json?>(
      future: matchesEnabled ? matchRequest ??= readMatch() : null,
      builder: (context, snapshot) {
        final match = matchesEnabled ? snapshot.data : null;
        final session = widget.nextSession;
        final actions = [
          if (widget.enrollmentId != null)
            (
              icon: Icons.calendar_today_outlined,
              label: session == null ? 'جدول الحصص' : 'الحصة القادمة',
              detail: session == null
                  ? ''
                  : appointment(session['sessionDate'], session['startTime']),
              page: () => ProfileRecords(
                auth: widget.auth,
                playerId: widget.playerId,
                enrollmentId: widget.enrollmentId,
                section: 'schedule',
              ),
            ),
          if (match != null)
            (
              icon: Icons.sports_soccer_outlined,
              label: 'المباراة القادمة',
              detail: appointment(match['matchDate'], match['startTime']),
              page: () => MatchSchedule(
                auth: widget.auth,
                playerId: widget.playerId,
                matchId: textOf(match['id']),
              ),
            ),
          (
            icon: Icons.restaurant_outlined,
            label: 'النظام الغذائي',
            detail: 'اقتراحات اليوم',
            page: () => NutritionLibrary(
              auth: widget.auth,
              playerId: widget.playerId,
              playerName: widget.playerName,
              today: widget.nutritionDate,
              showToday: true,
            ),
          ),
        ];
        return LayoutBuilder(
          builder: (context, constraints) {
            final horizontal =
                constraints.maxWidth >= 260 &&
                MediaQuery.textScalerOf(context).scale(1) <= 1.25;
            final width = horizontal
                ? (constraints.maxWidth - (actions.length - 1) * 4) /
                      actions.length
                : constraints.maxWidth;
            return Wrap(
              spacing: 4,
              runSpacing: 4,
              children: [
                for (final action in actions)
                  SizedBox(
                    width: width,
                    child: TextButton(
                      onPressed: () => openPage(context, action.page()),
                      style: TextButton.styleFrom(
                        minimumSize: const Size(0, 48),
                        padding: const EdgeInsets.symmetric(
                          horizontal: 3,
                          vertical: 6,
                        ),
                        backgroundColor: Theme.of(context)
                            .colorScheme
                            .surfaceContainerHighest,
                      ),
                      child: Column(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Icon(action.icon, size: 16),
                          UiText(
                            action.label,
                            textAlign: TextAlign.center,
                            style: const TextStyle(
                              fontSize: 11,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                          if (action.detail.isNotEmpty)
                            UiText(
                              action.detail,
                              textAlign: TextAlign.center,
                              style: const TextStyle(fontSize: 10),
                            ),
                        ],
                      ),
                    ),
                  ),
              ],
            );
          },
        );
      },
    );
  }
}
