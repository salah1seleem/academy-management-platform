import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/academy_brand.dart';
import '../../core/theme/ui_text.dart';
import 'match_experience.dart';
import 'page_video.dart';
import 'native_match_management.dart';
import '../guardian/guardian_ui.dart';
import '../guardian/family_child_filter.dart';

class NextMatchStrip extends StatelessWidget {
  final AuthController auth;
  final String? playerId;
  const NextMatchStrip({super.key, required this.auth, this.playerId});
  @override
  Widget build(BuildContext context) =>
      BrandScope.maybeOf(context) != null &&
          !BrandScope.maybeOf(context)!.features.contains('matches')
      ? const SizedBox.shrink()
      : RemoteBody(
          auth: auth,
          path:
              '/api/v1/my-matches?upcomingOnly=true${playerId == null ? '' : '&playerId=${Uri.encodeQueryComponent(playerId!)}'}',
          builder: (context, value) {
            final matches =
                rows(value).where((x) => x['isUpcoming'] == true).toList()
                  ..sort(
                    (a, b) => '${a['matchDate']} ${a['startTime']}'.compareTo(
                      '${b['matchDate']} ${b['startTime']}',
                    ),
                  );
            final nearest = matches.take(1).toList();
            if (nearest.isEmpty) return const SizedBox.shrink();
            return Column(
              children: [
                for (final m in nearest)
                  GCard(
                    onTap: () => openPage(
                      context,
                      MatchSchedule(
                        auth: auth,
                        playerId: playerId,
                        matchId: textOf(m['id']),
                      ),
                    ),
                    child: Row(
                      children: [
                        Container(
                          width: 54 * MediaQuery.textScalerOf(context).scale(1),
                          height:
                              72 * MediaQuery.textScalerOf(context).scale(1),
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
                        const Icon(Icons.chevron_right, size: 18),
                      ],
                    ),
                  ),
              ],
            );
          },
        );
}

class MatchSchedule extends StatefulWidget {
  final AuthController auth;
  final String? playerId, matchId;
  const MatchSchedule({
    super.key,
    required this.auth,
    this.playerId,
    this.matchId,
  });
  @override
  State<MatchSchedule> createState() => _MatchScheduleState();
}

class _MatchScheduleState extends State<MatchSchedule> {
  String? selectedChild;
  String? contextKey() => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final identity = contextKey();
  @override
  void initState() {
    super.initState();
    selectedChild = widget.playerId;
  }

  Widget schedule() => RemoteBody(
    key: ValueKey('matches-$selectedChild'),
    auth: widget.auth,
    path:
        '/api/v1/my-matches${selectedChild == null ? '' : '?playerId=${Uri.encodeQueryComponent(selectedChild!)}'}',
    builder: (context, v) => MatchExperience(
      auth: widget.auth,
      items: widget.matchId == null
          ? rows(v)
          : rows(v).where((m) => m['id'] == widget.matchId).toList(),
    ),
  );
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: BrandScope.maybeOf(context)?.sportTheme == 'swimming'
        ? localized(context, 'المنافسات', 'Competitions')
        : localized(context, 'المباريات', 'Matches'),
    actions: [
      NativeMatchEntry(auth: widget.auth),
      PageVideoButton(auth: widget.auth, screen: 'matches'),
    ],
    child: ListenableBuilder(
      listenable: widget.auth,
      builder: (context, _) {
        if (identity == null || identity != contextKey()) {
          return const SizedBox.shrink();
        }
        if (widget.auth.session?.selected?.role != 'Guardian') {
          return schedule();
        }
        return RemoteBody(
          auth: widget.auth,
          path: '/api/v1/guardian/home',
          builder: (context, home) {
            final children = rows(obj(home)['children']);
            return Column(
              children: [
                Padding(
                  padding: const EdgeInsets.fromLTRB(18, 10, 18, 0),
                  child: FamilyChildFilter(
                    children: children,
                    selected: selectedChild ?? '',
                    onSelected: (id) => setState(() {
                      selectedChild = id.isEmpty ? null : id;
                    }),
                  ),
                ),
                Expanded(child: schedule()),
              ],
            );
          },
        );
      },
    ),
  );
}
