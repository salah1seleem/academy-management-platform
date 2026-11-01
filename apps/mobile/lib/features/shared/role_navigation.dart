import 'role_home_navigation.dart';
import '../guardian/guardian_progress.dart';
import 'directory_cards.dart';
import '../coach/coach_evaluation.dart';
import 'coach_tasks.dart';
import 'mobile_push.dart';
import '../../core/theme/academy_brand.dart';
import '../../core/theme/academy_home_skin.dart';
import 'owner_brand_navigation.dart';
import 'attendance_corrections.dart';
import '../owner/owner_home_settings.dart';
import '../owner/native_staff_management.dart';

import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import 'experience_ui.dart';
import 'role_shell_body.dart';
import '../guardian/guardian_ui.dart';
import '../guardian/guardian_content.dart';
import '../guardian/guardian_profile.dart';
import '../guardian/guardian_report.dart';
import '../guardian/family_renewal_request.dart';
import '../coach/coach_home.dart';
import '../coach/coach_player.dart';
import '../coach/monthly_stars.dart';
import '../admin/admin_home.dart';
import '../owner/owner_reports.dart';
import '../owner/owner_quick_operations.dart';
import '../admin/native_player_workspace.dart';
import '../admin/native_renewal.dart';
import '../admin/native_renewal_proof_review.dart';
import '../admin/native_product_orders.dart';
import 'academy_news.dart';
import 'community_ads.dart';
import 'matches.dart';
import 'native_match_management.dart';
import 'staff_amounts.dart';
import 'staff_attendance_exceptions.dart';
import 'staff_work_periods.dart';
import 'coach_agreements.dart';
import 'facility_board.dart';
import 'role_agenda.dart';
import 'help_center.dart';
import 'assigned_followups.dart';
import 'native_academy_content.dart';

class NativeRoleTabs extends StatefulWidget {
  final AuthController auth;
  final Widget home, account;
  final String role;
  final String environment;
  const NativeRoleTabs({
    super.key,
    required this.auth,
    required this.home,
    required this.account,
    required this.role,
    this.environment = 'Production',
  });
  @override
  State<NativeRoleTabs> createState() => _NativeRoleTabsState();
}

class _NativeRoleTabsState extends State<NativeRoleTabs> {
  int selected = 0;
  void returnHome() {
    if (mounted) setState(() => selected = 0);
  }

  @override
  void initState() {
    super.initState();
    if (widget.role == 'AcademyOwner') {
      registerOwnerHome(widget.auth, returnHome);
    }
  }

  @override
  void dispose() {
    if (widget.role == 'AcademyOwner') registerOwnerHome(widget.auth, null);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final guardian = widget.role == 'Guardian',
        owner = widget.role == 'AcademyOwner',
        admin = widget.role == 'AcademyAdmin';
    final swimming = BrandScope.maybeOf(context)?.sportTheme == 'swimming';
    final matchLabel = swimming ? 'المنافسات' : 'المباريات';
    final destinations = guardian
        ? [
            ('الرئيسية', 'Home', Icons.home_outlined),
            ('الحصص', 'Sessions', Icons.calendar_month_outlined),
            (
              matchLabel,
              swimming ? 'Competitions' : 'Matches',
              Icons.emoji_events_outlined,
            ),
            ('التقييمات', 'Evaluations', Icons.insights_outlined),
          ]
        : owner
        ? [
            ('الرئيسية', 'Home', Icons.home_outlined),
            ('العمليات', 'Operations', Icons.bolt_outlined),
            (
              matchLabel,
              swimming ? 'Competitions' : 'Matches',
              Icons.emoji_events_outlined,
            ),
            ('التقارير', 'Reports', Icons.analytics_outlined),
            ('المزيد', 'More', Icons.grid_view_outlined),
          ]
        : admin
        ? [
            ('الرئيسية', 'Home', Icons.home_outlined),
            ('العمليات', 'Operations', Icons.bolt_outlined),
            ('الحضور', 'Attendance', Icons.fact_check_outlined),
            ('المزيد', 'More', Icons.grid_view_outlined),
          ]
        : [
            ('الرئيسية', 'Home', Icons.home_outlined),
            (
              'الحضور والتقييم',
              'Attendance & evaluation',
              Icons.fact_check_outlined,
            ),
            (
              matchLabel,
              swimming ? 'Competitions' : 'Matches',
              Icons.emoji_events_outlined,
            ),
            ('المزيد', 'More', Icons.grid_view_outlined),
          ];
    final brand = BrandScope.maybeOf(context);
    final showMatches = brand == null || brand.features.contains('matches');
    if (!showMatches && selected == 2 && !admin) selected = 0;
    final visibleIndexes = [
      for (var i = 0; i < destinations.length; i++)
        if (showMatches || admin || i != 2) i,
    ];
    final isMore = !guardian && selected == destinations.length - 1;
    final Widget page = selected == 0
        ? widget.home
        : isMore
        ? NativeMore(
            auth: widget.auth,
            role: widget.role,
            account: widget.account,
            environment: widget.environment,
          )
        : guardian
        ? selected == 1
              ? RoleAgenda(auth: widget.auth, guardian: true)
              : selected == 2
              ? MatchSchedule(auth: widget.auth)
              : GuardianHub(
                  auth: widget.auth,
                  section: 'evaluations',
                  environment: widget.environment,
                )
        : owner
        ? selected == 1
              ? OwnerQuickOperations(
                  auth: widget.auth,
                  environment: widget.environment,
                )
              : selected == 2
              ? MatchSchedule(auth: widget.auth)
              : OwnerReports(auth: widget.auth)
        : admin
        ? selected == 1
              ? OwnerQuickOperations(
                  auth: widget.auth,
                  environment: widget.environment,
                )
              : NativeSessions(auth: widget.auth)
        : selected == 1
        ? CoachGroupsPage(auth: widget.auth)
        : MatchSchedule(auth: widget.auth);
    final screen = selected == 0
        ? 'home'
        : isMore
        ? 'more'
        : guardian
        ? (selected == 1
              ? 'attendance'
              : selected == 2
              ? 'matches'
              : 'evaluations')
        : owner
        ? (selected == 1
              ? 'operations'
              : selected == 2
              ? 'matches'
              : 'reports')
        : admin
        ? (selected == 1 ? 'operations' : 'attendance')
        : selected == 1
        ? 'evaluations'
        : 'matches';
    final header = ExperienceHeader(
      auth: widget.auth,
      subtitle: switch (widget.role) {
        'AcademyOwner' => 'لوحة المالك',
        'AcademyAdmin' => 'الإداري',
        'Coach' => 'المدرب',
        _ => 'ولي الأمر',
      },
      lesson: screen,
      home: selected == 0,
      account: widget.account,
    );
    final content = KeyedSubtree(
      key: ValueKey('$selected-${widget.role}'),
      child: RoleShellBody(
        child: (owner || selected == 0) ? AcademyHomeSkin(child: page) : page,
      ),
    );
    return Scaffold(
      body: SafeArea(
        bottom: false,
        child: Column(
          children: [
            selected == 0
                ? Padding(padding: const EdgeInsets.only(top: 8), child: header)
                : header,

            Expanded(child: content),
          ],
        ),
      ),
      bottomNavigationBar: owner
          ? OwnerBrandNavigation(
              selected: selected,
              showMatches: showMatches,
              onSelected: (i) => setState(() => selected = i),
            )
          : SafeArea(
              top: false,
              child: Padding(
                padding: const EdgeInsets.fromLTRB(14, 6, 14, 8),
                child: Container(
                  decoration: BoxDecoration(
                    color: Theme.of(context).colorScheme.surface,
                    borderRadius: BorderRadius.circular(24),
                    border: Border.all(
                      color: Theme.of(context).colorScheme.outlineVariant
                          .withValues(alpha: .8),
                    ),
                  ),
                  clipBehavior: Clip.antiAlias,
                  child: NavigationBar(
                    height: MediaQuery.textScalerOf(context).scale(1) > 1.4
                        ? 94
                        : 76,
                    selectedIndex: visibleIndexes.indexOf(selected),
                    onDestinationSelected: (i) =>
                        setState(() => selected = visibleIndexes[i]),
                    labelBehavior:
                        NavigationDestinationLabelBehavior.alwaysShow,
                    destinations: [
                      for (final d in [
                        for (final i in visibleIndexes) destinations[i],
                      ])
                        NavigationDestination(
                          icon: d.$1 == 'الرئيسية' && brand?.logo != null
                              ? const SizedBox(
                                  width: 34,
                                  height: 34,
                                  child: CanonicalAcademyLogo(),
                                )
                              : Icon(d.$3),
                          selectedIcon:
                              d.$1 == 'الرئيسية' && brand?.logo != null
                              ? const SizedBox(
                                  width: 38,
                                  height: 38,
                                  child: CanonicalAcademyLogo(),
                                )
                              : Icon(
                                  d.$1 == 'الرئيسية'
                                      ? Icons.home_rounded
                                      : d.$3,
                                ),
                          label: localized(
                            context,
                            d.$1 == 'الحضور والتقييم'
                                ? 'الحضور\nوالتقييم'
                                : d.$1,
                            d.$2,
                          ),
                        ),
                    ],
                  ),
                ),
              ),
            ),
    );
  }
}

class NativeMore extends StatelessWidget {
  final AuthController auth;
  final String role, environment;
  final Widget account;
  const NativeMore({
    super.key,
    required this.auth,
    required this.role,
    required this.account,
    required this.environment,
  });
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'المزيد', 'More'),
    child: gList([
      const Heading('أدوات الأكاديمية'),
      if (role == 'AcademyOwner')
        GLink(
          'الموظفون والصلاحيات',
          Icons.badge_outlined,
          () => openPage(context, NativeStaffManagement(auth: auth)),
        ),
      if (role == 'AcademyOwner' || role == 'AcademyAdmin' || role == 'Coach')
        NativeMatchEntry(auth: auth, asLink: true),
      if (role == 'AcademyOwner' || role == 'AcademyAdmin')
        NativeProductOrdersEntry(auth: auth),
      if (role == 'Guardian') ...[
        GLink(
          localized(context, 'التغذية والوجبات', 'Nutrition & meals'),
          Icons.restaurant_outlined,
          () => openPage(
            context,
            GuardianHub(
              auth: auth,
              section: 'nutrition',
              environment: environment,
            ),
          ),
        ),
        GLink(
          localized(context, 'تقدم أبنائي', 'Children’s progress'),
          Icons.insights_outlined,
          () => openPage(
            context,
            GuardianHub(
              auth: auth,
              section: 'evaluations',
              environment: environment,
            ),
          ),
        ),
      ],
      if (role == 'AcademyOwner' || role == 'AcademyAdmin')
        FacilityBoardLink(auth: auth),
      if (role == 'Guardian') SponsorPreferenceEntry(auth: auth),
      if (role == 'AcademyOwner')
        GLink(
          'إعدادات الرئيسية',
          Icons.dashboard_customize_outlined,
          () => openPage(context, OwnerHomeSettingsPage(auth: auth)),
        ),
      if (role == 'AcademyOwner')
        GLink(
          'الإعلانات',
          Icons.campaign_outlined,
          () => openPage(context, OwnerCommunityCampaigns(auth: auth)),
        ),
      CoachTasksEntry(auth: auth),
      if (role == 'AcademyOwner' || role == 'AcademyAdmin')
        AttendanceCorrectionsEntry(auth: auth),
      if (role == 'Coach')
        GLink(
          'نجوم فريقي الشهرية',
          Icons.star_outline_rounded,
          () => openPage(context, CoachMonthlyStars(auth: auth)),
        ),
      if (role == 'Coach')
        GLink(
          localized(context, 'مجموعاتي', 'My groups'),
          Icons.groups_outlined,
          () => openPage(context, CoachGroupsPage(auth: auth)),
        ),
      if (role == 'Coach' || role == 'AcademyAdmin')
        GLink(
          localized(context, 'سجل الحضور', 'Attendance history'),
          Icons.access_time_outlined,
          () => openPage(context, StaffWorkPeriodsPage(auth: auth)),
        ),
      if (role == 'AcademyAdmin')
        RemoteBody(
          auth: auth,
          path: '/api/v1/my-permissions',
          builder: (context, value) =>
              value is List && value.contains('followups.read')
              ? GLink(
                  'متابعاتي المسندة',
                  Icons.task_alt,
                  () => openPage(context, AssignedFollowUpsPage(auth: auth)),
                )
              : const SizedBox.shrink(),
        ),
      if (role == 'Coach' || role == 'AcademyAdmin')
        GLink(
          localized(context, 'طلب إثبات حضوري', 'My attendance claim'),
          Icons.fact_check_outlined,
          () => openPage(context, StaffAttendanceExceptionPage(auth: auth)),
        ),
      if (role == 'AcademyAdmin')
        RemoteBody(
          auth: auth,
          path: '/api/v1/my-permissions',
          builder: (context, value) =>
              value is List && value.contains('staff.provision')
              ? GLink(
                  localized(
                    context,
                    'مراجعة إثبات حضور الموظفين',
                    'Review staff attendance claims',
                  ),
                  Icons.rule_outlined,
                  () => openPage(
                    context,
                    StaffAttendanceExceptionPage(auth: auth, review: true),
                  ),
                )
              : const SizedBox.shrink(),
        ),
      if (role == 'Coach' || role == 'AcademyAdmin')
        GLink(
          localized(context, 'السلف والمبالغ الإضافية', 'Request extra amount'),
          Icons.add_card_outlined,
          () => openPage(context, StaffAmounts(auth: auth)),
        ),
      if (role == 'Coach')
        GLink(
          localized(
            context,
            'اتفاقات أجر الخاص',
            'Private coaching agreements',
          ),
          Icons.receipt_long_outlined,
          () => openPage(context, CoachAgreementsPage(auth: auth)),
        ),
      if (role == 'AcademyOwner' || role == 'AcademyAdmin')
        NativeContentEntry(auth: auth),
      if (role == 'Guardian')
        GLink(
          localized(context, 'الأخبار', 'News'),
          Icons.newspaper_outlined,
          () => openPage(context, AcademyNewsPage(auth: auth)),
        ),
      const Heading('التفضيلات والمساعدة'),
      MobilePushToggle(auth: auth),
      if (role != 'Coach') HelpEntry(auth: auth),
    ]),
  );
}

class CoachPlayers extends StatelessWidget {
  final AuthController auth;
  final bool admin;
  const CoachPlayers({super.key, required this.auth, this.admin = false});
  @override
  Widget build(BuildContext context) => admin
      ? NativePlayerDirectory(auth: auth)
      : GuardianPage(
          title: admin
              ? 'اللاعبون'
              : localized(context, 'لاعبو مجموعاتي', 'My groups’ players'),
          child: RemoteBody(
            auth: auth,
            path: admin
                ? '/api/v1/people/roster'
                : '/api/v1/evaluations/options',
            builder: (context, value) {
              final players = rows(obj(value)['enrollments']);
              return gList([
                for (final p in players)
                  PlayerDirectoryCard(
                    auth: auth,
                    player: {...p, 'arabicName': p['player']},
                    enrollments: [p],
                    openLabel: 'تفاصيل اللاعب',
                    onOpen: () => openPage(
                      context,
                      CoachPlayerPage(
                        auth: auth,
                        enrollmentId: textOf(p['id']),
                        groupId: textOf(p['groupId']),
                      ),
                    ),
                    onEvaluate: obj(value)['canEvaluate'] == true
                        ? () => openPage(
                            context,
                            CoachEvaluation(auth: auth, enrollment: p),
                          )
                        : null,
                  ),
                if (players.isEmpty) const EmptyMessage(),
              ]);
            },
          ),
        );
}

class GuardianHub extends StatelessWidget {
  final AuthController auth;
  final String section;
  final String environment;
  const GuardianHub({
    super.key,
    required this.auth,
    required this.section,
    this.environment = 'Production',
  });
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(
      context,
      section == 'children'
          ? 'أبنائي'
          : section == 'nutrition'
          ? 'التغذية والوجبات'
          : section == 'evaluations'
          ? 'تقدم أبنائي'
          : 'المواعيد والحضور',
      section == 'children'
          ? 'Children'
          : section == 'nutrition'
          ? 'Nutrition & meals'
          : section == 'evaluations'
          ? 'Children’s progress'
          : 'Schedule & attendance',
    ),
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/home',
      builder: (context, value) {
        final d = obj(value);
        if (section == 'evaluations') {
          return GuardianProgress(auth: auth, children: rows(d['children']));
        }
        return gList([
          if (section == 'schedule')
            GLink(
              localized(context, 'مباريات أبنائي', 'Children’s matches'),
              Icons.sports_soccer,
              () => openPage(context, MatchSchedule(auth: auth)),
            ),
          for (final c in rows(d['children']))
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Row(
                    children: [
                      PlayerAvatar(
                        textOf(c['arabicName']),
                        auth: auth,
                        reference: c['photoReference'],
                        size: 48,
                      ),
                      const SizedBox(width: 12),
                      Expanded(
                        child: UiText(
                          textOf(c['arabicName']),
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 12),
                  FilledButton.tonal(
                    onPressed: () => openPage(
                      context,
                      section == 'children'
                          ? ChildProfile(
                              auth: auth,
                              environment: environment,
                              playerId: textOf(c['id']),
                              today: textOf(d['today']),
                              nutritionDate: textOf(
                                d['trainingToday'] ?? d['today'],
                              ),
                            )
                          : section == 'nutrition'
                          ? NutritionLibrary(
                              auth: auth,
                              playerId: c['id'],
                              playerName: textOf(c['arabicName']),
                              today: d['trainingToday'] ?? d['today'],
                            )
                          : section == 'evaluations'
                          ? ReportHistory(auth: auth, playerId: c['id'])
                          : ProfileRecords(
                              auth: auth,
                              playerId: c['id'],
                              section: 'schedule',
                            ),
                    ),
                    child: UiText(
                      localized(context, 'عرض التفاصيل', 'View details'),
                    ),
                  ),
                  if (section == 'schedule')
                    TextButton(
                      onPressed: () => openPage(
                        context,
                        ProfileRecords(
                          auth: auth,
                          playerId: c['id'],
                          section: 'attendance',
                        ),
                      ),
                      child: UiText(
                        localized(context, 'حضور اللاعب', 'Player attendance'),
                      ),
                    ),
                ],
              ),
            ),
          if (rows(d['children']).isEmpty) const EmptyMessage(),
        ]);
      },
    ),
  );
}

class CoachGroupsPage extends StatelessWidget {
  final AuthController auth;
  const CoachGroupsPage({super.key, required this.auth});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'مجموعاتي', 'My groups'),
    child: FutureBuilder<List<Object?>>(
      future: Future.wait([auth.request('GET', '/api/v1/coach/groups')]),
      builder: (context, data) {
        if (data.hasError) return const EmptyMessage('تعذر تحميل المجموعات.');
        if (!data.hasData) {
          return const Center(child: CircularProgressIndicator());
        }
        final groups = rows(data.data![0]);
        return gList([
          for (final g in groups)
            GroupDirectoryCard(
              group: g,
              onTap: () => openPage(context, CoachGroup(auth: auth, group: g)),
            ),
          if (groups.isEmpty) const EmptyMessage(),
        ]);
      },
    ),
  );
}

class NativePaymentRequests extends StatelessWidget {
  final AuthController auth;
  final Map<String, String> filters;
  const NativePaymentRequests({
    super.key,
    required this.auth,
    this.filters = const {},
  });
  @override
  Widget build(BuildContext context) => filters['type'] == 'Product'
      ? NativeProductOrders(auth: auth, filters: filters)
      : GuardianPage(
          title: localized(context, 'طلبات الدفع', 'Payment requests'),
          child: RemoteBody(
            auth: auth,
            path:
                '/api/v1/manage/payment-requests?${Uri(queryParameters: {...filters, if (filters['from']?.isNotEmpty != true && filters['to']?.isNotEmpty != true) 'allDates': 'true', 'status': 'Pending'}).query}',
            builder: (context, value) {
              final d = obj(value);
              return gList([
                Heading(
                  localized(
                    context,
                    '${d['pending']} طلب بانتظار المراجعة',
                    '${d['pending']} requests awaiting review',
                  ),
                ),
                UiText(
                  localized(
                    context,
                    'اختر طلبًا لعرض تفاصيله والإجراءات المتاحة لصلاحياتك.',
                    'Choose a request to view its details and available actions.',
                  ),
                ),
                for (final r in rows(d['items']))
                  GCard(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        UiText(
                          textOf(r['player']),
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                        UiText('${r['sport']} · ${r['branch']}'),
                        UiText(textOf(r['plan'])),
                        if (r['type'] == 'Renewal' && r['hasProof'] != true)
                          TextButton(
                            onPressed: () => openPage(
                              context,
                              NativeRenewalPayment(
                                auth: auth,
                                id: textOf(r['id']),
                              ),
                            ),
                            child: const UiText('استكمال طلب التجديد'),
                          ),
                        if (r['type'] == 'Renewal' && r['hasProof'] == true)
                          TextButton(
                            onPressed: () => openPage(
                              context,
                              NativeRenewalProofReview(
                                auth: auth,
                                id: textOf(r['id']),
                              ),
                            ),
                            child: const UiText('مراجعة إثبات التجديد'),
                          ),
                        if (r['type'] == 'FamilyRenewal')
                          TextButton(
                            onPressed: () => openPage(
                              context,
                              NativeFamilyRenewalRequest(
                                auth: auth,
                                id: textOf(r['id']),
                                staff: true,
                              ),
                            ),
                            child: UiText(
                              localized(
                                context,
                                'تفاصيل الأسرة والإثبات',
                                'Family details and proof',
                              ),
                            ),
                          ),
                        if (r['type'] == 'Product')
                          TextButton(
                            onPressed: () => openPage(
                              context,
                              NativeProductOrderReview(
                                auth: auth,
                                id: textOf(r['id']),
                              ),
                            ),
                            child: const UiText('عرض طلب المنتج ومراجعته'),
                          ),
                        UiText(money(r['amount'], r['currency'])),
                        UiText(
                          localized(
                            context,
                            r['hasProof'] == true
                                ? 'وصل إثبات الدفع'
                                : 'لم يصل إثبات دفع',
                            r['hasProof'] == true
                                ? 'Payment proof received'
                                : 'No payment proof yet',
                          ),
                        ),
                      ],
                    ),
                  ),
              ]);
            },
          ),
        );
}
