import 'package:flutter/material.dart';

import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/experience_ui.dart';
import 'home_trend.dart';
import 'summary_hero.dart';
import 'swipe_card_deck.dart';
import '../../core/theme/academy_home_skin.dart';
import 'summary_surface.dart';
import 'operating_monthly_chart.dart';

class AcademySummaryPanel extends StatelessWidget {
  const AcademySummaryPanel({
    super.key,
    required this.data,
    required this.onReport,
    required this.onCapacity,
    this.onTarget,
    this.onWeek,
    this.dateFilters,
    this.showHeading = true,
    this.showWeeklyTrend = true,
    this.onGroups,
    this.onPlayers,
  });
  final Widget? dateFilters;
  final bool showHeading, showWeeklyTrend;
  final VoidCallback? onGroups, onPlayers;
  final Json data;
  final ValueChanged<String> onReport;
  final VoidCallback onCapacity;
  final VoidCallback? onTarget;
  final ValueChanged<Json>? onWeek;
  @override
  Widget build(BuildContext context) {
    final c = obj(data['collections']),
        overdue = data['overdue'] is Json
            ? obj(data['overdue'])
            : const <String, dynamic>{},
        attendance = obj(data['attendance']),
        capacity = obj(data['capacity']),
        members = obj(data['members'] ?? <String, dynamic>{});
    final amount = overdue['amount'];
    final players = overdue['players'];
    final hasAmount = amount is num && amount.isFinite && amount >= 0;
    final hasPlayers = players is int && players >= 0;
    final asOf = DateTime.tryParse('${overdue['asOf']}');
    final colors = Theme.of(context).colorScheme;
    Widget measure(
      String title,
      String value,
      Color color, {
      bool numeric = true,
    }) {
      final text = UiText(
        value,
        textDirection: numeric ? TextDirection.ltr : null,
        maxLines: numeric ? 1 : null,
        softWrap: !numeric,
        style: TextStyle(
          fontSize: 22,
          fontWeight: FontWeight.w600,
          color: color,
        ),
      );
      return Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          UiText(title, style: const TextStyle(fontSize: 11)),
          const SizedBox(height: 4),
          if (numeric)
            FittedBox(
              fit: BoxFit.scaleDown,
              alignment: AlignmentDirectional.centerStart,
              child: text,
            )
          else
            text,
        ],
      );
    }

    Widget tile(Widget content, VoidCallback open) => Material(
      color: colors.surface,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(12),
        side: BorderSide(color: colors.outlineVariant),
      ),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: open,
        child: Padding(padding: const EdgeInsets.all(9), child: content),
      ),
    );
    Widget pair(Widget first, Widget second) => IntrinsicHeight(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Expanded(child: first),
          const SizedBox(width: 8),
          Expanded(child: second),
        ],
      ),
    );
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (showHeading)
          Padding(
            padding: const EdgeInsets.only(top: 4, bottom: 6),
            child:
                MediaQuery.textScalerOf(context).scale(12) > 18 &&
                    dateFilters != null
                ? Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const UiText(
                        'ملخص الأكاديمية',
                        style: TextStyle(
                          fontSize: 16,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                      const SizedBox(height: 8),
                      dateFilters!,
                    ],
                  )
                : Row(
                    children: [
                      const Expanded(
                        child: UiText(
                          'ملخص الأكاديمية',
                          style: TextStyle(
                            fontSize: 16,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ),
                      ?dateFilters,
                    ],
                  ),
          ),
        SummarySurface(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              SummaryHero(
                data: data,
                onReport: () => onReport('financial'),
                onTarget: onTarget,
              ),
              if (showWeeklyTrend &&
                  rows(c['weekly'] ?? <Json>[]).isNotEmpty) ...[
                const SizedBox(height: 8),
                Divider(color: Theme.of(context).colorScheme.outline),
                const SizedBox(height: 8),
                HomeTrend(
                  items: rows(c['weekly'] ?? <Json>[]),
                  monthly: false,
                  currency: data['currency'],
                  onOpen: onWeek,
                ),
              ],
              const SizedBox(height: 12),
              const UiText(
                'خلال الفترة',
                style: TextStyle(fontSize: 12, fontWeight: FontWeight.w700),
              ),
              const SizedBox(height: 6),
              pair(
                tile(
                  measure(
                    'متوسط الحضور',
                    attendance['percentage'] == null
                        ? '—'
                        : '${attendance['percentage']}%',
                    ExperienceColors.positive(context),
                  ),
                  () => onReport('attendance'),
                ),
                tile(
                  measure(
                    'عدد اللاعبين',
                    '${members['players'] ?? '—'}',
                    ExperienceColors.information(context),
                  ),
                  onPlayers ?? (() => onReport('renewals')),
                ),
              ),
              const SizedBox(height: 12),
              const UiText(
                'الوضع الحالي',
                style: TextStyle(fontSize: 12, fontWeight: FontWeight.w700),
              ),
              const SizedBox(height: 6),
              tile(
                Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (!hasAmount)
                      const UiText('رصيد المستحقات المتأخرة غير متاح')
                    else if (amount == 0 && players == 0)
                      const UiText('لا توجد مستحقات متأخرة')
                    else
                      pair(
                        Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            const UiText(
                              'مستحقات متأخرة',
                              style: TextStyle(fontSize: 11),
                            ),
                            const SizedBox(height: 4),
                            MoneyText(
                              amount,
                              data['currency'],
                              size: 22,
                              color: ExperienceColors.negative(context),
                            ),
                          ],
                        ),
                        measure(
                          'لاعبون عليهم متأخرات',
                          hasPlayers ? '$players' : 'غير متاح',
                          ExperienceColors.warning(context),
                          numeric: hasPlayers,
                        ),
                      ),
                    if (hasAmount && asOf != null) ...[
                      const SizedBox(height: 6),
                      UiText(
                        'الرصيد حتى ${sessionDateLabel(asOf)}',
                        style: TextStyle(
                          fontSize: 11,
                          color: colors.onSurfaceVariant,
                        ),
                      ),
                    ],
                  ],
                ),
                () => onReport('receivables'),
              ),
              const SizedBox(height: 8),
              pair(
                tile(
                  measure(
                    'أماكن تدريب متاحة',
                    (capacity['unknownGroups'] as num? ?? 0) > 0
                        ? 'غير مكتمل'
                        : '${capacity['seats']}',
                    ExperienceColors.information(context),
                    numeric: (capacity['unknownGroups'] as num? ?? 0) == 0,
                  ),
                  onCapacity,
                ),
                tile(
                  measure(
                    'عدد المجموعات',
                    '${members['groups'] ?? '—'}',
                    ExperienceColors.information(context),
                  ),
                  onGroups ?? onCapacity,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class HomeSubscriptions extends StatelessWidget {
  const HomeSubscriptions({
    super.key,
    required this.data,
    required this.onOpen,
    this.onBranch,
    this.onMonth,
  });
  final Json data;
  final ValueChanged<String> onOpen;
  final ValueChanged<String>? onBranch;
  final ValueChanged<Json>? onMonth;
  @override
  Widget build(BuildContext context) => GCard(
    child: Column(
      children: [
        Row(
          children: [
            Icon(
              Icons.layers_outlined,
              size: 20,
              color: ExperienceColors.information(context),
            ),
            const SizedBox(width: 8),
            const Expanded(
              child: UiText(
                'الاشتراكات',
                style: TextStyle(fontSize: 17, fontWeight: FontWeight.w800),
              ),
            ),
            TextButton(
              onPressed: () => onOpen(''),
              child: UiText(
                '${data['total']} إجمالي',
                style: const TextStyle(fontWeight: FontWeight.w800),
              ),
            ),
          ],
        ),
        const Divider(),
        Row(
          children: [
            for (final item in [
              ('active', 'فعالة', ExperienceColors.positive(context)),
              ('expiring', 'تنتهي قريبًا', ExperienceColors.warning(context)),
              ('expired', 'منتهية', ExperienceColors.negative(context)),
            ])
              Expanded(
                child: InkWell(
                  onTap: () => onOpen(item.$1),
                  child: Padding(
                    padding: const EdgeInsets.symmetric(vertical: 8),
                    child: Column(
                      children: [
                        UiText(
                          '${data[item.$1]}',
                          style: TextStyle(
                            fontSize: 24,
                            fontWeight: FontWeight.w800,
                            color: item.$3,
                          ),
                        ),
                        UiText(item.$2, style: const TextStyle(fontSize: 10)),
                      ],
                    ),
                  ),
                ),
              ),
          ],
        ),
        if (rows(data['branches'] ?? <Json>[]).isNotEmpty) ...[
          const Divider(height: 24),
          const Align(
            alignment: AlignmentDirectional.centerStart,
            child: UiText(
              'حسب الفرع · عدد الاشتراكات',
              style: TextStyle(fontSize: 12),
            ),
          ),
          const SizedBox(height: 12),
          for (final b in rows(data['branches']))
            Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: InkWell(
                onTap: onBranch == null
                    ? null
                    : () => onBranch!(textOf(b['id'])),
                child: Column(
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: UiText(
                            textOf(b['name']),
                            style: const TextStyle(fontSize: 12),
                          ),
                        ),
                        UiText(
                          '${b['count']}',
                          style: const TextStyle(
                            fontSize: 13,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 6),
                    LinearProgressIndicator(
                      value: (data['total'] as num? ?? 0) > 0
                          ? ((b['count'] as num) / (data['total'] as num))
                                .clamp(0, 1)
                                .toDouble()
                          : 0,
                      minHeight: 6,
                      semanticsLabel: '${b['name']} · ${b['count']} اشتراكًا',
                    ),
                  ],
                ),
              ),
            ),
        ],
        HomeTrend(
          items: rows(data['trend'] ?? <Json>[]),
          monthly: true,
          onOpen: onMonth,
        ),
      ],
    ),
  );
}

class HomeCollectionCards extends StatefulWidget {
  const HomeCollectionCards({
    super.key,
    required this.data,
    required this.currency,
    required this.referenceDay,
    this.receivables,
    this.onReceivables,
    this.periodOnly = false,
    required this.onOpen,
    this.onSource,
    this.onBranch,
  });
  final Json data;
  final Object? currency;
  final String referenceDay;
  final bool periodOnly;
  final num? receivables;
  final VoidCallback? onReceivables;
  final void Function(bool, String) onOpen;
  final void Function(bool, Json)? onSource;
  final void Function(bool, String)? onBranch;
  @override
  State<HomeCollectionCards> createState() => _HomeCollectionCardsState();
}

class _HomeCollectionCardsState extends State<HomeCollectionCards> {
  bool dailyChart = false;
  @override
  Widget build(BuildContext context) {
    final data = widget.data, currency = widget.currency;
    final sources = [
      ...rows(data[dailyChart ? 'todaySources' : 'monthSources'] ?? <Json>[]),
    ];
    final branches = [
      ...rows(data[dailyChart ? 'todayBranches' : 'monthBranches'] ?? <Json>[]),
    ];
    if (sources.isEmpty) {
      final template = rows(data['monthSources'] ?? <Json>[]);
      sources.addAll(
        template.isEmpty
            ? [
                {'label': 'التحصيل', 'amount': 0},
              ]
            : template.map((r) => {...r, 'amount': 0, '_empty': true}),
      );
    }
    if (branches.isEmpty) {
      final template = rows(data['monthBranches'] ?? <Json>[]);
      branches.addAll(
        template.isEmpty
            ? [
                {'name': 'الفروع', 'amount': 0},
              ]
            : template.map((r) => {...r, 'amount': 0, '_empty': true}),
      );
    }
    final sourceMax = sources.fold<num>(
      0,
      (v, s) => v + (s['amount'] as num).abs(),
    );
    final branchMax = branches.fold<num>(
      0,
      (v, s) => v + (s['amount'] as num).abs(),
    );
    Widget breakdown(Json row, num max, VoidCallback? open) => InkWell(
      onTap: row['_empty'] == true || row['amount'] == 0 ? null : open,
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 9),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            UiText(
              textOf(row['label'] ?? row['name']),
              style: const TextStyle(fontSize: 11, fontWeight: FontWeight.w600),
            ),
            const SizedBox(height: 4),
            Wrap(
              spacing: 5,
              runSpacing: 3,
              children: [
                MoneyText(
                  row['amount'],
                  currency,
                  size: 16,
                  color: (row['amount'] as num) < 0
                      ? ExperienceColors.negative(context)
                      : null,
                ),
                UiText(
                  '${max > 0 ? ((row['amount'] as num).abs() / max * 100).toStringAsFixed(0) : '0'}%',
                  style: TextStyle(
                    fontSize: 10,
                    color: Theme.of(context).colorScheme.onSurfaceVariant,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 6),
            LinearProgressIndicator(
              value: max > 0
                  ? ((row['amount'] as num).abs() / max).toDouble()
                  : 0,
              minHeight: 4,
              color: (row['amount'] as num) < 0
                  ? ExperienceColors.negative(context)
                  : academyAccent(context),
              backgroundColor: academyAccent(context).withValues(alpha: .09),
            ),
          ],
        ),
      ),
    );
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (!widget.periodOnly)
          ExperienceSection(
            'التحصيلات',
            Icons.account_balance_wallet_outlined,
            action: () => widget.onOpen(false, ''),
            actionLabel: 'السجل',
          ),
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  for (final daily in [
                    if (!widget.periodOnly) true,
                    false,
                  ]) ...[
                    Expanded(
                      child: Padding(
                        padding: const EdgeInsets.all(6),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            UiText(
                              widget.periodOnly
                                  ? 'صافي التحصيل خلال الفترة'
                                  : daily
                                  ? 'التحصيل اليومي'
                                  : 'التحصيل الشهري',
                              style: const TextStyle(
                                fontSize: 13,
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                            const SizedBox(height: 4),
                            InkWell(
                              onTap: () => widget.onOpen(daily, ''),
                              child: MoneyText(
                                data[daily ? 'today' : 'month'],
                                currency,
                                size: 24,
                              ),
                            ),
                            const Divider(height: 20),
                            for (final method in [
                              ('Cash', 'نقدي'),
                              ('Online', 'إلكتروني'),
                              if ((data['${daily ? 'today' : 'month'}Unspecified']
                                          as num? ??
                                      0) !=
                                  0)
                                ('Unspecified', 'طريقة صرف قديمة غير موثقة'),
                            ])
                              InkWell(
                                onTap: () => widget.onOpen(daily, method.$1),
                                child: Padding(
                                  padding: const EdgeInsets.symmetric(
                                    vertical: 6,
                                  ),
                                  child: Wrap(
                                    alignment: WrapAlignment.spaceBetween,
                                    spacing: 8,
                                    runSpacing: 4,
                                    children: [
                                      UiText(
                                        method.$2,
                                        style: const TextStyle(fontSize: 11),
                                      ),
                                      UiText(
                                        amountText(
                                          data['${daily ? 'today' : 'month'}${method.$1}'],
                                        ),
                                        style: const TextStyle(
                                          fontSize: 13,
                                          fontWeight: FontWeight.w600,
                                        ),
                                      ),
                                    ],
                                  ),
                                ),
                              ),
                          ],
                        ),
                      ),
                    ),
                    if (daily) const SizedBox(width: 12),
                  ],
                ],
              ),
              if (data['monthSources'] is List ||
                  data['todaySources'] is List) ...[
                const Divider(height: 24),
                if (!widget.periodOnly)
                  Wrap(
                    spacing: 8,
                    children: [
                      for (final daily in [true, false])
                        ChoiceChip(
                          selectedColor: academyAccent(context),
                          label: UiText(
                            daily ? 'اليوم' : 'الشهر',
                            style: TextStyle(
                              color: dailyChart == daily
                                  ? Theme.of(context).colorScheme.onPrimary
                                  : Theme.of(context).colorScheme.onSurface,
                            ),
                          ),
                          selected: dailyChart == daily,
                          onSelected: (_) => setState(() => dailyChart = daily),
                        ),
                    ],
                  ),
                const SizedBox(height: 12),
                Row(
                  key: const ValueKey('collection-breakdown-halves'),
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(
                      child: Padding(
                        padding: const EdgeInsetsDirectional.only(end: 10),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            const UiText(
                              'حسب الفرع',
                              style: TextStyle(
                                fontSize: 12,
                                fontWeight: FontWeight.w700,
                              ),
                            ),
                            for (final b in branches)
                              breakdown(
                                b,
                                branchMax,
                                b['id'] == null || widget.onBranch == null
                                    ? null
                                    : () => widget.onBranch!(
                                        dailyChart,
                                        textOf(b['id']),
                                      ),
                              ),
                          ],
                        ),
                      ),
                    ),
                    Expanded(
                      child: Container(
                        padding: const EdgeInsetsDirectional.only(start: 10),
                        decoration: BoxDecoration(
                          border: BorderDirectional(
                            start: BorderSide(
                              color: Theme.of(context).colorScheme.outline,
                            ),
                          ),
                        ),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            const UiText(
                              'مصادر التحصيل',
                              style: TextStyle(
                                fontSize: 12,
                                fontWeight: FontWeight.w700,
                              ),
                            ),
                            for (final source in sources)
                              breakdown(
                                source,
                                sourceMax,
                                widget.onSource == null
                                    ? null
                                    : () =>
                                          widget.onSource!(dailyChart, source),
                              ),
                          ],
                        ),
                      ),
                    ),
                  ],
                ),
              ],
            ],
          ),
        ),
        if ((widget.receivables ?? 0) > 0)
          Padding(
            padding: const EdgeInsets.only(top: 8),
            child: OutlinedButton.icon(
              onPressed: widget.onReceivables,
              icon: const Icon(Icons.pending_actions_rounded, size: 20),
              label: Padding(
                padding: const EdgeInsets.symmetric(vertical: 10),
                child: Wrap(
                  spacing: 12,
                  crossAxisAlignment: WrapCrossAlignment.center,
                  children: [
                    const UiText(
                      'مستحقات لم تُحصَّل',
                      style: TextStyle(
                        fontSize: 13,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    MoneyText(widget.receivables, currency, size: 18),
                    const Icon(Icons.chevron_right_rounded),
                  ],
                ),
              ),
            ),
          ),
      ],
    );
  }
}

class HomeOperatingStatement extends StatelessWidget {
  const HomeOperatingStatement({
    super.key,
    required this.data,
    required this.onOpen,
    this.onMonth,
    this.expenseAction,
    this.showTitle = true,
    this.showBalances = true,
    this.showReceivables = true,
    this.showHistoryLabel = false,
  });
  final Json data;
  final VoidCallback? onOpen;
  final bool showTitle, showBalances, showReceivables, showHistoryLabel;
  final ValueChanged<Json>? onMonth;
  final VoidCallback? Function(Json)? expenseAction;

  Widget _line(Json line, Color color, VoidCallback? action) {
    final title = UiText(
      textOf(line['label']),
      style: TextStyle(
        fontSize: action == null ? 10 : 12,
        fontWeight: action == null ? null : FontWeight.w600,
      ),
    );
    final tile = Padding(
      padding: const EdgeInsets.symmetric(vertical: 6),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (action == null)
            title
          else
            Row(
              children: [
                Expanded(child: title),
                const Icon(Icons.chevron_right_rounded, size: 16),
              ],
            ),
          MoneyText(line['amount'], data['currency'], size: 15, color: color),
        ],
      ),
    );
    return action == null
        ? tile
        : Semantics(
            button: true,
            child: InkWell(
              onTap: action,
              child: ConstrainedBox(
                constraints: const BoxConstraints(minHeight: 48),
                child: tile,
              ),
            ),
          );
  }

  @override
  Widget build(BuildContext context) {
    final surplus = data['surplus'] as num;
    final change = data['surplusChangePercentage'] as num?;
    final color = surplus < 0
        ? ExperienceColors.negative(context)
        : ExperienceColors.positive(context);
    final label = UiText(
      surplus < 0 ? 'العجز التشغيلي' : 'الفائض التشغيلي',
      style: TextStyle(fontSize: 14, fontWeight: FontWeight.w600, color: color),
    );
    final result = UiText(
      money(surplus, data['currency']),
      style: TextStyle(fontSize: 24, fontWeight: FontWeight.w600, color: color),
    );
    return GCard(
      onTap: onOpen,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (showTitle)
            const UiText(
              'تقرير الأرباح والخسائر',
              style: TextStyle(fontSize: 17, fontWeight: FontWeight.w500),
            ),
          const StatementBasis(),
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
            decoration: BoxDecoration(
              border: BorderDirectional(
                start: BorderSide(color: color, width: 4),
                end: BorderSide(color: color),
                top: BorderSide(color: color),
                bottom: BorderSide(color: color),
              ),
            ),
            child: MediaQuery.textScalerOf(context).scale(1) > 1.3
                ? Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [label, const SizedBox(height: 8), result],
                  )
                : Row(
                    children: [
                      Icon(Icons.balance_rounded, size: 20, color: color),
                      const SizedBox(width: 10),
                      Expanded(child: label),
                      Flexible(child: result),
                    ],
                  ),
          ),
          if (change != null)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: PeriodTrend(change: change),
            ),
          const SizedBox(height: 12),
          if (showHistoryLabel)
            const Padding(
              padding: EdgeInsets.only(bottom: 6),
              child: UiText(
                'آخر ٣ أشهر حتى نهاية الفترة',
                style: TextStyle(fontSize: 12, fontWeight: FontWeight.w700),
              ),
            ),
          OperatingMonthlyChart(
            showRange: false,
            items: rows(data['monthly'] ?? []),
            currency: textOf(data['currency']),
            onOpen: onMonth,
          ),
          const SizedBox(height: 14),
          Row(
            key: const ValueKey('operating-halves'),
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              for (final section in [
                (
                  'income',
                  'الإيرادات',
                  'totalIncome',
                  ExperienceColors.positive(context),
                ),
                (
                  'expenses',
                  'المصروفات',
                  'totalExpenses',
                  ExperienceColors.negative(context),
                ),
              ])
                Expanded(
                  child: Container(
                    padding: const EdgeInsets.all(10),
                    decoration: BoxDecoration(
                      color: section.$4.withValues(alpha: .035),
                      border: BorderDirectional(
                        start: BorderSide(
                          color: Theme.of(context).colorScheme.outline,
                        ),
                      ),
                    ),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        UiText(
                          section.$2,
                          style: TextStyle(
                            fontSize: 13,
                            fontWeight: FontWeight.w700,
                            color: section.$4,
                          ),
                        ),
                        const SizedBox(height: 6),
                        MoneyText(
                          data[section.$3],
                          data['currency'],
                          size: 19,
                          color: section.$4,
                        ),
                        const SizedBox(height: 8),
                        for (final line in rows(
                          data[section.$1],
                        ).where((line) => line['amount'] != 0))
                          _line(
                            line,
                            section.$4,
                            section.$1 == 'expenses'
                                ? expenseAction?.call(line)
                                : null,
                          ),
                      ],
                    ),
                  ),
                ),
            ],
          ),
          if (showBalances &&
              ((showReceivables && (data['receivables'] as num? ?? 0) != 0) ||
                  (data['approvedPayrollPayable'] as num? ?? 0) != 0)) ...[
            const Divider(height: 24),
            FinancialCategoryTiles(
              items: [
                if (showReceivables && (data['receivables'] as num? ?? 0) != 0)
                  {
                    'label': 'مستحقات لم تُحصَّل',
                    'amount': data['receivables'],
                  },
                if ((data['approvedPayrollPayable'] as num? ?? 0) != 0)
                  {
                    'label': 'رواتب مستحقة لم تُصرف',
                    'amount': data['approvedPayrollPayable'],
                  },
              ],
              currency: textOf(data['currency']),
              color: Theme.of(context).colorScheme.onSurface,
            ),
          ],
          if (![
            'income',
            'expenses',
          ].any((key) => rows(data[key]).any((line) => line['amount'] != 0)))
            const Padding(
              padding: EdgeInsets.only(top: 12),
              child: EmptyMessage(
                'لا توجد حركة قبض أو صرف مسجلة في هذه الفترة.',
              ),
            ),
          const SizedBox(height: 8),
          if (onOpen != null)
            const Align(
              alignment: AlignmentDirectional.centerEnd,
              child: Icon(Icons.arrow_forward_rounded, size: 18),
            ),
        ],
      ),
    );
  }
}

class HomeDecisionCards extends StatelessWidget {
  const HomeDecisionCards({
    super.key,
    required this.data,
    required this.onReport,
    required this.onCapacity,
    this.onReminders,
    this.onFollowUps,
    this.onGroup,
    this.onCoach,
    this.onPayments,
  });
  final Json data;
  final ValueChanged<String> onReport;
  final VoidCallback onCapacity;
  final ValueChanged<String>? onReminders;
  final VoidCallback? onFollowUps, onPayments;
  final ValueChanged<Json>? onGroup, onCoach;
  @override
  Widget build(BuildContext context) {
    final overdue = data['overdue'] is Json
            ? obj(data['overdue'])
            : const <String, dynamic>{},
        risk = obj(data['risk']),
        capacity = obj(data['capacity']);
    final daily = obj(data['todayPlan'] ?? <String, dynamic>{});
    final requests = obj(data['paymentRequests'] ?? <String, dynamic>{});
    final pending = [
      'newEnrollments',
      'renewals',
      'products',
      'tournaments',
    ].fold<num>(0, (sum, key) => sum + (requests[key] as num? ?? 0));
    final openFollowUps =
        daily['openFollowUps'] as num? ??
        data['followups']?['overdue'] as num? ??
        0;
    final absenceGroups = daily['recordedAbsenceGroups'] as num? ?? 0;
    final decisions = [
      if (pending > 0 && onPayments != null)
        (
          Icons.receipt_long_rounded,
          pending == 1
              ? 'طلب دفع بانتظار المراجعة'
              : '$pending طلبات دفع بانتظار المراجعة',
          'راجع الإثبات وأكمل الاشتراك',
          'مراجعة طلبات الدفع',
          onPayments!,
        ),
      if (absenceGroups > 0 && onFollowUps != null)
        (
          Icons.event_busy_rounded,
          '$absenceGroups مجموعات تحتاج متابعة الغياب',
          'تواصل مع الأسر لمعرفة السبب',
          'متابعة حالات الغياب',
          onFollowUps!,
        ),
      if (openFollowUps > 0 && onFollowUps != null)
        (
          Icons.task_alt_rounded,
          '$openFollowUps متابعات مفتوحة',
          'راجع المسؤول والخطوة التالية',
          'استكمال المتابعات',
          onFollowUps!,
        ),

      if (openFollowUps == 0 &&
          ((data['followups']?['overdue'] as num?) ?? 0) > 0 &&
          onFollowUps != null)
        (
          Icons.task_alt_rounded,
          '${data['followups']['overdue']} متابعات تجاوزت موعدها',
          'راجع المسؤول والموعد والإجراء التالي',
          'مراجعة المتابعات وإسنادها',
          onFollowUps!,
        ),
      if (overdue['amount'] case final num amount
          when amount.isFinite && amount > 0 && overdue['families'] is int)
        (
          Icons.receipt_long_outlined,
          '${money(amount, data['currency'])} مستحقات متأخرة',
          '${overdue['families']} ولي أمر يحتاجون متابعة',
          'تحضير تذكيرات التحصيل',
          () => onReminders == null
              ? onReport('receivables')
              : onReminders!('Overdue'),
        ),
      if ((risk['players'] as num? ?? 0) > 0)
        (
          Icons.favorite_outline,
          '${risk['players']} لاعب يحتاجون متابعة التجديد',
          'راجع الاشتراكات السابقة والأسر المرتبطة',
          'عرض خطة الاحتفاظ بهم',
          () => onReminders == null
              ? onReport('renewals')
              : onReminders!('Renewal'),
        ),
      if (rows(data['groupAttention'] ?? []).isNotEmpty && onGroup != null)
        (
          Icons.groups_2_outlined,
          textOf(rows(data['groupAttention']).first['name']),
          'مجموعة تحتاج مراجعة التغطية والحضور',
          'مراجعة المجموعة وفتح متابعة',
          () => onGroup!(rows(data['groupAttention']).first),
        ),
      if (rows(data['underAssignedCoaches'] ?? []).isNotEmpty &&
          onCoach != null)
        (
          Icons.sports_outlined,
          '${rows(data['underAssignedCoaches']).first['name']} · ${rows(data['underAssignedCoaches']).first['groupCount']} مجموعات من أصل ${rows(data['underAssignedCoaches']).first['target']}',
          'راجع التغطية والمواعيد قبل الإسناد',
          'إسناد مدرب لمجموعة',
          () => onCoach!(rows(data['underAssignedCoaches']).first),
        ),
      if ((capacity['seats'] as num? ?? 0) > 0)
        (
          Icons.event_seat_outlined,
          '${capacity['seats']} مكان تدريب متاح',
          'راجع المجموعات قبل استقبال تسجيلات جديدة',
          'عرض الأماكن المتاحة',
          onCapacity,
        ),
    ];
    if (decisions.isEmpty) {
      return const EmptyMessage('لا توجد قرارات عاجلة في هذا النطاق.');
    }
    return SwipeCardDeck(
      height:
          185 +
          (MediaQuery.textScalerOf(context).scale(12) - 12).clamp(0, 24) * 12,
      children: [
        for (final d in decisions)
          GCard(
            child: SingleChildScrollView(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Row(
                    children: [
                      Icon(d.$1, color: ExperienceColors.warning(context)),
                      const SizedBox(width: 10),
                      Expanded(
                        child: UiText(
                          d.$2,
                          style: const TextStyle(
                            fontWeight: FontWeight.w800,
                            fontSize: 14,
                          ),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 6),
                  UiText(
                    d.$3,
                    style: const TextStyle(fontSize: 11, height: 1.4),
                  ),
                  const SizedBox(height: 16),
                  Align(
                    alignment: Alignment.center,
                    child: TextButton.icon(
                      style: TextButton.styleFrom(
                        foregroundColor: academyInk(context),
                        backgroundColor: academyAccent(context)
                            .withValues(alpha: .09),
                        padding: const EdgeInsets.symmetric(
                          horizontal: 12,
                          vertical: 9,
                        ),
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(12),
                        ),
                      ),
                      onPressed: d.$5,
                      icon: const Icon(Icons.arrow_forward_rounded, size: 16),
                      label: UiText(
                        d.$4,
                        textAlign: TextAlign.center,
                        style: const TextStyle(
                          fontFamily: 'AcademyArabic',
                          fontSize: 11,
                          fontWeight: FontWeight.w700,
                          height: 1.5,
                        ),
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
      ],
    );
  }
}

class StatementBasis extends StatelessWidget {
  const StatementBasis({super.key});
  @override
  Widget build(BuildContext context) => Row(
    children: [
      const Expanded(
        child: UiText(
          'على أساس التحصيل والصرف',
          style: TextStyle(fontSize: 11),
        ),
      ),
      IconButton(
        tooltip: 'أساس التقرير',
        icon: const Icon(Icons.info_outline, size: 17),
        onPressed: () => showDialog<void>(
          context: context,
          builder: (c) => AlertDialog(
            title: const UiText('أساس التقرير'),
            content: const UiText(
              'يعرض المقبوضات والمدفوعات الفعلية بعد الاسترداد. المستحقات غير المحصلة والمصروفات والرواتب غير المسددة أرصدة منفصلة؛ الفائض النقدي ليس ربحًا نهائيًا على أساس الاستحقاق.',
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(c),
                child: const UiText('حسنًا'),
              ),
            ],
          ),
        ),
      ),
    ],
  );
}

class FinancialCategoryTiles extends StatelessWidget {
  const FinancialCategoryTiles({
    super.key,
    required this.items,
    required this.currency,
    required this.color,
  });
  final List<Json> items;
  final String currency;
  final Color color;
  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, constraints) {
      final visible = items.where((r) => r['amount'] != 0).toList();
      final columns =
          constraints.maxWidth >= 320 &&
              MediaQuery.sizeOf(context).width >= 400 &&
              MediaQuery.textScalerOf(context).scale(1) <= 1.3
          ? 3
          : 2;
      final width = (constraints.maxWidth - (columns - 1) * 6) / columns;
      return Wrap(
        spacing: 6,
        runSpacing: 6,
        children: [
          for (final line in visible)
            SizedBox(
              width: width,
              child: Container(
                padding: const EdgeInsets.all(10),
                decoration: BoxDecoration(
                  border: Border.all(color: Theme.of(context).dividerColor),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    UiText(
                      textOf(line['label']),
                      style: const TextStyle(fontSize: 12),
                    ),
                    const SizedBox(height: 4),
                    MoneyText(line['amount'], currency, size: 16, color: color),
                  ],
                ),
              ),
            ),
        ],
      );
    },
  );
}
