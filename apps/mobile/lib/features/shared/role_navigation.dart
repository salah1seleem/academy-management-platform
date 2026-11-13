import 'role_home_navigation.dart';
import '../guardian/guardian_progress.dart';
import 'directory_cards.dart' show PlayerDirectoryCard;
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

import '../../core/api/api_client.dart';
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
import '../admin/native_enrollment_payment_read.dart';
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
import 'staff_capabilities.dart';

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
            ('المجموعات', 'Groups', Icons.groups_outlined),
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
        ? NativeMore(auth: widget.auth, role: widget.role)
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
        ? 'groups'
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
                          label: localized(context, d.$1, d.$2),
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
  const NativeMore({super.key, required this.auth, required this.role});
  final AuthController auth;
  final String role;

  @override
  Widget build(BuildContext context) {
    final owner = role == 'AcademyOwner';
    final admin = role == 'AcademyAdmin';
    final coach = role == 'Coach';
    return GuardianPage(
      title: localized(context, 'المزيد', 'More'),
      child: gList([
        if (owner) ...[
          Heading(
            localized(context, 'إدارة الأكاديمية', 'Academy administration'),
          ),
          _moreLink(
            context,
            'الموظفون والصلاحيات',
            Icons.badge_outlined,
            NativeStaffManagement(auth: auth),
          ),
          _publishingLink(context, auth),
          _moreLink(
            context,
            localized(context, 'الحملات الإعلانية', 'Ad campaigns'),
            Icons.campaign_outlined,
            OwnerCommunityCampaigns(auth: auth),
          ),
          Heading(
            localized(context, 'التشغيل والمتابعة', 'Operations & follow-up'),
          ),
          CoachTasksEntry(auth: auth),
          AttendanceCorrectionsEntry(auth: auth),
          NativeMatchEntry(auth: auth, asLink: true),
          NativeProductOrdersEntry(auth: auth),
          FacilityBoardLink(auth: auth),
        ],
        if (coach) ...[
          Heading(localized(context, 'الفريق', 'Team')),
          CoachTasksEntry(auth: auth),
          _moreLink(
            context,
            'نجوم فريقي الشهرية',
            Icons.star_outline_rounded,
            CoachMonthlyStars(auth: auth),
          ),
          NativeMatchEntry(auth: auth, asLink: true),
        ],
        if (admin)
          ListenableBuilder(
            listenable: auth,
            builder: (context, _) {
              final session = auth.session, member = session?.selected;
              if (member == null ||
                  member.role != 'AcademyAdmin' ||
                  auth.busy) {
                return const SizedBox.shrink();
              }
              return _AdminMoreTools(
                key: ValueKey((
                  auth,
                  session?.userId,
                  member.academyId,
                  member.id,
                  member.contextRevision,
                )),
                auth: auth,
              );
            },
          ),
        if (admin || coach) ...[
          Heading(localized(context, 'دوامي ومستحقاتي', 'My work & pay')),
          _moreLink(
            context,
            localized(context, 'سجل دوامي', 'My attendance history'),
            Icons.access_time_outlined,
            StaffWorkPeriodsPage(auth: auth),
          ),
          _moreLink(
            context,
            localized(context, 'طلب إثبات حضوري', 'My attendance claim'),
            Icons.fact_check_outlined,
            StaffAttendanceExceptionPage(auth: auth),
          ),
          _moreLink(
            context,
            localized(
              context,
              'السلف والمبالغ الإضافية',
              'Advances & extra amounts',
            ),
            Icons.add_card_outlined,
            StaffAmounts(auth: auth),
          ),
          if (coach)
            _moreLink(
              context,
              localized(
                context,
                'اتفاقات أجر الخاص',
                'Private coaching agreements',
              ),
              Icons.receipt_long_outlined,
              CoachAgreementsPage(auth: auth),
            ),
        ],
        Heading(localized(context, 'التفضيلات', 'Preferences')),
        if (owner)
          _moreLink(
            context,
            'إعدادات الرئيسية',
            Icons.dashboard_customize_outlined,
            OwnerHomeSettingsPage(auth: auth),
          ),
        MobilePushToggle(auth: auth),
        if (!coach) HelpEntry(auth: auth),
      ]),
    );
  }
}

GLink _moreLink(
  BuildContext context,
  String label,
  IconData icon,
  Widget page,
) => GLink(label, icon, () => openPage(context, page));

GLink _publishingLink(BuildContext context, AuthController auth) => _moreLink(
  context,
  'الأخبار والفعاليات والإعلانات',
  Icons.newspaper_outlined,
  NativeAcademyContent(auth: auth),
);

class _AdminMoreTools extends StatefulWidget {
  const _AdminMoreTools({super.key, required this.auth});
  final AuthController auth;

  @override
  State<_AdminMoreTools> createState() => _AdminMoreToolsState();
}

class _AdminMoreToolsState extends State<_AdminMoreTools>
    with AutomaticKeepAliveClientMixin<_AdminMoreTools> {
  late var staffAccess = _read(StaffCapabilities.load(widget.auth));
  Future<bool?>? matchAccess;

  @override
  bool get wantKeepAlive => true;

  Future<T?> _read<T>(Future<T> request) async {
    try {
      return await request;
    } on ApiFailure catch (error) {
      if (error.status == 401 || error.status == 403) return null;
      rethrow;
    }
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final matches =
        BrandScope.maybeOf(context)?.features.contains('matches') ?? true;
    if (matches) {
      matchAccess ??= _read(NativeMatchEntry.canRead(widget.auth));
    } else {
      matchAccess = null;
    }
  }

  @override
  Widget build(BuildContext context) {
    super.build(context);
    return FutureBuilder<StaffCapabilities?>(
      future: staffAccess,
      builder: (context, staff) => FutureBuilder<bool?>(
        future: matchAccess,
        builder: (context, match) {
          final access = staff.connectionState == ConnectionState.done
              ? staff.data
              : null;
          final hasMatches =
              matchAccess != null &&
              match.connectionState == ConnectionState.done &&
              match.data == true;
          final loading =
              staff.connectionState == ConnectionState.waiting ||
              match.connectionState == ConnectionState.waiting;
          final staffFailed =
              staff.connectionState == ConnectionState.done && staff.hasError;
          final matchFailed =
              matchAccess != null &&
              match.connectionState == ConnectionState.done &&
              match.hasError;
          final failed = staffFailed || matchFailed;
          final auth = widget.auth;
          final links = [
            if (access != null) ...[
              if (access.permissions.contains('followups.read'))
                _moreLink(
                  context,
                  'متابعاتي المسندة',
                  Icons.task_alt,
                  AssignedFollowUpsPage(auth: auth),
                ),
              if (canAssignCoachTasks(access))
                _moreLink(
                  context,
                  'مهام الحضور والتقييم',
                  Icons.task_alt,
                  CoachTasksPage(auth: auth),
                ),
              if (access.permissions.contains('staff.provision'))
                _moreLink(
                  context,
                  localized(
                    context,
                    'مراجعة إثبات حضور الموظفين',
                    'Review staff attendance claims',
                  ),
                  Icons.rule_outlined,
                  StaffAttendanceExceptionPage(auth: auth, review: true),
                ),
              if (access.canReadAttendanceCorrections)
                _moreLink(
                  context,
                  'طلبات تصحيح الحضور',
                  Icons.fact_check_outlined,
                  AttendanceCorrectionsPage(auth: auth),
                ),
            ],
            if (hasMatches) NativeMatchEntry.link(context, auth),
            if (access != null) ...[
              if (access.canReadProductOrders)
                _moreLink(
                  context,
                  'طلبات المنتجات',
                  Icons.shopping_bag_outlined,
                  NativeProductOrders(auth: auth),
                ),
              if (access.permissions.contains('facilities.read'))
                _moreLink(
                  context,
                  localized(context, 'المرافق والحارات', 'Facilities & lanes'),
                  Icons.pool_outlined,
                  FacilityBoardPage(auth: auth),
                ),
              if (access.scope['useActionGrants'] == true
                  ? access.explicitAction('news', 'Read')
                  : access.permissions.contains('content.media.manage'))
                _publishingLink(context, auth),
            ],
          ];
          if (links.isEmpty && !loading && !failed) {
            return const SizedBox.shrink();
          }
          return Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Heading(localized(context, 'مهام الأكاديمية', 'Academy work')),
              ...links,
              if (loading)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 10),
                  child: LinearProgressIndicator(
                    semanticsLabel: localized(
                      context,
                      'جارٍ تحميل أدوات الإدارة',
                      'Loading administration tools',
                    ),
                  ),
                ),
              if (failed)
                TextButton.icon(
                  onPressed: () => setState(() {
                    if (staffFailed) {
                      staffAccess = _read(StaffCapabilities.load(auth));
                    }
                    if (matchFailed) {
                      matchAccess = _read(NativeMatchEntry.canRead(auth));
                    }
                  }),
                  icon: const Icon(Icons.refresh_rounded),
                  label: UiText(
                    localized(
                      context,
                      'تعذر تحميل بعض الأدوات · إعادة المحاولة',
                      'Retry unavailable tools',
                    ),
                  ),
                ),
            ],
          );
        },
      ),
    );
  }
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
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/coach/groups',
      builder: (context, value) {
        final groups = rows(value);
        return gList([
          for (final g in groups) CoachGroupCard(auth: auth, group: g),
          if (groups.isEmpty)
            EmptyMessage(
              localized(
                context,
                'لا توجد مجموعات مسندة إليك.',
                'No groups are assigned to you.',
              ),
            ),
        ]);
      },
    ),
  );
}

class NativePaymentRequests extends StatefulWidget {
  final AuthController auth;
  final Map<String, String> filters;
  const NativePaymentRequests({
    super.key,
    required this.auth,
    this.filters = const {},
  });
  @override
  State<NativePaymentRequests> createState() => _NativePaymentRequestsState();
}

class _NativePaymentRequestsState extends State<NativePaymentRequests> {
  int revision = 0;
  final referenceInput = TextEditingController();
  String reference = '';
  String? referenceError;
  late final openedContext = contextKey;
  static final referencePattern = RegExp(
    r'^(?:[a-f0-9]{12}|[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12})$',
    caseSensitive: false,
  );
  AuthController get auth => widget.auth;
  Map<String, String> get filters => widget.filters;
  String get path => Uri(
    path: '/api/v1/manage/payment-requests',
    queryParameters: {
      ...filters,
      if (filters['from']?.isNotEmpty != true &&
          filters['to']?.isNotEmpty != true)
        'allDates': 'true',
      'status': 'Pending',
      if (reference.isNotEmpty) 'reference': reference,
      'page': '1',
      'pageSize': '25',
    },
  ).toString();

  String? get contextKey {
    final selected = auth.session?.selected;
    return selected == null
        ? null
        : '${auth.session?.userId}:${selected.academyId}:${selected.id}:${selected.contextRevision}';
  }

  @override
  void initState() {
    super.initState();
    openedContext;
  }

  @override
  void dispose() {
    referenceInput.dispose();
    super.dispose();
  }

  void search(String raw) {
    FocusManager.instance.primaryFocus?.unfocus();
    final term = raw.trim();
    if (term.isNotEmpty && !referencePattern.hasMatch(term)) {
      setState(
        () => referenceError = 'أدخل رقم الطلب كاملًا كما يظهر في تفاصيله.',
      );
      return;
    }
    referenceInput.text = term;
    setState(() {
      referenceError = null;
      reference = term;
      revision++;
    });
  }

  Future<void> open(Widget page) async {
    final expected = contextKey;
    await openPage(context, page);
    if (mounted && expected != null && expected == contextKey) {
      setState(() => revision++);
    }
  }

  @override
  Widget build(BuildContext context) => filters['type'] == 'Product'
      ? NativeProductOrders(auth: auth, filters: filters)
      : GuardianPage(
          title: localized(context, 'طلبات الدفع', 'Payment requests'),
          child: ListenableBuilder(
            listenable: auth,
            builder: (context, _) =>
                openedContext == null || openedContext != contextKey
                ? const SizedBox.shrink()
                : Column(
                    children: [
                      Padding(
                        padding: const EdgeInsets.fromLTRB(20, 4, 20, 4),
                        child: TextField(
                          controller: referenceInput,
                          textDirection: TextDirection.ltr,
                          style: const TextStyle(fontSize: 13),
                          textInputAction: TextInputAction.search,
                          autocorrect: false,
                          enableSuggestions: false,
                          onSubmitted: search,
                          onChanged: (_) {
                            if (referenceError != null)
                              setState(() => referenceError = null);
                          },
                          decoration: InputDecoration(
                            labelText: ui(context, 'رقم الطلب'),
                            errorText: referenceError == null
                                ? null
                                : ui(context, referenceError!),
                            errorMaxLines: 3,
                            suffixIcon: IconButton(
                              tooltip: ui(context, 'بحث'),
                              icon: const Icon(Icons.search_rounded),
                              onPressed: () => search(referenceInput.text),
                            ),
                          ),
                        ),
                      ),
                      if (reference.isNotEmpty)
                        TextButton(
                          onPressed: () => search(''),
                          child: const UiText('مسح البحث'),
                        ),
                      Expanded(
                        child: RemoteBody(
                          key: ValueKey(revision),
                          auth: auth,
                          path: path,
                          builder: (context, value) {
                            final d = obj(value), items = rows(d['items']);
                            final owner =
                                auth.session?.selected?.role == 'AcademyOwner';
                            final reviewProof =
                                d['canReviewRenewalProof'] as bool? ?? owner;
                            final webReviewTypes = <String>{
                              for (final row in items)
                                if (row['canOpen'] != false)
                                  if (row['type'] == 'Enrollment')
                                    'الاشتراكات الجديدة'
                                  else if (row['type'] == 'Tournament')
                                    'رسوم البطولات',
                            };
                            final total = switch (filters['type']) {
                              'Renewal' => d['renewals'],
                              'Enrollment' => d['newEnrollments'],
                              'Tournament' => d['tournaments'],
                              _ => d['pending'],
                            };
                            final count = total is num
                                ? total.toInt()
                                : items.length;
                            final matches =
                                (d['total'] as num?)?.toInt() ?? items.length;
                            return gList([
                              if (items.isEmpty)
                                EmptyMessage(
                                  reference.isEmpty
                                      ? 'لا توجد طلبات دفع معلقة ضمن النطاق المختار.'
                                      : 'لا توجد طلبات معلقة بهذا الرقم ضمن النطاق المختار.',
                                )
                              else
                                Heading(
                                  reference.isNotEmpty
                                      ? localized(
                                          context,
                                          'نتائج البحث ($matches)',
                                          'Search results ($matches)',
                                        )
                                      : localized(
                                          context,
                                          'طلبات معلّقة ($count)',
                                          'Pending requests ($count)',
                                        ),
                                ),
                              if (webReviewTypes.isNotEmpty)
                                UiText(
                                  'اعتماد ${webReviewTypes.join(' و')} من لوحة التحكم.',
                                  style: Theme.of(context).textTheme.bodySmall,
                                ),
                              for (final r in items)
                                GCard(
                                  key: ValueKey(
                                    'payment-request:${r['type']}:${r['id']}',
                                  ),
                                  child: Column(
                                    crossAxisAlignment:
                                        CrossAxisAlignment.start,
                                    children: [
                                      UiText(
                                        textOf(r['player']),
                                        style: Theme.of(context)
                                            .textTheme
                                            .titleMedium,
                                      ),
                                      UiText('${r['sport']} · ${r['branch']}'),
                                      UiText(textOf(r['plan'])),
                                      PaymentRequestReference(order: r),
                                      if (r['canOpen'] != false &&
                                          r['type'] == 'Renewal' &&
                                          r['hasProof'] != true)
                                        TextButton(
                                          onPressed: () => open(
                                            NativeRenewalPayment(
                                              auth: auth,
                                              id: textOf(r['id']),
                                            ),
                                          ),
                                          child: UiText(
                                            owner
                                                ? 'استكمال طلب التجديد'
                                                : 'عرض طلب التجديد',
                                          ),
                                        ),
                                      if (r['canOpen'] != false &&
                                          r['type'] == 'Renewal' &&
                                          r['hasProof'] == true)
                                        TextButton(
                                          onPressed: () => open(
                                            NativeRenewalProofReview(
                                              auth: auth,
                                              id: textOf(r['id']),
                                              fromPaymentQueue: true,
                                            ),
                                          ),
                                          child: UiText(
                                            reviewProof
                                                ? 'مراجعة إثبات التجديد'
                                                : 'عرض إثبات التجديد',
                                          ),
                                        ),
                                      if (r['canOpen'] != false &&
                                          r['type'] == 'FamilyRenewal')
                                        TextButton(
                                          onPressed: () => open(
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
                                      if (r['canOpen'] != false &&
                                          r['type'] == 'Product')
                                        TextButton(
                                          onPressed: () => open(
                                            NativeProductOrderReview(
                                              auth: auth,
                                              id: textOf(r['id']),
                                            ),
                                          ),
                                          child: const UiText('عرض طلب المنتج'),
                                        ),
                                      if (r['type'] == 'Enrollment')
                                        const UiText('اشتراك جديد'),
                                      if (r['canOpen'] == true &&
                                          r['type'] == 'Enrollment')
                                        TextButton(
                                          onPressed: () => open(
                                            NativeEnrollmentPaymentRead(
                                              auth: auth,
                                              id: textOf(r['id']),
                                            ),
                                          ),
                                          child: const UiText(
                                            'عرض طلب الاشتراك',
                                          ),
                                        ),
                                      if (r['type'] == 'Tournament')
                                        const UiText('رسم بطولة'),
                                      UiText(
                                        r['amount'] is num &&
                                                (r['amount'] as num).isFinite &&
                                                r['currency'] is String &&
                                                (r['currency'] as String)
                                                    .isNotEmpty
                                            ? money(r['amount'], r['currency'])
                                            : localized(
                                                context,
                                                'المبلغ غير محدد',
                                                'Amount not set',
                                              ),
                                      ),
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
                      ),
                    ],
                  ),
          ),
        );
}
