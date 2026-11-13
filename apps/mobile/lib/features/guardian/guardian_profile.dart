import '../shared/group_schedule.dart';

import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart' show DateFormat;

import '../../core/theme/appearance.dart';
import '../../core/widgets/page_status.dart';

import '../../core/auth/auth_controller.dart';
import 'guardian_ui.dart';
import 'guardian_report.dart';
import 'guardian_content.dart';
import 'guardian_payments.dart';
import 'group_transfer_notice.dart';
import 'compensation_card.dart';
import 'player_card.dart';
import 'guardian_visits.dart';
import 'coach_feedback.dart';
import '../shared/matches.dart';
import '../shared/player_skills.dart';
import '../shared/player_certificates.dart';
import '../shared/player_badges.dart';
import '../shared/player_monthly.dart';
import '../shared/player_goals.dart';

import 'guardian_profile_header.dart';
import 'daily_nutrition.dart';

String _subscriptionDateLabel(Object? value) {
  final date = DateTime.tryParse(day(value));
  if (date == null) return 'تاريخ غير متاح';
  final format = DateFormat.yMMMd('ar')..useNativeDigits = false;
  return format.format(date);
}

class ChildProfile extends StatefulWidget {
  final AuthController auth;
  final String environment, playerId, today;
  final String? initialEnrollmentId, nutritionDate;
  const ChildProfile({
    super.key,
    required this.auth,
    required this.environment,
    required this.playerId,
    required this.today,
    this.initialEnrollmentId,
    this.nutritionDate,
  });
  @override
  State<ChildProfile> createState() => _ChildProfileState();
}

class _ChildProfileState extends State<ChildProfile> {
  final scroll = ScrollController();
  String tab = 'card';
  late String? enrollmentId = widget.initialEnrollmentId;
  bool invalidated = false;
  String? get currentIdentity => auth.session?.selected == null
      ? null
      : '${auth.session!.selected!.academyId}:${auth.session!.selected!.id}:${auth.session!.selected!.role}:${auth.session!.selected!.contextRevision}';
  late final openedIdentity = currentIdentity;
  @override
  void initState() {
    super.initState();
    openedIdentity;
    auth.addListener(changedIdentity);
  }

  void changedIdentity() {
    if (!mounted) return;
    if (currentIdentity != openedIdentity) invalidated = true;
    setState(() {});
  }

  AuthController get auth => widget.auth;
  String get playerId => widget.playerId;
  String get today => widget.today;
  String get environment => widget.environment;
  @override
  void didUpdateWidget(covariant ChildProfile oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.auth != widget.auth) {
      oldWidget.auth.removeListener(changedIdentity);
      auth.addListener(changedIdentity);
      invalidated = true;
    }
    if (oldWidget.playerId != playerId ||
        oldWidget.initialEnrollmentId != widget.initialEnrollmentId) {
      enrollmentId = widget.initialEnrollmentId;
      tab = 'card';
    }
  }

  @override
  void dispose() {
    auth.removeListener(changedIdentity);
    scroll.dispose();
    super.dispose();
  }

  void choose({String? section, String? enrollment}) {
    setState(() {
      if (section != null) tab = section;
      if (enrollment != null) enrollmentId = enrollment;
    });
    if (scroll.hasClients) scroll.jumpTo(0);
  }

  Widget link(String title, IconData icon, Widget page) =>
      GLink(title, icon, () => openPage(context, page));
  Widget subscription(Json e, Json player) {
    final period = e['period'] is Json
        ? obj(e['period'])
        : const <String, dynamic>{};
    final next = e['nextPeriod'] is Json
        ? obj(e['nextPeriod'])
        : const <String, dynamic>{};
    final periodId = period['id'];
    final nextId = next['id'];
    return GCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          UiText('${e['sport']} · ${e['group']}'),
          UiText(trainingRegistrationLabel(e['registrationFormat'])),
          if (periodId is String) ...[
            const SizedBox(height: 8),
            StatusPill(period['status']),
            UiText(textOf(period['plan'])),
            UiText(
              'بداية الاشتراك ${_subscriptionDateLabel(period['startDate'])}',
            ),
            if (period['endDate'] != null)
              UiText(
                'نهاية الاشتراك ${_subscriptionDateLabel(period['endDate'])}',
              ),
            if (period['remainingSessions'] != null)
              UiText('الحصص المتبقية ${period['remainingSessions']}'),
            TextButton(
              onPressed: () =>
                  openPage(context, PeriodPage(auth: auth, id: periodId)),
              child: const UiText('تفاصيل حالة الاشتراك'),
            ),
          ] else
            const UiText('لم يتم تفعيل اشتراك بعد'),
          if (nextId is String && nextId != periodId) ...[
            const Divider(),
            UiText(
              'الفترة التالية تبدأ ${_subscriptionDateLabel(next['startDate'])}',
            ),
            TextButton(
              onPressed: () =>
                  openPage(context, PeriodPage(auth: auth, id: nextId)),
              child: const UiText('تفاصيل الفترة التالية'),
            ),
          ],
          const SizedBox(height: 8),
          FilledButton(
            onPressed: () => openPage(
              context,
              PlanPicker(
                auth: auth,
                environment: environment,
                enrollmentId: e['id'],
                childName: player['arabicName'],
              ),
            ),
            child: UiText(
              periodId is String ? 'تجديد الاشتراك' : 'اختيار باقة والاشتراك',
            ),
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) =>
      invalidated || openedIdentity == null || currentIdentity != openedIdentity
      ? const SizedBox.shrink()
      : GuardianPage(
          title: 'ملف اللاعب',
          child: RemoteBody(
            auth: auth,
            path: '/api/v1/guardian/children/$playerId/profile',
            builder: (context, value) {
              final data = obj(value), player = obj(data['player']);
              final enrollments = rows(data['enrollments']);
              final selected =
                  enrollments
                      .where((e) => e['id'] == enrollmentId)
                      .firstOrNull ??
                  enrollments.firstOrNull;
              final stats = rows(data['performances'] ?? <Json>[])
                  .where((p) => p['enrollmentId'] == selected?['id'])
                  .firstOrNull;
              final performance = stats?['latest'] is Map
                  ? obj(stats!['latest'])
                  : <String, dynamic>{};
              final criteria = rows(performance['criteria'] ?? <Json>[])
                  .where((c) => validAssessment(c['value']))
                  .toList();
              final attendance = rows(data['attendance'] ?? <Json>[])
                  .where((a) => a['sportEnrollmentId'] == selected?['id'])
                  .toList();
              return ListView(
                padding: const EdgeInsets.fromLTRB(16, 14, 16, 28),
                physics: const AlwaysScrollableScrollPhysics(),
                controller: scroll,
                children: [
                  if (tab == 'card')
                    GuardianProfileHeader(
                      auth: auth,
                      player: player,
                      today: today,
                      enrollment: selected,
                      performance: performance,
                      evaluationCount: stats?['evaluationCount'] as int?,
                      monthlyStars: data['monthlyStars'] as int?,
                    ),
                  if (tab != 'card')
                    GCard(
                      key: const ValueKey('profile-compact-identity'),
                      child: Row(
                        children: [
                          PlayerAvatar(
                            textOf(player['arabicName']),
                            auth: auth,
                            reference: player['photoReference'],
                            gender: player['gender'],
                            size: 48,
                          ),
                          const SizedBox(width: 12),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                UiText(
                                  textOf(player['arabicName']),
                                  style: const TextStyle(
                                    fontSize: 17,
                                    fontWeight: FontWeight.w800,
                                  ),
                                ),
                                if (selected != null)
                                  UiText(
                                    '${selected['sport']} · ${selected['branch']}',
                                    style: Theme.of(context)
                                        .textTheme
                                        .bodySmall,
                                  ),
                                if (selected?['group'] != null)
                                  UiText(
                                    textOf(selected!['group']),
                                    style: Theme.of(context)
                                        .textTheme
                                        .bodySmall,
                                  ),
                              ],
                            ),
                          ),
                        ],
                      ),
                    ),
                  if (enrollments.length > 1) ...[
                    const SizedBox(height: 12),
                    SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: Row(
                        children: [
                          for (final e in enrollments)
                            Padding(
                              padding: const EdgeInsetsDirectional.only(end: 6),
                              child: OutlinedButton(
                                key: ValueKey('profile-enrollment-${e['id']}'),
                                onPressed: () =>
                                    choose(enrollment: textOf(e['id'])),
                                style: OutlinedButton.styleFrom(
                                  backgroundColor: e['id'] == selected?['id']
                                      ? Theme.of(context)
                                            .colorScheme
                                            .primaryContainer
                                      : null,
                                ),
                                child: UiText(
                                  textOf(e['sport']),
                                  style: const TextStyle(fontSize: 12),
                                ),
                              ),
                            ),
                        ],
                      ),
                    ),
                  ],
                  const SizedBox(height: 12),
                  Container(
                    decoration: BoxDecoration(
                      border: Border(
                        bottom: BorderSide(
                          color: Theme.of(context).dividerColor,
                        ),
                      ),
                    ),
                    child: SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      child: Row(
                        children: [
                          for (final item in const [
                            ('card', 'البطاقة والتقرير'),
                            ('att', 'الحضور'),
                            ('prog', 'التطور والشارات'),
                            ('health', 'التغذية والصحة'),
                            ('media', 'الوسائط'),
                            ('more', 'المزيد'),
                          ])
                            Semantics(
                              selected: tab == item.$1,
                              child: Container(
                                margin: const EdgeInsetsDirectional.only(
                                  end: 18,
                                ),
                                decoration: BoxDecoration(
                                  border: Border(
                                    bottom: BorderSide(
                                      width: 2,
                                      color: tab == item.$1
                                          ? Theme.of(context)
                                                .colorScheme
                                                .primary
                                          : Colors.transparent,
                                    ),
                                  ),
                                ),
                                child: TextButton(
                                  key: ValueKey('profile-tab-${item.$1}'),
                                  onPressed: () => choose(section: item.$1),
                                  child: UiText(
                                    item.$2,
                                    style: TextStyle(
                                      fontSize: 13,
                                      fontWeight: tab == item.$1
                                          ? FontWeight.w600
                                          : FontWeight.w400,
                                      color: tab == item.$1
                                          ? Theme.of(context)
                                                .colorScheme
                                                .primary
                                          : Theme.of(context)
                                                .colorScheme
                                                .onSurfaceVariant,
                                    ),
                                  ),
                                ),
                              ),
                            ),
                        ],
                      ),
                    ),
                  ),
                  const SizedBox(height: 12),
                  if (tab == 'card') ...[
                    if (criteria.isEmpty)
                      const EmptyMessage(
                        'لا توجد قيم تقييم منشورة لهذه الرياضة.',
                      ),
                    for (final c in criteria)
                      Padding(
                        padding: const EdgeInsets.symmetric(vertical: 10),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            Row(
                              children: [
                                Expanded(
                                  child: UiText(
                                    textOf(c['name']),
                                    style: const TextStyle(
                                      fontSize: 14,
                                      fontWeight: FontWeight.w600,
                                    ),
                                  ),
                                ),
                                UiText(
                                  textOf(c['value']),
                                  style: const TextStyle(
                                    fontSize: 18,
                                    fontWeight: FontWeight.w600,
                                  ),
                                ),
                              ],
                            ),
                            const SizedBox(height: 5),
                            LinearProgressIndicator(
                              value: (c['value'] as num).toDouble() / 100,
                              minHeight: 3,
                            ),
                          ],
                        ),
                      ),
                    link(
                      'بطاقة اللاعب',
                      Icons.qr_code_2,
                      GuardianPlayerCard(
                        auth: auth,
                        playerId: playerId,
                        name: textOf(player['arabicName']),
                      ),
                    ),
                    link(
                      'تقرير اللاعب',
                      Icons.sports_soccer,
                      ReportHistory(auth: auth, playerId: playerId),
                    ),
                    if (selected != null)
                      link(
                        'التدريب والمواعيد',
                        Icons.calendar_month_outlined,
                        ProfileRecords(
                          auth: auth,
                          playerId: playerId,
                          section: 'schedule',
                          enrollmentId: selected['id'] as String?,
                        ),
                      ),
                    if (selected != null) subscription(selected, player),
                  ],
                  if (tab == 'att') ...[
                    const UiText(
                      'الحضور المسجل ضمن آخر 20 سجلًا للطفل؛ ليس إجمالي الشهر.',
                      style: TextStyle(fontSize: 12),
                    ),
                    if (attendance.isEmpty)
                      const EmptyMessage(
                        'لا يوجد حضور مسجل لهذه الرياضة ضمن السجل المعروض.',
                      ),
                    for (final a in attendance)
                      GCard(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            UiText(
                              sessionDateLabel(a['sessionDate']),
                              textDirection: TextDirection.rtl,
                            ),
                            UiText(textOf(a['group'])),
                            StatusPill(a['status']),
                          ],
                        ),
                      ),
                    link(
                      'سجل حضور كل الرياضات',
                      Icons.fact_check_outlined,
                      ProfileRecords(
                        auth: auth,
                        playerId: playerId,
                        section: 'attendance',
                      ),
                    ),
                    if (compensationForEnrollment(
                      data,
                      selected?['id'],
                    ).isNotEmpty)
                      link(
                        'التعويضات ومواعيد التدريب',
                        Icons.calendar_month_outlined,
                        ProfileRecords(
                          auth: auth,
                          playerId: playerId,
                          section: 'schedule',
                          enrollmentId: selected?['id'] as String?,
                        ),
                      ),
                  ],
                  if (tab == 'prog') ...[
                    const Heading('آخر التقييمات المنشورة'),
                    if (rows(stats?['history'] ?? <Json>[]).isEmpty)
                      const EmptyMessage(
                        'لا توجد تقييمات منشورة لهذه الرياضة.',
                      ),
                    for (final h in rows(stats?['history'] ?? <Json>[]))
                      GCard(
                        onTap: () => openPage(
                          context,
                          PlayerReport(
                            auth: auth,
                            id: textOf(h['evaluationId']),
                          ),
                        ),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            UiText(
                              day(h['evaluationDate']),
                              textDirection: TextDirection.ltr,
                            ),
                            UiText(
                              validAssessment(h['meanPerformance'])
                                  ? textOf(h['meanPerformance'])
                                  : 'لا توجد قيم مسجلة',
                              style: const TextStyle(fontSize: 18),
                            ),
                            if (validAssessment(h['meanPerformance']))
                              LinearProgressIndicator(
                                value:
                                    (h['meanPerformance'] as num).toDouble() /
                                    100,
                                minHeight: 3,
                              ),
                          ],
                        ),
                      ),
                    link(
                      'خطوات تعلم اللاعب',
                      Icons.auto_graph_rounded,
                      PlayerSkillsPage(auth: auth, playerId: playerId),
                    ),
                    link(
                      'أهداف التطور',
                      Icons.flag_outlined,
                      PlayerGoalsPage(auth: auth, playerId: playerId),
                    ),
                    link(
                      'إنجازات وشهادات اللاعب',
                      Icons.workspace_premium_outlined,
                      PlayerCertificatesPage(auth: auth, playerId: playerId),
                    ),
                    link(
                      'تقرير التطور الشهري',
                      Icons.auto_graph_rounded,
                      PlayerMonthlyPage(auth: auth, playerId: playerId),
                    ),
                    link(
                      'شارات المجهود والتحسن',
                      Icons.auto_awesome,
                      PlayerBadgesPage(auth: auth, playerId: playerId),
                    ),
                  ],
                  if (tab == 'health') ...[
                    DailyNutritionCard(
                      auth: auth,
                      playerId: playerId,
                      playerName: textOf(player['arabicName']),
                      today: widget.nutritionDate ?? today,
                    ),
                    link(
                      'مكتبة التغذية',
                      Icons.restaurant_outlined,
                      NutritionLibrary(
                        auth: auth,
                        playerId: playerId,
                        playerName: textOf(player['arabicName']),
                        today: widget.nutritionDate ?? today,
                      ),
                    ),
                    link(
                      'الإصابات والاستشارات الطبية',
                      Icons.health_and_safety_outlined,
                      MedicalPage(auth: auth, playerId: playerId),
                    ),
                  ],
                  if (tab == 'media') ...[
                    const UiText(
                      'وسائط الطفل المصرح بها من كل الرياضات',
                      style: TextStyle(fontSize: 12),
                    ),
                    GalleryPage(auth: auth, playerId: playerId, embedded: true),
                  ],
                  if (tab == 'more') ...[
                    link(
                      'المباريات',
                      Icons.emoji_events_outlined,
                      MatchSchedule(auth: auth, playerId: playerId),
                    ),
                    link(
                      localized(context, 'الدخول والخروج', 'Entry and exit'),
                      Icons.door_front_door_outlined,
                      GuardianVisits(auth: auth, playerId: playerId),
                    ),
                    link(
                      'تقييم المدرب أو إرسال ملاحظة',
                      Icons.rate_review_outlined,
                      CoachFeedbackPage(auth: auth, playerId: playerId),
                    ),
                    link(
                      'التدريب والمواعيد',
                      Icons.calendar_month_outlined,
                      ProfileRecords(
                        auth: auth,
                        playerId: playerId,
                        section: 'schedule',
                      ),
                    ),
                    const Heading('كل اشتراكات اللاعب'),
                    for (final e in enrollments) subscription(e, player),
                    link(
                      'إيصالاتي',
                      Icons.receipt_long,
                      ReceiptsPage(auth: auth, playerId: playerId),
                    ),
                  ],
                ],
              );
            },
          ),
        );
}

class ProfileRecords extends StatelessWidget {
  final AuthController auth;
  final String playerId, section;
  final String? enrollmentId;
  const ProfileRecords({
    super.key,
    required this.auth,
    required this.playerId,
    required this.section,
    this.enrollmentId,
  });
  List<Widget> schedule(Json e, Json profile) {
    final grants = compensationForEnrollment(profile, e['id']).toList();
    final makeups = makeupSessionIds(grants);
    final recurring = rows(e['recurring'] ?? <Json>[]);
    return [
      Heading(textOf(e['sport'])),
      UiText(trainingRegistrationLabel(e['registrationFormat'])),
      UiText('${e['branch']} · ${e['group']}'),
      for (final transfer
          in e['transfers'] is List ? rows(e['transfers']) : <Json>[])
        GroupTransferNotice(
          group: textOf(transfer['group']),
          date: day(transfer['effectiveDate']),
        ),
      for (final move in rows(e['sessionMoves'] ?? <Json>[]))
        GroupTransferNotice(
          group: textOf(move['group']),
          date: day(move['date']),
          occurrence: true,
        ),
      const Heading('الحصص القادمة'),
      if (rows(e['upcoming']).isEmpty)
        const EmptyMessage('لا توجد حصص قادمة منشورة.'),
      for (final s in rows(e['upcoming']))
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              UiText(textOf(s['group'] ?? e['group'])),
              if (makeups.contains(s['id']))
                const UiText(
                  'حصة تعويضية',
                  style: TextStyle(fontWeight: FontWeight.w600),
                ),
              UiText(
                sessionDateLabel(s['sessionDate']),
                textDirection: TextDirection.rtl,
              ),
              UiText(
                trainingTimeRange(s['startTime'], s['endTime']),
                textDirection: TextDirection.rtl,
              ),
            ],
          ),
        ),
      if (grants.isNotEmpty) ...[
        const Heading('التعويضات المعتمدة'),
        for (final grant in grants)
          CompensationCard(
            key: ValueKey(grant['id']),
            data: grant,
            today: profile['trainingToday'] as String?,
          ),
      ],
      if (recurring.isNotEmpty) WeeklyGroupSchedule(schedules: recurring),
    ];
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: section == 'schedule' ? 'التدريب والمواعيد' : 'الحضور',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/children/$playerId/profile',
      builder: (context, value) {
        final d = obj(value);
        final enrollments = rows(
          d['enrollments'],
        ).where((entry) => enrollmentId == null || entry['id'] == enrollmentId);
        return gList(
          section == 'schedule'
              ? [
                  if (enrollments.isEmpty)
                    const EmptyMessage('لا توجد مواعيد تدريب لهذا التسجيل.'),
                  for (final e in enrollments) ...schedule(e, d),
                ]
              : [
                  Facts([
                    ('حاضر', textOf(d['attendanceSummary']['present'])),
                    ('غائب', textOf(d['attendanceSummary']['absent'])),
                  ]),
                  if (rows(d['attendance']).isEmpty)
                    const EmptyMessage('لم يُسجل حضور أو غياب بعد.'),
                  for (final a in rows(d['attendance']))
                    GCard(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          UiText('${a['sport']} · ${a['group']}'),
                          UiText(sessionDateLabel(a['sessionDate'])),
                          StatusPill(a['status']),
                        ],
                      ),
                    ),
                ],
        );
      },
    ),
  );
}

class PeriodPage extends StatelessWidget {
  final AuthController auth;
  final String id;
  const PeriodPage({super.key, required this.auth, required this.id});

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'حالة الاشتراك',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/subscriptions/periods/$id/adjustments',
      notFound: const PageStatus(
        child: StatusPanel(
          message: 'هذا الاشتراك غير متاح.',
          icon: Icons.event_busy_outlined,
        ),
      ),
      builder: (context, value) {
        final d = obj(value);
        final adjustments = rows(d['adjustments']);
        return gList([
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                UiText(
                  textOf(d['plan']),
                  style: Theme.of(context).textTheme.titleLarge,
                ),
                const SizedBox(height: 8),
                StatusPill(d['status']),
                if (d['origin'] == 'ApprovedWaiver') ...[
                  const SizedBox(height: 8),
                  const UiText('تدريب مجاني بإعفاء معتمد'),
                ],
                const SizedBox(height: 12),
                UiText(
                  'بداية الاشتراك ${_subscriptionDateLabel(d['startDate'])}',
                ),
                if (d['endDate'] != null)
                  UiText(
                    'نهاية الاشتراك ${_subscriptionDateLabel(d['endDate'])}',
                  ),
                if (d['frozenFromDate'] != null)
                  UiText(
                    'مجمد من ${_subscriptionDateLabel(d['frozenFromDate'])}',
                  ),
                if (d['remainingSessions'] != null)
                  UiText('الحصص المتبقية ${d['remainingSessions']}'),
              ],
            ),
          ),
          const Heading('سجل التعديلات'),
          if (adjustments.isEmpty) const EmptyMessage('لا توجد تعديلات.'),
          for (final a in adjustments)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(
                    a['type'] == 'Cancelled'
                        ? 'إلغاء الاشتراك'
                        : label(a['type']),
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  UiText(_subscriptionDateLabel(a['effectiveDate'])),
                  if (a['sessionsDelta'] != null)
                    UiText('أضيفت ${a['sessionsDelta']} حصة'),
                  if (a['oldRemainingSessions'] != null &&
                      a['newRemainingSessions'] != null)
                    UiText(
                      'الرصيد من ${a['oldRemainingSessions']} إلى ${a['newRemainingSessions']} حصة',
                    ),
                  if (a['newEndDate'] != null)
                    UiText(
                      'نهاية الاشتراك ${_subscriptionDateLabel(a['newEndDate'])}',
                    ),
                ],
              ),
            ),
        ]);
      },
    ),
  );
}
