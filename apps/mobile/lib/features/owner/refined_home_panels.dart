import 'package:flutter/material.dart';

import '../../core/theme/ui_text.dart';
import '../guardian/guardian_ui.dart';
import '../shared/experience_ui.dart';

class AcademySummaryPanel extends StatelessWidget {
  const AcademySummaryPanel({
    super.key,
    required this.data,
    required this.onReport,
    required this.onCapacity,
    this.onTarget,
  });
  final Json data;
  final ValueChanged<String> onReport;
  final VoidCallback onCapacity;
  final VoidCallback? onTarget;
  @override
  Widget build(BuildContext context) {
    final c = obj(data['collections']),
        target = obj(data['target']),
        overdue = obj(data['overdue']),
        risk = obj(data['risk']),
        attendance = obj(data['attendance']),
        capacity = obj(data['capacity']);
    final change = c['changePercentage'] as num?,
        progress = target['achievement'] as num?;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        GCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  const Icon(
                    Icons.insights_rounded,
                    size: 20,
                    color: ExperienceColors.blue,
                  ),
                  const SizedBox(width: 8),
                  const Expanded(
                    child: UiText(
                      'ملخص الأكاديمية',
                      style: TextStyle(
                        fontWeight: FontWeight.w800,
                        fontSize: 18,
                      ),
                    ),
                  ),
                  if (onTarget != null)
                    IconButton(
                      tooltip: 'ضبط هدف التحصيل',
                      onPressed: onTarget,
                      icon: const Icon(Icons.flag_outlined, size: 20),
                    ),
                ],
              ),
              const SizedBox(height: 8),
              const UiText(
                'التحصيل خلال الفترة',
                style: TextStyle(fontSize: 12),
              ),
              InkWell(
                onTap: () => onReport('financial'),
                child: Padding(
                  padding: const EdgeInsets.symmetric(vertical: 8),
                  child: UiText(
                    money(c['net'], data['currency']),
                    style: const TextStyle(
                      fontSize: 34,
                      fontWeight: FontWeight.w900,
                      height: 1.5,
                    ),
                  ),
                ),
              ),
              if (change != null)
                Row(
                  children: [
                    Icon(
                      change < 0
                          ? Icons.trending_down_rounded
                          : Icons.trending_up_rounded,
                      size: 20,
                      color: change < 0
                          ? ExperienceColors.red
                          : ExperienceColors.green,
                    ),
                    const SizedBox(width: 6),
                    Flexible(
                      child: UiText(
                        '${change.abs()}% ${change < 0 ? 'أقل' : 'أعلى'} من الفترة السابقة',
                        style: TextStyle(
                          fontSize: 11,
                          color: change < 0
                              ? ExperienceColors.red
                              : ExperienceColors.green,
                        ),
                      ),
                    ),
                  ],
                ),
              if (target['amount'] != null && progress != null) ...[
                const Divider(height: 26),
                Row(
                  children: [
                    const Expanded(
                      child: UiText(
                        'هدف تحصيل الشهر',
                        style: TextStyle(fontSize: 11),
                      ),
                    ),
                    UiText(
                      '${progress.toStringAsFixed(1)}%',
                      style: const TextStyle(
                        color: ExperienceColors.green,
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 8),
                LinearProgressIndicator(
                  value: (progress / 100).clamp(0, 1).toDouble(),
                  minHeight: 6,
                  borderRadius: BorderRadius.circular(10),
                  color: ExperienceColors.green,
                ),
                const SizedBox(height: 6),
                UiText(
                  money(target['amount'], data['currency']),
                  style: const TextStyle(fontSize: 10),
                ),
              ],
            ],
          ),
        ),
        LayoutBuilder(
          builder: (context, bounds) => Wrap(
            spacing: 10,
            runSpacing: 0,
            children: [
              for (final k in [
                (
                  'مستحقات متأخرة',
                  money(overdue['amount'], data['currency']),
                  Icons.receipt_long_outlined,
                  ExperienceColors.red,
                  () => onReport('receivables'),
                ),
                (
                  'أعضاء يحتاجون متابعة',
                  '${risk['players']}',
                  Icons.favorite_outline_rounded,
                  ExperienceColors.amber,
                  () => onReport('renewals'),
                ),
                (
                  'أماكن تدريب متاحة',
                  (capacity['unknownGroups'] as num? ?? 0) > 0
                      ? 'غير مكتمل'
                      : '${capacity['seats']}',
                  Icons.event_seat_outlined,
                  ExperienceColors.blue,
                  onCapacity,
                ),
                (
                  'متوسط الحضور',
                  attendance['percentage'] == null
                      ? '—'
                      : '${attendance['percentage']}%',
                  Icons.task_alt_rounded,
                  ExperienceColors.green,
                  () => onReport('attendance'),
                ),
              ])
                SizedBox(
                  width: (bounds.maxWidth - 10) / 2,
                  child: GCard(
                    onTap: k.$5,
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Icon(k.$3, color: k.$4, size: 22),
                        const SizedBox(height: 10),
                        UiText(
                          k.$2,
                          maxLines: 2,
                          style: const TextStyle(
                            fontSize: 23,
                            fontWeight: FontWeight.w800,
                          ),
                        ),
                        const SizedBox(height: 4),
                        UiText(k.$1, style: const TextStyle(fontSize: 10)),
                      ],
                    ),
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
  });
  final Json data;
  final ValueChanged<String> onOpen;
  @override
  Widget build(BuildContext context) => GCard(
    child: Column(
      children: [
        Row(
          children: [
            const Icon(
              Icons.layers_outlined,
              size: 20,
              color: ExperienceColors.blue,
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
              ('active', 'فعالة', ExperienceColors.green),
              ('expiring', 'تنتهي قريبًا', ExperienceColors.amber),
              ('expired', 'منتهية', ExperienceColors.red),
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
      ],
    ),
  );
}

class HomeCollectionCards extends StatelessWidget {
  const HomeCollectionCards({
    super.key,
    required this.data,
    required this.currency,
    required this.referenceDay,
    required this.onOpen,
  });
  final Json data;
  final Object? currency;
  final String referenceDay;
  final void Function(bool, String) onOpen;
  @override
  Widget build(BuildContext context) => Row(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      for (final daily in [true, false]) ...[
        Expanded(
          child: GCard(
            onTap: () => onOpen(daily, ''),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                UiText(
                  daily ? 'التحصيل اليومي' : 'التحصيل الشهري',
                  style: const TextStyle(
                    fontSize: 12,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                UiText(
                  daily ? referenceDay : referenceDay.substring(0, 7),
                  style: const TextStyle(fontSize: 9),
                ),
                const SizedBox(height: 8),
                UiText(
                  money(data[daily ? 'today' : 'month'], currency),
                  style: const TextStyle(
                    fontSize: 19,
                    fontWeight: FontWeight.w800,
                  ),
                ),
                const Divider(height: 18),
                for (final method in [
                  ('Cash', 'نقدي', 'Cash'),
                  ('Online', 'إلكتروني', 'Online'),
                ])
                  InkWell(
                    onTap: () => onOpen(daily, method.$1),
                    child: Padding(
                      padding: const EdgeInsets.symmetric(vertical: 6),
                      child: Row(
                        children: [
                          UiText(
                            method.$2,
                            style: const TextStyle(fontSize: 9),
                          ),
                          const Spacer(),
                          UiText(
                            amountText(
                              data['${daily ? 'today' : 'month'}${method.$3}'],
                            ),
                            style: const TextStyle(
                              fontSize: 12,
                              color: ExperienceColors.green,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                if (data['${daily ? 'today' : 'month'}Unspecified'] is num &&
                    data['${daily ? 'today' : 'month'}Unspecified'] != 0)
                  TextButton(
                    onPressed: () => onOpen(daily, 'Unspecified'),
                    child: UiText(
                      'طريقة صرف قديمة غير موثقة\n${money(data['${daily ? 'today' : 'month'}Unspecified'], currency)}',
                      style: const TextStyle(
                        fontSize: 9,
                        color: ExperienceColors.amber,
                      ),
                    ),
                  ),
              ],
            ),
          ),
        ),
        if (daily) const SizedBox(width: 10),
      ],
    ],
  );
}

class HomeOperatingStatement extends StatelessWidget {
  const HomeOperatingStatement({
    super.key,
    required this.data,
    required this.onOpen,
  });
  final Json data;
  final VoidCallback onOpen;
  @override
  Widget build(BuildContext context) {
    final surplus = data['surplus'] as num;
    return GCard(
      onTap: onOpen,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const UiText(
            'تقرير الأرباح والخسائر',
            style: TextStyle(fontSize: 17, fontWeight: FontWeight.w800),
          ),
          const SizedBox(height: 8),
          Row(
            children: [
              Expanded(
                child: UiText(
                  surplus < 0 ? 'العجز التشغيلي' : 'الفائض التشغيلي',
                  style: const TextStyle(fontSize: 11),
                ),
              ),
              Flexible(
                child: UiText(
                  money(surplus, data['currency']),
                  style: TextStyle(
                    fontSize: 22,
                    fontWeight: FontWeight.w900,
                    color: surplus < 0
                        ? ExperienceColors.red
                        : ExperienceColors.green,
                  ),
                ),
              ),
            ],
          ),
          const UiText('على أساس نقدي', style: TextStyle(fontSize: 9)),
          for (final section in [
            ('income', 'الإيرادات', 'totalIncome', ExperienceColors.green),
            ('expenses', 'المصروفات', 'totalExpenses', ExperienceColors.red),
          ]) ...[
            const Divider(height: 24),
            UiText(
              section.$2,
              style: TextStyle(
                fontSize: 13,
                fontWeight: FontWeight.w800,
                color: section.$4,
              ),
            ),
            const SizedBox(height: 6),
            for (final line in rows(
              data[section.$1],
            ).where((r) => r['amount'] != 0))
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 5),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(
                      child: UiText(
                        textOf(line['label']),
                        style: const TextStyle(fontSize: 11),
                      ),
                    ),
                    const SizedBox(width: 8),
                    UiText(
                      money(line['amount'], data['currency']),
                      style: const TextStyle(
                        fontSize: 12,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                  ],
                ),
              ),
            Row(
              children: [
                const Expanded(
                  child: UiText(
                    'الإجمالي',
                    style: TextStyle(fontSize: 12, fontWeight: FontWeight.w800),
                  ),
                ),
                UiText(
                  money(data[section.$3], data['currency']),
                  style: TextStyle(
                    fontWeight: FontWeight.w800,
                    color: section.$4,
                  ),
                ),
              ],
            ),
          ],
          const SizedBox(height: 8),
          const Align(
            alignment: AlignmentDirectional.centerEnd,
            child: Icon(Icons.arrow_back_rounded, size: 18),
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
  });
  final Json data;
  final ValueChanged<String> onReport;
  final VoidCallback onCapacity;
  final ValueChanged<String>? onReminders;
  final VoidCallback? onFollowUps;
  final ValueChanged<Json>? onGroup;
  @override
  Widget build(BuildContext context) {
    final overdue = obj(data['overdue']),
        risk = obj(data['risk']),
        capacity = obj(data['capacity']);
    final decisions = [
      if (((data['followups']?['overdue'] as num?) ?? 0) > 0 &&
          onFollowUps != null)
        (
          Icons.task_alt_rounded,
          '${data['followups']['overdue']} متابعات تجاوزت موعدها',
          'راجع المسؤول والموعد والإجراء التالي',
          'مراجعة المتابعات وإسنادها',
          onFollowUps!,
        ),
      if ((overdue['amount'] as num? ?? 0) > 0)
        (
          Icons.receipt_long_outlined,
          '${money(overdue['amount'], data['currency'])} مستحقات متأخرة',
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
      if ((capacity['seats'] as num? ?? 0) > 0)
        (
          Icons.event_seat_outlined,
          '${capacity['seats']} مكان تدريب متاح',
          'راجع المجموعات قبل استقبال تسجيلات جديدة',
          'عرض الأماكن المتاحة',
          onCapacity,
        ),
    ];
    return Column(
      children: [
        if (decisions.isEmpty)
          const EmptyMessage('لا توجد قرارات عاجلة في هذا النطاق.'),
        for (final d in decisions.take(3))
          GCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Row(
                  children: [
                    Icon(d.$1, color: ExperienceColors.amber),
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
                UiText(d.$3, style: const TextStyle(fontSize: 11)),
                Align(
                  alignment: AlignmentDirectional.centerStart,
                  child: TextButton.icon(
                    onPressed: d.$5,
                    icon: const Icon(Icons.arrow_back_rounded, size: 16),
                    label: UiText(d.$4, style: const TextStyle(fontSize: 11)),
                  ),
                ),
              ],
            ),
          ),
      ],
    );
  }
}
