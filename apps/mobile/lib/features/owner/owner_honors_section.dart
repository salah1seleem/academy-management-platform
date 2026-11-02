import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart' show ScrollDirection;
import 'package:flutter/scheduler.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/academy_home_skin.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/experience_ui.dart';
import '../shared/vip_rewards.dart';
import 'featured_players.dart';
import 'home_destinations.dart';
import 'member_values.dart';
import 'honor_portrait_card.dart';

class OwnerHonorsSection extends StatefulWidget {
  const OwnerHonorsSection({
    super.key,
    required this.auth,
    required this.coaches,
    required this.branches,
    required this.filters,
    required this.branch,
    required this.asOf,
    this.fallbacks = const {},
  });
  final AuthController auth;
  final List<Json> coaches, branches;
  final Map<String, String> filters;
  final String branch, asOf;
  final Json fallbacks;
  @override
  State<OwnerHonorsSection> createState() => _OwnerHonorsSectionState();
}

class _OwnerHonorsSectionState extends State<OwnerHonorsSection>
    with SingleTickerProviderStateMixin, WidgetsBindingObserver {
  late Future<List<Json>> data;
  final scroll = ScrollController();
  late final Ticker ticker;
  Duration? last;
  Duration resumeAt = Duration.zero;
  bool touching = false, foreground = true;
  int selected = 0;
  double extent = 300;
  String? get identity => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late String? opened = identity;
  @override
  void initState() {
    super.initState();
    data = load();
    ticker = createTicker(tick);
    WidgetsBinding.instance.addObserver(this);
    widget.auth.addListener(authChanged);
  }

  void authChanged() {
    if (opened != identity && mounted) {
      setState(() {
        opened = identity;
        data = load();
      });
    }
  }

  Future<List<Json>> load() async {
    final expected = identity;
    final query = Uri(
      queryParameters: {
        if (widget.branch.isNotEmpty) 'branchId': widget.branch,
        if (widget.asOf.isNotEmpty) 'asOf': widget.asOf,
      },
    ).query;
    final result = await Future.wait([
      for (final kind in ['featured-players', 'vip-members'])
        widget.auth
            .request('GET', '/api/v1/reports/$kind?$query')
            .then((v) => obj(v))
            .catchError(
              (Object _) => <String, dynamic>{
                'items': <Json>[],
                'failed': true,
              },
            ),
    ]);
    if (expected != identity) throw StateError('context');
    return result;
  }

  @override
  void didUpdateWidget(OwnerHonorsSection old) {
    super.didUpdateWidget(old);
    if (old.branch != widget.branch ||
        old.asOf != widget.asOf ||
        old.auth != widget.auth) {
      data = load();
    }
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    // Recognition cards stay in place until the user selects or swipes.
    if (ticker.isActive) ticker.stop();
    last = null;
  }

  void tick(Duration elapsed) {
    final delta = last == null
        ? 0.0
        : (elapsed - last!).inMicroseconds / 1000000;
    last = elapsed;
    if (!foreground ||
        touching ||
        elapsed < resumeAt ||
        !scroll.hasClients ||
        ModalRoute.of(context)?.isCurrent == false) {
      return;
    }
    final cycle = extent * 3, next = scroll.offset - delta.clamp(0, .1) * 11;
    scroll.jumpTo(next <= 0 ? next + cycle : next);
    final visible = (scroll.offset / extent).round() % 3;
    if (visible != selected) setState(() => selected = visible);
  }

  void pause() =>
      resumeAt = (last ?? Duration.zero) + const Duration(seconds: 4);
  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    foreground = state == AppLifecycleState.resumed;
    last = null;
  }

  @override
  void dispose() {
    widget.auth.removeListener(authChanged);
    WidgetsBinding.instance.removeObserver(this);
    ticker.dispose();
    scroll.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (opened == null || opened != identity) return const SizedBox.shrink();
    final coaches = [...widget.coaches]
      ..sort((a, b) {
        final aRating = a['parentRating'] as num?,
            bRating = b['parentRating'] as num?;
        final order = bRating == null
            ? (aRating == null ? 0 : -1)
            : aRating == null
            ? 1
            : bRating.compareTo(aRating);
        return order != 0
            ? order
            : textOf(a['name']).compareTo(textOf(b['name']));
      });
    final reduced =
        MediaQuery.disableAnimationsOf(context) ||
        MediaQuery.accessibleNavigationOf(context);
    final height =
        450.0 +
        (MediaQuery.textScalerOf(context).scale(12) - 12)
                .clamp(0, 24)
                .toDouble() *
            14;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const ExperienceSection(
          'نجوم الأكاديمية',
          Icons.workspace_premium_outlined,
        ),
        LayoutBuilder(
          builder: (context, bounds) {
            extent = bounds.maxWidth * .86;
            return FutureBuilder<List<Json>>(
              future: data,
              builder: (context, snapshot) {
                if (!snapshot.hasData) {
                  return SizedBox(
                    height: height,
                    child: snapshot.hasError
                        ? const EmptyMessage('تعذر تحميل نجوم الأكاديمية.')
                        : const Center(child: CircularProgressIndicator()),
                  );
                }
                final players = rows(snapshot.data![0]['items']),
                    members = rows(snapshot.data![1]['items']);
                final lists = [players, members, coaches];
                final failed = [
                  snapshot.data![0]['failed'] == true,
                  snapshot.data![1]['failed'] == true,
                  false,
                ];
                const labels = ['أبرز اللاعبين', 'أعضاء مميزون', 'المدربون'];
                void open(int index) {
                  pause();
                  openPage(
                    context,
                    index == 0
                        ? FeaturedPlayers(
                            auth: widget.auth,
                            branch: widget.branch,
                            asOf: widget.asOf,
                            all: true,
                          )
                        : index == 1
                        ? OwnerVipRanking(
                            auth: widget.auth,
                            branch: widget.branch,
                            asOf: widget.asOf,
                          )
                        : HomeCoaches(
                            auth: widget.auth,
                            items: widget.coaches,
                            branches: widget.branches,
                            filters: widget.filters,
                            all: true,
                          ),
                  );
                }

                return Column(
                  children: [
                    Wrap(
                      spacing: 6,
                      children: [
                        for (var i = 0; i < 3; i++)
                          ChoiceChip(
                            selectedColor: academyAccent(context),
                            label: UiText(
                              labels[i],
                              style: TextStyle(
                                color: selected == i
                                    ? Theme.of(context).colorScheme.onPrimary
                                    : Theme.of(context).colorScheme.onSurface,
                              ),
                            ),
                            selected: selected == i,
                            onSelected: (_) {
                              pause();
                              setState(() => selected = i);
                              if (reduced) {
                                scroll.jumpTo(i * extent);
                              } else {
                                scroll.animateTo(
                                  i * extent,
                                  duration: const Duration(milliseconds: 300),
                                  curve: Curves.easeOut,
                                );
                              }
                            },
                          ),
                      ],
                    ),
                    const SizedBox(height: 10),
                    Listener(
                      onPointerDown: (_) {
                        touching = true;
                        pause();
                      },
                      onPointerUp: (_) {
                        touching = false;
                        pause();
                      },
                      onPointerCancel: (_) {
                        touching = false;
                        pause();
                      },
                      child: NotificationListener<ScrollNotification>(
                        onNotification: (n) {
                          if (n is ScrollEndNotification && scroll.hasClients) {
                            final index = (scroll.offset / extent).round() % 3;
                            if (index != selected) {
                              setState(() => selected = index);
                            }
                          }
                          if (n is UserScrollNotification &&
                              n.direction != ScrollDirection.idle) {
                            pause();
                          }
                          return false;
                        },
                        child: SizedBox(
                          height: height,
                          child: ListView.builder(
                            key: const ValueKey('owner-honors-rail'),
                            controller: scroll,
                            scrollDirection: Axis.horizontal,
                            itemExtent: extent,
                            itemCount: 3,
                            itemBuilder: (context, index) {
                              final kind = index % 3,
                                  item = lists[kind].firstOrNull;
                              if (item == null) {
                                return Padding(
                                  padding: const EdgeInsetsDirectional.only(
                                    end: 12,
                                  ),
                                  child: GCard(
                                    child: Column(
                                      mainAxisAlignment:
                                          MainAxisAlignment.center,
                                      children: [
                                        UiText(labels[kind]),
                                        const SizedBox(height: 12),
                                        UiText(
                                          failed[kind]
                                              ? 'تعذر تحميل هذه القائمة.'
                                              : 'لا توجد نتائج في النطاق المختار.',
                                        ),
                                        TextButton(
                                          onPressed: () {
                                            if (failed[kind]) {
                                              setState(() => data = load());
                                            } else {
                                              open(kind);
                                            }
                                          },
                                          child: UiText(
                                            failed[kind]
                                                ? 'إعادة المحاولة'
                                                : 'عرض القائمة',
                                          ),
                                        ),
                                      ],
                                    ),
                                  ),
                                );
                              }
                              return Padding(
                                padding: const EdgeInsetsDirectional.only(
                                  end: 12,
                                ),
                                child: HonorPortraitCard(
                                  auth: widget.auth,
                                  category: labels[kind],
                                  categoryIcon: [
                                    Icons.emoji_events_outlined,
                                    Icons.workspace_premium_outlined,
                                    Icons.sports_outlined,
                                  ][kind],
                                  photoHeight: 240,
                                  name: textOf(
                                    item[kind == 0
                                        ? 'name'
                                        : kind == 1
                                        ? 'displayName'
                                        : 'name'],
                                  ),
                                  reference: item['photoReference'],
                                  gender: item['gender'],
                                  identity: textOf(
                                    item['playerId'] ?? item['id'],
                                  ),
                                  avatarRole: [
                                    'player',
                                    'member',
                                    'coach',
                                  ][kind],
                                  fallbackAsset: honorFallback(
                                    ['player', 'member', 'coach'][kind],
                                    widget.fallbacks[[
                                      'player',
                                      'member',
                                      'coach',
                                    ][kind]],
                                  ),
                                  height: height,
                                  onTap: () => open(kind),
                                  details: kind == 2
                                      ? coachDetails(item, widget.branches)
                                      : kind == 0
                                      ? [
                                          (
                                            'التقييم ${item['score']}',
                                            const Color(0xFF236D46),
                                          ),
                                          (
                                            '${item['stars']} نجوم',
                                            const Color(0xFF9A5719),
                                          ),
                                          (
                                            textOf(item['branch']),
                                            const Color(0xFF6941A5),
                                          ),
                                        ]
                                      : [
                                          (
                                            money(
                                              item['monthlySubscriptionValue'],
                                              item['currency'],
                                            ),
                                            const Color(0xFF9A5719),
                                          ),
                                          (
                                            'قيمة الاشتراكات الشهرية',
                                            const Color(0xFF126C81),
                                          ),
                                        ],
                                ),
                              );
                            },
                          ),
                        ),
                      ),
                    ),
                  ],
                );
              },
            );
          },
        ),
      ],
    );
  }
}

class OwnerVipRanking extends StatelessWidget {
  const OwnerVipRanking({
    super.key,
    required this.auth,
    required this.branch,
    required this.asOf,
  });
  final AuthController auth;
  final String branch, asOf;
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'الأعضاء المميزون',
    child: RemoteBody(
      auth: auth,
      path:
          '/api/v1/reports/vip-members?${Uri(queryParameters: {if (branch.isNotEmpty) 'branchId': branch, if (asOf.isNotEmpty) 'asOf': asOf}).query}',
      builder: (context, value) => gList([
        OutlinedButton.icon(
          onPressed: () =>
              openPage(context, NativeVipRewards(auth: auth, owner: true)),
          icon: const Icon(Icons.card_giftcard),
          label: const UiText('إدارة مكافآت الأعضاء المميزين'),
        ),
        const SizedBox(height: 16),
        for (final item in rows(obj(value)['items']))
          Padding(
            padding: const EdgeInsets.only(bottom: 12),
            child: HonorPortraitCard(
              compact: true,
              auth: auth,
              name: textOf(item['displayName']),
              reference: item['photoReference'],
              gender: item['gender'],
              identity: textOf(item['id']),
              avatarRole: 'guardian',
              height:
                  370 +
                  (MediaQuery.textScalerOf(context).scale(12) - 12).clamp(
                        0,
                        24,
                      ) *
                      10,
              details: [
                (
                  money(item['monthlySubscriptionValue'], item['currency']),
                  const Color(0xFF9A5719),
                ),
              ],
              onTap: () => openPage(
                context,
                MemberGuardianDetail(auth: auth, id: textOf(item['id'])),
              ),
            ),
          ),
        if (rows(obj(value)['items']).isEmpty)
          const EmptyMessage('لا توجد نتائج في النطاق المختار.'),
      ]),
    ),
  );
}
