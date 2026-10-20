import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import 'page_video.dart';
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
import '../owner/home_destinations.dart';
import 'academy_news.dart';
import 'community_ads.dart';
import 'matches.dart';
import 'staff_amounts.dart';
import 'staff_attendance_exceptions.dart';
import 'staff_work_periods.dart';
import 'staff_passkeys.dart';
import 'native_staff_attendance.dart';
import 'coach_agreements.dart';
import 'facility_board.dart';
import 'role_agenda.dart';
import 'help_center.dart';
import 'assigned_followups.dart';
import 'player_roster_status.dart';
import 'content_editor.dart';

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
  @override
  Widget build(BuildContext context) {
    final guardian = widget.role == 'Guardian',
        owner = widget.role == 'AcademyOwner',
        admin = widget.role == 'AcademyAdmin';
    final destinations = guardian
        ? [
            ('الرئيسية', 'Home', Icons.home_outlined),
            ('أبنائي', 'Children', Icons.family_restroom_outlined),
            ('التقويم', 'Calendar', Icons.calendar_month_outlined),
            ('المزيد', 'More', Icons.grid_view_outlined),
          ]
        : owner
        ? [
            ('الرئيسية', 'Home', Icons.home_outlined),
            ('المباريات', 'Matches', Icons.sports_soccer_rounded),
            ('التقارير', 'Reports', Icons.analytics_outlined),
            ('المزيد', 'More', Icons.grid_view_outlined),
          ]
        : admin
        ? [
            ('الرئيسية', 'Home', Icons.home_outlined),
            ('الحضور', 'Attendance', Icons.fact_check_outlined),
            ('الطلبات', 'Requests', Icons.receipt_long_outlined),
            ('المزيد', 'More', Icons.grid_view_outlined),
          ]
        : [
            ('الرئيسية', 'Home', Icons.home_outlined),
            ('جدولي', 'Schedule', Icons.calendar_month_outlined),
            ('اللاعبون', 'Players', Icons.groups_outlined),
            ('المباريات', 'Matches', Icons.sports_soccer_rounded),
            ('المزيد', 'More', Icons.grid_view_outlined),
          ];
    final Widget page = selected == 0
        ? widget.home
        : selected == destinations.length - 1
        ? NativeMore(
            auth: widget.auth,
            role: widget.role,
            account: widget.account,
            environment: widget.environment,
          )
        : guardian
        ? selected == 2
              ? RoleAgenda(auth: widget.auth, guardian: true)
              : GuardianHub(
                  auth: widget.auth,
                  section: selected == 1 ? 'children' : 'schedule',
                  environment: widget.environment,
                )
        : owner
        ? selected == 1
              ? MatchSchedule(auth: widget.auth)
              : OwnerReports(auth: widget.auth)
        : admin
        ? selected == 1
              ? NativeSessions(auth: widget.auth)
              : NativePaymentRequests(auth: widget.auth)
        : selected == 1
        ? RoleAgenda(auth: widget.auth, guardian: false)
        : selected == 3
        ? MatchSchedule(auth: widget.auth)
        : CoachPlayers(auth: widget.auth);
    return Scaffold(
      body: KeyedSubtree(
        key: ValueKey('$selected-${widget.role}'),
        child: page,
      ),
      bottomNavigationBar: NavigationBar(
        height: 72,
        indicatorColor: owner && selected == 1 ? const Color(0x2449D6A0) : null,
        selectedIndex: selected,
        onDestinationSelected: (i) => setState(() => selected = i),
        labelBehavior: NavigationDestinationLabelBehavior.alwaysShow,
        destinations: [
          for (final d in destinations)
            NavigationDestination(
              icon: Icon(
                d.$3,
                color: owner && d.$1 == 'المباريات'
                    ? const Color(0xFF49D6A0)
                    : null,
              ),
              selectedIcon: Icon(
                d.$3,
                color: owner && d.$1 == 'المباريات'
                    ? const Color(0xFF49D6A0)
                    : Colors.white,
              ),
              label: localized(context, d.$1, d.$2),
            ),
        ],
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
      HelpEntry(auth: auth),
      GLink(
        'فيديوهات شرح الصفحات',
        Icons.play_circle_outline_rounded,
        () => openPage(context, PageVideoLibrary(auth: auth)),
      ),
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
          'الإعلانات',
          Icons.campaign_outlined,
          () => openPage(context, OwnerCommunityCampaigns(auth: auth)),
        ),
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
          localized(context, 'فترات عملي', 'My work periods'),
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
          localized(context, 'طلب مبلغ إضافي', 'Request extra amount'),
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
      GLink(
        localized(context, 'الأخبار', 'News'),
        Icons.newspaper_outlined,
        () => openPage(
          context,
          role == 'AcademyOwner'
              ? AcademyNewsManagement(auth: auth)
              : AcademyNewsPage(auth: auth),
        ),
      ),
      if (role != 'AcademyOwner')
        GLink(
          localized(context, 'الحساب والإعدادات', 'Account & settings'),
          Icons.settings_outlined,
          () => openPage(context, account),
        ),
      if (role == 'Coach' || role == 'AcademyAdmin')
        GLink(
          'حضوري وانصرافي',
          Icons.fingerprint,
          () => openPage(context, NativeStaffAttendancePage(auth: auth)),
        ),
      if (role == 'Coach' || role == 'AcademyAdmin')
        GLink(
          localized(
            context,
            'وسائل التحقق الخاصة بي',
            'My verification methods',
          ),
          Icons.security_outlined,
          () => openPage(context, StaffPasskeyPage(auth: auth)),
        ),
    ]),
  );
}

class CoachPlayers extends StatelessWidget {
  final AuthController auth;
  final bool admin;
  const CoachPlayers({super.key, required this.auth, this.admin = false});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: admin
        ? 'اللاعبون'
        : localized(context, 'لاعبو مجموعاتي', 'My groups’ players'),
    child: RemoteBody(
      auth: auth,
      path: admin ? '/api/v1/people/roster' : '/api/v1/evaluations/options',
      builder: (context, value) {
        final players = rows(obj(value)['enrollments']);
        return gList([
          for (final p in players)
            GCard(
              child: ListTile(
                contentPadding: EdgeInsets.zero,
                leading: PlayerAvatar(
                  textOf(p['player']),
                  auth: auth,
                  reference: p['photoReference'],
                  size: 48,
                ),
                title: UiText(textOf(p['player'])),
                subtitle: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    UiText(textOf(p['group'] ?? p['sport'])),
                    PlayerRosterStatus(player: p),
                  ],
                ),
                trailing: const Icon(Icons.chevron_left),
                onTap: () => openPage(
                  context,
                  admin
                      ? StaffPlayerRead(auth: auth, id: textOf(p['playerId']))
                      : CoachPlayerPage(
                          auth: auth,
                          enrollmentId: textOf(p['id']),
                          groupId: textOf(p['groupId']),
                        ),
                ),
              ),
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
                            )
                          : section == 'nutrition'
                          ? NutritionLibrary(
                              auth: auth,
                              playerId: c['id'],
                              today: d['today'],
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
            GLink(
              textOf(g['arabicName']),
              Icons.groups_outlined,
              () => openPage(context, CoachGroup(auth: auth, group: g)),
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
  Widget build(BuildContext context) => GuardianPage(
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
              'اعتماد الطلبات وإسناد المجموعة متاحان من لوحة الويب.',
              'Approve requests and assign groups from the web dashboard.',
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
