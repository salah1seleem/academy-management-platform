import 'package:flutter/material.dart';

import '../shared/help_center.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../../core/widgets/app_widgets.dart';
import '../guardian/guardian_ui.dart';
import '../shared/private_media.dart';
import 'coach_evaluation.dart';
import '../shared/matches.dart';

// Uses the existing authorized roster/options, never a guardian or medical API.
class CoachPlayerPage extends StatefulWidget {
  final AuthController auth;
  final String enrollmentId;
  final String? sessionId, groupId;
  const CoachPlayerPage({
    super.key,
    required this.auth,
    required this.enrollmentId,
    this.sessionId,
    this.groupId,
  });

  @override
  State<CoachPlayerPage> createState() => _CoachPlayerPageState();
}

String? coachContext(AuthController auth) => auth.session?.selected == null
    ? null
    : '${auth.session!.selected!.academyId}:${auth.session!.selected!.id}:${auth.session!.selected!.contextRevision}';

class _CoachPlayerPageState extends State<CoachPlayerPage> {
  late final String? identity = coachContext(widget.auth);
  @override
  Widget build(BuildContext context) => AppScaffold(
    title: 'ملف اللاعب',
    child: ListenableBuilder(
      listenable: widget.auth,
      builder: (context, _) {
        if (identity == null || identity != coachContext(widget.auth)) {
          return const SizedBox.shrink();
        }
        return RemoteBody(
          auth: widget.auth,
          path: widget.sessionId == null
              ? '/api/v1/evaluations/options'
              : '/api/v1/attendance/sessions/${widget.sessionId}/players',
          builder: (context, value) {
            final data = obj(value),
                session = obj(data['session'] ?? <String, dynamic>{});
            final matches =
                rows(data[widget.sessionId == null ? 'enrollments' : 'players'])
                    .where(
                      (p) =>
                          (p['id'] ?? p['sportEnrollmentId']) ==
                              widget.enrollmentId &&
                          (widget.sessionId != null ||
                              widget.groupId == null ||
                              p['groupId'] == widget.groupId),
                    );
            if (matches.isEmpty) {
              return Center(
                child: UiText(
                  localized(
                    context,
                    'لم يعد اللاعب ضمن هذا الكشف.',
                    'The player is no longer in this roster.',
                  ),
                ),
              );
            }
            final p = matches.first;
            final name = textOf(p['player'] ?? p['arabicName']);
            return gList([
              HelpEntry(auth: widget.auth, lesson: 'evaluations'),
              AppCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    CoachPlayerAvatar(
                      auth: widget.auth,
                      reference: p['photoReference'],
                      size: 64,
                    ),
                    const SizedBox(height: 12),
                    UiText(
                      name,
                      style: Theme.of(context).textTheme.headlineSmall,
                    ),
                    UiText(textOf(p['playerCode'])),
                    const SizedBox(height: 12),
                    UiText(textOf(p['group'] ?? session['group'])),
                    UiText(textOf(p['sport'] ?? session['sport'])),
                    if (widget.sessionId != null) ...[
                      UiText(day(session['sessionDate'])),
                      UiText(
                        '${localized(context, 'الحالة', 'Status')}: ${label(p['attendanceStatus'])}',
                      ),
                      if (p['subscriptionPlan'] != null)
                        UiText(textOf(p['subscriptionPlan'])),
                      if (p['remainingSessions'] != null)
                        UiText(
                          localized(
                            context,
                            '${p['remainingSessions']} حصص متبقية',
                            '${p['remainingSessions']} sessions remaining',
                          ),
                        ),
                    ],
                  ],
                ),
              ),
              if (data['canEvaluate'] == true)
                FilledButton.icon(
                  icon: const Icon(Icons.assessment_outlined),
                  label: const UiText('تقييم اللاعب'),
                  onPressed: () => openPage(
                    context,
                    CoachEvaluation(
                      auth: widget.auth,
                      enrollment: {
                        ...p,
                        'id': widget.enrollmentId,
                        'player': name,
                        'group': p['group'] ?? session['group'],
                        if (widget.sessionId != null)
                          'sessionId': widget.sessionId,
                        if (widget.sessionId != null)
                          'evaluationDate': session['sessionDate'],
                      },
                    ),
                  ),
                ),
              if (p['playerId'] is String)
                GLink(
                  'المباريات',
                  Icons.emoji_events_outlined,
                  () => openPage(
                    context,
                    MatchSchedule(
                      auth: widget.auth,
                      playerId: p['playerId'] as String,
                    ),
                  ),
                ),
            ]);
          },
        );
      },
    ),
  );
}

class CoachPlayerAvatar extends StatelessWidget {
  final AuthController auth;
  final Object? reference;
  final double size;
  const CoachPlayerAvatar({
    super.key,
    required this.auth,
    this.reference,
    this.size = 48,
  });
  @override
  Widget build(BuildContext context) => SizedBox.square(
    dimension: size,
    child: ClipOval(
      child: isPrivateAsset(reference)
          ? PrivateImage(
              auth: auth,
              path: reference.toString(),
              height: size,
              fallback: Icon(
                Icons.person_outline_rounded,
                size: size * .6,
                color: Theme.of(context).colorScheme.onSurfaceVariant,
              ),
            )
          : ColoredBox(
              color: Theme.of(context).colorScheme.primaryContainer,
              child: Icon(
                Icons.person_outline_rounded,
                color: Theme.of(context).colorScheme.onPrimaryContainer,
                size: size * .6,
              ),
            ),
    ),
  );
}

bool playerMatches(Json player, String query) {
  final term = query.trim().toLowerCase();
  return term.isEmpty ||
      '${player['player'] ?? player['arabicName']} ${player['playerCode'] ?? ''}'
          .toLowerCase()
          .contains(term);
}
