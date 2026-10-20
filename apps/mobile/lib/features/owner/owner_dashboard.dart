import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/experience_ui.dart';
import '../shared/role_navigation.dart';
import 'owner_reports.dart';
import 'owner_operations.dart';
import 'owner_cockpit.dart';
import 'member_values.dart';
import 'refined_home_panels.dart';
import 'home_destinations.dart';
import 'featured_players.dart';
import 'collection_reminders.dart';
import 'collection_target.dart';
import 'owner_followups.dart';

class OwnerDashboard extends StatefulWidget {
  const OwnerDashboard({
    super.key,
    required this.auth,
    required this.accountPage,
  });
  final AuthController auth;
  final Widget accountPage;
  @override
  State<OwnerDashboard> createState() => _OwnerDashboardState();
}

class _OwnerDashboardState extends State<OwnerDashboard> {
  String from = '', to = '', branch = '';
  bool cleared = false;
  String? identity;
  late Future<List<Object?>> data;
  String? get contextKey => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  @override
  void initState() {
    super.initState();
    identity = contextKey;
    widget.auth.addListener(authChanged);
    data = load();
  }

  void authChanged() {
    if (!mounted || identity == contextKey) return;
    setState(() {
      identity = contextKey;
      from = '';
      to = '';
      branch = '';
      cleared = false;
      data = identity == null ? Future.value(<Object?>[]) : load();
    });
  }

  @override
  void dispose() {
    widget.auth.removeListener(authChanged);
    super.dispose();
  }

  Map<String, String> get filters => {
    if (from.isNotEmpty) 'from': from,
    if (to.isNotEmpty) 'to': to,
    if (branch.isNotEmpty) 'branchId': branch,
    if (cleared) 'allDates': 'true',
  };
  Future<List<Object?>> load() async {
    final expected = contextKey;
    if (!cleared && from.isEmpty && to.isEmpty) {
      final calendar = obj(
        await widget.auth.request('GET', '/api/v1/academy/calendar'),
      );
      if (expected != contextKey) throw StateError('context');
      from = textOf(calendar['trailingMonthFrom']);
      to = textOf(calendar['today']);
    }
    final query = Uri(queryParameters: filters).query;
    final result = await Future.wait([
      widget.auth.request('GET', '/api/v1/reports/owner-summary?$query'),
      widget.auth.request('GET', '/api/v1/reports/owner-cockpit?$query'),
      widget.auth.request('GET', '/api/v1/reports/operating-statement?$query'),
      widget.auth.request('GET', '/api/v1/manage/structure/options'),
    ]);
    if (expected != contextKey) throw StateError('context');
    return result;
  }

  void refresh() {
    if (!mounted || contextKey == null) return;
    setState(() {
      data = load();
    });
  }

  void report(String name) => openPage(
    context,
    OwnerReports(
      auth: widget.auth,
      initialReport: name,
      initialFilters: {
        ...filters,
        if (name == 'receivables') 'state': 'overdue',
      },
    ),
  );
  @override
  Widget build(BuildContext context) => Scaffold(
    body: SafeArea(
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 720),
          child: identity == null
              ? const SizedBox.shrink()
              : FutureBuilder<List<Object?>>(
                  key: ValueKey(identity),
                  future: data,
                  builder: (context, snapshot) {
                    if (!snapshot.hasData) {
                      if (snapshot.hasError) {
                        return Center(
                          child: Column(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              const UiText('تعذر تحميل الرئيسية.'),
                              TextButton(
                                onPressed: refresh,
                                child: const UiText('إعادة المحاولة'),
                              ),
                            ],
                          ),
                        );
                      }
                      return const Center(child: CircularProgressIndicator());
                    }
                    final summary = obj(snapshot.data![0]),
                        cockpit = obj(snapshot.data![1]),
                        statement = obj(snapshot.data![2]),
                        options = obj(snapshot.data![3]);
                    final requests = obj(summary['paymentRequests']),
                        collections = obj(summary['collections']),
                        scope = {...filters};
                    void queue([String type = '']) => openPage(
                      context,
                      NativePaymentRequests(
                        auth: widget.auth,
                        filters: {...scope, if (type.isNotEmpty) 'type': type},
                      ),
                    );
                    void collection(bool daily, String method) {
                      final end = textOf(summary['collectionDay']);
                      openPage(
                        context,
                        OwnerReports(
                          auth: widget.auth,
                          initialReport: 'financial',
                          initialFilters: {
                            'from': daily
                                ? end
                                : textOf(summary['collectionMonthFrom']),
                            'to': end,
                            if (branch.isNotEmpty) 'branchId': branch,
                            if (method.isNotEmpty) 'provider': method,
                          },
                        ),
                      );
                    }

                    return RefreshIndicator(
                      onRefresh: () async {
                        refresh();
                        await data;
                      },
                      child: gList([
                        ExperienceHeader(
                          auth: widget.auth,
                          subtitle: 'لوحة المالك',
                          lesson: 'owner-dashboard',
                          account: widget.accountPage,
                        ),
                        ScopeDateTiles(
                          from: from,
                          to: to,
                          onChanged: (a, b) {
                            setState(() {
                              from = a;
                              to = b;
                              cleared = false;
                              data = load();
                            });
                          },
                          onClear: () {
                            setState(() {
                              from = '';
                              to = '';
                              branch = '';
                              cleared = true;
                              data = load();
                            });
                          },
                        ),
                        ScopeBranchTabs(
                          items: rows(options['branches']),
                          selected: branch,
                          onSelected: (v) {
                            setState(() {
                              branch = v;
                              data = load();
                            });
                          },
                        ),
                        AcademySummaryPanel(
                          data: cockpit,
                          onTarget: () async {
                            final saved = await openPage<bool>(
                              context,
                              CollectionTargetPage(auth: widget.auth),
                            );
                            if (saved == true && mounted) {
                              final calendar = obj(
                                await widget.auth.request(
                                  'GET',
                                  '/api/v1/academy/calendar',
                                ),
                              );
                              if (!mounted) return;
                              setState(() {
                                to = textOf(calendar['today']);
                                from = '${to.substring(0, 7)}-01';
                                branch = '';
                                cleared = false;
                                data = load();
                              });
                            }
                          },
                          onReport: report,
                          onCapacity: () => openPage(
                            context,
                            OwnerCapacityPage(
                              auth: widget.auth,
                              filters: scope,
                            ),
                          ),
                        ),
                        HomeSubscriptions(
                          data: obj(summary['subscriptions']),
                          onOpen: (state) => openPage(
                            context,
                            HomeSubscriptionPlayers(
                              auth: widget.auth,
                              filters: {...scope, 'to': textOf(summary['to'])},
                              state: state,
                            ),
                          ),
                        ),
                        HomeCollectionCards(
                          data: collections,
                          currency: summary['currency'],
                          referenceDay: textOf(summary['collectionDay']),
                          onOpen: collection,
                        ),
                        HomeOperatingStatement(
                          data: statement,
                          onOpen: () => report('operating'),
                        ),
                        ExperienceSection(
                          'طلبات الدفع',
                          Icons.receipt_long_outlined,
                          action: () => queue(),
                        ),
                        GCard(
                          child: Wrap(
                            spacing: 8,
                            runSpacing: 8,
                            children: [
                              for (final item in [
                                (
                                  'اشتراكات جديدة',
                                  'Enrollment',
                                  requests['newEnrollments'],
                                ),
                                (
                                  'تجديد الاشتراك',
                                  'Renewal',
                                  requests['renewals'],
                                ),
                                (
                                  'منتجات رياضية',
                                  'Product',
                                  requests['products'],
                                ),
                                if ((requests['tournaments'] as num? ?? 0) > 0)
                                  (
                                    'بطولات',
                                    'Tournament',
                                    requests['tournaments'],
                                  ),
                              ])
                                ActionChip(
                                  key: ValueKey('owner-request-${item.$2}'),
                                  label: UiText(
                                    '${item.$1} · ${item.$3}',
                                    style: const TextStyle(fontSize: 11),
                                  ),
                                  onPressed: () => queue(item.$2),
                                ),
                            ],
                          ),
                        ),
                        ExperienceSection(
                          'مركز القرارات الذكية',
                          Icons.bolt_rounded,
                          actionLabel: 'كل القرارات',
                          action: () async {
                            final expected = contextKey;
                            await openPage(
                              context,
                              OwnerDecisionsPage(auth: widget.auth),
                            );
                            if (mounted && contextKey == expected) refresh();
                          },
                        ),
                        HomeDecisionCards(
                          data: {
                            ...cockpit,
                            'groupAttention':
                                summary['operations']['groupAttention'],
                          },
                          onGroup: (group) => openPage(
                            context,
                            OwnerGroupOperations(
                              auth: widget.auth,
                              id: textOf(group['id']),
                            ),
                          ),
                          onFollowUps: () => openPage(
                            context,
                            OwnerFollowUpsPage(auth: widget.auth),
                          ),
                          onReminders: (kind) => openPage(
                            context,
                            CollectionReminders(
                              auth: widget.auth,
                              branch: branch,
                              kind: kind,
                            ),
                          ),
                          onReport: report,
                          onCapacity: () => openPage(
                            context,
                            OwnerCapacityPage(
                              auth: widget.auth,
                              filters: scope,
                            ),
                          ),
                        ),
                        HomeCoaches(
                          auth: widget.auth,
                          items: rows(
                            obj(summary['operations'])['coachPerformance'],
                          ),
                          branches: rows(options['branches']),
                          filters: scope,
                        ),
                        OwnerVipMembers(
                          auth: widget.auth,
                          branch: branch,
                          asOf: textOf(summary['to']),
                        ),
                        FeaturedPlayers(
                          auth: widget.auth,
                          branch: branch,
                          asOf: textOf(summary['to']),
                        ),
                      ]),
                    );
                  },
                ),
        ),
      ),
    ),
  );
}
