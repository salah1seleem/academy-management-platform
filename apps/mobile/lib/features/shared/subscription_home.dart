import 'package:flutter/material.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/api/api_client.dart';
import '../../core/theme/ui_text.dart';
import '../../core/theme/academy_home_skin.dart';
import '../guardian/guardian_ui.dart';
import '../owner/home_destinations.dart';
import 'subscription_identity_card.dart';

const subscriptionDimensions = <String, String>{
  'branches': 'الفروع',
  'sports': 'الرياضات',
  'packages': 'الباقات',
  'groups': 'المجموعات',
  'days': 'أيام التدريب',
  'ages': 'الفئات العمرية',
  'months': 'نمو الاشتراكات',
};

class SubscriptionHomePanel extends StatefulWidget {
  const SubscriptionHomePanel({
    super.key,
    required this.auth,
    this.branchId = '',
    this.asOf,
    this.from,
    this.academyToday,
    this.dateFilters,
    this.onBranchChanged,
    this.inlineOwnerScope = false,
  });
  final AuthController auth;
  final String branchId;
  final String? asOf, from, academyToday;
  final Widget? dateFilters;
  final bool inlineOwnerScope;
  final ValueChanged<String>? onBranchChanged;
  @override
  State<SubscriptionHomePanel> createState() => _SubscriptionHomePanelState();
}

class _SubscriptionHomePanelState extends State<SubscriptionHomePanel> {
  String? get contextId => widget.auth.session?.selected == null
      ? null
      : '${widget.auth.session!.selected!.id}:${widget.auth.session!.selected!.contextRevision}';
  late String? opened = contextId;
  late String branch = widget.branchId;
  String sport = '', asOf = '', selected = '';
  bool showFilters = false, showBreakdown = false;
  Json? data;
  String? error;
  int epoch = 0;
  final Map<String, Future<Json>> cache = {};
  @override
  void initState() {
    super.initState();
    widget.auth.addListener(authorityChanged);
    load();
  }

  void authorityChanged() {
    if (mounted && opened != contextId) {
      epoch++;
      setState(() {
        data = null;
        cache.clear();
      });
    }
  }

  @override
  void didUpdateWidget(SubscriptionHomePanel old) {
    super.didUpdateWidget(old);
    if (old.auth != widget.auth) {
      old.auth.removeListener(authorityChanged);
      widget.auth.addListener(authorityChanged);
      opened = contextId;
    }
    if (old.auth != widget.auth ||
        old.branchId != widget.branchId ||
        old.asOf != widget.asOf ||
        old.from != widget.from) {
      branch = widget.branchId;
      asOf = '';
      load();
    }
  }

  @override
  void dispose() {
    widget.auth.removeListener(authorityChanged);
    epoch++;
    super.dispose();
  }

  Map<String, String> get filters => {
    if (widget.from?.isNotEmpty == true) 'from': widget.from!,
    if (branch.isNotEmpty) 'branchId': branch,
    if (sport.isNotEmpty) 'sportId': sport,
    if ((widget.asOf ?? asOf).isNotEmpty) 'asOf': widget.asOf ?? asOf,
  };
  Future<void> load() async {
    final attempt = ++epoch;
    if (mounted) {
      setState(() {
        data = null;
        error = null;
        cache.clear();
      });
    }
    if (opened == null || opened != contextId) return;
    try {
      final value = obj(
        await widget.auth.request(
          'GET',
          '/api/v1/subscriptions/home?${Uri(queryParameters: filters).query}',
        ),
      );
      if (![
            'total',
            'active',
            'expiring',
            'expired',
          ].every((k) => value[k] is num) ||
          value['dimensions'] is! List) {
        throw const ApiFailure(500, 'تعذر تحميل أعداد الاشتراكات.');
      }
      if (mounted && attempt == epoch && opened == contextId) {
        setState(() => data = value);
      }
    } catch (e) {
      if (mounted && attempt == epoch && opened == contextId) {
        setState(
          () => error = e is ApiFailure ? e.message : 'تعذر تحميل الاشتراكات.',
        );
      }
    }
  }

  void open({
    String state = '',
    String? dimension,
    String? value,
    String? scopeLabel,
    bool uniquePlayers = false,
  }) {
    final destination = {
      ...filters,
      if (data?['asOf'] != null) 'asOf': textOf(data!['asOf']),
      'dimension': ?dimension,
      'key': ?value,
    };
    if (dimension == 'months') destination.remove('from');
    openPage(
      context,
      HomeSubscriptionPlayers(
        auth: widget.auth,
        state: state,
        uniquePlayers: uniquePlayers,
        scopeLabel: scopeLabel,
        academyToday: widget.academyToday,
        filters: destination,
      ),
    );
  }

  Future<Json> breakdown(String dimension) {
    final query = Uri(
      queryParameters: {
        ...filters,
        'asOf': textOf(data!['asOf']),
        'dimension': dimension,
      },
    ).query;
    return cache.putIfAbsent(
      query,
      () async => obj(
        await widget.auth.request(
          'GET',
          '/api/v1/subscriptions/home/breakdown?$query',
        ),
      ),
    );
  }

  VoidCallback _toggleBreakdown(List<String> dimensions) =>
      () => setState(() {
        showBreakdown = !showBreakdown;
        if (showBreakdown && selected.isEmpty && dimensions.isNotEmpty) {
          selected = dimensions.first;
        }
      });

  @override
  Widget build(BuildContext context) {
    if (opened == null || opened != contextId) return const SizedBox.shrink();
    if (data == null) {
      return error == null
          ? const PendingHomeSection(
              label: 'جارٍ تحميل الاشتراكات',
              height: 272,
            )
          : GCard(
              child: Column(
                children: [
                  UiText(error!),
                  TextButton(
                    onPressed: load,
                    child: const UiText('إعادة تحميل الاشتراكات'),
                  ),
                ],
              ),
            );
    }
    final d = data!,
        owner = widget.auth.session!.selected!.role == 'AcademyOwner';
    final from = DateTime.tryParse('${d['from'] ?? widget.from}');
    final order = rows(d['sports']).length <= 1
        ? ['groups', 'days', 'ages', 'branches', 'packages', 'months']
        : [
            'branches',
            'sports',
            'groups',
            'days',
            'ages',
            'packages',
            'months',
          ];
    final dimensions = order
        .where((key) => (d['dimensions'] as List).contains(key))
        .toList();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (!widget.inlineOwnerScope || rows(d['sports']).length > 1)
          Row(
            children: [
              Expanded(
                child: UiText(
                  '${branch.isEmpty ? 'كل الفروع' : rows(d['branches']).where((b) => b['id'] == branch).firstOrNull?['name'] ?? 'الفرع المحدد'} · ${rows(d['sports']).length == 1
                      ? textOf(rows(d['sports']).first['name'])
                      : sport.isEmpty
                      ? 'كل الرياضات'
                      : rows(d['sports']).where((s) => s['id'] == sport).firstOrNull?['name'] ?? 'الرياضة المحددة'}',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ),
              TextButton.icon(
                key: const ValueKey('subscription-scope-toggle'),
                onPressed: () => setState(() => showFilters = !showFilters),
                icon: Icon(
                  showFilters ? Icons.close_rounded : Icons.tune_rounded,
                  size: 17,
                ),
                label: const UiText('تصفية'),
              ),
            ],
          ),
        if (!widget.inlineOwnerScope && from != null)
          Padding(
            padding: const EdgeInsets.only(bottom: 10),
            child: UiText(
              'بداية الفترة ${MaterialLocalizations.of(context).formatShortDate(from)}',
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ),
        if (showFilters) ...[
          if (!widget.inlineOwnerScope && rows(d['branches']).length > 1)
            SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              child: Row(
                children: [
                  for (final b in <Json>[
                    {'id': '', 'name': 'كل الفروع'},
                    ...rows(d['branches']),
                  ])
                    Padding(
                      padding: const EdgeInsetsDirectional.only(end: 6),
                      child: ChoiceChip(
                        label: UiText(textOf(b['name'])),
                        selected: branch == b['id'],
                        onSelected: (_) {
                          branch = textOf(b['id']);
                          if (widget.onBranchChanged != null) {
                            widget.onBranchChanged!(branch);
                          } else {
                            load();
                          }
                        },
                      ),
                    ),
                ],
              ),
            ),
          if (rows(d['sports']).length > 1)
            SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              child: Row(
                children: [
                  for (final s in <Json>[
                    {'id': '', 'name': 'كل الرياضات'},
                    ...rows(d['sports']),
                  ])
                    Padding(
                      padding: const EdgeInsetsDirectional.only(end: 6),
                      child: ChoiceChip(
                        label: UiText(textOf(s['name'])),
                        selected: sport == s['id'],
                        onSelected: (_) {
                          sport = textOf(s['id']);
                          load();
                        },
                      ),
                    ),
                ],
              ),
            ),
          if (!widget.inlineOwnerScope && widget.dateFilters != null)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 8),
              child: widget.dateFilters!,
            ),
        ],
        SubscriptionIdentityCard(
          data: d,
          owner: owner,
          onTotal: () => open(state: 'active'),
          onPlayers: () => open(uniquePlayers: true),
          onStatus: (state) => open(state: state),
          newSubscriptions: widget.inlineOwnerScope
              ? d['newSubscriptions'] as num?
              : null,
        ),
        const SizedBox(height: 10),
        if (owner)
          OutlinedButton(
            key: const ValueKey('subscription-breakdown-toggle'),
            onPressed: _toggleBreakdown(dimensions),
            style: OutlinedButton.styleFrom(
              padding: const EdgeInsets.symmetric(vertical: 11, horizontal: 12),
              backgroundColor: academyAccent(context).withValues(alpha: .09),
              side: BorderSide(
                color: academyAccent(context).withValues(alpha: .42),
              ),
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(14),
              ),
            ),
            child: Row(
              children: [
                Icon(
                  Icons.bar_chart_rounded,
                  color: academyAccent(context),
                  size: 25,
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const UiText(
                        'تحليل الاشتراكات',
                        style: TextStyle(
                          fontSize: 14,
                          fontWeight: FontWeight.w800,
                        ),
                      ),
                      UiText(
                        'توزيع الاشتراكات ونموها',
                        style: TextStyle(
                          fontSize: 11,
                          color: Theme.of(context).colorScheme.onSurfaceVariant,
                        ),
                      ),
                    ],
                  ),
                ),
                Icon(
                  showBreakdown
                      ? Icons.expand_less_rounded
                      : Icons.chevron_left_rounded,
                ),
              ],
            ),
          )
        else
          Align(
            alignment: AlignmentDirectional.centerEnd,
            child: TextButton.icon(
              key: const ValueKey('subscription-breakdown-toggle'),
              onPressed: _toggleBreakdown(dimensions),
              icon: Icon(
                showBreakdown
                    ? Icons.expand_less_rounded
                    : Icons.bar_chart_rounded,
                size: 18,
              ),
              label: const UiText('تحليل الاشتراكات'),
            ),
          ),
        if (showBreakdown) ...[
          const UiText(
            'فرز الاشتراكات بحسب',
            style: TextStyle(fontSize: 12, fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 6),
          SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            child: Row(
              children: [
                for (final dim in dimensions)
                  if (subscriptionDimensions.containsKey(dim) &&
                      (dim != 'sports' || rows(d['sports']).length > 1))
                    Padding(
                      padding: const EdgeInsetsDirectional.only(end: 6),
                      child: ChoiceChip(
                        label: UiText(subscriptionDimensions[dim]!),
                        selected: selected == dim,
                        onSelected: (_) => setState(
                          () => selected = selected == dim ? '' : dim,
                        ),
                      ),
                    ),
              ],
            ),
          ),
          if (selected.isNotEmpty)
            FutureBuilder<Json>(
              key: ValueKey('${filters.toString()}-$selected'),
              future: breakdown(selected),
              builder: (context, s) {
                if (s.hasError) {
                  return GCard(
                    child: Column(
                      children: [
                        const UiText('تعذر تحميل هذا التفصيل.'),
                        TextButton(
                          onPressed: () => setState(() => cache.clear()),
                          child: const UiText('إعادة تحميل التفصيل'),
                        ),
                      ],
                    ),
                  );
                }
                if (!s.hasData) {
                  return const PendingHomeSection(
                    label: 'جارٍ تحميل التفصيل',
                    height: 180,
                  );
                }
                final items = rows(s.data!['items']);
                final maximum = items.fold<num>(
                  0,
                  (m, r) => (r['total'] as num) > m ? r['total'] as num : m,
                );
                return GCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Heading(subscriptionDimensions[selected]!),
                      if (selected == 'months' && items.isNotEmpty)
                        const UiText(
                          'التسجيلات التي بدأت اشتراكاتها حتى كل تاريخ.',
                        ),
                      if (items.isEmpty)
                        const EmptyMessage('لا توجد اشتراكات في هذا النطاق.'),
                      for (final item in items)
                        InkWell(
                          onTap: () => open(
                            dimension: selected,
                            value: textOf(item['key']),
                            scopeLabel: textOf(item['name']),
                          ),
                          child: Padding(
                            padding: const EdgeInsets.symmetric(vertical: 10),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.stretch,
                              children: [
                                Row(
                                  children: [
                                    Expanded(
                                      child: UiText(textOf(item['name'])),
                                    ),
                                    const SizedBox(width: 8),
                                    UiText(
                                      '${item['total']}',
                                      style: const TextStyle(
                                        fontWeight: FontWeight.w800,
                                      ),
                                    ),
                                  ],
                                ),
                                if (selected != 'months' &&
                                    item['detail'] != null)
                                  UiText(
                                    textOf(item['detail']),
                                    style: const TextStyle(fontSize: 10),
                                  ),
                                const SizedBox(height: 6),
                                LinearProgressIndicator(
                                  value: maximum == 0
                                      ? 0
                                      : (item['total'] as num) / maximum,
                                  minHeight: 4,
                                  backgroundColor: Theme.of(context)
                                      .colorScheme
                                      .surfaceContainerHighest,
                                ),
                              ],
                            ),
                          ),
                        ),
                    ],
                  ),
                );
              },
            ),
        ],
      ],
    );
  }
}

/// A section appears only while its own request is pending; reduced motion is static.
class PendingHomeSection extends StatefulWidget {
  const PendingHomeSection({super.key, required this.label, this.height = 180});
  final String label;
  final double height;
  @override
  State<PendingHomeSection> createState() => _PendingHomeSectionState();
}

class _PendingHomeSectionState extends State<PendingHomeSection>
    with SingleTickerProviderStateMixin {
  late final motion = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 1600),
  );
  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (!MediaQuery.disableAnimationsOf(context) &&
        !MediaQuery.accessibleNavigationOf(context) &&
        TickerMode.valuesOf(context).enabled) {
      motion.repeat();
    } else {
      motion.stop();
    }
  }

  @override
  void dispose() {
    motion.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Semantics(
    label: widget.label,
    child: ExcludeSemantics(
      child: AnimatedBuilder(
        animation: motion,
        builder: (context, _) => Container(
          height: widget.height,
          margin: const EdgeInsets.symmetric(vertical: 8),
          padding: const EdgeInsets.all(18),
          decoration: BoxDecoration(
            color: Theme.of(context).colorScheme.onSurface
                .withValues(alpha: .025 + motion.value * .018),
            borderRadius: BorderRadius.circular(20),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Align(
                alignment: AlignmentDirectional.centerStart,
                child: Container(
                  width: 130,
                  height: 18,
                  color: Theme.of(context).colorScheme.onSurface
                      .withValues(alpha: .055),
                ),
              ),
              const Spacer(),
              Row(
                children: [
                  for (var i = 0; i < 3; i++)
                    Expanded(
                      child: Container(
                        height: 52,
                        margin: const EdgeInsets.all(4),
                        decoration: BoxDecoration(
                          color: Theme.of(context).colorScheme.onSurface
                              .withValues(alpha: .045),
                          borderRadius: BorderRadius.circular(10),
                        ),
                      ),
                    ),
                ],
              ),
            ],
          ),
        ),
      ),
    ),
  );
}
