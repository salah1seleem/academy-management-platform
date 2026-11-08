import 'inventory_preview.dart';
import '../../core/api/api_client.dart';
import '../shared/subscription_home.dart';
import 'owner_honors_section.dart';
import '../admin/native_structure_operations.dart';

import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/experience_ui.dart';
import '../shared/role_navigation.dart';
import 'owner_reports.dart';
import 'owner_branch_rail.dart';
import '../../core/theme/academy_brand.dart';
import 'owner_operations.dart';
import 'home_destinations.dart';
import 'owner_cockpit.dart';
import 'refined_home_panels.dart';
import 'collection_reminders.dart';
import 'collection_target.dart';
import 'owner_followups.dart';

class OwnerDashboard extends StatefulWidget {
  const OwnerDashboard({
    super.key,
    required this.auth,
    required this.accountPage,
    this.header,
  });
  final AuthController auth;
  final Widget accountPage;
  final Widget? header;
  @override
  State<OwnerDashboard> createState() => _OwnerDashboardState();
}

class _OwnerDashboardState extends State<OwnerDashboard> {
  String from = '', to = '', branch = '';
  String? selectedQueue;
  bool cleared = false;
  String? identity;
  late Future<List<Object?>> data;
  List<Object?>? partial;
  List<String> paths = [];
  int generation = 0;
  String? get contextKey =>
      widget.auth.session?.selected?.role != 'AcademyOwner'
      ? null
      : '${widget.auth.session!.selected!.academyId}:${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
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
      generation++;
      partial = null;
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
    final expected = contextKey, attempt = ++generation;
    partial = null;
    if (expected == null) return <Object?>[];
    if (!cleared && from.isEmpty && to.isEmpty) {
      final calendar = obj(
        await widget.auth.request('GET', '/api/v1/academy/calendar'),
      );
      if (expected != contextKey || attempt != generation) return <Object?>[];
      from = textOf(calendar['trailingMonthFrom']);
      to = textOf(calendar['today']);
    }
    final query = Uri(queryParameters: filters).query;
    paths = [
      '/api/v1/reports/owner-summary?$query&includeSubscriptionAnalytics=false',
      '/api/v1/reports/owner-cockpit?$query',
      '/api/v1/reports/operating-statement?$query&includeMonthly=true',
      '/api/v1/manage/structure/options',
    ];
    final sources = List<String>.from(paths),
        values = List<Object?>.filled(4, null);
    if (mounted && expected == contextKey && attempt == generation) {
      setState(() => partial = values);
    }
    await Future.wait([
      for (var i = 0; i < sources.length; i++)
        () async {
          Object? result;
          try {
            result = await widget.auth.request('GET', sources[i]);
          } catch (e) {
            result = e is ApiFailure
                ? e
                : const ApiFailure(500, 'تعذر تحميل هذا القسم.');
          }
          if (!mounted || expected != contextKey || attempt != generation) {
            return;
          }
          values[i] = result;
          setState(() {
            final next = List<Object?>.from(partial ?? values);
            next[i] = result;
            partial = next;
          });
        }(),
    ]);
    return values;
  }

  Future<void> retrySource(int index) async {
    if (contextKey == null || index >= paths.length || partial == null) return;
    final expected = contextKey, attempt = generation, path = paths[index];
    setState(() => partial![index] = null);
    Object? value;
    try {
      value = await widget.auth.request('GET', path);
    } catch (e) {
      value = e is ApiFailure
          ? e
          : const ApiFailure(500, 'تعذر تحميل هذا القسم.');
    }
    if (mounted && expected == contextKey && attempt == generation) {
      setState(() => partial![index] = value);
    }
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
                    if (!snapshot.hasData && partial == null) {
                      return RefreshIndicator(
                        onRefresh: () async {
                          refresh();
                          await data;
                        },
                        child: gList([
                          if (widget.header != null) ...[
                            widget.header!,
                            const SizedBox(height: 10),
                          ],
                          SubscriptionHomePanel(
                            key: ValueKey('owner-subscriptions-$identity'),
                            auth: widget.auth,
                            branchId: branch,
                            asOf: to.isEmpty ? null : to,
                            from: from.isEmpty ? null : from,
                            dateFilters: ScopeDateTiles(
                              compact: true,
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

                            onBranchChanged: (v) => setState(() {
                              branch = v;
                              data = load();
                            }),
                          ),
                          if (snapshot.hasError)
                            GCard(
                              child: Column(
                                children: [
                                  const UiText('تعذر تحميل ملخص الأكاديمية.'),
                                  TextButton(
                                    onPressed: refresh,
                                    child: const UiText('إعادة المحاولة'),
                                  ),
                                ],
                              ),
                            )
                          else
                            const PendingHomeSection(
                              label: 'جارٍ تحميل ملخص الأكاديمية',
                              height: 260,
                            ),
                        ]),
                      );
                    }
                    final values = partial ?? snapshot.data!;
                    bool ready(int i) =>
                        values.length > i && values[i] is Map<String, dynamic>;
                    Widget sectionState(
                      int i,
                      String name, {
                      double height = 200,
                    }) => values[i] == null
                        ? PendingHomeSection(
                            label: 'جارٍ تحميل $name',
                            height: height,
                          )
                        : GCard(
                            child: Column(
                              children: [
                                UiText(
                                  values[i] is ApiFailure
                                      ? (values[i] as ApiFailure).message
                                      : 'تعذر تحميل $name.',
                                ),
                                TextButton(
                                  onPressed: () => retrySource(i),
                                  child: UiText('إعادة تحميل $name'),
                                ),
                              ],
                            ),
                          );
                    final summary = ready(0)
                            ? obj(values[0])
                            : <String, dynamic>{},
                        cockpit = ready(1)
                            ? obj(values[1])
                            : <String, dynamic>{},
                        statement = ready(2)
                            ? obj(values[2])
                            : <String, dynamic>{},
                        options = ready(3)
                            ? obj(values[3])
                            : <String, dynamic>{};
                    final requests = obj(
                          summary['paymentRequests'] ?? <String, dynamic>{},
                        ),
                        collections = obj(
                          summary['collections'] ?? <String, dynamic>{},
                        ),
                        scope = {...filters};
                    void queue([String type = '']) {
                      setState(() => selectedQueue = type);
                      openPage(
                        context,
                        NativePaymentRequests(
                          auth: widget.auth,
                          filters: {
                            ...scope,
                            if (type.isNotEmpty) 'type': type,
                          },
                        ),
                      );
                    }

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
                        if (widget.header != null) ...[
                          widget.header!,
                          const SizedBox(height: 10),
                        ],
                        if (ready(3))
                          OwnerBranchRail(
                            branches: rows(options['branches'] ?? <Json>[]),
                            branchId: branch,
                            academy: BrandScope.maybeOf(context)?.name ?? '',
                            onChanged: (id) => setState(() {
                              branch = id;
                              data = load();
                            }),
                          ),
                        SubscriptionHomePanel(
                          key: ValueKey('owner-subscriptions-$identity'),
                          auth: widget.auth,
                          branchId: branch,
                          asOf: to.isEmpty ? null : to,
                          from: from.isEmpty ? null : from,
                          dateFilters: ScopeDateTiles(
                            compact: true,
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

                          onBranchChanged: (v) => setState(() {
                            branch = v;
                            data = load();
                          }),
                        ),
                        if (!ready(1))
                          sectionState(1, 'ملخص الأكاديمية', height: 260)
                        else
                          AcademySummaryPanel(
                            data: cockpit,
                            onPlayers: () => openPage(
                              context,
                              HomeSubscriptionPlayers(
                                auth: widget.auth,
                                filters: filters,
                                uniquePlayers: true,
                              ),
                            ),
                            onGroups: () => openPage(
                              context,
                              NativeGroupPicker(
                                auth: widget.auth,
                                title: 'المجموعات',
                                branchId: branch,
                                open: (id) => OwnerGroupOperations(
                                  auth: widget.auth,
                                  id: id,
                                ),
                              ),
                            ),

                            onWeek: (week) => openPage(
                              context,
                              OwnerReports(
                                auth: widget.auth,
                                initialReport: 'financial',
                                initialFilters: {
                                  if (branch.isNotEmpty) 'branchId': branch,
                                  'from': textOf(week['from']),
                                  'to': textOf(week['to']),
                                },
                              ),
                            ),
                            onTarget: () async {
                              if (from.isEmpty || to.isEmpty) {
                                ScaffoldMessenger.of(context).showSnackBar(
                                  const SnackBar(
                                    content: UiText(
                                      'حدد فترة أولًا لضبط هدف التحصيل.',
                                    ),
                                  ),
                                );
                                return;
                              }
                              final saved = await showModalBottomSheet<bool>(
                                context: context,
                                isScrollControlled: true,
                                builder: (_) => SizedBox(
                                  height:
                                      MediaQuery.sizeOf(context).height * .65,
                                  child: CollectionTargetPage(
                                    auth: widget.auth,
                                    from: from,
                                    to: to,
                                    branchId: branch,
                                    branchName: branch.isEmpty
                                        ? 'كل الفروع'
                                        : textOf(
                                            rows(
                                                      options['branches'] ??
                                                          <Json>[],
                                                    )
                                                    .where(
                                                      (b) => b['id'] == branch,
                                                    )
                                                    .firstOrNull?['arabicName'] ??
                                                'الفرع المحدد',
                                          ),
                                    initialAmount: obj(
                                      cockpit['target'],
                                    )['amount'],
                                  ),
                                ),
                              );
                              if (saved == true && mounted) refresh();
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
                        if (!ready(0) || !ready(1))
                          sectionState(!ready(0) ? 0 : 1, 'القرارات والمتابعات')
                        else ...[
                          ExperienceSection(
                            'خطة اليوم',
                            Icons.bolt_rounded,
                            actionLabel: 'كل القرارات',
                            action: () async {
                              final expected = contextKey;
                              await openPage(
                                context,
                                OwnerDecisionsPage(
                                  auth: widget.auth,
                                  filters: filters,
                                ),
                              );
                              if (mounted && contextKey == expected) {
                                refresh();
                              }
                            },
                          ),
                          HomeDecisionCards(
                            data: {
                              ...cockpit,
                              'paymentRequests': requests,
                              'underAssignedCoaches':
                                  summary['operations']['underAssignedCoaches'],
                              'groupAttention':
                                  summary['operations']['groupAttention'],
                            },
                            onCoach: (coach) async {
                              final expected = contextKey;
                              await openPage(
                                context,
                                NativeCoachAssignment(
                                  auth: widget.auth,
                                  coachId: textOf(coach['id']),
                                  branchId: branch.isEmpty ? null : branch,
                                ),
                              );
                              if (mounted && contextKey == expected) {
                                refresh();
                              }
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
                            onPayments: () => queue(),
                            onReport: report,
                            onCapacity: () => openPage(
                              context,
                              OwnerCapacityPage(
                                auth: widget.auth,
                                filters: scope,
                              ),
                            ),
                          ),
                        ],
                        if (!ready(0))
                          sectionState(0, 'التحصيلات')
                        else
                          HomeCollectionCards(
                            receivables: statement['receivables'] as num?,
                            onReceivables: () => openPage(
                              context,
                              OwnerReports(
                                auth: widget.auth,
                                initialReport: 'receivables',
                                initialFilters: scope,
                              ),
                            ),
                            data: collections,
                            currency: summary['currency'],
                            referenceDay: textOf(summary['collectionDay']),
                            onOpen: collection,
                            onSource: (daily, source) => openPage(
                              context,
                              OwnerReports(
                                auth: widget.auth,
                                initialReport: 'financial',
                                initialFilters: {
                                  if (branch.isNotEmpty) 'branchId': branch,
                                  'from': daily
                                      ? textOf(summary['collectionDay'])
                                      : textOf(summary['collectionMonthFrom']),
                                  'to': textOf(summary['collectionDay']),
                                  'collectionType': textOf(
                                    source['collectionType'],
                                  ),
                                  if (source['subscriptionKind'] != null)
                                    'subscriptionKind': textOf(
                                      source['subscriptionKind'],
                                    ),
                                },
                              ),
                            ),
                            onBranch: (daily, id) => openPage(
                              context,
                              OwnerReports(
                                auth: widget.auth,
                                initialReport: 'financial',
                                initialFilters: {
                                  'branchId': id,
                                  'from': daily
                                      ? textOf(summary['collectionDay'])
                                      : textOf(summary['collectionMonthFrom']),
                                  'to': textOf(summary['collectionDay']),
                                },
                              ),
                            ),
                          ),
                        if (!ready(2))
                          sectionState(2, 'الموقف التشغيلي')
                        else
                          HomeOperatingStatement(
                            data: statement,
                            showReceivables: false,
                            onOpen: () => report('operating'),
                            onMonth: (month) => openPage(
                              context,
                              OwnerReports(
                                auth: widget.auth,
                                initialReport: 'operating',
                                initialFilters: {
                                  ...filters,
                                  'from': textOf(month['from']),
                                  'to': textOf(month['to']),
                                  'allDates': 'false',
                                },
                              ),
                            ),
                          ),
                        OwnerInventoryPreview(auth: widget.auth),
                        if (!ready(0) || !ready(3))
                          sectionState(!ready(0) ? 0 : 3, 'نجوم الأكاديمية')
                        else
                          OwnerHonorsSection(
                            auth: widget.auth,
                            coaches: rows(
                              obj(summary['operations'])['coachPerformance'],
                            ),
                            branches: rows(options['branches'] ?? <Json>[]),
                            filters: scope,
                            branch: branch,
                            asOf: textOf(summary['to']),
                            fallbacks: obj(
                              obj(
                                    cockpit['home'] ?? <String, dynamic>{},
                                  )['highlightPortraits'] ??
                                  <String, dynamic>{},
                            ),
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
