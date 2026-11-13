import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/ui_text.dart';
import '../admin/native_structure_operations.dart';
import '../guardian/guardian_ui.dart';
import '../shared/academy_news.dart';
import '../shared/experience_ui.dart' show isoDay;
import '../shared/role_navigation.dart';
import '../shared/subscription_home.dart';
import 'collection_target.dart';
import 'decision_center.dart';
import 'home_destinations.dart';
import 'inventory_preview.dart';
import 'owner_cockpit.dart';
import 'owner_home_scope.dart';
import 'owner_feedback.dart';
import 'owner_honors_section.dart';
import 'owner_operations.dart';
import 'owner_reports.dart';
import 'owner_expenses.dart';
import 'refined_home_panels.dart';

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

enum _OwnerSection { summary, collections, statement, branches }

typedef _OwnerHomeScope = ({
  String branch,
  OwnerHomePeriod subscriptions,
  OwnerHomePeriod summary,
  OwnerHomePeriod collections,
  OwnerHomePeriod statement,
});

class _OwnerDashboardState extends State<OwnerDashboard> {
  String? identity;
  String branch = '';
  DateTime? today, trailingStart;
  Object? calendarError;
  OwnerHomePeriod subscriptions = const OwnerHomePeriod();
  OwnerHomePeriod summaryPeriod = const OwnerHomePeriod();
  OwnerHomePeriod collectionsPeriod = const OwnerHomePeriod();
  OwnerHomePeriod statementPeriod = const OwnerHomePeriod();
  final values = <_OwnerSection, Object?>{};
  final requests = <_OwnerSection, Object>{};
  Object? calendarRequest;
  bool restoredScope = false;
  (String, AuthController, String?) get scopeKey =>
      ('owner-home-scope', widget.auth, identity);

  String? get contextKey {
    final selected = widget.auth.session?.selected;
    if (selected?.role != 'AcademyOwner') return null;
    return '${selected!.academyId}:${selected.id}:${selected.contextRevision}';
  }

  @override
  void initState() {
    super.initState();
    identity = contextKey;
    widget.auth.addListener(authChanged);
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (restoredScope) return;
    restoredScope = true;
    final saved = PageStorage.maybeOf(context)
        ?.readState(context, identifier: scopeKey);
    if (identity != null && saved is _OwnerHomeScope) {
      branch = saved.branch;
      subscriptions = saved.subscriptions;
      summaryPeriod = saved.summary;
      collectionsPeriod = saved.collections;
      statementPeriod = saved.statement;
    }
    _refresh();
  }

  void _rememberScope() {
    if (identity == null || identity != contextKey) return;
    PageStorage.maybeOf(context)?.writeState(context, (
      branch: branch,
      subscriptions: subscriptions,
      summary: summaryPeriod,
      collections: collectionsPeriod,
      statement: statementPeriod,
    ), identifier: scopeKey);
  }

  @override
  void didUpdateWidget(covariant OwnerDashboard oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.auth == widget.auth) return;
    oldWidget.auth.removeListener(authChanged);
    widget.auth.addListener(authChanged);
    _resetScope();
  }

  @override
  void dispose() {
    widget.auth.removeListener(authChanged);
    super.dispose();
  }

  void authChanged() {
    if (mounted && identity != contextKey) _resetScope();
  }

  void _resetScope() {
    setState(() {
      identity = contextKey;
      branch = '';
      subscriptions = const OwnerHomePeriod();
      summaryPeriod = const OwnerHomePeriod();
      collectionsPeriod = const OwnerHomePeriod();
      statementPeriod = const OwnerHomePeriod();
      today = trailingStart = null;
      calendarError = null;
      calendarRequest = null;
      values.clear();
      requests.clear();
    });
    _refresh();
  }

  Map<String, String> _filters(OwnerHomePeriod period) {
    final range = period.dates(today!, trailingStart!);
    String key(DateTime date) => date.toIso8601String().split('T').first;
    return {
      'from': key(range.start),
      'to': key(range.end),
      if (branch.isNotEmpty) 'branchId': branch,
    };
  }

  Future<void> _refresh() async {
    if (identity == null) return;
    final expected = identity, token = Object();
    final validateBranch = today == null && branch.isNotEmpty;
    calendarRequest = token;
    if (calendarError != null) setState(() => calendarError = null);
    try {
      final reads = await Future.wait<Object?>([
        widget.auth.request('GET', '/api/v1/academy/calendar'),
        if (validateBranch) _loadSection(_OwnerSection.branches),
      ]);
      final calendar = obj(reads.first);
      final end = DateTime.parse(textOf(calendar['today']));
      final start = DateTime.parse(textOf(calendar['trailingMonthFrom']));
      if (!mounted || expected != contextKey || calendarRequest != token) {
        return;
      }
      final options = values[_OwnerSection.branches];
      final branchRemoved =
          validateBranch &&
          options is Json &&
          options['branches'] is List &&
          !rows(options['branches']).any((item) => item['id'] == branch);
      setState(() {
        if (branchRemoved) branch = '';
        today = end;
        trailingStart = start;
      });
      if (branchRemoved) _rememberScope();
      await Future.wait([
        for (final section in _OwnerSection.values)
          if (!validateBranch || section != _OwnerSection.branches)
            _loadSection(section),
      ]);
    } catch (error) {
      if (mounted && expected == contextKey && calendarRequest == token) {
        setState(() => calendarError = error);
      }
    }
  }

  Future<void> _loadSection(_OwnerSection section) async {
    if (identity == null ||
        (today == null && section != _OwnerSection.branches)) {
      return;
    }
    final expected = identity, token = Object();
    final (path, query) = switch (section) {
      _OwnerSection.summary => (
        '/api/v1/reports/owner-cockpit',
        _filters(summaryPeriod),
      ),
      _OwnerSection.collections => (
        '/api/v1/reports/owner-summary',
        {
          ..._filters(collectionsPeriod),
          'includeSubscriptionAnalytics': 'false',
        },
      ),
      _OwnerSection.statement => (
        '/api/v1/reports/operating-statement',
        {..._filters(statementPeriod), 'includeHistory': 'true'},
      ),
      _OwnerSection.branches => (
        '/api/v1/manage/structure/options',
        const <String, String>{},
      ),
    };
    requests[section] = token;
    setState(() => values.remove(section));
    Object? result;
    try {
      result = await widget.auth.request(
        'GET',
        Uri(
          path: path,
          queryParameters: query.isEmpty ? null : query,
        ).toString(),
      );
    } catch (error) {
      result = error is ApiFailure
          ? error
          : const ApiFailure(500, 'تعذر تحميل هذا القسم.');
    }
    if (mounted && expected == contextKey && requests[section] == token) {
      setState(() => values[section] = result);
    }
  }

  void _changeBranch(String id) {
    if (branch == id) return;
    setState(() => branch = id);
    _rememberScope();
    _loadSection(_OwnerSection.summary);
    _loadSection(_OwnerSection.collections);
    _loadSection(_OwnerSection.statement);
  }

  Widget _section(
    _OwnerSection section,
    String name,
    Widget Function(Json) build,
  ) {
    final value = values[section];
    if (value is Map<String, dynamic>) return build(value);
    if (value == null) {
      return PendingHomeSection(label: 'جارٍ تحميل $name', height: 138);
    }
    return GCard(
      child: Column(
        children: [
          UiText(value is ApiFailure ? value.message : 'تعذر تحميل $name.'),
          TextButton(
            onPressed: () => _loadSection(section),
            child: UiText('إعادة تحميل $name'),
          ),
        ],
      ),
    );
  }

  Widget _period(
    String scope,
    OwnerHomePeriod period,
    ValueChanged<OwnerHomePeriod> change,
  ) => OwnerPeriodControls(
    key: ValueKey('owner-period-$scope'),
    sectionLabel: switch (scope) {
      'subscriptions' => 'الاشتراكات',
      'summary' => 'ملخص الأكاديمية',
      'collections' => 'التحصيلات',
      _ => 'الأرباح والخسائر',
    },
    period: period,
    today: today!,
    trailingStart: trailingStart!,
    onChanged: (next) {
      change(next);
      _rememberScope();
    },
  );

  void _report(String name, Map<String, String> filters) => openPage(
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

  void _target(List<Json> branches) async {
    final scope = _filters(summaryPeriod);
    final end = isoDay(today!);
    final saved = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (context) => SizedBox(
        height: MediaQuery.sizeOf(context).height * .65,
        child: CollectionTargetPage(
          auth: widget.auth,
          from: scope['from']!,
          to: scope['to']!,
          branchId: scope['branchId'] ?? '',
          branchName: branch.isEmpty
              ? 'كل الفروع'
              : textOf(
                  branches
                          .where((b) => b['id'] == branch)
                          .firstOrNull?['arabicName'] ??
                      'الفرع المحدد',
                ),
          usesMonthlyDefault:
              branch.isEmpty &&
              scope['to'] == end &&
              scope['from'] == '${end.substring(0, 7)}-01',
        ),
      ),
    );
    if (saved == true && mounted) await _loadSection(_OwnerSection.summary);
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    body: SafeArea(
      child: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 720),
          child: identity == null
              ? const SizedBox.shrink()
              : RefreshIndicator(
                  onRefresh: _refresh,
                  child: gList(_content(), key: ValueKey(identity)),
                ),
        ),
      ),
    ),
  );

  List<Widget> _content() {
    final header = [
      if (widget.header != null) widget.header!,
      const SizedBox(height: 8),
    ];
    if (today == null || trailingStart == null) {
      return [
        ...header,
        if (calendarError == null)
          const PendingHomeSection(label: 'جارٍ تحميل الرئيسية', height: 168)
        else
          GCard(
            child: Column(
              children: [
                const UiText('تعذر تحميل تاريخ الأكاديمية.'),
                TextButton(
                  onPressed: _refresh,
                  child: const UiText('إعادة المحاولة'),
                ),
              ],
            ),
          ),
      ];
    }
    final branches = values[_OwnerSection.branches];
    final branchItems = branches is Map<String, dynamic>
        ? rows(branches['branches'] ?? <Json>[])
        : <Json>[];
    final subscriptionScope = _filters(subscriptions);
    final summaryScope = _filters(summaryPeriod);
    final collectionScope = _filters(collectionsPeriod);
    final statementScope = _filters(statementPeriod);
    final liveScope = _filters(const OwnerHomePeriod());
    final collectionData = values[_OwnerSection.collections];
    final cockpitData = values[_OwnerSection.summary];
    final operations = collectionData is Json
        ? obj(collectionData['operations'] ?? <String, dynamic>{})
        : <String, dynamic>{};
    return [
      ...header,
      _section(
        _OwnerSection.branches,
        'الفروع',
        (_) => OwnerBranchRail(
          branches: branchItems,
          branchId: branch,
          academy: widget.auth.session!.selected!.academyName,
          onChanged: _changeBranch,
        ),
      ),
      if ((operations['openSafetyFeedback'] as num? ?? 0) > 0)
        OutlinedButton.icon(
          onPressed: () async {
            final expected = contextKey;
            await openPage(
              context,
              OwnerFeedbackInbox(
                auth: widget.auth,
                initialFilters: {
                  if (branch.isNotEmpty) 'branchId': branch,
                  'safetyOnly': 'true',
                  'status': 'Unresolved',
                },
              ),
            );
            if (mounted && expected == contextKey) {
              await _loadSection(_OwnerSection.collections);
            }
          },
          icon: const Icon(Icons.health_and_safety_outlined),
          label: UiText(
            'بلاغات سلامة تحتاج مراجعتك · ${operations['openSafetyFeedback']}',
          ),
        ),
      const _HomeHeading('الاشتراكات'),
      _period(
        'subscriptions',
        subscriptions,
        (next) => setState(() => subscriptions = next),
      ),
      SubscriptionHomePanel(
        key: ValueKey('owner-subscriptions-$identity'),
        auth: widget.auth,
        branchId: branch,
        asOf: subscriptionScope['to'],
        from: subscriptionScope['from'],
        academyToday: today == null ? null : isoDay(today!),
        inlineOwnerScope: true,
      ),
      const _HomeHeading('ملخص الأكاديمية'),
      _period('summary', summaryPeriod, (next) {
        setState(() => summaryPeriod = next);
        _loadSection(_OwnerSection.summary);
      }),
      _section(
        _OwnerSection.summary,
        'ملخص الأكاديمية',
        (cockpit) => AcademySummaryPanel(
          data: cockpit,
          showHeading: false,
          showWeeklyTrend: summaryScope['from'] != summaryScope['to'],
          onPlayers: () => openPage(
            context,
            HomeSubscriptionPlayers(
              auth: widget.auth,
              filters: summaryScope,
              uniquePlayers: true,
            ),
          ),
          onGroups: () => openPage(
            context,
            NativeGroupPicker(
              auth: widget.auth,
              title: 'المجموعات',
              branchId: branch,
              open: (id) => OwnerGroupOperations(auth: widget.auth, id: id),
            ),
          ),
          onWeek: (week) => _report('financial', {
            ...summaryScope,
            'from': textOf(week['from']),
            'to': textOf(week['to']),
          }),
          onTarget: () => _target(branchItems),
          onReport: (name) => _report(
            name,
            name == 'receivables'
                ? {if (branch.isNotEmpty) 'branchId': branch}
                : summaryScope,
          ),
          onCapacity: () => openPage(
            context,
            OwnerCapacityPage(
              auth: widget.auth,
              filters: {if (branch.isNotEmpty) 'branchId': branch},
            ),
          ),
        ),
      ),
      OwnerDecisionCenter(
        auth: widget.auth,
        branchId: branch,
        pendingPayments:
            (operations['pendingPaymentQueueBacklog'] as num?)?.toInt() ?? 0,
        onPayments: () async {
          final expected = contextKey;
          await openPage(
            context,
            NativePaymentRequests(
              auth: widget.auth,
              filters: {if (branch.isNotEmpty) 'branchId': branch},
            ),
          );
          if (mounted && expected == contextKey) {
            await _loadSection(_OwnerSection.collections);
          }
        },
      ),
      _HomeHeading(
        'التحصيلات',
        onOpen: () => _report('financial', collectionScope),
      ),
      _period('collections', collectionsPeriod, (next) {
        setState(() => collectionsPeriod = next);
        _loadSection(_OwnerSection.collections);
      }),
      _section(
        _OwnerSection.collections,
        'التحصيلات',
        (summary) => HomeCollectionCards(
          data: obj(summary['collections']),
          currency: summary['currency'],
          referenceDay: textOf(summary['collectionDay']),
          periodOnly: true,
          onOpen: (_, method) => _report('financial', {
            ...collectionScope,
            if (method.isNotEmpty) 'provider': method,
          }),
          onSource: (_, source) => _report('financial', {
            ...collectionScope,
            'collectionType': textOf(source['collectionType']),
            if (source['subscriptionKind'] != null)
              'subscriptionKind': textOf(source['subscriptionKind']),
          }),
          onBranch: (_, id) =>
              _report('financial', {...collectionScope, 'branchId': id}),
        ),
      ),
      _HomeHeading(
        'تقرير الأرباح والخسائر',
        onOpen: () => _report('operating', statementScope),
      ),
      _period('statement', statementPeriod, (next) {
        setState(() => statementPeriod = next);
        _loadSection(_OwnerSection.statement);
      }),
      _section(
        _OwnerSection.statement,
        'الموقف التشغيلي',
        (statement) => HomeOperatingStatement(
          data: statement,
          showTitle: false,
          showHistoryLabel: true,
          showReceivables: false,
          onOpen: null,
          expenseAction: (line) {
            final source = operatingExpenseSource(
              line,
              statement,
              statementScope,
            );
            return source == null ? null : () => _report('expenses', source);
          },
          onMonth: (month) => _report('operating', {
            ...statementScope,
            'from': textOf(month['from']),
            'to': textOf(month['to']),
          }),
        ),
      ),
      OwnerInventoryPreview(auth: widget.auth),
      if (collectionData is Json)
        OwnerHonorsSection(
          auth: widget.auth,
          coaches: rows(operations['coachPerformance'] ?? <Json>[]),
          branches: branchItems,
          filters: liveScope,
          branch: branch,
          asOf: liveScope['to']!,
          fallbacks: obj(
            obj(
                  cockpitData is Json
                      ? cockpitData['home'] ?? <String, dynamic>{}
                      : <String, dynamic>{},
                )['highlightPortraits'] ??
                <String, dynamic>{},
          ),
        ),
      NewsStrip(auth: widget.auth),
    ];
  }
}

class _HomeHeading extends StatelessWidget {
  const _HomeHeading(this.title, {this.onOpen});

  final String title;
  final VoidCallback? onOpen;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(top: 14, bottom: 6),
    child: Row(
      children: [
        Expanded(
          child: UiText(
            title,
            style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w800),
          ),
        ),
        if (onOpen != null)
          TextButton.icon(
            onPressed: onOpen,
            icon: const Icon(Icons.arrow_forward_rounded, size: 16),
            label: const UiText('التفاصيل'),
          ),
      ],
    ),
  );
}
