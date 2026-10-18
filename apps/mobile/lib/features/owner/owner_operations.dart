import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/appearance.dart';
import '../../core/theme/ui_text.dart';
import '../coach/coach_attendance.dart';
import '../guardian/guardian_ui.dart';
import '../shared/role_navigation.dart';
import '../shared/player_badges.dart';
import '../shared/player_goals.dart';
import '../shared/player_skills.dart';
import 'owner_feedback.dart';
import 'owner_followups.dart';
import '../shared/player_technical_reviews.dart';
import '../shared/admin_media_gallery.dart';

/// Hides cached operational records when the selected authorization context ends.
class OwnerScopedRead extends StatefulWidget {
  final AuthController auth;
  final String path;
  final Widget Function(BuildContext, Object?) builder;
  const OwnerScopedRead({
    super.key,
    required this.auth,
    required this.path,
    required this.builder,
  });
  @override
  State<OwnerScopedRead> createState() => _OwnerScopedReadState();
}

class _OwnerScopedReadState extends State<OwnerScopedRead> {
  String? get contextKey => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final identity = contextKey;
  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.auth,
    builder: (context, _) {
      if (identity == null || identity != contextKey) {
        return const SizedBox.shrink();
      }
      return RemoteBody(
        auth: widget.auth,
        path: widget.path,
        builder: widget.builder,
      );
    },
  );
}

class OwnerDecisionsPage extends StatefulWidget {
  final AuthController auth;
  const OwnerDecisionsPage({super.key, required this.auth});
  @override
  State<OwnerDecisionsPage> createState() => _OwnerDecisionsPageState();
}

class _OwnerDecisionsPageState extends State<OwnerDecisionsPage> {
  int revision = 0;
  String? get contextKey => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late final identity = contextKey;
  void refresh() {
    if (mounted && identity != null && identity == contextKey) {
      setState(() {
        revision++;
      });
    }
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'القرارات', 'Decisions'),
    child: OwnerScopedRead(
      key: ValueKey(revision),
      auth: widget.auth,
      path: '/api/v1/reports/owner-summary',
      builder: (context, value) {
        final d = obj(value),
            ops = d['operations'] is Map
                ? obj(d['operations'])
                : <String, dynamic>{};
        return gList([
          UiText(
            localized(
              context,
              'كل الفروع ضمن صلاحياتك',
              'All branches within your access',
            ),
          ),
          OwnerDecisionsPanel(
            auth: widget.auth,
            operations: ops,
            onReturn: refresh,
          ),
          OutlinedButton.icon(
            onPressed: () =>
                openPage(context, AdminMediaGallery(auth: widget.auth)),
            icon: const Icon(Icons.perm_media_outlined),
            label: Text(
              localized(
                context,
                'مراجعة وسحب وسائط اللاعبين',
                'Review and withdraw player media',
              ),
            ),
          ),
          OutlinedButton.icon(
            onPressed: () => openPage(
              context,
              PlayerTechnicalReviewsPage(auth: widget.auth),
            ),
            icon: const Icon(Icons.fact_check_outlined),
            label: Text(
              localized(
                context,
                'طلبات المراجعة الفنية للمستويات',
                'Level technical review requests',
              ),
            ),
          ),
          OutlinedButton.icon(
            onPressed: () async {
              await Navigator.of(context).push(
                MaterialPageRoute<void>(
                  builder: (_) => OwnerFollowUpsPage(auth: widget.auth),
                ),
              );
              refresh();
            },
            icon: const Icon(Icons.task_alt),
            label: const UiText('متابعات المالك المحفوظة'),
          ),
          if (ops.isNotEmpty)
            OwnerOperationsPanel(
              auth: widget.auth,
              onReturn: refresh,
              operations: ops,
              filters: {
                if (d['from'] != null) 'from': textOf(d['from']),
                if (d['to'] != null) 'to': textOf(d['to']),
              },
            ),
        ]);
      },
    ),
  );
}

class OwnerDecisionsPanel extends StatelessWidget {
  final AuthController auth;
  final Json operations;
  final String branchId;
  final VoidCallback? onReturn;
  const OwnerDecisionsPanel({
    super.key,
    required this.auth,
    required this.operations,
    this.branchId = '',
    this.onReturn,
  });
  @override
  Widget build(BuildContext context) {
    final groups = rows(operations['groupAttention'] ?? <Json>[]);
    final payments = (operations['pendingPaymentQueueBacklog'] as num?)
        ?.toInt();
    final overdue = (operations['overdueFeedback'] as num?)?.toInt() ?? 0;
    final complaints = (operations['openComplaints'] as num?)?.toInt() ?? 0;
    final suggestions = (operations['openSuggestions'] as num?)?.toInt() ?? 0;
    final decisions = <Widget>[
      if (payments != null && payments > 0)
        _decision(
          context,
          'راجع طلبات الدفع',
          'Review payment requests',
          localized(
            context,
            '$payments طلب معلق عبر كل الفترات',
            '$payments pending across all periods',
          ),
          Icons.receipt_long_outlined,
          () => openPage(
            context,
            NativePaymentRequests(
              auth: auth,
              filters: {if (branchId.isNotEmpty) 'branchId': branchId},
            ),
          ),
        ),
      if (overdue > 0 || complaints > 0 || suggestions > 0)
        _decision(
          context,
          overdue > 0
              ? 'ملاحظات تجاوزت موعد المتابعة'
              : complaints > 0
              ? 'شكاوى تحتاج متابعة'
              : 'اقتراحات تحتاج متابعة',
          overdue > 0
              ? 'Overdue family follow-ups'
              : complaints > 0
              ? 'Complaints to follow up'
              : 'Suggestions to follow up',
          localized(
            context,
            '${overdue > 0
                ? overdue
                : complaints > 0
                ? complaints
                : suggestions} ملاحظة مفتوحة',
            '${overdue > 0
                ? overdue
                : complaints > 0
                ? complaints
                : suggestions} open notes',
          ),
          Icons.forum_outlined,
          () => openPage(
            context,
            OwnerFeedbackInbox(
              auth: auth,
              initialFilters: {
                if (branchId.isNotEmpty) 'branchId': branchId,
                'status': 'Unresolved',
                if (overdue > 0)
                  'overdue': 'true'
                else
                  'kind': complaints > 0 ? 'Complaint' : 'Suggestion',
              },
            ),
          ),
        ),
      if (groups.isNotEmpty)
        _decision(
          context,
          'مجموعة تحتاج متابعة',
          'Group requiring attention',
          textOf(groups.first['name']),
          Icons.groups_2_outlined,
          () => openPage(
            context,
            OwnerGroupOperations(auth: auth, id: textOf(groups.first['id'])),
          ),
        ),
    ];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Heading(
          localized(context, 'أولويات تحتاج قرارك', 'Your next decisions'),
        ),
        ...decisions,
        if (decisions.isEmpty)
          UiText(
            localized(
              context,
              operations.isEmpty
                  ? 'بيانات المتابعة غير متاحة.'
                  : 'لا توجد أولويات معلقة حسب القواعد الحالية.',
              operations.isEmpty
                  ? 'Operational data is unavailable.'
                  : 'No pending priorities under the current rules.',
            ),
          ),
      ],
    );
  }

  Widget _decision(
    BuildContext context,
    String ar,
    String en,
    String detail,
    IconData icon,
    Future<dynamic> Function() open,
  ) => GCard(
    onTap: () async {
      await open();
      if (context.mounted) onReturn?.call();
    },
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Icon(icon, color: Theme.of(context).colorScheme.primary),
        const SizedBox(width: 12),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              UiText(
                localized(context, ar, en),
                style: const TextStyle(
                  fontSize: 16,
                  fontWeight: FontWeight.w700,
                ),
              ),
              const SizedBox(height: 4),
              UiText(detail, style: const TextStyle(fontSize: 14)),
            ],
          ),
        ),
        const Icon(Icons.chevron_left),
      ],
    ),
  );
}

class OwnerOperationsPanel extends StatelessWidget {
  final AuthController auth;
  final Json operations;
  final Map<String, String> filters;
  final VoidCallback? onReturn;
  const OwnerOperationsPanel({
    super.key,
    required this.auth,
    required this.operations,
    this.filters = const {},
    this.onReturn,
  });
  Future<void> open(BuildContext context, Widget page) async {
    await openPage(context, page);
    if (context.mounted) onReturn?.call();
  }

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Heading(
        localized(
          context,
          'المجموعات التي تحتاج متابعة',
          'Groups needing attention',
        ),
      ),
      for (final g in rows(operations['groupAttention'] ?? <Json>[]))
        GLink(
          '${g['name']} · ${g['hasCoach'] == false ? localized(context, 'بلا مدرب · ', 'No coach · ') : ''}${g['notRecorded']} ${localized(context, 'غير مسجل', 'unrecorded')} · ${g['absent']} ${localized(context, 'غياب', 'absent')}',
          Icons.groups_outlined,
          () => open(
            context,
            OwnerGroupOperations(auth: auth, id: textOf(g['id'])),
          ),
        ),
      UiText(
        textOf(operations['attentionRule']),
        style: Theme.of(context).textTheme.bodySmall,
      ),
      Heading(
        localized(
          context,
          'المدربون وتقييم الأسرة',
          'Coaches and family feedback',
        ),
      ),
      for (final c in rows(operations['coachPerformance'] ?? <Json>[]))
        GLink(
          '${c['name']} · ${c['groupCount']} ${localized(context, 'مجموعات', 'groups')} · ${c['sessionCount']} ${localized(context, 'حصص', 'sessions')} · ${c['parentRating'] == null ? localized(context, 'لا تقييم بعد', 'No rating yet') : '${c['parentRating']}/5 (${c['parentRatingCount']})'}',
          Icons.sports_outlined,
          () => open(
            context,
            OwnerCoachOperations(
              auth: auth,
              id: textOf(c['id']),
              filters: filters,
            ),
          ),
        ),
      GLink(
        localized(
          context,
          'شكاوى ${operations['openComplaints']} · اقتراحات ${operations['openSuggestions']}',
          'Complaints ${operations['openComplaints']} · suggestions ${operations['openSuggestions']}',
        ),
        Icons.feedback_outlined,
        () => open(
          context,
          OwnerFeedbackInbox(
            auth: auth,
            initialFilters: {
              if (filters['branchId']?.isNotEmpty == true)
                'branchId': filters['branchId']!,
              'status': 'Unresolved',
            },
          ),
        ),
      ),
      GLink(
        localized(
          context,
          'تغييرات الصلاحيات · ${operations['accessChanges']} خلال الفترة — كل الفروع',
          'Access changes · ${operations['accessChanges']} in period — all branches',
        ),
        Icons.admin_panel_settings_outlined,
        () => open(
          context,
          OwnerAccessHistory(
            auth: auth,
            filters: {
              for (final key in ['from', 'to'])
                if (filters[key]?.isNotEmpty == true) key: filters[key]!,
            },
          ),
        ),
      ),
    ],
  );
}

class OwnerCoachOperations extends StatelessWidget {
  final AuthController auth;
  final String id;
  final Map<String, String> filters;
  const OwnerCoachOperations({
    super.key,
    required this.auth,
    required this.id,
    this.filters = const {},
  });
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'متابعة المدرب', 'Coach performance'),
    child: OwnerScopedRead(
      auth: auth,
      path:
          '/api/v1/reports/coach-performance/$id?${Uri(queryParameters: filters).query}',
      builder: (context, value) {
        final d = obj(value);
        return gList([
          Heading(textOf(d['name'])),
          UiText(
            localized(
              context,
              'الإسناد الحالي، والحصص والتقييمات خلال الفترة',
              'Current assignments; sessions and evaluations within the period',
            ),
            style: Theme.of(context).textTheme.bodySmall,
          ),
          UiText('${day(d['from'])} — ${day(d['to'])}'),
          Facts([
            (
              localized(context, 'الحصص', 'Sessions'),
              textOf(d['sessionCount']),
            ),
            (
              localized(context, 'تقييمات منشورة', 'Published evaluations'),
              textOf(d['publishedEvaluations']),
            ),
          ]),
          UiText(
            localized(
              context,
              d['parentRating'] == null
                  ? 'لم يصل تقييم أسرة بعد.'
                  : 'تقييم الأسرة ${d['parentRating']}/5 من ${d['parentRatingCount']} آراء',
              d['parentRating'] == null
                  ? 'No family rating yet.'
                  : 'Family rating ${d['parentRating']}/5 from ${d['parentRatingCount']} responses',
            ),
          ),
          for (final g in rows(d['groups']))
            GLink(
              '${g['name']} · ${g['branch']} · ${g['sessions']} ${localized(context, 'حصص', 'sessions')}',
              Icons.groups_outlined,
              () => openPage(
                context,
                OwnerGroupOperations(auth: auth, id: textOf(g['id'])),
              ),
            ),
          GLink(
            localized(
              context,
              'ملاحظات الأسر لهذا المدرب',
              'Family feedback for this coach',
            ),
            Icons.forum_outlined,
            () => openPage(
              context,
              OwnerFeedbackInbox(
                auth: auth,
                initialFilters: {
                  'coachId': id,
                  if (filters['branchId']?.isNotEmpty == true)
                    'branchId': filters['branchId']!,
                },
              ),
            ),
          ),
        ]);
      },
    ),
  );
}

class OwnerGroupOperations extends StatelessWidget {
  final AuthController auth;
  final String id;
  const OwnerGroupOperations({super.key, required this.auth, required this.id});
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'تفاصيل المجموعة', 'Group details'),
    child: OwnerScopedRead(
      auth: auth,
      path: '/api/v1/manage/groups/$id/details',
      builder: (context, value) {
        final d = obj(value), group = obj(d['group']);
        return gList([
          Heading(textOf(group['arabicName'])),
          UiText(
            localized(
              context,
              '${rows(d['players']).length} لاعب · السعة ${group['capacity']}',
              '${rows(d['players']).length} players · capacity ${group['capacity']}',
            ),
          ),
          for (final c in rows(d['coaches'])) UiText(textOf(c['name'])),
          if (rows(d['coaches']).isEmpty)
            UiText(
              localized(
                context,
                'لا يوجد مدرب مسند؛ الإسناد من إدارة المجموعة في لوحة الويب.',
                'No assigned coach; assign one in the web group workspace.',
              ),
            ),
          Heading(localized(context, 'اللاعبون', 'Players')),
          for (final p in rows(d['players'])) ...[
            UiText(textOf(p['arabicName'])),
            if (p['id'] is String)
              GLink(
                'خطوات تعلم اللاعب',
                Icons.auto_graph_rounded,
                () => openPage(
                  context,
                  PlayerSkillsPage(auth: auth, enrollmentId: p['id'] as String),
                ),
              ),
            if (p['id'] is String)
              GLink(
                'أهداف التطور',
                Icons.flag_outlined,
                () => openPage(
                  context,
                  PlayerGoalsPage(auth: auth, enrollmentId: p['id'] as String),
                ),
              ),
            if (p['id'] is String)
              GLink(
                'شارات المجهود والتحسن',
                Icons.auto_awesome,
                () => openPage(
                  context,
                  PlayerBadgesPage(auth: auth, enrollmentId: p['id'] as String),
                ),
              ),
          ],
          Heading(
            localized(
              context,
              'أحدث حصص المجموعة — حتى 200',
              'Latest group sessions — up to 200',
            ),
          ),
          for (final s in rows(d['sessions']))
            GLink(
              '${day(s['sessionDate'])} · ${textOf(s['startTime']).split(':').take(2).join(':')} — ${textOf(s['endTime']).split(':').take(2).join(':')}',
              Icons.calendar_month_outlined,
              () => openPage(
                context,
                CoachAttendance(
                  auth: auth,
                  sessionId: textOf(s['id']),
                  title: localized(context, 'تفاصيل الحصة', 'Session details'),
                ),
              ),
            ),
          if (rows(d['sessions']).isEmpty)
            const EmptyMessage('لا توجد حصص لهذه المجموعة.'),
        ]);
      },
    ),
  );
}

class OwnerAccessHistory extends StatelessWidget {
  final AuthController auth;
  final Map<String, String> filters;
  const OwnerAccessHistory({
    super.key,
    required this.auth,
    this.filters = const {},
  });
  @override
  Widget build(BuildContext context) => GuardianPage(
    title: localized(context, 'تغييرات الصلاحيات', 'Access changes'),
    child: OwnerScopedRead(
      auth: auth,
      path:
          '/api/v1/reports/staff-access-audit?${Uri(queryParameters: filters).query}',
      builder: (context, value) {
        final d = obj(value);
        return gList([
          UiText(
            localized(
              context,
              '${d['count']} تغيير · أحدث 50 سجلًا — كل الفروع',
              '${d['count']} changes · latest 50 records — all branches',
            ),
          ),
          for (final a in rows(d['items']))
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText('${a['actorName']} ← ${a['staffName']}'),
                  UiText(textOf(a['change'])),
                  UiText(day(a['createdAtUtc'])),
                ],
              ),
            ),
          if (rows(d['items']).isEmpty) const EmptyMessage(),
        ]);
      },
    ),
  );
}
