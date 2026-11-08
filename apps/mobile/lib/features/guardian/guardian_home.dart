import '../shared/modern_home.dart';
import '../shared/experience_ui.dart';

import 'dart:convert';

import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';

import '../../core/widgets/gentle_carousel.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/academy_brand.dart';
import 'guardian_ui.dart';
import 'guardian_profile.dart';
import 'guardian_child_actions.dart';
import 'coach_feedback.dart';
import 'guardian_payments.dart';
import 'guardian_report.dart';
import 'daily_nutrition.dart';
import 'family_nutrition_rail.dart';
import 'family_media.dart';
import 'reward_banner.dart';
import 'enrollment_requests.dart';
import '../shared/academy_news.dart';
import '../shared/community_ads.dart';
import '../shared/notification_inbox.dart';
import 'sport_store.dart';
import '../shared/matches.dart';
import '../shared/receivable_balances.dart';
import '../shared/private_media.dart';
import 'guardian_content.dart';
import '../owner/owner_action_rail.dart';

class GuardianHome extends StatefulWidget {
  final AuthController auth;
  final String environment;
  final Widget accountPage;
  const GuardianHome({
    super.key,
    required this.auth,
    required this.environment,
    required this.accountPage,
  });
  @override
  State<GuardianHome> createState() => _GuardianHomeState();
}

class _GuardianHomeState extends State<GuardianHome>
    with WidgetsBindingObserver {
  String? selectedPlayer, identity;
  int section = 0, homeRevision = 0;
  bool detailsExpanded = false;
  final selectedEnrollments = <String, String>{};

  Json workspaceChild(Json child) {
    final sports = rows(child['sports']);
    final sport =
        sports
            .where((s) => s['id'] == selectedEnrollments[textOf(child['id'])])
            .firstOrNull ??
        sports.firstOrNull;
    final performance = sport?['performance'] is Map
        ? obj(sport!['performance'])
        : null;
    final legacy = child['latestEvaluation'];
    final latest = sport?.containsKey('performance') == true
        ? performance == null || performance['evaluationId'] == null
              ? null
              : <String, dynamic>{
                  'id': performance['evaluationId'],
                  'sport': sport?['sport'],
                  'evaluationDate': performance['evaluationDate'],
                  'overallScore': performance['meanPerformance'],
                }
        : legacy is Map && legacy['sport'] == sport?['sport']
        ? legacy
        : null;
    return {
      ...child,
      'sports': <Json>[?sport],
      'latestEvaluation': latest,
      'power': null,
    };
  }

  String? get currentIdentity =>
      widget.auth.session?.selected?.role != 'Guardian'
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  @override
  void initState() {
    super.initState();
    identity = currentIdentity;
    WidgetsBinding.instance.addObserver(this);
    widget.auth.addListener(contextChanged);
    notificationChanges(widget.auth).addListener(noticesChanged);
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) {
      noticesChanged();
    }
  }

  void noticesChanged() {
    if (mounted && identity != null && identity == currentIdentity) {
      setState(() => homeRevision++);
    }
  }

  void contextChanged() {
    if (!mounted || identity == currentIdentity) return;
    setState(() {
      identity = currentIdentity;
      selectedPlayer = null;
      detailsExpanded = false;
      selectedEnrollments.clear();
    });
  }

  @override
  void didUpdateWidget(GuardianHome old) {
    super.didUpdateWidget(old);
    if (old.auth != widget.auth) {
      old.auth.removeListener(contextChanged);
      notificationChanges(old.auth).removeListener(noticesChanged);
      widget.auth.addListener(contextChanged);
      notificationChanges(widget.auth).addListener(noticesChanged);
      identity = currentIdentity;
      selectedPlayer = null;
      detailsExpanded = false;
      selectedEnrollments.clear();
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    widget.auth.removeListener(contextChanged);
    notificationChanges(widget.auth).removeListener(noticesChanged);
    super.dispose();
  }

  void open(Widget page) => openPage(context, page);
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'الرئيسية', 'Home'),
    showAppBar: false,
    actions: [
      IconButton(
        tooltip: localized(context, 'الحساب', 'Account'),
        onPressed: () => open(widget.accountPage),
        icon: const Icon(Icons.person_outline),
      ),
    ],
    child: currentIdentity == null
        ? const SizedBox.shrink()
        : RemoteBody(
            key: ValueKey('$identity:$homeRevision'),
            auth: widget.auth,
            path: '/api/v1/guardian/home',
            builder: (context, value) {
              final home = obj(value),
                  allChildren = rows(home['children'])
                    ..sort(
                      (a, b) =>
                          textOf(a['dateOfBirth'])
                              .compareTo(textOf(b['dateOfBirth'])),
                    );
              final selected = allChildren.any((c) => c['id'] == selectedPlayer)
                  ? selectedPlayer
                  : allChildren.isEmpty
                  ? null
                  : textOf(allChildren.first['id']);
              final children = allChildren
                  .where((c) => c['id'] == selected)
                  .map(workspaceChild)
                  .toList();
              final competitions =
                  BrandScope.maybeOf(context)?.features.contains('matches') ??
                  true;
              final effectiveSection = section == 4 && !competitions
                  ? 0
                  : section;
              final nutritionToday = textOf(
                home['trainingToday'] ?? home['today'],
              );
              return gList([
                GuardianUrgentMatchAlerts(
                  auth: widget.auth,
                  alerts: rows(home['urgentAlerts'] ?? <Json>[]),
                  onReturned: () {
                    if (mounted) setState(() => homeRevision++);
                  },
                ),
                GuardianRewardBanner(
                  key: ValueKey('reward-$identity'),
                  auth: widget.auth,
                ),
                const RoleHomeIntro(
                  title: 'رحلة أبنائك',
                  subtitle: 'تقدمهم ومواعيدهم، وأنت بالقرب منهم',
                ),
                if (allChildren.length > 1)
                  SingleChildScrollView(
                    scrollDirection: Axis.horizontal,
                    child: Row(
                      children: [
                        for (final child in allChildren)
                          Padding(
                            padding: const EdgeInsetsDirectional.only(end: 8),
                            child: ChoiceChip(
                              key: ValueKey('child-tab-${child['id']}'),
                              label: UiText(
                                textOf(child['arabicName']),
                                style: const TextStyle(fontSize: 12),
                              ),
                              selected: child['id'] == selected,
                              showCheckmark: false,
                              onSelected: (_) => setState(
                                () => selectedPlayer = textOf(child['id']),
                              ),
                            ),
                          ),
                      ],
                    ),
                  ),
                if (allChildren.isNotEmpty)
                  GuardianChildCarousel(
                    key: ValueKey('carousel-$identity'),
                    auth: widget.auth,
                    children: allChildren,
                    selectedPlayerId: selected,
                    selectedEnrollments: selectedEnrollments,
                    onEnrollmentSelected: (player, enrollment) => setState(() {
                      selectedPlayer = player;
                      selectedEnrollments[player] = enrollment;
                    }),
                    today: textOf(home['today']),
                    nutritionDate: nutritionToday,
                    onSelected: (id) => setState(() => selectedPlayer = id),
                    onOpenEnrollment: (id, enrollmentId) => open(
                      ChildProfile(
                        auth: widget.auth,
                        environment: widget.environment,
                        playerId: id,
                        today: home['today'],
                        nutritionDate: nutritionToday,
                        initialEnrollmentId: enrollmentId,
                      ),
                    ),
                    onOpen: (id) => open(
                      ChildProfile(
                        auth: widget.auth,
                        environment: widget.environment,
                        playerId: id,
                        today: home['today'],
                        nutritionDate: nutritionToday,
                      ),
                    ),
                  ),
                if (allChildren.isEmpty)
                  const EmptyMessage('لا يوجد أبناء مرتبطون بهذا الحساب.'),
                OwnerActionRail(
                  key: ValueKey('guardian-home-actions-$identity'),
                  automatic: false,
                  actions: [
                    OwnerRailAction(
                      localized(context, 'اشتراك جديد', 'New enrollment'),
                      Icons.person_add_alt,
                      const Color(0xFFCA2449),
                      () => open(
                        EnrollmentForm(
                          auth: widget.auth,
                          environment: widget.environment,
                        ),
                      ),
                      key: const ValueKey('guardian-action-اشتراك جديد'),
                    ),
                    OwnerRailAction(
                      localized(context, 'تجديد الاشتراك', 'Renew'),
                      Icons.autorenew,
                      const Color(0xFF159B82),
                      () => open(
                        RenewalPicker(
                          auth: widget.auth,
                          environment: widget.environment,
                        ),
                      ),
                      key: const ValueKey('guardian-action-تجديد الاشتراك'),
                    ),
                    OwnerRailAction(
                      localized(context, 'تجديد لغيره', 'Renew for someone'),
                      Icons.card_giftcard,
                      const Color(0xFF6755C7),
                      () => open(
                        ExternalRenewal(
                          auth: widget.auth,
                          environment: widget.environment,
                        ),
                      ),
                      key: const ValueKey('guardian-action-تجديد اشتراك لغيره'),
                    ),
                    OwnerRailAction(
                      'تقييم المدربين',
                      Icons.star_outline_rounded,
                      const Color(0xFFE5B54D),
                      () => open(
                        GuardianFeedbackChildren(
                          auth: widget.auth,
                          children: allChildren,
                        ),
                      ),
                      key: const ValueKey('guardian-action-تقييم المدربين'),
                    ),
                    OwnerRailAction(
                      'شكوى أو اقتراح',
                      Icons.chat_bubble_outline_rounded,
                      const Color(0xFF5076B8),
                      () => open(
                        GuardianFeedbackChildren(
                          auth: widget.auth,
                          children: allChildren,
                          complaint: true,
                        ),
                      ),
                      key: const ValueKey('guardian-action-شكوى أو اقتراح'),
                    ),
                  ],
                ),
                const SizedBox(height: 14),
                if (children.isNotEmpty)
                  GCard(
                    padding: EdgeInsets.zero,
                    child: ExpansionTile(
                      key: const ValueKey('child-home-details'),
                      initiallyExpanded: detailsExpanded,
                      onExpansionChanged: (value) =>
                          setState(() => detailsExpanded = value),
                      shape: const Border(),
                      collapsedShape: const Border(),
                      title: UiText(
                        'تفاصيل ${textOf(children.first['arabicName']).split(' ').first}',
                      ),
                      leading: const Icon(Icons.manage_accounts_outlined),
                      childrenPadding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
                      children: [
                        SingleChildScrollView(
                          scrollDirection: Axis.horizontal,
                          child: Row(
                            children: [
                              for (final item in [
                                (0, 'الاشتراك', Icons.card_membership_rounded),
                                (
                                  1,
                                  'الجدول والحصص',
                                  Icons.calendar_month_rounded,
                                ),
                                (2, 'التقييمات', Icons.insights_rounded),
                                (3, 'التغذية والصحة', Icons.restaurant_rounded),
                                if (competitions)
                                  (4, 'المنافسات', Icons.emoji_events_rounded),
                              ])
                                Padding(
                                  padding: const EdgeInsetsDirectional.only(
                                    end: 8,
                                  ),
                                  child: _ChildFeatureTab(
                                    key: ValueKey('child-section-${item.$1}'),
                                    label: item.$2,
                                    icon: item.$3,
                                    selected: effectiveSection == item.$1,
                                    onTap: () =>
                                        setState(() => section = item.$1),
                                  ),
                                ),
                            ],
                          ),
                        ),
                        const SizedBox(height: 14),
                        for (final child in children)
                          KeyedSubtree(
                            key: ValueKey('child-workspace-${child['id']}'),
                            child: Column(
                              children: [
                                if (effectiveSection == 1)
                                  for (final sport in rows(child['sports']))
                                    if (sport['nextSession'] is Map)
                                      NextSessionCard(
                                        childName: textOf(child['arabicName']),
                                        sport: sport,
                                        onTap: () => open(
                                          ProfileRecords(
                                            auth: widget.auth,
                                            playerId: child['id'],
                                            section: 'schedule',
                                            enrollmentId: textOf(sport['id']),
                                          ),
                                        ),
                                      ),
                                if (effectiveSection == 1 &&
                                    !rows(child['sports'])
                                        .any((s) => s['nextSession'] is Map))
                                  Padding(
                                    padding: const EdgeInsets.only(bottom: 14),
                                    child: UiText(
                                      localized(
                                        context,
                                        'لا يوجد موعد تدريب قادم منشور لهذا الطفل.',
                                        'No upcoming training session published for this child.',
                                      ),
                                      style: const TextStyle(fontSize: 14),
                                    ),
                                  ),
                                if (effectiveSection == 0)
                                  GuardianSubscriptionSummary(
                                    auth: widget.auth,
                                    environment: widget.environment,
                                    childData: child,
                                  ),
                                if (effectiveSection == 2)
                                  GuardianLatestProgress(
                                    auth: widget.auth,
                                    childData: child,
                                  ),
                                if (effectiveSection == 4)
                                  NextMatchStrip(
                                    auth: widget.auth,
                                    playerId: textOf(child['id']),
                                  ),
                                if (effectiveSection == 4)
                                  GLink(
                                    'مباريات ${child['arabicName']}',
                                    Icons.emoji_events_outlined,
                                    () => open(
                                      MatchSchedule(
                                        auth: widget.auth,
                                        playerId: textOf(child['id']),
                                      ),
                                    ),
                                  ),
                                if (effectiveSection == 3)
                                  DailyNutritionCard(
                                    key: ValueKey(
                                      'home-nutrition-${child['id']}',
                                    ),
                                    auth: widget.auth,
                                    playerId: child['id'],
                                    playerName: child['arabicName'],
                                    today: nutritionToday,
                                  ),
                                const SizedBox(height: 18),
                              ],
                            ),
                          ),
                      ],
                    ),
                  ),
                Heading(
                  localized(
                    context,
                    'أقرب المواعيد لأبنائي',
                    'Upcoming for my children',
                  ),
                ),
                for (final child in allChildren) ...[
                  Padding(
                    padding: const EdgeInsets.only(bottom: 8),
                    child: UiText(
                      textOf(child['arabicName']),
                      style: const TextStyle(fontWeight: FontWeight.w800),
                    ),
                  ),
                  for (final sport in rows(child['sports']))
                    if (sport['nextSession'] is Map)
                      NextSessionCard(
                        childName: textOf(child['arabicName']),
                        sport: sport,
                        onTap: () => open(
                          ProfileRecords(
                            auth: widget.auth,
                            playerId: child['id'],
                            section: 'schedule',
                            enrollmentId: textOf(sport['id']),
                          ),
                        ),
                      ),
                  if (competitions)
                    NextMatchStrip(
                      key: ValueKey('family-match-${child['id']}'),
                      auth: widget.auth,
                      playerId: textOf(child['id']),
                    ),
                ],
                const SizedBox(height: 12),
                Heading(
                  localized(
                    context,
                    'النظام الغذائي اليومي للأبناء',
                    'Daily family nutrition',
                  ),
                ),
                FamilyNutritionRail(
                  auth: widget.auth,
                  children: allChildren,
                  today: nutritionToday,
                ),
                GuardianSponsoredCard(auth: widget.auth),
                NewsStrip(auth: widget.auth),
                Heading(
                  localized(context, 'منتجات الرياضة', 'Sports products'),
                ),
                CatalogSections(auth: widget.auth),
                if (selected != null)
                  FamilyTrainingMedia(
                    key: ValueKey('home-media-$identity-$homeRevision'),
                    auth: widget.auth,
                    children: allChildren,
                  ),
                GLink(
                  localized(context, 'طلبات الاشتراك', 'Enrollment requests'),
                  Icons.inbox_outlined,
                  () => open(
                    EnrollmentRequests(
                      auth: widget.auth,
                      environment: widget.environment,
                    ),
                  ),
                ),
                GLink(
                  localized(context, 'إيصالاتي', 'My receipts'),
                  Icons.receipt_long_outlined,
                  () => open(ReceiptsPage(auth: widget.auth)),
                ),
                GLink(
                  localized(context, 'أرصدة الأسرة', 'Family balances'),
                  Icons.account_balance_wallet_outlined,
                  () => open(ReceivableBalancesPage(auth: widget.auth)),
                ),
              ], key: const ValueKey('guardian-home-scroll'));
            },
          ),
  );
}

class GuardianHomeMedia extends StatelessWidget {
  const GuardianHomeMedia({
    super.key,
    required this.auth,
    required this.playerId,
  });
  final AuthController auth;
  final String playerId;
  @override
  Widget build(BuildContext context) => RemoteBody(
    auth: auth,
    path:
        '/api/v1/assets/gallery?playerId=${Uri.encodeQueryComponent(playerId)}',
    builder: (context, value) {
      final media = rows(value).take(4).toList();
      if (media.isEmpty) return const SizedBox.shrink();
      return Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const Heading('صور وفيديوهات التمرين'),
          for (final item in media) MediaTile(auth: auth, media: item),
          TextButton(
            onPressed: () =>
                openPage(context, GalleryPage(auth: auth, playerId: playerId)),
            child: const UiText('عرض كل الصور والفيديوهات'),
          ),
        ],
      );
    },
  );
}

class GuardianSubscriptionSummary extends StatelessWidget {
  final AuthController auth;
  final String environment;
  final Json childData;
  const GuardianSubscriptionSummary({
    super.key,
    required this.auth,
    required this.environment,
    required this.childData,
  });
  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      for (final sport in rows(childData['sports']))
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              UiText(
                localized(
                  context,
                  'الاشتراك · ${sport['sport']}',
                  'Subscription · ${sport['sport']}',
                ),
                style: const TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.w700,
                ),
              ),
              const SizedBox(height: 8),
              if (sport['period'] is Map) ...[
                StatusPill(sport['period']['status'], fontSize: 14),
                const SizedBox(height: 6),
                if (sport['period']['plan'] != null)
                  UiText(
                    textOf(sport['period']['plan']),
                    style: const TextStyle(fontSize: 14),
                  ),
                UiText(
                  'بداية الاشتراك ${day(sport['period']['startDate'])} · نهاية الاشتراك ${day(sport['period']['endDate'])}',
                  style: const TextStyle(fontSize: 14),
                  textDirection: TextDirection.ltr,
                ),
              ] else
                UiText(
                  localized(
                    context,
                    'لا يوجد اشتراك فعال',
                    'No active subscription',
                  ),
                  style: const TextStyle(fontSize: 14),
                ),
              if (sport['id'] != null &&
                  (sport['period'] == null ||
                      [
                        'Expired',
                        'Expiring',
                      ].contains(sport['period']['status']))) ...[
                const SizedBox(height: 12),
                FilledButton.icon(
                  key: ValueKey('home-renew-${sport['id']}'),
                  onPressed: () => openPage(
                    context,
                    PlanPicker(
                      auth: auth,
                      environment: environment,
                      enrollmentId: textOf(sport['id']),
                      childName: textOf(childData['arabicName']),
                    ),
                  ),
                  icon: const Icon(Icons.autorenew),
                  label: UiText(
                    localized(
                      context,
                      'تجديد · ${childData['arabicName']}',
                      'Renew · ${childData['arabicName']}',
                    ),
                  ),
                ),
              ],
            ],
          ),
        ),
    ],
  );
}

class GuardianLatestProgress extends StatelessWidget {
  final AuthController auth;
  final Json childData;
  const GuardianLatestProgress({
    super.key,
    required this.auth,
    required this.childData,
  });
  @override
  Widget build(BuildContext context) {
    final power = childData['power'];
    final latest =
        childData['latestEvaluation'] ??
        (power is Map && power['evaluationId'] != null
            ? {
                'id': power['evaluationId'],
                'overallScore': power['overallScore'],
              }
            : null);
    return GCard(
      onTap: latest is Map
          ? () => openPage(
              context,
              PlayerReport(auth: auth, id: textOf(latest['id'])),
            )
          : () => openPage(
              context,
              ReportHistory(auth: auth, playerId: textOf(childData['id'])),
            ),
      child: Row(
        children: [
          Icon(
            Icons.insights_outlined,
            color: Theme.of(context).colorScheme.primary,
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                UiText(
                  localized(
                    context,
                    'تطور ${childData['arabicName']}',
                    '${childData['arabicName']}’s progress',
                  ),
                  style: const TextStyle(
                    fontSize: 16,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                UiText(
                  latest is Map
                      ? latest['evaluationDate'] == null
                            ? localized(
                                context,
                                'عرض آخر تقييم منشور',
                                'Open latest published report',
                              )
                            : '${latest['sport']} · ${day(latest['evaluationDate'])}'
                      : localized(
                          context,
                          'لا يوجد تقييم منشور بعد',
                          'No published report yet',
                        ),
                  style: const TextStyle(fontSize: 14),
                ),
              ],
            ),
          ),
          if (latest is Map && latest['overallScore'] is num)
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 8),
              child: UiText(
                '${(latest['overallScore'] as num).round()}',
                style: TextStyle(
                  fontSize: 24,
                  fontWeight: FontWeight.w800,
                  color: Theme.of(context).colorScheme.primary,
                ),
              ),
            ),
          const Icon(Icons.chevron_right),
        ],
      ),
    );
  }
}

class _ChildFeatureTab extends StatelessWidget {
  const _ChildFeatureTab({
    super.key,
    required this.label,
    required this.icon,
    required this.selected,
    required this.onTap,
  });
  final String label;
  final IconData icon;
  final bool selected;
  final VoidCallback onTap;
  @override
  Widget build(BuildContext context) {
    final color = Theme.of(context).colorScheme.primary;
    return Semantics(
      button: true,
      selected: selected,
      label: label,
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(14),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 8),
          child: Column(
            children: [
              Container(
                width: 38,
                height: 38,
                decoration: BoxDecoration(
                  borderRadius: BorderRadius.circular(12),
                  color: selected
                      ? color.withValues(alpha: .13)
                      : Theme.of(context).colorScheme.surfaceContainerHighest,
                ),
                child: Icon(
                  icon,
                  size: 21,
                  color: selected
                      ? color
                      : Theme.of(context).colorScheme.onSurfaceVariant,
                ),
              ),
              const SizedBox(height: 6),
              UiText(
                label,
                style: TextStyle(
                  fontSize: 11,
                  fontWeight: selected ? FontWeight.w700 : FontWeight.w500,
                  color: selected ? color : null,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class GuardianChildCarousel extends StatefulWidget {
  const GuardianChildCarousel({
    super.key,
    required this.auth,
    required this.children,
    this.selectedPlayerId,
    required this.today,
    this.nutritionDate,
    required this.onSelected,
    required this.onOpen,
    this.onOpenEnrollment,
    this.selectedEnrollments = const {},
    this.onEnrollmentSelected,
  });
  final Map<String, String> selectedEnrollments;
  final void Function(String playerId, String enrollmentId)?
  onEnrollmentSelected;
  final AuthController auth;
  final List<Json> children;
  final String? selectedPlayerId;
  final String today;
  final String? nutritionDate;
  final ValueChanged<String> onSelected, onOpen;
  final void Function(String playerId, String? enrollmentId)? onOpenEnrollment;
  @override
  State<GuardianChildCarousel> createState() => _GuardianChildCarouselState();
}

class _GuardianChildCarouselState extends State<GuardianChildCarousel> {
  late final controller = PageController(
    viewportFraction: .94,
    keepPage: false,
    initialPage:
        widget.children.indexWhere((c) => c['id'] == widget.selectedPlayerId) <
            0
        ? 0
        : widget.children.indexWhere((c) => c['id'] == widget.selectedPlayerId),
  );
  final heights = <int, double>{};
  String? layoutSignature;
  double get uniformHeight =>
      heights.values.fold<double>(450, (a, b) => a > b ? a : b);
  @override
  void didUpdateWidget(GuardianChildCarousel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.selectedPlayerId == widget.selectedPlayerId) return;
    final index = widget.children.indexWhere(
      (c) => c['id'] == widget.selectedPlayerId,
    );
    if (index < 0) return;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted &&
          controller.hasClients &&
          controller.page?.round() != index) {
        controller.jumpToPage(index);
      }
    });
  }

  @override
  void dispose() {
    controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final signature = jsonEncode([
      MediaQuery.sizeOf(context).width,
      MediaQuery.textScalerOf(context).scale(1),
      widget.children,
      widget.selectedEnrollments,
    ]);
    if (signature != layoutSignature) {
      layoutSignature = signature;
      heights.clear();
    }
    return SizedBox(
      height: uniformHeight,
      child: PageView.builder(
        controller: controller,
        padEnds: false,
        itemCount: widget.children.length,
        onPageChanged: (i) =>
            widget.onSelected(textOf(widget.children[i]['id'])),
        itemBuilder: (context, i) {
          final c = widget.children[i];
          return Padding(
            padding: const EdgeInsetsDirectional.only(end: 12),
            child: Semantics(
              label: 'الابن ${i + 1} من ${widget.children.length}',
              child: GestureDetector(
                key: ValueKey('select-child-${c['id']}'),
                onTap: () => widget.onSelected(textOf(c['id'])),
                child: OverflowBox(
                  alignment: Alignment.topCenter,
                  minHeight: 0,
                  maxHeight: double.infinity,
                  child: _ChildCardMeasure(
                    onSize: (size) {
                      if (mounted &&
                          ((heights[i] ?? 0) - size.height).abs() > .5) {
                        setState(() {
                          heights[i] = size.height;
                        });
                      }
                    },
                    child: PremiumChildCard(
                      key: ValueKey('large-child-${c['id']}'),
                      cardHeight: heights.containsKey(i) ? uniformHeight : null,
                      auth: widget.auth,
                      childData: c,
                      today: widget.today,
                      nutritionDate: widget.nutritionDate,
                      selectedEnrollmentId:
                          widget.selectedEnrollments[textOf(c['id'])],
                      onEnrollmentChanged: widget.onEnrollmentSelected == null
                          ? null
                          : (id) => widget.onEnrollmentSelected!(
                              textOf(c['id']),
                              id,
                            ),
                      onTap: () => widget.onOpen(textOf(c['id'])),
                      onOpenEnrollment: widget.onOpenEnrollment == null
                          ? null
                          : (id) =>
                                widget.onOpenEnrollment!(textOf(c['id']), id),
                    ),
                  ),
                ),
              ),
            ),
          );
        },
      ),
    );
  }
}

class _ChildCardMeasure extends SingleChildRenderObjectWidget {
  const _ChildCardMeasure({required this.onSize, required super.child});
  final ValueChanged<Size> onSize;
  @override
  RenderObject createRenderObject(BuildContext context) =>
      _ChildCardMeasurement(onSize);
  @override
  void updateRenderObject(
    BuildContext context,
    covariant _ChildCardMeasurement renderObject,
  ) {
    renderObject.onSize = onSize;
  }
}

class _ChildCardMeasurement extends RenderProxyBox {
  _ChildCardMeasurement(this.onSize);
  ValueChanged<Size> onSize;
  Size? previous;
  @override
  void performLayout() {
    super.performLayout();
    if (previous == size) return;
    previous = size;
    final measured = size;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      onSize(measured);
    });
  }
}

class PremiumChildCard extends StatefulWidget {
  final bool showSubscription;
  final double? cardHeight;
  final AuthController auth;
  final Json childData;
  final String today;
  final String? nutritionDate;
  final VoidCallback onTap;
  final ValueChanged<String?>? onOpenEnrollment;
  final String? selectedEnrollmentId;
  final ValueChanged<String>? onEnrollmentChanged;
  const PremiumChildCard({
    super.key,
    required this.auth,
    required this.childData,
    required this.today,
    this.nutritionDate,
    required this.onTap,
    this.showSubscription = true,
    this.cardHeight,
    this.onOpenEnrollment,
    this.selectedEnrollmentId,
    this.onEnrollmentChanged,
  });
  @override
  State<PremiumChildCard> createState() => _PremiumChildCardState();
}

class _PremiumChildCardState extends State<PremiumChildCard> {
  String? enrollment;
  @override
  Widget build(BuildContext context) {
    final c = widget.childData, sports = rows(c['sports']);
    final sport =
        sports
            .where(
              (s) => s['id'] == (widget.selectedEnrollmentId ?? enrollment),
            )
            .firstOrNull ??
        sports.firstOrNull;
    final performance = sport?['performance'] is Map
        ? obj(sport!['performance'])
        : sport?.containsKey('performance') != true &&
              sports.length == 1 &&
              sport?['sport'] == 'كرة القدم' &&
              c['power'] is Map
        ? <String, dynamic>{...obj(c['power']), 'isFootballReport': true}
        : <String, dynamic>{};
    final assessedAxes = rows(performance['axes'] ?? <Json>[])
        .where((a) => a['value'] is num)
        .toList();
    final criteria = rows(performance['criteria'] ?? <Json>[])
        .where((a) => a['value'] is num)
        .toList();
    final score = performance['meanPerformance'];
    final legacyScore = performance['overallScore'];
    final scored = performance['scoredCriteria'] as num? ?? criteria.length;
    final applicable =
        performance['applicableCriteria'] as num? ?? criteria.length;
    final colors = Theme.of(context).colorScheme;
    final name = textOf(c['arabicName']);
    final mean = score is num ? performanceScoreText(score) : '—';
    final chart =
        performance['isFootballReport'] == true && assessedAxes.isNotEmpty
        ? assessedAxes
        : [
            for (final item in criteria)
              {'axis': item['name'], 'value': item['value']},
          ];
    Widget portrait(double width) => ClipRRect(
      borderRadius: BorderRadius.circular(18),
      child: SizedBox(
        width: width,
        height: 158,
        child: Stack(
          fit: StackFit.expand,
          children: [
            PlayerAvatar(
              name,
              auth: widget.auth,
              reference: c['photoReference'],
              gender: c['gender'],
              size: width,
              height: 158,
              square: true,
            ),
            Positioned(
              left: 0,
              right: 0,
              bottom: 0,
              child: Container(
                padding: const EdgeInsets.fromLTRB(12, 32, 12, 10),
                decoration: const BoxDecoration(
                  gradient: LinearGradient(
                    begin: Alignment.topCenter,
                    end: Alignment.bottomCenter,
                    colors: [Colors.transparent, Color(0xF0071B32)],
                  ),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    UiText(
                      mean,
                      style: const TextStyle(
                        color: Colors.white,
                        fontSize: 32,
                        height: 1.1,
                        fontWeight: FontWeight.w900,
                      ),
                    ),
                    const UiText(
                      'متوسط التقييم',
                      style: TextStyle(
                        color: Color(0xFFDCE7F3),
                        fontSize: 10,
                        height: 1.3,
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
    Widget indicators() => Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        UiText(
          'مؤشر الأداء · ${sport?['sport'] ?? ''}',
          style: TextStyle(
            fontSize: 11,
            height: 1.3,
            fontWeight: FontWeight.w600,
            color: colors.onSurfaceVariant,
          ),
        ),
        if (performance['isFootballReport'] == true && chart.isNotEmpty)
          RadarChart(chart, compact: true)
        else if (criteria.isNotEmpty)
          Padding(
            padding: const EdgeInsets.only(top: 8),
            child: Column(
              children: [
                for (final criterion in criteria.take(6))
                  Padding(
                    padding: const EdgeInsets.only(bottom: 5),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        UiText(
                          '${criterion['name']} · ${performanceScoreText(criterion['value'] as num)}',
                          style: const TextStyle(fontSize: 11, height: 1.25),
                        ),
                        const SizedBox(height: 4),
                        LinearProgressIndicator(
                          value: ((criterion['value'] as num) / 100).clamp(
                            0,
                            1,
                          ),
                          minHeight: 3,
                        ),
                      ],
                    ),
                  ),
              ],
            ),
          )
        else
          const Padding(
            padding: EdgeInsets.symmetric(vertical: 24),
            child: UiText(
              'سيظهر تطور الأداء عند نشر تقييم لهذه الرياضة.',
              style: TextStyle(fontSize: 12),
            ),
          ),
      ],
    );
    return Container(
      key: ValueKey('child-${c['id']}'),
      constraints: BoxConstraints(minHeight: widget.cardHeight ?? 0),
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(26),
        border: Border.all(color: colors.outlineVariant.withValues(alpha: .85)),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: .035),
            blurRadius: 24,
            offset: const Offset(0, 8),
          ),
        ],
      ),
      child: Column(
        mainAxisAlignment: widget.cardHeight == null
            ? MainAxisAlignment.start
            : MainAxisAlignment.spaceBetween,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    UiText(
                      name,
                      style: TextStyle(
                        fontSize: 20,
                        height: 1.35,
                        fontWeight: FontWeight.w800,
                        color: colors.onSurface,
                      ),
                    ),
                    const SizedBox(height: 3),
                    UiText(
                      childContextLine(c, sport, widget.today),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        fontSize: 11,
                        height: 1.3,
                        color: colors.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
              ),
              if ((c['monthlyStars'] as num? ?? 0) > 0)
                UiText(
                  '★ ' * (c['monthlyStars'] as num).toInt(),
                  style: TextStyle(
                    fontSize: 15,
                    color: ExperienceColors.award(context),
                  ),
                ),
            ],
          ),
          const SizedBox(height: 14),
          if (sports.length > 1) ...[
            DropdownButtonFormField<String>(
              key: ValueKey('card-enrollment-${c['id']}-${sport?['id']}'),
              initialValue: textOf(sport!['id']),
              isExpanded: true,
              decoration: const InputDecoration(
                labelText: 'الرياضة والاشتراك',
                isDense: true,
                constraints: BoxConstraints(minHeight: 42),
                contentPadding: EdgeInsets.symmetric(
                  horizontal: 10,
                  vertical: 7,
                ),
              ),
              items: [
                for (final item in sports)
                  DropdownMenuItem(
                    value: textOf(item['id']),
                    child: UiText(
                      '${item['sport']} · ${item['branch'] ?? ''}',
                      style: const TextStyle(fontSize: 12),
                    ),
                  ),
              ],
              onChanged: (value) {
                setState(() => enrollment = value);
                widget.onEnrollmentChanged?.call(value!);
              },
            ),
            const SizedBox(height: 14),
          ],
          LayoutBuilder(
            builder: (context, bounds) =>
                bounds.maxWidth > 248 &&
                    MediaQuery.textScalerOf(context).scale(1) <= 1.3
                ? Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      portrait(bounds.maxWidth * .4),
                      const SizedBox(width: 12),
                      Expanded(child: indicators()),
                    ],
                  )
                : Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Center(child: portrait(146)),
                      const SizedBox(height: 14),
                      indicators(),
                    ],
                  ),
          ),
          if (score is num) ...[
            const SizedBox(height: 10),
            LinearProgressIndicator(
              key: ValueKey('performance-mean-gauge-${c['id']}'),
              value: (score / 100).clamp(0, 1),
              minHeight: 3,
            ),
            if (scored < applicable) ...[
              const SizedBox(height: 6),
              UiText(
                'التقييم غير مكتمل',
                style: TextStyle(fontSize: 11, color: colors.error),
              ),
            ],
          ] else ...[
            const UiText(
              'بيانات الأداء غير كافية لحساب المتوسط',
              style: TextStyle(fontSize: 11),
            ),
            if (legacyScore is num)
              UiText(
                '${performanceScoreText(legacyScore)} · التقييم المركب',
                style: const TextStyle(fontSize: 12),
              ),
          ],
          if (widget.showSubscription && sport != null) ...[
            const SizedBox(height: 14),
            Container(
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(
                color: colors.surfaceContainerLowest,
                borderRadius: BorderRadius.circular(15),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  if (sport['period'] is Map) ...[
                    Row(
                      children: [
                        Expanded(
                          child: UiText(
                            textOf(sport['period']['plan']),
                            style: const TextStyle(
                              fontSize: 12,
                              height: 1.4,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                        ),
                        const SizedBox(width: 8),
                        StatusPill(sport['period']['status'], fontSize: 10),
                      ],
                    ),
                    const SizedBox(height: 5),
                    UiText(
                      textOf(sport['group'] ?? 'في انتظار المجموعة'),
                      style: TextStyle(
                        fontSize: 11,
                        height: 1.3,
                        color: colors.onSurfaceVariant,
                      ),
                    ),
                    if (sport['period']['remainingSessions'] != null)
                      Padding(
                        padding: const EdgeInsets.only(top: 8, bottom: 4),
                        child: UiText(
                          '${sport['period']['remainingSessions']} حصص متبقية',
                          style: TextStyle(
                            fontSize: 15,
                            height: 1.3,
                            fontWeight: FontWeight.w800,
                            color: colors.primary,
                          ),
                        ),
                      ),
                    Wrap(
                      spacing: 12,
                      runSpacing: 4,
                      children: [
                        UiText(
                          'بداية الاشتراك: ${shortArabicDate(sport['period']['startDate'])}',
                          style: TextStyle(
                            fontSize: 10,
                            height: 1.4,
                            color: colors.onSurfaceVariant,
                          ),
                        ),
                        UiText(
                          'نهاية الاشتراك: ${shortArabicDate(sport['period']['endDate'])}',
                          style: TextStyle(
                            fontSize: 10,
                            height: 1.4,
                            color: colors.onSurfaceVariant,
                          ),
                        ),
                      ],
                    ),
                  ] else
                    const UiText(
                      'لا يوجد اشتراك فعال',
                      style: TextStyle(fontSize: 12),
                    ),
                ],
              ),
            ),
          ],
          for (final coach in rows(sport?['coaches'] ?? []))
            Align(
              alignment: AlignmentDirectional.centerStart,
              child: TextButton.icon(
                onPressed: () => openPage(
                  context,
                  CoachFeedbackPage(
                    auth: widget.auth,
                    playerId: textOf(c['id']),
                    initialGroupId: textOf(coach['groupId']),
                    initialCoachId: textOf(coach['id']),
                  ),
                ),
                icon: const Icon(Icons.sports_outlined, size: 17),
                label: UiText(
                  'المدرب ${coach['name']}',
                  style: const TextStyle(fontSize: 12),
                ),
              ),
            ),
          const SizedBox(height: 8),
          GuardianChildActions(
            auth: widget.auth,
            playerId: textOf(c['id']),
            playerName: name,
            nutritionDate: widget.nutritionDate ?? widget.today,
            enrollmentId: sport?['id']?.toString(),
            nextSession: sport?['nextSession'] is Map
                ? obj(sport!['nextSession'])
                : null,
          ),
          const SizedBox(height: 12),
          FilledButton(
            onPressed: () {
              if (widget.onOpenEnrollment != null) {
                widget.onOpenEnrollment!(sport?['id']?.toString());
              } else {
                widget.onTap();
              }
            },
            child: FittedBox(
              fit: BoxFit.scaleDown,
              child: UiText(
                'ملف ${name.split(' ').first} والتقرير',
                maxLines: 1,
                softWrap: false,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class NextSessionCard extends StatelessWidget {
  final String childName;
  final Json sport;
  final VoidCallback onTap;
  const NextSessionCard({
    super.key,
    required this.childName,
    required this.sport,
    required this.onTap,
  });
  @override
  Widget build(BuildContext context) {
    final session = obj(sport['nextSession']),
        date = DateTime.tryParse(textOf(session['sessionDate']));
    return GCard(
      onTap: onTap,
      child: Row(
        children: [
          Container(
            width: 70,
            padding: const EdgeInsets.all(8),
            decoration: BoxDecoration(
              color: Theme.of(context).colorScheme.primary
                  .withValues(alpha: .12),
              borderRadius: BorderRadius.circular(12),
            ),
            child: Column(
              children: [
                UiText(
                  date == null
                      ? '—'
                      : localized(
                          context,
                          label(
                            [
                              'Sunday',
                              'Monday',
                              'Tuesday',
                              'Wednesday',
                              'Thursday',
                              'Friday',
                              'Saturday',
                            ][date.weekday % 7],
                          ),
                          [
                            'Sun',
                            'Mon',
                            'Tue',
                            'Wed',
                            'Thu',
                            'Fri',
                            'Sat',
                          ][date.weekday % 7],
                        ),
                  style: TextStyle(fontSize: 14),
                ),
                UiText(
                  date?.day.toString() ?? '—',
                  style: TextStyle(
                    fontSize: 24,
                    color: Theme.of(context).colorScheme.primary,
                    fontWeight: FontWeight.bold,
                  ),
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
                  localized(
                    context,
                    'الحصة القادمة · $childName',
                    'Next session · $childName',
                  ),
                  style: TextStyle(fontWeight: FontWeight.bold),
                ),
                UiText(
                  trainingTimeRange(session['startTime'], session['endTime']),
                  textDirection: TextDirection.rtl,
                ),
                UiText(
                  sessionDateLabel(session['sessionDate']),
                  style: const TextStyle(fontSize: 14),
                ),
                UiText(
                  '${session['group'] ?? sport['group']} · ${sport['branch']}',
                  style: const TextStyle(fontSize: 14),
                ),
                if (session['resource'] != null)
                  UiText(
                    textOf(session['resource']),
                    style: const TextStyle(fontSize: 14),
                  ),
              ],
            ),
          ),
          Icon(Icons.chevron_right),
        ],
      ),
    );
  }
}

class CatalogSections extends StatefulWidget {
  final AuthController auth;
  const CatalogSections({super.key, required this.auth});
  @override
  State<CatalogSections> createState() => _CatalogSectionsState();
}

class _CatalogSectionsState extends State<CatalogSections> {
  late Future<Object?> request;
  @override
  void initState() {
    super.initState();
    request = load();
  }

  Future<Object?> load() =>
      widget.auth.request('GET', '/api/v1/guardian/catalog');
  @override
  Widget build(BuildContext context) => FutureBuilder(
    future: request,
    builder: (context, snapshot) {
      if (snapshot.hasError) {
        return TextButton(
          onPressed: () => setState(() {
            request = load();
          }),
          child: const UiText('تعذر تحميل الكتالوج · إعادة المحاولة'),
        );
      }
      if (!snapshot.hasData) {
        return const Padding(
          padding: EdgeInsets.all(20),
          child: LinearProgressIndicator(),
        );
      }
      final sections = rows(snapshot.data);
      return Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          GLink(
            'طلباتي من المنتجات الرياضية',
            Icons.shopping_bag_outlined,
            () => openPage(context, GuardianProductOrders(auth: widget.auth)),
          ),
          if (sections.isEmpty)
            const EmptyMessage('لا توجد منتجات منشورة لرياضات الأبناء.'),
          for (final section in sections) ...[
            Heading(textOf(section['sport'])),
            GentleCarousel(
              height:
                  300 +
                  (MediaQuery.textScalerOf(context).scale(1).clamp(1, 2) - 1) *
                      120,
              width:
                  200 +
                  (MediaQuery.textScalerOf(context).scale(1).clamp(1, 2) - 1) *
                      60,
              count: rows(section['items']).length,
              itemBuilder: (context, index) {
                final item = rows(section['items'])[index];
                final base = (item['displayPrice'] as num?)?.toDouble();
                final price = base == null
                    ? null
                    : base *
                          (100 - (item['discountPercentage'] as num? ?? 0)) /
                          100;
                return GCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      ClipRRect(
                        borderRadius: BorderRadius.circular(12),
                        child: DemoImage(item['imageReference'], height: 146),
                      ),
                      const SizedBox(height: 8),
                      UiText(
                        textOf(item['arabicName']),
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(
                          fontSize: 13,
                          fontWeight: FontWeight.bold,
                        ),
                      ),
                      const Spacer(),
                      if (price != null)
                        UiText(
                          money(price, item['currency']),
                          style: TextStyle(
                            color: Theme.of(context).colorScheme.primary,
                            fontWeight: FontWeight.bold,
                          ),
                        ),
                      FilledButton(
                        onPressed:
                            price != null &&
                                (item['stockQuantity'] as num? ?? 0) > 0
                            ? () => openPage(
                                context,
                                ProductPurchase(
                                  auth: widget.auth,
                                  product: {
                                    ...item,
                                    'sportId': section['sportId'],
                                  },
                                ),
                              )
                            : null,
                        child: UiText(localized(context, 'شراء', 'Buy')),
                      ),
                    ],
                  ),
                );
              },
            ),
          ],
        ],
      );
    },
  );
}

class GuardianFeedbackChildren extends StatelessWidget {
  const GuardianFeedbackChildren({
    super.key,
    required this.auth,
    required this.children,
    this.complaint = false,
  });
  final AuthController auth;
  final List<Json> children;
  final bool complaint;
  @override
  Widget build(BuildContext context) {
    final associations = <String, List<Json>>{};
    for (final child in children) {
      for (final sport in rows(child['sports'] ?? [])) {
        for (final coach in rows(sport['coaches'] ?? [])) {
          associations.putIfAbsent(textOf(coach['id']), () => []).add({
            'child': child,
            'sport': sport,
            'coach': coach,
          });
        }
      }
    }
    Widget childTile(Json child, {Json? sport, Json? coach}) => ListTile(
      contentPadding: const EdgeInsets.symmetric(vertical: 6),
      leading: PlayerAvatar(
        textOf(child['arabicName']),
        auth: auth,
        reference: child['photoReference'],
        gender: child['gender'],
        size: 44,
        square: true,
      ),
      title: UiText(
        textOf(child['arabicName']),
        style: const TextStyle(fontWeight: FontWeight.w700),
      ),
      subtitle: sport == null
          ? null
          : UiText(
              '${sport['sport']} · ${sport['group']}',
              style: const TextStyle(fontSize: 12),
            ),
      trailing: const Icon(Icons.chevron_left),
      onTap: () => openPage(
        context,
        CoachFeedbackPage(
          auth: auth,
          playerId: textOf(child['id']),
          initialKind: complaint ? 'Complaint' : 'Rating',
          initialGroupId: coach?['groupId']?.toString(),
          initialCoachId: coach?['id']?.toString(),
        ),
      ),
    );
    return GuardianPage(
      title: complaint ? 'شكوى أو اقتراح' : 'تقييم المدربين',
      child: gList([
        if (complaint)
          for (final child in children) GCard(child: childTile(child)),
        if (!complaint)
          for (final entries in associations.values)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Row(
                    children: [
                      const Icon(Icons.sports_outlined),
                      const SizedBox(width: 8),
                      Expanded(
                        child: UiText(
                          textOf(entries.first['coach']['name']),
                          style: const TextStyle(
                            fontSize: 17,
                            fontWeight: FontWeight.w800,
                          ),
                        ),
                      ),
                    ],
                  ),
                  for (final entry in entries)
                    childTile(
                      obj(entry['child']),
                      sport: obj(entry['sport']),
                      coach: obj(entry['coach']),
                    ),
                ],
              ),
            ),
        if (children.isEmpty)
          const EmptyMessage('لا يوجد أبناء مرتبطون بالحساب حاليًا.'),
        if (!complaint && children.isNotEmpty && associations.isEmpty)
          const EmptyMessage('لا يوجد مدرب نشط مرتبط بالأبناء حاليًا.'),
      ]),
    );
  }
}
