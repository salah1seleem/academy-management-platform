import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/academy_home_skin.dart';
import '../../core/theme/ui_text.dart';
import '../admin/native_structure_operations.dart';
import '../guardian/guardian_ui.dart';
import '../shared/experience_ui.dart';
import 'collection_reminders.dart';
import 'group_merge.dart';
import 'owner_followups.dart';
import 'owner_operations.dart';
import 'record_followup.dart';
import 'swipe_card_deck.dart';

typedef _Decision = ({
  String kind,
  int count,
  String title,
  String action,
  IconData icon,
});

String _path(String branch, {String? kind, int? page}) =>
    '/api/v1/manage/decision-center?${Uri(queryParameters: {if (branch.isNotEmpty) 'branchId': branch, 'kind': ?kind, if (page != null) 'page': '$page'}).query}';

class OwnerDecisionCenter extends StatefulWidget {
  const OwnerDecisionCenter({
    super.key,
    required this.auth,
    required this.branchId,
    required this.pendingPayments,
    required this.onPayments,
  });

  final AuthController auth;
  final String branchId;
  final int pendingPayments;
  final Future<void> Function() onPayments;

  @override
  State<OwnerDecisionCenter> createState() => _OwnerDecisionCenterState();
}

class _OwnerDecisionCenterState extends State<OwnerDecisionCenter> {
  int revision = 0;

  List<_Decision> _cards(Json data) {
    int count(String key) => (data[key] as num?)?.toInt() ?? 0;
    return [
      (
        kind: 'Payment',
        count: widget.pendingPayments,
        title: 'طلبات دفع بانتظار المراجعة',
        action: 'مراجعة الطلبات',
        icon: Icons.receipt_long_outlined,
      ),
      (
        kind: 'Expiring',
        count: count('expiring'),
        title: 'لاعبون اقترب تجديدهم',
        action: 'تنبيه الأسر بالتجديد',
        icon: Icons.event_repeat_outlined,
      ),
      (
        kind: 'Expired',
        count: count('expired'),
        title: 'لاعبون انتهى اشتراكهم',
        action: 'متابعة تجديد الاشتراكات',
        icon: Icons.person_off_outlined,
      ),
      (
        kind: 'Absence',
        count: count('absence'),
        title: 'لاعبون غابوا ٣ حصص متتالية',
        action: 'متابعة الغياب',
        icon: Icons.event_busy_outlined,
      ),
      (
        kind: 'Capacity',
        count: count('seats'),
        title: 'أماكن تدريب متاحة',
        action: 'تسكين لاعبين أو دمج مجموعات',
        icon: Icons.event_seat_outlined,
      ),
      (
        kind: 'Tasks',
        count: count('openTasks'),
        title: 'مهام تحتاج متابعة',
        action: 'عرض الحالة والمسؤول',
        icon: Icons.task_alt_rounded,
      ),
    ].where((d) => d.count > 0).toList();
  }

  Future<void> _open(BuildContext context, _Decision decision) async {
    final auth = widget.auth, branch = widget.branchId;
    final expected = followUpContext(auth);
    switch (decision.kind) {
      case 'Payment':
        await widget.onPayments();
      case 'Tasks':
        await openPage(
          context,
          OwnerFollowUpsPage(auth: auth, initialBranch: branch, allDates: true),
        );
      case 'Capacity':
        await openPage(context, _CapacityDecision(auth: auth, branch: branch));
      default:
        await openPage(
          context,
          _PlayerDecision(auth: auth, branch: branch, decision: decision),
        );
    }
    if (mounted && expected != null && expected == followUpContext(auth)) {
      setState(() => revision++);
    }
  }

  Widget _card(
    BuildContext context,
    _Decision decision, {
    VoidCallback? onOpen,
  }) => GCard(
    child: Semantics(
      button: true,
      child: InkWell(
        borderRadius: BorderRadius.circular(12),
        onTap: onOpen ?? () => _open(context, decision),
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 2),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Icon(decision.icon, color: academyAccent(context)),
                  const SizedBox(width: 10),
                  UiText(
                    '${decision.count}',
                    style: const TextStyle(
                      fontSize: 25,
                      fontWeight: FontWeight.w800,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 4),
              UiText(
                decision.title,
                style: const TextStyle(fontWeight: FontWeight.w800),
              ),
              const SizedBox(height: 7),
              Row(
                children: [
                  Expanded(
                    child: UiText(
                      decision.action,
                      style: TextStyle(
                        color: academyInk(context),
                        fontSize: 12,
                      ),
                    ),
                  ),
                  const Icon(
                    Icons.arrow_back_rounded,
                    size: 16,
                    textDirection: TextDirection.ltr,
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    ),
  );

  @override
  Widget build(BuildContext context) => OwnerScopedRead(
    key: ValueKey('decision-${widget.branchId}-$revision'),
    auth: widget.auth,
    path: _path(widget.branchId),
    builder: (context, value) {
      final decisions = _cards(obj(value));
      return Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          ExperienceSection(
            'مركز القرارات الذكية',
            Icons.bolt_rounded,
            actionLabel: 'كل القرارات',
            action: decisions.isEmpty
                ? null
                : () async {
                    final branch = widget.branchId;
                    final expected = followUpContext(widget.auth);
                    var directoryRevision = 0;
                    await openPage(
                      context,
                      StatefulBuilder(
                        builder: (directoryContext, setDirectoryState) =>
                            GuardianPage(
                              title: 'كل القرارات',
                              child: OwnerScopedRead(
                                key: ValueKey(directoryRevision),
                                auth: widget.auth,
                                path: _path(branch),
                                builder: (context, value) {
                                  final current = _cards(obj(value));
                                  return gList([
                                    if (current.isEmpty)
                                      const EmptyMessage(
                                        'لا توجد قرارات تحتاج إجراءً في هذا النطاق.',
                                      ),
                                    for (final decision in current)
                                      _card(
                                        context,
                                        decision,
                                        onOpen: () async {
                                          await _open(context, decision);
                                          if (directoryContext.mounted &&
                                              expected ==
                                                  followUpContext(
                                                    widget.auth,
                                                  )) {
                                            setDirectoryState(
                                              () => directoryRevision++,
                                            );
                                          }
                                        },
                                      ),
                                  ]);
                                },
                              ),
                            ),
                      ),
                    );
                    if (mounted && expected == followUpContext(widget.auth)) {
                      setState(() => revision++);
                    }
                  },
          ),
          if (decisions.isEmpty)
            const EmptyMessage('لا توجد قرارات تحتاج إجراءً في هذا النطاق.')
          else
            SwipeCardDeck(
              children: [
                for (final decision in decisions) _card(context, decision),
              ],
            ),
        ],
      );
    },
  );
}

class _PlayerDecision extends StatefulWidget {
  const _PlayerDecision({
    required this.auth,
    required this.branch,
    required this.decision,
  });
  final AuthController auth;
  final String branch;
  final _Decision decision;

  @override
  State<_PlayerDecision> createState() => _PlayerDecisionState();
}

class _PlayerDecisionState extends State<_PlayerDecision> {
  int page = 1, revision = 0;

  Future<void> _assign(Json player) async {
    final expected = followUpContext(widget.auth);
    final playerId = textOf(player['playerId']);
    if (player['taskId'] case final String id when id.isNotEmpty) {
      await openPage(context, OwnerFollowUpPage(auth: widget.auth, id: id));
      if (mounted && expected == followUpContext(widget.auth)) {
        setState(() => revision++);
      }
      return;
    }
    final id = await openPage<String>(
      context,
      OwnerRecordFollowUpCreate(
        auth: widget.auth,
        selectedSource: {...player, 'id': playerId},
        initialNote: switch (widget.decision.kind) {
          'Absence' => 'تواصل مع الأسرة بشأن الغياب عن آخر ثلاث حصص.',
          'Expiring' => 'تواصل مع الأسرة قبل انتهاء الاشتراك.',
          _ => 'تواصل مع الأسرة لبحث تجديد الاشتراك.',
        },
      ),
    );
    if (!mounted || id == null || expected != followUpContext(widget.auth)) {
      return;
    }
    setState(() => revision++);
    ScaffoldMessenger.of(context)
        .showSnackBar(const SnackBar(content: UiText('تم حفظ مهمة المتابعة.')));
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: widget.decision.title,
    child: OwnerScopedRead(
      key: ValueKey((page, revision)),
      auth: widget.auth,
      autoPaginate: false,
      path: _path(widget.branch, kind: widget.decision.kind, page: page),
      builder: (context, value) {
        final result = obj(value);
        final items = rows(result['items']);
        return gList([
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                UiText(switch (widget.decision.kind) {
                  'Absence' => 'اشتراكات هؤلاء اللاعبين سارية. يمكنك متابعة سبب الغياب مع الأسرة.',
                  'Expiring' => 'ذكّر الأسر قبل انتهاء الاشتراك، أو أسند التواصل لموظف مختص.',
                  _ => 'ادعُ الأسر للعودة إلى التدريب، أو أسند متابعة التجديد لموظف مختص.',
                }),
                const SizedBox(height: 10),
                FilledButton.icon(
                  onPressed: items.isEmpty
                      ? null
                      : () => openPage(
                          context,
                          CollectionReminders(
                            auth: widget.auth,
                            branch: widget.branch,
                            kind: widget.decision.kind,
                          ),
                        ),
                  icon: const Icon(Icons.notifications_active_outlined),
                  label: const UiText('إعداد تنبيه للأسر'),
                ),
              ],
            ),
          ),
          Heading('${result['total']} لاعب'),
          for (final player in items)
            GCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  UiText(
                    textOf(player['name']),
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  UiText('${player['branch']} · ${player['sport']}'),
                  if (player['date'] != null)
                    UiText(
                      '${widget.decision.kind == 'Absence' ? 'آخر غياب' : 'انتهاء الاشتراك'}: ${day(player['date'])}',
                    ),
                  TextButton(
                    onPressed: () => _assign(player),
                    child: UiText(
                      player['taskId'] == null ? 'إسناد المهمة' : 'عرض المهمة',
                    ),
                  ),
                ],
              ),
            ),
          if (items.isEmpty)
            EmptyMessage(
              page > 1
                  ? 'هذه الصفحة خالية بعد آخر تحديث.'
                  : 'لا يوجد لاعبون ضمن هذه القائمة الآن.',
            ),
          if (page > 1 || (result['total'] as num? ?? 0) > page * 50)
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                TextButton(
                  onPressed: page > 1 ? () => setState(() => page--) : null,
                  child: const UiText('السابق'),
                ),
                TextButton(
                  onPressed: (result['total'] as num? ?? 0) > page * 50
                      ? () => setState(() => page++)
                      : null,
                  child: const UiText('التالي'),
                ),
              ],
            ),
        ]);
      },
    ),
  );
}

class _CapacityDecision extends StatefulWidget {
  const _CapacityDecision({required this.auth, required this.branch});
  final AuthController auth;
  final String branch;
  @override
  State<_CapacityDecision> createState() => _CapacityDecisionState();
}

class _CapacityDecisionState extends State<_CapacityDecision> {
  int revision = 0;

  Future<void> _open(Widget page) async {
    await openPage(context, page);
    if (mounted) setState(() => revision++);
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'أماكن التدريب المتاحة',
    child: OwnerScopedRead(
      key: ValueKey(revision),
      auth: widget.auth,
      path: _path(widget.branch, kind: 'Capacity'),
      builder: (context, value) {
        final data = obj(value), groups = rows(data['groups']);
        return gList([
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              FilledButton.icon(
                onPressed: () => _open(
                  _UnassignedPlayers(
                    auth: widget.auth,
                    branch: widget.branch,
                    groups: groups,
                  ),
                ),
                icon: const Icon(Icons.person_add_alt_1_outlined),
                label: const UiText('تسكين لاعبين'),
              ),
              OutlinedButton.icon(
                onPressed: groups.length < 2
                    ? null
                    : () => _open(
                        OwnerGroupMerge(
                          auth: widget.auth,
                          groups: groups,
                          today: textOf(data['today']),
                        ),
                      ),
                icon: const Icon(Icons.merge_type_rounded),
                label: const UiText('دمج مجموعات'),
              ),
            ],
          ),
          const Heading('السعة حسب المجموعة'),
          for (final group in groups.where(
            (g) => (g['available'] as num? ?? 0) > 0,
          ))
            GCard(
              child: ListTile(
                contentPadding: EdgeInsets.zero,
                title: UiText(textOf(group['name'])),
                subtitle: UiText(
                  '${group['branch']} · ${group['occupied']} من ${group['capacity']}',
                ),
                trailing: UiText('${group['available']} مكان'),
                onTap: () => _open(
                  NativeGroupPlacement(
                    auth: widget.auth,
                    groupId: textOf(group['groupId']),
                  ),
                ),
              ),
            ),
        ]);
      },
    ),
  );
}

class _UnassignedPlayers extends StatefulWidget {
  const _UnassignedPlayers({
    required this.auth,
    required this.branch,
    required this.groups,
  });
  final AuthController auth;
  final String branch;
  final List<Json> groups;

  @override
  State<_UnassignedPlayers> createState() => _UnassignedPlayersState();
}

class _UnassignedPlayersState extends State<_UnassignedPlayers> {
  int revision = 0, page = 1;

  Future<void> _place(Json player) async {
    final candidates = widget.groups
        .where(
          (g) =>
              g['branchId'] == player['branchId'] &&
              g['sportId'] == player['sportId'] &&
              (g['available'] as num? ?? 0) > 0,
        )
        .toList();
    final chosen = await showModalBottomSheet<String>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: SizedBox(
          height: MediaQuery.sizeOf(context).height * .55,
          child: gList([
            Heading('تسكين ${player['name']}'),
            if (candidates.isEmpty)
              const EmptyMessage(
                'لا توجد مجموعة بها مكان في نفس الفرع والرياضة.',
              ),
            for (final group in candidates)
              ListTile(
                title: UiText(textOf(group['name'])),
                subtitle: UiText('${group['available']} مكان متاح'),
                onTap: () => Navigator.pop(context, textOf(group['groupId'])),
              ),
          ]),
        ),
      ),
    );
    if (!mounted || chosen == null) return;
    await openPage(
      context,
      NativeGroupPlacement(
        auth: widget.auth,
        groupId: chosen,
        initialEnrollmentId: textOf(player['enrollmentId']),
      ),
    );
    if (mounted) setState(() => revision++);
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: 'لاعبون بانتظار التسكين',
    child: OwnerScopedRead(
      key: ValueKey((page, revision)),
      auth: widget.auth,
      autoPaginate: false,
      path: _path(widget.branch, kind: 'Unassigned', page: page),
      builder: (context, value) {
        final data = obj(value), players = rows(data['items']);
        return gList([
          if (players.isEmpty)
            const EmptyMessage('لا يوجد لاعبون بانتظار التسكين في هذه الصفحة.'),
          for (final player in players)
            GCard(
              child: ListTile(
                contentPadding: EdgeInsets.zero,
                title: UiText(textOf(player['name'])),
                subtitle: UiText('${player['branch']} · ${player['sport']}'),
                trailing: const Icon(Icons.person_add_alt_1_outlined),
                onTap: () => _place(player),
              ),
            ),
          if (page > 1 || (data['total'] as num? ?? 0) > page * 50)
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                TextButton(
                  onPressed: page > 1 ? () => setState(() => page--) : null,
                  child: const UiText('السابق'),
                ),
                TextButton(
                  onPressed: (data['total'] as num? ?? 0) > page * 50
                      ? () => setState(() => page++)
                      : null,
                  child: const UiText('التالي'),
                ),
              ],
            ),
        ]);
      },
    ),
  );
}
