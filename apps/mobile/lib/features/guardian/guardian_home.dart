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
import 'coach_feedback.dart';
import 'guardian_payments.dart';
import 'guardian_report.dart';
import 'family_nutrition_rail.dart';
import 'family_media.dart';
import 'reward_banner.dart';
import 'enrollment_requests.dart';
import '../shared/academy_news.dart';
import '../shared/community_ads.dart';
import '../shared/notification_inbox.dart';
import 'sport_store.dart';
import '../shared/matches.dart';
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
  int homeRevision = 0;
  Future<List<Json>>? upcomingMatches;
  final selectedEnrollments = <String, String>{};
  bool restoredSelection = false;
  (String, AuthController, String?) get selectionKey =>
      ('guardian-home-selection', widget.auth, identity);

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (restoredSelection || identity == null) return;
    restoredSelection = true;
    final saved = PageStorage.maybeOf(context)
        ?.readState(context, identifier: selectionKey);
    if (saved is (String, Map<String, String>)) {
      selectedPlayer = saved.$1;
      selectedEnrollments.addAll(saved.$2);
    }
  }

  void selectChild(String player, {String? enrollment}) {
    if (identity == null || identity != currentIdentity) return;
    setState(() {
      selectedPlayer = player;
      if (enrollment != null) selectedEnrollments[player] = enrollment;
    });
    PageStorage.maybeOf(context)?.writeState(context, (
      player,
      Map<String, String>.of(selectedEnrollments),
    ), identifier: selectionKey);
  }

  Future<List<Json>> readUpcomingMatches() async {
    final matches = rows(
      await widget.auth.request('GET', '/api/v1/my-matches?upcomingOnly=true'),
    ).where((item) => item['isUpcoming'] == true).toList();
    matches.sort(
      (a, b) => '${a['matchDate']} ${a['startTime']}'.compareTo(
        '${b['matchDate']} ${b['startTime']}',
      ),
    );
    return matches;
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
      setState(() {
        upcomingMatches = null;
        homeRevision++;
      });
    }
  }

  void contextChanged() {
    if (!mounted || identity == currentIdentity) return;
    setState(() {
      identity = currentIdentity;
      selectedPlayer = null;
      upcomingMatches = null;
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
      upcomingMatches = null;
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
              final competitions =
                  BrandScope.maybeOf(context)?.features.contains('matches') ??
                  true;
              final nutritionToday = textOf(
                home['trainingToday'] ?? home['today'],
              );
              return gList([
                GuardianUrgentMatchAlerts(
                  auth: widget.auth,
                  alerts: rows(home['urgentAlerts'] ?? <Json>[]),
                  onReturned: () {
                    noticesChanged();
                  },
                ),
                GuardianRewardBanner(
                  key: ValueKey('reward-$identity'),
                  auth: widget.auth,
                ),
                const RoleHomeIntro(title: 'أبنائي'),
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
                              onSelected: (_) =>
                                  selectChild(textOf(child['id'])),
                            ),
                          ),
                      ],
                    ),
                  ),
                if (allChildren.isNotEmpty)
                  FutureBuilder<List<Json>>(
                    future: competitions
                        ? upcomingMatches ??= readUpcomingMatches()
                        : null,
                    builder: (context, matches) => GuardianChildCarousel(
                      key: ValueKey('carousel-$identity'),
                      auth: widget.auth,
                      children: allChildren,
                      selectedPlayerId: selected,
                      selectedEnrollments: selectedEnrollments,
                      onEnrollmentSelected: (player, enrollment) =>
                          selectChild(player, enrollment: enrollment),
                      today: textOf(home['today']),
                      nutritionDate: nutritionToday,
                      matches: competitions
                          ? matches.data ?? const []
                          : const [],
                      onSelected: selectChild,
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
                  ),
                if (allChildren.isEmpty)
                  const EmptyMessage('لا يوجد أبناء مرتبطون بهذا الحساب.'),
                _GuardianQuickActions(
                  key: ValueKey('guardian-home-actions-$identity'),
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
                if (allChildren.isNotEmpty) ...[
                  Heading(
                    localized(
                      context,
                      'اقتراحات من مكتبة التغذية',
                      'Suggestions from the nutrition library',
                    ),
                  ),
                  FamilyNutritionRail(
                    auth: widget.auth,
                    children: allChildren,
                    today: nutritionToday,
                  ),
                ],
                GuardianSponsoredCard(auth: widget.auth),
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
                NewsStrip(auth: widget.auth),
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

class _GuardianQuickActions extends StatelessWidget {
  const _GuardianQuickActions({super.key, required this.actions});
  final List<OwnerRailAction> actions;

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      const Heading('العمليات السريعة'),
      LayoutBuilder(
        builder: (context, bounds) {
          final scale = MediaQuery.textScalerOf(context).scale(1);
          final columns = scale > 1.3
              ? 2
              : bounds.maxWidth >= 440
              ? 5
              : bounds.maxWidth >= 320
              ? 4
              : 3;
          const gap = 8.0;
          final width = (bounds.maxWidth - gap * (columns - 1)) / columns;
          final colors = Theme.of(context).colorScheme;
          return Wrap(
            spacing: gap,
            runSpacing: gap,
            children: [
              for (final action in actions)
                SizedBox(
                  width: width,
                  child: Material(
                    color: colors.surfaceContainerLow,
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(14),
                      side: BorderSide(color: colors.outlineVariant),
                    ),
                    clipBehavior: Clip.antiAlias,
                    child: InkWell(
                      key: action.key,
                      onTap: action.onTap,
                      child: Padding(
                        padding: const EdgeInsets.symmetric(
                          horizontal: 6,
                          vertical: 10,
                        ),
                        child: Column(
                          children: [
                            CircleAvatar(
                              radius: 17,
                              backgroundColor: Color.alphaBlend(
                                action.color.withValues(alpha: .13),
                                colors.surfaceContainerHigh,
                              ),
                              foregroundColor: colors.onPrimaryContainer,
                              child: Icon(action.icon, size: 18),
                            ),
                            const SizedBox(height: 6),
                            UiText(
                              action.label,
                              textAlign: TextAlign.center,
                              style: const TextStyle(
                                fontSize: 11,
                                fontWeight: FontWeight.w700,
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ),
                ),
            ],
          );
        },
      ),
    ],
  );
}

class GuardianChildCarousel extends StatefulWidget {
  const GuardianChildCarousel({
    super.key,
    required this.auth,
    required this.children,
    this.selectedPlayerId,
    required this.today,
    this.nutritionDate,
    this.matches = const [],
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
  final List<Json> matches;
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
  int layoutVersion = 0;
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
      widget.matches,
    ]);
    if (signature != layoutSignature) {
      layoutSignature = signature;
      layoutVersion++;
      heights.clear();
    }
    final measuredVersion = layoutVersion;
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
                    key: ValueKey((i, measuredVersion)),
                    onSize: (size) {
                      if (mounted &&
                          measuredVersion == layoutVersion &&
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
                      nutritionDate: widget.nutritionDate ?? widget.today,
                      nextMatch: widget.matches
                          .where(
                            (match) =>
                                (match['children'] as List?)?.contains(
                                  c['id'],
                                ) ==
                                true,
                          )
                          .firstOrNull,
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
  const _ChildCardMeasure({
    super.key,
    required this.onSize,
    required super.child,
  });
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
  final Json? nextMatch;
  final VoidCallback onTap;
  final ValueChanged<String?>? onOpenEnrollment;
  final String? selectedEnrollmentId;
  final ValueChanged<String>? onEnrollmentChanged;
  const PremiumChildCard({
    super.key,
    required this.auth,
    required this.childData,
    required this.today,
    required this.onTap,
    this.showSubscription = true,
    this.nutritionDate,
    this.nextMatch,
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
    final age = ageOn(c['dateOfBirth'], widget.today);
    final period = sport?['period'] is Map ? obj(sport!['period']) : null;
    final coaches = rows(sport?['coaches'] ?? const []);
    final session = sport?['nextSession'] is Map
        ? obj(sport!['nextSession'])
        : null;
    final match = widget.nextMatch;
    const portraitHeight = 134.0;
    String appointment(Object? date, Object? time) {
      final when = DateTime.tryParse(textOf(date));
      if (when == null) return 'الموعد لم يُحدد';
      final shortDate = MaterialLocalizations.of(context)
          .formatShortMonthDay(when);
      return '$shortDate · ${trainingTimeOf(time)}';
    }

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
        height: portraitHeight,
        child: Stack(
          fit: StackFit.expand,
          children: [
            PlayerAvatar(
              name,
              auth: widget.auth,
              reference: c['photoReference'],
              gender: c['gender'],
              size: width,
              height: portraitHeight,
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
                      'متوسط الأداء',
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
            padding: EdgeInsets.symmetric(vertical: 14),
            child: UiText(
              'سيظهر تطور الأداء عند نشر تقييم لهذه الرياضة.',
              style: TextStyle(fontSize: 12),
            ),
          ),
      ],
    );
    return Container(
      key: ValueKey('child-${c['id']}'),
      height: widget.cardHeight,
      padding: const EdgeInsets.all(14),
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
                    if (sports.length <= 1)
                      UiText(
                        [
                          if (sport != null) textOf(sport['sport']),
                          if (age != null) '$age سنة',
                          if (sport?['branch'] != null)
                            textOf(sport!['branch']),
                        ].join(' · '),
                        style: TextStyle(
                          fontSize: 11,
                          height: 1.35,
                          color: colors.onSurfaceVariant,
                        ),
                      ),
                  ],
                ),
              ),
              if ((c['monthlyStars'] as num? ?? 0) > 0)
                Semantics(
                  label: '${c['monthlyStars']} نجوم',
                  child: Row(
                    children: [
                      Icon(
                        Icons.star_rounded,
                        size: 17,
                        color: ExperienceColors.award(context),
                      ),
                      if ((c['monthlyStars'] as num) > 1)
                        UiText(
                          '${c['monthlyStars']}',
                          style: TextStyle(
                            color: ExperienceColors.award(context),
                            fontWeight: FontWeight.w800,
                          ),
                        ),
                    ],
                  ),
                ),
            ],
          ),
          const SizedBox(height: 9),
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
                      [
                        textOf(item['sport']),
                        if (age != null) '$age سنة',
                        textOf(item['branch']),
                      ].join(' · '),
                      style: const TextStyle(fontSize: 12),
                    ),
                  ),
              ],
              onChanged: (value) {
                setState(() => enrollment = value);
                widget.onEnrollmentChanged?.call(value!);
              },
            ),
            const SizedBox(height: 8),
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
            const SizedBox(height: 6),
            Tooltip(
              message: 'متوسط الدرجات المسجّلة في آخر تقييم منشور.',
              child: UiText(
                '$scored من $applicable معايير · آخر تقييم منشور',
                style: TextStyle(
                  fontSize: 10,
                  height: 1.3,
                  color: colors.onSurfaceVariant,
                ),
              ),
            ),
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
            const SizedBox(height: 8),
            DecoratedBox(
              decoration: BoxDecoration(
                color: colors.surfaceContainerLowest,
                borderRadius: BorderRadius.circular(13),
              ),
              child: Padding(
                padding: const EdgeInsets.symmetric(
                  horizontal: 10,
                  vertical: 7,
                ),
                child: period == null
                    ? const UiText(
                        'لا يوجد اشتراك فعال',
                        style: TextStyle(fontSize: 12),
                      )
                    : Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Row(
                            children: [
                              Expanded(
                                child: UiText(
                                  textOf(period['plan']),
                                  maxLines: 2,
                                  overflow: TextOverflow.ellipsis,
                                  style: const TextStyle(
                                    fontSize: 12,
                                    fontWeight: FontWeight.w700,
                                  ),
                                ),
                              ),
                              const SizedBox(width: 6),
                              StatusPill(period['status'], fontSize: 10),
                            ],
                          ),
                          const SizedBox(height: 4),
                          UiText(
                            [
                              textOf(sport['group'] ?? 'في انتظار المجموعة'),
                              if (period['remainingSessions'] != null)
                                '${period['remainingSessions']} حصص متبقية',
                              if (period['endDate'] != null)
                                'حتى ${day(period['endDate'])}',
                            ].join(' · '),
                            style: TextStyle(
                              fontSize: 10,
                              height: 1.4,
                              color: colors.onSurfaceVariant,
                            ),
                          ),
                        ],
                      ),
              ),
            ),
          ],
          if (coaches.isNotEmpty)
            Wrap(
              spacing: 6,
              children: [
                for (final coach in coaches)
                  TextButton(
                    onPressed: () => openPage(
                      context,
                      CoachFeedbackPage(
                        auth: widget.auth,
                        playerId: textOf(c['id']),
                        initialGroupId: textOf(coach['groupId']),
                        initialCoachId: textOf(coach['id']),
                      ),
                    ),
                    child: Text.rich(
                      TextSpan(
                        text: ui(context, 'المدرب'),
                        style: TextStyle(color: colors.onSurfaceVariant),
                        children: [
                          TextSpan(
                            text: '  ${coach['name']}',
                            style: TextStyle(
                              color: colors.primary,
                              fontWeight: FontWeight.w800,
                            ),
                          ),
                        ],
                      ),
                      style: const TextStyle(fontSize: 12),
                    ),
                  ),
              ],
            ),
          if (widget.nutritionDate != null) ...[
            const SizedBox(height: 6),
            _ChildCardShortcuts(
              actions: [
                (
                  icon: Icons.calendar_today_outlined,
                  label: session == null ? 'جدول الحصص' : 'الحصة القادمة',
                  detail: session == null
                      ? 'الجدول'
                      : appointment(
                          session['sessionDate'],
                          session['startTime'],
                        ),
                  onTap: () => openPage(
                    context,
                    ProfileRecords(
                      auth: widget.auth,
                      playerId: textOf(c['id']),
                      section: 'schedule',
                      enrollmentId: sport?['id']?.toString(),
                    ),
                  ),
                ),
                if (match != null)
                  (
                    icon: Icons.sports_soccer_outlined,
                    label: 'المباراة القادمة',
                    detail: appointment(match['matchDate'], match['startTime']),
                    onTap: () => openPage(
                      context,
                      MatchSchedule(
                        auth: widget.auth,
                        playerId: textOf(c['id']),
                        matchId: textOf(match['id']),
                      ),
                    ),
                  ),
                (
                  icon: Icons.restaurant_outlined,
                  label: 'مكتبة التغذية',
                  detail: 'اقتراحات اليوم',
                  onTap: () => openPage(
                    context,
                    NutritionLibrary(
                      auth: widget.auth,
                      playerId: textOf(c['id']),
                      today: widget.nutritionDate!,
                      playerName: name,
                      showToday: true,
                    ),
                  ),
                ),
              ],
            ),
          ],
          const SizedBox(height: 7),
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

class _ChildCardShortcuts extends StatelessWidget {
  const _ChildCardShortcuts({required this.actions});
  final List<({IconData icon, String label, String detail, VoidCallback onTap})>
  actions;

  @override
  Widget build(BuildContext context) {
    final accessible = MediaQuery.textScalerOf(context).scale(1) > 1.3;
    final colors = Theme.of(context).colorScheme;
    const gap = 5.0;
    final tiles = [
      for (final action in actions)
        Material(
          color: colors.surfaceContainerHigh,
          borderRadius: BorderRadius.circular(12),
          child: InkWell(
            borderRadius: BorderRadius.circular(12),
            onTap: action.onTap,
            child: Padding(
              padding: const EdgeInsets.symmetric(vertical: 7, horizontal: 3),
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Icon(action.icon, color: colors.primary, size: 16),
                  const SizedBox(height: 2),
                  UiText(
                    action.label,
                    textAlign: TextAlign.center,
                    style: const TextStyle(
                      fontWeight: FontWeight.w800,
                      fontSize: 10,
                    ),
                  ),
                  UiText(
                    action.detail,
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      color: colors.onSurfaceVariant,
                      fontSize: 9,
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
    ];
    if (accessible) {
      return Column(
        spacing: gap,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: tiles,
      );
    }
    return IntrinsicHeight(
      child: Row(
        spacing: gap,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          for (final tile in tiles) Expanded(child: tile),
          if (tiles.length < 3) const Spacer(),
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
