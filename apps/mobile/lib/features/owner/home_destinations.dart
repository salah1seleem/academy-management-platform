import '../shared/directory_cards.dart';
import '../coach/coach_evaluation.dart';

import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../admin/native_player_workspace.dart';
import '../shared/experience_ui.dart';
import 'owner_operations.dart';
import 'honor_portrait_card.dart';

class HomeCoaches extends StatefulWidget {
  const HomeCoaches({
    super.key,
    required this.auth,
    required this.items,
    required this.branches,
    required this.filters,
    this.all = false,
    this.byGroups = false,
  });
  final AuthController auth;
  final List<Json> items, branches;
  final Map<String, String> filters;
  final bool all, byGroups;
  @override
  State<HomeCoaches> createState() => _HomeCoachesState();
}

class _HomeCoachesState extends State<HomeCoaches> {
  late bool byGroups = widget.byGroups;
  AuthController get auth => widget.auth;
  List<Json> get items => widget.items;
  List<Json> get branches => widget.branches;
  Map<String, String> get filters => widget.filters;
  bool get all => widget.all;
  Widget portraitCard(BuildContext context, Json c, double height) {
    return HonorPortraitCard(
      compact: all,
      auth: auth,
      name: textOf(c['name']),
      reference: c['photoReference'],
      gender: c['gender'],
      identity: textOf(c['id']),
      avatarRole: 'coach',
      details: coachDetails(c, branches),
      height: height,
      onTap: () => openPage(
        context,
        OwnerCoachOperations(auth: auth, id: textOf(c['id']), filters: filters),
      ),
    );
  }

  double portraitHeight(BuildContext context, double width) =>
      width * 1.7 +
      (MediaQuery.textScalerOf(context).scale(14) - 14).clamp(0, 100) * 12;
  @override
  Widget build(BuildContext context) {
    final list = [...items]
      ..sort((a, b) {
        final count = (b['groupCount'] as num).compareTo(
          a['groupCount'] as num,
        );
        final ratingA = a['parentRating'] as num?,
            ratingB = b['parentRating'] as num?;
        final rating = ratingB == null
            ? (ratingA == null ? 0 : -1)
            : ratingA == null
            ? 1
            : ratingB.compareTo(ratingA);
        final primary = byGroups ? count : rating;
        return primary != 0
            ? primary
            : textOf(a['name']).compareTo(textOf(b['name']));
      });
    final sorting = Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
      child: Wrap(
        spacing: 8,
        children: [
          for (final groups in [false, true])
            ChoiceChip(
              label: UiText(groups ? 'حسب المجموعات المسندة' : 'حسب التقييم'),
              selected: byGroups == groups,
              onSelected: (_) => setState(() => byGroups = groups),
            ),
        ],
      ),
    );
    if (all) {
      return GuardianPage(
        title: 'كل المدربين',
        child: gList([
          sorting,
          if (list.isEmpty) const EmptyMessage('لا يوجد مدربون في هذا النطاق.'),
          for (final coach in list) portraitCard(context, coach, 150),
        ]),
      );
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        ExperienceSection(
          'المدربون · ${items.length}',
          Icons.sports_rounded,
          actionLabel: 'كل المدربين',
          action: () => openPage(
            context,
            HomeCoaches(
              auth: auth,
              items: items,
              branches: branches,
              filters: filters,
              all: true,
              byGroups: byGroups,
            ),
          ),
        ),
        sorting,
        if (items.isEmpty) const EmptyMessage('لا يوجد مدربون في هذا النطاق.'),
        if (items.isNotEmpty)
          LayoutBuilder(
            builder: (context, constraints) {
              final width =
                  constraints.maxWidth *
                  (MediaQuery.sizeOf(context).width < 400 ? .46 : .42);
              final height = portraitHeight(context, width);
              return SizedBox(
                height: height,
                child: ListView.separated(
                  scrollDirection: Axis.horizontal,
                  itemCount: list.length,
                  separatorBuilder: (_, _) => const SizedBox(width: 10),
                  itemBuilder: (context, i) => SizedBox(
                    width: width,
                    child: portraitCard(context, list[i], height),
                  ),
                ),
              );
            },
          ),
      ],
    );
  }
}

class HomeSubscriptionPlayers extends StatefulWidget {
  const HomeSubscriptionPlayers({
    super.key,
    required this.auth,
    required this.filters,
    this.state = '',
    this.uniquePlayers = false,
  });
  final AuthController auth;
  final Map<String, String> filters;
  final String state;
  final bool uniquePlayers;
  @override
  State<HomeSubscriptionPlayers> createState() =>
      _HomeSubscriptionPlayersState();
}

class _HomeSubscriptionPlayersState extends State<HomeSubscriptionPlayers> {
  int page = 1;
  bool sendingReminder = false;
  Future<void> remind() async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (c) => AlertDialog(
        title: const UiText('تنبيه قرب انتهاء الاشتراك'),
        content: const UiText(
          'إرسال تنبيه داخل التطبيق لأولياء أمور الاشتراكات الحالية التي تنتهي خلال 7 أيام، في الفرع والرياضة المحددين؟',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(c, false),
            child: const UiText('رجوع'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(c, true),
            child: const UiText('إرسال التنبيه'),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;
    setState(() => sendingReminder = true);
    try {
      final result = obj(
        await widget.auth.request(
          'POST',
          '/api/v1/subscriptions/home/expiry-notices',
          body: {
            'branchId': widget.filters['branchId'],
            'sportId': widget.filters['sportId'],
          },
        ),
      );
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: UiText(textOf(result['message']))));
      }
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: UiText(
              'تعذر إرسال التنبيه. راجع إعدادات الإشعارات وأعد المحاولة.',
            ),
          ),
        );
      }
    } finally {
      if (mounted) setState(() => sendingReminder = false);
    }
  }

  Widget entry(BuildContext context, Json p, bool allowed, bool canEvaluate) {
    final sports = p['sports'] is List ? rows(p['sports']) : [p];
    Future<void> evaluate() async {
      Json? picked = sports.length == 1
          ? sports.first
          : await showModalBottomSheet<Json>(
              context: context,
              showDragHandle: true,
              builder: (c) => SafeArea(
                child: ListView(
                  shrinkWrap: true,
                  children: [
                    const Padding(
                      padding: EdgeInsets.all(16),
                      child: Heading('اختر رياضة التقييم'),
                    ),
                    for (final e in sports)
                      ListTile(
                        title: UiText(textOf(e['sport'])),
                        subtitle: UiText(
                          '${e['branch']} · ${e['group'] ?? 'في انتظار المجموعة'}',
                        ),
                        onTap: () => Navigator.pop(c, e),
                      ),
                  ],
                ),
              ),
            );
      if (picked != null && context.mounted) {
        await openPage(
          context,
          CoachEvaluation(
            auth: widget.auth,
            enrollment: {
              ...picked,
              'playerId': p['playerId'],
              'photoReference': p['photoReference'],
              'gender': p['gender'],
              'player': p['name'],
            },
          ),
        );
      }
    }

    return PlayerDirectoryCard(
      auth: widget.auth,
      player: {
        ...p,
        'arabicName': p['name'],
        'hasActiveSubscription': sports.any((s) => s['active'] == true),
      },
      enrollments: [
        for (final s in sports)
          {
            ...s,
            'subscription': {
              'status': s['frozen'] == true
                  ? 'Frozen'
                  : s['active'] == true
                  ? 'Active'
                  : 'Expired',
              'remainingSessions': s['remainingSessions'],
              'endDate': s['endDate'],
              'sessionBased': s['planType'] != 'Duration',
            },
          },
      ],
      onOpen: allowed
          ? () => openPage(
              context,
              StaffPlayerRead(auth: widget.auth, id: textOf(p['playerId'])),
            )
          : null,
      onEvaluate: canEvaluate ? evaluate : null,
    );
  }

  @override
  Widget build(BuildContext context) => GuardianPage(
    title: widget.uniquePlayers ? 'قائمة اللاعبين' : 'الاشتراكات',
    child: RemoteBody(
      auth: widget.auth,
      path:
          '/api/v1/subscriptions/home/items?${Uri(queryParameters: {...widget.filters, 'page': '$page', if (widget.filters['to'] != null) 'asOf': widget.filters['to']!, if (widget.uniquePlayers) 'uniquePlayers': 'true', 'state': widget.state.isEmpty ? 'all' : widget.state}).query}',
      builder: (context, value) {
        final d = obj(value), items = rows(d['items']);
        final allowed =
            widget.auth.session?.selected?.role == 'AcademyOwner' ||
            d['canOpenPlayer'] == true;
        return gList([
          if (widget.uniquePlayers && allowed)
            Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: OutlinedButton.icon(
                onPressed: () =>
                    openPage(context, NativePlayerDirectory(auth: widget.auth)),
                icon: const Icon(Icons.search_rounded),
                label: const UiText('بحث في جميع اللاعبين'),
              ),
            ),
          if (widget.state == 'expiring' &&
              widget.auth.session?.selected?.role == 'AcademyOwner' &&
              widget.filters['dimension'] == null)
            Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: OutlinedButton.icon(
                onPressed: sendingReminder ? null : remind,
                icon: const Icon(Icons.notifications_active_outlined),
                label: const UiText('تنبيه أولياء الأمور'),
              ),
            ),
          Heading('${d['total']} ${widget.uniquePlayers ? 'لاعب' : 'اشتراك'}'),
          if (d['from'] != null)
            UiText(
              'من ${day(d['from'])} إلى ${day(d['asOf'])}',
              style: const TextStyle(fontSize: 11),
            )
          else if (d['asOf'] != null)
            UiText(
              'حتى ${day(d['asOf'])}',
              style: const TextStyle(fontSize: 11),
            ),
          if (items.isEmpty)
            const EmptyMessage('لا توجد نتائج مطابقة للفلاتر.'),
          for (final p in items)
            entry(
              context,
              p,
              allowed,
              widget.auth.session?.selected?.role == 'AcademyOwner' ||
                  d['canEvaluate'] == true,
            ),
          const SizedBox.shrink(),
        ]);
      },
    ),
  );
}

class StaffPlayerRead extends StatelessWidget {
  const StaffPlayerRead({super.key, required this.auth, required this.id});
  final AuthController auth;
  final String id;
  @override
  Widget build(BuildContext context) =>
      NativePlayerWorkspace(auth: auth, id: id);
}
