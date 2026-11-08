import '../shared/group_schedule.dart';

import 'package:academy_mobile/core/theme/ui_text.dart';
import 'package:flutter/material.dart';

import '../../core/theme/appearance.dart';

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
import 'guardian_child_actions.dart';

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
  Widget subscription(Json e, Json player) => GCard(
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        UiText('${e['sport']} · ${e['group']}'),
        UiText(trainingRegistrationLabel(e['registrationFormat'])),
        if (e['period'] is Map) ...[
          StatusPill(e['period']['status']),
          UiText(textOf(e['period']['plan'])),
          UiText(
            '${day(e['period']['startDate'])} — ${day(e['period']['endDate'])}',
            textDirection: TextDirection.ltr,
          ),
          if (e['period']['remainingSessions'] != null)
            UiText('الحصص المتبقية: ${e['period']['remainingSessions']}'),
          TextButton(
            onPressed: () => openPage(
              context,
              PeriodPage(auth: auth, id: e['period']['id']),
            ),
            child: const UiText('تفاصيل حالة الاشتراك'),
          ),
        ],
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
          child: const UiText('تجديد الاشتراك'),
        ),
      ],
    ),
  );

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
                  if (tab == 'card') ...[
                    const SizedBox(height: 8),
                    GuardianChildActions(
                      auth: auth,
                      playerId: playerId,
                      playerName: textOf(player['arabicName']),
                      nutritionDate: widget.nutritionDate ?? today,
                      enrollmentId: selected?['id']?.toString(),
                      nextSession: rows(selected?['upcoming'] ?? <Json>[])
                          .firstOrNull,
                    ),
                  ],
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
                                UiText(
                                  childContextLine(player, selected, today),
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                  style: Theme.of(context).textTheme.bodySmall,
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
                    if (rows(data['compensations'] ?? <Json>[]).isNotEmpty) ...[
                      const Heading('تعويضات اللاعب المعتمدة'),
                      for (final c in rows(data['compensations']))
                        CompensationCard(data: c),
                    ],
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
                      ReceiptsPage(auth: auth),
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
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: section == 'schedule' ? 'التدريب والمواعيد' : 'الحضور',
    child: RemoteBody(
      auth: auth,
      path: '/api/v1/guardian/children/$playerId/profile',
      builder: (context, value) {
        final d = obj(value);
        return gList(
          section == 'schedule'
              ? [
                  for (final e in rows(d['enrollments']).where(
                    (e) => enrollmentId == null || e['id'] == enrollmentId,
                  )) ...[
                    Heading(textOf(e['sport'])),
                    UiText(trainingRegistrationLabel(e['registrationFormat'])),
                    UiText('${e['branch']} · ${e['group']}'),
                    WeeklyGroupSchedule(
                      schedules: rows(e['recurring'] ?? <Json>[]),
                    ),
                    for (final transfer
                        in e['transfers'] is List
                            ? rows(e['transfers'])
                            : <Json>[])
                      GroupTransferNotice(
                        group: textOf(transfer['group']),
                        date: day(transfer['effectiveDate']),
                      ),
                    for (final move in rows(e['sessionMoves']))
                      GroupTransferNotice(
                        group: textOf(move['group']),
                        date: day(move['date']),
                        occurrence: true,
                      ),
                    const Heading('الجلسات القادمة'),
                    if (rows(e['upcoming']).isEmpty)
                      const EmptyMessage('لا توجد جلسات قادمة منشورة.'),
                    for (final s in rows(e['upcoming']))
                      GCard(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            UiText(textOf(s['group'] ?? e['group'])),
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
                  ],
                ]
              : [
                  Facts([
                    ('حاضر', textOf(d['attendanceSummary']['present'])),
                    ('غائب', textOf(d['attendanceSummary']['absent'])),
                  ]),
                  if (rows(d['attendance']).isEmpty) const EmptyMessage(),
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
      builder: (context, value) {
        final d = obj(value);
        return gList([
          Heading(textOf(d['plan'])),
          StatusPill(d['status']),
          if (d['origin'] == 'ApprovedWaiver')
            const UiText('تدريب مجاني بإعفاء معتمد'),
          UiText('${day(d['startDate'])} — ${day(d['endDate'])}'),
          if (d['frozenFromDate'] != null)
            UiText('مجمد من ${day(d['frozenFromDate'])}'),
          if (d['remainingSessions'] != null)
            UiText('الحصص المتبقية: ${d['remainingSessions']}'),
          const Heading('سجل التعديلات'),
          if (rows(d['adjustments']).isEmpty)
            const EmptyMessage('لا توجد تعديلات.'),
          for (final a in rows(d['adjustments']))
            GCard(
              child: UiText(
                '${label(a['type'])} · ${day(a['effectiveDate'])}\n${a['type'] == 'SessionsAdded' ? '+${a['sessionsDelta']} حصة · الرصيد ${a['oldRemainingSessions']} ← ${a['newRemainingSessions']}\n' : ''}نهاية الاشتراك: ${day(a['newEndDate'])}',
              ),
            ),
        ]);
      },
    ),
  );
}
